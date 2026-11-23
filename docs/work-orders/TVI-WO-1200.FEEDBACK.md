# TVI-WO-1200 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1200-release-identity` (the kickoff's name; the WO says `wo/tvi-1200-release`), from `main` `e20cae5`.

**State:**
- **§1.1 measured; no stop fired** (bus, 13:26:12Z). The build, (1)–(4), followed.
- `TVI-WO-1300` had not merged when the measurement was made, nor when `main` was last fetched (§6).
- Steering documents, `README.md`, `docs/reviews/`, `spike/` and Aethelgard: untouched.
- One PR, left unmerged.

---

## 1. The measurement (WO §1.1)

**Method:** `spike/1150` (vi)'s, with a copy of `harness/replay-ap.sh` in scratch, its type string changed to `Tviburi.GuardedSTM32H7_SPI`.
- **Recaptured,** because `~/.cache/tvi-1150` was gone: Aethelgard `7dec1d7b` (1150's commit). Its CLI and `aethelgard-bridge` were built in image `1e0ab105` (main's pin; `7dec1d7b` pinned `e5fc7ddb`, which carries the same Renode build).
- **The boot:** `simulate --spec sentinel-02-own.json --firmware arducopter.elf (a799dfb6…) --foreign 5000ms`. Verdict pass, the CLI's `peak_rss_kib` 854,860. The emitted platform's sha256 is `3927f28a…`, as in 1150.
- **The overlay:** the real `model/` (recorded mode and the constant in; the environment-variable hash out) as one file, hand-built by the release's rules, 52,665 B. One `include @` in the argv (checked).

**Peak RSS, full 5 s boot, KiB, against 1,048,576.** One container, runs interleaved, every run listed:

| Shape | Runs | Share of the bound |
|---|---|---|
| one file, recorded | 963,860; 975,524; 971,432 | 91.9–93.0 % |
| one file, enforcing | 972,016; 975,044; 950,964 | 90.7–93.0 % |
| stock | 867,712; 861,492; 857,768; 850,756; 856,228 | 81.1–82.8 % |

