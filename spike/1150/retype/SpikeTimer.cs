// TVI-WO-1150 (i) spike only, not a model: a subclass of Renode's stock STM32_Timer in the RUL-07 shape
// (it re-implements IDoubleWordPeripheral), which logs every write it receives, then passes it to the stock
// model. It exists to show whether a .repl entry re-types the stock `timer1` and whether a write reaches it.
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Timers;

namespace Antmicro.Renode.Peripherals.Tviburi1150
{
    public class SpikeTimer : STM32_Timer, IDoubleWordPeripheral
    {
        public SpikeTimer(IMachine machine, ulong frequency, uint initialLimit) : base(machine, frequency, initialLimit)
        {
            this.machine = machine;
        }

        public new void WriteDoubleWord(long offset, uint value)
        {
            var pc = machine.SystemBus.TryGetCurrentCPU(out var cpu) ? "0x" + cpu.PC.RawValue.ToString("X8") : "none";
            this.Log(LogLevel.Warning, "TVI1150-SPIKE SpikeTimer write offset=0x{0:X} value=0x{1:X} pc={2}", offset, value, pc);
            base.WriteDoubleWord(offset, value);
        }

        public new uint ReadDoubleWord(long offset)
        {
            return base.ReadDoubleWord(offset);
        }

        private readonly IMachine machine;
    }
}
