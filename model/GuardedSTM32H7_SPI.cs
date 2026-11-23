// Renode's STM32H7_SPI with refusal guards (TVI-WO-0100, TVI-WO-0100a). The rules, their truth
// table and every page cited here: docs/work-orders/TVI-WO-0100a.rm0433.md. All citations are to
// ST RM0433 Rev 9, chapter 50.
//
// Insertion point (RUL-07): this subclass re-implements the bus access interfaces, so every bus
// write reaches the guard first; a write the guard passes goes to the stock model unchanged. The
// stock methods are `virtual final`. The stock model is not modified.
//
// What the stock model does not model (measured on Renode 1.17.0+…aet1605): CFG2.SSM, SSIOP, SSOE
// and CR1.SSI are tags (written values dropped, read as 0); CR1.IOLOCK is dropped; there is no mode
// fault; slave mode is rejected; CFG2.MASTER always reads 1. The guard therefore keeps its own copies
// of the last CR1 and CFG2 values it passed.
//
// Reads (modeled, so that firmware that reads a register, changes a field and writes it back computes
// the word silicon would; adversarial pass, TVI-WO-0100a): a CFG2 read returns the guard's copy
// (every CFG2 field is software-written, rw, reset 0: RM0433 Rev 9 §50.11.4 p2221-2222; the guard
// refuses every CFG2 write silicon would drop, and any mode fault refuses before it could clear MASTER).
// A CR1 read returns the stock value with SSI taken from the copy (SSI is rw, p2215). A CR1 read while
// the IOLOCK latch is set refuses: silicon reads IOLOCK=1 until SPE goes from 1 to 0, which is not
// modeled. A sub-word or unaligned read overlapping a guarded register refuses.
//
// Rules. A refused write is not passed to the model.
//   TVI-SPI-MODF   a CR1 write with SPE=1 passes only if the copies show master, Motorola mode
//                  (SP=000), software SS (SSM=1), SSIOP=0 and SSI=1 (internal SS inactive), and no
//                  master state other than that since reset (D1: RM0433 does not settle whether
//                  MODF is evaluated while SPE=0, so the guard takes the side that cannot pass
//                  falsely). Anything else refuses, by the first failing branch.
//   TVI-SPI-IOLOCK after a CR1 write carrying IOLOCK, every CFG2 write refuses until reset.
//   TVI-SPI-CFG2-SPE a CFG2 write while the last CR1 passed had SPE=1 refuses (write-protected).
//   TVI-SPI-I2SMOD an I2SCFGR write with I2SMOD=1 refuses (I2S/PCM mode is not modeled).
//   TVI-SPI-WIDTH  any access overlapping CR1, CFG2 or I2SCFGR other than an aligned 32-bit one refuses.
//   TVI-LOAD       (Records) the model version could not be computed: every access refuses.

