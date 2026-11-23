# TVI-WO-1150 — the seam spike: results

**Status:** (i)–(iv) and (vii) measured; (v) and (vi) in progress. **WO §7's first stop fired at (i):** no `.repl` form re-types a stock instance. The architect ruled at 11:00Z to continue, and added (vii).
**Predictions:** `predictions.md`, commit `f26ed6c`, before any run.
**Setup, for every item:**
- **Aethelgard:** `main` at `7dec1d7b`, cloned read-only to `~/tvi-1150-aet` with its push URL disabled. Its CLI and `aethelgard-bridge` were built from that clone (`cargo build --locked`) into this worktree's untracked `target-aet/`.
- **Image:** `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…`, the one `7dec1d7b`'s `container/IMAGE` names. Renode `1.17.0+20260907gitf1dd1b4af.aet1605`.
- **Containers:** each under `~/bin/capped-run` (8G, swap 0), at `nice -n 10`, `--network none`, one at a time. The CLI build was the only container with a network.
- **Renode configuration:** exactly what Aethelgard writes. It was taken from a captured `simulate --foreign` boot (`harness/capture-foreign.sh`), with only `history-path` moved to the container's scratch.
- **Marking:** "read, not run" marks a fact taken from source.

---

## (i) Re-typing stock instances (`RUL-09`)

**Method.** `harness/run-retype.sh` loads each form in `retype/forms/`, in the pinned build under Aethelgard's `renode.config`. Each form is `using "platforms/cpus/stm32h753.repl"` plus one change. Then, per form:
- `peripherals`;
- `retype/checks.py`, which reads back by reflection each instance's class, bus address, parents, `Frequency` and `Limit`, interrupt endpoints, and the RCC's registration table;
- a monitor write to TIM1 and to RCC;
- the poke firmware (`fw/pokes.rs`), whose CPU writes are RCC `APB2ENR`, `D2CCIP1R`, and TIM1 `PSC`, `ARR`, `DIER.UIE`, `CR1.CEN`;
- a read of NVIC `ISPR0`;
- a TIM1 block reset through `RCC_APB2RSTR.TIM1RST` (RM0433 Rev 9 §8.7.35 p442: offset 0x098, bit 0), checked by `PSC` reading 0 afterwards.

The spike classes:
- `retype/SpikeTimer.cs`: a subclass of `STM32_Timer` in `RUL-07`'s shape, which logs each write and passes it on.
- `retype/SpikeRcc.cs`: a wrapper that holds a stock `STM32H7_RCC` and forwards every access to it, because no subclass of that class can exist (below).

Excerpts of every form's console are in `retype/excerpts.txt`.

