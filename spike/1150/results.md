# TVI-WO-1150 — the seam spike: results

**Status:** (i) measured. **WO §7's first stop fired at (i):** no `.repl` form re-types a stock instance. (ii)–(vi) wait on the architect's ruling (see the end of this file).
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

---

## (iii)–(vi)

Being measured, in order.
