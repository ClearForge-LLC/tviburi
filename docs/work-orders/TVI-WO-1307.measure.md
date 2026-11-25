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
