# TVI-WO-1306a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25/26. **Branch:** `wo/tvi-1306a-i2c-nack`, from `main` `a896644` (unchanged at the push: nothing to merge).

**State:**
- §1 measured first, with predictions committed before any run (`7ccff87`). The stop went to the bus at `dc672d2`; the architect ruled at 2026-11-26T00:22Z (Q1–Q11), and this is built to that ruling.
- **The WO's error, corrected by the measurement (ruled Q1, at the architect's request):** the WO said "with `AUTOEND` = 0 the bus waits" (from memory). RM0433 Rev 9 p1975 sends a STOP after a NACK whatever `AUTOEND` (Figure 556, p1976), and every measured absent `START` but one has `AUTOEND` = 0. The model sends the STOP in both cases; the acceptance's `AUTOEND` = 1 write is planted (`nack`, `nacktime`), and so is the `AUTOEND` = 0 path both firmwares take (`nackpll`, `nackstop`).
- **Granted outside the fence (Q2):** one read-only public method on the RCC guard, `KernelHz`, beside `QueryKernelClock`, and its three private helpers. Nothing else in the RCC guard changed. `1300b`'s, `1306`'s and `1307`'s cases pass unchanged (§7).
- **Two of `1306`'s mutant rows withdrawn with the code they guarded** (`I220`, the `Absent` table's acknowledgement; `I238`, the table against the ledger text), and two follow text this WO changed, killed as before (`I216`, `I245`). Their `times.tsv` rows follow.
- One PR, left unmerged.

## 1. What was built

`model/GuardedSTM32F7_I2C.cs` (the NACK), `model/GuardedSTM32H7_RCC.cs` (`KernelHz`), the ledger text in `tests/1306/fields.py` (the C# table regenerated from it), eight cases in `tests/1306/i2c.sh`, planted paths in `firmware/i2c.rs`.

| Ruling | Built |
|---|---|
| **Q1** `AUTOEND` | a STOP after a NACK whatever `AUTOEND` (p1975) |
| **Q2** timing | `NACKF` nine SCL periods after the `START` (p1955), the period by p1971 from `TIMINGR`, `DNF` and `KernelHz`, each tSYNC at its stated floor (two kernel clocks plus `DNF`). No frequency (a PLL outside p404's ranges, an HSE with no `hseFrequency`, a source with no rule): `TVI-I2C-NACK`, `unmodeled`, at the `START` (`nackclock`) |
| **Q3** gap | `STOPF` one SCL period after `NACKF`, labelled "inferred: RM0433 gives no figure" in the code and the ledger |
| **Q4** pending STOP | a firmware `STOP` while the automatic STOP is pending is absorbed (p2005; `nackstop`, `ap`); a `START` or a held `CR2` change then is `TVI-I2C-TRANSFER` (`nacktransfer`); `PE` = 0 and the guard's `Reset()` (`I2CxRST`) cancel a pending NACK (`nackpe`) |
| **Q5** `TVI-I2C-ABSENT` withdrawn for writes | the `Absent` table and its ledger text gone; every write `START` to an address with no device NACKs, on `i2c1`–`i2c3` |
| **Q6** read | a read `START` to an absent address: `TVI-I2C-ABSENT`, `unmodeled` (`absent`, now a read) |
| **Q7** data-phase NACK | not modeled; the `NACKF` row says so (no Renode device NACKs a byte) |
| **Q8** "Unknown slave" | still written under the guard's name at every absent `START` (`absentname`; the replays' counts, §3) |
| **Q9** form | the guard owns `EventInterrupt`: the stock line (followed by `AddStateChangedHook`, as the USART guard) OR `NACKF`·`NACKIE` OR the NACK's `STOPF`·`STOPIE` (`nackirq`: the NVIC's `ISPR0` bit 31); `ISR` served with the guard's `NACKF` and `STOPF`; `NACKCF` and `STOPCF` clear them (p2012), and `NACKCF` is not handed on to the stock tag. Ledger: `NACKF`, `NACKCF`, `NACKIE`, `STOP`, `STOPCF` "modeled by the guard". The restatement is unchanged |
| **Q10** budget | judged in CI (§8) |
| **Q11** shards | ten, unchanged |

## 2. Acceptance

