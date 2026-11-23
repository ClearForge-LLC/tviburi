# TVI-WO-1200 — Identity, the ledger, and the release artifact: one compiled file, a manifest that binds, and recorded mode in `model/`

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff (`ef3cfca` when written; verify live HEAD).
**Branch:** `wo/tvi-1200-release`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1. **Cadence:** one measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 2, 3, 7, 8; §6 ("so slow or heavy").
- **Architecture:** `RUL-03`, `RUL-16`, `RUL-17` (the ledger); `RUL-09` (a) and `RUL-10` (a)–(g), as amended from `1150`; `RUL-11`, `RUL-12`, `RUL-14`, `RUL-18`, `RUL-19`; `OQ-01` (this work order sets the ledger's field names).
- **Roadmap:** Phase 1, the `1200` row; gate clauses 2, 3, 6 and 7.
- **Evidence:** `spike/1150/results.md` (iii), (iv) and (vi) cost; `spike/1150/release/` (`make-release.sh`, `consumer-check.sh`, the `readonly` template); `spike/1150/overlay/Records1150.cs` and `nucleo-recorded.repl`; `TVI-WO-1150.FEEDBACK.md` §3.4, §7 and §8.4; `TVI-WO-0100b.FEEDBACK.md` §1 (the bench checks) and §2 items 5 and 12; `TVI-WO-0100b.rm0433.md` §2 (the classes); the `Fields` table in `model/GuardedSTM32H7_SPI.cs`.

> **What this is:** the release a consumer pins, and the identity and ledger that make it checkable. Today the witness reports a hash of whatever directory `$TVIBURI_MODEL_DIR` names (`model/Refusal.cs`). Inside Aethelgard's empty environment that is a `TVI-LOAD` on every access (`1150` (ii)). Nothing is released, and no ledger exists.
> - **(1) The release** (`RUL-09` (a), `RUL-14`): a Rust program, `tools/tviburi-release`, with no crate dependency (it builds offline and locked, as the check does). From one commit it writes:
>   - **the overlay, one C# file:** every `model/*.cs` in byte order of name, with their `using` lines hoisted, deduplicated and sorted, then the filled release constant. It takes any `model/*.cs`, so `1300`'s guard needs no change to it. A `using` after a declaration or inside a namespace, a byte-order mark, a CR, or a file that is not regular refuses by name;
>   - **the manifest:** a format line; both build strings `tests/model-version` pins today (`Renode.dll`'s and `Infrastructure.dll`'s, `0100b` F5); the class map; the sha256 of every released file; and the two excluded files, each named with how it is checked. The release hash is the sha256 of the manifest's bytes;
>   - **the class map:** per guard, its stock class; and, read from the pinned stock files, each instance they declare with that class, which the consumer cancels and restates (`RUL-09` (b)). Any other instance is the consumer's own, and its type string changes. One declaration per guard, never written twice;
>   - **the constant, outside the hashed set** (`RUL-10` (e)): `static readonly`, in a template with no `using` line (`1150` (iii)'s `readonly` template). **The consumer's binding check (`RUL-10` (f)) is then a byte comparison with no C# parsing:** the manifest lists the overlay's body (everything before the constant) and the template. The shipped overlay must equal that body followed by the template filled with the pinned hash. `tviburi-release verify <dir> <pinned hash>` is that check, as `tviburi-check` is the records check;
>   - **the tarball,** with a fixed order, mtime, owner and mode, so two builds from one commit are byte-identical.
> - **(2) Identity** (`RUL-10`). The witness, every refusal and the `end` record report `TviburiRelease.ManifestSha256`. `$TVIBURI_MODEL_DIR`, the directory hash and its helpers are removed from the model, the runner and `tests/model-version`. No environment variable, path or name is read. The check's expected version is the release hash, read from the manifest the test built. Every run from `scripts/` loads the release's one file, never `model/` directly.
> - **(3) The ledger** (`RUL-03`, `RUL-16`, `RUL-17`), written by the release program as canonical JSONL: a schema line first, naming the schema version and the release hash; then rows sorted by (peripheral class, register, field); UTF-8, LF, no trailing whitespace; its hash is the sha256 of its bytes. It is the second excluded file, because it names the release hash. Rows:
>   - **one per field of every register of each guarded peripheral:** its bits, its state, the values allowed (`RUL-17`), what the model behaves as, and its RM0433 Rev 9 section and page. The state is one of `guard`, `inherited-unaudited`, `acknowledged` (with its reason), `tag`, `reserved` or `canned`. Rows are read from the data the guards run on (SPI's `Fields`; `1300`'s RCC data once merged), by a strict reader of the row form that refuses any line in the table it cannot read. They are never typed again;
>   - **one per inherited model** in the pinned `stm32h753.repl` include chain, `inherited-unaudited` (invariant 2); and a `tag` or `canned` row for each stock tag and Python stub (`RUL-16`: PWR and FMC answer "ready"), read from the pinned files and keyed by their sha256;
>   - **one per `predicted-fault` rule, with its bench check** (`RUL-18`). `0100b` FEEDBACK §1's B1–B4 move into one data file, each with `result: "not run"`.
> - **(4) Recorded mode in `model/`** (`RUL-19`), as `1150` (iv) prototyped it. `mode` is a constructor argument from the platform description, `"enforcing"` by default; any other string is a `TVI-LOAD` refusal. The witness carries it. A would-be refusal is a `would-refuse` record with its `kind`: nothing pauses, and the access goes on to the stock model and to the guard's copies as if passed. The check already fails such a run (`0100b`). It is one record per access, as prototyped. Whether to fold repeats (`1150` FEEDBACK §8.4) is not ruled: measure the volume and report it.
>
> It is NOT: any guard's rules or field data (`0100b`'s SPI, `1300`'s RCC); a record field beyond `mode`; publishing a release (CI builds and tests it; publication is Phase 2's); any change to Aethelgard.

## 1. Build

1. **Measure first.** Build the one-file overlay as (1) would, by hand if the program is not written yet, with recorded mode in it. Give its size and the release's size. Give the peak RSS of ArduPilot's full 5 s boot, recorded, as `1150` (vi) measured it (`harness/replay-ap.sh`, the reaper's measure, three runs), against 1,048,576 KiB. Include `1300`'s guard if it has merged, and say whether it had. Map every class in SPI's `Fields` onto (3)'s six states: `read-only`, `stock, wrong` and `undefined in the stock model` have no obvious one. **Stop** with both.
2. **Build (1)–(4).** Each test comes with a mutant it kills.
3. **Merge `main` before READY.** If `1300` merged first, wire `mode` into its guard's constructor and refusal returns, as for SPI.

## 2. Acceptance

- **Reproducible:** two release builds from one commit, in two checkouts with different mtimes and umasks, are byte-identical: the tarball, the manifest, the overlay and the ledger.
- **Identity:**
  - changing any byte of any `model/*.cs` changes the manifest hash and the witness's version;
  - `verify` passes `1150` (iii)'s case A, and fails B (an edited model byte), C (a swapped constant), E (a stale manifest) and D (no overlay);
  - the witness's version is unchanged with `TVIBURI_MODEL_DIR` set to another directory, unset, and with an empty environment, as Aethelgard runs Renode (gate clause 3).
- **The ledger:**
  - it regenerates byte-identically (gate clause 2), and a reader refuses one with rows out of order, a CR, a trailing space, or no schema line;
  - every bit 31:0 of every guarded register is in exactly one row, checked by enumeration;
  - in a run, each row's disallowed value refuses `unmodeled` and each allowed value passes, with the cases generated from the ledger, not from a second list (gate clause 6);
  - every `predicted-fault` that `conditions` produces has a bench row.
- **Recorded:** `modf`, recorded, gives a `mode: "recorded"` witness, a `would-refuse` with `kind: predicted-fault`, the after-marker and an `end`, and the check does not pass it. `mode: "Recorded"` is `TVI-LOAD` (gate clause 7).
- **Every existing test passes on the release,** and CI is green on the PR.

## 3. Fences

- **Fence:**
  - `model/Refusal.cs` (identity, recorded mode); in `model/GuardedSTM32H7_SPI.cs`, only the constructor's `mode` and the refusal helpers' return path (refuse, or record and go on); one new data file of bench checks in `model/`; `mode` in `model/tviburi-nucleo-h753zi.repl`;
  - `tools/` (the release program, the check's version input); `tests/`; `scripts/`, where the runner must use the release; `docs/work-orders/TVI-WO-1200.*`.
- **Not:** SPI's rules or `Fields` data (`0100b`'s, merged), beyond reading them; the RCC guard; the steering documents, `README.md`, `docs/reviews/`, `spike/`, or Aethelgard.
- **Running beside you:** `TVI-WO-1300`. It adds `model/GuardedSTM32H7_RCC.cs`, a generated restatement, and one `include` line in `scripts/prelude.resc`, which you replace with the release's one file. Whichever merges second carries the other's change. Tell its builder on the bus when the release takes `model/*.cs`.
- One PR, left unmerged. Write `~/.cache/tvi-1200/pr-title-body.md`.

## 5. Adversarial pass, full

- **A release that differs from what compiled:** a model file the glob misses (a symlink, a subdirectory, a name in another case); an order that follows the locale; a `using` alias that changes meaning when hoisted; a template edited so the filled constant still matches.
- **An identity from a name:** any remaining read of the environment, a path, or `Assembly.Location` in `model/`, `scripts/` or `tests/`; a cached assembly. `1150` (iii) found a `const` goes stale under the cache: keep `static readonly`, and show that a cache-on second boot fails closed.
- **A ledger that lies:** a row the reader skips or misreads from the C# data (a mutant per row form); a field in no row; an inherited model listed as audited; a ledger hash that changes when nothing in `model/` or the pinned stock files did.
- **A recorded run that passes anywhere:** at the check, at `verify`, or from a witness whose `mode` is missing, duplicated or in another case; a mode chosen by anything but the platform description.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement and the class mapping.
- **The one-file overlay's peak exceeds 1,048,576 KiB** on ArduPilot's full boot. Stop: northstar §6.
- **The constant cannot be both compiled in and outside the hashed set** with the consumer's compiler cache off (`RUL-10` (g)). Stop with the runs.
- **A ledger state the data cannot express:** a class in a guard's data that maps to none of (3)'s states, or a row that needs a field `RUL-03` does not give. Stop with the class and the rows.
- **A guard's rule or data must change** for the release or the ledger to work. Stop with the line.
