// TVI-WO-1300 §1.1 MEASUREMENT COPY (not a model) of TVI-WO-0100b's model/Refusal.cs at 9ddad9a8, with a recorded mode as 1150 (iv) prototyped it: a
// `mode` constructor argument, carried by the witness; in "recorded" a would-be refusal is a `would-refuse` record and no pause.
// Tviburi records runtime (TVI-WO-0100a): the records contract of RUL-11 and RUL-12.
//
// Every guarded model owns one Records channel. It emits three kinds of record, each one JSON
// object on one line, through Renode's logger under the guarded peripheral's own name:
//   witness - once, when the peripheral is named on the bus: the guard is loaded, with its model
//             version and its mode, always "enforcing" here (RUL-19). A run with no witness is not a pass.
//   refusal - a path the model does not model (northstar invariants 1 and 5), with its kind (RUL-18):
//             predicted-fault or unmodeled. Logged at Error,
//             which `logLevel` cannot suppress (measured: `logLevel 4` is rejected). Latches
//             (RUL-08): after it, nothing the run does is claimed.
//   end     - from Dispose, when Renode tears the machine down cleanly. A crash (an unhandled
//             exception, SIGKILL) leaves none (measured). SIGTERM still disposes, so an end record
//             shows a clean teardown, not that the consumer's script completed.
// Each record carries a per-peripheral `seq` from 0. Renode collapses identical consecutive messages
// into "msg (2)" (measured), so the sequence also keeps every record distinct.
//
// Why the witness waits for the name: a log call from a model's constructor carries no source name
// (measured), and the consumer accepts records only from the guarded peripheral's name (RUL-11).
// A failure to compute the model version at construction is held until then and emitted as a
// refusal (rule TVI-LOAD) in place of the witness; the guard then refuses every write.
//
// The halt after a refusal is best-effort: the rest of the translation block may run (RUL-08).

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.CPU;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public sealed class Records
    {
        public Records(IMachine machine, IPeripheral owner, string guard) : this(machine, owner, guard, "enforcing")
        {
        }

        public Records(IMachine machine, IPeripheral owner, string guard, string mode)
        {
            this.mode = mode;
            if(mode != "enforcing" && mode != "recorded")
            {
                loadFailure = "mode must be \"enforcing\" or \"recorded\", not \"" + mode + "\"";
            }
            this.machine = machine;
            this.owner = owner;
            this.guard = guard;
            try
            {
                version = ModelVersion.Value;
            }
            catch(Exception e)
            {
                loadFailure = e.Message;
            }
            machine.PeripheralsChanged += OnPeripheralsChanged;
        }

        public const string LoadCitation = "Tviburi records contract (RUL-12); northstar invariant 5";

        // RUL-19: this runtime has no recorded mode (TVI-WO-1150 (iv) prototypes it), so every witness says
        // so, and every refusal fails the run.
        private readonly string mode;

        // True when the model version could not be computed: the guard must refuse every access.
        public bool LoadFailed => loadFailure != null;

        public void Refuse(string rule, string kind, string register, string bit, string condition, string citation,
            string state, long offset, ulong value, int width)
        {
            var pc = "none";
            if(machine.SystemBus.TryGetCurrentCPU(out var cpu))
            {
                pc = "0x" + cpu.PC.RawValue.ToString("X8");
            }
            Emit(LogLevel.Error, mode == "recorded" ? "would-refuse" : "refusal", ",\"rule\":" + Json(rule)
                + ",\"kind\":" + Json(kind)
                + ",\"peripheral\":" + Json(machine.GetAnyNameOrTypeName(owner))
                + ",\"register\":" + Json(register)
                + ",\"bit\":" + Json(bit)
                + ",\"condition\":" + Json(condition)
                + ",\"model_version\":" + Json(version ?? "unavailable")
                + ",\"citation\":" + Json(citation)
                + ",\"state\":" + Json(state)
                + ",\"access\":{\"offset\":" + Json("0x" + offset.ToString("X"))
                + ",\"value\":" + Json("0x" + value.ToString("X"))
                + ",\"width\":" + width
                + ",\"pc\":" + Json(pc)
                + ",\"virtual_time\":" + Json(machine.ElapsedVirtualTime.TimeElapsed.ToString()) + "}");
            if(mode == "recorded")
            {
                return;
            }
            (cpu as TranslationCPU)?.RequestReturn();
            machine.PauseAndRequestEmulationPause(true);
        }

        public void End()
        {
            if(named)
            {
                Emit(LogLevel.Info, "end", ",\"guard\":" + Json(guard) + ",\"model_version\":" + Json(version ?? "unavailable"));
            }
        }

        private void OnPeripheralsChanged(IMachine m, PeripheralsChangedEventArgs args)
        {
            if(named || args.Peripheral != owner || args.Operation != PeripheralsChangedEventArgs.PeripheralChangeType.NameChanged)
            {
                return;
            }
            named = true;
            if(loadFailure == null)
            {
                Emit(LogLevel.Info, "witness", ",\"guard\":" + Json(guard) + ",\"model_version\":" + Json(version) + ",\"mode\":" + Json(mode));
            }
            else
            {
                Refuse("TVI-LOAD", "unmodeled", "none", "none", "the guard's model version could not be computed at load: " + loadFailure
                    + "; the guard refuses every access to this peripheral", LoadCitation,
                    "no witness emitted", 0, 0, 0);
            }
        }

        private void Emit(LogLevel level, string kind, string fields)
        {
            lock(sync)
            {
                owner.Log(level, "{\"tviburi\":" + Json(kind) + ",\"seq\":" + seq + fields + "}");
                seq++;
            }
        }

        // sha256 over every file under $TVIBURI_MODEL_DIR (relative path + content hash, ordinal
        // order) plus the Renode and Infrastructure build strings. Throws if any input is missing.
        // Known gap, kept as built (TVI-WO-0100a; RUL-10 is TVI-WO-1200's): it hashes the files an
        // environment variable names, not the bytes that were compiled.
        private static readonly Lazy<string> ModelVersion = new Lazy<string>(() =>
        {
            var dir = Environment.GetEnvironmentVariable("TVIBURI_MODEL_DIR");
            if(string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                throw new InvalidOperationException("TVIBURI_MODEL_DIR is unset or missing");
            }
            var root = Path.GetFullPath(dir);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if(files.Count == 0)
            {
                throw new InvalidOperationException("TVIBURI_MODEL_DIR holds no files");
            }
            var manifest = new StringBuilder();
            foreach(var rel in files)
            {
                manifest.Append(rel).Append('\0').Append(Sha256(File.ReadAllBytes(Path.Combine(root, rel)))).Append('\n');
            }
            manifest.Append("renode\0").Append(BuildString("Renode")).Append('\n');
            manifest.Append("infrastructure\0").Append(BuildString("Infrastructure")).Append('\n');
            return "sha256:" + Sha256(Encoding.UTF8.GetBytes(manifest.ToString()));
        });

        private static string BuildString(string assembly)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assembly);
            var version = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if(string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("no build string for assembly " + assembly);
            }
            return version;
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

        private readonly IMachine machine;
        private readonly IPeripheral owner;
        private readonly string guard;
        private readonly string version;
        private readonly string loadFailure;
        private readonly object sync = new object();
        private bool named;
        private int seq;
    }
}
