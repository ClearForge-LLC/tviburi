// TVI-WO-1150 (vii) spike only, not a guard: candidate (b). A peripheral on (a slice of) the RCC's address range,
// beside the stock STM32H7_RCC, which stays declared and registered (so every stock entry's
// `rcc RegistrationPeripheralAlias.X` still resolves) but is moved off, or narrowed within, that range. Each access
// is logged, then forwarded to the stock instance at offset + baseOffset.
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Miscellaneous;

namespace Antmicro.Renode.Peripherals.Tviburi1150
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class RccGuard : IDoubleWordPeripheral, IKnownSize
    {
        public RccGuard(IMachine machine, STM32H7_RCC stock, long baseOffset = 0, long size = 0x400)
        {
            this.machine = machine;
            this.stock = stock;
            this.baseOffset = baseOffset;
            Size = size;
        }

        public long Size { get; }

        public void Reset()
        {
        }

        public uint ReadDoubleWord(long offset)
        {
            return stock.ReadDoubleWord(offset + baseOffset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            var pc = machine.SystemBus.TryGetCurrentCPU(out var cpu) ? "0x" + cpu.PC.RawValue.ToString("X8") : "none";
            this.Log(LogLevel.Warning, "TVI1150-SPIKE RccGuard write offset=0x{0:X} value=0x{1:X} pc={2}", offset + baseOffset, value, pc);
            stock.WriteDoubleWord(offset + baseOffset, value);
        }

        private readonly IMachine machine;
        private readonly STM32H7_RCC stock;
        private readonly long baseOffset;
    }
}
