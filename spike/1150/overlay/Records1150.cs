// TVI-WO-1150 SPIKE COPY of model/Refusal.cs (0100a), not a model. Two changes, each marked "1150":
//   (iii) the model version is the compiled-in constant TviburiRelease.ManifestSha256, from a generated file
//         outside the manifest's hash (spike/1150/release/), never an environment variable or a path;
//   (iv)  a mode, chosen by the platform description (RUL-19): the witness carries it, and in "recorded" mode
//         a would-be refusal is a `would-refuse` record with its kind, and the access goes on to the stock model.
// Original header follows.
// Tviburi records runtime (TVI-WO-0100a): the records contract of RUL-11 and RUL-12.
//
// Every guarded model owns one Records channel. It emits three kinds of record, each one JSON
// object on one line, through Renode's logger under the guarded peripheral's own name:
//   witness - once, when the peripheral is named on the bus: the guard is loaded, with its model
//             version. A run with no witness is not a pass.
//   refusal - a path the model does not model (northstar invariants 1 and 5). Logged at Error,
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

namespace Antmicro.Renode.Peripherals.Tviburi1150
{
    public sealed class Records
    {
        public Records(IMachine machine, IPeripheral owner, string guard, string mode)
        {
            this.machine = machine;
            this.owner = owner;
            this.guard = guard;
            // 1150 (iv): the mode, from the platform description; anything but the two names is a load failure.
            this.mode = mode;
            if(mode != "enforcing" && mode != "recorded")
            {
                loadFailure = "mode must be \"enforcing\" or \"recorded\", not \"" + mode + "\"";
            }
            // 1150 (iii): the release constant, compiled in from the generated TviburiRelease.cs.
            version = TviburiRelease.ManifestSha256;
            machine.PeripheralsChanged += OnPeripheralsChanged;
        }

        public const string LoadCitation = "Tviburi records contract (RUL-12); northstar invariant 5";

        // True when the model version could not be computed: the guard must refuse every access.
        public bool LoadFailed => loadFailure != null;

        public bool Recorded => mode == "recorded";

        // 1150 (iv): true when the access is refused (enforcing): the caller must not pass it on. False in recorded
        // mode: a `would-refuse` record is emitted, nothing is paused, and the caller passes the access on.
        public bool Refuse(string rule, string kind, string register, string bit, string condition, string citation,
            string state, long offset, ulong value, int width)
        {
            var pc = "none";
            if(machine.SystemBus.TryGetCurrentCPU(out var cpu))
            {
                pc = "0x" + cpu.PC.RawValue.ToString("X8");
            }
            Emit(LogLevel.Error, Recorded ? "would-refuse" : "refusal", ",\"rule\":" + Json(rule)
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
            if(Recorded)
            {
                return false;
            }
            (cpu as TranslationCPU)?.RequestReturn();
            machine.PauseAndRequestEmulationPause(true);
            return true;
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
        private readonly string mode;
        private readonly object sync = new object();
        private bool named;
        private int seq;
    }
}