| Clause | Evidence |
|---|---|
| A planted firmware starts a write to an absent address with `AUTOEND` = 1 and reads `NACKF` and `STOPF` set, at the page's timing; its `ICR` clears them | `nack` (the firmware: `0x1` at the `START`, `0x11` at `NACKF`, `0x31` at `STOPF`, `0x1` after `ICR` = `0x30`); `nacktime` (the timing, monitor-read 0.5 µs either side of each edge: `NACKF` at 54.5625 µs, `STOPF` 6.0625 µs later, at `rcc_pclk1` / 2 with `DNF` 15). Monitor-read because the log file's stamps are coarse in this harness (12.3 ms where the address phase is 54.6 µs), and a `RunFor` is exact |
| ArduPilot's boot and the armed run probe their absent sensors with no I2C record, and the armed run still arms | §3 |
| The 14 generated boots and the fixture: each board's observers unchanged, or each difference cited and named | §3, §4 |
| The budget, within `1201`'s 32 MiB, judged in CI | §8 |
| CI green; every new case and mutant in a shard, the aggregate showing it | §8 |

## 3. The replays

(filled in below)

## 4. Consumer observers (Aethelgard `589030ca`), by board

- **Unchanged on every board** (measure §2.4, re-checked on the final tree, §3):
  - `absent_address`: every "Unknown slave" line under its port, the same count;
  - `i2c_timeout`: the generated driver never reads `NACKF`, so its record stays `timeout_txis`, and every boot block is byte-identical;
  - the faulted-bus scenarios' expected `i2c_timeout`;
  - the sensor witnesses, `peripherals` and `renode_error_log`.
- **Changed, named:**
  - **ArduPilot's timeline** (both runs): each absent probe ends after its address phase (23–95 µs) instead of a 4.0–4.4 ms timeout, so the boot runs about 0.46 s ahead in the armed run. The arm command is accepted at 9.00 s on both; heartbeats 18 against 19 (their phase); one heartbeat with `base_mode` 217, not explained further (measure §2.3).
  - **The faulted `sentinel-03`** (its BMP581 at `0x46` dropped): `main`'s three `TVI-I2C-ABSENT` become none (measure §2.5).
  - **The stock model's source-less `Unhandled write to offset 0x1C … NACKCF` warning:** gone, because the guard now handles `NACKCF` (adversarial F1). On `main` it appears at every generated board's `ICR` = `0x30`; ArduPilot never wrote `NACKCF` on `main`, and would at every NACK if it were handed on. `[WARNING]` only: no judge reads it as a finding; Aethelgard's foreign `unhandled_write_key` counts such lines in `boot.warnings` (a count, not a verdict).
  - **Aethelgard's prose:** "never signals NACK" (`docs/architecture.md`:147, :319), `cli/analog.rs`:43 `RENDERING`, `cli/README.md`:549, `aethelgard-svd-hal/src/board.rs`:612, and the ledger's NACK, `ICR`, `CR2`, `CR1` rows (`cli/ledger.rs`:1135–1238). A consumer row.

## 5. Adversarial pass

A full pass (WO §5) by an independent reader, given the diff, the WO, the ruling and the held text, not my conclusions (`~/.cache/tvi-1306a/adv/review.md`). I checked each finding against the code and the text myself.

