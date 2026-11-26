// Renode's STM32H7_SPI with refusal guards (TVI-WO-0100, 0100a, 0100b). The rules, their truth tables
// and every page cited here: docs/work-orders/TVI-WO-0100a.rm0433.md and TVI-WO-0100b.rm0433.md. All
// citations are to ST RM0433 Rev 9, chapter 50.
//
// Insertion point (RUL-07): this subclass re-implements the bus access interfaces, so every bus access
// reaches the guard first; an access the guard passes goes to the stock model unchanged. The stock
// methods are `virtual final`. The stock model is not modified.
//
// The allow-list (RUL-17): `Fields` below is every field of every SPI register, keyed by RM0433's
// register and field names (RUL-16), with the values the model implements. A write that gives any
// field another value refuses (TVI-SPI-FIELD, kind unmodeled). A write to an offset with no register
// refuses the same way.
//
// Copies, served on read (RUL-17; ruling C2): the stock model drops tags and reads CFG1 as 0 after reset
// and CFG2.MASTER as 1 (measured, TVI-WO-0100b.rm0433.md §1.3), so the guard keeps whole copies of CR1,
// CFG1, CFG2, IER (OVRIE is an acknowledged tag) and CRCPOLY as last passed, at RM0433's reset values,
// and serves those reads from them.
// CR1's SPE, CSTART and CSUSP are read from the stock model, which drives them. CR1.IOLOCK is read
// from the guard's latch. A CR1 read while the latch is set refuses: the clear on SPE 1->0 (p2215) is
// not modeled. A CFG2 read refuses while MASTER may have been cleared by a mode fault.
//
// Rules, in the order they are evaluated. A refused access is not passed to the model. Recorded mode (RUL-19) is
// model/Refusal.cs's contract: every rule is evaluated and records, and the access goes on. TVI-LOAD always refuses.
//   TVI-LOAD          (Records) the guard failed to load (a mode other than the two): every access refuses.
//   TVI-SPI-ENABLE    (TVI-WO-1300a (1)) any access while the RCC guard's copy of this SPI's bus-clock enable is 0: silicon
//                     ignores the write and returns invalid data for the read (p373). predicted-fault. With no RCC guard at
//                     "sysbus.rcc", or none that covers this instance's name, unmodeled. The RCC guard keeps this SPI unlocked
//                     on the bus, so the access reaches this rule.
//   TVI-SPI-WIDTH     any access other than an aligned 32-bit one, except at TXDR and RXDR, where the
//                     access must be as wide as the frame (whole bytes of DSIZE+1 bits).
//   TVI-SPI-ORDER     a write the manual orders against SPE, where it does not say what silicon does:
//                     TXDR with SPE=0 (the FIFOs are flushed at SPE=0, p2217; the stock model queues the
//                     data and sends it at the enable), CR2 with SPE=1 (p2218), CSTART or CSUSP with
//                     SPE=0 (p2216).
//   TVI-SPI-READ      a read whose value the twin cannot give: an offset with no register (Table 429,
//                     p2233); a write-only register (TXDR p2227, IFCR p2226); RXDR with the RxFIFO empty
//                     (SR.RXP=0; p2228 does not say what it reads); SR while EOT is the stock model's,
//                     set by CSUSP (silicon sets only SUSP at a suspend, p2225, and EOT only when TSIZE
//                     frames are done, p2226). A refused read is not passed to the model.
//   TVI-SPI-IOLOCK    a CFG2 write while the IOLOCK latch is set. The latch is taken only from a CR1
//                     write made with SPE=0, as silicon accepts it (p2217). predicted-fault while the
//                     latch is certain, otherwise unmodeled (ruling C7).
//   TVI-SPI-CFG2-SPE  a CFG2 write that changes it while the last CR1 passed had SPE=1 (write-protected, p2221); the value
//                     CFG2 already holds changes nothing and passes (TVI-WO-1300a (4); RUL-18 as amended).
//   TVI-SPI-CFG1-SPE  a CFG1 write while SPE=1 that changes it (ruling C3).
//   TVI-SPI-I2SMOD    an I2SCFGR write with I2SMOD=1 (I2S/PCM mode is not modeled).
//   TVI-SPI-FIELD     the allow-list above; and at a CR1 write with SPE=1, CFG1.DSIZE must be 00111.
//   TVI-SPI-MODF      a CR1 write with SPE=1 passes only if the copies show master, Motorola mode
//                     (SP=000), and an inactive internal SS: SSM=0 with SSOE=1 (p2221), or SSM=1,
//                     SSIOP=0 and SSI=1; and no master state other than that since reset (the D1
//                     history), and MASTER known. IFCR.MODFC clears the history (p2227), but MASTER is
//                     then unknown until CFG2 is written again (ruling C6). Anything else refuses, by
//                     the first failing branch.
//   TVI-SPI-KERNEL    (TVI-WO-1300a (2)) a start (SPE rising, or CSTART rising with SPE) with no kernel clock, by the RCC
//                     guard's query: predicted-fault, naming the selector, its source and the pages (p371-372, p414; p2159).
//                     unmodeled when the query has no rule for this instance. The RCC guard is found by its name,
//                     "sysbus.rcc" (RUL-09 (b)), and this instance's own name keys the query (spi1; spi2 on the consumer's
//                     platform), at each use: never a static, a global or a stale reference.
// Every refusal carries a kind (RUL-18): predicted-fault only where RM0433 says by page that silicon
// faults or ignores the write; otherwise unmodeled.

