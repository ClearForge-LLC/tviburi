# Tviburi — roadmap

**Status:** ratified by Scotty on 2026-11-23, by the merge of the rework (PR #5) (see Amendments); first drafted 2026-11-21. **Supersedes nothing.** Read after `architecture.md`.
**Authors:** Grok (seed, 2026-11-21); Claude, co-architect (rework, 2026-11-23) · **Gate:** Scotty · **Builder:** a separate build session.

**What to build next** is answered from this document alone: the first work order in the earliest phase whose dependencies are met and whose gate has not passed. There is no other task list.

---

## Numbering

- **The prefix is `TVI`** (`RUL-06`).
- **Ids follow the repo-genesis scheme `<PREFIX>-WO-PSNN[a]`,** where P is the phase, S the subsection, NN the order within it, and a letter marks an inserted refinement. The skill defines the scheme; it is not redefined here.
- **Legacy rule:** `TVI-WO-0100` was numbered before this scheme was adopted. Under the scheme it would read as phase 0, but it is Phase 1 work. **It keeps its id, and so does anything that refines it (`TVI-WO-0100a`). Never renumber an id that has been used in a commit, branch, PR or review.** New Phase 1 work orders start at `TVI-WO-1100`.

## Phase 0 — documentary backbone

**Goal:** steering documents that a builder with no memory of the planning conversation can work from.

**Exit gate:** the four steering documents and a `CLAUDE.md` pointer exist on `main`, and the steering-document rework PR (#5) is merged by Scotty. That merge is the ratification.

**Work:** the rework PR. No work order.

## Phase 1 — the refusal contract on the STM32H7

**Depends on:** Phase 0.

**Goal:** guarded models that refuse by name on the paths the consumer's firmware actually uses, with a hashed ledger and fail-closed records, proven in CI.

**Work orders, in order:**

| Id | What | Cadence |
|---|---|---|
| `TVI-WO-0100` | Stand up the overlay and the refusal runtime, proven on SPI1 `MODF`. **Built twice and judged; the blind build is PR #4** | done; merged via `0100a` |
| `TVI-WO-0100a` | Bring PR #4 to mergeable, with the verdict's seven fixes. **R8:** IOLOCK refuses. **R9:** fail closed per `RUL-12`. Then construction-time records, the harnesses, the stale quotes, the README, and the MODF truth table checked against RM0433 Rev 9 by page. Add CI per `RUL-15` | done; merged as PR #6 |
| `TVI-WO-1100` | **The coverage map** (`RUL-13`, `OQ-03`). From Aethelgard's witness ledger for every committed board, list each `ignored-by-model` and `same-source` field. Run the committed boards' generated firmware on the stock build with access logging, and record which stock models drop which writes. Join the two, ordered by what flies first. Output: an ordered queue of guarded paths, each cited to the row it answers | done; merged as PR #8. Its proposed `OQ-03` cut is not ruled. `1150` (v) has measured ArduPilot, and the cut now waits for a longer or armed window (`OQ-03`) |
| `TVI-WO-1150` | **The seam spike** (review A1–A3, A10, A11; `RUL-09`, `RUL-10`, `RUL-11`, `RUL-13`, `RUL-19`). Measure: (i) re-typing stock instances (`timer1`, `rcc`) in the pinned build; (ii) a console capture under Aethelgard's Renode configuration, committed as the check's fixture; (iii) whether a compiled-in release constant is feasible; (iv) a recorded-mode prototype; (v) ArduPilot's access log on the Sentinel-02 platform, joined into `1100`'s queue format; (vi) the overlay's compile time and memory per boot, and the records across `machine Reset` | done; merged as PR #10. It also measured (vii), how a guard covers the sealed RCC. Two stops fired, and the architect ruled on both: ship one file, and cancel and restate. `RUL-07`, `09`, `10`, `11` and `13` were amended from it |
| `TVI-WO-0100b` | **Guard fixes on SPI1** (review A6, A7, A8, A12, B1; `RUL-17`, `RUL-18`). The mode-fault pass for `SSM` = 0 with `SSOE` = 1; the `IOLOCK` latch and the D1 history corrected; a `kind` on every refusal; the expected version and unnamed sources in the check; SPI's allow-list, measured against the generated firmware and ArduPilot before it is built. `1150` (ii)'s fixture has landed (`spike/1150/fixtures/aet-7dec1d7b/`). `1150` (iv) found a check defect for it to fix: the check passes a recorded run with no `would-refuse` | done; merged as PR #11. Its FEEDBACK §2 item 11 goes to `1300a` |
| `TVI-WO-1300` | **The RCC guard: kernel clocks and enables,** the first `13NN` (`RUL-04`, `RUL-13`). It is a wrapper over the sealed stock RCC, with the 58 registrations restated (`RUL-07`, `RUL-09` (b)). It has an allow-list over the RCC registers both firmwares write (`RUL-17`), and a `predicted-fault` for a peripheral enabled with no kernel clock source (the `SPI123SEL` scar). It is measured first against the generated firmware and ArduPilot, in recorded mode. Its measurement runs beside `0100b`; its build waits for `0100b`'s records contract | done; merged as PR #16. Its FEEDBACK §5.4 backlog and §7 findings go to `1300a` and to rows |
| `TVI-WO-1300a` | **A clock that reaches the peripheral** (`RUL-18`; amended 2026-11-23 from `1300`). (1) The guard's `*ENR` copies start at 0 and an unenabled peripheral is locked, as silicon (p373); the stock model starts every peripheral enabled, an inherited false pass. (2) SPI1's start (`SPE` or `CSTART`) asks the RCC guard's kernel-clock query: no running source is a `predicted-fault` at SPI1. (3) A source, output or selector changed under a running peripheral refuses at the RCC write. (4) `0100b` FEEDBACK §2 item 11: a same-value `CFG2` write with `SPE` = 1 passes. (5) A bench row for the PLL1-source clear (pp. 384–386 against §8.5.2). (6) `1300`'s allow-list regenerable from a clean checkout, and its §1.1 harness runnable (for Aethelgard's `4240e`). (7) The teardown flake measured | done; merged as PR #21 |
| `TVI-WO-1200` | **Identity, the ledger, and the release.** The release program (`RUL-14`): the one-file overlay, the class map, and the manifest naming the build strings, with the release constant outside the hashed set (`RUL-09` (a), `RUL-10` as amended from `1150` (iii)); reproducible from one commit. The witness reports the compiled-in constant, and `$TVIBURI_MODEL_DIR` is removed. The ledger as canonical JSONL (`RUL-03`), generated from the guards' own data (`RUL-16`, `RUL-17`), listing every inherited model as unaudited (northstar invariant 2). Recorded mode built in `model/` (`RUL-19`), which no work order owned. **After `0100b`,** because it touches the same files (`model/Refusal.cs`, `tools/`) | done; merged as PR #14 |
| `TVI-WO-1301` | **The GPIO guard: alternate-function routing against DS12117's own table** (`RUL-04`, `RUL-13`). DS12117 Rev 10's AF table transcribed as data, independently of Aethelgard's and ArduPilot's pin data, then compared with ST's open pin data (each disagreement ruled). A wrapper over the stock ports with their registrations restated; an AF with no function on its pin is a `predicted-fault`; the query "which signal does pin P carry" for later guards; an allow-list over every GPIO field both firmwares write. Runs beside `1300a` | a measurement and a transcription, a stop, then the build. Written |
| `TVI-WO-1301a` | **A pin drives its connections as silicon does** (`1301` F1): from `ODR` only in output mode, from the peripheral in AF mode, nothing in input or analog (pp. 537–539). The stock port drives from `ODR` in every mode, so a firmware that forgets `MODER` = output lights an LED in the twin and not on silicon. `IDR` by mode; "set `ODR`, then output" drives the latched level | a short measurement, a stop, then the build. Written; after `1301` |
| `TVI-WO-1201` | **The memory budget, judged where it is stable** (Scotty, 2026-11-23): each pull request's one file against main's in the same CI job, ArduPilot booted 5+ times on each. Every boot under the consumer's 1.5 GiB ceiling (Aethelgard `RUL-13` as amended); the median at most 32 MiB over main's. `1301`'s absolute step is replaced; the 200 MiB spread between runs is measured, not fixed | a measurement, then the build. Written; after `1301` |
| `TVI-WO-1302` | **The advanced-control timer guard** (`RUL-13`'s next family): a motor output drives only when silicon would. The stock timer drives with `MOE` = 0 and never gates `CHxN` (`1301a`), a false pass on the path that flies. Gate on `MOE`/`CCxE`/`CCxNE` with idle states, polarity from copies, `LOCK`, an allow-list; the period off by one and the clock measured. Judged by `1201`'s budget | done; merged as PR #31 |
| `TVI-WO-1303` | **The DMA guard** (`RUL-04`'s second family): a stream moves data only where and when silicon would. DMA1/DMA2 cannot reach the TCM memories; configuration written while `EN` = 1 takes silicon's behaviour; DMAMUX routes to real requests. Then the timer's DMA side (`UDE`, `DMAR`) moves from `unmodeled` if the measurement allows, or DShot stays unwitnessable as a row | measured and ruled (2026-11-24, 14:10Z: per-field writes under `EN`, three `EN` states, routing checked at the stream's enable, ArduPilot's request ids acknowledged by name; (3) a row); done; merged as PR #37. The budget was judged in CI (+9.1 MiB) after Furnace's ceiling stop was ruled host contention |
| `TVI-WO-1202` | **CI wall time back under 20 minutes** (Scotty, 2026-11-24): `ci` grew from about 20 min (2026-11-23) to 52.6 min (run 67: mutants 22.2, acceptance 15.1, budget 6.3, budget tests 6.1), and a docs-only PR took 34 min. Parallel jobs sharded by measured time; every case and mutant still runs on every code PR, checked by an aggregate; a docs-only route with an inventory test; the budget's own tests only when its code changes. Target under 20 min for a code PR, under 3 for docs | done; merged as PR #33. Code PRs 14.5–15.2 min, docs-only 65 s; every case and mutant runs exactly once, checked by the aggregate |
| `TVI-WO-1304` | **The flash guard** (`RUL-13`'s queue after DMA: `FLASH` is 24 rows, every one taken by ArduPilot). (1) Wait states against the clock: `ACR.LATENCY` and `WRHIGHFREQ` against the AXI clock and the voltage scale, in both orders (raise the latency before the clock; lower the clock before the latency). (2) Bank 2 as ArduPilot's parameter storage uses it: the unlock, `PSIZE`, the busy and queue flags, errors and their clears. (3) `OPTSR_CUR`, which both firmwares read for `BOR_LEV`, served from a declared option-byte image, never the stock model's invented reset value | measured and ruled (2026-11-24, 15:50Z: decided at VOS1 where VOS is unknown; `WRHIGHFREQ` 11 one-sided; `optionBytes`; no `PSIZE` rule); done; merged as PR #38. Its CI took 23.5 min on a slow runner against 14.7 min for the same tree one run earlier |
| `TVI-WO-1305` | **The USART guard** (`RUL-13`'s queue: 136 rows, 54 taken by ArduPilot; northstar §3.1's command link). Fields protected while `UE` = 1, refused; a start with no kernel clock, by the RCC guard's query; `IDLE` and the receive DMA request modeled as silicon raises them, so `1303`'s acknowledgement of request 41 can be withdrawn where the platform wires it; the rest `unmodeled`. Measured on ArduPilot's boot and on Aethelgard `4400h`'s armed Sentinel-03 run | measured and ruled (2026-11-24, 18:55Z, 19:13Z, 19:57Z); done; merged as PR #40. ArduPilot armed on Sentinel-03's ELF over unpadded MAVLink with no USART refusal (Furnace); the budget +5.9 MiB in CI |
| `TVI-WO-1202a` | **A seventh shard, as ruled** (`1202`'s row): `times.tsv` re-weighed from CI's own timings; the slowest runner measured at 1.53 (the WO's 1.29 superseded); seven shards whose longest job is 15.7 min at 1.53; the aggregate counting seven | done; merged as PR #43. 17.0 min wall with an EPYC 7763 in the run; six shards had measured 19.05 min. About one guard of headroom left: an eighth shard comes around the second guard work order from now (F2) |
| `TVI-WO-1305a` | **The USART guard's three open questions, as ruled** (`1305` §6 Q2–Q4): a start with `BRR` < 16 acknowledged only for ArduPilot's measured pattern and `unmodeled` otherwise, with bench row U2; overrun with `FIFOEN` = 0 `unmodeled`; `IDLE`'s frame time from the RCC kernel-clock query | done; merged as PR #44. The HSE question ruled (b) and built (`sys_ck` from PLL1 on an HSI or HSE reference); the 8 MHz default planted for `1300b`; CI green across seven shards |
| `TVI-WO-1300b` | **The RCC guard's open rows:** a running USART's kernel clock lost (`TVI-RCC-CLOCK` beyond SPI); the armed Sentinel-03 run's RCC records (`ADC12EN`/`ADC3EN`, the `*LPEN` clears) ruled by page; no default crystal (a platform declaring no `hseFrequency` gets `unmodeled`, never 8 MHz); `RTCEN` at its silicon reset value | done; merged as PR #47. `TVI-RCC-CLOCK` watches running USARTs; the ADC enables modeled and four `LPEN` clears acknowledged; `TVI-RCC-HSE` (no default crystal); `RTCEN` from the guard's copy, so `rtc` follows `RTCAPBEN`. CI green in 18.8 min; budget −0.1 MiB |
| `TVI-WO-1306` | **The I2C guard** (`RUL-13`'s queue: ArduPilot writes `TIMINGR`, `CR1` and `CR2` on I2C1, and the generated boards' sensor buses the same). The stock model stores no `TIMINGR`, so a read-back is 0. Fields protected while `PE` = 1, refused; a start with no kernel clock, by the RCC guard's query; `TIMINGR` stored and read back; the software reset on `PE` = 0; ArduPilot's access before `I2C1EN` (`1300a` §1.1) ruled from the measurement; the rest `unmodeled`. Its stop projects the shards, and the eighth (`1202a` F2) comes first if it is due | done; merged as PR #49. `i2c1`–`i2c3` guarded; `TIMINGR` read back; the software reset; transfers the bus would not make refused; the RCC guard watches running I2C ports (`IRccWatched`); an eighth shard. CI 18.7 min, budget +7.4 MiB |
| `TVI-WO-1303a` | **The DMA guard's circular mode** (the armed Sentinel-03 run's last record: `dma1` `CIRC` = 1 on an ADC stream). `NDT` reloads, `EN` stays 1 and `TCIF`/`HTIF` are set at each wrap, as silicon does; `CIRC` protected under `EN`; a stream whose request the twin never raises (the ADC's) ruled from the measurement, never a silent pass; double-buffer `unmodeled` | done; merged as PR #51. The armed Sentinel-03 run has no record from any guard and still arms; `TCIF`/`HTIF` lines driven by the guard; a ninth shard. CI green after one re-run of `rcc-usart-stopped` (the teardown race; 3 of 3 on Furnace) |
| `TVI-WO-1307` | **The ADC guard** (`1303a` §5 Q1's list): the enable sequence (`DEEPPWD`, `ADVREGEN`, `ADCAL`) at silicon's values; the start on a running kernel clock (`CKMODE`, `ADCSEL`); requests paced by the firmware's own clock and sample time, never a fixed 1 kHz; `DMNGT`'s four modes; the overrun; samples from a declared platform level, never 0 mV by default | done; merged as PR #53. Paced at silicon's rate (rev V's /2); the kernel clock at `ADSTART`; `DMNGT`; `OVR`; declared samples (the armed run's battery reads the declared level); a required `revision`; a tenth shard. The armed run's wall is +31–41 % (CI runs none). CI green after three fixes at their cause |
| `TVI-WO-1306a` | **The I2C guard's NACK** (`1306` Q9 (c)): an absent address NACKed as silicon does (`NACKF`, then `STOP`/`STOPF` with `AUTOEND`), replacing the acknowledgement by name; the guard's "Unknown slave" line kept for the consumer | written 2026-11-25 |
| `TVI-WO-13NN` | **The guarded paths, in `RUL-13`'s amended order,** over `1100`'s queue joined with `1150` (v) (`spike/1150/data/queue-ap.tsv`). After `1300` (the RCC) come GPIO alternate-function routing, from DS12117's table; then the advanced-timer `MOE`/`CCxP` paths and DMA. BOR and DShot DMA wait for a consumer row. One work order per peripheral family. Each states its read rule: which reads it serves from its copies (`RUL-17`), and which it leaves to the stock model (review A11) | builds, one at a time |

**Rows with no work order yet** (each names its source; a work order that takes one says so here):
- **The RCC, from `1300` §5.4:** CSleep (`*LPEN` across `WFI`) is not modeled; `D1CKRDY`/`D2CKRDY` read 0, so a firmware that polls them (p372 step c) stalls with no record; `RCC_CSICFGR` is undefined in the stock model (silicon reads `0x2000 00xx`, p390); `PLL2DIVR`/`PLL3DIVR` drop each field's top bit, and no PLL2/3 frequency is modeled; `HSEBYP` written with the HSE on is not checked (p384). Neither firmware reaches any of them today.
- **SPI1's start asks which signal its pins carry** (`1301`'s query wired in, as `1300a` wires the clock's).
- **I2C, USART and RNG starts ask for their kernel clock** when their own guards exist (`1300` (4)). The USART's is built (`1305`); the I2C's is taken by `TVI-WO-1306`.
- **The records' log level:** the witness and end records log at INFO, against `RUL-12`'s "Error level". It fails closed today, but the contract and the code disagree; the records' owner (`model/Refusal.cs`) decides which moves (`1300a` §1.1 Q4).
- ~~**`rtc` and `RCC_BDCR.RTCEN`:** the stock model reads `RTCEN` as 1 at reset, so `rtc` is never locked (`1300a` §1.1 M1, Q4).~~ Done by `TVI-WO-1300b` (#47): `RTCEN` is served from the guard's copy, and `rtc` is never locked because the RTC's registers follow `RTCAPBEN` (p471).
- **Seven bit-backed peripherals are unregistered and cannot be locked** (`crypto`, `fdcan1/2`, `fmc`, `sdmmc`, `usb1/2`; `1300a` §1.1 M1).
- ~~**CI runs the full suite on a docs-only pull request.**~~ Taken by `TVI-WO-1202` (#33): a docs-only PR runs in about 65 s.
- **ADC's kernel clock depends on `ADCx_CCR.CKMODE`** (p920): with `CKMODE` ≠ 00 the ADC runs on `adc_sclk` and `ADCSEL` is unused. The RCC guard cannot see `CKMODE`, so it answers ADC `unmodeled` until an ADC guard reads it (Aethelgard `4240e`; `1300` F8 corrected).
- **For the I2C and DMA guards (`1300a` §1.1):** ArduPilot reads and writes I2C1 `CR1` before `I2C1EN` in program order (a read-modify-write clearing `PE`), and touches DMA1/DMA2 at 0.92 ms while locked. Silicon ignores both; each guard decides its enable rule from this. The I2C half is taken by `TVI-WO-1306`.
- **`SYSCFG_PMCR`:** no firmware writes `PC2SO`/`PC3SO` (Aethelgard `4410` S1); a guard row when SYSCFG is guarded.
- **From `TVI-WO-1302` FEEDBACK §7** (merged #31):
  - **The period and first edge, coordinated with Aethelgard:** the stock model counts `ARR` ticks and rises one period after `CEN`; silicon counts `ARR` + 1 and `OCxREF` is high from `CEN` (p1558). Both are `stock-wrong` rows. Aethelgard's motor witness encodes Renode's convention on purpose, so the change is one coordinated work order in both repositories.
  - **The open motor link:** with Aethelgard's switchable pull-down open, a disabled output floats; Tviburi shows it low and cannot see fitment.
  - **A `CHxN` pin in AF mode with `CCxNE` = 0** still shows NOT CHx through the port's `invertedAFPins`, where silicon leaves it Hi-Z (the GPIO guard's).
  - **TIM2–TIM5** under Table 342 (p1695): four committed boards drive TIM2.
  - **`tests/1301/replay-1301.sh` and Aethelgard's reaper argv** (`… -- /usr/local/bin/renode …`, Aethelgard ≥ `65c74788`), which the harness does not strip.
  - **The ledger cannot say "no value allowed"** (`DMAR` has no field row): the release program's table reader has no empty allowed set.
  - **`SR.UIF`/`CCxIF` set only with their interrupt enabled** (`stock-wrong`, p1597): a polling firmware reads 0 with no record.
  - **The overlay's compile garbage** grows with the one file (about 24 MiB of 36 at `1302`); each later budget compares against a main that already collects.
- **From `TVI-WO-1303` §1.1** (measured; ruled 14:10Z):
  - **DMAMUX1 is not guarded:** writes with the clocks off, sub-word writes dropped, sync/event/generator fields as tags, and a `CxCR` rewrite under a live stream unseen. The routing rules run at each stream's enable.
  - **No model raises USART1's receive request** (id 41): a receive over DMA moves nothing in the twin. It belongs to a USART guard, and to `OQ-03`'s arming path.
  - **The twin does not pace memory-to-peripheral transfers:** a transfer completes at `EN`, with no request. DShot's unwitnessability is one consequence.
  - **No transfer, FIFO, direct-mode or half-transfer event is ever raised.**
  - **For Aethelgard (a finding, not a change):** its `usart1` access log stamps a DMA-moved byte with the PC of the `EN` store.
  - **A Sentinel-03 ArduPilot capture**, once its hwdef emits; with PWM motor outputs it would still raise no timer DMA.
- **From `TVI-WO-1202` FEEDBACK §6** (merged #33):
  - **Wall time is set by the slowest runner** (0.85–1.29 × plan across CPU models): room for about two more guards before a 6th shard. *Superseded by `1202a`:* the slowest runner is 1.53, and seven shards were built.
  - **A required status check, if one exists, names `ci`**, not the old `suite` (a repository setting: Scotty's).
  - **"Re-run failed jobs" keeping the other shards' outputs is unexercised.** It fails closed either way.
  - **`tests/ci/citations.tsv`:** any work order may append its own rows for the citations it adds (ruled 14:45Z).
- **A sixth CI shard, now due** (taken by `TVI-WO-1202a`, which built seven). `1305`'s shard 1 took 21.1 min, and `1304`'s run 23.5 min, against `1202`'s 20-minute target. `1202`'s row foresaw room for about two more guards, and there have been two (`1304`, `1305`). The next guard work order adds the sixth shard first.
- **From `TVI-WO-1305`** (merged #40; its FEEDBACK §6 questions ruled by the architect, 2026-11-24):
  - **`BRR` below 16 at a start** ("USARTDIV must be greater than or equal to 16", p2035): ArduPilot's armed run starts `usart2` with `BRR` = 0 for one write (0.2017 s); whether a byte moves before the rewrite was not measured. **Ruled:** that measured write is acknowledged by name, and the next USART work order measures whether a byte moves under it; any other start with `BRR` < 16 is `unmodeled`. A Nucleo bench row (U2) observes the line.
  - **Overrun with `FIFOEN` = 0:** silicon loses the second unread byte (p2030), and the twin's receive queue is unbounded. **Ruled:** `1305`'s acknowledgement covers FIFO mode only, which both firmwares run. A second byte arriving unread with `FIFOEN` = 0 is `unmodeled`.
  - **`IDLE`'s frame time** uses the stock `frequency`, not the RCC's selected kernel source. **Ruled:** compute it from the RCC guard's kernel-clock query, as the start already asks it.
  - These three go to the next USART work order: **taken by `TVI-WO-1305a`**.
  - **A kernel clock lost under a running USART** (done by `TVI-WO-1300b`, #47) is not refused: the RCC guard's `TVI-RCC-CLOCK` watches the SPI guards only. Reception stops, but transmission goes on. A row for the RCC guard.
  - **An armed Sentinel-03 run is not a pass on main's other guards** (the RCC records taken by `TVI-WO-1300b`; the DMA's `CIRC` stays a row): RCC `ADC12EN`/`ADC3EN` and their `LPEN` clears, and DMA `CIRC` = 1 on an ADC stream. Each is a row for its guard.
  - **The records' owner** (`model/Refusal.cs`): recorded mode evaluates every rule for an access, as the DMA guard does, while the comment says "evaluates no further rule". The code or the comment moves.
  - ~~**The configuration check** does not confirm the USART guard is registered on the RCC guard (adversarial G6).~~ Done by `TVI-WO-1300b` (#47): an unregistered USART guard refuses every access.
  - **For Aethelgard (Phase 2 seam):** its serial access parser reads every `[INFO] usartN:` line as an access, so the guard's own INFO records break the generated judge's write reconstruction. It fails closed; same class as `rcc` and `flash`.
- **From `TVI-WO-1307` FEEDBACK** (merged #53):
  - **For Aethelgard (Phase 2 seam):** its platforms must declare the MCU's `revision` (rev V or Y changes the ADC's rate ×2, `BOOST`, `LDORDY` and `RES` codes), an analog level for each ADC channel a foreign run reads (Sentinel-03 declares a cell range, not a level), and the guard's class map.
  - **Upstream:** ChibiOS sets `BOOST` 01 at 15 MHz on rev V where p1009 asks for 10 (an `#if`-order bug), and ArduPilot writes ADC3's `CCR` internal-channel bits under `ADEN` (p1038). Both are acknowledged by name in the guard.
  - **ADC3's route on ArduPilot** (DMAMUX2 → BDMA) is not modeled: a BDMA guard's row.
  - **The armed run's wall** rose 31–41 % with silicon pacing (ADC3 paces into no reader). A lever is named in FEEDBACK §8, not built.
  - **The PLL/VCO and `sys_ck` derivation** now lives in the USART and ADC guards: a shared clock helper would be one home (a reduction row).
- **From `TVI-WO-1306` FEEDBACK** (merged #49):
  - **`NACKF` is not modeled:** silicon NACKs an absent address and the twin hangs. The measured addresses are acknowledged by name, per port (`TVI-I2C-ABSENT` otherwise). A row for the I2C guard. **Taken by `TVI-WO-1306a`.**
  - **For Aethelgard (Phase 2 seam):** its generated platforms declare sensors `@ i2cN`, which fail E39 on the guarded twin (they must name `tviburiI2cN`). Its faulted-bus scenarios, which drop the sensor, meet `TVI-I2C-ABSENT` where the stock twin gave `i2c_timeout`.
  - **For upstream Renode:** the stock `STM32F7_I2C` aborts Renode on a write transfer with `NBYTES` = 0. The guard refuses it first; in recorded mode it is passed on with `START` cleared, the one write recorded mode changes.
  - **A mid-transfer `START`** is a queued repeated `START` on silicon (p2005); it stays `unmodeled`, because the stock model starts at once.
- **From `TVI-WO-1300b` FEEDBACK §6** (merged #47):
  - **A bench row for the USART branch of `TVI-RCC-CLOCK`** (`RUL-18`): R4–R8 cover SPI only. R11, `usart-lost` with TX probed, goes to `model/bench-checks.tsv` with the next bench pass.
  - **The ledger `detail` of the four acknowledged `LPEN` rows** resolves to `1300`'s A6, which does not list them. The next RCC work order either adds them to A6's source, or teaches the release a second reference form.
  - **In recorded mode, a refused `HSEON` still reaches the stock model,** and `sys_ck` on the HSE then throws at 0 Hz. This fails closed. Recorded mode's contract (`model/Refusal.cs`, the records' owner) decides whether a refused write is forwarded.
  - **The teardown race** raced 3 of 14 on the branch against 0 of 14 on `main`, all under host contention, then 6 of 6 clean on each. It is read as contention; 14 runs cannot exclude a small rise.
- **The GPIO package property for the consumer's platforms** (Phase 2): without it the unbonded-pin rule does not run, and the witness says `package: none` (`1301` stop 1).
- **Renode's peak memory rises with wall time** (r = 0.978, `1301` §8): on a contended host one boot reached 1,910,576 KiB. Northstar §6's bound is judged in CI, or on unslowed runs as a proxy; a CI job that measures the one-file peak over several ArduPilot boots belongs in CI.
- **ST's pin data, for Aethelgard** (`1301` D3, D5): `DSIHOST_TE` comes from ST's shared H747 file, and `SPDIFRX1_INn` is off by one in 8 rows. Neither function is used by a committed board.
- **The AF check at the peripheral's start** (`1301` F5): `MODER` = AF before `AFSEL` refuses the transient (139 of 168 pins have no AF0 function), though silicon ends valid. Kept as a refusal until a peripheral's start asks the "which signal" query; then the check moves there.
- **Renode's `invertedAFPins` misses PA1 AF4** (`TIM15_CH1N`, DS12117 p89; `1301` F7), a stock data gap.
- **`tests/spike.sh` holds the guard-name list in nine copies** (`1301`); one list, after `1300a`.
- **CI builds ArduPilot's ELF** from Aethelgard's pinned `aethelgard-ardupilot` image and checks it against `a799dfb6…` (reproduced byte for byte from Aethelgard `0829fe83`'s hwdef, `1301`). No ArduPilot bytes are committed, as in Aethelgard (`AET-WO-4408` D1 (a)). Committing the GPL-3 ELF is Scotty's decision if the build ever stops reproducing.
- **From `1300a` (FEEDBACK §3–§4):** a DMA guard and an I2C guard (ArduPilot's locked accesses); an ADC guard reading `CKMODE`; `CKPERSEL` = 11 (reserved); `PLL2DIVR`/`PLL3DIVR` lose each field's top bit silently (`1300`'s data).
- **The memory bound, measured (`1301`, 2026-11-23):** .NET's GC settings (workstation, non-concurrent, `GCConserveMemory`) change nothing (CI run 45257810560), so the peak is not garbage. Across CI runs of nearly the same tree, the ArduPilot boot's peak ranged from 768,856 to 1,049,556 KiB, and **the cause of that spread is not known**: an early reading that it follows the runner's speed has a counterexample (run 45265963786: slower per boot, yet ~200 MiB lower; same runner image as the highest run). On Furnace the same tree peaks at about 984 MiB. `1301` itself costs about 10 MiB over main when both are measured in the same job. An absolute bound judged on GitHub's runners therefore passes or fails by something not yet identified. **Ruled by Scotty, 2026-11-23 evening:** the consumer's ceiling rises to 1.5 GiB (Aethelgard `RUL-13`, `AET-WO-2313`), and Tviburi judges its growth by a relative budget, each pull request's one file against main's in the same CI job, at most 32 MiB over at the median (`TVI-WO-1201`). What moves the peak by 200 MiB between runs is a row; `1201` takes its first measurement.
- **Board pulls and external levels (`1301a`, ruled 2026-11-23):** the GPIO guard takes a board's external levels as a platform property. ArduPilot's I2C bus check reads PB7 (I2C1_SDA) as an input; stock Renode read 1 only through an ODR leak, silicon reads 1 through the board's pull-up. **Aethelgard Phase 2:** the consumer declares its boards' pulls from its own solved topology (the I2C pull-ups are IR facts). Tviburi's replay declares PB6/PB7 for Sentinel-02 meanwhile.
- **An undeclared floating input reads 0** (`1301a` (d)): an assumption, not silicon's behaviour (a floating pin has no defined level). A firmware that needs 0 from a floating pin passes in the twin and may not on silicon. The witness cannot yet list the floating pins read: that needs `Refusal.cs` (the records owner).
- **The stock timer:** it drives its output with `MOE` = 0, and never gates `CHxN` (`CCxNE` is a tag) (`1301a`). For the timer guard.
- **On the stock platform, a pin with a later `->`** (a timer input, an LED) loses its SYSCFG/EXTI route (`1301a`, `1301` §3).
- **The SPI, USART and I2C models drive no pins:** an AF pin of theirs holds its level (`1301a` (3)).
- **The teardown race** loses an end record in about 2 of 6 runs on a loaded Furnace (`1301a`); it fails closed, and CI reruns are the remedy until it is placed.
- **The 200 MiB between CI runs** is all anonymous memory (RssAnon about 900 MiB on an Intel runner, about 692 MiB on AMD; file-backed memory, threads and huge pages equal), with the CPU model the leading candidate (`1201` (4)). The same-job budget makes it harmless to gating. A weekly scheduled main-against-main calibration would show drift in the comparison.
- **Consumer side, from `1300` §5.3** (Aethelgard Phase 2): every RCC property goes on the guard's entry (the `hse16-trap`); the consumer's own `spi1`/`spi2` registrations name the guard; `sysbus.rcc`'s access log loses register names; the stock RCC's warnings become sourceless; ArduPilot's `AHB2ENR` boot value changes to silicon's.

**Exit gate.** Each clause is checkable in CI on a pull request, in the pinned image:
1. Every path in the queue (`1100`, joined with `1150` (v)) up to the cut ruled under `OQ-03` refuses by name with a complete record (northstar invariants 1 and 5). A planted firmware that hits each path shows it.
2. Two runs of the same overlay build emit byte-identical ledgers, and changing any overlay file changes the model hash (invariant 3).
3. Removing a guarded model, crashing the simulator, or hitting a refusal each produces a non-pass at Tviburi's runner **and** from a consumer-side check that reads only the records (invariant 8), in both of `RUL-11`'s framings, the committed console fixture included.
4. The SPI1 `MODF` scar firmware refuses where stock Renode passes, and the condition text matches RM0433 Rev 9 by page.
5. A firmware line that imitates a record is not accepted as one (`RUL-11`).
6. For each guarded peripheral, a planted firmware that gives each field outside its allow-list a value the model does not implement refuses with `kind: unmodeled` (`RUL-17`).
7. A recorded-mode run is not a pass at the reference check, whatever else its log holds (`RUL-19`).

## Phase 2 — consumer integration

**Depends on:** Phase 1's exit gate. (`OQ-05`, the licence, is closed.)

**Goal:** Aethelgard's verdicts run on the guarded models, with a refusal that blocks a gate by name.

**Work.** Tviburi's half is a first release, the `RUL-09` artifact. Aethelgard's half is its own work orders, in its own roadmap:
- pin the artifact by sha256 in its tools image;
- apply the manifest's class map in its platform emitter. A stock file's instance is cancelled and restated, with no untyped update left on the cancelled name (`RUL-09` (b)). The RCC's 58 registrations are restated onto its guard (`RUL-07`);
- hash the overlay file it includes against the manifest, and run Renode with the compiler cache off. Refuse a run with no load witness, a wrong release constant, or a recorded-mode witness (`RUL-10`, `RUL-19`);
- read records in `RUL-11`'s grammar: trim `\r`, and anchor the source after the level;
- map a refusal record to a named gate failure;
- record the model hash and the ledger hash in its trace header and verdict;
- **a monitor write is a harness access:** a record with `pc: "none"` is one, and the consumer may exclude a refusal its own harness caused only by naming that access, never by a blanket rule (review A11). `TVI-WO-1150` (vi) checks that no other access, a DMA write for one, also shows `none`;
- confirm by a run that a witness line survives its console filter's budget (review A4).

**Decided at this phase:** whether Aethelgard's own sensor models (`models/renode/BMP581.cs`, `ICM42688P.cs`) come under the refusal contract (review B4). Until then they stay outside it, and clause 5 below is about Tviburi's code only.

**Exit gate.** On a committed Aethelgard board:
1. `simulate` and `campaign` run with the pinned overlay.
2. A planted firmware that hits a guarded path fails the gate with the refusal's rule name.
3. Removing the overlay makes the run refuse.
4. The trace header carries both hashes.
5. No Tviburi model code lives in Aethelgard's repository. *(Amended: was "No model code"; Aethelgard's own sensor models are B4's decision, above.)*

## Phase 3 — optional

- Re-expressing stable models for easier audit;
- a second vendor (Aethelgard's `RUL-08` names the RP2350);
- tighter timing.

**Exit gate:** none yet. This phase is not scoped; Phase 4's first run is the spike that shapes it, and its gate is written then.

Not planned until Phase 4 has run once. *(Amended 2026-11-23: was "until Phase 2 has been used".)*

## Phase 4 — the scar loop

**Depends on:** Phase 2, and `OQ-04`. Phase numbers name phases, not order: this phase runs before Phase 3 is planned.

**Goal:** every silicon disagreement found on the bench becomes a permanent regression case, and a cited Tviburi model can turn a consumer's unjudged ledger row into a judged one.

**Shape:** not scoped to work orders yet. The spike that shapes it is the first bench scar after Phase 2 (Aethelgard's bench sessions): carry it into a guarded model, and record what the round trip took.

**Deferred into this phase:** modelling the fault itself where the manual is clear, in place of refusing (review B2): for example `SSI` = 0 setting `MODF` and clearing `SPE` and `MASTER`, as silicon does. Only behind `RUL-18`'s `predicted-fault` rule, a page and a bench row, which is why it waits for the bench.

## Phase 5 — a register-truth layer across vendors and simulators

**Depends on:** a trigger, not a date. Either a target Renode cannot host is scheduled (for example RP2350, ESP32-C6, a C2000 or a dsPIC: no Renode core or SoC base exists for them today), or a consumer needs a refusal to hold the same way in two simulators.

**Goal:** the refusal table of `RUL-16`, generated from each vendor's register files, with adapters into Renode and into a second simulator, and a coverage report per target. It is not a new emulator.

**Exit gate:** none yet; it is written when the phase is scoped.

**The family order** (Scotty, 2026-11-24, named and ranked, the same order as Aethelgard's P5):
1. **RP2350, next.** It is Aethelgard's `RUL-08` planned second target, kept by Scotty. Renode ships no RP2350 platform, so **this is the first target past the trigger**: scheduling it starts this phase.
2. **Then the 2026-11-23 research's ranking** (`docs/reviews/2026-11-23-chip-families-research.md`):
   - nRF52840, STM32F405/F407, LiteX on iCE40/ECP5, i.MX RT1062;
   - then STM32G4, STM32U5/H5, nRF54L15, STM32H723/F7, RP2040, ESP32-S3, AT32F435;
   - then TI C2000, dsPIC33, ESP32-C6, i.MX RT1176, nRF54H20.
   - SAM E70 is a cheap audit only.
   - Finishing the H753/H743 is Phase 1's work, not this phase's.

**Shape:** not scoped yet. The spike that shapes it is the first target past the trigger: the RP2350. When it starts, decide whether it needs its own repository, and if so, name it then (Scotty, 2026-11-23: "if our Renode-like extension or a total rework project needs its own repo home ... we will come up with an appropriate name and create that repo"). The research behind the trigger and the order of vendors is `docs/reviews/2026-11-23-chip-families-research.md`.

## Critical path

- **Where risk concentrates:**
  - `RUL-11`'s channel: if a firmware can forge a load witness, invariant 8 fails silently;
  - the RM0433 citations: a wrong truth table makes a refusal or a pass wrong with full confidence.
  - Both are tested first, in `0100a` and `1200`.
- **What runs in parallel:**
  - `1100` (a measurement) beside `0100a`;
  - `1300`'s measurement beside `0100b`; `1300`'s build after `0100b`'s records contract merges;
  - `1200` after `0100b`, which touches the same files. It runs beside `1300` only while `1300` keeps out of `model/Refusal.cs`, `tools/` and the release;
  - after `1300` and `1200`, the `13NN` guarded-path work orders are disjoint by peripheral family. They can run two at a time.
- **Human track:**
  - Scotty rules the `OQ-03` cut. `1150` (v) has joined ArduPilot's 5 s log to `1100`'s queue; a longer or armed window comes first (`OQ-03`);
  - ~~Scotty rules the replacement for northstar invariant 6~~ ruled 2026-11-23, as the review response proposed;
  - Phase 4's input is a bench session at the Nucleo, which only Scotty can run;
  - Aethelgard's Phase 2 work orders queue behind its own package queue.

## Reconciliation

| Planned | Built | Note |
|---|---|---|
| `TVI-WO-0100`, one build | Built twice: PR #2 (Grok's team) and the blind build, PR #4. Judged blind, 27/40 and 37/40; the blind build is kept (Scotty, 2026-11-23) | The comparison record is `docs/reviews/2026-11-22-*`. PR #2's strengths (every SPI1 access refuses; a non-zero exit) are taken into `0100a` as R8 and R9 |
| `TVI-WO-1100`, the queue from the generated firmware's ledger, motor timers first | Built (PR #8): 722 rows over 13 boards, Sentinel-03 included, and a proposed cut | The review (A10) showed that what flies is ArduPilot, which has no ledger. `RUL-13` amended; `1150` (v) adds ArduPilot; the cut is not ruled |
| `TVI-WO-1150`, six measurements | Built (PR #10): seven, with (vii) added at the first stop. Two stops fired: no `.repl` form re-types, and three files exceed 1 GiB on ArduPilot | The class map becomes cancel and restate. The overlay ships as one file. The RCC guard is a wrapper. `1300` is the first `13NN` |

## Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed |
| 2026-11-23 | all | Rework (repo-genesis): numbering and the legacy rule; Phase 0's gate made checkable; Phase 1 scoped to work orders, its gate split into checkable clauses; Phase 2's gate made concrete on Aethelgard's side; the scar loop added as Phase 4, which runs before Phase 3 (optional) is planned; critical path and reconciliation added | Scotty, 2026-11-23. The scar loop takes the next free number rather than renumbering the seed's Phase 3, because phase numbers are never reused |
| 2026-11-23 | Phase 5 (new) | A register-truth layer across vendors and simulators, started by a trigger, with its own repository decided when it starts | The chip research of 2026-11-23 and Scotty's instruction the same day |
| 2026-11-23 | Phase 1, Phase 2, Phase 4, critical path, reconciliation | Rulings on the Mythos 5.2 review: `TVI-WO-1150` (the seam spike) and `TVI-WO-0100b` (SPI guard fixes) added; `1200` after `1150`; `13NN` in `RUL-13`'s amended order; gate clauses 1 and 3 amended, 6 and 7 added; Phase 2's work and gate clause 5 amended, B4 decided at Phase 2; B2 deferred to Phase 4; `0100a` and `1100` marked done | `docs/reviews/2026-11-23-mythos-5-2-tviburi.response.md` |
| 2026-11-23 | Phase 1, Phase 2, critical path, human track, reconciliation | `1150` marked done (PR #10); `TVI-WO-1300` (the RCC guard) added as the first `13NN`, after `0100b`'s records contract; `1200` after `0100b`, not `1150`; Phase 2's work amended to cancel and restate, one file with the cache off, and the record grammar; the `OQ-03` cut waits for a longer or armed window | `spike/1150/results.md`; `architecture.md` Amendments, the `1150` row |
| 2026-11-23 | Phase 1 | `0100b` marked done (PR #11); `1200` startable, its scope stated: the release program, the one-file overlay and manifest, recorded mode in `model/`; `1300a` given its own row, with `0100b` FEEDBACK §2 item 11 (a same-value `CFG2` write with `SPE` = 1 is a false refusal) | `0100b` merged; `TVI-WO-1200.md`. Recorded mode had no owner: `0100b` and `1300` both excluded it |
| 2026-11-23 | Phase 1 | `1200` (PR #14) and `1300` (PR #16) marked done; `1300a` amended to "a clock that reaches the peripheral" (the `*ENR` false pass, the start's query, a clock lost under a running peripheral, the same-value `CFG2`, a bench row, `1300`'s harness and generator inputs, the teardown flake); `TVI-WO-1301` (GPIO routing against DS12117) written as the second `13NN` | `TVI-WO-1300` FEEDBACK §5.4, §5.5, §7; the architect's rulings, 16:10Z |
| 2026-11-24 | Phase 1 | `TVI-WO-1202` written: CI wall time, measured at 52.6 min on main (run 67) | Scotty, 2026-11-24 |
| 2026-11-24 | Phase 1 | `TVI-WO-1302` merged (#31); `TVI-WO-1303` (the DMA guard) written | the merge; `1302` FEEDBACK §7 |
| 2026-11-24 | Phase 1 | `TVI-WO-1202` merged (#33); `1303` ruled at §1.1 (its clock clause corrected: an access with the DMA clock off is silicon's ignored write, covered with no record, not a refusal); `TVI-WO-1304` (the flash guard) written; the rows of `1302` FEEDBACK §7, `1303` §1.1 and `1202` FEEDBACK §6 recorded | the merges; the architect's rulings of 14:10Z and 14:45Z |
| 2026-11-24 | Phase 5 | The family order named and ranked: the RP2350 next (Aethelgard `RUL-08`, kept by Scotty), which is the first target past the trigger; then the 2026-11-23 research's ranking | Scotty, 2026-11-24; the northstar §3.1 PR |
| 2026-11-24 | Phase 1 | `TVI-WO-1303` merged (#37); `1304` ruled at §1.1; `TVI-WO-1305` (the USART guard) written; `OQ-03`'s armed window measured by Aethelgard `4400h` | the merge; the rulings of 15:50Z; Aethelgard #393 |
| 2026-11-24 | Phase 1 | `TVI-WO-1304` (#38) and `TVI-WO-1305` (#40) merged; `1305`'s three questions ruled (`BRR` < 16 acknowledged for the measured pattern only, `FIFOEN` = 0 overrun `unmodeled`, `IDLE` from the RCC source); a sixth shard due; `1305`'s rows | the merges; the architect's rulings at merge |
| 2026-11-24 | Phase 1 | `TVI-WO-1202a` (a sixth shard) and `TVI-WO-1305a` (the USART guard's three ruled questions) written | the rulings at `1305`'s merge; Scotty: keep the builders busy within Furnace's headroom |
| 2026-11-25 | Phase 1 | `TVI-WO-1202a` merged (#43) with seven shards (the slowest runner measured at 1.53); `TVI-WO-1300b` (the RCC guard's open rows) written | the merge; `1305a`'s HSE finding; the rows from `1305` |
| 2026-11-25 | Phase 1 | `TVI-WO-1305a` merged (#44). `TVI-WO-1306` (the I2C guard) written, beside `1300b`; it takes the I2C kernel-clock row and the I2C half of `1300a` §1.1 | the queue's next family; the architect |
| 2026-11-25 | Phase 1 | `TVI-WO-1300b` merged (#47); three rows closed by it; its three questions recorded as rows | the merge |
| 2026-11-25 | Phase 1 | `TVI-WO-1306` merged (#49), its rows recorded; `TVI-WO-1303a` (the DMA guard's circular mode) written | the merge; the armed run's last record |
| 2026-11-25 | Phase 1 | `TVI-WO-1303a` merged (#51): the armed run's last records gone. `TVI-WO-1307` (the ADC guard) written | the merge; `1303a` §5 Q1 |
| 2026-11-25 | Phase 1 | `TVI-WO-1307` merged (#53); its rows recorded; its final FEEDBACK line (CI on the final head) carried here | the merge |
| 2026-11-25 | Phase 1 | `TVI-WO-1306a` (the I2C guard's NACK) written | `1306`'s row |

## Provenance

- **Seed:** written 2026-11-21 by Grok. The phase boundaries are the ones named in the 2026-11-21 conversation.
- **Rework:** 2026-11-23, by Claude as co-architect. It rests on:
  - `architecture.md` §2;
  - the blind verdict (`docs/reviews/2026-11-22-*`);
  - Scotty's instruction of 2026-11-23 to build Tviburi so that Aethelgard can consume it.
