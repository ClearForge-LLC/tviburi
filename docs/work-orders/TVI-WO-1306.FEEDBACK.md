# TVI-WO-1306 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1306-i2c-guard`, from `main` `48abb6b`; `main` `f44fb73` (#47, `1300b`) and `3eb52b4` (#48, docs) merged before the push.

**State:**
- §1.1 measured first, with predictions committed before any run (`b111160`). The stop went to the bus at 04:36:54Z (`TVI-WO-1306.measure.md`); every question was ruled at 05:06Z (Q1–Q12) and built to that ruling.
- **The eighth shard came first**, as its own commit (`4aaaedc`, ruled Q12), with `1300b`'s 11 cases and 17 mutants re-weighed by `1202a`'s method from CI run 45545853730, the one the ruling named (`#47`'s pull-request run, at its head `14fc84f`).
- **Granted outside the fence (Q4):** the RCC guard's `watched` set retyped to an interface, `IRccWatched`, which the USART and I2C guards implement; the USART guard gains its three members. Nothing else in either guard changed. `1300b`'s USART cases pass unchanged; three of its mutant rows (`RB01`, `RB02`, `RB13`) follow the retyped text, killed as before.
- **Built differently from the letter of the ruling** (named here and on the bus with READY):
  - the adversarial pass added three `TVI-I2C-TRANSFER` refusals under Q8's rule (§5 F1–F3): `TXDR` outside a write transfer, `STOP` with no transfer, `START` with `STOP`;
  - `ICR.ADDRCF` and `ISR.TXE` allowed at 0 only (F6, F8), where Q11's table had them allowed: no firmware writes either;
  - every sub-word access refuses (`TVI-I2C-WIDTH`), where my stop proposed judging a byte on its word: "as measured" (Q11), and every measured access is a word;
  - an empty `START` is stripped of `START` in recorded mode rather than passed on, so Renode does not abort (Q8 said never passed on; RUL-19 says recorded passes on);
  - the absent-address acknowledgement is per port, the union of both firmwares', where Q9 said "per firmware" (F9: the guard cannot know the firmware, `RUL-10`);
  - an acknowledged absent `START` ends the data phase, as silicon's NACK and automatic STOP would (p1975), so the next `START` is valid; found in review after the adversarial pass, planted as `absentnext`.
- **The replays** (§3): the 14 generated boots, ArduPilot's fixture and the armed run, on `main` and on this branch in one harness, are identical on every RCC, I2C, USART, sensor, dump and boot-block line, but the dumped `TIMINGR`, which now reads back as written. No I2C record in any run. §7's second stop did not fire.
- One PR, left unmerged.

## 1. What was built

`model/GuardedSTM32F7_I2C.cs`, with its generated table (`tests/1306/fields.py` from `tests/1306/data/rm0433-rev9-i2c-fields.tsv`, extracted from the held text by `tests/1306/extract.py`), its restatement (`model/stm32h7-i2c-restate.awk` → `.repl`), the board's three modes, the class row, cases in `tests/1306/i2c.sh` and planted firmware in `firmware/i2c.rs`.

| Ruling | Built |
|---|---|
| **Q1** the form | `RUL-07`'s wrapper, a `SimpleContainer<II2CPeripheral>` whose owned stock instance shares its `ChildCollection` (one reflection write; a failure is `TVI-I2C-CONFIG` at every access). `i2c1`–`i2c3` guarded, `i2c4` stock. Sensors declared `@ tviburiI2cN` (the replay repoints the consumer's `@ i2cN`) |
| **Q2** protected fields | `TVI-I2C-PE`, `unmodeled`: `TIMINGR` (p2008), `CR1` `DNF` (p2003), `ANFOFF`, `NOSTRETCH` (p2002) changed while `PE` = 1. Same value passes; a write that clears `PE` is judged as with `PE` = 0 |
| **Q3** the start's clock | `TVI-I2C-KERNEL` at `PE` 0 → 1 by `QueryKernelClock`: not running is `predicted-fault` (p1954, p371); no rule (`i2c3`) `unmodeled`. With `I2CxEN` = 0 the bus lock drops the write with no record (case `clock`): covered, not duplicated |
| **Q4** registration | `IRccWatched`; an I2C guard the RCC guard does not hold refuses every access, `TVI-I2C-CONFIG` (case `unheld`). Through the interface, `TVI-RCC-CLOCK` also watches a running I2C port (`PE` = 1), naming `PE` and p1954 (case `clocklost`) |
| **Q5** reads | `CR1` and `TIMINGR` from copies holding their defined bits only; `TIMEOUTR` and `PECR` read 0; the rest from the stock model. The copies reset at the guard's `Reset()`, which `I2CxRST` runs (case `rccreset`) |
| **Q6** software reset | At a `CR1` write that clears `PE`: the owned instance reset, `CR1`, `OAR1`, `OAR2` and `CR2` (but `START`/`STOP`) restored (p1959, p2011). `TVI-I2C-RESET`, `unmodeled`: `PE` set as the very next access after the one that cleared it (p1960) |
| **Q7** pre-enable access | The existing bus lock, no new code (case `clock`) |
| **Q8** transfers | `TVI-I2C-TRANSFER`, `unmodeled`: `START`, `STOP` or `TXDR` with `PE` = 0; `START` with `NBYTES` = 0 and `RELOAD` = 0; `START` while a transfer is open (silicon queues it after `NBYTES`, p2005; the stock model starts at once); `CR2`'s `NBYTES`/`HEAD10R`/`ADD10`/`RD_WRN`/`SADD` changed while open (p1973). **Added by the adversarial pass (§5), under the same rule:** a `TXDR` write outside a write transfer's data phase (p2014); a `STOP` with no transfer (p2005); `START` and `STOP` in one write. An empty `START` (`NBYTES` = 0) never reaches the stock model, recorded or not (recorded: passed with `START` cleared) |
| **Q9** absent address | `TVI-I2C-ABSENT`, `unmodeled`, but the measured (port, address) pairs, acknowledged with no record: `i2c1` 0x30 0x38 0x39 0x48 0x54 0x55 0x76, `i2c2` 0x30 0x38 0x39 0x54 0x55. Passing one, the guard writes "Unknown slave at address N." under its own name |
| **Q10** `ICR` reserved bits | Bits 1, 2 and 6 acknowledged by name (rows `Res2_1`, `Res6`, p2012); 0, 7 and 31:14 `unmodeled` |
| **Q11** allow-list | 97 rows over 11 registers, every bit 31:0 once; 51 fields allowed only at 0 (case `fields` gives each outside `ISR` its widest value: 49). The adversarial pass moved `ICR.ADDRCF` and `ISR.TXE` to 0 only (§5 F6, F8): no firmware writes either |
| **Q12** eighth shard | Built first (`4aaaedc`); this WO's 38 cases and 44 mutants added to `times.tsv` at the USART rows' medians, unmeasured |