- Load average 1–3.3. **Margin: 73,052–97,612 KiB (71–95 MiB).** That is about 1150's one-file 954,716–964,704, now with the real guard. (The bus note of 13:26Z said "73–98 MiB", subtracting KiB and calling it MiB; corrected on the bus.)
- **Both modes ran the full boot.** The boot block (88,058,789 instructions `0x53EABA5`, the PC, and the GPIO, RCC and NVIC words) is byte-identical to stock in recorded and in enforcing.
- **ArduPilot now meets no refusal** (0100b's acknowledgements): one witness and one end each on `spi1` and `spi2`. **0 `would-refuse` records, 0 bytes.** 1150 FEEDBACK §8.4's volume question is therefore moot for ArduPilot today.
- **Release size, at this PR's head:** `Tviburi.cs` 52,924 B (the measured file was 52,665 B: the same C# with a shorter header); tarball 104,960 B (`MANIFEST` 2,974, `LEDGER.jsonl` 44,812, template 508). The release hash is `sha256:7cd09307…`; the ledger's is `5e1c4ea2…`.

**The class mapping (stop 3: not fired).** SPI's `Fields` classes map one to one onto `RUL-16` as amended:

| Class string in `Fields` | Ledger state |
|---|---|
| modeled by the guard | `guard` |
| inherited, unaudited (acknowledged by name) | `inherited-unaudited` |
| acknowledged by name | `acknowledged`, with `reason` = its rm0433.md §4 A-number |
| tag | `tag` |
| reserved | `reserved` |
| read-only | `read-only` |
| stock, wrong (byte order, L352) | `stock-wrong`, with `detail` |
| undefined in the stock model | `stock-undefined` |

Any other class string refuses the release as a stop.

**The stock chain** (`stm32h753` → `h743` → `h7`, each checked against Aethelgard's sha256 pins):
- typed declarations, memories included, are `inherited-unaudited`: one row per class, with its instances;
- `pwr` and `fmc` (Python stubs) are `canned`;
- 37 `Tag` lines in `sysbus init` are `tag`;
- connection-only updates get no row.
- **One judgement call:** `system_flash_ro` is a `MappedMemory` whose Python `init` writes a random UID and `FLASH_SIZE` = 0x800. It is listed as `canned`, beside its memory's inherited row (§5, Q2).

**The constant (stop 2: not fired).** Under Aethelgard's captured configuration (`compiler-cache-enabled = False`), the witness reports the filled constant from the one file. The manifest hashes the overlay's body and the template, never the shipped file. The tests show a byte flip and a cache-on second boot (§3).

## 2. What was built

**(1) The release.** `tools/tviburi-release`, Rust, no crate, offline and locked; it reuses the check's `json.rs` by `#[path]`.
- **`build <repo> <renode-root> <out>`** writes:
  - `Tviburi.cs`: every regular `model/*.cs` in byte order of name, `using` lines before the first declaration hoisted, deduplicated and sorted, then the template filled;
  - `MANIFEST`: the format line; a line saying why every source is listed (ruled 13:45Z); both build strings; the stock pins; the class map with its `restate` lines; a `source` line per input; the template; the overlay's body hash and length; `ledger-rows`, the hash of the ledger's lines after its schema line; the two `excluded` files;
  - `LEDGER.jsonl`;
  - `tviburi-release.tar` (ustar: mtime 0, uid and gid 0, mode 0644, fixed order).
- **It refuses by name:** `using` aliases, `using static`, `global using`, a `using` after a declaration or inside a namespace, an unreadable `using` line, a byte-order mark, a CR, a missing final newline, a non-regular entry or subdirectory in `model/`, and a `*.CS`-style name. It also refuses a template without exactly one placeholder on the `static readonly` line.
- **`verify <dir> <hash>`** is the consumer's binding check, as bytes:
  - `MANIFEST` is the pin;
  - each `file` matches;
  - `Tviburi.cs` is the listed body followed by the template filled with the pin;
  - the ledger reads strictly for that hash, and its rows are the manifest's `ledger-rows`;
  - the tarball holds exactly those files.
- **`cases <ledger> <guard> <base> <size>`** generates gate clause 6's cases from the ledger.
- **One home per pin:** `tools/tviburi-release/pins.tsv` holds the two build strings (formerly in `tests/model-version`), the stock chain's pins, and the class map, one `class` row per guard.

**(2) Identity.**
- `Records` reports `TviburiRelease.ManifestSha256`. `$TVIBURI_MODEL_DIR`, the directory hash, its helpers, five unused `using` lines and `tests/model-version` are gone.
- `scripts/build-release [tree [out]]` builds the program from the tree it releases, then the release, into `/out/release` by default. `scripts/prelude.resc` includes the release's one file.
- The runner takes the expected version from the manifest it built. It also refuses to run when `renode --version` is not the build the manifest names. That replaces the build strings the removed hash read at load.

**(3) The ledger.**
- **The schema line:** `{"ledger":"tviburi","release":"sha256:…","schema":1}`.
- **The rows:** canonical JSON (keys sorted, no whitespace), sorted bytewise by (`peripheral`, `register`, `field`), with no key twice. `row` is one of `field`, `model`, `tag`, `canned` or `bench`:
  - **field:** the guard type, the register and field, `offset`, `bits`, `state`, `allowed` (`"any"` or the values), `behaves_as`, `citation` (RM0433 Rev 9 section and page), and `reason` or `detail` where the data gives one;
  - **model:** (stock type, "", ""), `instances`, `files` (each `path sha256:…`, the pin), and `guarded_by` where the class map names it;
  - **tag:** ("Tag", name, ""), `range`, `file` (with its pinned sha256);
  - **canned:** (type, instance, ""), `file` (with its pinned sha256);
  - **bench:** (guard, "bench", B1…), `rule`, `bit`, `procedure`, `confirms`, `refutes`, `citation`, and `result` "not run".
- **This release's ledger:** 167 rows, of which 90 are SPI fields, 33 inherited models, 37 tags, 3 canned and 4 bench.
- **The field reader is strict.** Every line of `Fields` must be one `R(...),` row, and every symbol it names is resolved from the file's own declaration. Every bit 31:0 of every register must lie in exactly one row.
- **The bench rows** come from the new `model/bench-checks.tsv`: 0100b FEEDBACK §1's B1–B4, keyed by the rule and the bit the refusal names.

**(4) Recorded mode in `model/`.**
- `mode` is a constructor argument from the platform description, `"enforcing"` by default. Any other string is `TVI-LOAD`. The witness carries `mode`.
- In recorded mode, `Records.Refuse` logs `would-refuse` and does not pause. The guard's entry points pass the access on to the stock model and the copies.
- **One record per access:** the guard's rule chain already stops at the first rule that fires. So a recorded run logs at each access the same record an enforcing run would refuse with.
- `model/tviburi-nucleo-h753zi.repl` now states `mode: "enforcing"`.

## 3. Acceptance (WO §2), and where each is shown

`tests/spike.sh`; the test name is in brackets. The full suite passes: acceptance and adversarial, 74 checks (§6).

| Clause | Shown by |
|---|---|
| **Reproducible:** two builds, two checkouts (umask 022 and 077, mtimes 2000 and 2030), byte-identical tarball, manifest, overlay and ledger | [reproducible] |
| A changed byte in any `model/*.cs` changes the manifest hash and the witness's version | [identity]: one byte in `Refusal.cs`, one in the guard. Each still PASSes, with its own hash |
| `verify` passes A and fails B (edited model byte), C (swapped constant), E (stale manifest, under either pin) and D (no overlay) | [verify]; plus F: an edited ledger row with the tarball removed (§4, finding R1) |
| The witness's version is unchanged with `TVIBURI_MODEL_DIR` at another directory, unset, and with an empty environment (`env -i`) | [identity] |
| The ledger regenerates byte-identically | [reproducible] |
| A reader refuses rows out of order, a CR, a trailing space, or no schema line | the release's unit tests (`ledger.rs`), and `verify` |
| Every bit 31:0 of every guarded register is in exactly one row, checked by enumeration, by offset | the build refuses otherwise. Unit tests: gap, overlap, two offsets, two names at one offset. [adv-release] `rel-row` (a row removed) |
| In a run, each row's disallowed value refuses `unmodeled` and each allowed value passes, with cases generated from the ledger | [fields]: `tviburi-release cases` from the ledger gives 348 cases, 296 of them refusals (54 field values and 242 offsets with no register in the 0x400 region, which holds 14 registers). 303 records, each matched to its case by (offset, value). The 8 cases where an earlier rule answers first are listed and each must occur. `tests/fields.resc`, the hand-written list, is removed |
| Every `predicted-fault` that `conditions` produces has a bench row | [conditions]: the (rule, bit) set of the predicted faults equals the bench rows' |
| **Recorded:** `modf`, recorded, gives a `mode: "recorded"` witness, a `would-refuse` with `kind: predicted-fault`, the after-marker and an `end`, and the check does not pass it | [recorded] |
| Recorded accesses go on to the stock model and the copies, one record per access | [recorded]: monitor cases |
| `mode: "Recorded"` is `TVI-LOAD` | [load], now run on that platform: `TVI-LOAD` at seq 0, no witness, every access refused |
| Every existing test passes on the release | every run loads `/out/release/Tviburi.cs`; `scripts/prelude.resc` no longer names `model/` |
| CI green on the PR | §6 |

**Mutants (diff-sized):** one per guard this WO adds or changes, in `tests/mutants.tsv`:
- `T01`–`T03` and `T05`–`T07` (recorded mode, mode, version; `T04` is not in the file, see below);
- `U01`–`U10` (the release program: overlap, gap, a late `using`, the verify body, constant and rows, ledger order, sources, stock pin, the runner's build check);
- `F01` (the field reader's value lists);
- `M20`, `M22`, `M26` and `M31`, whose text moved; `S07` is removed (its line is gone).

Each was killed by its named test; the full run is in §6. **One guard has no killing test: `T04`** (recorded mode does not pause; §5, Q3).

## 4. The adversarial pass (WO §5)

**Mine, during the build:**
- **A release that differs from what compiled.** The build refuses each case by name, and each is a case in [adv-release]:
  - a symlink, a subdirectory, a `.CS` name;
  - a `using` alias, a `using` inside a namespace;
  - a row removed, an unknown class string, a row with a trailing comment;
  - a stock pin changed;
  - the template's `static readonly` made `const`;
  - a bench row naming another guard.
  
  Order is bytewise, never locale. The template must carry the placeholder exactly once, on the constant's line, and is itself hashed into the manifest.
- **A `using` that changes meaning when hoisted.** Aliases, `using static` and `global using` refuse. One residual stays, by the ruled design (`RUL-09` (a)): hoisting puts every file's namespace imports in one scope. An ambiguity fails to compile, which fails closed. But an extension method or overload from another file's namespace that matches better would bind silently. Today the two files import only `System`, `System.Linq`, `System.Text` and Renode namespaces.
- **An identity from a name:**
  - [adv-noname] checks that no `Environment.`, `Assembly.Location`, `GetExecutingAssembly` or `TVIBURI_MODEL_DIR` appears in `model/` or `scripts/`, and that `tests/model-version` is gone.
  - **Cache on, the second boot of a changed release** [adv-cache]: the cache directory was populated, and the second boot reported its own release, not the first's. With one file, a new constant is a new compilation unit. 1150 (iii) reasoned this; it is now measured.
- **A ledger that lies:**
  - the strict reader refuses a line it cannot read, and a symbol it cannot resolve (unit tests, one per row form);
  - every bit is in exactly one row;
  - inherited models are `inherited-unaudited`;
  - the release hash covers the ledger (R1, below).
- **A recorded run that passes anywhere:**
  - the check fails a `mode` that is missing, `"recorded"`, `"Enforcing"`, non-string, or given twice (new unit cases; `json.rs` refuses a duplicate key);
  - `verify` never reads a run;
  - the runner fails on the check's FAIL;
  - the mode is a constructor argument with no setter, read from the platform description only.
- **Found by me and fixed:** the `stock-forge`, `stock-modf` and `no-overlay` fixtures stopped matching once the platform said `mode: "enforcing"`. The stock model has no `mode` property, so `stock-modf` and `no-overlay` were passing for the wrong reason (a failed platform load). They now swap or remove the whole `spi1` entry.

**An independent review** (a fresh subagent on Opus, read-only, given the code and the WO's §5, not my conclusions). Its findings, each checked against the code:

| # | Finding | Verdict | Action |
|---|---|---|---|
| R1 | **The release hash did not bind the ledger.** `verify` passed a ledger whose model rows were edited to `guard`, with tag and bench rows deleted and the optional tarball removed | Reproduced | **Fixed:** the MANIFEST carries `ledger-rows <sha256>`, the hash of every line after the schema line. Those lines never name the release, so this is not circular. `verify` checks it (case F, mutant `U10`). On the bus, 13:51Z, because RUL-09 says the manifest excludes the ledger |
| R2 | `build-release` compiled the program from `/repo` while releasing another tree, so the `source` lines did not name the binary that ran | Valid | **Fixed:** the program is built from the tree it releases |
| R3 | Bit coverage was keyed by register name, so a second name at one offset passed | Valid | **Fixed:** keyed by offset; two names at one offset refuse (unit test) |
| R4 | The ledger lists `SPI1_/_I2S1` as a stock tag, while Tviburi's platform puts the guard on that range | Valid, out of scope | §5, Q4 |
| R5 | Stock rows named their file without its sha256; the board file and its LED, button and PHY models are not in the ledger; the infrastructure build string is checked by nothing | First valid; the others out of scope | **Fixed:** every stock file reference carries its pinned sha256. The rest is §5, Q5 |
| R6 | Tests passing for the wrong reason: no mutant for the field reader; [adv-cache] asserted nothing about the cache; [identity]'s ledger comparison could never fail (the schema line differs) | Valid | **Fixed:** mutant `F01`; [adv-cache] asserts a populated cache and the second boot's own hash; [identity] compares the rows only |
| R7 | Parsing edge cases: a typed C# 8 `using` declaration is refused as an alias; `fields.rs` resolves symbols file-wide; `build` reads the working tree, not a commit | Valid; each fails closed or is theoretical. The tests and CI build from `git archive` | Kept; listed here |

The reviewer also checked these and found them sound: the hoisting refusals; the template check; `mode` handling; recorded writes reaching the copies; cases B, C and E being same-length edits (so `U04` and `U05` really kill); and the absence of any identity from the environment.

## 5. Findings and questions for the architect

1. **RUL-16's reason says 38 tagged blocks;** the pinned chain has 37 `Tag` lines (ruled 13:45Z: the architect corrects the reason).
2. **`system_flash_ro` as `canned`** beside its memory's inherited row: accepted 13:45Z.
3. **`T04`: the pause after an enforcing refusal is not observable in Tviburi's monitor harness.** The `modf` firmware executed exactly 100,000 instructions in its 1 ms, enforcing or recorded, at the default quantum and at 10 µs. So no test here kills "recorded mode pauses", and the mutant is not in `tests/mutants.tsv`. RUL-08 already makes the halt best-effort, and the record the verdict. Whether the pause does anything under Tviburi's runner, as opposed to Aethelgard's (where 1150 saw enforcing boots stop early), is worth one measurement in a later WO.
4. **The `SPI1_/_I2S1` tag row (R4).** The ledger is the stock chain's; Tviburi's own platform and Aethelgard's both register a model over that tag's range. A consumer joining by register name never meets the tag. A `guarded_by`-style link from a tag to a class map entry would need the consumer's platform. Not built.
5. **Out of the WO's chain (R5):**
   - `platforms/boards/nucleo_h753zi.repl`, which Tviburi's test platform includes, is neither pinned nor in the ledger (LEDs, a button, the Ethernet PHY);
   - the `Infrastructure.dll` build string is named in the manifest and checked by nothing. The runner checks Renode's, from `renode --version`; reading the other needs .NET reflection.
6. **The `source` lines are now redundant for invariant 3.** With `ledger-rows` (R1), every byte the release ships is bound by the manifest without them. They still make the release hash move when an input that is not shipped changes, such as the platform's comment or the generator's code, and [identity] tests exactly that. Kept, as ruled 13:45Z; removing them is a reduction for your call.
7. **README's status line** says `TVI-WO-1200` "builds the release". After the merge it is built, not published (publication is Phase 2's). The README is protected; noted for the architect.
8. **Recorded mode's volume** (1150 FEEDBACK §8.4): ArduPilot's full boot now gives no `would-refuse` at all, so there is nothing to fold today.

## 6. Close

- **Gates, run on Furnace in the pinned image, each container under `~/bin/capped-run`, one at a time:**
  - `tests/spike.sh` with `TVIBURI_TESTS=all` (acceptance and adversarial): **74 checks, ALL PASS**, on the head after the reduction pass;
  - `tests/mutants.sh`, every row: **108 mutants, ALL KILLED**.
  - CI runs the same on the PR.
- **Reduction pass (diff): −14 net · 1 duplicate removed · 3 dead paths removed · 0 protected kept · 1 finding (§5 Q6).**
  - The duplicate: `t_adv-env`, folded into [identity], which now also checks that run's PASS.
  - The dead paths:
    - `ledger::text`, used only by tests;
    - the template check's `lines.len() != 1`, which cannot be true once the placeholder count is 1;
    - the runner's `cat` of an always-empty file on a failed release build.
  - Two unused derives also went.
  - Also removed by the WO itself: `tests/model-version`, `tests/fields.resc` (now generated from the ledger), `Records`' directory hash, and five `using` lines.
- **Merged `main`:** nothing to merge. `main` is `e20cae5`, and `TVI-WO-1300` has not merged. Whichever merges second carries the other: the RCC guard needs a `class` row in `pins.tsv`, a reader for its data's form (a guard without one refuses the release), and `mode` wired as for SPI.
- **Scratch:** `~/.cache/tvi-1200` holds the Aethelgard export, its CLI build, the capture, the replays, the measured overlay and the test outputs. It is kept until the PR is ruled; the CLI build (`target-aet/`) is removed at close.
- **Aethelgard is untouched;** its source was read through `git archive` into scratch.
