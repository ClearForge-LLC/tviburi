# TVI-WO-1205 (1), (2): Phase 1's exit gate, clause by clause, on `main`

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1205-gate-judged`, from `main` `63fe557`.
**Judged on:** `main` `63fe557` and its CI run 45858432173: every shard green, and the aggregate reads "1000 expected (446 cases, 554 mutants), 1000 ran across 10 shards, 0 failures". Clause 1's final rows are on the post-`1308` `main` (§1.3).
**Evidence:** this file; `spike/1205/` (the queue join's scripts and data); `~/.cache/tvi-1205/` (the runs).

How each clause is judged:
- **The proving job, case and mutant,** by name. A mutant counts only if CI ran it and the named case killed it.
- **What a skeptic would try** to make the clause pass falsely, and the case that stops that.
- **The verdict:** met, partly met (the gap named), or not met.

## 1. Clause 1: every path up to the cut refuses by name

**The cut, as Scotty ruled `OQ-03`:** everything the armed run reaches on the four flight paths (motor; clock tree; sensor buses; command links) and their DMA routes (`TVI-WO-1100a.cut.md`).

**How a row is judged:**
- **Guarded:** a guard refuses it by name (a refusal for a disallowed value, or a modeled field).
- **Acknowledged:** a ledger row names its field (`RUL-17`).
- **Anything else is open.**
- **The method:** `1100a`'s, re-run unchanged on the later tree by `spike/1205/harness/pipeline.sh`. That is the armed run twice (the control, and one with every access logged) on Aethelgard `4400h`'s Sentinel-03 capture, 935 queue rows placed, each joined to the release's ledger and the guard bound at the instance.

### 1.1 On `main` `63fe557`, before `1308`

**Summary:**
- **Rows:** 935, the same set as `1100a`: guarded 610, acknowledged 253, inherited 72, unplaced 0.
- **The armed run:** arms; 0 records.
- **The table:** `spike/1205/data/main-63fe557/cut.txt`.

**Above the cut:**

| Family | Rows + armed registers | Guarded | Acknowledged | Open |
|---|---|---|---|---|
| RCC (tiers 1, 4a) | 88 + 19 | 65 | 42 | 0 |
| GPIO (tiers 2, 4b) | 108 | 85 | 23 | 0 |
| DMA1/2 streams | 85 + 30 | 52 | 63 | 0 |
| **DMAMUX1** | 0 + 10 | 0 | 0 | **10** (`1308`) |
| TIM1 | 67 + 9 | 57 | 18, then 19 with `TIMx_SR` read (below) | 0 (`1100a`'s form finding closed, below) |
| FLASH.ACR | 2 | 2 | 0 | 0 |
| ADC1 | 2 + 19 | 21 | 0 | 0 |
| I2C1/2 | 66 + 1 | 56 | 11 | 0 |
| **PWR** | 3 + 2 | 0 | 0 | **5** (`1308`) |
| SPI1/2 | 70 + 2 | 44 | 28 | 0 |
| USART1–3 | 162 + 5 | 87 | 80 | 0 |

**What is open, exactly:** the two families the cut named, and nothing else.
- **`dmamux1`:** channels C0–C7, C10 and C11 (offsets `0x00`–`0x1C`, `0x28`, `0x2C`), on the stock `STM32_DMAMUX`.
- **PWR:** `CR1`–`CR3` and `0x10`, `0x18`, on the stock Python stub.
- **Both are `TVI-WO-1308`'s guards** (#64).

**Since `1100a`:** only the RCC's split by tier moved. `1203` gave the RCC's registers their real names in the access log, so five armed registers now fall in tier 4a, not tier 1. The totals are unchanged.

**TIM1's `TIMx_SR`: read in full, and acknowledged by name** (architect 17:02Z and 17:35Z; it was `1100a` F3's form finding).
- **What the armed run does at TIM1** (run B, every access logged):
  - SR: written `0x0` three times, and never read. There are no TIM1 reads at all: 0 of 5,568,732 CPU read lines in the post-`1308` run B (`spike/1205/data/main-5d4a61a/tim1-accesses.tsv`; the pre-`1308` run gave the same).
  - DIER: `0x0`, so no TIM1 interrupt or DMA request.
  - CCMR1/2 `0x6868` and CCMR3 `0x01016868` (channels 1–6 outputs); SMCR never written; BDTR `0x8000` (no BKE/BK2E); CR2 `0`; EGR `0x1` (UG only: no BG, B2G, COMG or TG).
- **Each field against the stock model and RM0433 Rev 9 pp1596–1597:**
  - **UIF, CC1IF–CC4IF, TIF, CC5IF, CC6IF, BIF, B2IF, SBIF, COMIF:** `stock, wrong`.
    - **The twin:** the stock model sets UIF/CCxIF/TIF only with their interrupt enabled, and never sets the channel 5–6, break and COM flags.
    - **Silicon** sets each on its own event: a TRG event or TG (pp1597–1598); a break input with BKE/BK2E, or BG/B2G whatever those bits (p1567); a system break source (p1566); a COM event (COMG, or a TRGI edge with CCPC, pp1592, 1598); a compare.
    - **Each row says why ArduPilot's path is not affected,** quoting the ruling.
  - **CC1OF–CC4OF:** acknowledged by name, **A10** (`TVI-WO-0100b.rm0433.md` §4, granted 17:35Z). The stock model's overcapture matches silicon's, and ArduPilot's outputs never capture.
- **The text is generated:** it is set in `tests/1302/fields.py`, and the table is regenerated from it; no rule or behaviour changed. Re-joined on this ledger (`spike/1205/data/main-63fe557+a10/`): TIM1 is 57 guarded and 19 acknowledged, with 0 findings.
- **CC5IF/CC6IF on silicon** (setting at a compare with CCR5/CCR6 at reset 0) is inferred from p1597, not run.
- **The adversarial pass corrected this text:** the first version said BIF/B2IF set "only" with BKE/BK2E (false: BG/B2G, p1567) and cited SBIF's sources to the wrong page.

**Guarded on a sibling's case** (the adversarial pass's "sibling instance" question): 97 of the guarded rows above the cut are on an instance whose clause-6 sweep wrote another instance of the same guard. They are `usart2` 27 and `usart3` 27 (swept on `usart1`), `i2c2` 17 (`i2c1`), `spi2` 16 (`spi1`), `gpioPortB` 9 and `gpioPortD` 1 (`gpioPortA`).
- **Each is guarded, not merely inferred.** The run binds the guard's type at that instance (the platform listing). The rule that refuses is the same code, reading the same `Fields` table by offset, with no per-instance branch for these fields.
- **The evidence is the sibling's case,** which is why they are counted apart.

### 1.2 Clause 1's "a planted firmware"

As amended and ratified 2026-11-26: a planted monitor write proves a path whose rule reads only the value written. A planted firmware is kept where the rule depends on CPU state, order or timing.
- **The sweeps are monitor writes** (`pc: none`), and `1203` FEEDBACK §4 found no guard rule that reads the PC.
- **The order- and time-dependent paths each have a planted firmware case:** the SPI's enable sequence and MODF (`conditions`, `modf`), the RCC's start sequences (`firmware/rcc.rs`), I2C's NACK timing (`nack`, `nacktime`), USART's IDLE (`idle`).

### 1.3 On `main` after `1308`

**On:** `9864bce`, this branch with `main` `5d4a61a` (#64, `TVI-WO-1308`) merged. This branch adds only ledger text and mutants. The data is in `spike/1205/data/main-5d4a61a/`.

**The 16 replays,** every guard recorded, each the committed `tests/1301/replay-1301.sh` (harness `~/.cache/tvi-1205/h/loop.sh`):
- **The 14 generated boots and ArduPilot's fixture:** 0 records.
  - Every boot block's hash is identical to the runs before `1308`.
  - Witnesses rise by up to two, `dmamux1` and `pwr`: 32–34 per board, 34 on the fixture.
- **The armed run:** arms from 9.332 s, with 34 witnesses, 34 ends and 0 records.

**The join:** 935 rows: guarded 612, acknowledged 254, inherited 69, findings 0.

**Above the cut, nothing is open.**
- **653 rows:** 412 guarded, 241 acknowledged. 86 of the acknowledged carry a `stock-wrong` field, and 105 rest only on `inherited, unaudited (acknowledged by name)` fields (§1.4).
- **97 armed registers outside every row:** 71 guarded, 26 acknowledged.
- **The table below sums both:** 483 guarded, 267 acknowledged.

| Family | Rows + armed registers | Guarded | Acknowledged | Open |
|---|---|---|---|---|
| **DMAMUX1** | 0 + 10 | **10** | 0 | **0** |
| **PWR** | 3 + 2 | **4** | **1** | **0** |
| every other family | as §1.1, with TIM1's 19 acknowledged | | | 0 |

- **DMAMUX1** (`GuardedSTM32H7_DMAMUX`, bound at `dmamux1` in the run): C0's `CxCR` by its own sweep, and C1–C7, C10 and C11 by C0's rows and sweep. The guard judges every `CxCR` by C0's rows (`GuardedSTM32H7_DMAMUX.cs` L391), so their evidence is C0's case, as with the DMA streams.
- **PWR:** judged against **the platform the armed run boots**, not the board's.
  - That platform is Aethelgard 4400h's Sentinel-03, as rewritten by `replay-1301.sh`: the stock `pwr` cancelled (`pwr: @none`), and `tviburiPwr: Tviburi.GuardedSTM32H7_PWR @ sysbus 0x58024800 as "pwr"` with `revision: "V"`.
  - The board itself keeps the stock stub (`1308` Q17 (a)), so `pwr` is not in `scripts/guards`.
  - **The rows:** `CR1`, `CR2`, `CPUCR` and `D3CR` are guarded. `CR3` is guarded, but for `Res6`, which is acknowledged by name: rev V's reset sets it, and ArduPilot's `0x02` clears it (`1308` Q7). That counts by reading R3 (below).
- **Reading R3** (accepted by the architect 18:38Z, in his wording; set after this data, as `1100a`'s R2 was after its own): "a row of any state whose text quotes an architect's or Scotty's ruling by its timestamp is acknowledged by that ruling". It applies only where a row would otherwise count for nothing.
  - **It moves exactly one row,** `PWR_CR3.Res6`, from a form finding to acknowledged.
  - **Checked by diffing** every row's state, with R3 and without it, on both `main-5d4a61a` and `main-63fe557+a10`.

**Clause 6 for the two new guards:**
- **The sweeps:** `dmamux-fields` and `pwr-fields` are ledger-driven, like the five above.
- **Mutants** (granted 18:38Z): `DX34-fieldsSweep` and `PW39-fieldsSweep`, each the guard's `RefusedField` returning false, killed by `dmamux-fields` and `pwr-fields`. Before the grant, a scratch mutant failed both sweeps (`~/.cache/tvi-1205/h/c6mut-1308.sh`).
- **So clause 6 is met for all eleven guards.**

**Clause 1: met on post-`1308` `main`.**
- **Guarded:** 412 rows and 71 armed registers above the cut. Some are guarded by type, with the evidence from a sibling's case:
  - 97 rows on a sibling instance's sweep;
  - 9 DMA registers on stream 0's sweep (streams 1–7, and `dma2` on `dma1`'s);
  - 9 DMAMUX1 channel registers on C0's.
- **Every guarded row and register has a clause-6 case.** The join now fails one with none (its fourth change). It reads `adc-fieldsmonwrite` and `i2c-isrmonwrite`, which covered six ADC registers it had listed as "NO case".
- **Acknowledged by name:** 241 rows and 26 registers, `TIMx_SR` included in full; but see §1.4.

## 2. Clause 2: same build, byte-identical ledgers; any overlay file changes the hash

**Met.**
- **Proving cases** (all in the `tests/spike.sh` shards):
  - `reproducible`: two release builds from two checkouts (umask 022 and 077, mtimes 2000 and 2030) give an identical tarball, manifest, overlay and ledger.
  - `identity`: one byte changed in a model file, in a source the release reads but does not ship (the platform's comment), and in the bench data each gives a new release hash. The witness reports the hash of the release that ran.
  - `verify`: the consumer's binding check fails on an edited model byte, a swapped constant, a stale manifest, a missing overlay, and an edited ledger row.
  - `manifest`: every `restate.repl` regenerates from its generator, byte for byte.
- **Mutants:**
  - `U07-sourcesInManifest` (`identity`): a source dropped from the manifest.
  - `U04-verifyBody`, `U05-verifyConstant`, `U10-ledgerRowsBound` (`verify`).
  - `MF01`–`MF03` and the five restate mutants, all killed by `manifest`.
- **The skeptic:**
  - **A file the overlay depends on but the hash doesn't cover.** The `sources` line binds every file the release reads, its generator included. `identity` changes one that is not shipped, and `U07` drops a source line.
  - **A build that differs by environment.** `reproducible` varies umask and mtimes.
  - **Renode's compiler cache serving a stale overlay.** `adv-cache`: a changed release reports its own hash, or no witness.
- **Not proved, and not claimed:** that an identical hash means identical behaviour under a different Renode build. The manifest's `build renode` line pins that build, and `tviburi-spike` refuses any other.

## 3. Clause 3: removed, crashed or refused, a non-pass at both checks, in both framings

**Met.** The cells, framing × condition:

| | Removed guard | Crash | Refusal |
|---|---|---|---|
| **Tviburi's runner** | `adv-removed` (four removals), `removed-console` (runner half) | `crash-runner` (killed at spi1's first write, exit 137, after its witness) | `modf` (exit 3) |
| **Records-only check, `logFile` framing** | `stock-forge` (`no load witness`) | `crash` (no end record) | `modf`'s check output |
| **Records-only check, console framing (Aethelgard's committed `renode.config`)** | `removed-console` (only spi1's witness and end missing) | `crash-console` (witness seen before the kill, exit 137) | `unit` `the_console_fixture_is_read_in_both_framings` (the committed `aet-7dec1d7b` console holds a refusal) |

- **Mutants** (`unit`, the check tool's own tests, run in CI): `R06-endRequired`, `R07-witnessRequired`, `R16-consoleCr`, `R17-virtualStamp`, `R19-unframedRecord`. The runner: `S01-refusedExit`, `S04-scriptEnd`, `S05-afterMarker`.
- **The skeptic:**
  - **A timeout passed off as a crash.** `crash-console` requires the witness to be seen before the kill, and the exit to be 137, not a timeout.
  - **A check that reads only the log file.** `removed-console` and `crash-console` read the console in Aethelgard's framing.
  - **An end record lost at a clean quit, which a reader would take for a crash.** Fixed at its cause in `1204`. `teardown` holds the logger deterministically, and `TD01` kills a missing flush every time.
- **One cell rests on a fixture, not a live run:** the console-framed refusal is `aet-7dec1d7b`'s committed console, read by the check's unit test. The clause asks for "the committed console fixture included", so this is the cell it names.

## 4. Clause 4: the MODF scar refuses where stock Renode passes; the text matches RM0433 by page

**Met.**
- **Proving cases:**
  - `modf`: REFUSED (3), `TVI-SPI-MODF`, `predicted-fault`, after the witness, citing §50.5.2 p2184.
  - `stock-modf`: stock Renode runs the same firmware's two writes (CFG2 = MASTER|SSM, then CR1 = SPE with SSI = 0, p2184's trigger) to its end marker `0xAF7E0002`, Renode exit 0. Since `1203` it no longer passes on a missing witness alone.
  - `modf-page`: the refusal's citation, trigger and three effects each match RM0433 Rev 9 p2184's text, its section heading and its footer.
- **Mutants:**
  - `M06-ssi` and `M09-speGate` (`modf`);
  - `S02-checkMustPass` (`stock-modf`);
  - the `conditions` case's 53 mutants on the SPI guard's rule branches.
- **The skeptic:** **a page match against text written from memory.** `modf-page` compares against `tests/1203/rm0433-rev9-p2184.txt`, a committed excerpt.
  - `tests/1203/rm0433-p2184.sh` regenerates it from the held manual and matches on Furnace.
  - **CI cannot reach the manual,** so CI checks against the excerpt. Only a Furnace run checks the excerpt itself (`1203` FEEDBACK §5).

## 5. Clause 5: a firmware line that imitates a record is not one

**Met.**
- **Proving cases:**
  - `forge`: forged refusals on USART3 reach the log and are ignored; the run PASSes.
  - `stock-forge`: a forged witness and end on a stock model don't PASS.
  - `adv-spray`: record bytes written to SPI1 reach the log under `spi1`, and none is taken as a record.
  - `adv-hook`: a monitor write hook doesn't displace the guard.
  - `unit` `forged_records_on_the_console_are_not_records` (the console fixture).
- **Mutants:** `R02-sourceFilter`, `R13-unnamedSource` and `R19-unframedRecord` (`unit`).
- **The skeptic:** a forgery through a peripheral's own log line. That is `adv-spray`: the bytes land under `spi1`'s name, and the check still refuses them because they aren't a record's framing.

## 6. Clause 6: every field outside its allow-list refuses `unmodeled`, by name

**Met for the nine guards on `main`.** The two `1308` guards: §1.3.
- **The sweeps:** each guard's `fields` case writes every ledger row's disallowed value, and each must refuse naming that field, `unmodeled`; allowed values pass.
  - Guards and cases: SPI `fields`, RCC `rcc-fields`, GPIO `gpio-fields`, timer `timer-fields`, DMA `dma-fields`, flash `flash-fields`, USART `usart-fields`, I2C `i2c-fields` and `i2c-isrmonwrite`, ADC `adc-fields` and `adc-fieldsmonwrite`.
  - `1203` closed the 20 rows `1100a` found unproven.
- **Committed mutants:** `N02-fieldAllowList`, `N03-noRegister` and `F03-sweepEnable` (`fields`); `C12-allowList` (RCC); `G07-fieldRow` (GPIO); `DM17-field` (DMA); `I207-allowList` (I2C); `AD26-field` (ADC); `A01-sel5` (`allowlist`).
- **Mutants for the sweeps that had none** (granted by the architect, 17:02Z): `TM34-fieldsSweep`, `FL42-fieldsSweep`, `US63-fieldsSweep`, `I265-isrMonwrite` and `AD48-fieldsMonwrite`. Each makes the guard's `RefusedField` return false. Each is killed by its sweep (timer, flash and USART `fields`, `i2c-isrmonwrite`, `adc-fieldsmonwrite`).
  - **Before the grant,** the same mutation as a scratch run failed all 7 sweeps (`~/.cache/tvi-1205/c6mut.log`).
  - **CI:** this PR's run (`TVI-WO-1205.FEEDBACK.md` §2).
- **The skeptic:**
  - **A sweep that counts records without matching each to its write.** `1203` matched each record to its own write in `isrmonwrite` and `fieldsmonwrite`.
  - **A sweep that writes a sibling instance only.** Each sweep writes one instance by monitor, so another instance of the same guard type rests on the shared rule (§1's join names those rows).
  - **A disallowed value the sweep derives from the same table it checks.** The ledger and the guard's `Fields` are the same rows, regenerated from the manual's data (`allowlist`, `regen`). So a widened row widens both: the sweep proves the guard enforces its table, not that the table is right. **The table's rightness is RUL-17's citation per row, held by review, not by CI.**

## 7. Clause 7: a recorded-mode run is not a pass

**Met.**
- **Proving cases:**
  - `recorded`: a recorded witness, the would-refuse records, the run reaches its marker and an end, and the check FAILs.
  - `adv-recorded`: a recorded run with no `would-refuse` at all still FAILs.
  - `load`: `mode "Recorded"` is `TVI-LOAD`.
  - `unit` `a_recorded_mode_run_is_never_a_pass`.
- **Mutants:**
  - `R12-witnessMode` and `R18-wouldRefuse` (`unit`);
  - `T02-recordedKind` and `T03-witnessMode`;
  - `RM01`, `RM02`, `RM05` (`1204`'s every-rule contract);
  - `T01-modeChecked` (`load`).
- **The skeptic:** a recorded run with nothing to record. `adv-recorded`: the witness's mode alone fails the run.