| # | Finding | Kind | Done |
|---|---|---|---|
| F1 | `ICR`'s `NACKCF` bit was handed on to the stock tag, which logged the clear as unhandled: 0 → 58 lines in the fixture, 0 → 110 in the armed run | wrong text, a log count | **Fixed:** not handed on. Mutant `I264`; `nack` checks no such line |
| F2 | ChibiOS's `STOP` races the inferred `STOPF`: landing later than one SCL period after `NACKF`, it would meet a port no longer in controller mode (`TVI-I2C-TRANSFER`) | latent false refusal | **Measured, not changed:** from `NACKF` to ChibiOS's `STOP`, at most 0.07 µs in the fixture (window 2.57 µs) and 2.28 µs in the armed run (window 10.57 µs), over 168 probes (measure M2-C). A row (§6) |
| F3 | Ledger text stale: `TIMINGR` "times nothing", `DNF` only a filter, `STOPIE`, `STOPCF`, `AUTOEND` silent on the NACK | wrong text | **Fixed** in `fields.py`; `STOPCF` classed "modeled by the guard" |
| F4 | The RCC guard's ledger rows (`PLLxRGE`, `PLLxVCOSEL`, `PLLxFRACEN`: "no PLL frequency is modeled beyond PLL1's integer one"; the PLL2/3 dividers "no side effect"; `HSIDIV`, `D1CPRE`, `HPRE`, `D2PPRE`) now feed `KernelHz` | wrong text, outside the fence | **Row** (§6): the grant was one method |
| F5 | `divr` can disagree with silicon: with a PLL on, a read-modify-write of the stock model's truncated read-back (it keeps DIVN2/3 in 8 bits) passes the RCC guard's rule as "no change", and `divr` takes the truncated value | false refusal or wrong timing | **Row** (§6): the RCC guard's existing rule, outside the fence; ArduPilot writes each divider once, with values that fit |
| F6 | `KernelHz` checks p404's ranges only, not `PLLxRGE` against the reference (p400–402), the VCO choice against it (p346: VCOH 2–16 MHz, VCOL 1–2 MHz), or `DIVP1`'s "not allowed" values (p404) | silent pass, timing only | **Row** (§6); unreachable by either firmware |
| F7 | "A lower bound" overstated it: nine periods from the `START` leave out the `START`'s hold (tHD:STA, which `SCLH` sets, p1973) | wrong text | **Fixed:** named inferred, early by about tHD:STA, in the code and the ledger |
| F8 | The software reset recomputed the event line before cancelling the NACK, so `PE` cleared with `NACKIE` newly set could pulse it | a read that lies | **Fixed:** cancelled first |
| F9 | The pending rule's `TXDR` clause could never fire (an earlier rule wins) | dead path | **Removed** |
| F10 | Recorded mode with `ADD10` = 1 (after its `TVI-I2C-FIELD`) NACKed the 7-bit reading of a 10-bit address the stock model then transferred to | NACK the bus would not give (recorded only) | **Fixed:** 7-bit only. The other two edges (a recorded `START` inside a pending NACK; an uncleared NACK `STOPF` keeping controller mode) are rows (§6) |
| F11 | Behaviours with no mutant: `STOPCF`'s clear, `Reset()`'s cancel, the epoch check, the `STOPIE` term | test gap | **Mutants `I260`–`I263`**; `nackirq` sets `STOPIE` too, `nackpe` pulses `I2C1RST` |

**Withstood:** the 7-bit address from `SADD[7:1]` on the one shared device table (no table to go stale); the next `START` on both firmwares; the epoch under the lock; `NACKCF`, `STOPCF` and `PE` = 0 clears (p2011, p2012); no `TXIS` at a NACKed `START`; the period formula; the event line; Renode's scheduler keeps nanosecond times; `KernelHz`'s encodings (HSIDIV, SWS, D1CPRE, HPRE, D2PPRE, DIVM shifts, DIVN < 3) against RM0433; `model/Refusal.cs` unchanged; every new record `unmodeled` with its page.

## 6. Rows and findings

- **For the RCC guard (a later WO):**
  - **F4:** its ledger rows now understate what reads them;
  - **F5:** `divr` and the truncated read-back;
  - **F6:** `PLLxRGE`, VCO choice and `DIVP1` parity unchecked;
  - **the reduction row `1307` named, now three copies:** the USART and ADC guards each carry the PLL arithmetic `KernelHz` now holds (ruled Q2: a later WO).
- **F2, the inferred gap:** what decides ArduPilot is that ChibiOS's `STOP` lands inside one SCL period after `NACKF`. Measured with at least 8 µs of margin; an interrupt held off longer by a higher-priority one would refuse `unmodeled`. RM0433 gives no figure for the STOP's time; a bench check would.
- **F10's two recorded-mode edges:**
  - a `START` passed (recorded) while a NACK is pending: the NACK's `STOPF` still lands, inside the new transfer;
  - an uncleared NACK `STOPF` keeps the port in controller mode, so a later `STOP` passes where `1306` would refuse.
  No measured firmware does either.
- **For Aethelgard (no change here):**
  - its NACK prose (§4);
  - ArduPilot's timeline on the guarded twin;
  - the faulted `sentinel-03` passing with `i2c_timeout` where `main` refuses.
- **The timing cases are monitor-driven** (`nacktime`, `nackpll`), because the log file's stamps are too coarse for the planted firmware to be timed from its access log in this harness (§2).
- **Two flaky cases, cause not established:** in one 198-case local run, `i2c-nackpll` and `1307`'s `adc-rate2` failed once each. Both passed on every rerun: `nackpll` 12 of 12 (its six reads identical every time), `rate2` 3 of 3, one of those on `main`. Both are monitor-timed. The failed run's outputs were not kept. I name this rather than guess at it; the teardown race (`1306` §6) is one candidate.

## 7. Gates

(filled in below)

## 8. Budget and CI

(filled in below)
