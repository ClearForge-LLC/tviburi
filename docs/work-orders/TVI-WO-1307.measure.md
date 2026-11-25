# TVI-WO-1307 — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1307-adc-guard`, from `main` `fe17ea4` (`1303a` merged; this WO written).
**State:** predictions (§0), committed before any run of this work order.
**Stock source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/Analog/`: `STM32H7_ADC.cs` (sha256 `fb587b9a…`), `STM32_ADC_Common.cs` (`0cc82d40…`), read whole by me. No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1, M2).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`. At the time of these predictions the ADC chapter (§25) had not been read: every RM0433 statement in §0 is **from memory**, except the pages `1303a` already cited (p964–965, p1016, p419, p920).

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock source above, and the stock entries (`stm32h7.repl` as `1303` copied it, L84–91, L231–238): `adc3` at `0x58026000` (`RegistrationPeripheralAlias.ADC3`, `dmamux1` channel 115, `-> exti@75`), `adcM1S2` at `0x40022000` (`ADC12`, `dmamux1` channel 9, `-> nvic@18`), both `referenceVoltage: 3.3`, `externalEventFrequency: 1000`;
- `1303a.measure.md` (its §2.1–§2.4 and §5 Q1): the 13 generated boots other than `d1-drv8876` and ArduPilot's fixture make no ADC access; `d1-drv8876` polls ADC3 by the CPU (652 `ISR` reads, one `DR` read, no DMA); the armed run's ADC1 `CFGR` = `0x2003` with `ADSTART` at 3.286 ms, DMA1 stream 3 circular, `NDT` 8 (one channel × 8); ADC3 on a BDMA channel through DMAMUX2 (unmapped); the stock ADC1's 13,988 `DR` reads by the DMA engine;
- `RUL-13`'s queue rows: `RCC.D3CCIPR.ADCSEL` written 1 by ArduPilot (row 15); `AHB4ENR.ADC3EN` (row 28); the `ADC3` `JQDIS`, `ADCAL`, `SMP0–9` rows from `AET-WO-1211c`;
- the RCC guard on `main`: `QueryKernelClock` has **no ADC rule** (its `Clocked` table: `spi1`, `spi2`, `i2c1`, `i2c2`, `usart1–3`, `rng`; no `ADCSEL` mux), so it answers `adc12`/`adc3` "not modeled"; `ADC12EN`'s and `ADC3EN`'s rows say the kernel clock is "an ADC guard's row"; `IRccWatched` (`IsRunning`, `RunningName`, `KernelCitation`); the USART guard's `KernelHz` (it derives its kernel clock in Hz from the RCC guard's registers and its `Written` event).

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9, from memory; §1 reads it)
- **The clock.** `CKMODE` = 00: the asynchronous clock, `adc_ker_ck` chosen by `ADCSEL` (00 `pll2_p_ck` at reset, 01 `pll3_r_ck`, 10 `per_ck`, 11 reserved), divided by `PRESC` (1–256). `CKMODE` = 01/10/11: the AHB clock `adc_hclk` /1, /2, /4. On revision V silicon the ADC divides its input by 2 again (from memory, uncertain). `CKMODE` and `PRESC` "can be written only when the ADC is disabled" (`ADEN` = 0 on both ADCs of a pair).
- **The conversion time.** `t_conv` = sampling time (`SMPx`: 1.5, 2.5, 8.5, 16.5, 32.5, 64.5, 387.5, 810.5 cycles) + SAR time by resolution (16-bit 8.5, 14-bit 7.5, 12-bit 6.5, 10-bit 5.5, 8-bit 4.5 cycles). **`RES`'s encoding differs by silicon revision** (rev Y against rev V; from memory, low confidence on the values).
- **The enable sequence.** `DEEPPWD` resets to 1; software clears it, sets `ADVREGEN`, waits `t_ADCVREG_STUP` (about 10 µs; `LDORDY`), calibrates (`ADCAL` with `ADEN` = 0; `ADCALLIN` for linearity), then sets `ADEN` and waits for `ADRDY`. `ADCAL` and `ADEN` are refused by silicon in the wrong state (write ignored), not faulted.
- **`DMNGT`** (p1016): 00 `DR` only; 01 DMA one-shot (requests stop after the DMA's last transfer, the DMA's `TC`); 10 DFSDM; 11 DMA circular.
- **Overrun** (p964): `OVR` sets when a conversion ends with `EOC` still set; `OVRMOD` = 0 keeps the old data, 1 overwrites. With DMA, an overrun stops requests until `OVR` is cleared.
- **Continuous mode** (`CONT`) converts the sequence back to back with no trigger; a software `ADSTART` starts it.

