// TVI-WO-1150 (i) spike only, not a model. Renode's STM32H7_RCC is `sealed` (renode-infrastructure 066a7f13,
// STM32H7_RCC.cs L24), so no subclass of it can exist. This is the nearest shape: a peripheral that holds a
// stock STM32H7_RCC and forwards every access, registration and reset to it, logging each write first.
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Tviburi1150
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class SpikeRcc : IDoubleWordPeripheral, IKnownSize, IRegisterablePeripheral<IPeripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias>>
    {
        public SpikeRcc(IMachine machine, IHasDivisibleFrequency systick = null, ulong hseFrequency = 8000000, ulong csiFrequency = 4000000)
        {
            this.machine = machine;
            Inner = new STM32H7_RCC(machine, systick, hseFrequency, csiFrequency);
        }

        public STM32H7_RCC Inner { get; }

        public long Size => Inner.Size;

        public void Reset()
        {
            Inner.Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return Inner.ReadDoubleWord(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            var pc = machine.SystemBus.TryGetCurrentCPU(out var cpu) ? "0x" + cpu.PC.RawValue.ToString("X8") : "none";
            this.Log(LogLevel.Warning, "TVI1150-SPIKE SpikeRcc write offset=0x{0:X} value=0x{1:X} pc={2}", offset, value, pc);
            Inner.WriteDoubleWord(offset, value);
        }

        public void Register(IPeripheral peripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias> registrationPoint)
        {
            Inner.Register(peripheral, registrationPoint);
        }

        public void Unregister(IPeripheral peripheral)
        {
            Inner.Unregister(peripheral);
        }

        private readonly IMachine machine;
    }
}
