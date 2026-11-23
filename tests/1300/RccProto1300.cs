// TVI-WO-1300 §1.1 SCRATCH PROTOTYPE (measurement only, not a model). RUL-07's wrapper shape for the sealed
// STM32H7_RCC: it owns an unregistered stock instance, built with the stock entry's constructor arguments, and
// forwards every access, registration, unregistration and reset to it. It implements all three bus widths itself.
// It is transparent: no access is refused or changed. With trace: true it logs every access from load, in order,
// as one `TVI1300-T` line each, so the field table, the widths and the order of enables can be read off a boot.
// A sub-word access is forwarded as Renode's ByteToDoubleWord/WordToDoubleWord translation does it (from memory:
// read the double word, merge, write it back), and its width is logged.
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class RccProto1300 : IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IDisposable,
        IRegisterablePeripheral<IPeripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias>>
    {
        public RccProto1300(IMachine machine, IHasDivisibleFrequency systick = null, ulong hseFrequency = 8000000,
            ulong csiFrequency = 4000000, string mode = "enforcing", bool trace = false)
        {
            this.machine = machine;
            this.trace = trace;
            inner = new STM32H7_RCC(machine, systick, hseFrequency, csiFrequency);
            records = new Records(machine, this, nameof(RccProto1300), mode);
        }

        public long Size => inner.Size;

        public void Reset()
        {
            inner.Reset();
            Trace("RESET", 0, 0, 0, null);
        }

        public void Dispose()
        {
            records.End();
        }

        public void Register(IPeripheral peripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias> registrationPoint)
        {
            inner.Register(peripheral, registrationPoint);
        }

        public void Unregister(IPeripheral peripheral)
        {
            inner.Unregister(peripheral);
        }

        public uint ReadDoubleWord(long offset)
        {
            var v = inner.ReadDoubleWord(offset);
            Trace("R", 32, offset, v, null);
            return v;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            inner.WriteDoubleWord(offset, value);
            Trace("W", 32, offset, value, offset == 0x17C ? (uint?)null : inner.ReadDoubleWord(offset));
        }

        public ushort ReadWord(long offset)
        {
            var v = (ushort)(inner.ReadDoubleWord(offset & ~3L) >> (int)(8 * (offset & 3)));
            Trace("R", 16, offset, v, null);
            return v;
        }

        public void WriteWord(long offset, ushort value)
        {
            var aligned = offset & ~3L;
            var shift = (int)(8 * (offset & 3));
            var merged = (inner.ReadDoubleWord(aligned) & ~(0xFFFFu << shift)) | ((uint)value << shift);
            inner.WriteDoubleWord(aligned, merged);
            Trace("W", 16, offset, value, aligned == 0x17C ? (uint?)null : inner.ReadDoubleWord(aligned));
        }

        public byte ReadByte(long offset)
        {
            var v = (byte)(inner.ReadDoubleWord(offset & ~3L) >> (int)(8 * (offset & 3)));
            Trace("R", 8, offset, v, null);
            return v;
        }

        public void WriteByte(long offset, byte value)
        {
            var aligned = offset & ~3L;
            var shift = (int)(8 * (offset & 3));
            var merged = (inner.ReadDoubleWord(aligned) & ~(0xFFu << shift)) | ((uint)value << shift);
            inner.WriteDoubleWord(aligned, merged);
            Trace("W", 8, offset, value, aligned == 0x17C ? (uint?)null : inner.ReadDoubleWord(aligned));
        }

        private void Trace(string op, int width, long offset, uint value, uint? after)
        {
            if(!trace)
            {
                return;
            }
            var pc = machine.SystemBus.TryGetCurrentCPU(out var cpu) ? "0x" + cpu.PC.RawValue.ToString("X8") : "none";
            this.Log(LogLevel.Warning, "TVI1300-T {0} {1}{2} off=0x{3:X3} val=0x{4:X} pc={5}{6}", seq++, op, width, offset, value, pc,
                after.HasValue ? " after=0x" + after.Value.ToString("X") : "");
        }

        private readonly IMachine machine;
        private readonly STM32H7_RCC inner;
        private readonly Records records;
        private readonly bool trace;
        private long seq;
    }
}