using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_SPI : STM32H7_SPI, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IDisposable
    {
        // `mode` comes from the platform description (RUL-19): "enforcing" or "recorded".
        public GuardedSTM32H7_SPI(IMachine machine, string mode = Records.Enforcing) : base(machine)
        {
            records = new Records(machine, this, nameof(GuardedSTM32H7_SPI), mode);
            host = machine;
        }

        // Whether this SPI is enabled (the last CR1 passed has SPE = 1): the RCC guard asks it at every RCC write, for
        // TVI-RCC-CLOCK (TVI-WO-1300a (3)).
        public bool Started => (cr1 & Cr1Spe) != 0;

        // The base constructor calls this before `records` exists (measured): touch only the copies.
        public override void Reset()
        {
            base.Reset();
            cr1 = 0;
            cfg1 = Cfg1Reset;
            cfg2 = 0;
            ier = 0;
            ioLock = false;
            ioLockCertain = false;
            history = null;
            masterUnknown = false;
            eotBySuspend = false;
        }

        public void Dispose()
        {
            records.End();
        }

        public new void WriteDoubleWord(long offset, uint value)
        {
            var fired = RefusedAccess(offset, value, 32, "write");
            fired |= records.Evaluates(fired) && RefusedOrder(offset, value, 32);
            fired |= records.Evaluates(fired) && RefusedWrite(offset, value);
            if(fired && !records.Recorded)
            {
                return;
            }
            switch(offset)
            {
            case Cr1Offset:
                var wasEnabled = (cr1 & Cr1Spe) != 0;
                if((value & Cr1IoLock) != 0 && !wasEnabled && !ioLock)
                {
                    // Set only from SPE=0 (p2215, p2217). Certain only if this write leaves SPE=0: whether the
                    // write that enables counts is not settled. A doubt about MODF at this point lasts until
                    // reset while latched (CFG2, which would clear it, is refused), so the refusal checks it.
                    ioLock = true;
                    ioLockCertain = (value & Cr1Spe) == 0;
                }
                if(wasEnabled && (value & Cr1Spe) == 0)
                {
                    ioLockCertain = false; // silicon clears IOLOCK on SPE 1->0 (p2215); not modeled
                }
                // The stock model clears EOT when SPE falls or CSTART rises, and sets it at CSUSP when it was
                // clear (STM32H7_SPI.cs L100-123): remember when EOT is only the suspend's.
                var stockSr = base.ReadDoubleWord(SrOffset);
                var cstartRises = (value & Cr1Cstart) != 0 && (base.ReadDoubleWord(Cr1Offset) & Cr1Cstart) == 0;
                if((value & Cr1Spe) == 0 || cstartRises)
                {
                    eotBySuspend = false;
                }
                if((value & Cr1Csusp) != 0 && ((stockSr & SrEot) == 0 || cstartRises))
                {
                    eotBySuspend = true;
                }
                cr1 = value;
                NoteMasterState();
                break;
            case Cfg1Offset:
                cfg1 = value;
                break;
            case Cfg2Offset:
                cfg2 = value;
                masterUnknown = false;
                NoteMasterState();
                break;
            case IerOffset:
                ier = value;
                break;
            case IfcrOffset:
                if((value & IfcrEotc) != 0)
                {
                    eotBySuspend = false;
                }
                if((value & IfcrModfc) != 0 && history != null)
                {
                    history = null;
                    masterUnknown = true;
                }
                break;
            }
            base.WriteDoubleWord(offset, value);
        }

        public new void WriteWord(long offset, ushort value)
        {
            var fired = RefusedAccess(offset, value, 16, "write");
            fired |= records.Evaluates(fired) && RefusedOrder(offset, value, 16);
            if(!fired || records.Recorded)
            {
                base.WriteWord(offset, value);
            }
        }

        public new void WriteByte(long offset, byte value)
        {
            var fired = RefusedAccess(offset, value, 8, "write");
            fired |= records.Evaluates(fired) && RefusedOrder(offset, value, 8);
            if(!fired || records.Recorded)
            {
                base.WriteByte(offset, value);
            }
        }

        public new uint ReadDoubleWord(long offset)
        {
            var fired = RefusedAccess(offset, 0, 32, "read");
            fired |= records.Evaluates(fired) && RefusedRead(offset, 32);
            if(fired && !records.Recorded)
            {
                return 0;
            }
            var value = base.ReadDoubleWord(offset);
            switch(offset)
            {
            case Cr1Offset:
                if(ioLock && records.Evaluates(fired))
                {
                    records.Refuse("TVI-SPI-IOLOCK", Unmodeled, "SPI_CR1", "CR1.IOLOCK (bit 16), read",
                        "CR1 read while the guard's IOLOCK latch is set: silicon reads IOLOCK=1 until SPE goes from 1 to 0, and the guard does not model the clear, so the value cannot be given",
                        "RM0433 Rev 9 §50.11.1 p2215 (CR1.IOLOCK)", State(), offset, value, 32);
                    if(!records.Recorded)
                    {
                        return value;
                    }
                }
                return (value & Cr1Stock) | (cr1 & ~Cr1Stock & ~Cr1IoLock);
            case Cfg1Offset:
                return cfg1;
            case Cfg2Offset:
                var doubt = history != null ? "a master state other than the passing one, since reset: " + history
                    : masterUnknown ? "IFCR.MODFC written after such a state, and CFG2 not written since" : null;
                if(doubt != null && records.Evaluates(fired))
                {
                    records.Refuse(Modf, Unmodeled, "SPI_CFG2", "CFG2 read after " + doubt,
                        "CFG2 read after the SPI was configured as a master in a state where a mode fault was possible: if silicon raised MODF there it cleared CFG2.MASTER, which the guard's copy still holds, and IFCR.MODFC clears MODF but does not set MASTER again. RM0433 does not settle whether MODF is evaluated while SPE=0, so the value cannot be given",
                        "RM0433 Rev 9 §50.5.2 p2184; §50.11.1 p2217; §50.4.9 p2171; §50.11.7 p2227 (MODFC)", State(), offset, value, 32);
                    if(!records.Recorded)
                    {
                        return value;
                    }
                }
                return cfg2;
            case IerOffset:
                return ier;
            case CrcPolyOffset:
                return CrcPolyReset; // the only value the allow-list lets CRCPOLY hold
            }
            return value;
        }

        public new ushort ReadWord(long offset)
        {
            var fired = RefusedAccess(offset, 0, 16, "read");
            fired |= records.Evaluates(fired) && RefusedRead(offset, 16);
            return fired && !records.Recorded ? (ushort)0 : base.ReadWord(offset);
        }

        public new byte ReadByte(long offset)
        {
            var fired = RefusedAccess(offset, 0, 8, "read");
            fired |= records.Evaluates(fired) && RefusedRead(offset, 8);
            return fired && !records.Recorded ? (byte)0 : base.ReadByte(offset);
        }

        // TVI-SPI-READ, after TVI-SPI-WIDTH and before the model is read: true if the read was refused.
        private bool RefusedRead(long offset, int width)
        {
            string bit = null, condition = null, citation = null;
            if(!Fields.Any(f => f.Offset == offset))
            {
                bit = "no register at offset 0x" + offset.ToString("X");
                condition = "read of an SPI offset where RM0433 defines no register: the twin cannot say what silicon returns";
                citation = "RM0433 Rev 9 §50.12 Table 429 p2233";
            }
            else if(offset == TxdrOffset || offset == IfcrOffset)
            {
                bit = "write-only register";
                condition = "read of a write-only register: RM0433 gives its fields as write-only and does not say what a read returns; the stock model returns what it stored";
                citation = offset == TxdrOffset ? "RM0433 Rev 9 §50.11.8 p2227 (TXDR)" : "RM0433 Rev 9 §50.11.7 p2226 (IFCR)";
            }
            else if(offset == RxdrOffset && (base.ReadDoubleWord(SrOffset) & SrRxp) == 0)
            {
                bit = "SR.RXP (bit 0) = 0, RXDR read";
                condition = "RXDR read with the RxFIFO empty: the manual does not say what it returns while the SPI is enabled, and the stock model returns 0";
                citation = "RM0433 Rev 9 §50.11.9 p2228 (RXDR); §50.11.6 p2226 (RXP)";
            }
            else if(offset == SrOffset && eotBySuspend)
            {
                bit = "SR.EOT (bit 3) set by the stock model at CSUSP";
                condition = "SR read after CR1.CSUSP: silicon sets SUSP at a suspend and EOT only when TSIZE frames are done, while the stock model sets EOT too, so the value cannot be given";
                citation = "RM0433 Rev 9 §50.11.1 p2216 (CSUSP); §50.11.6 p2225 (SUSP), p2226 (EOT)";
            }
            if(bit == null)
            {
                return false;
            }
            records.Refuse("TVI-SPI-READ", Unmodeled, RegisterAt(offset), bit, condition, citation, State(), offset, 0, width);
            return true;
        }

        // TVI-LOAD, then TVI-SPI-ENABLE, then TVI-SPI-WIDTH: true if the access was refused.
        private bool RefusedAccess(long offset, ulong value, int width, string access)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset), "none",
                    "access to a guarded peripheral that failed to load", Records.LoadCitation,
                    State(), offset, value, width);
                return true;
            }
            var fired = RefusedEnable(offset, value, width, access);
            if(!records.Evaluates(fired))
            {
                return true;
            }
            if(offset == TxdrOffset || offset == RxdrOffset)
            {
                var frame = FrameBits();
                if(width != frame)
                {
                    records.Refuse("TVI-SPI-WIDTH", Unmodeled, RegisterAt(offset), width + "-bit " + access + ", frame " + frame + " bits",
                        width + "-bit " + access + " of a data register with " + frame + "-bit frames: silicon forbids an access narrower than the frame and packs several frames into a wider one, while the stock model takes one access as one frame (measured)",
                        "RM0433 Rev 9 §50.11.8 p2227 (TXDR); §50.11.9 p2228 (RXDR)", State(), offset, value, width);
                    return true;
                }
                return fired;
            }
            if(width != 32 || offset % 4 != 0)
            {
                records.Refuse("TVI-SPI-WIDTH", Unmodeled, RegisterAt(offset), "sub-word or unaligned " + access,
                    width + "-bit " + access + " at offset 0x" + offset.ToString("X") + ": the guards evaluate aligned 32-bit accesses only (a twin limitation; RM0433 Rev 9 §50.11 states no access width for these registers)",
                    "none: a limitation of the guard, not a behaviour of the manual", State(), offset, value, width);
                return true;
            }
            return fired;
        }

        // TVI-SPI-ORDER, after TVI-SPI-WIDTH: true if the write was refused.
        private bool RefusedOrder(long offset, ulong value, int width)
        {
            var enabled = (cr1 & Cr1Spe) != 0;
            string bit = null, condition = null, citation = null;
            if(offset == TxdrOffset && !enabled)
            {
                bit = "CR1.SPE (bit 0) = 0, TXDR written";
                condition = "TXDR written while the SPI is disabled: at SPE=0 \"all the FIFOs content is flushed\", while the stock model queues the data and sends it when SPE is set; the manual does not say what silicon does with the write";
                citation = "RM0433 Rev 9 §50.11.1 p2217 (SPE); §50.11.8 p2227 (TXDR)";
            }
            else if(offset == Cr2Offset && enabled)
            {
                bit = "CR1.SPE (bit 0) = 1, CR2 written";
                condition = "CR2 written while the SPI is enabled: \"When these bits are changed by software, the SPI must be disabled\", and the manual does not say what silicon does otherwise; the stock model takes the write";
                citation = "RM0433 Rev 9 §50.11.2 p2218 (TSIZE)";
            }
            else if(offset == Cr1Offset && !enabled && (value & Cr1Csusp) != 0)
            {
                bit = "CR1.CSUSP (bit 10) = 1 with SPE = 0";
                condition = "CSUSP set while the SPI was disabled: the manual describes the suspend only for an active master, and the stock model sets SUSP and ends the transfer";
                citation = "RM0433 Rev 9 §50.11.1 p2216 (CSUSP), p2217 (SPE)";
            }
            else if(offset == Cr1Offset && !enabled && (value & Cr1Cstart) != 0)
            {
                bit = "CR1.CSTART (bit 9) = 1 with SPE = 0";
                condition = "CSTART set while the SPI was disabled: the manual describes CSTART only for an enabled master, and the stock model sets it and starts the transfer at the enable";
                citation = "RM0433 Rev 9 §50.11.1 p2216 (CSTART), p2217 (SPE)";
            }
            if(bit == null)
            {
                return false;
            }
            records.Refuse("TVI-SPI-ORDER", Unmodeled, RegisterAt(offset), bit, condition, citation, State(), offset, value, width);
            return true;
        }

        // The write rules after TVI-SPI-ORDER, in order: true if the write was refused.
        private bool RefusedWrite(long offset, uint value)
        {
            switch(offset)
            {
            case Cfg2Offset:
                if(ioLock)
                {
                    // Certain only if it was certain when taken, SPE has not gone from 1 to 0 since, and no
                    // mode fault is possible now (an active MODF clears IOLOCK, p2225): ruling C7.
                    var certain = ioLockCertain && history == null && !masterUnknown;
                    records.Refuse("TVI-SPI-IOLOCK", certain ? PredictedFault : Unmodeled, "SPI_CFG2", "CR1.IOLOCK (bit 16) = 1",
                        certain
                            ? "CFG2 written after IOLOCK was set: silicon keeps the old CFG2 (\"When this bit is set, the SPI_CFG2 register content cannot be modified\"), while the stock model drops IOLOCK and would take the write"
                            : "CFG2 written after IOLOCK was written, where the twin cannot say whether silicon holds the lock: the write that set it also enabled the SPI, or a mode fault was possible (an active MODF blocks IOLOCK), or SPE has since gone from 1 to 0, which clears IOLOCK on silicon and is not modeled",
                        "RM0433 Rev 9 §50.11.1 p2215 (CR1.IOLOCK), p2217 (SPE); §50.11.4 p2221 (CFG2 write protection); §50.11.6 p2225 (MODF)",
                        State(), offset, value, 32);
                    return true;
                }
                if((cr1 & Cr1Spe) != 0 && value != cfg2)
                {
                    records.Refuse("TVI-SPI-CFG2-SPE", PredictedFault, "SPI_CFG2", "CR1.SPE (bit 0) = 1",
                        "CFG2 written while the SPI is enabled: silicon write-protects CFG2 when SPE=1 and keeps the old value; the guard's copy would take it",
                        "RM0433 Rev 9 §50.11.1 p2217 (SPE); §50.11.4 p2221 (CFG2 write protection)",
                        State(), offset, value, 32);
                    return true;
                }
                break;
            case Cfg1Offset:
                if((cr1 & Cr1Spe) != 0 && value != cfg1)
                {
                    var protectedChange = ((value ^ cfg1) & ~Cfg1DmaEnables) != 0;
                    records.Refuse("TVI-SPI-CFG1-SPE", protectedChange ? PredictedFault : Unmodeled, "SPI_CFG1", "CR1.SPE (bit 0) = 1",
                        protectedChange
                            ? "CFG1 changed while the SPI is enabled: silicon write-protects CFG1 when SPE=1, except TXDMAEN and RXDMAEN, and keeps the old value of every other field"
                            : "CFG1's TXDMAEN or RXDMAEN changed while the SPI is enabled: silicon takes them, but the stock model drops the whole CFG1 write while SPE=1 (STM32H7_SPI.cs L329)",
                        "RM0433 Rev 9 §50.11.1 p2217 (SPE); §50.11.3 p2218 (CFG1 write protection)",
                        State(), offset, value, 32);
                    return true;
                }
                break;
            case I2sCfgrOffset:
                if((value & I2sMod) != 0)
                {
                    records.Refuse("TVI-SPI-I2SMOD", Unmodeled, "SPI_I2SCFGR", "I2SCFGR.I2SMOD (bit 0) = 1",
                        "I2S/PCM mode selected: the guard models SPI mode only, where I2SMOD must be kept at 0",
                        "RM0433 Rev 9 §50.11.14 p2230 (\"kept at 0 in SPI mode\"), p2232 (I2SMOD, bit 0); §50.4.9 p2171 step 2(f)",
                        State(), offset, value, 32);
                    return true;
                }
                break;
            }
            if(RefusedField(offset, value))
            {
                return true;
            }
            if(offset != Cr1Offset || (value & Cr1Spe) == 0)
            {
                return false;
            }
            if(!EnablePasses(value))
            {
                return true;
            }
            // A start: SPE rising, or CSTART rising with SPE (TVI-WO-1300a (2); CSTART with SPE = 0 is TVI-SPI-ORDER's).
            var starts = (cr1 & Cr1Spe) == 0 || ((value & Cr1Cstart) != 0 && (base.ReadDoubleWord(Cr1Offset) & Cr1Cstart) == 0);
            return starts && RefusedKernel(value);
        }

        // TVI-SPI-ENABLE (TVI-WO-1300a (1)): any access while the RCC guard's copy of this SPI's bus-clock enable is 0.
        // Silicon ignores the write and returns invalid data for the read (p373). The RCC guard leaves its guarded
        // peripherals unlocked on the bus, so this guard sees the access and refuses it by name.
        private bool RefusedEnable(long offset, ulong value, int width, string access)
        {
            var rcc = Rcc(out var name);
            string kind, bit, condition, citation;
            if(rcc == null)
            {
                kind = Unmodeled;
                bit = "none";
                condition = access + " of " + (name ?? "an SPI") + " with " + NoRcc + ": the twin cannot say whether its bus clock is enabled, nor watch its kernel clock";
                citation = "TVI-WO-1300a (1), (2): the SPI guard asks the RCC guard, found by name (RUL-09 (b))";
            }
            else if(!rcc.TryGetEnable(name, out var enabled, out var enableName, out var enablePage))
            {
                kind = Unmodeled;
                bit = "none";
                condition = access + " of " + name + ", for which the RCC guard has no enable rule: the twin cannot say whether its bus clock is enabled";
                citation = "TVI-WO-1300 (4): the RCC guard's query covers spi1, spi2, i2c1, i2c2, usart1, usart2, usart3 and rng";
            }
            else if(enabled)
            {
                return false;
            }
            else
            {
                kind = PredictedFault;
                bit = enableName + " = 0";
                condition = width + "-bit " + access + " of " + name + " while its bus clock is not enabled: silicon " + (access == "write" ? "ignores the write" : "returns invalid data")
                    + " (\"When the bus interface clock is not active, read or write accesses to the peripheral registers are not supported\")";
                citation = "RM0433 Rev 9 §8.5.10 p373 (an access with the bus interface clock off); " + enablePage + " (" + enableName + ")";
            }
            records.Refuse("TVI-SPI-ENABLE", kind, RegisterAt(offset), bit, condition, citation, State(), offset, value, width);
            return true;
        }

        // TVI-SPI-KERNEL (TVI-WO-1300a (2); RUL-18): a start with no kernel clock, by the RCC guard's query, recorded here, at
        // this SPI's own start (the architect, 2026-11-23).
        private bool RefusedKernel(uint newCr1)
        {
            var rcc = Rcc(out var name);
            var k = rcc?.QueryKernelClock(name);
            if(k != null && k.Modeled && k.Running)
            {
                return false;
            }
            var start = (cr1 & Cr1Spe) == 0 ? "CR1.SPE" : "CR1.CSTART";
            if(k == null || !k.Modeled)
            {
                records.Refuse("TVI-SPI-KERNEL", Unmodeled, "SPI_CR1", start + " = 1",
                    (name ?? "an SPI") + " started with no kernel-clock rule: " + (k == null ? NoRcc : k.Condition),
                    k == null ? "TVI-WO-1300a (2): the SPI guard asks the RCC guard, found by name (RUL-09 (b))" : k.Citation,
                    State(), Cr1Offset, newCr1, 32);
                return true;
            }
            records.Refuse("TVI-SPI-KERNEL", PredictedFault, "SPI_CR1", k.Selector,
                name + " started (" + start + ") with no kernel clock: " + k.Condition + "; the master \"needs the spi_ker_ck kernel clock coming from RCC active during communication\"",
                k.Citation + "; §50.4.1 p2159 (spi_ker_ck)", State(), Cr1Offset, newCr1, 32);
            return true;
        }

        // The RCC guard, by its name in the platform, and this instance's own name, both looked up at each use: never a
        // static, a global or a stale reference (TVI-WO-1300a (2)).
        private GuardedSTM32H7_RCC Rcc(out string name)
        {
            if(!host.TryGetLocalName(this, out name))
            {
                name = null;
            }
            return name != null && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc) && rcc.Holds(name, this) ? rcc : null;
        }

        // TVI-SPI-FIELD: the first field, in the table's order, whose written value is not allowed.
        private bool RefusedField(long offset, uint value)
        {
            var rows = Fields.Where(f => f.Offset == offset).ToArray();
            if(rows.Length == 0)
            {
                records.Refuse(Field, Unmodeled, RegisterAt(offset), "no register at offset 0x" + offset.ToString("X"),
                    "write to an SPI offset where RM0433 defines no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §50.12 Table 429 p2233", State(), offset, value, 32);
                return true;
            }
            foreach(var f in rows)
            {
                var v = f.Of(value);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    records.Refuse(Field, Unmodeled, f.Register, f.Register + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        f.Register + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-0100b.rm0433.md §2)",
                        State(), offset, value, 32);
                    return true;
                }
            }
            return false;
        }

        // At a CR1 write with SPE=1: DSIZE, then TVI-SPI-MODF (the current state, MASTER known, the history).
        private bool EnablePasses(uint newCr1)
        {
            var dsize = cfg1 & Cfg1Dsize;
            if(dsize != DsizeModeled)
            {
                records.Refuse(Field, Unmodeled, "SPI_CFG1", "SPI_CFG1.DSIZE (4:0) = " + Convert.ToString(dsize, 2).PadLeft(5, '0') + " at the enable",
                    "CR1.SPE set with a frame size other than 8 bits: 00000 is acknowledged only at rest (written while disabled, as ArduPilot's spi_lld_stop does), and the stock model's other frame sizes are not acknowledged",
                    "RM0433 Rev 9 §50.11.3 p2220 (DSIZE); allow-list row SPI_CFG1.DSIZE (TVI-WO-0100b.rm0433.md §2, §4 A6)",
                    State(), Cr1Offset, newCr1, 32);
                return false;
            }
            // MASTER unknown first: if a mode fault cleared MASTER, no branch of the truth table applies.
            var failure = masterUnknown ? new Branch(Unmodeled, "CFG2.MASTER unknown: IFCR.MODFC written after a master state other than the passing one, and CFG2 not written since",
                    "CR1.SPE set after IFCR.MODFC cleared a possible mode fault: if silicon raised MODF it also cleared CFG2.MASTER, and MODFC clears only MODF, so whether this SPI is still a master cannot be given until CFG2 is written again",
                    "RM0433 Rev 9 §50.11.7 p2227 (MODFC); §50.5.2 p2184")
                : FirstFailingBranch(cfg2, newCr1);
            if(failure == null && history != null)
            {
                failure = new Branch(Unmodeled, "a master state other than the passing one, since reset: " + history,
                    "CR1.SPE set after the SPI was configured as a master in a state where a mode fault was possible. RM0433 does not settle whether MODF is evaluated while SPE=0: against a gate, p2184 states the fault for \"the master device\" with no enable condition, and its multimaster procedure raises MODF on switching into master mode, which is done at SPE=0; for a gate, p2217 says that at SPE=0 \"any SPI operation is stopped\", and p2171 configures CFG2 before SSI. A latched MODF would block SPE and clear MASTER. The guard takes the side that cannot pass falsely (northstar invariant 1); only silicon settles it",
                    "RM0433 Rev 9 §50.5.2 p2184; §50.11.1 p2217; §50.4.9 p2171");
            }
            if(failure == null)
            {
                return true;
            }
            records.Refuse(Modf, failure.Kind, "SPI_CR1 / SPI_CFG2", failure.Bit, failure.Condition, failure.Citation,
                State(), Cr1Offset, newCr1, 32);
            return false;
        }

        // After a passed CR1 or CFG2 write: remember the first master state the enable rule would not pass.
        private void NoteMasterState()
        {
            if(history == null && (cfg2 & Cfg2Master) != 0 && FirstFailingBranch(cfg2, cr1) != null)
            {
                history = "CR1=0x" + cr1.ToString("X8") + ", CFG2=0x" + cfg2.ToString("X8");
            }
        }

        // The truth table (TVI-WO-0100a.rm0433.md §1; TVI-WO-0100b.rm0433.md §3.1): null for the passing rows.
        private static Branch FirstFailingBranch(uint cfg2, uint cr1)
        {
            if((cfg2 & Cfg2Master) == 0)
            {
                return new Branch(Unmodeled, "CFG2.MASTER (bit 22) = 0",
                    "CR1.SPE set in slave mode: the stock STM32H7_SPI model supports master mode only (measured)",
                    "RM0433 Rev 9 §50.11.4 p2222 (CFG2.MASTER)");
            }
            if((cfg2 & Cfg2Sp) != 0)
            {
                return new Branch(Unmodeled, "CFG2.SP[2:0] (bits 21:19) != 000",
                    "CR1.SPE set in master mode with a serial protocol other than Motorola: in TI mode the setting of SSM, SSIOP and SSOE is not required, and the manual does not describe mode fault for a TI master, so a mode fault cannot be ruled out",
                    "RM0433 Rev 9 §50.5.1 p2181; §50.4.9 p2171 step 2(d); §50.11.4 p2222 (SP[2:0])");
            }
            if((cfg2 & Cfg2Ssm) == 0)
            {
                if((cfg2 & Cfg2Ssoe) == 0)
                {
                    return new Branch(Unmodeled, "CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 0",
                        "CR1.SPE set in master mode with hardware SS input: an active NSS pin level raises a mode fault, and the twin does not model the pin level",
                        "RM0433 Rev 9 §50.4.7 p2166; §50.11.4 p2221 (SSM, SSOE)");
                }
                // SSM=0, SSOE=1: "the NSS signal input is forced to non active state internally to prevent
                // master mode fault error" (p2221; p2166). The NSS output itself is the allow-list's row
                // SPI_CFG2.SSOE, acknowledged with no NSS pin routed (TVI-WO-0100b.rm0433.md §1.2, §4 A2).
                return null;
            }
            if((cfg2 & Cfg2Ssiop) != 0)
            {
                return new Branch(Unmodeled, "CFG2.SSIOP (bit 28) = 1",
                    "CR1.SPE set in master mode with SS active-high polarity: the manual is inconsistent on which SSI level faults (\"equal to SSIOP\" on p2165 and in Figure 618 on p2167; \"pulled low\" on p2184), so a mode fault cannot be ruled out",
                    "RM0433 Rev 9 §50.4.7 p2165, Figure 618 p2167; §50.5.2 p2184");
            }
            if((cr1 & Cr1Ssi) == 0)
            {
                return new Branch(PredictedFault, "CR1.SSI (bit 12) = 0",
                    "master mode with software SS management and the internal SS input at its active level (SSM=1, SSI=0, SSIOP=0): silicon sets SR.MODF (bit 9), forces CR1.SPE to 0 and clears CFG2.MASTER; the stock model implements none of these",
                    "RM0433 Rev 9 §50.5.2 p2184; §50.4.7 p2165, Figure 618 p2167; §50.11.4 p2221 (SSM, SSIOP); §50.11.6 p2225 (SR.MODF)");
            }
            return null;
        }

        private int FrameBits()
        {
            return ((int)(cfg1 & Cfg1Dsize) / 8 + 1) * 8; // whole bytes, as the stock model transfers (L350)
        }

        private string State()
        {
            return "CR1 last passed=0x" + cr1.ToString("X8") + ", CFG1 last passed=0x" + cfg1.ToString("X8")
                + ", CFG2 last passed=0x" + cfg2.ToString("X8")
                + ", IOLOCK latched=" + (ioLock ? (ioLockCertain ? "yes" : "yes, uncertain") : "no")
                + ", master history=" + (history ?? "none") + ", MASTER unknown=" + (masterUnknown ? "yes" : "no");
        }

        private static string RegisterAt(long offset)
        {
            var row = Fields.FirstOrDefault(f => f.Offset <= offset && offset < f.Offset + 4);
            return row?.Register ?? "SPI offset 0x" + offset.ToString("X");
        }

        private sealed class Branch
        {
            public Branch(string kind, string bit, string condition, string citation)
            {
                Kind = kind;
                Bit = bit;
                Condition = condition;
                Citation = citation;
            }

            public string Kind { get; }
            public string Bit { get; }
            public string Condition { get; }
            public string Citation { get; }
        }

        // One row of the allow-list: an RM0433 field, the values the model implements (null: any value),
        // its class (guard, inherited and unaudited, acknowledged by name, tag, reserved), and what the
        // model behaves as. Rows are the measured table of TVI-WO-0100b.rm0433.md §2 as ruled (§4).
        private sealed class Row
        {
            public Row(string register, long offset, string section, string name, int msb, int lsb, uint[] allowed,
                string cls, string behavesAs, int page)
            {
                Register = register;
                Offset = offset;
                Section = section;
                Name = name;
                Msb = msb;
                Lsb = lsb;
                Allowed = allowed;
                Class = cls;
                BehavesAs = behavesAs;
                Page = page;
            }

            public uint Of(uint value)
            {
                var width = Msb - Lsb + 1;
                return width == 32 ? value : (value >> Lsb) & ((1u << width) - 1);
            }

            public string Format(uint v)
            {
                var width = Msb - Lsb + 1;
                return width > 1 && width <= 5 ? Convert.ToString(v, 2).PadLeft(width, '0') : width == 1 ? v.ToString() : "0x" + v.ToString("X");
            }

            public string Bits => Msb == Lsb ? "bit " + Lsb : Msb + ":" + Lsb;
            public string Register { get; }
            public long Offset { get; }
            public string Section { get; }
            public string Name { get; }
            public int Msb { get; }
            public int Lsb { get; }
            public uint[] Allowed { get; }
            public string Class { get; }
            public string BehavesAs { get; }
            public int Page { get; }
        }

        private static readonly uint[] Any = null;
        private static readonly uint[] Zero = { 0 };
        private const string Guard = "modeled by the guard";
        private const string Inherited = "inherited, unaudited (acknowledged by name)";
        private const string Acked = "acknowledged by name";
        private const string Tag = "tag";
        private const string Reserved = "reserved";

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, cls, behaves, page);
        }

        private static readonly Row[] Fields =
        {
            R("SPI_CR1", 0x00, "§50.11.1", "Res31_17", 31, 17, Zero, Reserved, "0", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "IOLOCK", 16, 16, Any, Guard, "the guard's latch", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "TCRCINI", 15, 15, Zero, Tag, "0 (no CRC)", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "RCRCINI", 14, 14, Zero, Tag, "0 (no CRC)", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "CRC33_17", 13, 13, Zero, Tag, "0 (no CRC)", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "SSI", 12, 12, Any, Guard, "the guard's copy", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "HDDIR", 11, 11, Zero, Tag, "0", 2215),
            R("SPI_CR1", 0x00, "§50.11.1", "CSUSP", 10, 10, Any, Inherited, "the stock model", 2216),
            R("SPI_CR1", 0x00, "§50.11.1", "CSTART", 9, 9, Any, Inherited, "the stock model", 2216),
            R("SPI_CR1", 0x00, "§50.11.1", "MASRX", 8, 8, Any, Acked, "a FIFO that never fills, served from the guard's copy (A4)", 2217),
            R("SPI_CR1", 0x00, "§50.11.1", "Res7_1", 7, 1, Zero, Reserved, "0", 2217),
            R("SPI_CR1", 0x00, "§50.11.1", "SPE", 0, 0, Any, Inherited, "the stock model; the enable is TVI-SPI-MODF's", 2217),
            R("SPI_CR2", 0x04, "§50.11.2", "TSER", 31, 16, Zero, Tag, "0", 2218),
            R("SPI_CR2", 0x04, "§50.11.2", "TSIZE", 15, 0, Any, Inherited, "the stock model", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "Res31", 31, 31, Zero, Reserved, "0", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "MBR", 30, 28, Any, Acked, "no clock: a transfer takes no virtual time (A1)", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "Res27_23", 27, 23, Zero, Reserved, "0", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "CRCEN", 22, 22, Zero, Tag, "0 (no CRC)", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "Res21", 21, 21, Zero, Reserved, "0", 2218),
            R("SPI_CFG1", 0x08, "§50.11.3", "CRCSIZE", 20, 16, new uint[] { 7, 0 }, Acked, "00111; 00000 acknowledged with CRCEN=0, which its own row requires (A5)", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "TXDMAEN", 15, 15, Any, Inherited, "the stock model (stored)", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "RXDMAEN", 14, 14, Any, Inherited, "the stock model", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "Res13", 13, 13, Zero, Reserved, "0", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "UDRDET", 12, 11, Zero, Tag, "00 (slave only)", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "UDRCFG", 10, 9, Zero, Tag, "00 (slave only)", 2219),
            R("SPI_CFG1", 0x08, "§50.11.3", "FTHLV", 8, 5, Zero, Tag, "0000: one frame per packet", 2220),
            R("SPI_CFG1", 0x08, "§50.11.3", "DSIZE", 4, 0, new uint[] { 7, 0 }, Inherited, "8-bit frames; 00000 acknowledged at rest only (A6)", 2220),
            R("SPI_CFG2", 0x0C, "§50.11.4", "AFCNTR", 31, 31, Any, Acked, "no pins (A3)", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "SSOM", 30, 30, Zero, Tag, "0", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "SSOE", 29, 29, Any, Acked, "the guard's copy for the mode fault; no NSS pin routed (A2; DS12117 Rev 10 Table 9)", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "SSIOP", 28, 28, Any, Guard, "the guard's copy", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "Res27", 27, 27, Zero, Reserved, "0", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "SSM", 26, 26, Any, Guard, "the guard's copy", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "CPOL", 25, 25, Zero, Tag, "no clock edge", 2221),
            R("SPI_CFG2", 0x0C, "§50.11.4", "CPHA", 24, 24, Zero, Tag, "no clock edge", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "LSBFRST", 23, 23, Zero, "stock, wrong (byte order, L352)", "MSB first", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "MASTER", 22, 22, Any, Guard, "the guard's copy", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "SP", 21, 19, Any, Guard, "the guard's copy", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "COMM", 18, 17, Zero, Tag, "00: full duplex", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "Res16", 16, 16, Zero, Reserved, "0", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "IOSWP", 15, 15, Zero, Tag, "0", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "Res14_8", 14, 8, Zero, Reserved, "0", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "MIDI", 7, 4, Zero, Tag, "0000 (no delay)", 2222),
            R("SPI_CFG2", 0x0C, "§50.11.4", "MSSI", 3, 0, Zero, Tag, "0000 (no delay)", 2222),
            R("SPI_IER", 0x10, "§50.11.5", "Res31_11", 31, 11, Zero, Reserved, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "TSERFIE", 10, 10, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "MODFIE", 9, 9, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "TIFREIE", 8, 8, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "CRCEIE", 7, 7, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "OVRIE", 6, 6, Any, Acked, "an interrupt that never fires: OVR is never raised (A7)", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "UDRIE", 5, 5, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "TXTFIE", 4, 4, Zero, Tag, "0", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "EOTIE", 3, 3, Any, Inherited, "the stock model", 2223),
            R("SPI_IER", 0x10, "§50.11.5", "DXPIE", 2, 2, Zero, Tag, "0", 2224),
            R("SPI_IER", 0x10, "§50.11.5", "TXPIE", 1, 1, Any, Inherited, "the stock model", 2224),
            R("SPI_IER", 0x10, "§50.11.5", "RXPIE", 0, 0, Any, Inherited, "the stock model", 2224),
            R("SPI_SR", 0x14, "§50.11.6", "SR", 31, 0, Any, "read-only", "a write is ignored, as on silicon", 2224),
            R("SPI_IFCR", 0x18, "§50.11.7", "Res31_12", 31, 12, Any, Acked, "ignored; acknowledged for IFCR only, unmodeled (A9)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "SUSPC", 11, 11, Any, Inherited, "the stock model", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "TSERFC", 10, 10, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "MODFC", 9, 9, Any, Guard, "clears the D1 history, leaving MASTER unknown; no effect without it", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "TIFREC", 8, 8, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "CRCEC", 7, 7, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "OVRC", 6, 6, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "UDRC", 5, 5, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "TXTFC", 4, 4, Any, Acked, "clears a flag the model never sets (A8)", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "EOTC", 3, 3, Any, Inherited, "the stock model", 2227),
            R("SPI_IFCR", 0x18, "§50.11.7", "Res2_0", 2, 0, Any, Acked, "ignored; acknowledged for IFCR only, unmodeled (A9)", 2227),
            R("SPI_TXDR", 0x20, "§50.11.8", "TXDR", 31, 0, Any, Inherited, "the stock model, one frame per access", 2227),
            R("SPI_RXDR", 0x30, "§50.11.9", "RXDR", 31, 0, Any, "read-only", "a write is ignored, as on silicon", 2228),
            R("SPI_CRCPOLY", 0x40, "§50.11.10", "CRCPOLY", 31, 0, new uint[] { CrcPolyReset }, Guard, "0x107, the reset, served from the guard's copy (C4)", 2228),
            R("SPI_TXCRC", 0x44, "§50.11.11", "TXCRC", 31, 0, Any, "read-only", "a write is ignored, as on silicon", 2229),
            R("SPI_RXCRC", 0x48, "§50.11.12", "RXCRC", 31, 0, Any, "read-only", "a write is ignored, as on silicon", 2229),
            R("SPI_UDRDR", 0x4C, "§50.11.13", "UDRDR", 31, 0, Zero, "undefined in the stock model", "0 (slave only)", 2230),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "Res31_26", 31, 26, Zero, Reserved, "0", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "MCKOE", 25, 25, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "ODD", 24, 24, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "I2SDIV", 23, 16, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "Res15", 15, 15, Zero, Reserved, "0", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "DATFMT", 14, 14, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "WSINV", 13, 13, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "FIXCH", 12, 12, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "CKPOL", 11, 11, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "CHLEN", 10, 10, Zero, Tag, "0 (I2S only)", 2231),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "DATLEN", 9, 8, Zero, Tag, "0 (I2S only)", 2232),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "PCMSYNC", 7, 7, Zero, Tag, "0 (I2S only)", 2232),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "Res6", 6, 6, Zero, Reserved, "0", 2232),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "I2SSTD", 5, 4, Zero, Tag, "0 (I2S only)", 2232),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "I2SCFG", 3, 1, Zero, Tag, "0 (I2S only)", 2232),
            R("SPI_I2SCFGR", 0x50, "§50.11.14", "I2SMOD", 0, 0, Zero, Guard, "SPI mode (TVI-SPI-I2SMOD)", 2232),
        };

        private uint cr1;
        private uint cfg1;
        private uint cfg2;
        private uint ier;
        private bool ioLock;
        private bool ioLockCertain;
        private string history;
        private bool masterUnknown;
        private bool eotBySuspend;
        private readonly Records records;
        private readonly IMachine host;
        private const string RccName = "sysbus.rcc";
        private const string NoRcc = "no RCC guard at " + RccName + " that holds it registered under that name";

        private const string Modf = "TVI-SPI-MODF";
        private const string Field = "TVI-SPI-FIELD";
        private const string PredictedFault = "predicted-fault";
        private const string Unmodeled = "unmodeled";
        private const long Cr1Offset = 0x00;
        private const long Cr2Offset = 0x04;
        private const long Cfg1Offset = 0x08;
        private const long Cfg2Offset = 0x0C;
        private const long IerOffset = 0x10;
        private const long SrOffset = 0x14;
        private const long IfcrOffset = 0x18;
        private const long TxdrOffset = 0x20;
        private const long RxdrOffset = 0x30;
        private const long CrcPolyOffset = 0x40;
        private const long I2sCfgrOffset = 0x50;
        private const uint Cr1Spe = 1u << 0;
        private const uint Cr1Stock = Cr1Spe | Cr1Cstart | Cr1Csusp; // driven by the stock model
        private const uint Cr1Cstart = 1u << 9;
        private const uint Cr1Csusp = 1u << 10;
        private const uint Cr1Ssi = 1u << 12;
        private const uint Cr1IoLock = 1u << 16;
        private const uint Cfg1Reset = 0x00070007; // p2218
        private const uint Cfg1Dsize = 0x1F;
        private const uint DsizeModeled = 7;
        private const uint Cfg1DmaEnables = (1u << 14) | (1u << 15);
        private const uint Cfg2Sp = 7u << 19;
        private const uint Cfg2Master = 1u << 22;
        private const uint Cfg2Ssm = 1u << 26;
        private const uint Cfg2Ssiop = 1u << 28;
        private const uint Cfg2Ssoe = 1u << 29;
        private const uint IfcrModfc = 1u << 9;
        private const uint IfcrEotc = 1u << 3;
        private const uint SrRxp = 1u << 0;
        private const uint SrEot = 1u << 3;
        private const uint CrcPolyReset = 0x107; // p2228
        private const uint I2sMod = 1u << 0;
    }
}
