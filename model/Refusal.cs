// Tviburi refusal runtime (TVI-WO-0100 spike).
//
// A guarded model calls Refusal.Refuse(...) on a path it does not model. The runtime:
//   1. writes the refusal record to $TVIBURI_OUT/refusals.jsonl and flushes it to disk
//      before anything else, so the record survives whatever happens to Renode next;
//   2. prints it on stderr;
//   3. asks the CPU to leave its translation block and the emulation to pause.
// Renode's pause is not instruction-precise here: the rest of the current translation
// block may still execute (measured; see FEEDBACK.md). The refusal record is the verdict,
// not the halt.
//
// Fail-closed rules: no model version, no output directory, or a record that cannot be
// written all end the process (constructor exception or FailFast). None of them lets a
// run continue as if it were modeled.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.CPU;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public static class Refusal
    {
        // sha256 over every file under $TVIBURI_MODEL_DIR (relative path + content hash,
        // ordinal order) plus the Renode and Infrastructure build strings. Throws if any
        // input is missing: a refusal without a model version is not a refusal (invariant 5).
        public static string ModelVersion
        {
            get
            {
                lock(sync)
                {
                    if(modelVersion == null)
                    {
                        modelVersion = ComputeModelVersion();
                    }
                    return modelVersion;
                }
            }
        }

        public static void Armed(string guard)
        {
            Append("armed.jsonl", "{\"tviburi\":\"armed\",\"guard\":" + Json(guard) + ",\"model_version\":" + Json(ModelVersion) + "}");
        }

        public static void Refuse(IMachine machine, IPeripheral peripheral, string rule, string register, string bit,
            string condition, string citation, string state, long offset, ulong value, int width)
        {
            var name = machine.GetAnyNameOrTypeName(peripheral);
            var pc = "none";
            var cpu = default(ICPU);
            if(machine.SystemBus.TryGetCurrentCPU(out cpu))
            {
                pc = "0x" + cpu.PC.RawValue.ToString("X8");
            }
            var record = "{\"tviburi\":\"refusal\""
                + ",\"rule\":" + Json(rule)
                + ",\"peripheral\":" + Json(name)
                + ",\"register\":" + Json(register)
                + ",\"bit\":" + Json(bit)
                + ",\"condition\":" + Json(condition)
                + ",\"model_version\":" + Json(ModelVersion)
                + ",\"citation\":" + Json(citation)
                + ",\"state\":" + Json(state)
                + ",\"access\":{\"offset\":" + Json("0x" + offset.ToString("X"))
                + ",\"value\":" + Json("0x" + value.ToString("X"))
                + ",\"width\":" + width
                + ",\"pc\":" + Json(pc)
                + ",\"virtual_time\":" + Json(machine.ElapsedVirtualTime.TimeElapsed.ToString()) + "}}";
            Append("refusals.jsonl", record);
            Console.Error.WriteLine("TVIBURI REFUSAL " + record);

            (cpu as TranslationCPU)?.RequestReturn();
            machine.PauseAndRequestEmulationPause(true);
        }

        private static string ComputeModelVersion()
        {
            var dir = Environment.GetEnvironmentVariable("TVIBURI_MODEL_DIR");
            if(string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                throw new InvalidOperationException("TVIBURI_MODEL_DIR is unset or missing; refusing to run without a model version");
            }
            var manifest = new StringBuilder();
            var root = Path.GetFullPath(dir);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if(files.Count == 0)
            {
                throw new InvalidOperationException("TVIBURI_MODEL_DIR holds no files; refusing to run without a model version");
            }
            foreach(var rel in files)
            {
                manifest.Append(rel).Append('\0').Append(Sha256(File.ReadAllBytes(Path.Combine(root, rel)))).Append('\n');
            }
            manifest.Append("renode\0").Append(BuildString("Renode")).Append('\n');
            manifest.Append("infrastructure\0").Append(BuildString("Infrastructure")).Append('\n');
            return "sha256:" + Sha256(Encoding.UTF8.GetBytes(manifest.ToString()));
        }

        private static string BuildString(string assembly)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assembly);
            var version = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if(string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("no build string for assembly " + assembly + "; refusing to run without a model version");
            }
            return version;
        }

        private static void Append(string file, string line)
        {
            try
            {
                var dir = Environment.GetEnvironmentVariable("TVIBURI_OUT");
                if(string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    throw new InvalidOperationException("TVIBURI_OUT is unset or missing");
                }
                lock(sync)
                {
                    using(var stream = new FileStream(Path.Combine(dir, file), FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        var bytes = Encoding.UTF8.GetBytes(line + "\n");
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                }
            }
            catch(Exception e)
            {
                // A refusal that cannot be recorded must not let the run continue.
                Environment.FailFast("tviburi: cannot record " + file + ": " + e.Message);
            }
        }

        private static string Sha256(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string Json(string s)
        {
            var b = new StringBuilder("\"");
            foreach(var c in s)
            {
                if(c == '"' || c == '\\') { b.Append('\\').Append(c); }
                else if(c < 0x20) { b.Append("\\u").Append(((int)c).ToString("x4")); }
                else { b.Append(c); }
            }
            return b.Append('"').ToString();
        }

        private static readonly object sync = new object();
        private static string modelVersion;
    }
}