## 2. Acceptance

| Clause | Evidence |
|---|---|
| A planted firmware writing `TIMINGR` with `PE` = 1 refuses, naming the port, the field and the page; with `PE` = 0 it passes and reads back as written | `timingrpe` (`unmodeled`, as ruled Q2: `sysbus.i2c1`, `I2C_TIMINGR`, `SCLL (7:0) changed with PE = 1`, p2008); `timingroff` reads `0x00300F38` |
| A planted firmware setting `PE` with `I2C123SEL` on a stopped source refuses at the start, by the query | `kernel` (`predicted-fault`, `RCC_D2CCIP2R.I2C123SEL = 01`, p1954, p416) |
| ArduPilot's boot, recorded, and the armed run: no I2C `would-refuse` outside the acknowledged table; sensor transfers unchanged | §3: zero I2C records; every BMP581 line identical |
| The 14 generated boots: observers unchanged, or each difference named | §3 and §4 |
| The budget, judged in CI | §8 |
| CI green; every case and mutant in a shard, the aggregate showing it | §8 |

## 3. The replays

`main` (`f44fb73`) and the branch in one harness (`h/replay-b.sh`: the branch's committed `replay-1301.sh` with access logging), every guard recorded, `rcc`, `i2c1`–`i2c4`, `usart1`–`usart3` logged. Compared line by line (`h/compare.py`; evidence `~/.cache/tvi-1306/r-main`, `r-br`, `identity3.txt` for the final tree).

| Runs | Lines compared a run | Differences | I2C records | "Unknown slave" under `i2c1`/`i2c2` |
|---|---|---|---|---|
| ArduPilot's fixture | 99,413 | the dumped `TIMINGR` (0 → `0x00300F38`) | 0 | 58 |
| The armed run (Sentinel-03, 13 s) | 1,140,170 | the two dumped `TIMINGR` (`0x00707CBB`) | 0 | 110 |
| Ten generated boots with an I2C device | 20,134–63,515 | the dumped `TIMINGR` of each configured port | 0 | 1 each (the TMP117 at `0x48`) |
| `sentinel-02-bench`, `-bench-hse8` | 154–158 | the dumped `TIMINGR` | 0 | 0 |
| `d1-drv8876`, `driver-judges-nothing` | 4,581–4,605 | none | 0 | 0 |

Every other Tviburi record is the same on both sides (the armed run's two `TVI-DMA-FIELD`, `main`'s).

**Found by the replays, fixed before the suite:** the generated firmware clears `STOPF` (`ICR` = `0x30`) after a read with `AUTOEND` and before its next `START`. The first build closed an open transfer only when it saw the end at a `CR2` write, so that `START` was a false `TVI-I2C-TRANSFER` on 12 boards. The guard now checks for the end at every access, before passing it on; `blindclear` plants the pattern with no `ISR` read, and mutant `I236` shows the check is load-bearing.

## 4. Consumer observers (Aethelgard `4e5b0d3b`), by board

- **Unchanged by the rules on every board:** nothing in Aethelgard reads `TIMINGR` or `CR1` back, access-logs an I2C port or judges a transfer from the controller's registers (measure §2.5). The sensor witnesses (`[NOISY] i2c1.uN: …`) are identical (§3).
- **`absent_address` (`renode.rs`:2257–2268) is unchanged** on the ten generated boards that probe `0x48` (`sentinel-01`, `-minimal`, `-ext-bmp581`, `motors-pwm`, `motors-dshot600`, `serial-dshot-9600`, `hse-crystal-16`, `hse-external-8`, `sentinel-02-shaped`, `sentinel-03`): the guard writes `[WARNING] i2c1: Unknown slave at address 72.` under its name (ruled Q9). The owned instance's source-less copy stays beside it.
- **Changed by the form, for the consumer's Phase 2 (a seam row, ruled Q1; no Aethelgard change here):**
  - the `peripherals` kind of `i2c1`–`i2c3` (`STM32F7_I2C` → `GuardedSTM32F7_I2C`), on every board: the class map's step, as for the earlier guards;
  - **every sensor Aethelgard declares `@ i2cN` must be declared `@ tviburiI2cN`**, or the load fails, E39 (measure §2.4). New with this guard. Tviburi's replay does it for the captured platforms;
  - the owned instance's own WARNING and INFO lines lose their source name ("Unhandled write …", "Slave address …"); none is parsed by Aethelgard.
- **Aethelgard's ledger text goes stale:** the `TIMINGR`, `DNF`, `ANFOFF` rows' "not stored (measured)" (`ledger.rs`:1133–1191) and `boot.rs`:1043's `unobservable` entry (a consumer row, `OQ-04`).

## 5. Adversarial pass

A full pass (WO §5), by an independent reader given the code, the WO and the held text, not my conclusions (`~/.cache/tvi-1306/adv/i2c-guard-review.md`), then each finding checked against the code and the text by me.

| # | Finding | Kind | Done |
|---|---|---|---|
| F1 | A `TXDR` write in a read transfer, or after a write's `NBYTES`, passed: the stock model hands the byte to the device | silent pass | **Refused**, `TVI-I2C-TRANSFER` (p2014). Case `txdrread`, mutant `I239` |
| F2 | A `STOP` with no transfer passed and the stock model set `STOPF`; a `STOPF` left from an earlier transfer then ended the next one at once (the stock `START` leaves `STOPF`, as silicon's does), so a mid-transfer `START` or `CR2` change passed | silent pass | **Refused** (`STOP` outside controller mode, p2005); a `STOPF` set before a `START` ends nothing until cleared. Cases `stopidle`, `stalestop`; mutants `I241`, `I242` |
| F3 | `START` and `STOP` in one write: the stock model ignores both, and the guard then held a transfer open that never existed | false refusal later | **Refused** at that write. Case `startstop`, mutant `I240` |
| F4 | Recorded mode: an empty `START` with `PE` = 0, or with `RELOAD` = 1, reached the stock model's crash path | crash | **Never passed on**, on any path. `recorded` extended; mutant `I244` |
| F5 | `ISR` read `0x3` after an RCC `I2CxRST` in a write transfer: the stock `Reset` leaves `TXIS` | a read that lies | **Fixed** (the reset runs the stock model's `PE` = 0 path). `rccreset` extended; mutant `I243` |
| F6 | `ICR.ADDRCF` allowed: in controller mode the stock model clears `TXIS` with it | silent difference | **0 only** (target mode; no firmware writes it) |
| F8 | `ISR.TXE` written 1 (a flush) allowed, where the stock model holds no `TXDR` | silent difference | **0 only** (no firmware writes `ISR`) |
| F10 | Two condition texts overstated RM0433: a mid-transfer `START` is a *queued* repeated `START` on silicon (p2005), and `CR2` changes are forbidden only "as long as the START bit is set" (p1973) | wrong text | **Texts corrected**; both stay `unmodeled` (Q8) |
| F7 | A read transfer completes (`TC`, or `STOPF` with `AUTOEND`) only when `RXDR` drains in the stock model; silicon sets it when the last byte is received (p1979). A firmware polling `TC` before reading the last byte waits | gap | **Row** (§6). No measured firmware polls it so |
| F9 | The absent-address set is a per-port union (any firmware may probe any of the listed addresses), where Q9 said "per firmware" | ruling | **Row** (§6): the guard cannot know the firmware (`RUL-10`) |
| F11 | Recorded mode drops a sub-word or undefined-offset access, and a non-zero `TIMEOUTR`, rather than passing it on | recorded only | **Row** (§6); enforcing refuses each |

**Withstood:** sub-word writes (every one refuses); `PE` cleared and a field changed in one write (judged as `PE` = 0, then the software reset); `TIMINGR`'s reserved bits (refused, and masked in the copy); a `PE` 0 → 1 with any access between counts as the check (ruled Q6); a half-read transfer stays open (silicon stretches SCL, p1978); a RESTART after `TC`; `I2CxRST` resets the copies and the transfer state; the shared `ChildCollection` (sensors are disposed once); `IsRunning` read by `TVI-RCC-CLOCK` without a lock (an atomic word read; no deadlock).

**Re-proved after the fixes:** the 16 replays again (identical, as §3), the 38 cases, and the 44 mutants (§7).

## 6. Rows and findings

- **Seam row for Aethelgard, Phase 2 (ruled Q1):** its generated platforms declare sensors `@ i2cN`, which fail E39 on the guarded twin; they must be declared `@ tviburiI2cN`. Also the `peripherals` kind, as for the earlier guards.
- **Upstream (ruled Q8):** the stock `STM32F7_I2C` hands a device an empty write for a `START` with `NBYTES` = 0, and Renode aborts on a BME280 (measure §2.3 I8b), depending on the transmit queue's state. A firmware can crash the simulator with one `CR2` write. The guard never passes it on.
- **Roadmap row (ruled Q9 (c)):** model `NACKF` (silicon NACKs an absent address and sends a STOP, p1975; the twin hangs). Then the acknowledged addresses can go.
- **Q7, named:** a read of an I2C port with its `I2CxEN` = 0 returns 0 through the bus lock, where p373 says "invalid data". No measured firmware uses the value (ArduPilot writes 0 whatever it read).
- **F9, a ruling question:** "per firmware" cannot be built, since the guard cannot know which firmware runs (`RUL-10`). The set is the union per port: ArduPilot's probes (`i2c1`, `i2c2`) and the generated TMP117 at `0x48` (`i2c1`). The ledger states it, and case `ledger` holds the ledger's text to the code's table.
- **F7:** a read transfer's completion comes at the last `RXDR` read in the stock model, not at the last byte received (p1979). A stock-wrong row for a later I2C work order, with `NACKF`.
- **F11:** recorded mode drops sub-word and undefined-offset accesses instead of passing them on (as the USART guard does); enforcing refuses each.
- **A stale `STOPF` at an `AUTOEND` start:** a transfer started with `AUTOEND` while an earlier `STOPF` is still set has no end the guard can see, so the next `START` refuses (`unmodeled`). No measured firmware does it: the generated firmware clears `STOPF`, and ArduPilot never uses `AUTOEND`.
- **The teardown race** (`ci-runner-speed-and-race`): `config` lost its `end` records once in this build's first suite run, then passed 3 of 3. The race, not the guard: no other case lost one, and `timingrsame`/`timingroff` run the same sequence.
- **Aethelgard's bus-fault scenarios (seam row):** its analog pass leaves a faulted bus's devices out of the `.repl` (`platform.rs`:664–674). On the guarded twin the firmware's `START` to its sensor's own address is then `TVI-I2C-ABSENT`, `unmodeled`, where the stock twin times out and Aethelgard judges `i2c_timeout`. Honest (the twin cannot say silicon's NACK), but it changes that scenario's verdict: a consumer step, or `NACKF` modeled first (the roadmap row above).
- **`i2c3`** is guarded with no committed firmware using it; every start answers `unmodeled` (no query rule).
- **`TVI-RCC-CLOCK` now watches a running I2C port** through `IRccWatched` (ruled Q4). My stop had called a clock lost under a running I2C a row; the ruled interface brought it in, and case `clocklost` plants it.
- **The guard-name list is stated in eleven places** (`tests/spike.sh` ×6, each guard's harness, `scripts/tviburi-spike`): the reduction reader's finding. Out of scope; a list in one file the others read would make each guard's append one line.

## 7. Gates

- **Cases:** 38 in `tests/1306/i2c.sh`, run locally on the final tree, all passing (one teardown-race loss, §6, passed on re-run). **Mutants:** 44 (`I201`–`I245`; `I214` folded into `I213` by the reduction pass), each killed locally, plus `RB01`, `RB02`, `RB13` (the retyped RCC text), `C26` and `G17`.
- **Reduction pass (diff), run twice (before and after the adversarial fixes):** −6 lines net in code · 2 duplicates removed (the USART guard's `UeSet`, folded into `IsRunning`; `TVI-I2C-TRANSFER`'s two `PE` = 0 branches, one for `TXDR` and one for `START`/`STOP`, merged, with their two mutants) · 0 dead paths · 2 protected kept (the acknowledged addresses as ledger text and as the guard's table, now held equal by `ledger` and mutant `I238`; the per-guard `Row`/`R()` plumbing, the codebase's convention) · 1 finding (§6: the guard-name lists). Rejected: dropping `R()`'s class argument, which the release reads for each ledger row's state.

## 8. Budget and CI

- **The budget, locally, on the built guard:** `tests/1301/ci-memory.sh`, `main` (`3eb52b4`) against this tree, five boots a side: `MEDIANS main 1020876 KiB, branch 1012304 KiB, difference -8572 KiB (-8.4 MiB)`, **PASS**, every branch boot under 1,572,864 KiB. The §1.1 prototype measured −15.8 MiB with eleven boots a side. CI judges it again (below).
- **Wall time:** no slowdown seen in the replays. The branch ran 6–17 % faster than `main`'s side, which ran under heavier load, so this is noise, not a gain. The guard reads the stock `ISR` once per access, and the RCC guard's `Holds` lookup runs at every access.
- **Run locally on the final tree, unmutated:** the 38 cases, `spike.sh`'s own 36 cases (`unit` through `adv-cache`), `1300b`'s USART cases, `rcc-tim2`, `conditions` and `modeled`; and the 44 mutants plus `RB01`, `RB02`, `RB13`, `C26` and `G17`, each killed.
- **CI, the first run** (45559350378, at `86c6cee`): **green**.
  - `ci`'s wall was **19.2 min** (06:16:14Z → 06:35:28Z).
  - The aggregate: "795 expected (348 cases, 447 mutants), 795 ran across 8 shards, 0 failures".
  - The budget in CI: `MEDIANS main 1006948 KiB, branch 1005308 KiB, difference -1640 KiB (-1.6 MiB)`, **PASS**.
- **The shards in that run, by job and CPU:**

  | Shard | Cases, mutants | CPU | Speed (1202a) | Job (min) |
  |---|---|---|---|---|
  | 1 | 45, 52 | 6975P-C | 1.00 | 11.6 |
  | 2 | 40, 58 | 6975P-C | 0.99 | 11.7 |
  | 3 | 51, 52 | **EPYC 7763** | **1.50** | **17.3** |
  | 4 | 44, 55 | EPYC 9V74 | 1.10 | 12.7 |
  | 5 | 42, 57 | 6975P-C | 1.01 | 12.0 |
  | 6 | 43, 57 | 6973P-C | 0.96 | 11.3 |
  | 7 | 42, 56 | 6975P-C | 1.00 | 11.7 |
  | 8 | 41, 60 | EPYC 9V45 | 0.93 | 10.6 |

- **The re-weigh (Q12), pushed after this run:**
  - `1300b`'s 28 items are weighed as the mean of runs 45545853730 and 45559350378, each run against its own 6975P-C median.
  - This WO's 82 items are weighed from 45559350378. A mutant run by two cases weighs 12.8–13.3 s, where the USART median it replaced was 6.3 s. That is why shard 3 ran longer than the plan.
  - On the new weights eight shards plan evenly at 627 s each (5,016 s in all). That is **16.9 min a job at 1.53**, and `ci`'s wall 18.6–18.9 min.