### P2. The stock model, by source (each to be probed, §1 M2)
- **Map: the generic STM32 map, not H7's.** `CFGR` bit 0 `DMAEN`, bit 1 `DMACFG` (H7's `DMNGT[1:0]`): 01 (one-shot) warns "DMA One Shot mode not supported" and raises requests as circular; 10 raises none. `RES` is two bits at 2–3 (H7: three at 2–4), so bit 4 is dropped; the 2-bit value maps 00→16, 01→14, 10→12, 11→8 bits. `ALIGN` at bit 5 (H7: `EXTSEL[0]`), so an `EXTSEL` with bit 0 set left-aligns every sample.
- **No clock.** `CKMODE` is a stored field in `CCR` (`0x308`) with no effect; `CFGR2` has a `CKMODE` tag at 30–31 (H7: `LSHIFT`); `ADCSEL` does not exist; `SMPR1`/`SMPR2` are tags (read reset, writes dropped).
- **Pacing.** With `CONT` or `EXTEN` ≠ 0, `ADSTART` starts a managed thread at `externalEventFrequency` (1 kHz) that starts a sequence per tick; each sequence converts one channel immediately, and the next channel only when `DR` is read or `EOC` cleared (the DMA's read does it). Without either, `ADSTART` converts at once. A sequence still running at a tick logs "Issued a start event before the last sequence finished".
- **Enable sequence.** `ADEN` sets `ADRDY` at once whatever `ADVREGEN`, `DEEPPWD` or the clock; `DEEPPWD` reserved (reads 0); `LDORDY` reads `ADVREGEN` at once; `ADCAL` a tag (written 1 is dropped, so a firmware polling `ADCAL` = 0 passes at once); `LINCALRDYW1–6` read 1; `ADSTART` with `ADEN` = 0 converts nothing (`SampleNextChannel` exits) but sets the flag.
- **Samples.** 0 mV on any channel with no `ADCChannelSource`-style child at its address; converted against 3.3 V; internal channels are tags.
- **Overrun.** `OVR` sets when a conversion ends with `EOC` set; `OVRMOD` read; DMA requests stop while `OVR` is set.
- **ADC2** (`+0x100`): `ADEN` a tag, `ADDIS` reads 0; every other ADC2 register absent.
- **Width:** words and half-words (`IWordPeripheral`); no byte access.

### P3. The generated firmware (14 boots)
- **13 make no ADC access**, as `1303a` measured. High.
- **`d1-drv8876` polls ADC3 by the CPU:** `CR` (`DEEPPWD` cleared, `ADVREGEN`), perhaps `ADCAL`, `ADEN`, `ISR` polled for `ADRDY`, `SQR1` and `SMPR1`/`SMPR2` written, `ADSTART`, `ISR` polled for `EOC`, one `DR` read. Moderate.
- **Its clock:** it writes neither `ADCSEL` (reset: `pll2_p_ck`) nor PLL2, and either writes `ADC3_CCR.CKMODE` ≠ 00 (the AHB clock) or leaves 00. **If it leaves 00 with PLL2 off, its `ADSTART` has no kernel clock** and the WO's clock rule refuses `d1-drv8876`'s boot: a §7 stop on a path RM0433 says is invalid (the firmware's defect, not the twin's). Low confidence either way.
- **Its sample:** one channel, read once, 0 mV today; the declared level would change only the value it reads, which a consumer observer may or may not judge.

### P4. ArduPilot, `sentinel-02-own` (the committed fixture, ELF `a799dfb6`, 5 s)
- **No ADC access** (as `1303a`), but RCC writes: `ADCSEL` = 01 (`pll3_r_ck`), `ADC12EN`, `ADC3EN`. High.

### P5. The armed run (Sentinel-03, 13 s)
- **ADC1 (`adcM1S2`):** ChibiOS's ADCv4 start: `CR` `DEEPPWD` cleared, `ADVREGEN`, a wait, `ADCAL` with `ADCALLIN` (polled until 0), `ADEN` polled on `ADRDY`; `CCR.CKMODE` = 00 with `ADCSEL` = 01 (`pll3_r_ck`) and a `PRESC` divider; `CFGR` = `0x2003` (`DMNGT` = 11, `CONT`, `RES` = 000); `SMPR` at the longest or second-longest sample time; `PCSEL` and `SQR1` for one channel (the battery voltage pin; current perhaps on ADC3 or not sampled); `ADSTART` at 3.286 ms. Moderate.
- **ADC3:** the same start, with `VREFEN` and `TSEN` (internal channels, the WO's `unmodeled` list) and its DMA through DMAMUX2 to BDMA, so its samples never reach memory in the twin. Moderate.
- **ADC2:** `ADEN` cleared or never written; no slave use. Moderate.
- **No** injected group, analog watchdog, oversampling or offset on either. Moderate.
- **The order holds on silicon:** `ADVREGEN` before `ADEN`, `ADCAL` before `ADEN`, `ADSTART` on a running kernel clock (`pll3_r_ck` enabled by then). Moderate.

### P6. The rate (WO (1))
- **Silicon:** ADC1's clock is `pll3_r_ck` / `PRESC` (/2 again on rev V), in the 10–50 MHz range; with one channel, 16-bit, and a long sample time (387.5 cycles), a conversion takes 8–40 µs: **25–120 k conversions per second**, each a DMA request, so `HTIF`/`TCIF` every 4 or 8 conversions (`NDT` 8): **6–30 k DMA interrupts per second**, against the stock model's 1 k requests and 250 interrupts per second. Low confidence on the factor; high that it is well over 10.
- **So pacing at silicon's rate multiplies the armed run's ADC work by 25–120.** The DMA guard serves each request; ArduPilot's ISR and `adccallback` run 6–30 k times a second. **The armed run's wall time rises, perhaps by more than its budget**, and its MAVLink timing fields move: a §7 stop ("an observer would change, on a path RM0433 says is valid"), and a cost question for CI.
- **`d1-drv8876`:** one software-started conversion, polled: the rate is one conversion's time, which changes when `EOC` rises (after `t_conv` instead of at once), and with it its `ISR` poll count.

### P7. Consumer observers (Aethelgard `589030ca`; to be surveyed, §2)
- **The armed run's MAVLink `SYS_STATUS`:** `voltage_battery` (and `current_battery` if a current pin is sampled) reads the ADC: 0 mV today, so 0 (or a value ArduPilot clamps); with a declared level, the declared voltage × the board's divider. **Changed**, by design.
- **The armed run's timing fields** (`1303a`'s three, and the instruction count): changed by the rate (P6).
- **Arming:** ArduPilot's battery failsafe and pre-arm checks read the voltage; at 0 V with `BATT_MONITOR` set, arming may already pass only because no monitor is configured. A declared level that passes the arming check changes nothing; one below `BATT_ARM_VOLT` would stop arming. Low confidence.
- **The generated boots:** `d1-drv8876`'s judged value, if Aethelgard judges its ADC read (unknown); every other board unchanged. **The fixture:** unchanged (no ADC access).
- **From the form** (a wrapper, if `STM32H7_ADC` must be wrapped rather than subclassed): the `peripherals` listing's kind for `adcM1S2`/`adc3`, if Aethelgard lists them. `STM32H7_ADC` is **not** sealed, so `RUL-07`'s subclass may be possible (to measure: its bus methods are not `virtual`, so a subclass re-implementing the interfaces works, as the SPI guard's).

### P8. The samples (WO (1))
- **ArduPilot:** ADC1 samples the battery voltage pin of Sentinel-03's hwdef (one channel); ADC3 samples `VREFINT` and the temperature sensor. **A platform would declare the pin's level in volts at the pin**, after the divider (the WO's fence: the consumer declares the level at the pin).
- **`d1-drv8876`:** one ADC3 channel, named by its `SQR1`.
- **The consumer property:** Aethelgard's board specification has no analog level for an ADC pin today (moderate), so **declaring the samples needs a consumer property that does not exist**: a §7 stop, naming it. The twin's side is a guard property like the GPIO guard's `external` (`1301a`).

### P9. Memory (`1201`'s budget, like for like)
- `1201`'s budget boots ArduPilot's 5 s fixture, which makes no ADC access, so a guard costs its compiled code only: **within ±12 MiB of `main`** at the median, eleven boots a side.

### P10. The shard projection (`1202a` F2's method, at 1.53)
- Nine shards on `main` (`1303a` added the ninth). This guard adds 400–550 s at the reference, as the last four. **The slowest shard stays under 20 minutes at 1.53 with nine**, and `ci`'s wall stays under 20 minutes: no tenth shard. Moderate; the armed run's cost under silicon's rate (P6) is the risk, if its case runs in CI.

## 1. How it was measured (after §0 was committed at `05b91aa`, 20:21Z)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `589030ca` pins (`container/IMAGE`, fetched this session). Renode `…aet1605`. One container of mine at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1307/` (`m1/`, `p/`, `f/`, `bench/`, `mem/`, `budget.log`, `ap/`, `rm/adc-fields.md`, `observers.md`, the scripts in `h/`), kept until the architect says.
**Harness:** `h/replay-l.sh` is the committed `tests/1301/replay-1301.sh` plus `1300b`'s knobs (`LOGP`, `RUNFOR`, `EXTRA`, `AFTER`; `collapse-repeated-log-entries` false) and one of its own, `PLATX` (a platform update appended after the restatements). `h/loop.sh` runs a set, every guard recorded; the armed run is `1305`'s capture with its injection (`EXTRA='include @/inj/inject.resc;'`), as `1300b`/`1303a` ran it.

| Run | What | How |
|---|---|---|
| **M1** | the 14 generated boots, ArduPilot's fixture and the armed run on `main`'s release, `rcc`, `adcM1S2`, `adc3` access-logged | `h/m1.sh` over `1305`'s captures (read-only); `h/adc-analyse.py` separates the CPU's accesses from the DMA engine's `DR` reads |
| **P** | the pacing prototype: the armed run with the stock ADC1's own `externalEventFrequency` set to silicon's rate (37,879 Hz, rev V; 75,758 Hz, rev Y) by an appended `adcM1S2:` update, against `main`, in two back-to-back pairs, no ADC logging (the consumer logs none); one logged run checks the rate | `h/proto.sh`; `h/mav.py` (`1305`'s, with `SYS_STATUS` and an id count added), `h/mavdiff.py`, `1305`'s `bootblock.py` |
| **F** | the form: a pass-through subclass of `STM32H7_ADC` (`bench/model/ProbeSTM32H7_ADC.cs`) cancelled-and-restated onto both ADCs; `d1-drv8876` and the armed run, compared to M1 line by line | `h/form.sh` |
| **B** | the one-file memory, `1201`'s budget, eleven boots a side | `tests/1301/ci-memory.sh` from `bench/` (this branch plus the probe and its restatement in the replay) against this branch |
| **RM** | RM0433 Rev 9 §25 and DS12117 Rev 10's ADC tables | a subagent's extraction, `rm/adc-fields.md`; its five load-bearing quotes and their pages re-checked by me against the text (`rmpage.sh`) |
| **O** | the consumer's observers, Aethelgard `589030ca` through git objects | a subagent's survey, `observers.md`; the witness (`pinwitness.rs`:201–246), the stimulus (`:35–45`) and the kind map (`platform.rs`:89–90) re-read by me |
| **AP** | ArduPilot's and ChibiOS's ADC code, `/opt/ardupilot` `fb773567` in the pinned `aethelgard-ardupilot@sha256:e1809b90…` | copied out once (`ap/`): `AnalogIn.cpp`, `ADCv4/hal_adc_lld.{c,h}`, `hwdef/common/stm32h7_mcuconf.h` |
| **S** | the shard projection | `tools/tviburi-route plan` built from this tree, on `main`'s `times.tsv`, cases and mutants, with this guard's items at the last guards' sizes |

## 2. Results

### 2.1 The field table and the sequence, by firmware (M1)

**13 generated boots and ArduPilot's fixture make no ADC access.** The fixture's RCC writes: `D3CCIPR` = `0x10010100` (`ADCSEL` = 01, `pll3_r_ck`) at pc `0x81038DE`; `AHB4ENR` with `ADC3EN`; `AHB1ENR` written 4,776 times, never with `ADC12EN`.

**`d1-drv8876`, ADC3, polled by the CPU** (pc `0x80008BA` writes, `0x80008A4` reads; the generator is Aethelgard's `svd-hal/src/board/adc.rs`:306–383). Clocks: `AHB4ENR` = `0x1000023` (`ADC3EN`); sys_ck the HSI, 64 MHz; `D1CFGR` never written (`HPRE` /1).

| t (µs) | Access | Value | Fields | RM0433's rule, and whether this access keeps it |
|---|---|---|---|---|
| 9.58 | `CCR` (0x308) | `0x30000` | `CKMODE` = 11 | ADCs disabled (p1039): kept |
| 15.74 | `CR` | `0x0` | `DEEPPWD` = 0 | disabled (p1003): kept |
| 15.89 | `CR` | `0x10000100` | `ADVREGEN`, `BOOST` = 01 | disabled (p1003): kept |
| 15.89–100 | `ISR` ×650 | `0x1000` | polls `LDORDY` (a bound, `REGULATOR_READS 650`) | `t_ADCVREG_STUP` (DS12117: max 10 µs): kept |
| 100 | `CFGR`, `PCSEL`, `SMPR1`, `SQR1` | `0x80000008`, `0x20`, `0x38000`, `0x140` | `JQDIS`, `RES` = 010; channel 5; `SMP5` = 111 (810.5 cycles); one conversion of channel 5 | `ADSTART` = 0 (p934, p1016, p1020): kept |
| 107.58 | `CR` | `0x10010100` | `ADCALLIN` | disabled (p1005/p1009): kept |
| 107.72 | `CR` | `0x90010100` | `ADCAL` | `ADEN` = 0 (p1003): kept; polled once (the stock clears it at once) |
| 107.72 | `ISR` | `0x1` | `ADRDY` cleared | p933's procedure: kept |
| 108.28 | `CR` | `0x1FC10101` | `ADEN` (the read-modify-write carries `LINCALRDYW1–6` = 1 back) | "all bits of ADC_CR are 0 (ADCAL=0, …, ADEN=0) except ADVREGEN" (p1007/p1011): kept by the parenthesis; `BOOST`, `ADCALLIN` and `LINCALRDYW` are not command bits |
| 108.28 | `ISR` | `0x1001` | `ADRDY` seen | kept |
| 108.70 | `CR` | `0x1FC10104` | `ADSTART` | `ADEN` = 1, `ADDIS` = 0 (p1007): kept |
| 108.70 | `ISR` | `0x100F` | `EOC` seen at once (stock) | — |
| 108.98 | `DR` | `0x0` | one read | — |

**The consumer's own lines on this board:** `adc3 SetVoltage 0 5` (0 µV on channel 5, Renode's `IADC.SetVoltage(microvolts, channel)`), a watchpoint on `CR` writes (`AETHELGARD-GPIO 58026008 …`, with the instruction count) and one on `DR` reads (`AETHELGARD-DR 0 …`), and `DR` read back after the run.

**The armed run, ADC1 (`adcM1S2`) and ADC3** (ChibiOS ADCv4, `hal_adc_lld.c`; 37 and 43 CPU accesses):

| t (ms) | ADC1 | ADC3 | Rule, and whether kept |
|---|---|---|---|
| 3.200 / 3.300 | `CCR` = 0 (`CKMODE` 00, `PRESC` /1); `DIFSEL` = 0; `CR` = `0x10000000` (`ADVREGEN`; `DEEPPWD` cleared in the same write) | the same | disabled (p1003, p1039): kept. One write clearing `DEEPPWD` and setting `ADVREGEN` is not forbidden by the text |
| 3.286 / 3.317 | `CR` read-modify-written twice unchanged; `ADCAL` (`ADCALLIN` = 0); `ADCAL` polled (0 at once) | the same | `ADEN` = 0: kept. ChibiOS waits 10 µs after `ADVREGEN` (`osalSysPolledDelayX`), and 86 µs pass |
| | `CR` \|= `BOOST` = 01 | the same | `ADSTART` = 0: kept. **The value: see the rate (§2.4)** |
| | `ISR` = `ADRDY`; `ADEN`; `ADRDY` polled | the same | kept (by the parenthesis, as above) |
| | — | **`CCR` read-modify-written three times: `VREFEN`, then `TSEN`, then `VBATEN`, with `ADEN` = 1** (pc `0x81044B8`, `…C4`, `…D0`) | **"only when the ADCs are disabled (… ADEN=0)" (p1038): broken.** The stock tags read 0, so each write carries one bit; on silicon the three accumulate to `0x01C00000` |
| | `ISR` = `0x1001` (clears `ADRDY`; bit 12 `LDORDY` is read-only); `CFGR2` = 0; `PCSEL` = `0x400`; `LTR1` = `HTR1` = 0; `LTR2/HTR2/LTR3/HTR3`, `AWD2CR`, `AWD3CR` = 0; `SMPR1` = 0, `SMPR2` = `0x6` (`SMP10` = 110, 387.5 cycles); `SQR1` = `0x280` (channel 10); `SQR2–4` = 0; `CFGR` = `0x2003` (`DMNGT` = 11, `RES` = 000, 16-bit, `CONT`, `JQDIS` = 0) | the same, `PCSEL` = `0xE0000`, `SMPR2` = `0x36C00000` (`SMP17–19` = 110), `SQR1` = `0x4D2442` (three conversions: 17, 18, 19) | `ADSTART` = 0 (p934): kept. `HTR1` written 0 against its reset `0x03FFFFFF` with every watchdog off |
| 3.286 / 3.317 | `ADSTART` | `ADSTART` | `ADEN` = 1: kept |

- **ADC2 is never accessed.** No injected group, watchdog, oversampling, offset, differential channel or dual mode is used by either firmware (all written 0 or not written).
- **ADC1's stream** is `1303a`'s DMA1 stream 3. **ADC3's** goes to BDMA channel 7 through DMAMUX2 (unmapped in the platform): in the twin its request is wired to `dmamux1@115`, which no DMAMUX1 channel selects, so its `DR` is never read. The stock ADC3 then never finishes its first sequence ("Issued a start event before the last sequence finished", 13,995 times).
- **ADC3's channel 19 (`VREFINT`)** is past the stock model's 19 channels (0–18); the stock never reaches it, because its first sequence never ends.
- **On `main`, no record from any guard** in the armed run; the `[ERROR]` lines are the stock SPI's slave-mode line (38,406), as before.

### 2.2 What silicon does (RM0433 Rev 9 and DS12117 Rev 10; `rm/adc-fields.md`)

**One RM, two silicon revisions, and they differ where this guard computes** (each quote checked against the text by me where marked ✓):

| Item | rev Y | rev V | Page |
|---|---|---|---|
| The clock into the converter | `CKMODE` 00: `adc_ker_ck_input` (`ADCSEL`) / `PRESC`; 01/10/11: `adc_hclk` /1, /2, /4 | the same choices, with `adc_sclk` (sys_ck, or sys_ck/2 when `HPRE` ≠ 0) for 01/10/11; **"Whatever CKMODE[1:0] settings, an additional divider factor of 2 is applied"** ✓ | p920 ✓, p921, p1039 ✓ |
| `BOOST` | one bit (8): "Must be used when ADC clock > 20 MHz" | two bits (9:8): 00 ≤ 6.25, 01 ≤ 12.5, 10 ≤ 25, 11 ≤ 50 MHz; "must be set according to the ADC clock setting" | p1005, p1009, p922 |
| `LDORDY` | absent (the wait is timed by software) | `ISR` bit 12 | p917, p998 |
| `RES` | 001 14, 010 12, 011 10, 100 8 bits | 101/110/111 the optimised 14/12/8; 001/010 "standard mode"; 011 10; 100 not listed ✓ | p1016 ✓ |
| f_ADC max (DS12117) | 36 MHz (`BOOST` = 1), 20 MHz (0); Table 76 | 50 MHz (`BOOST` = 11); Table 173 | DS p166–167, p272–274 |

- **The conversion time:** `t_CONV` = `SMPx` (1.5, 2.5, 8.5, 16.5, 32.5, 64.5, 387.5, 810.5 cycles; p1019–1020) + SAR (16-bit 8.5, 14 7.5, 12 6.5, 10 5.5, 8 4.5; Table 214 p955). `PRESC` (1–256) only when `CKMODE` = 00 ✓ (p1039).
- **`ADCSEL`:** 00 `pll2_p_ck` (reset), 01 `pll3_r_ck`, 10 `per_ck`, 11 "the kernel clock is disabled" (p419); one selector for all three ADCs; enables `ADC12EN` (p455), `ADC3EN` (p458).
- **Calibration** takes `t_CAL` = 165,010 / f_ADC, and `t_ADCVREG_STUP` is 5 typ, 10 max µs (DS12117 Table 76 p167, Table 173 p273).
- **The "only when" rules** (p934, the bits' own rows p1003–1011, p1038–1039) ✓, and what breaking one does: **"There is no hardware protection to prevent these forbidden write accesses and ADC behavior may become in an unknown state"** ✓ (p934). The text never says a forbidden write is ignored.
- **`DMNGT`** (p1016): 00 `DR` only; 01 one-shot, which "stops generating DMA requests once the DMA has reached the last DMA transfer (when a transfer complete interrupt occurs …)" ✓ (p964), then freezes `DR`, aborts the conversion and resets the scan (p965); 10 DFSDM; 11 circular, "even if the DMA has reached the last DMA transfer" (p965).
- **Overrun:** `OVR` is set when "a new conversion has completed while the EOC flag was already set" (`ISR`, p999), but §25.4.27 describes an eight-stage `DR` FIFO for `OVRMOD` = 0 (p962): **the two definitions are not reconciled in the text.** On an overrun "the ADC stops generating DMA requests … The DMA transfer requests are blocked until the software clears the OVR bit" ✓ (p964).
- **Internal channels:** ADC3 17 = VBAT/4, 18 = VSENSE, 19 = VREFINT (Table 209 p920); ADC1 has none; ADC2 16, 17 = DAC outputs. `VREFEN`, `TSEN`, `VBATEN`: "only when the ADCs are disabled" (p1038).
- **The code:** `V = VREF+ / FULL_SCALE × DATA`, `FULL_SCALE` = 2^N − 1 (p996); the rounding is not stated.
- **Access width:** "Word (32-bit) accesses" (p926); sub-word behaviour not stated.

### 2.3 The stock model (by source, and as M1 saw it)

P2 holds where M1 reached it: the stock map is the generic STM32 one, not H7's (`CFGR` bit 1 `DMACFG`, a two-bit `RES` at 2–3, `ALIGN` at bit 5 where H7 has `EXTSEL[0]`; `SMPRx` tags; `CKMODE` stored with no effect; `CFGR2`'s top bits a `CKMODE` tag where H7 has `LSHIFT`; `VREFEN`/`TSEN`/`VBATEN` tags reading 0; `DEEPPWD` reserved and read 0; `LINCALRDYW1–6` read 1 always; `ADCAL` a tag; `LDORDY` = `ADVREGEN`; `ADRDY` at `ADEN` at once). Pacing is one sequence per tick of `externalEventFrequency` with `CONT` (1 kHz on the stock platform), each channel after the first converted only when `DR` is read.
- **Measured (P, the rate check):** an appended `adcM1S2:` update reaches the constructor, and the stock thread holds the asked rate exactly (3,788 DMA reads of `DR` per 0.1 s at 37,879 Hz).
- **Measured (F):** a **typed** redeclaration of `adc3` is refused (`Error E02: Variable 'adc3' was already declared`), because the stock entry comes from the `using` file. Cancel-and-restate works: `adc3: @none`, then `tviburiAdc3: … @ { sysbus 0x58026000; tviburiRcc RegistrationPeripheralAlias.ADC3 } as "adc3"` with the stock entry's four properties and interrupt line. Through a pass-through subclass so restated, **every `rcc`, `adc3` and `adcM1S2` access line of `d1-drv8876` (737) and of the armed run (1,004,402) is identical to M1**, as are the MAVLink stream (93 frames, byte for byte) and the boot block. Only the release hash and the `peripherals` kind (`STM32H7_ADC` → the subclass's) differ. `adc3 SetVoltage` still resolves (the subclass is an `IADC`).

### 2.4 The rate silicon would make, against the stock model's

From each firmware's own registers (§2.1) and §2.2:

| | Clock into the converter | f_ADC (rev V / rev Y) | One conversion | Silicon's rate (rev V / rev Y) | Stock |
|---|---|---|---|---|---|
| **ArduPilot ADC1** (one channel) | `CKMODE` 00, `ADCSEL` 01: `pll3_r_ck` = HSI 64 MHz / `DIVM3` 4 × `DIVN3` 15 / `DIVR3` 8 = **30 MHz** (`PLL3DIVR` `0x07040E0E`, `PLLCKSELR` `0x408040`, `PLL3ON`, `DIVR3EN`); `PRESC` /1 | **15 / 30 MHz** | 387.5 + 8.5 = 396 cycles: 26.4 / 13.2 µs | **37,879 / 75,758 conversions/s**, each a DMA request; `HTIF`/`TCIF` every 4 | 1,000/s |
| **ArduPilot ADC3** (17, 18, 19) | the same | 15 / 30 MHz | 396 cycles each, 79.2 / 39.6 µs a sequence | 37,879 / 75,758 conversions/s; requests reach no DMA in the twin | stuck after its first conversion |
| **`d1-drv8876` ADC3** (one, software-started) | `CKMODE` 11: rev V `adc_sclk` = sys_ck (`HPRE` /1) 64 MHz /4; rev Y `adc_hclk` 64 MHz /4 | **8 / 16 MHz** | 810.5 + 6.5 = 817 cycles: **102.1 / 51.1 µs** to `EOC` | one conversion | `EOC` at once |
| calibration (`t_CAL`) | | | ArduPilot 11.0 / 5.5 ms per ADC; `d1-drv8876` 20.6 / 10.3 ms | | at once |

- **The revision decides the rate by a factor of 2, and nothing declares it.** ChibiOS builds for rev V unless `STM32_ENFORCE_H7_REV_XY` (`hal_adc_lld.h`:389–449: `STM32_ADC12_CLOCK` = `STM32_ADCCLK / 2`); Aethelgard's generator chose `BOOST` 01 for its 8 MHz, the rev V range. Both firmwares therefore assume rev V; the platform says nothing.
- **ArduPilot's `BOOST` is wrong for rev V.** ChibiOS's chain tests `> 6250000` first (`hal_adc_lld.h`:461–469), so it always writes 01 above 6.25 MHz; at 15 MHz p1009 asks for 10. On rev Y the same bits mean `BOOST` = 1, which 30 MHz requires (p1005): right. What silicon does with a wrong `BOOST` is not stated.
- **Measured (P), the armed run at silicon's pace** (two pairs, back to back, Furnace load 12–15):

| | Wall (s) | Peak RSS (KiB) | Armed from | Frames | `SYS_STATUS.load` (first; then) | Instructions executed |
|---|---|---|---|---|---|---|
| `main` (1 kHz) | 52.56, 52.97 | 2.84–2.87 M | 9.774 s | 93, same ids | 414; 227–242 | 450,288,588 |
| 37,879 Hz (rev V) | 62.42, 61.27 (+17–19 %) | 3.30–3.34 M | 9.785 s | 93, same ids; a few frames reordered | 406; 226–242 | 489,110,724 (+8.6 %) |
| 75,758 Hz (rev Y) | 68.16, 72.94 (+30–38 %) | 3.58–3.80 M | 9.794 s | 93, same ids; a few frames reordered | 430; 242–255 | 531,463,333 (+18.0 %) |

  `main` against itself is byte-identical. **It still arms, with no record from any guard**; every difference is timing (`SYS_STATUS.load`, `SERVO_OUTPUT_RAW.time_usec`, `TIMESYNC`, the order of a few frames). The prototype paces only; the guard as proposed also takes `t_CAL` (about 11 ms per ADC, rev V) and `ADRDY`'s stabilisation, so the built guard moves these further.

### 2.5 Consumer observers each change would move, by board (O; Aethelgard `589030ca`)

| Observer | Reads | Boards | Moved by the pace and timings | by the samples | by the form |
|---|---|---|---|---|---|
| `peripherals` kind check (`renode.rs`:2846–2855; `foreign.rs`:1574–1582; `platform.rs`:89–90 `Analog.STM32H7_ADC`) | each solved ADC's (name, kind) | **`d1-drv8876`** only (an ADC is solved only for a pin device) | — | — | **yes: an untrusted-tool stop until the class map names the guard** (the consumer step every guard has taken) |
| ADC witness, kind 36 (`pinwitness.rs`:201–246) | `ADSTART` written before the first `DR` read; `DR` after the run = `adc_code(µV, 12)` (`record.rs`:58–63: round-half-up of µV × 4095 / 3.3 V) | `d1-drv8876` | no, while the conversion ends before the run's 100 ms: 102 µs, plus `t_CAL` 20.6 ms (rev V) | **yes**: any change of reference (3.3 V), rounding or scale | yes, if `SetVoltage(µV, channel)` stopped working |
| the stimulus `adc3 SetVoltage <µV> <ch>` (`pinwitness.rs`:35–45) | Renode's `IADC` method | `d1-drv8876` | — | it **is** the consumer's declaration | kept (measured, F) |
| `CR`/`DR` watchpoints (`anchor.rs`:495–499) | `CR` writes with the instruction count; `DR` reads | `d1-drv8876` | the instruction counts move (`ADCAL`, `ADRDY` and `EOC` now take time, so the polls run longer) | — | — |
| twin register image (`boot.rs`:1071–1140) | `PCSEL`, `SQR1` read back | `d1-drv8876` | — | — | yes, unless both read back as written (they would) |
| `renode_error_log` (`renode.rs`:2908; `foreign.rs`:1608–1616) | any `[ERROR]` line | all | a refusal record fails the run, as intended | | |
| MAVLink witness (`mavlink.rs`:548–587; closed table: no `SYS_STATUS`) | heartbeats, the first armed heartbeat, acks, texts | the armed run (a spike, not a gate) | **yes: the arm time (+11/+20 ms in P), frame order** | a `PreArm: Battery …` text, if a declared level were below a threshold (none set) | — |
| `SYS_STATUS`, `BATTERY_STATUS`, `MCU_STATUS` | not decoded or judged anywhere in Aethelgard | — | `load` | `voltage_battery` (0 today) | — |
| ledger rows (`ledger.rs`:1762–1860) | static text: `CR.ADCAL`, `CFGR.JQDIS`, `SMPR#` "ignored by model"; the rest "silicon only" | `d1-drv8876` | the text goes stale (`OQ-04`) | | |
| CI's foreign gate (`ci/foreign-judge.sh`:360–375) | Sentinel-02 does not arm | the fixture | no ADC access | | |

**The fixture and 13 generated boots: nothing moves.** Arming is not required by any gate.

### 2.6 The samples each firmware reads, and what a platform would declare

| Firmware | ADC, channel | Pin / source | Read by | Declared today | What a platform would declare |
|---|---|---|---|---|---|
| `d1-drv8876` | ADC3, 5 | `ADC3_INP5` (the bridge's current output) | the CPU, one `DR` read | **yes: `adc3 SetVoltage 0 5`** (0 µV), the consumer's scenario knob `analog_value` (0–3,300 mV) through Renode's `IADC` | already declared |
| ArduPilot, the armed run | ADC1, 10 | `PC0`, `ADC123_INP10`: `BATT_VOLTAGE_SENS`, R50 100 k / R51 10 k (÷11; `sentinel-03.json`:2083–2100; hwdef `HAL_BATT_VOLT_PIN 10`) | DMA1 stream 3, every conversion | **no** | the pack voltage ÷ 11 at the pin: Sentinel-03 declares 3–4 cells at 3.0–4.35 V (`:1787–1800`), **a range (0.82–1.58 V at the pin), not a level** |
| ArduPilot, the armed run | ADC3, 17, 18, 19 | VBAT/4, VSENSE, VREFINT (internal) | BDMA (unmapped): **never read in the twin** | no | a VBAT (backup) level, a die temperature, and VREFINT, none a consumer property |

**The consumer property that does not exist** (§7): a battery voltage, or a level for an analog net, that a foreign (ArduPilot) run can declare. Aethelgard's `analog_value` override applies only to a pin device's ADC input on generated boards; Sentinel-03's `power.battery` and `power.sense` feed only the hwdef. The twin's side exists: Renode's `SetVoltage(µV, channel)` on the ADC, which a guard subclass keeps.

## 3. Predictions, marked

| # | Prediction | Outcome |
|---|---|---|
| P1 | the clock, `PRESC`, the conversion time | **held**; the revision dependence was predicted but understated: it also moves `BOOST`, `LDORDY` and the `RES` codes |
| P1 | `ADCAL`/`ADEN` in the wrong state are ignored by silicon | **wrong**: the text says "unknown state" (p934), never "ignored" |
| P1 | one-shot ends at the DMA's last transfer; overrun stops requests | held; **not predicted:** the two unreconciled overrun definitions (p962, p999) |
| P2 | the stock map is generic STM32, not H7 | held (by source; M1 and P/F where reached) |
| P3 | 13 boots make no ADC access | held |
| P3 | `d1-drv8876`: no `ADCSEL`, and `CKMODE` either way | held: `CKMODE` = 11, so its start is clocked (no refusal) |
| P4 | the fixture makes no ADC access but writes `ADCSEL` = 01 | held |
| P5 | ArduPilot's start, `CKMODE` 00 with `ADCSEL` 01, `CFGR` `0x2003`, one ADC1 channel, ADC3 internal channels | held; `PRESC` is /1, not a divider; **not predicted:** ArduPilot writes ADC3's `CCR` with `ADEN` = 1 (p1038), and its `BOOST` is wrong for rev V |
| P5 | the order holds on silicon | **wrong** for ADC3's `CCR` (above) |
| P6 | 25–120 k conversions/s; the armed run's wall rises; timing fields move | held: 37.9 k (rev V) / 75.8 k (rev Y); wall +17–19 % / +30–38 %; it still arms |
| P7 | `SYS_STATUS` battery fields change by design; d1's judged value | held; **not predicted:** no Aethelgard code decodes `SYS_STATUS`, and the d1 witness judges `DR` against `adc_code` |
| P8 | no consumer property declares a level | **half wrong:** the consumer declares levels for generated boards (`SetVoltage`, `analog_value`); none exists for a foreign run |
| P9 | memory within ±12 MiB | held: +6.7 MiB with the probe (§4) |
| P10 | nine shards hold | **wrong by `ci`'s wall:** 19.6–20.4 min at this guard's likely size; a tenth is due (§4) |

## 4. The one-file memory and the shard projection

**Memory (B), `1201`'s budget:** `tests/1301/ci-memory.sh`, eleven boots a side, interleaved. This branch (`main` and this document) runs against `bench/`: the same tree plus the pass-through probe as both ADCs, cancelled and restated by the replay. Every branch boot carried the two probes ("PROBE-ADC" ×2), and no `main` boot did. ArduPilot's 5 s fixture (ELF `a799dfb6`), on Furnace at load 10–15, with walls of 11.7–12.2 s.

| | Peaks (KiB), in boot order | Median |
|---|---|---|
| `main` | 1,014,644; 1,004,644; 1,004,600; 1,013,124; 1,005,716; 1,013,720; 1,013,148; 1,006,364; 1,005,224; 1,003,048; 1,011,664 | 1,006,364 |
| `main` + the probe ×2 | 1,013,236; 1,013,656; 1,014,684; 1,015,256; 1,013,256; 1,006,072; 1,006,472; 1,014,104; 1,004,432; 1,013,568; 1,003,492 | 1,013,256 |

`MEDIANS main 1006364 KiB, branch 1013256 KiB, difference 6892 KiB (+6.7 MiB)`: **PASS.** Every branch boot is under the 1,572,864 KiB ceiling. **The fixture makes no ADC access,** so this figure measures the compiled code and the two instances, not the pace. The built guard (copies, rules, records, its own pacing) will cost more; the build reports the budget again, and CI judges it. The armed run's peaks under P are not the budget, but for the record: `main` 2.84–2.87 M KiB, 37,879 Hz 3.30–3.34 M, 75,758 Hz 3.58–3.80 M (single runs, no ADC logging).

**The shard projection (S),** `1202a`'s method: a shard job is 53 s of setup plus its load × 1.53; `ci`'s wall adds 1.7–2.0 min. `main` carries 5,475 s at the reference (361 cases, the mutants, unmeasured items at the medians), in nine shards. CI runs no armed run: the ArduPilot cases boot the 5 s fixture, which makes no ADC access, so silicon's pace (§2.4) costs CI nothing.

| Plan | Total at 1.00 (s) | Longest shard (s) | Shard job at 1.53 (min) | `ci` wall at 1.53 (min) |
|---|---|---|---|---|
| `main`, nine | 5,475 | 608 | 16.4 | 18.1–18.4 |
| + 402 s (the USART's), nine | 5,871 | 652 | 17.5 | 19.2–19.5 |
| + 542 s (the DMA's), nine | 6,016 | 669 | 17.9 | 19.6–19.9 |
| + 700 s, nine | 6,175 | 686 | 18.4 | **20.1–20.4** |
| + 700 s, **ten** | 6,175 | 618 | 16.6 | 18.3–18.6 |

- **This guard is larger than the last four:** the questions below imply about 16–20 cases (the enable sequence's rules and timings, the clock, the rate at two clocks, `DMNGT` ×3, overrun ×2, declared and undeclared samples, the revision, the acknowledged writes) and about 20–24 mutants: 600–750 s at the medians.
- **With nine shards, `ci`'s wall reaches 19.6–20.4 min at 542–700 s by the model,** and `1303a` measured real runs 0.3–0.6 min above the model. **A tenth shard is due by the WO's rule read as `ci`'s wall** (the reading `1306` and `1303a` used). The slowest shard job alone stays under 20 min (17.9–18.4).

## 5. The stop (WO §1, §7), and the questions

**Four §7 stops fire, plus §1's own.**
- **"The armed run would refuse … or an observer would change, on a path RM0433 says is valid."** Two of ArduPilot's accesses break a rule the text states. `ADC3_CCR`'s `VREFEN`, `TSEN` and `VBATEN` are written with `ADEN` = 1, against "only when the ADCs are disabled" (p1038). `BOOST` = 01 runs at 15 MHz on rev V, against "must be set according to the ADC clock setting" (p922, p1009). A guard that follows `RUL-17` refuses both, and the armed run with them (Q6, Q7). On the valid paths, silicon's pace moves the armed run's timing fields, its arm time (+11 ms) and its wall (+17–19 %) (§2.4). It also moves `d1-drv8876`'s instruction counts on its `CR` watchpoint (§2.5).
- **"Declaring the samples needs a consumer property that does not exist."** The armed run's battery pin (ADC1 channel 10) has no declared level. The consumer has no property for an analog net on a foreign run, and Sentinel-03 declares a cell range, not a level (§2.6). `d1-drv8876`'s level is declared, by Renode's `SetVoltage`.
- **A second consumer property is missing: the silicon revision.** It sets the rate by a factor of 2, and `BOOST`, `LDORDY` and the `RES` codes (§2.2). The WO says "if the rate cannot be computed from declared values, `unmodeled`", so with nothing declared every ADC start refuses (Q2).
- **A tenth shard is due** by `ci`'s wall (§4).
- **The fence:** the kernel-clock rule needs the RCC guard's query to know `ADCSEL`, and it has no ADC rule. Closing "the RCC guard's ADC row" edits RCC text (Q3).

**Q1. The form.** `STM32H7_ADC` is not sealed, but a typed redeclaration of `adc3` or `adcM1S2` is refused (E02, measured): the stock entries come from the `using` file. Its register map is not H7's (§2.3).
- **Proposal: `GuardedSTM32H7_ADC`, a subclass that re-implements the bus interfaces (`RUL-07`), cancelled and restated as `tviburiAdc3` / `tviburiAdc12` `as "adc3"` / `"adcM1S2"`.** The restatement is generated from the pinned stock file, with each entry's four properties, its interrupt line and its registration on `tviburiRcc`. The form is measured (F): every access line, the MAVLink and the boot block are identical, and `SetVoltage` still works.
- **The guard owns the regular conversion:** pacing, `EOC`/`EOS`/`EOSMP`/`OVR`, `DR`, the DMA request (through the stock entry's `IDMA`, `dmamux1` channel 9 or 115) and the interrupt. It serves `CR`, `CFGR`, `CFGR2`, `SMPR1/2`, `CCR` and `ISR` from its own copies, because the stock fields sit at the wrong bits.
- **The stock instance keeps** its channel sources (`SetVoltage`) and the registers it stores as H7 does (`PCSEL`, `SQRx`, the thresholds). The stock sampling thread is never started.
- **ADC2** (`adcM1S2` + 0x100) stays the stock mock behind ADC1. Any access to it refuses `unmodeled`; neither firmware makes one.
- **Prices for the consumer:** the `peripherals` kind for `d1-drv8876` (the class map); the restatement in its platforms; `RUL-09` (b)'s cancel-and-restate. `PCSEL` and `SQR1` read back as written.
- **Ask:** accept?

**Q2. The silicon revision** (a consumer property that does not exist).
- **Proposal:** a guard property `revision`, `"V"` or `"Y"`, with no default.
- With it undeclared, the first access whose behaviour depends on it refuses `unmodeled`, naming the property and p920/p1039, p1005/p1009, p1016. Those accesses are: `ADSTART` (the rate), a `RES` code that means different things on the two revisions, a `BOOST` write, and an `LDORDY` read.
- The consumer would declare it per board. Both firmwares assume rev V (§2.4). The replay stands in with `"V"`, as `ACCEPT_EXTERNAL` stood in for the GPIO levels.
- **Alternative:** default `"V"`, acknowledged by name. That puts a silent assumption in the rate, so it is not recommended.
- **Ask:** accept, or a default?

**Q3. The kernel clock** (the fence).
- **The smallest edit, recommended:** in the RCC guard, add the `ADCSEL` mux (`D3CCIPR` 17:16, p419: `pll2_p_ck`, `pll3_r_ck`, `per_ck`, disabled) and two query rows, `adc12` (`ADC12EN`, `AHB1ENR` bit 5, p455) and `adc3` (`ADC3EN`, `AHB4ENR` bit 24, p458). Then rewrite `ADC12EN`'s and `ADC3EN`'s ledger text, which says the clock is "an ADC guard's row".
  - **`ADSTART` with `CKMODE` = 00** asks the query. Not running is `predicted-fault`, naming the ADC, `ADCSEL`'s source and p920/p419.
  - **`ADSTART` with `CKMODE` ≠ 00** runs on the bus clock, which the bus lock already guarantees: no query.
  - **The ADC registers as `IRccWatched`.** `IsRunning` is `ADSTART` = 1 with `CKMODE` = 00, so `TVI-RCC-CLOCK` refuses `ADCSEL`'s source stopped mid-run (WO §5). It does not fire on a PLL stop under an ADC running on the bus clock.
- **The frequency:** the guard derives f_ADC itself from the RCC guard's registers, as the USART guard's `KernelHz` does. That needs `pll2_p_ck`, `pll3_r_ck` (new: ArduPilot's), `per_ck`, and sys_ck or hclk for `CKMODE` ≠ 00, with `PRESC` and the revision's /2. A source with no rule is `unmodeled`.
- **In-fence alternative:** the ADC guard reads `D3CCIPR` and the PLL bits itself, with no query rule. That duplicates the mux's "has not switched" logic (p371), and the RCC's ADC rows stay open. Not recommended.
- **Ask:** grant the RCC edit?

**Q4. Pacing at silicon's rate** (§2.4, measured).
- **Proposal:**
  - with `CONT`, one conversion every `t_CONV` (`SMPx` + SAR, by `RES`) of f_ADC, channel by channel through `SQRx`;
  - with `CONT` = 0, one sequence per `ADSTART`, `EOC` after `t_CONV`;
  - never a fixed default.
- **The price:**
  - the armed run's wall rises +17–19 % on rev V, with no record, and it still arms;
  - MAVLink keeps the same frames and changes in timing fields and a few frames' order;
  - the instruction count rises +8.6 %;
  - CI runs no armed run.
- **Ask:** accept, naming those differences (WO §7's "observer would change")?

**Q5. The enable sequence** (WO (2)).
- **Proposal:**
  - **`DEEPPWD`** at its reset value, 1 (p1003).
  - **`LDORDY` (rev V)** is set `t_ADCVREG_STUP` after `ADVREGEN`. The guard takes DS12117's maximum, 10 µs.
  - **`ADCAL`** stays 1 for `t_CAL` = 165,010 / f_ADC (DS12117).
  - **`ADRDY`** is set after `ADEN`, one conversion cycle later (DS12117's `t_STAB`).
  - **`LINCALRDYW1–6`** read 0 from reset and are set by a calibration with `ADCALLIN` = 1 (p931). A write that toggles one is `unmodeled` (no firmware does).
  - **Every "only when" rule** of p934 and the bits' own rows (`ADVREGEN`, `ADCAL`, `ADEN`, `ADSTART`, `ADSTP`, `ADDIS`, the `CFGR`/`SMPR`/`SQR` fields under `ADSTART`, the `CCR` fields under `ADEN`) refuses **`unmodeled`**, citing p934's "ADC behavior may become in an unknown state". It is not `predicted-fault`: the text gives no outcome, and `RUL-18` asks for one. Exceptions are Q6 and Q7.
  - The command bits are read as p1007's parenthesis lists them: `BOOST`, `ADCALLIN` and `LINCALRDYW` set do not block `ADEN`.
  - **Measured on the valid paths:** `d1-drv8876` keeps every rule (§2.1), but its polls run longer (20.6 ms of calibration). Its witness still holds, because the conversion ends at about 20.8 ms of a 100 ms run.
- **Ask:** accept, and `unmodeled` for the "only when" rules?

**Q6. ArduPilot's ADC3 `CCR` write under `ADEN` = 1** (`VREFEN`, `TSEN`, `VBATEN`; p1038 broken).
- **(i) `unmodeled`:** honest, and the armed run refuses: the WO's acceptance fails.
- **(ii) Acknowledged by name, recommended.** Those three bits, on ADC3, written with `ADEN` = 1 and `ADSTART` = 0, are taken and accumulate as silicon's register would.
  - The ledger row names p1038, p934 and ArduPilot's `AnalogIn` order.
  - Any other `CCR` field under `ADEN`, or these under `ADSTART`, stays `unmodeled`.
  - This needs a row for Aethelgard: an upstream ArduPilot/ChibiOS order defect, worth a silicon check.
- **(iii) `predicted-fault`:** p934 gives no outcome, so `RUL-18` cannot be met. Not recommended.
- **Ask:** (i), (ii) or (iii)?

**Q7. `BOOST` against f_ADC** (rev V: ArduPilot's 01 at 15 MHz, where p1009 asks for 10; ChibiOS's `#if` chain).
- **(i)** `unmodeled` at `ADSTART` when `BOOST` does not match f_ADC's range. This refuses the armed run on rev V, and only there: on rev Y the same bits are right.
- **(ii) Recommended:** a mismatch is acknowledged by name for `BOOST` below the range (the case measured), citing p922 and p1009 and the ChibiOS line. `BOOST` above the range is also acknowledged: p1009 calls it only power. Conversions run as if matched.
- **A row** for Aethelgard and upstream.
- **Ask:** (i) or (ii)?

**Q8. `DMNGT`** (p964–965, p1016).
- **11** is modeled.
- **00** is modeled: `DR` only.
- **01:** one-shot must stop at the DMA's last transfer. The ADC guard can see that only by reading, never writing, the serving stream's state: the DMAMUX1 channel that selects its request, then the DMA guard's `SxNDTR` (or `TCIF`). **Ask:** may the ADC guard read the DMA guard's registers through the bus interface (no edit), or is 01 `unmodeled` until a later WO? No firmware uses 01.
- **10:** the platform has no DFSDM model, so `ADSTART` with 10 is `unmodeled`.

**Q9. Overrun.**
- **Proposal:** `OVR` is set by `ISR`'s own definition, "a new conversion has completed while the EOC flag was already set" (p999).
  - `OVRMOD` = 0 keeps `DR`; `OVRMOD` = 1 overwrites it (p1015).
  - DMA requests are blocked until `OVR` is cleared (p964).
  - §25.4.27's eight-stage FIFO (p962) is acknowledged by name as not modeled: the twin's `DR` holds one value.
- **Where the two definitions diverge:** ADC3 in the armed run, whose requests nothing serves. It overruns at its second conversion, which is invisible to every observer: nothing reads ADC3.
- **Ask:** accept?

**Q10. The samples.**
- **Proposal:** a channel is declared in two ways:
  - by a guard property, `levels` (for example `"10:1.40"`, volts at the pin, as the WO says);
  - or by Renode's `SetVoltage(µV, channel)`, which is the consumer's own surface today (`d1-drv8876`).
- **`DR` read (by the CPU or a DMA) holding a conversion of an undeclared channel refuses `unmodeled`,** naming the ADC, the channel and the property. A conversion nobody reads makes no record (ADC3's internal channels in the armed run).
- **The code:** round(V / VREF+ × (2^N − 1)), VREF+ the entry's `referenceVoltage` (3.3 V, as Aethelgard's `adc_code` assumes). Rounding at exactly .5 is not stated in the text: ties go up, as the consumer computes.
- **A `DR` read before `EOC`** returns the previous conversion (reset 0), with no record. That is the WO §5 "level read before the sample time".
- **The armed run needs a declared ADC1 channel-10 level.** The replay stands in, with Sentinel-03 at 4 S × 3.85 V nominal ÷ 11 = 1.40 V. **Aethelgard has no property for it** (§7): a row.
- **To probe in the build:** that the guard can tell a `SetVoltage`'d channel from an untouched default source. If it cannot, `levels` is the only declaration, and the consumer moves its `SetVoltage` to it.
- **Ask:** accept both declarations, and the stand-in?

**Q11. The internal channels and ADC3's route.**
- **Internal channels:** `VREFEN`/`TSEN`/`VBATEN` taken (Q6). Conversions of channels 17–19 are paced, and their `DR` refuses `unmodeled` only if read (no declared VBAT, temperature or VREFINT). ArduPilot's never are.
- **ADC3's request:** raised on the stock entry's `dmamux1@115`, which nothing selects, as today.
  - The BDMA/DMAMUX2 route stays the WO's row, acknowledged by name in the ledger.
  - A DMA request raised that no stream serves is not refused by the ADC: it cannot know a future selection.
- **Ask:** accept?

**Q12. The allow-list** (`RUL-17`).
- **Modeled:** `CR` (`ADEN`, `ADDIS`, `ADSTART`, `ADSTP`, `ADVREGEN`, `DEEPPWD`, `ADCAL`, `ADCALLIN`, `LINCALRDYWx` unchanged, `BOOST` by revision and Q7); `CFGR` (`DMNGT` per Q8, `RES` by revision, `CONT`, `OVRMOD`, `EXTEN` = 0); `SMPR1/2`; `PCSEL`; `SQR1–4`; `DR`; `ISR`/`IER` regular flags; `CCR` `CKMODE`, `PRESC`, `VREFEN`/`TSEN`/`VBATEN` (Q6).
- **Inert, any value:** `JQDIS` (with `JADSTART` never set); `LTRx`/`HTRx` and `AWD2CR`/`AWD3CR` (while every watchdog enable is 0); `DIFSEL` = 0; `CFGR2` = 0.
- **0 only, else `unmodeled`:**
  - the injected group (`JSQR`, `JADSTART`, `JAUTO`, `JDISCEN`, `JQM`, `JAWD1EN`);
  - the watchdogs (`AWD1EN`, `AWD1SGL`, `AWD2CR`/`AWD3CR` ≠ 0 with an enable);
  - oversampling and `LSHIFT` (`CFGR2` ≠ 0);
  - `OFRx`, `DIFSEL` ≠ 0, `DISCEN`, `AUTDLY`, `EXTEN` ≠ 0 (hardware triggers: the timers' TRGO lines are not wired to the ADC);
  - `CCR` `DUAL`, `DELAY`, `DAMDF`;
  - `CALFACT`/`CALFACT2` writes;
  - every reserved bit, and a sub-word access (p926: word only).
- **Ask:** accept, or a different line for `EXTEN`?

**Q13. A tenth shard first** (§4), as `1306` built the eighth and `1303a` the ninth. **Ask:** ten, or nine with the wall at 20?
