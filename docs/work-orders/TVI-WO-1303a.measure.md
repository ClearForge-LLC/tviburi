# TVI-WO-1303a — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1303a-dma-circular`, from `main` `808f4e4`.
**State:** §0 committed at `1cfe182` (17:37Z) before any run; §1 measured; **stopped** (WO §1 step 1, §7) with the questions in §5, 2026-11-25T17:49Z.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt` (text sha256 `8e02c0c0…`), pages by `tests/1300/rmpage.sh`.
**Stock source read** (`~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/`): `DMA/STM32DMA.cs` (`f7105075…`, the file `1303` read), `Miscellaneous/STM32_DMAMUX.cs` (`5d54137d…`), `Analog/STM32H7_ADC.cs` (`fb587b9a…`), `Analog/STM32_ADC_Common.cs` (`0cc82d40…`). No hash pins that tree to the pinned build, so each claim drawn from it is measured (§1). The stock platform is the pinned image's `/opt/renode/platforms/cpus/stm32h7.repl` (`b85cb4af…`, read in the image).
**ArduPilot source read:** `/opt/ardupilot` (`fb773567`) in Aethelgard's pinned `aethelgard-ardupilot@sha256:e1809b90…`: `libraries/AP_HAL_ChibiOS/AnalogIn.{cpp,h}`, `modules/ChibiOS/os/hal/ports/STM32/LLD/ADCv4/hal_adc_lld.c`, `os/hal/include/hal_adc.h`.
**Captures:** `1305`'s, read-only, as `1300b` and `1306` replayed them: the 14 generated boots at Aethelgard `fa12abf4` (`~/.cache/tvi-1305/cap-gen`), ArduPilot's fixture (`cap-ap/fx`, ELF `a799dfb6`) and the armed Sentinel-03 run (`armed`, ELF `dbf598d8`, with its injection). Aethelgard `main` is now `589030ca`; the captures are not retaken.

## 0. Predictions, committed before any run

**Seen before writing them,** so these are not blind on those points:
- the WO, `1303`'s WO, measure.md and FEEDBACK, `1305`'s and `1300b`'s FEEDBACK on the armed run, and the DMA guard as it is on `main`;
- **`1305`'s stored armed console** (`armed/run2`, tree `8e1d2d1`, `collapse-repeated-log-entries = True`), read offline: its two `dma1` records (`TVI-DMA-FIELD` `CIRC` = 1, `CR` `0x22D1E` then `0x22D1F`, pc `0x081041B0`/`0x081041B8`, 3.286 ms, state `S3CR=0x0 NDTR=0x8 PAR=0x40022040 M0AR=0x38001A60 DMAMUX1_C3CR=0x9`); ArduPilot's `LIFCR` write `0x08000000` (`CTCIF3`) at 11.286 ms; one stock warning "Attempting to create DMA request with no data to send" at 12.286 ms; `adc3` warning "Issued a start event before the last sequence finished" each millisecond;
- an offline evaluation of `0x22D1E` and `0x22D1F` against the allow-list: each fails two rows, **`CIRC` and `HTIE`**. The record names only `CIRC` because `RefusedField` stops at the first failing row;
- RM0433 Rev 9 pp. 645, 647, 654–665, 964–965, 1016 (a subagent's read, spot-checked by me at p645, p647, p657, p664, p665, p965);
- the stock DMA, DMAMUX and ADC sources above (a subagent's read; the ADC request path spot-checked by me, `STM32_ADC_Common.cs` L123, L261–267, L300–345, L542–549);
- ArduPilot's ADC driver (read by me).

### P1. What silicon does (RM0433 Rev 9)

- **Circular mode** (§15.3.10, p647): "When the circular mode is activated, the number of data items to be transferred is automatically reloaded with the initial value programmed during the stream configuration phase, and the DMA requests continue to be served."
- **`NDT`** (p665): "Once the transfer is completed, this bitfield can either stay at zero (when the stream is in normal mode), or be reloaded automatically with the previously programmed value … when the stream is configured in circular mode"; "If the value of this bitfield is zero, no transaction can be served even if the stream is enabled."
- **`EN` in circular mode: inferred, not stated.**
  - `EN`'s text (p665) says only that it "is cleared by hardware … on a DMA end of transfer".
  - §15.3.15 (p654) qualifies it: "If the stream is configured in non-circular mode, after the end of the transfer … the DMA is stopped (EN bit … is cleared by Hardware)".
  - That `EN` stays 1 in circular mode is read from that contrast and from p647's "continue to be served". No sentence states it.
- **`TCIF` at each wrap, `HTIF` at each half: inferred, not stated.**
  - p657: "Once half the data have been transferred on the AHB destination port, the half-transfer flag (HTIF) is set … At the end of the transfer, the transfer complete flag (TCIF) is set." Each wrap is an "end of the transfer" only by reading p647 with p657.
- **`HTIF` for an odd `NDT`: RM0433 is silent.** "Half the data" is not defined in items, and no rounding is stated anywhere in §15. Any rule for odd `NDT` is *from memory*, or unmodeled.
- **`CIRC` under `EN` = 1: RM0433 gives no per-field line.**
  - p664 describes `CIRC` with no "protected … can be written only if EN = 0", which its neighbours `DIR`, `PFCTRL`, `PINC`, `MINC`, `PSIZE`, `MSIZE`, `PINCOS`, `PL` and `DBM` carry.
  - Only `EN`'s general sentence covers it (p665: "It is forbidden to write these registers when the EN bit is read as 1"). `1303` ruled against that sentence (Q2) and listed `CIRC` among the fields that take the write.
  - **So the WO's "`CIRC` is protected under `EN` = 1, as `1303` ruled for the other fields" has no page of its own: a question for the stop (§5).**
- **Other `CIRC` lines:**
  - p664: forced to 0 by hardware when `PFCTRL` = 1 and `EN` = 1; forced to 1 when `DBM` = 1 at `EN`;
  - p655: "The circular mode is forbidden in the peripheral flow controller mode";
  - p645: "When memory-to-memory mode is used, the circular and direct modes are not allowed";
  - p647: with a memory burst, `NDTR` must be a multiple of (Mburst beat × Msize / Psize). Bursts are refused today (`MBURST`/`PBURST` ≠ 00), so this rule is unreachable.
- **The ADC's side** (§25.4.27, pp. 964–965; `DMNGT` p1016): "a DMA request is generated after each conversion of a channel" when `DMNGT` = 01 or 11. In circular mode (11), "the ADC generates a DMA transfer request each time a new conversion data is available … even if the DMA has reached the last DMA transfer". On an overrun, "the ADC stops generating DMA requests" until `OVR` is cleared (p964).

### P2. The stock models (by source; each to be measured)

- **DMA:**
  - `CIRC` and `HTIF` are tags, so the stock model drops `CIRC` and never sets `HTIF`.
  - A peripheral-to-memory stream moves one `PSIZE` item per rising request (direct mode).
  - At `NDT` = 0 it sets `TCIF`, zeroes its offset, keeps `EN` = 1 and does not reload. Each later request logs "no data to send" and moves nothing.
  - The stream's interrupt line is `TCIE && TCIF` only, so a half-transfer interrupt needs the guard to drive the line.
- **DMAMUX1:** a request on input *n* pulses (true, then false) every channel whose `DMAREQ_ID` = *n*.
- **The ADC** (`STM32H7_ADC` over `STM32_ADC_Common`). `adcM1S2` (`0x40022000`) has `dmaChannel: 9`; `adc3` has `115`, both on `dmamux1`, `externalEventFrequency: 1000` (`stm32h7.repl` L84–91, L231–238).
  - **Requests:** it raises its DMAMUX1 request once per conversion while `CFGR` bit 0 (`DMAEN` in the stock model, `DMNGT[0]` on the H7) is 1 and `OVR` is 0. It does not distinguish one-shot from circular.
  - **Pace:** with `CONT` = 1 it starts a sequence every 1 ms (a managed thread at `externalEventFrequency`). A conversion is instantaneous and samples 0 mV when no source is attached.
  - **Overrun:** a request the DMA does not serve leaves `EOC` set. The next conversion then sets `OVR`, and the ADC raises no further request until software clears it.
- **BDMA** is the stock `STM32LDMA` at `0x58025400`. The stock platform declares no DMAMUX2.

### P3. The circular streams, by firmware

| Firmware | Prediction | Confidence |
|---|---|---|
| **The 14 generated boots** | no access to DMA1, DMA2, DMAMUX1, BDMA or either ADC; no circular stream | high (`1303` measured 0 accesses at `1cb994c1`) |
| **ArduPilot's fixture** (`sentinel-02-own`, 5 s) | no stream with `CIRC` = 1 (`1303` §2.1: "never written non-zero"); no ADC stream | high |
| **The armed run** (Sentinel-03, 14 s) | **DMA1 stream 3:** DMAMUX1 channel 3, id 9 (`adc1_dma`), peripheral-to-memory, `PAR` `0x40022040` (`ADC1_DR`), `M0AR` `0x38001A60` (SRAM4), `NDT` 8, `PSIZE` = `MSIZE` = 16-bit, `MINC`, `CIRC`, `HTIE`, `TCIE`, `TEIE`, `DMEIE`, `PL` = 10; enabled once at 3.286 ms and never disabled | high (from `run2`) |
| the armed run, ADC3 | **a BDMA stream** for ADC3 (ArduPilot's MCU monitoring: `adc3` gets `VREFEN`/`TSEN`), with `CIRC` and `HTIE`, its request written to DMAMUX2 (`0x58025800`), which the platform does not map | moderate |

**What the firmware reads** (armed, DMA1 stream 3):
- ChibiOS's DMA ISR reads `LISR` and writes `LIFCR` at each stream-3 interrupt, and branches on `TCIF`, then `HTIF` (`hal_adc_lld.c` L230–248).
- `AnalogIn::adccallback` reads only the sample buffer.
- **No read of `S3NDTR`.** Nothing waits on a flag in a loop: the stream is interrupt-driven.

### P4. Whether each request is raised, and how often (armed)

- **Today (stock, `CIRC` dropped):**
  - the stock ADC1 raises request 9 every 1 ms from its first `ADSTART` (about 3.3 ms);
  - the stream serves 8 (`NDT` 8 → 0 by about 11.3 ms), sets `TCIF`, and ArduPilot's ISR runs once (`CTCIF3` at 11.286 ms), so `adccallback` adds one batch of 8 zero samples;
  - the 9th request (12.286 ms) is unserved, so `OVR` sets and the ADC raises no request for the rest of the 14 s;
  - **9 rising edges in all.** ArduPilot never clears `OVR` (its group has no error callback set; moderate confidence).
- **ADC3:** its request goes to `dmamux1@115`, which no DMAMUX1 channel selects, so no stream sees it. Its sequence never finishes (the per-millisecond warning). It reaches no stream either way.
- **With circular mode modeled** (a scratch reload, §1):
  - the stream wraps every 8 requests, so there are about 10,700 requests in the 14 s;
  - `TCIF` sets every 8 ms (and `HTIF` every 4 ms where modeled), so ArduPilot's ISR and `adccallback` run every 4–8 ms;
  - every sample is 0.

### P5. Consumer observers the change would move

- **The generated boots and the fixture:** none (no circular stream, no ADC DMA).
- **The armed run, with circular modeled:**
  - the instruction count and the per-millisecond schedule change, because an ISR runs every 4–8 ms;
  - **the arming is unchanged:** `COMMAND_ACK` 400 result 0 at about 9.0 s;
  - the analog readings stay 0 (the samples are 0 either way). The transmitted MAVLink's values are unchanged, but its byte stream may shift where a field depends on timing. Low confidence on the byte stream.
- **Records:** `main` makes the two `CIRC` records. With `CIRC` allowed alone, the same two writes would refuse on `HTIE`. With both modeled, the armed run makes no DMA record.

### P6. Memory and the shard projection

- **`1201`'s budget, like for like:** the change is a few rules in an existing class, and the fixture runs no circular stream. Predicted within noise, |Δ| < 5 MiB at the median.
- **Shards:** this WO adds about 6–10 cases and as many mutants. At the DMA suite's measured weights that is about 150–300 s at the reference, and eight shards hold it under 20 minutes at 1.53. No ninth shard. Moderate confidence.

### P7. The stop this predicts

- **WO §7's "ruling the ADC stream needs the ADC's own model" fires in part.**
  - The request is raised, but by an unguarded stock ADC at 1 kHz with 0 mV samples, and only until an overrun the DMA's circular mode would prevent.
  - Modeling `CIRC` therefore makes data move that silicon would also move, at a pace and with values silicon would not have.
- **The question for the architect:** the stream either
  - is modeled as circular and fed by the stock ADC, with the ADC's pace and samples named as the ADC guard's row; or
  - stays `unmodeled`.
- The WO's two options for "a request the twin never raises" do not cover a request raised by an unguarded model.

## 1. How it was measured (after §0 was committed at `1cfe182`, 17:37Z)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `589030ca` pins (`container/IMAGE`, fetched this session). One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1303a/` (`R1/`–`R4/`, `budget.log`, the scripts in `h/`, the scratch trees `mtree*`), kept until the architect says.
**Harness:** `h/replay-l.sh` is the committed `tests/1301/replay-1301.sh` plus 1300b's knobs: `LOGP` access-logs peripherals after `LoadELF`; `RUNFOR`, `EXTRA` and `AFTER` add monitor lines; `collapse-repeated-log-entries` is set false (1305's `run2` had it true). `h/loop.sh` runs a set, with every guard recorded and the armed run as 1300b ran it (the injection, `usart1` → `dmamux1@41`). Logged: `dma1`, `dma2`, `dmamux1`, `adcM1S2`, `adc3` and `bdma`. After the run, the monitor reads `S3CR`, `S3NDTR`, `LISR`, `DMAMUX1_C3CR`, ADC1's `ISR`, `CR` and `CFGR`, and the stream's first 16 bytes.

