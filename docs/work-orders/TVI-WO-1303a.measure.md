# TVI-WO-1303a — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1303a-dma-circular`, from `main` `808f4e4`.
**State:** §0 written and committed before any Renode run of this work order. §1 onward follow the runs.
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
