# TVI-WO-1303a — The DMA guard's circular mode: a stream that wraps as silicon does, so the armed Sentinel-03 run's last record is modeled or ruled

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1303a-dma-circular`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, a `13NN` follow-up. Written 2026-11-25. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **The armed Sentinel-03 run's last record.** After `1300b` (#47) and `1306` (#49), the armed run (Aethelgard `4400h`/`4400i`) records only `dma1`'s `TVI-DMA-FIELD` `CIRC` = 1, twice, on an ADC stream (`1305` FEEDBACK L138; `1300b` FEEDBACK §3). Until it is modeled or ruled, an armed run is not a pass on main's guards.
- **`TVI-WO-1303`** (#37) §1.1: the stock DMA models no circular mode. `CIRC` is a tag, `NDT` never reloads, `EN` is never cleared at the end of a transfer, and no `HTIF`/`TCIF`/`TEIF` is raised (measure.md L23, L30, D8). `EN`, `NDT` and the end of a transfer are pp. 662–665.
- **`RUL-04`:** the first peripherals for explicit refusal include DMA "memory-to-peripheral and circular".
- **The ADC's side is not modeled** (roadmap: an ADC guard reading `CKMODE` is a row). Whether the stock ADC raises its DMA request at all decides what a circular ADC stream does in the twin.
- `RUL-07`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every DMA stream configured with `CIRC` = 1 by either firmware (the 14 generated boots, ArduPilot's recorded boot, and the armed run): the stream, its request (DMAMUX id), direction, `NDT`, the half/full interrupts, and what the firmware reads back (`NDTR`, `LISR`/`HISR`);
>   - whether the stock ADC (and any other source of those requests) raises the request on the twin, and how often;
>   - what the firmware does with the stream afterwards: does it wait on `HTIF`/`TCIF`, read `NDTR`, or only read the buffer;
>   - the one-file memory, and each CI shard's projected time at 1.53.
> - **(2) The guard's circular mode,** in the DMA guard's existing form (`1303`), each rule cited to RM0433 Rev 9 by page (*from memory until cited*):
>   - **modeled, cited:** with `CIRC` = 1, at the end of a transfer, `NDT` reloads, `EN` stays 1, and `TCIF` is set (and `HTIF` at half), as silicon does (p665 and the flag pages);
>   - **modeled, cited:** `CIRC` is protected under `EN` = 1, as `1303` ruled for the other fields;
>   - **for a request the twin never raises** (the ADC's, if §1 shows it): ruled from the measurement. Either the stream is acknowledged by name as configured-and-idle, with the request's absence named as the ADC guard's row, or it is `unmodeled`. Never a silent pass that claims data moved;
>   - **`unmodeled`:** double-buffer mode (`DBM`, `CT`, `M1AR`) and circular memory-to-memory (RM0433 forbids it; cite the page) unless §1 finds a firmware using them.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the ADC guard (`CKMODE`, the conversion, its request), unless §1 shows it is needed to rule the stream. Then stop;
> - DShot's timer DMA (Aethelgard's row, after `4400j`);
> - DMAMUX1's own guard (a row);
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the table of circular streams by firmware, and what each firmware reads;
   - whether each stream's request is ever raised in the twin;
   - the pages for each behaviour;
   - each consumer observer the change would move, by board;
   - the memory and the shard projection. If a ninth shard is due, the stop says so.
2. **Build (2)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that runs a circular stream sees `NDT` reload, `EN` stay 1 and `TCIF` set at each wrap, with the page. The same stream with `CIRC` = 0 ends with `EN` cleared.
- **`CIRC` changed with `EN` = 1** refuses, naming the stream, the field and the page.
- **The armed run** shows no DMA record outside the acknowledged table, and still arms (`COMMAND_ACK` 400 at about 9.0 s).
- **The 14 generated boots and ArduPilot's fixture:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32DMA.cs`, its data and allow-list;
  - `tests/1303/`, planted `firmware/`;
  - `tests/ci/citations.tsv`, `tests/mutants.tsv` and `tests/ci/times.tsv`: your own rows;
  - `docs/work-orders/TVI-WO-1303a.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar (a new field is a stop);
  - the RCC, GPIO, timer, flash, USART and I2C guards;
  - the steering documents;
  - Aethelgard.
- One PR, left unmerged. Write `~/.cache/tvi-1303a/pr-title-body.md`.

## 5. Adversarial pass, full

- **A wrap that moves data silicon would not:** a request after `EN` is cleared, or a wrap with `NDT` = 0.
- **A flag that lies:** `TCIF` set on a non-circular stream that silicon would end, or `HTIF` at the wrong count for odd `NDT`.
- **A circular stream that passes by never running:** an acknowledged idle stream whose firmware then waits on a flag the twin never sets.

## 7. Flag-and-stop conditions

- **After §1,** with the measurement.
- **Ruling the ADC stream needs the ADC's own model.** Stop, and name what an ADC guard would need.
- **Either firmware's boot or the armed run would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **The budget fails in CI, or the shard projection passes 20 minutes.**
