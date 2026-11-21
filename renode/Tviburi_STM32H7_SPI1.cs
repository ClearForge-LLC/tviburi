// Tviburi refusal shim for STM32H7 SPI1.
//
// Upstream Renode 1.17.0 does not instantiate SPI1 on the NUCLEO-H753ZI.
// The region at 0x40013000 is a sysbus tag ("SPI1_/_I2S1"): reads return 0
// and writes are ignored. This type is registered at that address so the
// access hits a peripheral instead of the tag.
//
// The named path is the silicon scar: a write that sets SPI_CR1.SPE.
// On the H753 that raises SPI_SR.MODF (bit 9) and clears MASTER. The model
// does not implement that side-effect, so the access refuses. Every other
// access on this peripheral also refuses; SPI1 has no modeled registers.

using System;
using System.IO;
using System.Security.Cryptography;

using Antmicro.Renode.Core;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class Tviburi_STM32H7_SPI1 : IBytePeripheral, IWordPeripheral, IDoubleWordPeripheral, IKnownSize
    {
        public Tviburi_STM32H7_SPI1(IMachine machine)
        {
            if(machine == null)
            {
                FailClosed("Tviburi refusal: peripheral=SPI1 register=model condition=machine is null model_version=" + ModelVersion + " model_hash=unavailable");
            }
            // The resc sets this to the shim source next to the script. Renode's
            // process directory is the host install, so a repl-relative path does not resolve.
            this.modelHash = HashModelSource(Environment.GetEnvironmentVariable("TVI_MODEL_SOURCE"));
        }

        public long Size => 0x400;

        public void Reset()
        {
        }

        public byte ReadByte(long offset)
        {
            Refuse(offset, "read", null, 1);
            throw new RecoverableException("unreachable");
        }

        public void WriteByte(long offset, byte value)
        {
            Refuse(offset, "write", value, 1);
        }

        public ushort ReadWord(long offset)
        {
            Refuse(offset, "read", null, 2);
            throw new RecoverableException("unreachable");
        }

        public void WriteWord(long offset, ushort value)
        {
            Refuse(offset, "write", value, 2);
        }

        public uint ReadDoubleWord(long offset)
        {
            Refuse(offset, "read", null, 4);
            throw new RecoverableException("unreachable");
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            Refuse(offset, "write", value, 4);
        }

        private void Refuse(long offset, string kind, ulong? value, int width)
        {
            var shown = value.HasValue ? string.Format("0x{0:X}", value.Value) : "-";
            var access = string.Format(
                "Tviburi access: peripheral={0} offset=0x{1:X} kind={2} value={3} width={4}",
                PeripheralName, offset, kind, shown, width);

            string register;
            string condition;
            if(IsModfPath(offset, kind, value))
            {
                register = "SPI_SR.MODF bit9";
                condition = offset == StatusOffset
                    ? "access to SPI_SR which contains unimplemented MODF; flag must not read as success"
                    : "SPI_CR1.SPE set; silicon raises SPI_SR.MODF and clears MASTER; this model does not";
            }
            else
            {
                register = string.Format("offset_0x{0:X}", offset);
                condition = "SPI1 register is not modeled";
            }

            var refusal = string.Format(
                "Tviburi refusal: peripheral={0} register={1} condition={2} model_version={3} model_hash={4}",
                PeripheralName, register, condition, ModelVersion, modelHash);
            FailClosed(access, refusal);
        }

        private static bool IsModfPath(long offset, string kind, ulong? value)
        {
            if(offset == StatusOffset)
            {
                return true;
            }
            return offset == Control1Offset
                && kind == "write"
                && value.HasValue
                && (value.Value & SpeMask) != 0;
        }

        private static string HashModelSource(string modelSource)
        {
            if(string.IsNullOrEmpty(modelSource) || !File.Exists(modelSource))
            {
                FailClosed("Tviburi refusal: peripheral=SPI1 register=model condition=model source missing or unreadable model_version=" + ModelVersion + " model_hash=unavailable");
                throw new RecoverableException("unreachable");
            }
            using(var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(File.ReadAllBytes(modelSource));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void FailClosed(string refusal)
        {
            FailClosed(null, refusal);
        }

        private static void FailClosed(string access, string refusal)
        {
            try
            {
                if(access != null)
                {
                    Logger.Log(LogLevel.Error, access);
                }
                Logger.Log(LogLevel.Error, refusal);
                Logger.Flush();
            }
            catch(Exception)
            {
                // The refusal still has to leave the process. Logging is best-effort.
            }
            if(access != null)
            {
                Console.Error.WriteLine(access);
            }
            Console.Error.WriteLine(refusal);
            Console.Error.Flush();
            Environment.ExitCode = 1;
            Environment.Exit(1);
            throw new RecoverableException(refusal);
        }

        private const string PeripheralName = "SPI1";
        private const string ModelVersion = "tvi-wo-0100+renode-1.17.0";
        private const long Control1Offset = 0x00;
        private const long StatusOffset = 0x14;
        private const ulong SpeMask = 0x1;

        private readonly string modelHash;
    }
}
