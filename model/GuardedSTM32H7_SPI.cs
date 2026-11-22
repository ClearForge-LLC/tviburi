// Renode's STM32H7_SPI with one refusal guard: TVI-SPI-MODF (TVI-WO-0100 spike).
//
// Insertion point: this subclass re-implements the bus access interfaces
// (IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral), so every bus write reaches the
// guard first; a modeled write is passed to the stock model unchanged (base.Write*). The stock
// methods are `virtual final` and cannot be overridden; interface re-implementation is the seam
// (measured: all three widths dispatch here, and a monitor SetHookBeforePeripheralWrite does not
// displace it). The stock model is not modified.
//
// What the stock model does not model (measured on Renode 1.17.0, Infrastructure cd4b002a):
//   - CFG2.SSM, CFG2.SSIOP, CFG2.SSOE and CR1.SSI are tags: written values are dropped and read
//     back as 0, so the guard keeps its own copy of the last 32-bit CFG2 value written and reads
//     SSI from the CR1 write it is evaluating;
//   - mode fault: SPE set in master mode with the internal SS input at its active level leaves
//     SR.MODF clear, CR1.SPE set and CFG2.MASTER set, where silicon sets MODF and clears both;
//   - slave mode: writing CFG2.MASTER=0 is rejected with an error log and MASTER stays 1.
//
// The rule, evaluated on every 32-bit CR1 write with SPE=1, passes only when a mode fault is
// provably impossible; anything it cannot decide refuses:
//   MASTER=1, SSM=1, SSIOP=0, SSI=1  -> pass (internal SS inactive; the stock model applies)
//   MASTER=1, SSM=1, SSIOP=0, SSI=0  -> refuse: mode fault
//   MASTER=1, SSM=1, SSIOP=1         -> refuse: SS polarity not handled by this guard
//   MASTER=1, SSM=0                  -> refuse: SS input is a pin level the twin does not model
//   MASTER=0                         -> refuse: slave mode not modeled
// A sub-word write that overlaps CR1 or CFG2 refuses: the guard evaluates 32-bit writes only.
// Writes the guard refuses are not passed to the model.

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_SPI : STM32H7_SPI, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral
    {
        public GuardedSTM32H7_SPI(IMachine machine) : base(machine)
        {
            this.machine = machine;
            Refusal.Armed(nameof(GuardedSTM32H7_SPI) + "/" + Rule);
        }

        public override void Reset()
        {
            base.Reset();
            cfg2 = 0;
        }

        public new void WriteDoubleWord(long offset, uint value)
        {
            if(offset == Cr1Offset && (value & Cr1Spe) != 0 && !ModeFaultImpossible(value, out var bit, out var condition))
            {
                Refusal.Refuse(machine, this, Rule, "SPI_CR1 / SPI_CFG2", bit, condition, Citation,
                    "CR1 written=0x" + value.ToString("X8") + ", " + Cfg2State(), offset, value, 32);
                return;
            }
            if(offset == Cfg2Offset)
            {
                cfg2 = value;
            }
            base.WriteDoubleWord(offset, value);
        }

        public new void WriteWord(long offset, ushort value)
        {
            if(OverlapsGuarded(offset, 2))
            {
                RefuseSubWord(offset, value, 16);
                return;
            }
            base.WriteWord(offset, value);
        }

        public new void WriteByte(long offset, byte value)
        {
            if(OverlapsGuarded(offset, 1))
            {
                RefuseSubWord(offset, value, 8);
                return;
            }
            base.WriteByte(offset, value);
        }

        private bool ModeFaultImpossible(uint newCr1, out string bit, out string condition)
        {
            if((cfg2 & Cfg2Master) == 0)
            {
                bit = "CFG2.MASTER (bit 22) = 0";
                condition = "CR1.SPE set in slave mode: the stock STM32H7_SPI model supports master mode only";
                return false;
            }
            if((cfg2 & Cfg2Ssm) == 0)
            {
                bit = "CFG2.SSM (bit 26) = 0";
                condition = "CR1.SPE set in master mode with hardware SS management: the SS input is a pin level the twin does not model, so a mode fault (SR.MODF) cannot be ruled out";
                return false;
            }
            if((cfg2 & Cfg2Ssiop) != 0)
            {
                bit = "CFG2.SSIOP (bit 28) = 1";
                condition = "CR1.SPE set in master mode with SS active-high polarity: this guard does not handle SSIOP=1, so a mode fault (SR.MODF) cannot be ruled out";
                return false;
            }
            if((newCr1 & Cr1Ssi) == 0)
            {
                bit = "CR1.SSI (bit 12) = 0 with CR1.SPE (bit 0) = 1";
                condition = "CR1.SPE set in master mode with software SS management and the internal SS input at its active level (SSM=1, SSI=0, SSIOP=0): silicon sets SR.MODF (bit 9) and clears CR1.SPE and CFG2.MASTER; the stock model implements none of these";
                return false;
            }
            bit = null;
            condition = null;
            return true;
        }

        private void RefuseSubWord(long offset, ulong value, int width)
        {
            Refusal.Refuse(machine, this, Rule, offset < Cr1Offset + 4 ? "SPI_CR1" : "SPI_CFG2", "sub-word access",
                width + "-bit write overlapping CR1 or CFG2: the mode-fault guard evaluates 32-bit writes only",
                Citation, Cfg2State(), offset, value, width);
        }

        private static bool OverlapsGuarded(long offset, int size)
        {
            return Overlaps(offset, size, Cr1Offset) || Overlaps(offset, size, Cfg2Offset);
        }

        private static bool Overlaps(long offset, int size, long register)
        {
            return offset < register + 4 && register < offset + size;
        }

        private string Cfg2State()
        {
            return "CFG2 last written=0x" + cfg2.ToString("X8");
        }

        private uint cfg2;
        private readonly IMachine machine;

        private const string Rule = "TVI-SPI-MODF";
        private const string Citation = "RM0433 (STM32H742/743/753/750 reference manual), SPI chapter: mode fault (MODF) error condition; SPI_CR1, SPI_CFG2 and SPI_SR register descriptions. Section number and revision not verified: no copy of the manual was available offline (TVI-WO-0100 FEEDBACK)";
        private const long Cr1Offset = 0x00;
        private const long Cfg2Offset = 0x0C;
        private const uint Cr1Spe = 1u << 0;
        private const uint Cr1Ssi = 1u << 12;
        private const uint Cfg2Master = 1u << 22;
        private const uint Cfg2Ssm = 1u << 26;
        private const uint Cfg2Ssiop = 1u << 28;
    }
}
