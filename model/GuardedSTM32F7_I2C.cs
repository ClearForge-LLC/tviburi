// Tviburi's guard over Renode's STM32F7_I2C for the STM32H753's I2C1, I2C2 and I2C3 (TVI-WO-1306): a sensor bus takes its
// timing and filters only as silicon does, starts only on a running clock, and reads back what it was given. The measurement,
// every page, and the rulings (2026-11-25T05:06Z, Q1-Q12): docs/work-orders/TVI-WO-1306.measure.md (RM0433 Rev 9 §47).
//
// Insertion point (RUL-07 as amended): STM32F7_I2C is sealed, and it is also its sensors' bus (a SimpleContainer on which a
// platform's `uN: ... @ i2cN 0xAA` registers). So this guard is a wrapper that owns an unregistered stock instance, is itself
// the container the sensors register on, and hands the owned instance its own ChildCollection (one reflection write: the
// owned instance finds a sensor at START in that dictionary; forwarding Register to it throws, measure.md §2.4). It forwards
// every access it passes, the three interrupt lines and every reset. The stock platform declares each port, so the board
// description cancels it and declares this guard at its address `as "i2cN"` on tviburiRcc with the stock entry's interrupt
// lines (model/stm32h7-i2c-restate.repl); a sensor is declared `@ tviburiI2cN` (an `@ i2cN` binds to the cancelled entry and
// the load fails, E39). i2c4 (D3 domain) stays stock. The nested `Registers` enum keeps the access log's register names.
//
// The bus clock (Q7): with I2CxEN = 0 the RCC guard locks this peripheral on the bus (it watches it and never unlocks it,
// TVI-WO-1300b), so an access is dropped there with no record: silicon's ignored write (p373). A read returns 0 where p373
// says "invalid data"; no measured firmware uses the value (ArduPilot reads CR1, then writes 0, before I2C1EN).
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-I2C-CONFIG    the platform gives this guard what it cannot model: more than one bus registration, the owned instance's
//                     sensors not shared, or no RCC guard holding it under its name (TVI-WO-1300b; ruled Q4). unmodeled.
//   TVI-I2C-WIDTH     a byte or half-word access, or a misaligned word: "The registers are accessed by words (32-bit)" (p2001).
//                     unmodeled. Every measured access is a word (measure.md §2.1).
//   TVI-I2C-READ      a read of an offset with no register (0x2C-0x3FC). unmodeled.
//   (ISR's flags, PECR, RXDR) read-only: a write is ignored and changes nothing, with no record (RUL-18 as amended).
//   TVI-I2C-PE        a write, while PE = 1, that changes TIMINGR (p2008), or CR1's DNF (p2003), ANFOFF or NOSTRETCH (p2002):
//                     "can be programmed only when the I2C peripheral is disabled (PE = 0)". unmodeled: RM0433 does not say what
//                     silicon does with such a write (ruled Q2). The same value passes (RUL-18); a CR1 write that clears PE is
//                     judged as written with PE = 0.
//   TVI-I2C-FIELD     the allow-list (`Fields`): a field given a value the model does not implement. unmodeled.
//   TVI-I2C-RESET     PE set (0 -> 1) as the very next access after the one that cleared it: "PE must be kept low during at least
//                     three APB clock cycles ... 1. Write PE = 0 2. Check PE = 0 3. Write PE = 1" (§47.4.6 p1960), which the twin
//                     cannot count. unmodeled (ruled Q6).
//   TVI-I2C-KERNEL    a start (PE 0 -> 1) with no running kernel clock, by the RCC guard's existing query (QueryKernelClock,
//                     TVI-WO-1300): "The I2C kernel is clocked by i2c_ker_ck" (§47.4.3 p1954) and "no clock will be provided to
//                     the peripheral" (p371). predicted-fault; unmodeled where the query has no rule (i2c3) or no RCC guard is
//                     found (ruled Q3).
//   TVI-I2C-TRANSFER  a transfer the bus would not make (ruled Q8; the adversarial pass's F1-F4), unmodeled: a START, STOP or
//                     TXDR write with PE = 0 (p2003 "Peripheral disabled"); a TXDR write outside a write transfer's data phase
//                     (p2014: the stock model would send the byte); START and STOP in one write; a START with NBYTES = 0 and
//                     RELOAD = 0 (the stock model hands the device an empty write and Renode aborts, measure.md §2.3 I8b); a START
//                     while a transfer is open (silicon queues it after NBYTES, p2005; the stock model starts at once); a STOP
//                     with no transfer (p2005: controller mode only; the stock model sets STOPF); CR2's NBYTES, HEAD10R, ADD10,
//                     RD_WRN or SADD changed while a transfer is open (p1973). An empty START (NBYTES = 0) is never passed on,
//                     recorded or not: recorded, the write goes on with START cleared. A transfer is open from a START this guard
//                     passes until the stock model shows TC, TCR or a STOPF that was not already set at that START, a STOP is
//                     written, or PE is cleared.
//                     During a NACKed address's pending automatic STOP (below), a START or a change to those CR2 fields
//                     (TVI-WO-1306a, ruled 2026-11-26T00:22Z Q4); a TXDR write then is the rule above (no data phase).
//   TVI-I2C-ABSENT    a read START to an address with no device: RM0433 states the automatic STOP after a NACK only for the
//                     controller transmitter (§47.4.9 p1975), and no measured firmware reads an absent address (TVI-WO-1306a.measure.md
//                     §2.1). unmodeled (ruled 2026-11-26T00:22Z Q6). A write is NACKed (below).
//   TVI-I2C-NACK      a write START the guard would NACK, with no kernel-clock frequency to time it by (the RCC guard's KernelHz
//                     answers none: a PLL outside its ranges, an HSE with no hseFrequency, a source with no rule). unmodeled (Q2).
// Every START to an address with no device, passed, is logged under the guard's name with the stock model's own line, "Unknown
// slave at address N.", which the owned instance's line lacks (Aethelgard's absent_address reads it by source; TVI-WO-1306 Q9).
// Enforcing and recorded mode (RUL-19) are model/Refusal.cs's contract, but the empty START above; its two F10 edges are named there.
//
// Modeled, cited:
//   TIMINGR, CR1.DNF and ANFOFF stored, and served on read as written (ruled Q5): the stock model has them as tags reading 0.
//   The software reset (§47.4.6 p1959; ruled Q6, prototyped on both ArduPilot runs, identical at all 2,057 PE clears): a CR1
//     write that clears PE resets the owned instance (its byte queues, every ISR flag, TXE = 1 as p2011 says, the interrupt
//     lines) and restores the configuration RM0433 keeps: CR1 as written, OAR1, OAR2, and CR2 but START and STOP. TIMINGR is
//     the guard's own copy. The stock model alone kept RXNE and a byte, then set TC (measure.md §2.3 I4).
//   PE = 1 is this port running, for the RCC guard's TVI-RCC-CLOCK (IRccWatched; ruled Q4).
//   The NACK (TVI-WO-1306a.measure.md; ruled 2026-11-26T00:22Z): a write START to a 7-bit address with no device on the port's bus
//     sets NACKF nine SCL periods later (p1955, the period by p1971 from TIMINGR, DNF and the RCC guard's KernelHz, each tSYNC at its
//     stated floor; inferred: the START's hold, tHD:STA, which SCLH sets (p1973), is not added, so it may be early by about that),
//     and STOPF one SCL period after NACKF (inferred: RM0433 gives no figure), whatever AUTOEND
//     ("a STOP condition is automatically sent", p1975). NACKIE raises the event interrupt; NACKCF and STOPCF clear the flags
//     (p2012); PE = 0 and a reset clear them and cancel what is pending (p1959, p2011). A firmware STOP while the automatic STOP is
//     pending is absorbed: silicon clears the bit "when a STOP condition is detected" (p2005; ChibiOS writes one at every NACK).
//     A NACK in the data phase (a device that NACKs a byte) is not modeled: no device model in Renode NACKs a byte.
//
// Reads: CR1 and TIMINGR from the guard's copies (RUL-17's read rule; ruled Q5); TIMEOUTR and PECR read 0 (their only
// allowed and reset values); ISR from the stock model with the NACK's NACKF and STOPF; the rest from the stock model. The copies return to reset at the guard's Reset(), which the RCC's
// I2CxRST runs (ArduPilot pulses I2C1RST before every start, measure.md §2.6) and a machine reset runs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32F7_I2C : SimpleContainer<II2CPeripheral>, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IRccWatched
    {
        // `mode` (RUL-19) is this guard's; the stock entry has no other constructor argument.
        public GuardedSTM32F7_I2C(IMachine machine, string mode = Records.Enforcing) : base(machine)
        {
            host = machine;
            inner = new STM32F7_I2C(machine);
            records = new Records(machine, this, nameof(GuardedSTM32F7_I2C), mode);
            EventInterrupt = new GPIO();
            inner.EventInterrupt.AddStateChangedHook(v => { innerEvent = v; UpdateEvent(); });
            try
            {
                typeof(SimpleContainerBase<II2CPeripheral>).GetField("ChildCollection", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(inner, ChildCollection);
            }
            catch(Exception e)
            {
                shareFailure = "the owned instance's sensors could not be shared (" + e.GetType().Name + ")";
            }
        }

        public long Size => 0x400;

        // The stock entry's lines (`EventInterrupt -> exti@N`, `ErrorInterrupt -> ...`): the error line is the owned instance's; the
        // event line is this guard's, the owned instance's OR NACKF with NACKIE OR the NACK's own STOPF with STOPIE (TVI-WO-1306a; the
        // stock model has `NACKIE && false`).
        public GPIO EventInterrupt { get; }
        public GPIO ErrorInterrupt => inner.ErrorInterrupt;
        public GPIO DmaReceive => inner.DmaReceive;

        // IRccWatched (ruled Q4): PE = 1 is this port running, for TVI-RCC-CLOCK.
        public bool IsRunning => (cr1 & PeBit) != 0;
        public string RunningName => "PE";
        public string KernelCitation => "§47.4.3 p1954 (\"The I2C kernel is clocked by i2c_ker_ck\")";

        public override void Reset()
        {
            lock(sync)
            {
                inner.Reset();
                inner.WriteDoubleWord(Cr1Offset, 0);   // the stock Reset leaves TXIS; its PE = 0 callback clears it (ISR 0x1, p2009)
                CancelNack();
                cr1 = timingr = 0;
                open = controller = staleStopf = clearedLast = false;
            }
        }

        public override void Dispose()
        {
            records.End();
            base.Dispose();
        }

        // ---- bus access ----------------------------------------------------------------------------------------

        public uint ReadDoubleWord(long offset)
        {
            return Read(offset, 32);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            Write(offset, value, 32);
        }

        public ushort ReadWord(long offset)
        {
            return (ushort)Read(offset, 16);
        }

        public void WriteWord(long offset, ushort value)
        {
            Write(offset, value, 16);
        }

        public byte ReadByte(long offset)
        {
            return (byte)Read(offset, 8);
        }

        public void WriteByte(long offset, byte value)
        {
            Write(offset, value, 8);
        }

        private uint Read(long offset, int width)
        {
            lock(sync)
            {
                Open();   // a completion the stock model shows ends the open transfer before this access (an ICR write may clear it)
                clearedLast = false;
                if(Unloaded(offset, 0, width) || Narrow(offset, 0, width, "read"))
                {
                    return 0;
                }
                if(!Defined(offset))
                {
                    records.Refuse("TVI-I2C-READ", Unmodeled, "none", "read of offset 0x" + offset.ToString("X"),
                        "read of an I2C offset where RM0433 defines no register: the twin cannot say what silicon returns",
                        "RM0433 Rev 9 §47.9.12 Table 401 p2015", State(), offset, 0, width);
                    return 0;
                }
                return Served(offset);
            }
        }

        private void Write(long offset, uint value, int width)
        {
            lock(sync)
            {
                Open();   // as in Read
                var afterClear = clearedLast;
                clearedLast = false;
                if(Unloaded(offset, value, width) || Narrow(offset, value, width, "write"))
                {
                    return;
                }
                if(!Defined(offset))
                {
                    records.Refuse("TVI-I2C-FIELD", Unmodeled, "none", "write to offset 0x" + offset.ToString("X"),
                        "write to an I2C offset where RM0433 defines no register: the twin cannot say what silicon does",
                        "RM0433 Rev 9 §47.9.12 Table 401 p2015", State(), offset, value, width);
                    return;
                }
                var refused = RefusedPe(offset, value, width);
                if(!refused || records.Recorded)
                {
                    refused |= RefusedField(offset, value, width);
                }
                var start = offset == Cr1Offset && !IsRunning && (value & PeBit) != 0;
                if(start && afterClear && (!refused || records.Recorded))
                {
                    refused |= RefuseReset(value);
                }
                if(start && (!refused || records.Recorded))
                {
                    refused |= RefusedKernel(value);
                }
                // An empty START (NBYTES = 0) never reaches the stock model, recorded or not: it hands the device an empty write.
                var empty = offset == Cr2Offset && (value & StartBit) != 0 && ((value >> 16) & 0xFF) == 0;
                if(!refused || records.Recorded)
                {
                    refused |= RefusedTransfer(offset, value, width);
                }
                if(!empty && (!refused || records.Recorded))
                {
                    refused |= RefusedAbsent(offset, value, width);
                }
                double? tick = null;
                if(!empty && (!refused || records.Recorded) && Nacked(offset, value))
                {
                    tick = SclPeriod(out var why);
                    refused |= tick == null && RefuseNack(why, value, width);
                }
                if(refused && !records.Recorded)
                {
                    return;
                }
                Pass(offset, empty ? value & ~StartBit : value, tick);
                UpdateEvent();
            }
        }

        private uint Served(long offset)
        {
            switch(offset)
            {
            case Cr1Offset: return cr1;
            case TimingrOffset: return timingr;
            case TimeoutrOffset:
            case PecrOffset: return 0;
            case IsrOffset: return Isr();
            default: return inner.ReadDoubleWord(offset);
            }
        }

        // ISR: the owned instance's, with the NACK's NACKF and STOPF, which the stock model never sets.
        private uint Isr()
        {
            return inner.ReadDoubleWord(IsrOffset) | (nackf ? NackfBit : 0) | (nackStopf ? StopfBit : 0);
        }

        private void Pass(long offset, uint word, double? tick)
        {
            switch(offset)
            {
            case Cr1Offset:
                var before = cr1;
                cr1 = word & Cr1Defined;   // reserved bits keep their reset value, 0 (p2001), even recorded
                if((before & PeBit) != 0 && (word & PeBit) == 0)
                {
                    SoftwareReset(word);
                    clearedLast = true;
                    return;
                }
                inner.WriteDoubleWord(offset, word);
                return;
            case Cr2Offset:
                var stopf = (Isr() & StopfBit) != 0;
                if((word & StopBit) != 0 && nackStopPending)
                {
                    // A STOP written while the NACK's automatic STOP is pending (ChibiOS's `CR2 |= STOP` at its NACKF): silicon clears
                    // the bit "when a STOP condition is detected" (p2005), so it adds nothing; the stock model would set STOPF now.
                    inner.WriteDoubleWord(offset, word & ~StopBit);
                    return;
                }
                inner.WriteDoubleWord(offset, word);
                if((word & StopBit) != 0)
                {
                    open = controller = false;
                }
                if((word & StartBit) != 0)
                {
                    open = controller = true;
                    staleStopf = stopf;   // the stock START leaves STOPF, as silicon does: an earlier STOPF ends nothing here
                    var a = Address(word);
                    if(!TryGetByAddress(a, out var _))
                    {
                        // The stock model's own line (STM32F7_I2C.cs, StartTransfer), under this guard's name: the owned
                        // instance's copy has none (measure.md §2.4), and Aethelgard's absent_address reads it by source (Q9).
                        this.Log(LogLevel.Warning, "Unknown slave at address {0}.", a);
                        // No data phase follows: silicon NACKs the address and sends a STOP (p1975). The port stays in controller
                        // mode until that STOP's STOPF.
                        open = false;
                        if(tick != null)
                        {
                            Nack(tick.Value);
                        }
                    }
                }
                return;
            case TimingrOffset:
                timingr = word & TimingrMask;   // the stock model's TIMINGR is all tags: kept here, never forwarded; 27:24 reserved
                return;
            case IcrOffset:
                nackf &= (word & NackcfBit) == 0;      // NACKCF clears NACKF (p2012)
                nackStopf &= (word & StopfBit) == 0;   // STOPCF clears STOPF (p2012)
                inner.WriteDoubleWord(offset, word & ~NackcfBit);   // NACKCF is a stock tag: it would log the clear as unhandled
                return;
            case TimeoutrOffset:  // 0 only (Fields); the stock model has no register 0x14
            case PecrOffset:      // read-only
            case RxdrOffset:      // read-only
                return;
            default:
                inner.WriteDoubleWord(offset, word);
                return;
            }
        }

        // §47.4.6 p1959 (ruled Q6): "Internal state machines are reset and the communication control bits and the status bits
        // revert to their reset values. This reset does not impact the configuration registers."
        private void SoftwareReset(uint word)
        {
            var cr2 = inner.ReadDoubleWord(Cr2Offset) & ~(StartBit | StopBit);
            var oar1 = inner.ReadDoubleWord(Oar1Offset);
            var oar2 = inner.ReadDoubleWord(Oar2Offset);
            CancelNack();   // NACKF and STOPF are reset by PE = 0, and a pending NACK or STOP with them (p1959, p2011)
            inner.Reset();
            if(oar1 != 0)
            {
                inner.WriteDoubleWord(Oar1Offset, oar1);
            }
            if(oar2 != 0)
            {
                inner.WriteDoubleWord(Oar2Offset, oar2);
            }
            inner.WriteDoubleWord(Cr2Offset, cr2);
            inner.WriteDoubleWord(Cr1Offset, word);
            open = controller = staleStopf = false;
        }

        // ---- the NACK (TVI-WO-1306a; ruled 2026-11-26T00:22Z) ------------------------------------------------------------------

        // The NACK, as the header states it (NACKF, then STOPF, one SCL period apart). From the START to that STOPF the automatic STOP
        // is pending.
        private void Nack(double tick)
        {
            var e = ++nackEpoch;
            nackStopPending = true;
            host.ScheduleAction(TimeInterval.FromNanoseconds((ulong)Math.Ceiling(AddressPeriods * tick * 1e9)), _ =>
            {
                lock(sync)
                {
                    if(e != nackEpoch)
                    {
                        return;
                    }
                    nackf = true;
                    UpdateEvent();
                    host.ScheduleAction(TimeInterval.FromNanoseconds((ulong)Math.Ceiling(tick * 1e9)), __ =>
                    {
                        lock(sync)
                        {
                            if(e != nackEpoch)
                            {
                                return;
                            }
                            nackStopf = true;
                            nackStopPending = false;
                            UpdateEvent();
                        }
                    });
                }
            });
        }

        private void CancelNack()
        {
            nackEpoch++;
            nackf = nackStopf = nackStopPending = false;
            UpdateEvent();
        }

        private void UpdateEvent()
        {
            EventInterrupt.Set(innerEvent || (nackf && (cr1 & NackieBit) != 0) || (nackStopf && (cr1 & StopieBit) != 0));
        }

        // A START this guard would NACK: a write, with PE = 1, to a 7-bit address with no device on this port.
        private bool Nacked(long offset, uint word)
        {
            return offset == Cr2Offset && (word & StartBit) != 0 && (word & StopBit) == 0 && (word & (RdWrnBit | Add10Bit)) == 0 && IsRunning
                && !TryGetByAddress(Address(word), out var _);
        }

        // The controller's SCL period (§47.4.9 p1971): tSCL = tSYNC1 + tSYNC2 + [(SCLH+1) + (SCLL+1)] x (PRESC+1) x tI2CCLK, each tSYNC
        // at the floor p1971 states, two i2c_ker_ck periods plus DNF x tI2CCLK; the slope and the analog filter add more, with no figure
        // here. tI2CCLK from the RCC guard's KernelHz (ruled Q2); null, with the reason, without one.
        private double? SclPeriod(out string why)
        {
            why = null;
            var hz = host.TryGetLocalName(this, out var name) && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc)
                ? rcc.KernelHz(name, out why) : null;
            if(hz == null)
            {
                why = why ?? "no RCC guard at " + RccName;
                return null;
            }
            var counted = (((timingr >> 8) & 0xFF) + 1 + (timingr & 0xFF) + 1) * (((timingr >> 28) & 0xF) + 1);
            return (counted + 2 * (2 + ((cr1 >> 8) & 0xF))) / hz.Value;
        }

        // ---- the rules -----------------------------------------------------------------------------------------

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c != null)
            {
                records.Refuse("TVI-I2C-CONFIG", Unmodeled, RegisterAt(offset), "none",
                    "the platform gives this I2C guard what it cannot model: " + c + "; every access refuses",
                    "TVI-WO-1306.measure.md §2.4 (the form); ruled 2026-11-25T05:06Z Q1", "none", offset, value, width);
                return true;
            }
            // TVI-WO-1300b (2), ruled for this guard 2026-11-25T05:06Z Q4: only a guard the RCC guard holds is watched for a
            // kernel clock lost under it (TVI-RCC-CLOCK), and locked by its enable. One it does not hold refuses.
            if(!(host.TryGetLocalName(this, out var name) && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc) && rcc.Holds(name, this)))
            {
                records.Refuse("TVI-I2C-CONFIG", Unmodeled, RegisterAt(offset), "none",
                    "the platform gives this I2C guard what it cannot model: " + (name ?? "an I2C port") + " is not registered on an RCC guard at "
                        + RccName + " under that name, so its enable and its kernel clock are not watched; every access refuses",
                    "TVI-WO-1300b (2); ruled 2026-11-25T05:06Z Q4", "none", offset, value, width);
                return true;
            }
            return false;
        }

        // Words only (p2001).
        private bool Narrow(long offset, ulong value, int width, string what)
        {
            if(width == 32 && (offset & 3) == 0)
            {
                return false;
            }
            records.Refuse("TVI-I2C-WIDTH", Unmodeled, RegisterAt(offset & ~3L), width + "-bit " + what,
                "a " + width + "-bit I2C " + what + " at offset 0x" + offset.ToString("X")
                    + ": the I2C registers are accessed by words, and the twin cannot say what silicon does with this one",
                "RM0433 Rev 9 §47.9 p2001 (\"The registers are accessed by words (32-bit)\")", State(), offset, value, width);
            return true;
        }

        // TVI-I2C-PE (ruled Q2): a protected field changed while PE = 1.
        private bool RefusedPe(long offset, uint word, int width)
        {
            if(!IsRunning || (offset == Cr1Offset && (word & PeBit) == 0))
            {
                return false;
            }
            var mask = offset == TimingrOffset ? TimingrMask : offset == Cr1Offset ? Cr1Protected : 0u;
            var changed = (Served(offset) ^ word) & mask;
            if(changed == 0)
            {
                return false;
            }
            var rows = Fields.Where(f => f.Offset == offset && (changed & f.Mask) != 0).ToArray();
            records.Refuse("TVI-I2C-PE", Unmodeled, RegisterAt(offset), string.Join(", ", rows.Select(f => f.Name + " (" + f.Bits + ")")) + " changed with PE = 1",
                RegisterAt(offset) + " written with PE = 1, changing " + string.Join(", ", rows.Select(f => f.Name + " " + f.Format(f.Of(Served(offset))) + " -> " + f.Format(f.Of(word))))
                    + ": RM0433 says it can be programmed only when the I2C peripheral is disabled (PE = 0) and does not say what silicon does with such a write, so the twin cannot say",
                "RM0433 Rev 9 " + string.Join(", ", rows.Select(f => f.Section + " p" + f.Page).Distinct()) + "; §47.4.5 p1959 (Figure 542); ruled 2026-11-25T05:06Z Q2",
                State(), offset, word, width);
            return true;
        }

        // TVI-I2C-FIELD: the first field, in the table's order, whose written value is not allowed.
        private bool RefusedField(long offset, uint word, int width)
        {
            foreach(var f in Fields)
            {
                if(f.Offset != offset || f.Allowed == null || f.Allowed.Contains(f.Of(word)))
                {
                    continue;
                }
                records.Refuse("TVI-I2C-FIELD", Unmodeled, f.Register, f.Name + " (" + f.Bits + ") = " + f.Format(f.Of(word)),
                    f.Register + "." + f.Name + " written " + f.Format(f.Of(word)) + ": the model implements only " + string.Join(", ", f.Allowed.Select(f.Format))
                        + " (" + f.BehavesAs + ")",
                    "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; the allow-list (RUL-17), ruled 2026-11-25T05:06Z Q11", State(), offset, word, width);
                return true;
            }
            return false;
        }

        // TVI-I2C-RESET (ruled Q6).
        private bool RefuseReset(uint word)
        {
            records.Refuse("TVI-I2C-RESET", Unmodeled, "I2C_CR1", "PE (bit 0) = 1",
                "PE set as the very next access to this port after the one that cleared it, with no check of PE between: RM0433 requires PE low for at least three APB clock cycles (write PE = 0, check PE = 0, write PE = 1), which the twin cannot count",
                "RM0433 Rev 9 §47.4.6 p1960; ruled 2026-11-25T05:06Z Q6", State(), Cr1Offset, word, 32);
            return true;
        }

        // TVI-I2C-KERNEL (ruled Q3): a start with no running kernel clock, by the RCC guard's existing query.
        private bool RefusedKernel(uint word)
        {
            string name = null;
            GuardedSTM32H7_RCC rcc = null;
            var k = host.TryGetLocalName(this, out name) && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out rcc) ? rcc.QueryKernelClock(name) : null;
            if(k != null && k.Modeled && k.Running)
            {
                return false;
            }
            if(k == null || !k.Modeled)
            {
                records.Refuse("TVI-I2C-KERNEL", Unmodeled, "I2C_CR1", "PE (bit 0) = 1",
                    (name ?? "an I2C port") + " started with no kernel-clock rule: " + (k == null ? "no RCC guard at " + RccName : k.Condition),
                    k == null ? "TVI-WO-1300 (4): the RCC guard's QueryKernelClock; ruled 2026-11-25T05:06Z Q3" : k.Citation,
                    State(), Cr1Offset, word, 32);
                return true;
            }
            records.Refuse("TVI-I2C-KERNEL", PredictedFault, "I2C_CR1", k.Selector,
                name + " started (CR1.PE) with no kernel clock: " + k.Condition + "; \"The I2C kernel is clocked by i2c_ker_ck\"",
                k.Citation + "; " + KernelCitation, State(), Cr1Offset, word, 32);
            return true;
        }

        // TVI-I2C-TRANSFER (ruled Q8).
        private bool RefusedTransfer(long offset, uint word, int width)
        {
            string what = null, page = null;
            var start = offset == Cr2Offset && (word & StartBit) != 0;
            var stop = offset == Cr2Offset && (word & StopBit) != 0;
            if((offset == TxdrOffset || start || stop) && !IsRunning)
            {
                what = (offset == TxdrOffset ? "TXDR written" : start ? "START" : "STOP") + " with PE = 0";
                page = "§47.9.1 p2003 (PE: \"0: Peripheral disabled\")";
            }
            else if(offset == TxdrOffset && !(Open() && (inner.ReadDoubleWord(Cr2Offset) & RdWrnBit) == 0))
            {
                what = "TXDR written outside a write transfer's data phase (no START since, a read transfer, or its NBYTES already sent; the stock model sends the byte to the device)";
                page = "§47.9.11 p2014 (\"These bits can be written only when TXE = 1\")";
            }
            else if(start && stop)
            {
                what = "START and STOP in one write (the stock model ignores both; RM0433 does not say what silicon does)";
                page = "§47.9.2 p2005 (START, STOP)";
            }
            else if(start && ((word >> 16) & 0xFF) == 0 && (word & ReloadBit) == 0)
            {
                what = "START with NBYTES = 0 and RELOAD = 0 (never passed on: the stock model hands the device an empty write, and Renode aborts)";
                page = "§47.9.2 p2004 (NBYTES); not in the text; TVI-WO-1306.measure.md §2.3 I8b";
            }
            else if(start && Open())
            {
                what = "START while a transfer is open (silicon queues a repeated START after the NBYTES transfer, where the stock model starts at once and abandons the open transfer)";
                page = "§47.9.2 p2005 (START: \"generates a repeated START condition ... after the end of the NBYTES transfer\")";
            }
            else if(nackStopPending && (start || (offset == Cr2Offset && ((inner.ReadDoubleWord(Cr2Offset) ^ word) & Cr2Held) != 0)))
            {
                what = "START or CR2's NBYTES, HEAD10R, ADD10, RD_WRN or SADD written while a NACKed address's automatic STOP is pending";
                page = "§47.4.9 p1975 (the automatic STOP), §47.9.2 p2005 (START); ruled 2026-11-26T00:22Z Q4";
            }
            else if(stop && !Controller())
            {
                what = "STOP with no transfer (no START since the last STOP, STOPF or PE clear; the stock model sets STOPF)";
                page = "§47.9.2 p2005 (STOP: \"This bit only pertains to controller mode\")";
            }
            else if(offset == Cr2Offset && Open() && ((inner.ReadDoubleWord(Cr2Offset) ^ word) & Cr2Held) != 0)
            {
                what = "CR2's NBYTES, HEAD10R, ADD10, RD_WRN or SADD changed while a transfer is open";
                page = "§47.4.9 p1973 (\"Changing these bitfields is not allowed as long as the START bit is set\"; after the address, not in the text)";
            }
            if(what == null)
            {
                return false;
            }
            records.Refuse("TVI-I2C-TRANSFER", Unmodeled, RegisterAt(offset), what.Split(' ')[0],
                "a transfer the bus would not make: " + what + "; the twin cannot say what silicon does",
                "RM0433 Rev 9 " + page + "; ruled 2026-11-25T05:06Z Q8", State(), offset, word, width);
            return true;
        }

        // TVI-I2C-ABSENT (TVI-WO-1306a, ruled 2026-11-26T00:22Z Q6): a read START to an address with no device. A write is NACKed
        // (above); for a read RM0433 states NACKF (p2011) but the automatic STOP only for the controller transmitter (p1975), and no
        // measured firmware reads an absent address, so the twin does not infer it.
        private bool RefusedAbsent(long offset, uint word, int width)
        {
            if(offset != Cr2Offset || (word & StartBit) == 0 || (word & RdWrnBit) == 0 || !IsRunning || TryGetByAddress(Address(word), out var _))
            {
                return false;
            }
            var name = host.TryGetLocalName(this, out var n) ? n : "an I2C port";
            records.Refuse("TVI-I2C-ABSENT", Unmodeled, "I2C_CR2", "SADD (9:0) = 0x" + (word & 0x3FF).ToString("X"),
                name + " started a read from 0x" + Address(word).ToString("X2") + ", where the platform has no device: RM0433 states the automatic STOP after a NACK only for a write, and the twin does not infer it for a read",
                "RM0433 Rev 9 §47.4.9 p1975 (\"When a NACK is received, the TXIS flag is not set and a STOP condition is automatically sent\", controller transmitter), §47.9.7 p2011 (NACKF); ruled 2026-11-26T00:22Z Q6",
                State(), offset, word, width);
            return true;
        }

        // TVI-I2C-NACK (ruled 2026-11-26T00:22Z Q2): a START the guard would NACK, with no kernel-clock frequency to time it by.
        private bool RefuseNack(string why, uint word, int width)
        {
            records.Refuse("TVI-I2C-NACK", Unmodeled, "I2C_CR2", "SADD (9:0) = 0x" + (word & 0x3FF).ToString("X"),
                "a write to 0x" + Address(word).ToString("X2") + ", where the platform has no device, would be NACKed nine SCL periods after the START, and the twin cannot time it: " + why,
                "RM0433 Rev 9 §47.4.9 p1971 (tSCL), §47.4.4 p1955 (nine SCL pulses), p1975 (the NACK); the RCC guard's KernelHz; ruled 2026-11-26T00:22Z Q2",
                State(), Cr2Offset, word, width);
            return true;
        }

        // A transfer is open from a START this guard passed until the stock model shows TC, TCR or STOPF (checked at every access,
        // before it is passed on), a STOP, or a PE clear. A STOPF already set at that START (the stock model's START leaves it,
        // as silicon's does) ends nothing until it has been cleared. The port is in controller mode from that START until a
        // STOPF of its own, a STOP, or a PE clear.
        private bool Open()
        {
            var isr = Isr();
            staleStopf &= (isr & StopfBit) != 0;
            var stopf = !staleStopf && (isr & StopfBit) != 0;
            if(open && (stopf || (isr & (TcBit | TcrBit)) != 0))
            {
                open = false;
            }
            controller &= !stopf;
            return open;
        }

        private bool Controller()
        {
            Open();
            return controller;
        }

        // ---- names and state -----------------------------------------------------------------------------------

        private static int Address(uint cr2)
        {
            return (int)((cr2 >> 1) & 0x7F);   // 7-bit: SADD[7:1] (p2006); ADD10 is 0 only
        }

        private string Config()
        {
            if(config == null)
            {
                config = Check();
            }
            return config.Length == 0 ? null : config;
        }

        private string Check()
        {
            if(shareFailure != null)
            {
                return shareFailure;
            }
            try
            {
                var points = host.SystemBus.GetRegistrationPoints(this).ToArray();
                return points.Length == 1 ? "" : points.Length + " bus registrations, not one";
            }
            catch(Exception e)
            {
                return "no bus registration (" + e.GetType().Name + ")";
            }
        }

        private static bool Defined(long offset)
        {
            return offset >= 0 && offset <= TxdrOffset;   // aligned: Narrow ran first
        }

        private static string RegisterAt(long offset)
        {
            var f = Fields.FirstOrDefault(x => x.Offset == offset);
            return f != null ? f.Register : "none";
        }

        private string State()
        {
            return "CR1=0x" + cr1.ToString("X") + " CR2=0x" + inner.ReadDoubleWord(Cr2Offset).ToString("X") + " TIMINGR=0x" + timingr.ToString("X")
                + " ISR=0x" + Isr().ToString("X") + (open ? " transfer open" : "") + (nackStopPending ? " NACK's STOP pending" : "");
        }

        private readonly IMachine host;
        private readonly STM32F7_I2C inner;
        private readonly Records records;
        private readonly object sync = new object();
        private readonly string shareFailure;
        private uint cr1, timingr;
        private bool open, controller, staleStopf, clearedLast;
        private bool nackf, nackStopf, nackStopPending, innerEvent;
        private int nackEpoch;
        private string config;

        private const string RccName = "sysbus.rcc";
        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";
        private const long Cr1Offset = 0x00;
        private const long Cr2Offset = 0x04;
        private const long Oar1Offset = 0x08;
        private const long Oar2Offset = 0x0C;
        private const long TimingrOffset = 0x10;
        private const long TimeoutrOffset = 0x14;
        private const long IsrOffset = 0x18;
        private const long IcrOffset = 0x1C;
        private const long PecrOffset = 0x20;
        private const long RxdrOffset = 0x24;
        private const long TxdrOffset = 0x28;
        private const uint PeBit = 1u << 0;
        private const uint Cr1Protected = 0x00021F00;   // NOSTRETCH (17), ANFOFF (12), DNF (11:8): p2002-2003
        private const uint TimingrMask = 0xF0FFFFFF;    // every TIMINGR field (p2008); 27:24 reserved
        private const uint Cr1Defined = 0x00FFDFFF;     // CR1 without its reserved 31:24 and 13 (p2001-2002)
        private const uint StartBit = 1u << 13;
        private const uint StopBit = 1u << 14;
        private const uint ReloadBit = 1u << 24;
        private const uint RdWrnBit = 1u << 10;
        private const uint Add10Bit = 1u << 11;
        private const uint Cr2Held = 0x00FF1FFF;        // NBYTES, HEAD10R, ADD10, RD_WRN, SADD (p2004-2006)
        private const uint TcBit = 1u << 6;
        private const uint TcrBit = 1u << 7;
        private const uint StopfBit = 1u << 5;
        private const uint NackfBit = 1u << 4;
        private const uint NackcfBit = 1u << 4;
        private const uint NackieBit = 1u << 4;
        private const uint StopieBit = 1u << 5;
        private const int AddressPeriods = 9;   // the address byte's nine SCL pulses, the ninth its acknowledge (p1955)

        // The stock model's register names, so the access log names them as it did (measure.md §2.4).
        private enum Registers
        {
            Control1 = 0x00,
            Control2 = 0x04,
            OwnAddress1 = 0x08,
            OwnAddress2 = 0x0C,
            Timing = 0x10,
            Timeout = 0x14,
            InterruptAndStatus = 0x18,
            InterruptClear = 0x1C,
            PacketErrorChecking = 0x20,
            ReceiveData = 0x24,
            TransmitData = 0x28,
        }

        // ---- the allow-list (RUL-16, RUL-17): one row per field, every bit 31:0 of every register ---------------

        private sealed class Row
        {
            public Row(string register, long offset, string section, string name, int msb, int lsb, uint[] allowed,
                string behavesAs, int page)
            {
                Register = register;
                Offset = offset;
                Section = section;
                Name = name;
                Msb = msb;
                Lsb = lsb;
                Allowed = allowed;
                BehavesAs = behavesAs;
                Page = page;
                Mask = Msb - Lsb == 31 ? uint.MaxValue : ((1u << (Msb - Lsb + 1)) - 1) << Lsb;
            }

            public uint Of(uint value)
            {
                return (value & Mask) >> Lsb;
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
            public uint Mask { get; }
            public uint[] Allowed { get; }
            public string BehavesAs { get; }
            public int Page { get; }
        }

        private static readonly uint[] Any = null;
        private static readonly uint[] Zero = { 0 };

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        // Generated by tests/1306/fields.py from RM0433 Rev 9 §47.9 (pp. 2001-2014; tests/1306/data), with the classes ruled
        // 2026-11-25T05:06Z.
        private static readonly Row[] Fields =
        {
            R("I2C_CR1", 0x000, "§47.9.1", "Res31_24", 31, 24, Zero, "reserved", "reserved: must be kept at reset value", 2001),
            R("I2C_CR1", 0x000, "§47.9.1", "PECEN", 23, 23, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "ALERTEN", 22, 22, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "SMBDEN", 21, 21, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "SMBHEN", 20, 20, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "GCEN", 19, 19, Zero, "tag", "0 only: a stock tag; general call (target mode) is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "WUPEN", 18, 18, Zero, "tag", "0 only: a stock tag; wake-up from Stop is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "NOSTRETCH", 17, 17, Zero, "acknowledged by name", "0 only: the stock model stores it; target (slave) mode is not modeled; protected under PE (p2002): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2); read for 0x0: written 0 only: no target mode, no clock-stretch disable, no receive DMA, no STOP interrupt, alike on both sides (TVI-WO-1206 A21; p2002, p2011; STM32F7_I2C.cs L136; STM32F7_I2C.cs L238-247)", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "SBC", 16, 16, Zero, "tag", "0 only: a stock tag; target (slave) mode is not modeled", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "RXDMAEN", 15, 15, Zero, "acknowledged by name", "0 only: the stock model stores it and raises DmaReceive; the DMA side of I2C is not modeled (TVI-WO-1306 NOT); read for 0x0: written 0 only: no target mode, no clock-stretch disable, no receive DMA, no STOP interrupt, alike on both sides (TVI-WO-1206 A21; p604, p2002; STM32F7_I2C.cs L134; STM32F7_I2C.cs L469)", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "TXDMAEN", 14, 14, Zero, "tag", "0 only: a stock tag; the DMA side of I2C is not modeled (TVI-WO-1306 NOT)", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "Res13", 13, 13, Zero, "reserved", "reserved: must be kept at reset value", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "ANFOFF", 12, 12, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag; ruled 2026-11-25T05:06Z Q5); the twin's bus carries no noise to filter; protected under PE (p2002): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2002),
            R("I2C_CR1", 0x000, "§47.9.1", "DNF", 11, 8, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag; ruled 2026-11-25T05:06Z Q5); the twin's bus carries no noise to filter, but DNF x tI2CCLK adds to each tSYNC of the NACK's SCL period (p1971; TVI-WO-1306a); protected under PE (p2003): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "ERRIE", 7, 7, Any, "stock, wrong (the twin raises no bus error (BERR, ARLO), where silicon can on a misplaced START or STOP or a lost arbitration (p1997-1998); unaffected: the twin's bus has one controller and no line faults, and the other four error sources (OVR, TIMEOUT, PECERR, ALERT) need modes their own rows allow only at 0)", "the stock model keeps it; no bus error (BERR, ARLO, OVR, TIMEOUT, PECERR, ALERT) is ever raised, so it never fires (ArduPilot sets it; ruled 2026-11-25T05:06Z Q11); read for 0x0,0x1 (TVI-WO-1206; p1997, p1998, p2003, p2004, p2009, p2010; STM32F7_I2C.cs L128-129; PeripheralRegisterExtensions.cs L455-458)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "TCIE", 6, 6, Any, "acknowledged by name", "the stock model: TC and TCR raise the event interrupt; read for 0x0,0x1: the interrupt is the flag AND the enable on both sides; ArduPilot takes all three (CR1 0xD3, 0xD7), each of the armed run's 12,750 handler entries with an enabled flag set; when the flags fire is I2C_TXDR.TXDATA's and I2C_CR2.AUTOEND's matter (TVI-WO-1206 A22; p1975, p1979, p2003, p2011; STM32F7_I2C.cs L127; STM32F7_I2C.cs L144)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "STOPIE", 5, 5, Any, "acknowledged by name", "the stock model: STOPF raises the event interrupt; and the guard's STOPF after a NACK (TVI-WO-1306a); read for 0x0: written 0 only: no target mode, no clock-stretch disable, no receive DMA, no STOP interrupt, alike on both sides (TVI-WO-1206 A21; p2003; STM32F7_I2C.cs L126; STM32F7_I2C.cs L464)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "NACKIE", 4, 4, Any, "modeled by the guard", "the guard: NACKF raises the event interrupt (Table 400 p1999; the stock model has NACKIE && false); the NACK: see I2C_CR2.START", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "ADDRIE", 3, 3, Zero, "acknowledged by name", "0 only: the stock model stores it; target (slave) mode is not modeled; read for 0x0: written 0 only: no target mode, no clock-stretch disable, no receive DMA, no STOP interrupt, alike on both sides (TVI-WO-1206 A21; p2003, p2011; STM32F7_I2C.cs L124; STM32F7_I2C.cs L466)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "RXIE", 2, 2, Any, "acknowledged by name", "the stock model: RXNE on a read raises the event interrupt; read for 0x0,0x1: the interrupt is the flag AND the enable on both sides; ArduPilot takes all three (CR1 0xD3, 0xD7), each of the armed run's 12,750 handler entries with an enabled flag set; when the flags fire is I2C_TXDR.TXDATA's and I2C_CR2.AUTOEND's matter (TVI-WO-1206 A22; p1979, p2003, p2011; STM32F7_I2C.cs L123; STM32F7_I2C.cs L144)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "TXIE", 1, 1, Any, "acknowledged by name", "the stock model: TXIS raises the event interrupt; read for 0x0,0x1: the interrupt is the flag AND the enable on both sides; ArduPilot takes all three (CR1 0xD3, 0xD7), each of the armed run's 12,750 handler entries with an enabled flag set; when the flags fire is I2C_TXDR.TXDATA's and I2C_CR2.AUTOEND's matter (TVI-WO-1206 A22; p1975, p2003, p2011; STM32F7_I2C.cs L122; STM32F7_I2C.cs L144)", 2003),
            R("I2C_CR1", 0x000, "§47.9.1", "PE", 0, 0, Any, "modeled by the guard", "the stock model; set (0 -> 1), the RCC guard's kernel-clock query (TVI-I2C-KERNEL; ruled 2026-11-25T05:06Z Q3), and set as the very next access after the one that cleared it is TVI-I2C-RESET, unmodeled (p1960: write 0, check, write 1; Q6); cleared (1 -> 0), the software reset (p1959): the guard resets the owned stock instance (every ISR flag, TXE = 1, its byte queues) and restores the configuration (CR1, OAR1, OAR2, CR2 but START and STOP; TIMINGR is its own) (Q6); a write that clears it is judged as written with PE = 0 (Q2)", 2003),
            R("I2C_CR2", 0x004, "§47.9.2", "Reserved", 31, 27, Zero, "reserved", "reserved: must be kept at reset value", 2004),
            R("I2C_CR2", 0x004, "§47.9.2", "PECBYTE", 26, 26, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2004),
            R("I2C_CR2", 0x004, "§47.9.2", "AUTOEND", 25, 25, Any, "stock, wrong (a STOP does not clear TC in the stock model, where silicon clears it when START or STOP is set (p2011); and TC (AUTOEND = 0) or STOPF (1) comes at the last RXDR read, not at the last byte received; unaffected: the generated boards' polling sees TC or STOPF at once, as I2C_TXDR.TXDATA's row says, and takes the same branches; ArduPilot, after each software STOP, clears TCIE in its next write and PE in the one after (CR2 0x6448C, CR1 0x97, 0x96: the armed run), and PE = 0 clears TC on both sides (p2011), with no event interrupt taken between (the armed run, and the fixture 507 times); the generated boards write CR1 only 0x0 and 0x1 (no interrupts) and never a software STOP (spike/1206/data/main-8e15435/gen-writes.tsv))", "the stock model: with AUTOEND = 1 a STOP after NBYTES (STOPF); with AUTOEND = 0 TC is set and the controller waits for a START or a STOP (p2004-2005, p2011); a NACKed address's STOP comes whatever AUTOEND (p1975; TVI-WO-1306a); read for 0x0,0x1 (TVI-WO-1206; p1962, p1975, p1978, p1979, p2004, p2005, p2011; STM32F7_I2C.cs L157; STM32F7_I2C.cs L426-434)", 2004),
            R("I2C_CR2", 0x004, "§47.9.2", "RELOAD", 24, 24, Zero, "acknowledged by name", "0 only: the stock model stores it and extends a transfer, used by no firmware (TVI-WO-1306.measure.md §2.1), so held at 0, not audited (ruled 2026-11-25T05:06Z Q11); read for 0x0: written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides (TVI-WO-1206 A8; p2004, p2010; STM32F7_I2C.cs L156; STM32F7_I2C.cs L436-441)", 2004),
            R("I2C_CR2", 0x004, "§47.9.2", "NBYTES", 23, 16, Any, "acknowledged by name", "the stock model; a START with NBYTES = 0 and RELOAD = 0 is TVI-I2C-TRANSFER, unmodeled, never passed on: the stock model hands the device an empty write and Renode aborts (measure.md §2.3 I8b; ruled 2026-11-25T05:06Z Q8); changed while a transfer is open (START passed, no TC, TCR or STOPF since, no STOP or PE clear) it is TVI-I2C-TRANSFER, unmodeled (\"Changing these bits when the START bit is set is not allowed\", p2004-2006; ruled 2026-11-25T05:06Z Q8); read for 0x0,0x1,0x2,0x3,0x6: each transfer goes to the address written, in the direction written, for NBYTES bytes (8,339 RXDR reads = 1,378 x 6 + 71 x 1 in the armed run; no START with NBYTES 0), as silicon's; at 0x90 ten generated boards address a TMP117 the platform has no model for, so the twin NACKs where silicon would ACK: the platform's missing device (TVI-WO-1306 Q9), not this field (TVI-WO-1206 A23; p1975, p1979, p2004, p2011; STM32F7_I2C.cs L155; STM32F7_I2C.cs L353-361)", 2004),
            R("I2C_CR2", 0x004, "§47.9.2", "NACK", 15, 15, Zero, "tag", "0 only: a stock tag; target (slave) mode is not modeled", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "STOP", 14, 14, Any, "modeled by the guard", "the stock model: STOPF; it closes an open transfer; written while a NACKed address's automatic STOP is pending, absorbed by the guard: silicon clears it \"when a STOP condition is detected\" (p2005; ruled 2026-11-26T00:22Z Q4)", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "START", 13, 13, Any, "modeled by the guard", "the stock model starts the transfer; with PE = 0, or while a transfer is open, it is TVI-I2C-TRANSFER, unmodeled (p2003 \"Peripheral disabled\"; ruled 2026-11-25T05:06Z Q8); while a NACKed address's automatic STOP is pending it is TVI-I2C-TRANSFER, unmodeled (ruled 2026-11-26T00:22Z Q4); a write START to an address with no device is NACKed by the guard: NACKF nine SCL periods after the START (p1955; the period by p1971 from TIMINGR, DNF and the RCC guard's KernelHz, each tSYNC at its stated floor; inferred: the START's hold, tHD:STA, which SCLH sets (p1973), is not added), STOPF one SCL period after NACKF (inferred: RM0433 gives no figure), whatever AUTOEND (\"a STOP condition is automatically sent\", p1975), the guard writing the stock line \"Unknown slave at address N.\" under its own name (TVI-WO-1306a.measure.md; ruled 2026-11-26T00:22Z Q1-Q3, Q5, Q8); with no kernel-clock frequency it is TVI-I2C-NACK, unmodeled (Q2); a read START to an address with no device is TVI-I2C-ABSENT, unmodeled: RM0433 states the automatic STOP only for a write (Q6)", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "HEAD10R", 12, 12, Zero, "tag", "0 only: a stock tag; 10-bit addressing is not modeled", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "ADD10", 11, 11, Zero, "acknowledged by name", "0 only: the stock model stores it; 10-bit addressing is not modeled; read for 0x0: written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides (TVI-WO-1206 A8; p2005, p2006; STM32F7_I2C.cs L150; STM32F7_I2C.cs L346)", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "RD_WRN", 10, 10, Any, "acknowledged by name", "the stock model; changed while a transfer is open (START passed, no TC, TCR or STOPF since, no STOP or PE clear) it is TVI-I2C-TRANSFER, unmodeled (\"Changing these bits when the START bit is set is not allowed\", p2004-2006; ruled 2026-11-25T05:06Z Q8); read for 0x0,0x1: each transfer goes to the address written, in the direction written, for NBYTES bytes (8,339 RXDR reads = 1,378 x 6 + 71 x 1 in the armed run; no START with NBYTES 0), as silicon's; at 0x90 ten generated boards address a TMP117 the platform has no model for, so the twin NACKs where silicon would ACK: the platform's missing device (TVI-WO-1306 Q9), not this field (TVI-WO-1206 A23; p1975, p2005; STM32F7_I2C.cs L149; STM32F7_I2C.cs L353-366)", 2005),
            R("I2C_CR2", 0x004, "§47.9.2", "SADD", 9, 0, Any, "acknowledged by name", "the stock model, 7-bit (SADD[7:1]; bits 9, 8 and 0 are don't care, p2006); changed while a transfer is open (START passed, no TC, TCR or STOPF since, no STOP or PE clear) it is TVI-I2C-TRANSFER, unmodeled (\"Changing these bits when the START bit is set is not allowed\", p2004-2006; ruled 2026-11-25T05:06Z Q8); read for the 9 distinct values written (spike/1206/data/items.tsv): each transfer goes to the address written, in the direction written, for NBYTES bytes (8,339 RXDR reads = 1,378 x 6 + 71 x 1 in the armed run; no START with NBYTES 0), as silicon's; at 0x90 ten generated boards address a TMP117 the platform has no model for, so the twin NACKs where silicon would ACK: the platform's missing device (TVI-WO-1306 Q9), not this field (TVI-WO-1206 A23; p1975, p2006; STM32F7_I2C.cs L148; STM32F7_I2C.cs L346-351)", 2006),
            R("I2C_OAR1", 0x008, "§47.9.3", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 2006),
            R("I2C_OAR1", 0x008, "§47.9.3", "OA1EN", 15, 15, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2006),
            R("I2C_OAR1", 0x008, "§47.9.3", "Res14_11", 14, 11, Zero, "reserved", "reserved: must be kept at reset value", 2006),
            R("I2C_OAR1", 0x008, "§47.9.3", "OA1MODE", 10, 10, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2006),
            R("I2C_OAR1", 0x008, "§47.9.3", "OA1", 9, 0, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2006),
            R("I2C_OAR2", 0x00C, "§47.9.4", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 2007),
            R("I2C_OAR2", 0x00C, "§47.9.4", "OA2EN", 15, 15, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2007),
            R("I2C_OAR2", 0x00C, "§47.9.4", "Res14_11", 14, 11, Zero, "reserved", "reserved: must be kept at reset value", 2007),
            R("I2C_OAR2", 0x00C, "§47.9.4", "OA2MSK", 10, 8, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2007),
            R("I2C_OAR2", 0x00C, "§47.9.4", "OA2", 7, 1, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it; target (slave) mode is not modeled", 2007),
            R("I2C_OAR2", 0x00C, "§47.9.4", "Res0", 0, 0, Zero, "reserved", "reserved: must be kept at reset value", 2007),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "PRESC", 31, 28, Any, "modeled by the guard", "kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md §2.3 I2; ruled 2026-11-25T05:06Z Q5); the guard times a NACK by it (p1971; TVI-WO-1306a), and nothing else; protected under PE (p2008): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2008),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "Reserved", 27, 24, Zero, "reserved", "reserved: must be kept at reset value", 2008),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "SCLDEL", 23, 20, Any, "modeled by the guard", "kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md §2.3 I2; ruled 2026-11-25T05:06Z Q5); the guard times a NACK by it (p1971; TVI-WO-1306a), and nothing else; protected under PE (p2008): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2008),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "SDADEL", 19, 16, Any, "modeled by the guard", "kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md §2.3 I2; ruled 2026-11-25T05:06Z Q5); the guard times a NACK by it (p1971; TVI-WO-1306a), and nothing else; protected under PE (p2008): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2008),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "SCLH", 15, 8, Any, "modeled by the guard", "kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md §2.3 I2; ruled 2026-11-25T05:06Z Q5); the guard times a NACK by it (p1971; TVI-WO-1306a), and nothing else; protected under PE (p2008): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2008),
            R("I2C_TIMINGR", 0x010, "§47.9.5", "SCLL", 7, 0, Any, "modeled by the guard", "kept by the guard and served on read: the stock model has every TIMINGR field as a tag that reads 0 (measure.md §2.3 I2; ruled 2026-11-25T05:06Z Q5); the guard times a NACK by it (p1971; TVI-WO-1306a), and nothing else; protected under PE (p2008): changed with PE = 1 it is TVI-I2C-PE, unmodeled: RM0433 says it can be programmed only with PE = 0 and not what silicon does with such a write (ruled 2026-11-25T05:06Z Q2)", 2008),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "TEXTEN", 31, 31, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "Res30_28", 30, 28, Zero, "reserved", "reserved: must be kept at reset value", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "TIMEOUTB", 27, 16, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "TIMOUTEN", 15, 15, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "Res14_13", 14, 13, Zero, "reserved", "reserved: must be kept at reset value", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "TIDLE", 12, 12, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0", 2009),
            R("I2C_TIMEOUTR", 0x014, "§47.9.6", "TIMEOUTA", 11, 0, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x14; SMBus timeouts are not modeled; reads 0", 2009),
            R("I2C_ISR", 0x018, "§47.9.7", "Res31_24", 31, 24, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2009),
            R("I2C_ISR", 0x018, "§47.9.7", "ADDCODE", 23, 17, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "DIR", 16, 16, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "BUSY", 15, 15, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "Res14", 14, 14, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "ALERT", 13, 13, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "TIMEOUT", 12, 12, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "PECERR", 11, 11, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "OVR", 10, 10, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "ARLO", 9, 9, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "BERR", 8, 8, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "TCR", 7, 7, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2010),
            R("I2C_ISR", 0x018, "§47.9.7", "TC", 6, 6, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "STOPF", 5, 5, Any, "read-only", "read-only: a write is ignored; the stock model's, and the guard's one SCL period after a NACK's NACKF (inferred: RM0433 gives no figure; ruled 2026-11-26T00:22Z Q3)", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "NACKF", 4, 4, Any, "modeled by the guard", "read-only (a write is ignored), set and served by the guard: a write START to an address with no device is NACKed by the guard: NACKF nine SCL periods after the START (p1955; the period by p1971 from TIMINGR, DNF and the RCC guard's KernelHz, each tSYNC at its stated floor; inferred: the START's hold, tHD:STA, which SCLH sets (p1973), is not added), STOPF one SCL period after NACKF (inferred: RM0433 gives no figure), whatever AUTOEND (\"a STOP condition is automatically sent\", p1975), the guard writing the stock line \"Unknown slave at address N.\" under its own name (TVI-WO-1306a.measure.md; ruled 2026-11-26T00:22Z Q1-Q3, Q5, Q8); with no kernel-clock frequency it is TVI-I2C-NACK, unmodeled (Q2); a read START to an address with no device is TVI-I2C-ABSENT, unmodeled: RM0433 states the automatic STOP only for a write (Q6); cleared by NACKCF (p2012) and at PE = 0 (p2011); a NACK in the data phase is not modeled: no device model in Renode NACKs a byte (ruled 2026-11-26T00:22Z Q7)", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "ADDR", 3, 3, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "RXNE", 2, 2, Any, "read-only", "read-only: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "TXIS", 1, 1, Zero, "inherited, unaudited (acknowledged by name)", "0 only: writing 1 generates TXIS with NOSTRETCH = 1 only, target (slave) mode is not modeled", 2011),
            R("I2C_ISR", 0x018, "§47.9.7", "TXE", 0, 0, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model's TXE always reads 1 (it holds no TXDR), so a flush (1 written) is not modeled; set at PE = 0 (p2011)", 2011),
            R("I2C_ICR", 0x01C, "§47.9.8", "Reserved", 31, 14, Zero, "reserved", "reserved: must be kept at reset value", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "ALERTCF", 13, 13, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "TIMOUTCF", 12, 12, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "PECCF", 11, 11, Zero, "tag", "0 only: a stock tag; SMBus is not modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "OVRCF", 10, 10, Zero, "tag", "0 only: a stock tag; no bus error is modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "ARLOCF", 9, 9, Zero, "tag", "0 only: a stock tag; no bus error is modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "BERRCF", 8, 8, Zero, "tag", "0 only: a stock tag; no bus error is modeled", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "Res6", 6, 6, Any, "acknowledged by name", "ChibiOS writes 1 here (ICR = 0x2, 0x4 or 0x40, the TXIS, RXNE and TC positions of ISR, at every ICR write it makes, TVI-WO-1306.measure.md §2.1); RM0433 marks the bits reserved, \"must be kept at reset value\" (p2012); a clear register: the write clears nothing (ruled 2026-11-25T05:06Z Q10); read for 0x0,0x1: by the architect's ruling of 2026-11-25T05:06Z Q10 (a clear register: the write clears nothing), on field evidence: p2012 says only \"Reserved, must be kept at reset value\", so RM0433 gives no behaviour for the 1s ChibiOS writes here (ICR = 0x2, 0x4); the twin ignores the bits, and a 0 behaves alike on both sides. ChibiOS flies with these writes on silicon, which is field evidence, not the manual. Bit 6 alike, as ruled 2026-11-27T00:43Z: ChibiOS writes ICR = 0x40 (TC's position in ISR) 2,901 times in the armed run, and p2012 gives it no behaviour either. Bench row BR2 (TVI-WO-1206 FEEDBACK §5): read I2C_ISR after ICR = 0x2, 0x4 and 0x40 on the Nucleo (TVI-WO-1206 A12; p2011, p2012; STM32F7_I2C.cs L267-269; STM32F7_I2C.cs L287)", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "STOPCF", 5, 5, Any, "modeled by the guard", "the stock model clears STOPF; the guard clears the NACK's own STOPF (p2012)", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "NACKCF", 4, 4, Any, "modeled by the guard", "the guard clears NACKF (p2012); the NACK: see I2C_CR2.START", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "ADDRCF", 3, 3, Zero, "acknowledged by name", "0 only: target (slave) mode is not modeled (in controller mode the stock model clears TXIS, where silicon clears ADDR and a pending START, p2012); read for 0x0: written 0 only: 7-bit addressing, no reload, and a 0 written to ADDRCF clears nothing, alike on both sides (TVI-WO-1206 A8; p101, p2011, p2012; STM32F7_I2C.cs L267-277; PeripheralRegister.cs L666-669)", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "Res2_1", 2, 1, Any, "acknowledged by name", "ChibiOS writes 1 here (ICR = 0x2, 0x4 or 0x40, the TXIS, RXNE and TC positions of ISR, at every ICR write it makes, TVI-WO-1306.measure.md §2.1); RM0433 marks the bits reserved, \"must be kept at reset value\" (p2012); a clear register: the write clears nothing (ruled 2026-11-25T05:06Z Q10); read for 0x0,0x1,0x2,0x4: by the architect's ruling of 2026-11-25T05:06Z Q10 (a clear register: the write clears nothing), on field evidence: p2012 says only \"Reserved, must be kept at reset value\", so RM0433 gives no behaviour for the 1s ChibiOS writes here (ICR = 0x2, 0x4); the twin ignores the bits, and a 0 behaves alike on both sides. ChibiOS flies with these writes on silicon, which is field evidence, not the manual. Bit 6 alike, as ruled 2026-11-27T00:43Z: ChibiOS writes ICR = 0x40 (TC's position in ISR) 2,901 times in the armed run, and p2012 gives it no behaviour either. Bench row BR2 (TVI-WO-1206 FEEDBACK §5): read I2C_ISR after ICR = 0x2, 0x4 and 0x40 on the Nucleo (TVI-WO-1206 A12; p101, p2012; STM32F7_I2C.cs L268; PeripheralRegister.cs L289-292)", 2012),
            R("I2C_ICR", 0x01C, "§47.9.8", "Res0", 0, 0, Zero, "reserved", "reserved: must be kept at reset value", 2012),
            R("I2C_PECR", 0x020, "§47.9.9", "Reserved", 31, 8, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2013),
            R("I2C_PECR", 0x020, "§47.9.9", "PEC", 7, 0, Any, "read-only", "read-only: reads 0 (PECEN 0 only); a write is ignored", 2013),
            R("I2C_RXDR", 0x024, "§47.9.10", "Reserved", 31, 8, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2013),
            R("I2C_RXDR", 0x024, "§47.9.10", "RXDATA", 7, 0, Any, "read-only", "read-only: the stock model's received byte; a write is ignored", 2013),
            R("I2C_TXDR", 0x028, "§47.9.11", "Reserved", 31, 8, Zero, "reserved", "reserved: must be kept at reset value", 2014),
            R("I2C_TXDR", 0x028, "§47.9.11", "TXDATA", 7, 0, Any, "stock, wrong (TXIS stays 1 from START to the last byte and TXE always reads 1, with no bus time (p2011, p2014); unaffected: the bytes, their order and their addressing are those silicon sends; a firmware polling I2C_ISR sees TXIS, TXE and TC at once where silicon sets them a byte time later (the generated boots poll so: genlog hse-crystal-16, i2c1 at 0.000042 s), so it polls fewer times and takes the same branches; no rule of the twin times the bus)", "the stock model sends the byte; written with PE = 0 it is TVI-I2C-TRANSFER, unmodeled (ruled 2026-11-25T05:06Z Q8); read for every value (12 distinct written) (TVI-WO-1206; p1961, p1975, p2011, p2014; STM32F7_I2C.cs L301-303; STM32F7_I2C.cs L398-408)", 2014),
        };
    }
}