| Run | Tree | What |
|---|---|---|
| **R1** | `main` (this branch at `1cfe182`: `main` `808f4e4` and §0) | the 14 generated boots, the fixture (5 s) and the armed run (14 s) |
| **R2** | `mtree`: R1's tree with the DMA guard's `OnGPIO` counting each request line's edges per stream, read back by a monitor call (`MeasureCounts`) after the run. Measurement only; never committed | the fixture and the armed run |
| **R3** | `mtree-c`: R2 plus **a scratch circular mode**. With `CIRC` = 1 in the guard's copy, at the request that leaves `NDT` = 0 the programmed `NDT` is written back into the stock stream, whose `TCIF` and offset reset are already the stock model's. At half (`NDT` = programmed − programmed/2) with `HTIE` = 1, **`TCIF` stands in for `HTIF`**, because the stock interrupt line is `TCIE && TCIF` only. `CIRC` and `HTIE` are allowed | the fixture and the armed run |
| **R4** | `mtree-e`: R3 plus **`EN` cleared at the end of a non-circular peripheral-to-memory transfer** (the request that takes `NDT` from non-zero to 0; p665, p654), as the WO's acceptance needs | the fixture and the armed run |
| **B** | `1201`'s budget: `tests/1301/ci-memory.sh`, `main` (the worktree) against `mtree-b` (R4's tree with its log lines removed), five boots a side (the script's own count), interleaved | the fixture's 5 s boot |
| **S** | the shard projection: `tools/tviburi-route plan`'s packing reproduced in `h/project.py` (longest first onto the least-loaded shard), over `main`'s `times.tsv`, its 348 cases and 447 mutants, with this WO's items added at the DMA suite's mean weights (cases 5.84 s, mutants 5.93 s) | — |

**The control:** R2 against R1 is **identical** on both runs: every access line (90,821 and 1,436,881), the boot block and both records. The counters change nothing, so R2's counts describe R1's run.

## 2. Results

### 2.1 The circular streams, by firmware

**The 14 generated boots** make no access to DMA1, DMA2, DMAMUX1 or BDMA, and configure no circular stream. **`d1-drv8876` polls ADC3 by the CPU** (652 `ISR` reads, one `DR` read, no DMA), which is outside this WO. The other 13 make no ADC access. **The fixture** configures no stream with `CIRC` = 1 and makes no ADC access (its six streams are `1303`'s).

**The armed run** configures two circular channels:

| | DMA1 stream 3 | BDMA channel 7 |
|---|---|---|
| Request | DMAMUX1 channel 3, id 9 `adc1_dma` (Table 124 p697) | DMAMUX2 channel 7 = 17 (`dma_adc3`, `dmamux2_req_in17`, p608), written at `0x5802581C`, which the platform does not map ("WriteDoubleWord to non existing peripheral", tag `DMAMUX2`) |
| Direction, addresses | P→M, `PAR` `0x40022040` (`ADC1_DR`), `M0AR` `0x38001A60` (SRAM4) | P→M, `CPAR` `0x58026040` (`ADC3_DR`), `CM0AR` `0x38001AA0` (SRAM4) |
| `NDT` | 8 (16-bit items: one channel × `ADC_DMA_BUF_DEPTH` 8) | 24 (`0x18`) |
| `CR` | `0x22D1E`, then `0x22D1F` (`EN`), at 3.286 ms, pc `0x081041B0`/`0x081041B8`: `CIRC`, `HTIE`, `TCIE`, `TEIE`, `DMEIE`, `MINC`, `PSIZE` = `MSIZE` = 01, `PL` = 10 | `0x25AE` (`CIRC`, `HTIE`, `TCIE`, `TEIE`, `MINC`, 16-bit, `PL` = 10), read back `0x0`, then **`0x1`** (ChibiOS's read-modify-write of `EN`), at 3.317 ms |
| Disabled | never in 14 s | never |
| The ADC | ADC1 `CFGR` = `0x2003` (`DMNGT` = 11 circular, `CONT`), `CR` `ADSTART` at 3.286 ms | ADC3 likewise (MCU monitoring: `VREFEN`, `TSEN`) |
| Records on `main` | `TVI-DMA-FIELD` `CIRC` = 1, twice. Each write also fails the `HTIE` row (evaluated offline; `RefusedField` names the first) | none: BDMA is not guarded |

**What the firmware reads and waits on** (DMA1 stream 3):
- ArduPilot's DMA ISR reads `LISR` and writes `LIFCR` once per stream-3 interrupt (R1: once, `CTCIF3` at 11.286 ms; R3: 3,520 `LISR` reads, 3,548 `LIFCR` writes);
- `adccallback` reads the buffer;
- **`S3NDTR` is never read, and nothing polls a flag.** The stream is interrupt-driven, so a stream that never interrupts leaves ArduPilot waiting on nothing: its analog inputs simply stop counting samples.

**BDMA's registers** are §16.6 (`BDMA_CCRx` §16.6.3 p686, `CIRC` bit 5 p688, `DIR` bit 4 p689). ADC3's request on DMAMUX2 is `dmamux2_req_in17` (p608), and on DMAMUX1 it is `dmamux1_req_in115` (p606). **The stock BDMA** (`STM32LDMA.cs` `fb7951bf…`) models circular mode, reloading at 0 and setting `HalfTransfer` when the remaining count equals initial/2 (integer division) (L283–291). But its `CCR` reads 0, so ChibiOS's read-modify-write enables channel 7 with `CIRC`, `HTIE` and `TCIE` cleared (L358). And with no DMAMUX2, no request reaches it. **A row for a BDMA guard; outside this WO's fence.**

### 2.2 Whether each request is raised, and how often

| Stream | Request source | R1/R2 (`main`) | R3 (scratch circular) |
|---|---|---|---|
| DMA1 S3 (id 9) | the stock ADC1 (`STM32H7_ADC`, unguarded): one request per conversion while `CFGR` bit 0 is set, its sequence started every 1 ms by a managed thread at `externalEventFrequency: 1000` (stock `stm32h7.repl` L236), each sample 0 mV | **9 requests in 14 s.** 8 served (`NDT` 8 → 0 at 11.286 ms, `TCIF`, one ISR, one `adccallback` of 8 zero samples); the 9th (12.286 ms) is unserved, which leaves `EOC` set, so the next conversion sets **`OVR`** and the ADC raises no further request (end: ADC1 `ISR` = `0x101F`, `OVR` set) | **13,996 requests, one per millisecond**, to the end; 1,749 wraps, 1,750 halves; `S3NDTR` 4 at the end |
| BDMA ch 7 | ADC3's request goes to `dmamux1@115` (stock platform), which no DMAMUX1 channel selects | none reaches a stream; ADC3's sequence never ends ("Issued a start event before the last sequence finished", 13,995 times) | the same |
| others (R2, for scale) | the consumer-wired SPI1/SPI2 RX (37, 39), the USART guard's 41 | fixture: DMA1 S2 25,073, DMA2 S1 552 (`1303`'s figures); armed: DMA2 S2 372,937, S3 177,534; DMA1 S0 (USART1 RX) 837 | unchanged in count (DMA2 S2 372,933, within the run's timing) |

**So the request is raised, by a model this WO does not guard.** On silicon, ADC1 in `DMNGT` = 11 with `CONT` converts back to back at its sample time and clock (p965: "even if the DMA has reached the last DMA transfer"). That rate is set by `SMPR`, the resolution and `CKMODE`/`ADCSEL`, none of which the stock ADC models. In the twin it converts at 1 kHz, of 0 mV. **Circular mode in the DMA moves data silicon would also move; the pace and the values are the ADC's.**

### 2.3 The pages, by behaviour

| Behaviour | RM0433 Rev 9 | Status |
|---|---|---|
| `NDT` reloads with the programmed value at the end of a circular transfer; requests continue to be served | §15.3.10 p647; `NDT` p665 | **cited** |
| `NDT` = 0: "no transaction can be served even if the stream is enabled" | p665 | cited |
| `EN` stays 1 at a circular wrap | p654 ("If the stream is configured in non-circular mode … EN … is cleared by Hardware"), against `EN`'s p665 | **inferred** from the contrast; never stated |
| `TCIF` set at each wrap | p657 ("At the end of the transfer, the transfer complete flag (TCIF) is set"), with p647 | **inferred**; "each wrap" is not stated |
| `HTIF` at half | p657 ("Once half the data have been transferred on the AHB destination port") | cited for even `NDT` |
| `HTIF` for odd `NDT` | none in §15 | **silent** |
| `EN` cleared at the end of a non-circular transfer | p654; `EN` p665 | cited |
| `CIRC` written with `EN` = 1 | p664 has no per-field line; only p665's "It is forbidden to write these registers when the EN bit is read as 1", which `1303` Q2 ruled against | **no per-field page** |
| circular forbidden in memory-to-memory | §15.3.8 p645 ("the circular and direct modes are not allowed"); Table 111 note p648 | cited |
| circular forbidden with the peripheral as flow controller; `CIRC` forced to 0 | p655; p664 | cited (unreachable: `PFCTRL` refuses) |
| `DBM` forces `CIRC` to 1 | p647, p664 | cited (unreachable: `DBM` refuses) |
| `NDTR` a multiple of the burst with memory burst in circular mode | p647 | cited (unreachable: bursts refuse) |
| the ADC's requests in `DMNGT` = 11; overrun stops them | pp. 964–965; p1016 | cited (the ADC's side) |

### 2.4 Consumer observers each change would move, by board

- **The 14 generated boots:** none. They make no DMA access, so no rule of this WO is reached.
- **The fixture** (R3, R4 against R1):
  - R3 is **identical**: every access, the boot block, no record. No circular stream.
  - R4 (`EN` cleared at a non-circular P→M end) changes only the values of ArduPilot's `CR` reads on its SPI receive streams: 2,354 reads of DMA1 `S2CR` and 19 of DMA2 `S1CR` return `EN` = 0 where `main` returns 1 (`0x10417` → `0x10416`). The access count, every other line and the boot block are identical.
- **The armed run, R3 against R1:**
  - **It still arms:** `COMMAND_ACK` 400 result 0 at 9.001 s, `COMMAND_ACK` 400 result 4 at 7.005 s, 19 heartbeats, "armed" (base_mode `0x80`) from **9.776 s** (`main` 9.775 s).
  - **The records:** both `CIRC` records are gone, and **R3's armed console has no `would-refuse` from any guard** (R1: the two `CIRC` records only). The WO's ground, "an armed run is not a pass on main's guards", is met on this run by Q1 (a).
  - **The instruction count** rises by 846,958 (+0.19 %; `0x1ACDABEB` → `0x1ADA9859`), and the boot block's final PC differs.
  - **MAVLink:** the same 93 frames, 2,908 bytes, with the same IDs in the same order. Payloads differ in timing fields only:
    - `SYS_STATUS` (×7): `load` 411 → 412 (0.1 % more CPU); its voltage and current fields stay 0;
    - `SERVO_OUTPUT_RAW` (×35): `time_usec`;
    - `TIMESYNC` (×1 on USART1, ×1 on USART3): `ts1`.
    - `HEARTBEAT`, `COMMAND_ACK`, `PARAM_VALUE` and `STATUSTEXT` payloads are identical. Their virtual-time stamps move by −0.6 ms to +3.1 ms over the run.
  - **USART3, ArduPilot's second MAVLink port** (20 frames; `h/mavdiff.py … usart3`): only `TIMESYNC.ts1` differs (with its CRC bytes); its 19 heartbeats differ in time only.
  - **USART2:** every access the same in value and PC, shifted in time.
  - **The access log** gains 13,988 `adcM1S2 ReadUInt16 from 0x40 (DataRegister)` lines. These are the stock DMA engine's reads, **stamped with whatever PC the CPU is at**: `1303`'s USART1 `TDR` finding, again, for Aethelgard.
- **The armed run, R4 against R3:** only the 39,074 `CR` reads of DMA2's SPI receive streams change (`EN` 1 → 0). The boot block, every other line and the MAVLink are identical.

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | held. Each page in §2.3 was checked by me with `rmpage.sh`, at its section heading. The non-circular `EN` sentence is §15.3.15 p654 (heading p653 L25354, sentence L25376), and the guard's suspension citation, §15.3.16 p654 (L25381), is a different sentence on the same page. `EN` at a wrap and `TCIF` at each wrap are inferred, `HTIF` for odd `NDT` is silent, and `CIRC` has no per-field line |
| P2 | held: the stock DMA served 8 and logged one "no data to send"; the ADC raises one request per conversion at 1 kHz; the overrun stops it (ADC1 `ISR` `0x101F`) |
| P3, generated boots | held for DMA (0 accesses). **Not predicted:** `d1-drv8876` polls ADC3 by the CPU (no DMA) |
| P3, fixture | held: no circular stream, no ADC access |
| P3, armed DMA1 S3 | held in every field |
| P3, armed ADC3 | held: BDMA channel 7, `CIRC` and `HTIE`, DMAMUX2 id 17 to an unmapped address. **Not predicted:** the stock BDMA models circular mode but loses `CIRC` on ChibiOS's read-modify-write |
| P3, reads | held: `LISR`/`LIFCR` per interrupt; no `S3NDTR` read; no poll |
| P4, `main` | **held exactly:** 9 rising edges; `OVR` set |
| P4, ADC3 | held |
| P4, circular | held: 13,996 requests (one per ms), 1,749 wraps |
| P5 | held: arms at 9.001 s, analog values 0. **The byte stream does differ,** in `SYS_STATUS.load` and two timestamp fields; the "armed" heartbeat moves 1 ms |
| P5, records | held (offline: `CIRC` then `HTIE`; R3 with both allowed: none) |
| P6, memory | §4 |
| P6, shards | held: eight shards hold (§4) |
| P7 | held: the request is raised by an unguarded model, a case the WO's two options do not cover |

## 4. Memory and the shard projection

**Memory (B), `1201`'s budget:**
- `tests/1301/ci-memory.sh`, five boots a side, interleaved, `main` against `mtree-b` (circular mode, the half-transfer stand-in, the `EN` clear and the counters, with no log lines). Load 9–10 on Furnace, every wall 11.7–11.9 s.
- `MEDIANS main 1004656 KiB, branch 1004424 KiB, difference -232 KiB (-0.2 MiB)`, **PASS**. Every boot counts; every branch boot is under 1,572,864 KiB.
- **What this measures:** the fixture runs no circular stream, so it measures the code, not the circular traffic.
- The armed run's peaks are not the budget, but for the record: R1 2,952,592 KiB, R3 3,000,440, R4 3,018,520. These are single runs with the access logging on, and the peak rises with wall time on a loaded host (the roadmap row).
- **A prototype figure.** The built guard adds the interrupt lines and the copies. CI judges it.

**The shard projection (S),** `1202a`'s method: a shard job is 53 s of setup plus its load × 1.53; `ci`'s wall adds 1.7–2.0 min.

| Plan | Items | Total at 1.00 (s) | Longest shard (s) | Shard job at 1.53 (min) | `ci` wall at 1.53 (min) |
|---|---|---|---|---|---|
| `main`, eight shards | 795 | 5,016 | 627 | 16.9 | 18.6–18.9 |
| + 8 cases, 8 mutants | 811 | 5,110 | 639 | 17.2 | 18.9–19.2 |
| + 12 cases, 12 mutants | 819 | 5,157 | 645 | 17.3 | 19.0–19.3 |
| nine shards, + 12 and 12 | 819 | 5,157 | 573 | 15.5 | 17.2–17.5 |

- **No ninth shard is due by the projection:** the slowest job stays at 17.3 min, and `ci`'s wall at 19.3 min or less.
- **The margin is thin.** `1306`'s first CI run measured `ci`'s wall at 19.2 min with eight shards, about 0.3–0.6 min over this model for the same plan (its shard 3, on an EPYC 7763 at 1.50, took 17.3 min). With this WO's items, a real run could reach about 19.5–19.9 min.
- **What Q1–Q5 as recommended imply:** about 10 cases and 14 mutants (circular wrap, `CIRC` = 0 ending with `EN` cleared, `CIRC` under `EN`, the half-transfer flag and interrupt with its clear, odd `NDT`, circular memory-to-peripheral and memory-to-memory, and the ledger rows). By the model, 10 and 14 give `ci`'s wall 19.1–19.4 min, and the model crosses 20.0 at about 30 and 30. **With the measured real-run gap (0.3–0.6 min), 10 and 14 reach 19.4–20.0 min:** at the line.
- **Recommendation (Q7): the ninth shard first, in this WO, as `1306` built the eighth.** It is outside this WO's fence (`.github/workflows/ci.yml`'s shard jobs, `shard.yml`'s `of`, the aggregate's count), so it needs the fence granted. Otherwise, eight shards, and §7's trigger decides in CI.

## 5. The stop (WO §1 step 1, §7), and the questions

**Two §7 stops fire. The shard stop is at the line, not past it (§4, Q7).**
- **"Ruling the ADC stream needs the ADC's own model," in part.** The request is raised, so the WO's two options for "a request the twin never raises" do not fit. It is raised by the unguarded stock ADC, at 1 kHz and of 0 mV, and today only until an overrun that circular mode would prevent (§2.2). The DMA's side can be ruled without an ADC guard (Q1). What one would need is listed below Q1.
- **"An observer would change, on a path RM0433 says is valid."** Modeling circular mode changes the armed run's MAVLink in three timing fields and its instruction count (§2.4), on a path p647 and p965 call valid. The fixture and the generated boots are unchanged. **The order:** the ADC's stream starts at 3.286 ms (`ADSTART`, then `S3CR` `EN`); the first difference is its first half-transfer interrupt (`LISR`/`LIFCR` at 7.286 ms, after four requests; `main`'s first interrupt is the transfer complete at 11.286 ms); every later difference follows from the added interrupts.

**Q1. The ADC stream (DMA1 S3, id 9).**
- **(a) Recommended: model circular mode in the DMA guard**, fed by whatever raises the request.
  - The stock ADC is an unguarded, inherited, unaudited model, so its pace and its samples are the coverage queue's (`RUL-17`'s scope), not this guard's.
  - Id 9 is already in `RaisedToMemory` as "a model raises it".
  - The ledger's `CIRC` row says what is modeled (the reload, `EN`, the flags) and names the source's gaps as the ADC guard's row.
  - Measured (R3): the armed run makes no DMA record and arms as before, with §2.4's named differences.
- **(b) `CIRC` = 1 on a request raised only by an unguarded model is `unmodeled`.** The armed run keeps refusing, now on a reason that names the ADC. Every circular ADC stream, which ArduPilot always configures on an H7 with analog inputs, refuses until an ADC guard exists.
- **What an ADC guard would need** (each from the stock source, `STM32_ADC_Common.cs` `0cc82d40…`, and RM0433 Rev 9):
  - **the conversion clock:** `CKMODE` (ADCx_CCR) and `ADCSEL` (RCC `D3CCIPR`, p419). The stock model has neither and paces conversions at `externalEventFrequency` (1 kHz);
  - **the conversion time:** `SMPRx`, `RES` and the clock. These set the request rate silicon makes;
  - **`DMNGT`'s four values** (p1016). The stock model has `DMAEN`/`DMACFG` only, so one-shot (01) raises requests like circular (11), where silicon stops after the DMA's last transfer (p964), and 10 (DFSDM) raises none;
  - **the overrun** (`OVR`, `OVRMOD`, p964);
  - **the enable sequence:**
    - `DEEPPWD` is reserved in the stock model and reads 0, where silicon resets it to 1;
    - `ADVREGEN`/`LDORDY`;
    - `ADCAL` is a tag, so no calibration runs;
    - `LINCALRDYW1–6` always read 1;
  - **the sample sources:** 0 mV with none attached. The internal channels (`VREFEN`, `TSEN`, `VBATEN` on `ADC3_CCR`) are tags;
  - **ADC3's request:** the stock platform wires it to `dmamux1@115` (p606). ArduPilot routes ADC3 through DMAMUX2 (17) to BDMA, and the platform has no DMAMUX2;
  - **`adcM1S2`:** ADC2 is a mock behind ADC1 (only its `CR` at +0x100).
- **Ask:** (a) or (b)?

**Q2. `CIRC` written while the stream runs.**
- RM0433 gives `CIRC` no per-field line (p664). Only p665's general sentence covers it, and `1303` Q2 ruled against that sentence, so `CIRC` today takes the write.
- **(i) Recommended: a changed `CIRC` while the stream runs (or is circular and enabled) refuses `unmodeled`,** naming the stream, the field, p664 (no line) and p665. Silicon's behaviour is not stated: the twin cannot say whether the stream wraps.
- (ii) `predicted-fault` on p665's sentence. That would be the first field ruled that way, against `1303` Q2, and `RUL-18` needs a bench row.
- (iii) Take the write (Q2 as it stands). The WO's acceptance clause then does not hold.
- No firmware measured changes `CIRC` under `EN`.
- **Ask:** (i), (ii) or (iii)?

**Q3. `HTIF`.**
- **Odd `NDT`: RM0433 is silent** (§2.3).
  - **(i) Recommended:** a circular stream with odd `NDT` refuses `unmodeled` at `EN`, whatever `HTIE` is. Silicon sets `HTIF` at a count the twin cannot name, and a firmware may poll the flag with `HTIE` = 0 (WO §5: "a flag that lies").
  - (ii) Renode's stock BDMA rule (remaining = programmed/2, integer division; `STM32LDMA.cs` L289), named "inherited from a sibling stock model, unaudited", which is not silicon's.
  - (iii) With odd `NDT`, `HTIF` is never set, acknowledged by name. That is a flag silicon sets and the twin does not, so it is listed for completeness, not recommended.
  - ArduPilot's `NDT` is 8.
- **The scope:**
  - **Recommended:** `HTIF` is modeled on circular streams, where this WO's acceptance asks for it. On a non-circular stream the acknowledged row "`HTIF` never sets" stays, and `HTIE` = 1 there stays refused as today.
  - Extending it to every peripheral-to-memory stream is silicon's behaviour too, but unmeasured on ArduPilot's SPI and USART streams (`HTIE` = 0 on all of them). A row.
- **The form (not a question, stated for the record):**
  - The stock interrupt line is `TCIE && TCIF`. The guard therefore drives its own stream lines: the stock line OR (`HTIE && HTIF`).
  - It serves `HTIF` in `LISR`/`HISR` from its own copy, and `CHTIF` in `LIFCR`/`HIFCR` clears it (those rows move from acknowledged to modeled).
  - R3 measured the firmware path with `TCIF` standing in for `HTIF`. ChibiOS calls the same callback for either (`hal_adc_lld.c` L242–248), so the built form's `LISR` values differ from R3's, but not the ISR's path.
- **Ask:** (i) or (ii); circular-only?

**Q4. `EN` at the end of a non-circular transfer** (the WO's acceptance: "the same stream with `CIRC` = 0 ends with `EN` cleared").
- The stock model never clears `EN`; `1303` Q3 (c) kept it so, with three states, because the stock model completes memory-to-peripheral and memory-to-memory transfers inside the `EN` write.
- **Recommended:**
  - **for a peripheral-to-memory stream, whose transfer the twin paces by requests:** at the request that takes `NDT` to 0, with `CIRC` = 0, `EN` is cleared and `TCIF` set (p654, p665). That stream's "completed" state disappears: it is off.
  - **memory-to-peripheral and memory-to-memory keep Q3 (c).**
- Measured (R4): on ArduPilot this changes only the `EN` bit of ArduPilot's own `CR` reads on its SPI receive streams (2,373 on the fixture, 39,074 on the armed run). Nothing else differs: the boot block, every other access and the MAVLink.
- `TVI-DMA-DONE` then applies to memory-to-peripheral and memory-to-memory only. A re-enable with `NDT` = 0 stays `TVI-DMA-RELOAD`.
- **Ask:** as recommended, or keep `EN` as the stock holds it (then the acceptance's second sentence cannot be met)?

**Q5. Circular mode where the twin cannot pace it.**
- **Recommended, all `unmodeled` at `EN`:**
  - `CIRC` = 1 on a memory-to-peripheral stream: the stock model moves the whole transfer at `EN`, unpaced, so a reload would loop without a request;
  - `CIRC` = 1 on memory-to-memory: forbidden (p645). It is `unmodeled` as the WO says, because RM0433 does not say what silicon does with it.
- `DBM`, `PFCTRL` and the bursts stay refused, which makes p647's and p655's rules unreachable.
- A circular stream on a request no model raises is already `TVI-DMA-REQUEST`.
- No firmware measured does any of these.

**Q6. The armed run's observer differences (the second §7 stop).**
- Accept them, named in the FEEDBACK, as the consequence of the stream now running as on silicon:
  - `SYS_STATUS.load` +0.1 %;
  - `SERVO_OUTPUT_RAW.time_usec`, and `TIMESYNC.ts1` on USART1 and USART3;
  - the "armed" heartbeat 1 ms later;
  - the instruction count +0.19 %.
- **Ask:** accepted?

**Q7. The ninth shard** (§4): build it first, in this WO, with the fence for `.github/workflows/ci.yml`, `shard.yml` and the aggregate's count granted; or stay at eight, and let CI's wall decide.

**Rows, outside this WO:**
- **BDMA and DMAMUX2** (§2.1):
  - the armed run's ADC3 stream is BDMA channel 7, circular, on DMAMUX2 id 17;
  - the platform has no DMAMUX2;
  - the stock BDMA's `CCR` reads 0, so ChibiOS's read-modify-write drops `CIRC`, `HTIE` and `TCIE`;
  - BDMA is unguarded.
- **The ADC guard** (Q1's list), including `d1-drv8876`'s polled ADC3.
- **For Aethelgard (a finding):** the stock DMA engine's peripheral reads (`ADC1_DR`) are access-logged with the CPU's current PC, as `1303` found for USART1's `TDR` writes.