| Form | What it declares | Result |
|---|---|---|
| `c0-control` | spike classes under new names at free addresses | **loads.** `peripherals` lists `spiketimer (SpikeTimer)` and `spikercc (SpikeRcc)`. The control rules out a compile or constructor failure being read as "cannot re-type" |
| `u0-update-only` | `timer1:` / `frequency: 200000000`, no type (Aethelgard's own form, `AET-WO-4400g`) | **merges.** The class stays `STM32_Timer`, `Frequency` reads 200000000, and the interrupts and the `TIM1` alias are kept |
| `t1-full` | the stock entry restated in full, with the type changed | `Error E02: Variable 'timer1' was already declared, previous declaration was at :316:1.` |
| `t1b-full-update-first` | as `t1`, with Aethelgard's update placed first | E02 |
| `t2-type-only` | `timer1: Tviburi1150.SpikeTimer` | E02 |
| `t3-type-registration` | type and registration | E02 |
| `t4-type-ctor` | type and constructor arguments | E02 |
| `t5-type-registration-ctor` | type, registration and constructor arguments | E02 |
| `l1-local` | `local timer1: …` | E02 |
| `r1-full`, `r2-type-only`, `r3-type-registration` | the same for `rcc` | E02 (`previous declaration was at :513:1`) |
| `n1-timer-unregister-rename` | **not re-typing:** `timer1: @none`, then `tim1g: Tviburi1150.SpikeTimer` at `0x40010000`, with the alias, constructor arguments and interrupts restated | **loads.** `peripherals` lists `tim1g (SpikeTimer)` and no `timer1`. The CPU's writes reach the spike, each with its PC (`pc=0x08000028` and on). The monitor write reaches it with `pc=none`. `ISPR0` = `0x02000000`, so IRQ 25 is pending from the spike's update. The RCC's table maps `TIM1=sysbus.tim1g`, and `TIM1RST` resets it (`PSC` reads 5, then 0) |
| `n2-rcc-unregister-rename` | **not re-typing:** `rcc: @none`, then `rccg: Tviburi1150.SpikeRcc` at `0x58024400` | `Error E39: Unable to finish the registration of 'adc3'. Are all parents: ['sysbus', 'rcc'], registered?` Every stock entry's `rcc RegistrationPeripheralAlias.X` names the stock variable |

**Renode exited 0 on every failed load** (architecture §2.0 already says so). The failure is visible only on the console.

**Why, from source (read, not run).** In Renode `f1dd1b4af`, `src/Renode/PlatformDescription/CreationDriver.cs` L109–127 (`ProcessVariableDeclarations`) treats any entry with a type as a declaration, and allows "only one" "anywhere in the usings hierarchy". A second one is `HandleDoubleDeclarationError`, E02. Entries without a type are merged as updates (L131–168). `VariableStore.TryGetVariableInLocalScope` (L45–47) checks globals before locals, so `local` cannot shadow an included global. The `l1` run agrees.

**`STM32H7_RCC` is `sealed`,** measured by reflection in the pinned build: `STM32H7_RCC=True STM32_Timer=False`. It is also so in the source (renode-infrastructure `066a7f13`, `STM32H7_RCC.cs` L24). So `RUL-07`'s guard, a subclass of the stock model, cannot exist for the RCC, under any `.repl` form. A guard there would have to be a wrapper that holds the stock model, as `SpikeRcc` does, or a reimplementation. Only the wrapper's loading was run (`c0`). Whether it behaves as the stock RCC was not run, because `n2` could not place it at the RCC.

**The shortest form that works: none re-types.** The nearest working change is `n1`, and it is a consumer change, not a class map:
- the stock instance's registrations are cancelled (`timer1: @none`);
- a new name is declared at the same address, restating the alias, the constructor arguments and every interrupt line;
- the instance's name changes (`timer1` → `tim1g`), and Aethelgard judges the `peripherals` listing by instance name and model (review A3).

Not determined: whether the cancelled stock `timer1` object is still constructed and still connected to `nvic@24–27`. It is not on the bus and receives no writes, so it raises nothing here.

For `rcc` even that fails. Swapping it means restating the registration of each of the 58 entries the RCC's table holds (`checks.py`: "RCC registered 58"), or Aethelgard no longer including the stock file's entries. That restatement was not run.

**Against the predictions.**
- "A second entry with a type is an update": **wrong.** It is a load error.
- "`peripherals` names the new class whenever the load succeeds": holds for `n1`.
- "`rcc` is harder": holds, and for a stronger reason than predicted. The class is sealed, and every stock entry registers under the `rcc` variable.

**Ruling: contradicts `RUL-09` as amended.** "A class map in the manifest … the consumer's emitter applies it to the instances it declares" can serve only instances the consumer declares itself (the SPI instances, as `sentinel-02-own`'s `spi1` and `spi2`). For an instance a pinned stock file declares, no `.repl` form re-types it. This is WO §7's first stop condition: `RUL-09`'s class map needs Aethelgard to stop including the stock entries, or to cancel and restate each one (`n1`), which is a consumer change. For the RCC, `RUL-07`'s subclass shape is impossible too, and `RUL-04` and `RUL-13` make the RCC the first family.

**The architect's ruling (bus, 2026-11-23T11:00Z):** (i) is a finding, not the end. Continue (ii)–(vi), and add (vii), how a guard can cover the sealed RCC. (i) also gains a question: can the guarded instance take the stock name after `@none`? Both are answered below (§(i) addendum and §(vii)).

---

## (ii) The console fixture (`RUL-11`, B6)

**Method.** `harness/run-console.sh` runs `0100a`'s firmware against the unchanged `model/` on the Nucleo platform, exactly as Aethelgard's CLI runs Renode:
- its `renode.config` as `--config`;
- `--disable-gui --console --plain -e "<commands>"`;
- its environment: `HOME` and `TMPDIR`, plus the two `DOTNET` variables, and nothing else;
- its seed and quantum.

Three firmwares, `modeled`, `modf` and `forge`, each run under two environments: Aethelgard's empty one, and that one plus `TVIBURI_MODEL_DIR`. Each pair ran twice: as Aethelgard runs it (the fixture), and with `logFile` added, so that one run gives both framings. Probes covered the UART analyzer, `spray`, a multi-line message and repeated messages. The scratch reader is `reader/records.rs` (Rust, built with `rustc` alone, no build file). It applies `RUL-11`'s grammar to one framing at a time, and checks JSON with the reference check's own `json.rs`, unchanged.

**The fixtures are committed:** `fixtures/aet-7dec1d7b/`, named for the Aethelgard commit. `SOURCE.md` there pins the configuration's sha256, the image and the invocation.

**Results.**
1. **Both framings give the same records.** For every run with `logFile` (six fixture runs, plus `forge.analyzer`, `spray` and the probe), the reader's records from the console equal its records from the `logFile`, byte for byte. Adding `logFile` does not change the console's records either: the fixture run and the two-framing run agree for all six.
2. **The `logFile` framing is not what `RUL-11` (b) says under this configuration.** `log-timestamps-type = Virtual` applies to the `logFile` backend too. Its lines are `[00:00:00.000000000]  [INFO] spi1: {…}`: the 20-byte virtual stamp, then **two** spaces. The console's is the same stamp and **one** space. Neither has `RUL-11` (b)'s host stamp `[HH:MM:SS.ffff]`. Prediction: wrong.
3. **`0100a`'s unchanged check sees no record in either framing under Aethelgard's configuration.** Its parser takes only the 13-character host stamp (`tools/tviburi-check/src/main.rs` L47–66). For `modeled` with the model directory set, and for the console capture, it prints "no load witness … no end record" and exits 1. For `modf` the refusal is invisible to it as well: it says FAIL, not REFUSED (exit 3). It fails closed, but it does not name the refusal.
4. **Aethelgard's empty environment makes `0100a`'s guard refuse everything** (review A2, now run). The witness is a `TVI-LOAD` refusal, `seq` 0. Every guarded access then refuses, and the `end` carries `model_version: "unavailable"`. `modeled` gives 6 records and `modf` 4, every one but the `end` a refusal.
5. **`use-synchronous-logging = true`: no mid-line interleave.** In all 15 consoles, every line that carries a record starts with the stamp, once a `\r` at either end is removed. `0100a` F7's split was not seen, although the monitor's echo and listing ran right beside records.
6. **A `\r` does precede a record line on the console.** The `end` record printed after the `peripherals` listing begins with `\r` (`^M[00:00:00.001000000] [INFO] spi1: {"tviburi":"end",…}`). Every console has 418 `\r` characters. Aethelgard's `clean_lines` trims them, and so does the reader. **A reader that does not trim them loses the `end` record.**
7. **`collapse-repeated-log-entries = True` folded nothing.** Three identical messages, logged in a row under `spi1`'s name by the monitor, appear three times on both framings, with no `(2)`. Why was not determined: it may be synchronous logging, or the source. `0100a`'s folding was measured under its own configuration. `seq` still makes every record distinct, so it stays.
8. **`always-log-machine-name = False`:** the source is `spi1` on both framings, never `machine-0:spi1`. The record's own `peripheral` field says `sysbus.spi1`.
9. **A multi-line message:** each continuation line is unstamped and indented by four spaces (`    {"tviburi":"refusal","seq":99,…}`). The reader rejects it as "no stamp at column 0", and so would `log_stamped`. A message cannot carry a second record past the grammar.

**Adversarial (WO §5).**
- **`forge` with `showAnalyzer usart3`:** the firmware's three imitation lines reach the console as `[stamp] [INFO] usart3: [host: …] [00:00:00.0000]  [INFO] spi1: {"tviburi":"witness",…}`. The reader rejects all three: "source usart3 is not guarded". **A reader that searches a line for `spi1: {"tviburi"` anywhere accepts 5 lines here, 3 of them forged.** Anchoring the source immediately after the level is what stops it. Aethelgard attaches no analyzer, so under its own invocation the UART text never reaches the console at all.
- **`spray`:** record bytes written into `spi1`'s unguarded registers come out as the stock model's hex warnings ("Unhandled write … value 0x…"). The reader finds only the witness and the `end`.
- **The residue:** anything that logs under a guarded peripheral's name can write a record. Here that is the monitor (`probe.py`) and Aethelgard's own hooks, which log through `Logger.Log(self, …)` on `spi1` and `spi2` (its `AETHELGARD-CFG2` line). Both are consumer-side, not firmware. No firmware route to a guarded-name record was found.

**Stop condition not met:** no record a firmware can write was found under Aethelgard's configuration.

**Ruling.**
- **Confirms `RUL-11`'s amendment that the record is the text after `<source>: `.** It is framing-independent, measured on both backends.
- **Contradicts `RUL-11` (b)'s description of the `logFile` framing** under the consumer's configuration: virtual stamp, two spaces.
- **For `0100b`:** the check's parser must accept the 20-byte virtual stamp. It must trim `\r` as `clean_lines` does, and anchor the source after the level. Its fixture is `fixtures/aet-7dec1d7b/*.empty.console.txt`, and `*.modeldir.*` for a guard that loads.
- **For `1200`:** under the consumer's empty environment, today's guard is a `TVI-LOAD` on every access (the identity is (iii)'s).

### (i) addendum: can the guarded instance take the stock name? (architect, 11:00Z)

The same harness was used. The forms are `n3` and `n5` in `retype/forms/`, and the excerpts are in `retype/excerpts.txt`.

| Form | What | Result |
|---|---|---|
| `n3-alias-stock-name` | `timer1: @none`, then `tim1g: Tviburi1150.SpikeTimer @ {…} as "timer1"` with everything restated (`frequency: 250000000`), then Aethelgard's untyped `timer1: frequency: 200000000` | **The name is taken.** `peripherals` lists `timer1 (SpikeTimer)`, and `sysbus.timer1` is the spike. CPU writes reach it with their PC, IRQ 25 is pending, and `TIM1RST` resets it. **Aethelgard's update does not reach it:** `Frequency` reads 250000000. The update binds to the `timer1` *variable*, which is the cancelled stock entry, with no error |
| `n5-second-load` | first description: `timer1: @none`; then the monitor loads a second description that declares `timer1: Tviburi1150.SpikeTimer @ {…}` with everything restated | **The name is taken, and nothing is split.** Listing, class, `Frequency` 200000000 (as restated), IRQs to `nvic@24`, `@25`, `@27`, CPU and monitor writes, and `TIM1RST` all hold. A variable that is no longer registered can be declared again by a later description |

**Every change a consumer sees, for an instance a stock file declares:**
1. **Its platform must cancel the stock entry,** `timer1: @none`, and restate the instance in full: the registration with its RCC alias, the constructor arguments, and every interrupt line. The values come from the stock file the consumer pins (`stm32h7.repl` L316–325 for `timer1`).
2. **With `as "<stock name>"` (n3), the listing and name are unchanged.** But **every untyped update the consumer emits for that name must move to the new variable.** Otherwise it silently reaches the cancelled stock entry. Aethelgard emits exactly such updates for 14 timers and 8 USARTs (`sentinel-02-own.repl`, `AET-WO-4400g`).
3. **With a second description (n5), the name and the updates stay where they are.** But the consumer's monitor commands gain a second `machine LoadPlatformDescription` per run, and its sealed-platform hashing covers two files.
4. **Not determined:** whether the cancelled stock object is still constructed and connected. It is off the bus and receives no writes.
5. **The interrupt endpoints read back unnamed** (`?@1`…) in `n1` and `n3`, where `n5` and the stock entry show `sysbus.nvic@24` and so on. Only IRQ 25 was checked functionally (pending in all three). Why the read-back differs was not determined.

**Ruling: refines (i).** No `.repl` form re-types. Cancelling and restating in the consumer's own platform does serve stock instances, and the n3 form keeps the stock name. `RUL-09`'s class map is therefore buildable for instances the consumer emits or restates, with the update hazard in item 2.

---

## (iii) The release constant (`RUL-10`)

**The scheme prototyped** (`release/`):
- `make-release.sh` copies the model files and writes `MANIFEST`. That holds a format line, the Renode build string, a line naming `TviburiRelease.cs` as excluded, and `sha256  file` for each model file.
- The release hash is the sha256 of `MANIFEST`'s bytes. It is written into `TviburiRelease.cs`, which holds only the constant: the fixed template (`TviburiRelease.cs.template`) filled with that hash. The constant therefore never hashes itself.
- `consumer-check.sh` is what a consumer that hashes its sealed copies checks:
  - `MANIFEST` is the pinned hash;
  - every listed file matches, and no unlisted file is present;
  - `TviburiRelease.cs` equals the template filled with the pinned hash, byte for byte;
  - after the run: one witness, reporting the pinned hash and `mode: "enforcing"`, no refusal or `would-refuse`, and an `end`.

The guard is a spike copy (`overlay/Records1150.cs`, `overlay/GuardedSpi1150.cs`) that takes its version from `TviburiRelease.ManifestSha256`. No environment variable or path enters. Runs used `harness/run-cases.sh`: Aethelgard's configuration, invocation, empty environment, seed and quantum, on the Nucleo board with `modeled` firmware. The three overlay files were included in order: `TviburiRelease.cs`, `Records1150.cs`, `GuardedSpi1150.cs`.

| Case | Sealed copy | Witness's version | Consumer (pin = A `a4a12a17…`) | A consumer that checks only the witness |
|---|---|---|---|---|
| A clean | release A | `a4a12a17…` | **PASS** | PASS |
| B edited model file | A, with `GuardedSpi1150.cs` changed after release | `a4a12a17…` | **FAIL**: the file's sha256 is not the manifest's | **PASS: the constant matches while a model file differs** |
| C swapped constant | A, with release V2's `TviburiRelease.cs` | `0423ce0b…` | **FAIL**: the release file is not the template filled with the pin; no witness reports the pin | FAIL |
| E stale constant | release V2's files and manifest, with A's constant | `a4a12a17…` | **FAIL**: the manifest is not the pinned one (pinned V2: the release file is not the template filled with the pin) | **PASS** (pinned A) |
| D stock run | the platform's stock `SPI.STM32H7_SPI`, no overlay | no witness | **FAIL**: no witness, no end | FAIL |

- **A constant in one `include @` file reaches a class compiled from another.** `Records1150.cs` reads `TviburiRelease.ManifestSha256` from the earlier include. Each include compiles separately, but the earlier assembly is referenced.
- **With `compiler-cache-enabled = False` (Aethelgard's), the constant is fresh every boot.** A boot of release A, then one of C in the same home directory, reported `a4a12a17…` and then `0423ce0b…`.
- **With the cache on, a `const` goes stale silently.** The same pair reported `a4a12a17…` both times. C# inlines a `const` into `Records1150`'s assembly, and `Records1150.cs` is byte-identical between the releases, so the cached assembly is reused. A `static readonly` field (`release/TviburiRelease.readonly.cs.template`) with the cache on crashed the second boot instead: `FileNotFoundException`, the cached assembly's reference to the earlier `TviburiRelease` assembly by GUID, exit 134, no witness. That fails closed. With the cache off, `static readonly` was fresh as well.

**Ruling: confirms `RUL-10` as amended, with two conditions.**
1. **The constant alone proves nothing about the code** (B and E pass a witness-only consumer). Part (a), the consumer hashing its sealed copies against the manifest, carries the identity. Part (b), the constant, ties the run's witness to the pinned release and catches a swapped constant.
2. **The constant should be `static readonly`, not `const`,** and the consumer keeps the compiler cache off. A `const` under a cache goes stale silently.

---

## (iv) Recorded mode (`RUL-19`)

**The prototype:**
- **The mode is a constructor argument from the platform description:** `mode: "recorded"` (`overlay/nucleo-recorded.repl`). The default is `"enforcing"`, and any other value (`"Recorded"`, in `nucleo-badmode.repl`) is a `TVI-LOAD` refusal on every access. No environment variable is read.
- **The witness carries `mode`.**
- **A would-be refusal** is logged as `{"tviburi":"would-refuse",…,"kind":…}` at Error, the run is not paused, and the access goes on to the stock model as if passed. The kinds are a spike mapping, not `RUL-18`'s table: `predicted-fault` for `MODF`'s `SSI` = 0 branch, `IOLOCK` and `CFG2-SPE`, and `unmodeled` otherwise.

| Run (Aethelgard's configuration) | Records |
|---|---|
| recorded, `modeled` | witness (`mode: "recorded"`), end |
| recorded, `modf` | witness, `would-refuse` `TVI-SPI-MODF` `predicted-fault`, end. The firmware reaches its after-marker |
| recorded, `iolock` | witness, `would-refuse` `TVI-SPI-IOLOCK` `predicted-fault`, end |
| enforcing, `modf` / `iolock` | witness (`mode: "enforcing"`), `refusal`, end |
| `mode: "Recorded"` | `TVI-LOAD` refusal in place of the witness, then a refusal at each access |

**`0100a`'s unchanged check.** It cannot read Aethelgard's framing at all (ii, item 3). So the same four runs were repeated in its own runner's framing (no `--config`, `logFile`), using the check built from `tools/` at `dc55f98`:

| Run | Check |
|---|---|
| enforcing, `modeled` | **PASS** |
| enforcing, `modf` | **REFUSED** |
| recorded, `modf` | **FAIL**: `unknown record kind "would-refuse"` |
| **recorded, `modeled`** | **PASS** |

**Why.** The check fails a recorded run only when the run holds a `would-refuse`, and then only because the kind is unknown to it. It never reads `mode`, because its witness rule checks only that `model_version` is a well-formed sha256 (`tools/tviburi-check/src/main.rs`, the `witness` arm). **A recorded run in which nothing would have refused is indistinguishable from an enforcing pass to the unchanged check.** Prediction: confirmed (medium-high).

**Ruling: confirms `RUL-19`'s need for the check to fail closed on a missing or non-enforcing `mode`. As the check stands, it is not met.** That clause must land in `0100b`'s check before any witness carries a mode. The consumer check prototyped in (iii) does it: recorded `modeled` gives "the witness does not say mode enforcing", CONSUMER FAIL.

---

## (v)–(vi)

Being measured, in order.

---

## (vii) How a guard can cover the sealed `STM32H7_RCC` (architect, 11:00Z; measured only, no guard built)

The harness was `harness/run-retype.sh`, with each form's `.monitor` commands applied right after the load. The forms are `retype/forms/vii-*`. Each had the poke firmware's CPU writes (RCC `APB2ENR`, `D2CCIP1R`), a monitor write (`D2CCIP2R`), and `TIM1RST` with `PSC` read back.

| Candidate | Loads | The 58 registrations | Firmware writes reach it, with PC | A monitor command displaces it |
|---|---|---|---|---|
| **(a) a wrapper that owns an unregistered stock RCC and forwards** (`retype/SpikeRcc.cs`). `rcc: @none`, the wrapper at `0x58024400`, and **all 58 stock registrations restated onto it** (generated by `rcc/restate.awk`) | yes | **resolve, on the wrapper** (58). `TIM1RST` resets `timer1` | yes (`pc=0x0800001A`, `0x0800001E`); a monitor write shows `pc=none` | **no**: a hook and access logging set on it leave its writes intact (`a2`) |
| (a) without the restatement (`n2`, §(i)) | **no**: E39 on `adc3` | — | — | — |
| **(b1) a guard beside the stock RCC, stock moved** (`rcc/RccGuard.cs`). `rcc: @ sysbus 0x58027000` (an untyped update), and the guard at `0x58024400` with `stock: rcc` | yes | **resolve, on the stock RCC**, which stays declared and registered. `TIM1RST` through the guard resets `timer1` | yes | **no** (`b3`) |
| **(b2) beside, stock narrowed.** `rcc: @ sysbus <0x58024400, +0x50>`, and the guard on `<0x58024450, +0x3B0>` forwarding at offset + 0x50 | yes | resolve, on the stock RCC | yes, for `0x50` and above | not tested beyond (b1) |
| **(c) a bus hook on the stock RCC,** set from the monitor | yes (the stock platform) | unchanged | **yes:** the hook reads the PC through `sysbus.TryGetCurrentCPU()` (`pc=0x0800001A`), and a monitor write shows `pc=none` | **yes:** a second `SetHookBeforePeripheralWrite` on `rcc` silently replaced it (`c3`: 0 hook lines). `LogPeripheralAccess` set *after* the hook did not displace it (`c2`: the hook still fired) |

**What each costs, measured:**
- **(a)** needs the consumer to restate the RCC registration of 58 stock entries in its own platform. That is a large consumer change, mechanical and generated from the pinned stock file. **The owned stock RCC is unregistered, so its own log lines lose their source name** (`[WARNING] Unhandled write to offset 0x50 …` with no `rcc:`). A consumer that cites or filters RCC lines by name loses them.
- **(b1) leaves a bypass.** The stock RCC stays on the bus at its new address, and a write there reaches it with no guard line: the monitor write to `0x58027050` landed on `rcc` directly. On silicon that address is unused, so firmware has no reason to write it, but the twin would answer it.
- **(b2)** has no second address, but **the stock RCC answers `0x00`–`0x4F` unguarded**: `CR`, `CFGR`, the domain prescalers, `PLLCKSELR`, `PLLCFGR` and the PLL dividers. Those are clock-tree registers too (RM0433 Rev 9 §8.7, offsets from the register map). The narrowed registration was accepted for a peripheral whose size is 0x400.
- **(c)** is displaceable by one monitor command, as architecture §2.0a recorded for SPI, and cannot be set from a `.repl`.

**The ruling this leaves open (the architect's):** (a) and (b) each work. (a) is closest to `RUL-07`'s "guard owns the access", at the price of 58 restated registrations. (b1) is the smallest consumer change, with a bypass address. (b2) splits the RCC at a register boundary.
