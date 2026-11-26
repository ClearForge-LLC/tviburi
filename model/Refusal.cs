// Tviburi records runtime (TVI-WO-0100a): the records contract of RUL-11 and RUL-12.
//
// Every guarded model owns one Records channel. It emits three kinds of record, each one JSON
// object on one line, through Renode's logger under the guarded peripheral's own name:
//   witness - once, when the peripheral is named on the bus: the guard is loaded, with its model
//             version and its mode (RUL-19). A run with no witness is not a pass.
//   refusal - a path the model does not model (northstar invariants 1 and 5), with its kind (RUL-18):
//             predicted-fault or unmodeled. Logged at Error,
//             which `logLevel` cannot suppress (measured: `logLevel 4` is rejected). Latches
//             (RUL-08): after it, nothing the run does is claimed.
//             In recorded mode (RUL-19) the same record is a `would-refuse` (the contract below).
//   end     - from Dispose, when Renode tears the machine down cleanly. A crash (an unhandled
//             exception, SIGKILL) leaves none (measured). SIGTERM still disposes, so an end record
//             shows a clean teardown, not that the consumer's script completed.
//             The logger is flushed after it, so a clean quit never loses it (TVI-WO-1204 (2)).
// Recorded mode's contract (RUL-19; TVI-WO-1204 (3), ruled 2026-11-26T12:21Z), written here once; each guard's header cites it:
//   - Enforcing: the first rule that fires refuses the access, and no later rule is evaluated for it.
//   - Recorded: every rule is evaluated for the access, in the guard's order, and each that fires records a `would-refuse`
//     (Records.Evaluates). Nothing pauses. TVI-LOAD is the exception: a guard that failed to load refuses in either mode.
//   - Recorded: the access goes on to the stock model and to the guard's copies as if no rule had fired, so a read returns
//     what the stock model serves and a write reaches the stock model whole.
//   - What follows a would-refuse is the stock model's behaviour, not a guard's promise; a recorded run is never a pass.
//     Two named examples (TVI-WO-1306a F10): a START the I2C guard passes while a NACK is pending still gets that NACK's
//     STOPF, inside the new transfer; and an uncleared NACK STOPF keeps the port in controller mode, so a later STOP passes
//     where enforcing mode would refuse it.
// Each record carries a per-peripheral `seq` from 0. Renode collapses identical consecutive messages
// into "msg (2)" (measured), so the sequence also keeps every record distinct.
//
// The model version is TviburiRelease.ManifestSha256: the release hash, compiled in from the release's
// one file (RUL-10 (b), (e)). No environment variable, path or name is read (RUL-10 (c)).
// The mode is a constructor argument from the platform description (RUL-19): "enforcing", the default,
// or "recorded". Any other value is a load failure.
//
// Why the witness waits for the name: a log call from a model's constructor carries no source name
// (measured), and the consumer accepts records only from the guarded peripheral's name (RUL-11).
// A load failure is held until then and emitted as a refusal (rule TVI-LOAD) in place of the
// witness; the guard then refuses every access, in either mode.
//
// The halt after a refusal is best-effort: the rest of the translation block may run (RUL-08).

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.CPU;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public sealed class Records
    {
        // `witness`: extra fields the guard's witness carries after its mode, each a JSON string (TVI-WO-1301, granted
        // 2026-11-23T19:08Z: `package`, so a consumer sees which package's rule ran, or "none").
        public Records(IMachine machine, IPeripheral owner, string guard, string mode, IEnumerable<KeyValuePair<string, string>> witness = null)
        {
            this.machine = machine;
            this.owner = owner;
            this.guard = guard;
            this.mode = mode;
            this.witness = witness == null ? "" : string.Concat(witness.Select(f => "," + Json(f.Key) + ":" + Json(f.Value)));
            if(mode != Enforcing && mode != RecordedMode)
            {
                loadFailure = "mode must be \"" + Enforcing + "\" or \"" + RecordedMode + "\", not " + (mode == null ? "null" : Json(mode));
            }
            machine.PeripheralsChanged += OnPeripheralsChanged;
        }

        public const string LoadCitation = "Tviburi records contract (RUL-12); northstar invariant 5";

        public const string Enforcing = "enforcing";
        public const string RecordedMode = "recorded";

        // True when the guard failed to load: it must refuse every access, in either mode.
        public bool LoadFailed => loadFailure != null;

        // True in recorded mode (RUL-19; the contract in this file's header). Never true while LoadFailed: a load failure is
        // a mode other than these two.
        public bool Recorded => mode == RecordedMode;

        // Whether the access's next rule is evaluated, given whether one has fired: in recorded mode always, in enforcing
        // mode only while none has (the contract in this file's header).
        public bool Evaluates(bool fired) => !fired || Recorded;

        public void Refuse(string rule, string kind, string register, string bit, string condition, string citation,
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
                + ",\"model_version\":" + Json(version)
                + ",\"citation\":" + Json(citation)
                + ",\"state\":" + Json(state)
                + ",\"access\":{\"offset\":" + Json("0x" + offset.ToString("X"))
                + ",\"value\":" + Json("0x" + value.ToString("X"))
                + ",\"width\":" + width
                + ",\"pc\":" + Json(pc)
                + ",\"virtual_time\":" + Json(machine.ElapsedVirtualTime.TimeElapsed.ToString()) + "}");
            if(Recorded)
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
                Emit(LogLevel.Info, "end", ",\"guard\":" + Json(guard) + ",\"model_version\":" + Json(version));
                // The teardown race (TVI-WO-1204 (1), (2)): under asynchronous logging, Renode's default, Emit only enqueues, and
                // a clean quit disposes the machines, then this emulation's logger, whose writer thread is cancelled with its
                // queue undrained, so a queued end record is never written. Flushing here writes every queued entry before this
                // Dispose returns, so before the logger can be torn down: deterministic, not a delay. Measured: 16 of 50 loaded
                // quits lost an end record without it, 0 of 50 with it. Under synchronous logging it only flushes the backends.
                Logger.Flush();
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
                Emit(LogLevel.Info, "witness", ",\"guard\":" + Json(guard) + ",\"model_version\":" + Json(version) + ",\"mode\":" + Json(mode) + witness);
            }
            else
            {
                Refuse("TVI-LOAD", "unmodeled", "none", "none", "the guard could not load: " + loadFailure
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
        private readonly string version = TviburiRelease.ManifestSha256;
        private readonly string mode;
        private readonly string witness;
        private readonly string loadFailure;
        private readonly object sync = new object();
        private bool named;
        private int seq;
    }
}