using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_SPI : STM32H7_SPI, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IDisposable
    {
        public GuardedSTM32H7_SPI(IMachine machine) : base(machine)
        {
            records = new Records(machine, this, nameof(GuardedSTM32H7_SPI));
        }

        // The base constructor calls this before `records` exists (measured): touch only the copies.
        public override void Reset()
        {
            base.Reset();
            cr1 = 0;
            cfg2 = 0;
            ioLock = false;
            history = null;
        }

        public void Dispose()
        {
            records.End();
        }

        public new void WriteDoubleWord(long offset, uint value)
        {
            if(RefusedAccess(offset, value, 32, "write"))
            {
                return;
            }
            switch(offset)
            {
            case Cr1Offset:
                if((value & Cr1Spe) != 0 && !EnablePasses(value))
                {
                    return;
                }
                cr1 = value;
                ioLock |= (value & Cr1IoLock) != 0;
                NoteMasterState();
                break;
            case Cfg2Offset:
                if(ioLock)
                {
                    records.Refuse("TVI-SPI-IOLOCK", "SPI_CFG2", "CR1.IOLOCK (bit 16) = 1",
                        "CFG2 written after IOLOCK was set: silicon keeps the old CFG2 (\"When this bit is set, the SPI_CFG2 register content cannot be modified\"), while the stock model drops IOLOCK and would take the write. Silicon clears IOLOCK when SPE goes from 1 to 0 (p2215); that is not modeled, so the lock holds until reset",
                        "RM0433 Rev 9 §50.11.1 p2215 (CR1.IOLOCK); §50.11.4 p2221 (CFG2 write protection)",
                        State(), offset, value, 32);
                    return;
                }
                if((cr1 & Cr1Spe) != 0)
                {
                    records.Refuse("TVI-SPI-CFG2-SPE", "SPI_CFG2", "CR1.SPE (bit 0) = 1",
                        "CFG2 written while the SPI is enabled: silicon write-protects CFG2 when SPE=1 and keeps the old value; the guard's copy would take it",
                        "RM0433 Rev 9 §50.11.1 p2217 (SPE); §50.11.4 p2221 (CFG2 write protection)",
                        State(), offset, value, 32);
                    return;
                }
                cfg2 = value;
                NoteMasterState();
                break;
            case I2sCfgrOffset:
                if((value & I2sMod) != 0)
                {
                    records.Refuse("TVI-SPI-I2SMOD", "SPI_I2SCFGR", "I2SCFGR.I2SMOD (bit 0) = 1",
                        "I2S/PCM mode selected: the guard models SPI mode only, where I2SMOD must be kept at 0",
                        "RM0433 Rev 9 §50.11.14 p2230 (\"kept at 0 in SPI mode\"), p2232 (I2SMOD, bit 0); §50.4.9 p2171 step 2(f)",
                        State(), offset, value, 32);
                    return;
                }
                break;
            }
            base.WriteDoubleWord(offset, value);
        }

        public new void WriteWord(long offset, ushort value)
        {
            if(!RefusedAccess(offset, value, 16, "write"))
            {
                base.WriteWord(offset, value);
            }
        }

        public new void WriteByte(long offset, byte value)
        {
            if(!RefusedAccess(offset, value, 8, "write"))
            {
                base.WriteByte(offset, value);
            }
        }

        public new uint ReadDoubleWord(long offset)
        {
            var value = base.ReadDoubleWord(offset);
            if(RefusedAccess(offset, value, 32, "read"))
            {
                return value;
            }
            switch(offset)
            {
            case Cr1Offset:
                if(ioLock)
                {
                    records.Refuse("TVI-SPI-IOLOCK", "SPI_CR1", "CR1.IOLOCK (bit 16), read",
                        "CR1 read while the guard's IOLOCK latch is set: silicon reads IOLOCK=1 until SPE goes from 1 to 0, the stock model reads 0, and the guard does not model the clear, so the value cannot be given",
                        "RM0433 Rev 9 §50.11.1 p2215 (CR1.IOLOCK)", State(), offset, value, 32);
                    return value;
                }
                return (value & ~Cr1Ssi) | (cr1 & Cr1Ssi);
            case Cfg2Offset:
                return cfg2;
            }
            return value;
        }

        public new ushort ReadWord(long offset)
        {
            var value = base.ReadWord(offset);
            RefusedAccess(offset, value, 16, "read");
            return value;
        }

        public new byte ReadByte(long offset)
        {
            var value = base.ReadByte(offset);
            RefusedAccess(offset, value, 8, "read");
            return value;
        }

        // TVI-LOAD, then TVI-SPI-WIDTH: true if the access was refused.
        private bool RefusedAccess(long offset, ulong value, int width, string access)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", RegisterAt(offset, width / 8), "none",
                    "access to a guarded peripheral whose model version could not be computed at load", Records.LoadCitation,
                    State(), offset, value, width);
                return true;
            }
            if(Guarded.Any(r => Overlaps(offset, width / 8, r.Offset)) && !(width == 32 && Guarded.Any(r => r.Offset == offset)))
            {
                records.Refuse("TVI-SPI-WIDTH", RegisterAt(offset, width / 8), "sub-word or unaligned " + access,
                    width + "-bit " + access + " at offset 0x" + offset.ToString("X") + " overlapping CR1, CFG2 or I2SCFGR: the guards evaluate aligned 32-bit accesses only (a twin limitation; RM0433 Rev 9 §50.11 states no access width for these registers)",
                    "none: a limitation of the guard, not a behaviour of the manual", State(), offset, value, width);
                return true;
            }
            return false;
        }

        // TVI-SPI-MODF at a CR1 write with SPE=1: the current state first, then the history.
        private bool EnablePasses(uint newCr1)
        {
            var failure = FirstFailingBranch(cfg2, newCr1);
            if(failure == null && history != null)
            {
                failure = new Branch("a master state other than the passing one, since reset: " + history,
                    "CR1.SPE set after the SPI was configured as a master in a state where a mode fault was possible. RM0433 does not settle whether MODF is evaluated while SPE=0: against a gate, p2184 states the fault for \"the master device\" with no enable condition, and its multimaster procedure raises MODF on switching into master mode, which is done at SPE=0; for a gate, p2217 says that at SPE=0 \"any SPI operation is stopped\", and p2171 configures CFG2 before SSI. A latched MODF would block SPE and clear MASTER. The guard takes the side that cannot pass falsely (northstar invariant 1); only silicon settles it",
                    "RM0433 Rev 9 §50.5.2 p2184; §50.11.1 p2217; §50.4.9 p2171");
            }
            if(failure == null)
            {
                return true;
            }
            records.Refuse(Modf, "SPI_CR1 / SPI_CFG2", failure.Bit, failure.Condition, failure.Citation,
                State(), Cr1Offset, newCr1, 32);
            return false;
        }

        // After a passed CR1 or CFG2 write: remember the first master state T1 would not pass.
        private void NoteMasterState()
        {
            if(history == null && (cfg2 & Cfg2Master) != 0 && FirstFailingBranch(cfg2, cr1) != null)
            {
                history = "CR1=0x" + cr1.ToString("X8") + ", CFG2=0x" + cfg2.ToString("X8");
            }
        }

        // The truth table (TVI-WO-0100a.rm0433.md §1): null only for row T1.
        private static Branch FirstFailingBranch(uint cfg2, uint cr1)
        {
            if((cfg2 & Cfg2Master) == 0)
            {
                return new Branch("CFG2.MASTER (bit 22) = 0",
                    "CR1.SPE set in slave mode: the stock STM32H7_SPI model supports master mode only (measured)",
                    "RM0433 Rev 9 §50.11.4 p2222 (CFG2.MASTER)");
            }
            if((cfg2 & Cfg2Sp) != 0)
            {
                return new Branch("CFG2.SP[2:0] (bits 21:19) != 000",
                    "CR1.SPE set in master mode with a serial protocol other than Motorola: in TI mode the setting of SSM, SSIOP and SSOE is not required, and the manual does not describe mode fault for a TI master, so a mode fault cannot be ruled out",
                    "RM0433 Rev 9 §50.5.1 p2181; §50.4.9 p2171 step 2(d); §50.11.4 p2222 (SP[2:0])");
            }
            if((cfg2 & Cfg2Ssm) == 0)
            {
                if((cfg2 & Cfg2Ssoe) == 0)
                {
                    return new Branch("CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 0",
                        "CR1.SPE set in master mode with hardware SS input: an active NSS pin level raises a mode fault, and the twin does not model the pin level",
                        "RM0433 Rev 9 §50.4.7 p2166; §50.11.4 p2221 (SSM, SSOE)");
                }
                return new Branch("CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 1",
                    "CR1.SPE set in master mode with hardware NSS output: RM0433 rules out a mode fault here (the NSS input is forced inactive), but the stock model drops SSOE, so NSS output is not modeled",
                    "RM0433 Rev 9 §50.11.4 p2221 (SSM, SSOE); §50.4.7 p2166");
            }
            if((cfg2 & Cfg2Ssiop) != 0)
            {
                return new Branch("CFG2.SSIOP (bit 28) = 1",
                    "CR1.SPE set in master mode with SS active-high polarity: the manual is inconsistent on which SSI level faults (\"equal to SSIOP\" on p2165 and in Figure 618 on p2167; \"pulled low\" on p2184), so a mode fault cannot be ruled out",
                    "RM0433 Rev 9 §50.4.7 p2165, Figure 618 p2167; §50.5.2 p2184");
            }
            if((cr1 & Cr1Ssi) == 0)
            {
                return new Branch("CR1.SSI (bit 12) = 0",
                    "master mode with software SS management and the internal SS input at its active level (SSM=1, SSI=0, SSIOP=0): silicon sets SR.MODF (bit 9), forces CR1.SPE to 0 and clears CFG2.MASTER; the stock model implements none of these",
                    "RM0433 Rev 9 §50.5.2 p2184; §50.4.7 p2165, Figure 618 p2167; §50.11.4 p2221 (SSM, SSIOP); §50.11.6 p2225 (SR.MODF)");
            }
            return null;
        }

        private string State()
        {
            return "CR1 last passed=0x" + cr1.ToString("X8") + ", CFG2 last passed=0x" + cfg2.ToString("X8")
                + ", IOLOCK latched=" + (ioLock ? "yes" : "no") + ", master history=" + (history ?? "none");
        }

        private static string RegisterAt(long offset, int size)
        {
            return Guarded.FirstOrDefault(r => Overlaps(offset, size, r.Offset)).Name ?? "SPI offset 0x" + offset.ToString("X");
        }

        private static bool Overlaps(long offset, int size, long register)
        {
            return offset < register + 4 && register < offset + size;
        }

        private sealed class Branch
        {
            public Branch(string bit, string condition, string citation)
            {
                Bit = bit;
                Condition = condition;
                Citation = citation;
            }

            public string Bit { get; }
            public string Condition { get; }
            public string Citation { get; }
        }

        private uint cr1;
        private uint cfg2;
        private bool ioLock;
        private string history;
        private readonly Records records;

        private const string Modf = "TVI-SPI-MODF";
        private const long Cr1Offset = 0x00;
        private const long Cfg2Offset = 0x0C;
        private const long I2sCfgrOffset = 0x50;
        private static readonly (long Offset, string Name)[] Guarded =
            { (Cr1Offset, "SPI_CR1"), (Cfg2Offset, "SPI_CFG2"), (I2sCfgrOffset, "SPI_I2SCFGR") };
        private const uint Cr1Spe = 1u << 0;
        private const uint Cr1Ssi = 1u << 12;
        private const uint Cr1IoLock = 1u << 16;
        private const uint Cfg2Sp = 7u << 19;
        private const uint Cfg2Master = 1u << 22;
        private const uint Cfg2Ssm = 1u << 26;
        private const uint Cfg2Ssiop = 1u << 28;
        private const uint Cfg2Ssoe = 1u << 29;
        private const uint I2sMod = 1u << 0;
    }
}
