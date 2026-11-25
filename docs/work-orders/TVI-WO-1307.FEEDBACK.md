# TVI-WO-1307 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1307-adc-guard`, from `main` `fe17ea4`. **PR:** #53, left unmerged.

**State:**
- §1 was measured first, with predictions committed before any run (`05b91aa`, 20:21Z). The stop went to the bus at 20:58Z (`TVI-WO-1307.measure.md`, corrected at `5816665`); every question was ruled at 21:17Z (Q1–Q14) and built to that ruling.
- **The tenth shard came first**, as its own commit (`fe829ba`, ruled Q13), with `times.tsv` re-weighed by `1202a`'s method from main's CI run 45693027304 (the push of `39307f4`, #51). The aggregate's record glob was `ran-?.txt`, which a tenth record (`ran-10.txt`) would have silently missed: it is `ran-*.txt` now.
- **Granted outside the fence and built:** the RCC guard's `ADCSEL` mux and `adc12`/`adc3` query rows (Q3), with the ADC rows' ledger text. `1300b`'s and `1306`'s cases are unchanged (§7).
- **Granted and not needed (Q8):** the one-method read-only addition to the DMA guard. Its existing public `StreamState` answers one-shot's last transfer, and its bus interface answers `CIRC` (read-only). The DMA guard is unchanged.
- **Built beyond the letter of the ruling, from the adversarial pass (§5):**
  - the clock is recomputed after an RCC write under running conversions;
  - ADC3's internal channels need their enable;
  - one-shot on a circular stream refuses.

  Each refuses `unmodeled` under Q4's, Q10's and Q8's rules, and each has a case and a mutant.
- **Also extended (Q3/Q5):** the kernel-clock rule runs at `ADCAL` and `ADEN` as well as at `ADSTART`, because all three need the analog clock. The planted `kernel` firmware reaches `ADSTART` by stopping PLL2 after `ADEN`, as the WO's acceptance asks.

## 1. What was built

`model/GuardedSTM32H7_ADC.cs` comes with:
- its generated table (`tests/1307/fields.py` from `tests/1307/data/rm0433-rev9-adc-fields.tsv`, extracted from the held text by `tests/1307/extract.py`);
- its restatement (`model/stm32h7-adc-restate.awk` → `.repl`);
- the board's modes and revision, and the class row;
- cases in `tests/1307/adc.sh` and planted firmware in `firmware/adc.rs`.

| Ruling | Built |
|---|---|
| **Q1** the form | A subclass of `STM32H7_ADC` re-implementing the bus interfaces, cancelled and restated as `tviburiAdc12` `as "adcM1S2"` and `tviburiAdc3` `as "adc3"`. It owns every H7 register (its copies) and the regular conversion. The stock part keeps its channel sources, so `SetVoltage` works. ADC2's window refuses (`TVI-ADC-READ`/`-FIELD`) |
| **Q2** the revision | `revision: "V"` or `"Y"`, never defaulted. A platform without one refuses at load, by name (`TVI-ADC-CONFIG` after the witness), and at every access. The Nucleo platform and the replay declare `"V"`, "declared, not read": no held document says which revision a board carries (DS12117 Rev 10 describes both; p21 note 3) |
| **Q3** the clock | `TVI-ADC-KERNEL` at `ADCAL`, `ADEN` and `ADSTART` with `CKMODE` = 00, by the RCC query's new `adc12`/`adc3` rows (`ADCSEL` p419): not running is `predicted-fault` (p920); no rule is `unmodeled`. The ADC registers as `IRccWatched`; `IsRunning` = `ADSTART` with `CKMODE` = 00, so `TVI-RCC-CLOCK` refuses its clock stopped under it |
| **Q4** pacing | f_ADC from the firmware's own registers: `CKMODE` 00 (`pll2_p_ck`, `pll3_r_ck`, `per_ck`, / `PRESC`), 01/10/11 (rev Y `adc_hclk`, rev V `adc_sclk`), and rev V's /2 (p920, p1039). One conversion per `SMPx` + SAR time (Table 214 p955), channel by channel through `SQRx`, by `machine.ScheduleAction`. Recomputed after any RCC write under running conversions (§5 F1) |
| **Q5** the enable sequence | `DEEPPWD` resets to 1; `LDORDY` 10 µs after `ADVREGEN`; `ADCAL` for `t_CAL` (165,010) or `t_OFF_CAL` (1,280) cycles by `ADCALLIN`; `ADRDY` after `t_STAB`; `LINCALRDYW1–6` set by a linearity calibration (DS12117 p167, p273; p931). `TVI-ADC-STATE`, `unmodeled`, for every "only when" rule (p934). A set-only bit written 1 while 1 is no command |
| **Q6** ADC3's `CCR` | `VREFEN`/`TSEN`/`VBATEN` changed alone on `adc3` with `ADEN` = 1 and `ADSTART` = 0 pass, acknowledged by name; any other `CCR` change under `ADEN` refuses |
| **Q7** `BOOST` | Checked against f_ADC at `ADSTART` and after a clock change. Rev V `BOOST` = 01 where 10 is asked passes, acknowledged by name; any other mismatch is `TVI-ADC-RATE` |
| **Q8** `DMNGT` | 00 and 11 modeled; 01 stops at the serving stream's last transfer (`StreamState` running → not), with `DR` frozen; 01 with no DMA guard stream, or on a circular one (§5 F3), and 10 (DFSDM) are `TVI-ADC-DMNGT` |
| **Q9** overrun | `OVR` when a conversion ends with `EOC` set (p999); `OVRMOD` (p1015); requests blocked while `OVR` (p964). The FIFO (p962) is acknowledged by name in the ledger |
| **Q10** samples | `levels: "<ch>:<volts>"` or `SetVoltage` (a default source whose sample was replaced, or a source the platform registered). `DR` read holding an undeclared channel's conversion is `TVI-ADC-LEVEL`. Channel 19 only by `levels` (`SetVoltage` stops at 18). The replay declares the battery pin at 1.40 V, a test value |
| **Q11** ADC3's route | Requests raised on the stock entry's `dmamux1@115`, as today; BDMA/DMAMUX2 stays the WO's row |
| **Q12** allow-list | 208 rows over 37 registers, every bit 31:0 once; 105 fields allowed only at 0 (case `fields` gives each its widest value) |
| **Q13** tenth shard | `fe829ba` |
| **Q14** half-word `DR` | A half-word read of `adcM1S2`'s `DR` with `DMNGT` = 11 passes, acknowledged by name; any other sub-word access is `TVI-ADC-WIDTH` |

Also: the DMA suite's `adc`/`adcoff` firmware now gives ADC1 a valid start (`CKMODE` 11, out of deep-power-down, the regulator wait, `PCSEL`, a long sample time, `BOOST` 01). Its case declares channel 0 by `SetVoltage`. `adcM1S2 adc3` are in every guard-name list, and mutant `G17` follows the runner's line.

## 2. Acceptance

| Clause | Evidence |
|---|---|
| A planted firmware setting `ADSTART` with no kernel clock refuses `predicted-fault`, naming the ADC, the clock and the page | `kernel`: `TVI-ADC-KERNEL`, `predicted-fault`, `sysbus.adcM1S2`, bit `RCC_D3CCIPR.ADCSEL = 00`, "adcM1S2 ADSTART … PLL2ON = 0", p920, p419 |
| A planted firmware on a declared 1.2 V channel reads the code `RES` gives, at the rate the clock and `SMPRx` give | `start` (16-bit `0x5D17`), `res12` (12-bit `0x5D1`); `rate` (`EOC` at 49.5 µs on 8 MHz, not at 49.45), `rate2` (24.56 µs on the kernel clock), `revy` (rev Y twice as fast) |
| The armed run: no ADC record outside the acknowledged table; it still arms; its battery fields read the declared level | §3: zero records from any guard; armed at 9.787 s; `SYS_STATUS.voltage_battery` 15,399 mV for 1.40 V × 11 |
| The 14 generated boots and the fixture: observers unchanged, or each difference named | §3, §4 |
| The budget, judged in CI | §8 |
| CI green; every case and mutant in a shard, the aggregate showing it | §8 |

## 3. The replays

`1305`'s captures, through this branch's committed `replay-1301.sh` (plus logging), every guard recorded, `rcc`/`adcM1S2`/`adc3` logged; compared with §1's M1 on `main` (evidence `~/.cache/tvi-1307/b1`, `b2`).

| Run | Against `main` | Records |
|---|---|---|
| 13 generated boots and ArduPilot's fixture | identical: boot block and every RCC and ADC access line | 0 |
| `d1-drv8876` | the boot block identical (`DR` 0, the instruction count); its ADC poll lines differ (129,711 lines): the calibration takes 20.6 ms and the conversion 102 µs, as silicon (rev V, `ADCALLIN` = 1) | 0 |
| The armed run (Sentinel-03, 13 s), the battery pin declared at 1.40 V | 93 MAVLink frames, the same ids; armed from 9.787 s (`main` 9.774); `voltage_battery` 15,399 mV (0 on `main`); timing fields and a few frames' order move, as §2.4 of the measurement predicted | 0 |

## 4. Consumer observers (Aethelgard `589030ca`), by board

- **`d1-drv8876`:**
  - **The `peripherals` kind** (`STM32H7_ADC` → `GuardedSTM32H7_ADC`; the class map's consumer step) is an untrusted-tool stop until Aethelgard's class map names the guard, as for every guard.
  - **The witness** is unchanged: `ADSTART` comes before the first `DR` read, and `DR` = 0 for its 0 V.
  - **The `CR`/`DR` watchpoints' instruction counts** move with silicon's calibration and conversion times.
- **Every consumer platform must declare `revision`** (Q2), or the ADCs refuse at load. **A row for Aethelgard.**
- **Sentinel-03 declares a cell range, not a level** (Q10). The consumer has no property for an analog level on a foreign run. **A row.**
- **The fixture and the other 13 boots:** nothing moves. CI's foreign gate (Sentinel-02 does not arm) has no ADC access.

## 5. Adversarial pass (WO §5)

- **F1, a conversion on a clock silicon would not give:** the rate was computed once at `ADSTART`, so an RCC change under running conversions (`HPRE` for `adc_sclk`, a divider) left the old pace. **Fixed:** any RCC write while `ADSTART` = 1 recomputes f_ADC before the next conversion. A clock it cannot compute, or one whose `BOOST` no longer holds, is `TVI-ADC-RATE`. Cases `rccchange` (rev Y: the next conversion takes 49.5 µs, not 24.75) and `rccboost` (rev V: 8 → 4 MHz crosses `BOOST` 01's range); mutant `AD45`. `CKMODE` switched under `ADEN` (`ckmode`) and `ADCSEL`'s source stopped mid-run (`clocklost`) refused as built.
- **F2, a sample the twin invents:** ADC3's internal channels converted a declared level with `VBATEN`/`TSEN`/`VREFEN` off. **Fixed:** their reads refuse `TVI-ADC-LEVEL`. Cases `internal`, `internalok`; mutant `AD46`. An undeclared channel (`undeclared`) and a read before `EOC` (`early`: the previous `DR`, 0) behaved as built.
- **F3, a stream that never ends:** one-shot on a circular stream never saw its last transfer, since `StreamState` stays running, so requests never stopped. **Fixed:** refused `TVI-ADC-DMNGT` at `ADSTART`. Case `oneshotcirc`; mutant `AD47`. One-shot on a normal stream stops (`oneshot`: no overrun after 2 ms).
- **F4, from the reduction read:** `BOOST` was judged on `CR` before the write, so a `BOOST` changed in the `ADSTART` word was judged stale. **Fixed:** judged on `CR` as the write leaves it.
- **Withstood:**
  - `ADEN` written 0 in a word that also writes `ADSTART` (the generator's `0x1FC10104`) is no command;
  - `ADC12RST` mid-conversion stops the engine and resets the copies (`rccreset`);
  - `ADDIS` with `ADSTART` = 1 refuses;
  - a `levels` channel above 19, or a negative or unparsable level, refuses at every access (`badlevels`);
  - recorded mode with a rate it cannot compute converts nothing (no invented pace);
  - ADC3's requests in the armed run overrun after its second conversion, unread, with no record.

## 6. Rows and findings

- **Upstream (ruled Q7):** ChibiOS's ADCv4 `BOOST` chain (`hal_adc_lld.h`:461–469, ArduPilot `fb773567`) tests `> 6250000` first, so it writes 01 for every ADC clock above 6.25 MHz. On rev V, 12.5–50 MHz needs 10 or 11 (p1009). ArduPilot runs at 15 MHz.
- **Upstream (ruled Q6):** ArduPilot's `AnalogIn` writes ADC3's `CCR` `VREFEN`, `TSEN` and `VBATEN` after `ADEN` (p1038: "only when the ADCs are disabled"), with read-modify-writes the stock twin answered with 0. Worth a silicon check, and an ordering fix upstream.
- **Upstream (ruled Q14):** ArduPilot's ADC DMA reads `DR` by half-words; RM0433 states word access only (p926). Common practice, unstated by ST.
- **Aethelgard rows:**
  - declare `revision` on every platform (Q2);
  - a level for an analog net on a foreign run, starting with Sentinel-03's battery pin (Q10);
  - the class map (`GuardedSTM32H7_ADC`);
  - the ledger rows that say the twin "logs the write as an unhandled tag" (`CR.ADCAL`, `CFGR.JQDIS`, `SMPR#`) and "models no kernel clock or sampling time" are stale (`OQ-04`).
- **The BDMA/DMAMUX2 row (Q11):** ADC3's samples never reach ArduPilot in the twin.
- **Cross-guard duplication (reduction finding, out of scope):** the PLL/VCO and `sys_ck` derivation is now in the USART guard and this one. A shared clock helper would be one home.
- **The guard-name list is now stated in twelve places** (`1306`'s finding, one longer).
- **The teardown race:** `setvoltage` lost its `end` records once in the first suite run (CHECK FAIL, "no end record"), then passed.

- **Three findings from CI's first run (45707633754, at `bdea34d`; the architect's notes 22:00–22:06Z), each fixed at its source:**
  - **Shard 6, `allowlist`:** my granted RCC edit changed three rows' text (`ADCSEL`, `ADC12EN`, `ADC3EN`) in the guard's `Fields`, which `tests/1300/allowlist.py` generates. The text now comes from the generator ("677 rows; allow-list regenerates byte for byte"), and the whole RCC suite passes.
  - **Shard 5, `AD12-sar` alive:** deterministic, not the runner. In virtual time (1 ns resolution) the SAR mutant moves `EOC` from 49.500 to 49.4375 µs, and `rate`'s probes at 49.4/49.6 µs bracket both. New case `sar`, on the slowest clock the guard reaches (251,953 Hz, a 3.97 µs cycle), puts `EOC` at 1,571.72 µs against the mutant's 1,569.74 µs. Its probes at ±1 µs read `0x1001`/`0x100F` unmutated and `0x100F`/`0x100F` mutated.
  - **Shard 4, `AD41-adcMux` alive:** moving `query` onto PLL2 (p371) left no case naming `per_ck`, the source the mutant renames. `query` now switches `ADCSEL` to 10 with both inputs running.
  - The local runs matched CI throughout (same script, same test mapping). My first local mutant run had `AD12` alive too, then aborted at `AD18`, whose text the adversarial refactor had moved.

## 7. Gates

- **Cases:** 47 in `tests/1307/adc.sh`, all passing on the final tree. `setvoltage` and `rccchange` each lost their `end` records once to the teardown race (§6) and passed on re-run. **The DMA suite's `adc` and `adcoff`** pass on the guarded ADC1. **`tests/1300/rcc.sh`, every case**, passes (0 failed).
- **Mutants:** 47 (`AD01`–`AD47`) plus `G17` (the runner's guard-name line), each killed by `tests/mutants.sh` on an archive of the final tree. `AD12` and `AD41` were killed after the CI fixes above.
- **Reduction pass (diff), by a fresh subagent's read and checked by me:** −11 lines net · 3 duplicates removed:
  - `Readable`/`Writable`, made one check;
  - the f_ADC ceiling and `BOOST`, folded into one helper for `ADSTART` and for a clock change;
  - the RCC lookup, kept as is (merging it would re-quote `AD35` for −2 lines).

  It also removed 4 dead items (an unread `Levels` property, two firmware constants, a `using`), kept 2 protected (the per-guard `Row`/`R()`/`Fields` convention; the `hz` cache with its `rateStale` flag), and lists 1 finding (§6: the PLL derivation duplicated from the USART guard). The read also found F4, a real defect: `BOOST` was judged on stale bits (§5).

## 8. Budget and CI

- **The armed run's cost, back to back on Furnace** (`main` from `origin/main`, the branch from this tree, every guard recorded, no ADC logging):
  - `main` took 51.8 s and 47.9 s; the branch took 67.8 s and 67.7 s: **+31 % and +41 %**. Peak RSS rose from 2.79–2.92 M KiB to 3.73–3.75 M KiB.
  - Both runs arm at 9.787 s (`main`: 9.774 s) with no record from any guard, and read 15,399 mV.
  - **The second pair is above the +17–38 % the ruling accepted (Q4).** The stop's prototype paced ADC1 alone and called its +17–19 % a floor. The built guard also paces ADC3, at the same rate, into no reader, and the RCC guard runs its clock-loss trial at ArduPilot's RCC writes while both ADCs are watched. CI runs no armed run.
  - **A lever, not taken (it is a scope question):** ADC3's conversions in the armed run reach nothing once it overruns. Pausing a converter that is overrun with `OVRMOD` = 0 and no reader would save most of ADC3's share. That is a model shortcut whose silicon equivalence I have not shown, so it is a row.
- **The budget (`1201`):**
  - CI's first run, on the built guard: `MEDIANS main 1021940 KiB, branch 1024068 KiB, difference 2128 KiB (+2.1 MiB)`, **PASS**.
  - The §1 probe: +6.7 MiB, eleven boots a side.
- **CI, the first run** (45707633754, at `bdea34d`): seven of ten shards green, three red, as §6 sets out. The budget and its tests passed. Shard jobs took 10.5–16.6 min; the longest was shard 4, on an EPYC 7763 (1.48 × the reference). That is inside the 20 minutes the tenth shard was built for.
- **CI on the final head:** below, once it has run.
