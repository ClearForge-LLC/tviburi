# TVI-WO-1303a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1303a-dma-circular`, from `main` `808f4e4`.

**State:**
- §1 measured, with predictions first (`1cfe182`), then stopped (`TVI-WO-1303a.measure.md`). Ruled at 2026-11-25T18:49Z: Q1–Q7 as recommended.
- Built to the ruling. The ninth shard came first, as its own commit (`4a49451`).
- Untouched: `model/Refusal.cs` and the record grammar (no new field), the RCC, GPIO, timer, flash, USART and I2C guards, the steering documents and Aethelgard.
- One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| **The ninth shard** (Q7): a `shard-9` job, `shard.yml`'s `of` 9, the `ci` job's counts (ten results, nine records). `times.tsv` is re-weighed from `main`'s last CI run with shards, 45620369016 (#49's merge, `3ce7dca`), by `1202a`'s method: all 795 items, 5,242 s at the reference, where the old weights said 5,016. Nine shards plan at 582 s each (15.7 min a job at 1.53). On the new weights, eight shards with this WO's items would plan at 674 s, with `ci`'s wall at 19.8–20.1 min | `.github/workflows/ci.yml`, `shard.yml`, `tests/ci/times.tsv` |
| **Circular mode** (Q1): on a peripheral-to-memory stream with `CIRC` = 1, at the request that leaves `NDT` = 0, the guard writes the programmed `NDT` back into the stock stream. `TCIF` and the offset reset are the stock model's own (`STM32DMA.cs` L259–264). Sources: §15.3.10 p647 and `NDT` p665. **`EN` stays 1 and `TCIF` sets at each wrap: both inferred from p654 against p665**, and labelled so in the code and the ledger | `model/GuardedSTM32DMA.cs` `OnGPIO`, `AfterRequest` |
| **`HTIF`** (Q3): the guard's own flag. It is set at the request that has moved half the programmed `NDT` in each cycle (p657), served in `LISR`/`HISR`, and cleared by `CHTIFx` (p660–661) and by a reset. The guard now drives each stream's interrupt line itself: `TCIE` (its copy) && `TCIF` (the stock model's), or `HTIE && HTIF` (Table 116 p658). It recomputes the line after every access to the stream and every request it serves; the stock model recomputes its own only when `TCIF` changes (`STM32DMA.cs` L119–137), so a `TCIE` write did not move it | the same: `Served`, `Pass`, `UpdateLine` |
| **`EN` at the end of a non-circular P→M transfer** (Q4): cleared by the guard at the request that takes `NDT` to 0 (§15.3.15 p654; p665). M→P and M→M keep `1303` Q3 (c) | `AfterRequest` |
| **`TVI-DMA-CIRC`** (Q2, Q5), `unmodeled`: `CIRC` changed while the stream is enabled (p664 states no rule; p665's "forbidden to write these registers when the EN bit is read as 1"); in recorded mode `CIRC` keeps its old value. At `EN`: `CIRC` = 1 on memory-to-peripheral (the twin moves it at once, unpaced; measure.md D9) or on memory-to-memory (§15.3.8 p645) | `RefusedEnabled`, `RefusedAtEnable` |
| **`TVI-DMA-HALF`** (Q3), `unmodeled`: at `EN`, a circular stream with an odd `NDT` (p657 states no count), or `HTIE` = 1 on a non-circular stream (no `HTIF` modeled there); and `HTIE` set while a non-circular stream is enabled | `RefusedAtEnable`, `RefusedEnabled` |
| **The allow-list:** `CIRC` and `HTIE` are now "modeled by the guard", allowed at any value; the refusals above sit at `EN` or under `EN`, never at a lone write. `HTIFx` is served by the guard and `CHTIFx` is modeled. `EN`, `NDT` and `TCIFx` state the wrap, the inference and the `EN` clear | `tests/1303/fields.py`, regenerated into `Fields` |
| **13 new cases:**<br>• monitor-driven, for exact request counts, the data, the line at the NVIC and a reset: `circ`, `circoff`, `tcie`, `circhi` (stream 7, through `HISR`/`HIFCR`) and `circrec` (recorded mode);<br>• planted firmware, one per refusal: `circen`, `circodd`, `htie`, `htien`, `circm2p` and `circm2m`;<br>• planted firmware on ArduPilot's ADC1 pattern, fed by the stock ADC1's own request, the firmware polling `LISR` and reading `CR`/`NDTR` at each flag: `adc` and `adcoff`.<br>`field` moves from `CIRC` to `DBM`, still a tag; `ledger` checks the new rows | `tests/1303/dma.sh`, `firmware/dma.rs` |
| **Mutants DM50–DM71,** one per change (22) | `tests/mutants.tsv` |
| Citations of `TVI-WO-1303a.measure.md` (4 rows) | `tests/ci/citations.tsv` |

## 2. Acceptance (WO §2)

| Clause | Shown by | Result |
|---|---|---|
| A planted firmware running a circular stream sees `NDT` reload, `EN` stay 1 and `TCIF` set at each wrap, with the page; the same stream with `CIRC` = 0 ends with `EN` cleared | `adc`, `adcoff` (firmware on the stock ADC1's requests); `circ`, `circoff` (exact counts) | as stated. `adc`: every `TCIF` the firmware sees has `EN` = 1 and `NDT` = 4, and `HTIF` is seen at each half. `adcoff`: one `TCIF`, seen with `EN` = 0 and `NDT` = 0 |
| `CIRC` changed with `EN` = 1 refuses, naming the stream, the field and the page | `circen` | `TVI-DMA-CIRC`, `unmodeled` (ruled Q2), "DMA_SxCR.CIRC of stream 0 changed to 0 (EN = 1, running)", p664 and p665 |
| The armed run shows no DMA record outside the acknowledged table, and still arms (`COMMAND_ACK` 400 at about 9.0 s) | the armed Sentinel-03 replay, `main` against the branch, same harness (§3) | **no record from any guard** (`main`: the two `CIRC` records). `COMMAND_ACK` 400 result 0 at 9.002 s; armed from 9.774 s |
| The 14 generated boots and ArduPilot's fixture: observers unchanged, or each difference cited and named | the same replays (§3) | the 14: every access, the boot block and the records identical. The fixture: only the `EN` bit of 2,373 of ArduPilot's own `CR` reads (p654, p665; Q4) |
| The budget, within `1201`'s 32 MiB, judged in CI | CI (§7) | |
| CI green; every new case and mutant runs in a shard, and the aggregate shows it | CI (§7) | |

## 3. Observers, by board (the replays)

`main` (R1, `1cfe182`'s tree, which is `main` `808f4e4`) against the branch: R5 (`9aab1bf`, as built), R6 (`78f766e`, after the adversarial pass's fixes) and R7 (`cd597ca`, after the reduction). **R5, R6 and R7 are identical to each other on all 16 runs** (every access, the boot block, the records), so the results below hold for each. The harness is the committed `tests/1301/replay-1301.sh` plus the measurement's logging (`TVI-WO-1303a.measure.md` §1). Every guard is recorded, and the armed run is injected as `1300b` ran it.

- **The 14 generated boots** (Aethelgard `fa12abf4`): every access, the boot block and the records are identical. None makes a DMA access.
- **ArduPilot's fixture** (5 s): the boot block and every access are identical, except the value of 2,373 of ArduPilot's `CR` reads on its SPI receive streams (DMA1 `S2CR` 2,354, DMA2 `S1CR` 19). These now read `EN` = 0 after the transfer's last request, as silicon (p654, p665; ruled Q4), where `main` read 1 (`0x10417` → `0x10416`). The count of reads is unchanged.
- **The armed Sentinel-03 run** (14 s; ruled Q6):
  - **Records:** none from any guard. On `main`: `dma1` `TVI-DMA-FIELD` `CIRC` = 1, twice.
  - **The arm:** `COMMAND_ACK` 400 result 4 at 7.004 s and result 0 at 9.002 s (`main` 7.005 s, 9.001 s); armed from 9.774 s (9.775 s); 19 heartbeats.
  - **The ADC stream:** DMA1 S3 serves the stock ADC1's 13,996 requests (one per millisecond), where `main` served 8 and the ADC then stopped on an overrun. ArduPilot's DMA ISR now runs at each half and each wrap, reading `LISR` (`HTIF3` = `0x4000000`, `TCIF3`).
  - **MAVLink, USART1:**
    - the same 93 frames and IDs;
    - **each message type's sequence is byte-identical** (`PARAM_VALUE`, `HEARTBEAT`, `COMMAND_ACK`, `STATUSTEXT`);
    - **the interleave moves in two places:** a `SERVO_OUTPUT_RAW` frame goes out about 0.1 ms before a `PARAM_VALUE` frame, at 7.45 s and 7.65 s;
    - **timing fields differ:** `SYS_STATUS.load` 411 → 414 (+0.3 %; its voltage and current fields stay 0), `SERVO_OUTPUT_RAW.time_usec`, `TIMESYNC.ts1`.
  - **MAVLink, USART3:** the same 20 frames; only `TIMESYNC.ts1` differs.
  - **The instruction count** is +1,388,513 (+0.31 %).
  - **Against the measurement's R3** (the scratch prototype, whose `TCIF` stood in for `HTIF`): load 412 there, 414 here, because the real half-transfer interrupt runs ChibiOS's half-buffer path. Reported at 19:07Z; ruled within Q6 at 19:33Z (timing).

## 4. The adversarial pass (WO §5)

**Two passes:**
- mine, through the build;
- an independent one, by a fresh reviewer given the diff, the stock source, RM0433 and the ruling, but not my conclusions.

I checked each of its findings in the code and the source before acting on it.

| WO §5 item | Result |
|---|---|
| **A wrap that moves data silicon would not:** a request after `EN` is cleared | moves nothing: the stock model ignores a request on a disabled stream (`STM32DMA.cs` L171), and the guard acts only on an enabled P→M stream that moved an item |
| a wrap with `NDT` = 0 | none: `NDT` = 0 serves no request (p665; stock L274), and the reload needs an item to have moved |
| a reload from a stale `programmed` | none in enforcing mode. `programmed` equals `NDT` at every accepted enable: `NDTR` is written only while disabled, a write under `EN` refuses (or is restored in recorded mode), a re-enable after a suspension refuses `TVI-DMA-OFFSET`, one after a completion refuses `TVI-DMA-RELOAD`, and a reset clears both. Direct mode only (`DMDIS` = 0), so each request moves exactly one item, which keeps the exact-count wrap and half checks sound |
| **A flag that lies:** `TCIF` on a non-circular stream silicon would end | `TCIF` is set at the end, as silicon (p654), and `EN` now clears with it (Q4) |
| `HTIF` at the wrong count for odd `NDT` | odd `NDT` on a circular stream refuses `TVI-DMA-HALF` at `EN` (`circodd`). Pinned: `HTIF` is not set at the first request (`circ`, `LISR1`), and mutant DM65 (`HTIF` at every request) is killed |
| **A circular stream that passes by never running** | none is acknowledged idle. The armed run's stream runs on the stock ADC1's requests (ruled Q1), and `adc` shows a firmware waiting on the flags seeing them |

**The independent review's findings, each checked, then fixed:**
1. **`HTIE` set while a non-circular stream runs passed silently,** and its interrupt never came. `TVI-DMA-HALF` was judged only at `EN`. It is now also judged on a write that sets `HTIE` under `EN` with `CIRC` = 0 (`htien`, DM66).
2. **A `TCIE` write did not move the stream's line.** The stock model recomputes its line only when `TCIF` changes (`STM32DMA.cs` L119–137), and the guard's line read the stock line. The guard now drives each line from its copy of `TCIE` and the stock `TCIF`, or `HTIE && HTIF` (Table 116 p658): `tcie`, DM67. The armed and fixture replays were rerun after this (§3).
3. **DM57 (the tap) survived `irq`,** where the transfer completes inside the guard's own `CR` write. `circoff` now checks the transfer-complete interrupt a request raises, which only the tap carries, and kills it.
4. **The half count was not pinned:** `circ` now reads `LISR` after the first request (DM65). `adc` asserts `HTIF` count ∈ {`TCIF`, `TCIF` + 1}.
5. **Lines without a case:**
   - `HISR`/`HIFCR` and the high streams' bit positions: `circhi`, DMA1 stream 7, with DM68 and DM69;
   - the line falling after `LIFCR`: `circ`, `ISPRC`, DM70;
   - the recorded-mode `CIRC` restore: `circrec`, DM71.
6. **"`HTIF` in each cycle"** was stated as cited (p657 is the configuration procedure for one transfer). It is now labelled "inferred from p647's 'the DMA requests continue to be served' with p657", in the code and the ledger.
7. **Nits fixed:**
   - the `off` state's header line, stale after Q4;
   - `NDT`'s row class: "modeled by the guard", since the guard writes it at a wrap;
   - DM63's name.

**Found and left (named here, and in §6):**
- **`HTIF` on a non-circular stream reads 0 where silicon reads 1, even with `HTIE` = 0** (p657 sets the flag whatever `HTIE` is). Ruled Q3 (circular only); the `HTIF` row says so. A firmware polling it would read 0 with no record.
- **ST's HAL** (from memory, not opened) sets `HTIE` for UART receive by DMA, which now refuses `TVI-DMA-HALF`. That is `unmodeled`, not a lie, and narrower than `main`, which refused `HTIE` = 1 on every stream.
- **In recorded mode only** (each case already carries a `would-refuse`):
  - odd circular `NDT` never sets `HTIF`;
  - after a suspension re-enabled without an `NDTR` write, the half count uses the programmed `NDT`, not the remaining one.
- **`TVI-DMA-OFFSET`** also refuses a circular stream disabled just after a wrap (`NDT` = programmed), where nothing is ambiguous. `unmodeled`, not a lie.
- **The `Reset()` line update** has no case: the NVIC latches pending, so a line falling at a reset is not observable from the monitor.

## 5. The reduction pass

**Reduction pass (diff): −33 net · 4 duplicates removed · 2 dead paths removed · 0 protected kept · 2 findings below.** A fresh reviewer made the proposals over `origin/main..HEAD`, and I checked each in the code and the stock source before acting.
- **A mechanism removed:** the tap on the stock interrupt lines (a class and its wiring). Every stock `TCIF` change happens inside an access the guard forwards, or inside a request it serves, so the guard recomputes the stream's line there.
- **Dead conditions removed:** the request gate's `value`, `Enabled` and direction clauses. The stock model moves an item only on a rising request to an enabled P→M stream (`STM32DMA.cs` L164–183), and `AfterRequest` already returns when `NDT` is unchanged.
- **Duplicates removed:**
  - the state word built three times in `RefusedEnabled`;
  - two header lines restating rules stated above them;
  - the request pulse written 23 times in `tests/1303/dma.sh`, now one array;
  - the firmware's `adc` doc comment restating its path header.
- **Rejected:**
  - recomputing all eight lines after every access (one mechanism instead of three call sites). It reads `LISR`/`HISR` eight times per access, and ArduPilot's armed run makes 1.4 million DMA accesses;
  - `return false` for `return circ || half` (no lines saved, against the file's convention);
  - dropping `&& n != 0` (no lines saved).
- **Proof:** the full DMA suite and DM50–DM71 on the reduced tree (`cd597ca`), and the replays identical to the unreduced tree's (§7).
- **Findings (outside this WO):**
  - the setup triple (`DMAMUX1_C0CR`, `PAR`, `M0AR`) repeats across the monitor-driven DMA cases, old and new: a helper for the next DMA work order;
  - `r == LifcrOffset ? 0 : 4` appears twice.

## 6. Rows and findings

**For the ADC guard (ruled Q1)** — the stream is now fed by the unguarded stock ADC1, so its pace and its samples are that guard's row. An ADC guard would need (measure.md §5 Q1):
- the conversion clock (`CKMODE`; `ADCSEL`, p419), where the stock model paces at `externalEventFrequency`, 1 kHz;
- the conversion time (`SMPRx`, `RES`, the clock);
- `DMNGT`'s four values (p1016): the stock model raises requests for one-shot (01) as for circular (11), where silicon stops after the DMA's last transfer (p964);
- the overrun (`OVR`, `OVRMOD`, p964);
- the enable sequence:
  - `DEEPPWD` is reserved in the stock model and reads 0 (silicon resets it to 1);
  - `ADCAL` is a tag;
  - `LINCALRDYW1–6` always read 1;
- the sample sources: 0 mV with none attached, and `VREFEN`, `TSEN` and `VBATEN` are tags;
- ADC3's request: DMAMUX2 17, p608, or DMAMUX1 115, p606;
- `adcM1S2`'s mocked ADC2;
- `d1-drv8876`'s polled ADC3.

**For a BDMA guard (ruled):** the armed run's ADC3 stream is BDMA channel 7, circular, with its request on DMAMUX2 id 17 (p608) written to an address the platform does not map. The stock BDMA models circular mode (`STM32LDMA.cs` L283–291), but its `CCR` reads 0, so ChibiOS's read-modify-write drops `CIRC`, `HTIE` and `TCIE` (L358).

**For Aethelgard (a finding):** the stock DMA engine's peripheral reads (`ADC1_DR`, 13,996 in the armed run) are access-logged with the CPU's current PC, as `1303` found for USART1's `TDR`.

**Rows this WO leaves:**
- **`HTIF` on non-circular streams** is not modeled (ruled Q3), and `HTIE` = 1 there refuses at `EN`. Extending it is silicon's behaviour, unmeasured on ArduPilot's SPI and USART streams.
- **The reload at a re-enable** after a completed P→M transfer (silicon reloads the programmed `NDT`, p665) stays `TVI-DMA-RELOAD`. With `EN` now cleared at the end, a firmware that re-enables without rewriting `NDTR` meets it, where it met `TVI-DMA-DONE` before.

## 7. Suites, mutants, memory and CI

All on Furnace, in the pinned image (`aethelgard-tools@sha256:1e0ab105…`), one capped container at a time, `--network none`.
- **The DMA suite** (`tests/1303/dma.sh`, every case):
  - on `9aab1bf`: 53 of 53;
  - on the reduced tree `cd597ca`: 55 of 57 pass. The two failures, `same` and `disable`, lost every guard's `end` record at teardown with no refusal recorded: the teardown race (`1303` FEEDBACK §8), at load about 11. Both pass on three reruns each, at load about 8.
- **Mutants DM50–DM71:** 22 of 22 killed on `cd597ca`, the reduced tree (`ALL MUTANTS KILLED`). DM57 survived its first named case, `irq` (§4, finding 3), and is killed by `circoff`.
- **DM28 re-pointed** (`1303`'s row: the guard's interrupt lines replaced by fresh, unconnected ones, killed by `irq`). Its old text, `Connections => inner.Connections;`, is the line this WO replaced with the guard's own lines, so CI's first run on this PR (45686087875, shard 5) aborted at it: "mutant text not found exactly once". It is the same mutation on the new line, `Connections => connections;`, and is killed by `irq` locally. Every row in `tests/mutants.tsv` now passes the exactly-once test locally.
- **The route and inventory** (`tests/ci/route.sh origin/main`): pass. A citation row the reduction made stale was removed (`39fb572`).
- **The replays:** §3.
- **Memory:** the measurement's prototype gave −0.2 MiB at the median, five boots a side, PASS (measure.md §4). CI's budget judges the built guard (below).
- **CI:** below.
