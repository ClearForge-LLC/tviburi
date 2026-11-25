# TVI-WO-1307 — The ADC guard: a converter that starts, paces and stops as silicon does, so the armed run's analog stream is judged, not inherited

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1307-adc-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the eighth `13NN`. Written 2026-11-25. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **`TVI-WO-1303a`** (#51): the armed Sentinel-03 run's circular ADC stream (DMA1 stream 3, request 9) is now modeled on the DMA side. Its source is the **unguarded stock ADC1**, which paces conversions at a fixed 1 kHz (`externalEventFrequency`) and samples 0 mV. `1303a.measure.md` §5 Q1 lists what an ADC guard needs, each from the stock source (`STM32_ADC_Common.cs` `0cc82d40…`) and RM0433 Rev 9:
  - **the conversion clock:** `CKMODE` (`ADCx_CCR`) and `ADCSEL` (RCC `D3CCIPR`, p419), neither of which the stock model has;
  - **the conversion time:** `SMPRx`, `RES` and the clock, which set the rate of requests silicon makes;
  - **`DMNGT`'s four values** (p1016). The stock model has only `DMAEN`/`DMACFG`, so one-shot raises requests like circular, and 10 (DFSDM) raises none;
  - **the overrun:** `OVR` and `OVRMOD` (p964);
  - **the enable sequence:** `DEEPPWD` (reserved in the stock model and reads 0; silicon resets it to 1), `ADVREGEN`/`LDORDY`, `ADCAL` (a tag, so no calibration runs), and `LINCALRDYW1–6` (always 1);
  - **the sample sources:** 0 mV with nothing attached, and the internal channels (`VREFEN`, `TSEN`, `VBATEN`) are tags;
  - **ADC3's request:** wired to `dmamux1@115` on the stock platform, while ArduPilot routes it through DMAMUX2 to BDMA, which the platform lacks;
  - **ADC2** is a mock behind ADC1.
- **The roadmap rows:** "ADC's kernel clock depends on `ADCx_CCR.CKMODE`" (p920; Aethelgard `4240e`); `1300b`'s ADC enables, modeled as bus enables only.
- **`RUL-13`'s queue:** the `ADC3` rows (`CFGR.JQDIS`, `CR.ADCAL`, `SMPR1.SMP0–9`) from Aethelgard `AET-WO-1211c`'s measurement.
- `RUL-07`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every ADC register both firmwares write, in order, against the enable sequence: the 14 generated boots (`d1-drv8876` polls ADC3 by the CPU), ArduPilot's recorded boot, and the armed run;
>   - what the stock model does with each, and what each read returns;
>   - the conversion rate silicon would make from the firmware's own `CKMODE`, `ADCSEL`, `SMPRx` and `RES` (cited), against the stock 1 kHz;
>   - which consumer observers each change would move, by board, including the armed run's MAVLink (`SYS_STATUS`'s battery fields read the ADC);
>   - the one-file memory, and each CI shard's projected time at 1.53.
> - **(2) The guard,** in the form `RUL-07` allows, as `1302`–`1306` did. Its rules, each cited by page (*from memory until cited*):
>   - **the enable sequence:** `DEEPPWD` at its silicon reset value; `ADVREGEN` before `ADEN`, and `ADCAL` before `ADEN`, refused or modeled as the held text says;
>   - **the kernel clock at the start:** `ADSTART` with no running clock (`CKMODE` ≠ 00 from the bus clock, or `ADCSEL` through the RCC guard's query) is `predicted-fault`, and the RCC guard's ADC row is closed;
>   - **pacing:** requests raised at the rate the firmware's own clock and sample time give, never a fixed default. If the rate cannot be computed from declared values, `unmodeled`;
>   - **`DMNGT`:** one-shot stops after the DMA's last transfer, circular continues, and DFSDM raises nothing, each cited (p964, p1016);
>   - **the overrun:** `OVR` set as silicon sets it, with `OVRMOD`;
>   - **the samples:** the guard takes each channel's level from a declared platform property, as the GPIO guard takes external levels (`1301a`). An undeclared channel is `unmodeled` when read, never 0 mV by default;
>   - **`unmodeled`:** the injected group, the analog watchdogs, oversampling, the internal channels and ADC2 as a slave, unless §1 finds a firmware using them.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the BDMA or DMAMUX2 (ADC3's route on ArduPilot): a row;
> - the analog front end (dividers, filters): the consumer declares the level at the pin;
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the field table and the sequence, by firmware;
   - the rate silicon would make, against the stock model's;
   - each consumer observer that would change, by board;
   - the samples each firmware reads, and what level a platform would have to declare;
   - the memory and the shard projection.
2. **Build (2)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that sets `ADSTART` with no kernel clock refuses `predicted-fault`, naming the ADC, the clock and the page.
- **A planted firmware** on a declared 1.2 V channel reads the code `RES` gives for 1.2 V, at the rate the clock and `SMPRx` give.
- **The armed run** shows no ADC record outside the acknowledged table, still arms, and its battery fields read the declared level.
- **The 14 generated boots and ArduPilot's fixture:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/`: the ADC guard, its data, allow-list and restatement;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row, naming its instances;
  - `tests/`, planted `firmware/`;
  - the guard-name lists;
  - `tests/ci/citations.tsv`, `tests/mutants.tsv` and `tests/ci/times.tsv`: your own rows;
  - `docs/work-orders/TVI-WO-1307.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar (a new field is a stop);
  - the RCC guard beyond calling its query and registering on it (`IRccWatched`);
  - the DMA guard (`1303a`'s), the GPIO guard, and the other guards;
  - the steering documents;
  - Aethelgard.
- **If a tenth shard is due,** the stop says so, as `1303a`'s did.
- One PR, left unmerged. Write `~/.cache/tvi-1307/pr-title-body.md`.

## 5. Adversarial pass, full

- **A conversion that runs on a clock silicon would not give:** `CKMODE` switched under `ADEN`, or `ADCSEL`'s source stopped mid-run.
- **A sample the twin invents:** an undeclared channel read as 0, or a level read before the sample time has passed.
- **A stream that never ends:** one-shot `DMNGT` that keeps raising requests after the DMA's last transfer.

## 7. Flag-and-stop conditions

- **After §1,** with the measurement.
- **Either firmware's boot or the armed run would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **Declaring the samples needs a consumer property that does not exist.** Stop, and name the property.
- **The budget fails in CI, or the shard projection passes 20 minutes.**
