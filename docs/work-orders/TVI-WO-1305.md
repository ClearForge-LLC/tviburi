# TVI-WO-1305 — The USART guard: a serial port receives, idles and takes its configuration only as silicon does, so the drone's command link is judged

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1305-usart-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the sixth `13NN`. Written 2026-11-24. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **Northstar §3.1** (ratified 2026-11-24): the first application is an agent-flown drone, commanded over MAVLink on a serial port. The paths an armed, commanded autopilot takes are modeled first (`RUL-13`).
- **`RUL-13`'s queue** (`spike/1150/data/queue-ap.tsv`): `USART` has 136 rows, 54 of them taken by ArduPilot in its 5 s window. They cover `CR1`'s `FIFOEN`, `M0`/`M1`, `OVER8`, `PCE`/`PS`, `IDLEIE` and FIFO interrupts; `CR2`'s `STOP`, `SWAP`, `RXINV`/`TXINV`, `DATAINV`, `MSBFIRST`, `RTOEN`; `CR3.EIE`; and `ICR`'s clears.
- **`TVI-WO-1303`** (merged, #37): request 41 (USART1 receive) is acknowledged by name, because no model raises it. A receive over DMA moves nothing in the twin. That row's own text says it belongs "to a USART guard, and to `OQ-03`'s arming path".
- **Aethelgard `AET-WO-4400h`** (merged as the record): ArduPilot armed over MAVLink only after two local twin changes:
  - `usart1`'s `ReceiveDmaRequest -> dmamux1@41`. The stock model has the request line; the platform did not wire it;
  - frames padded to 64 bytes, because **`IDLE` is a tag**. ChibiOS's receive path ends a DMA frame on `IDLE`.
- **Aethelgard `AET-WO-4400i`** (written beside this one) wires the receive request on the consumer's platform, and keeps or replaces the padding on the **stock** twin. The two projects stand alone (northstar §3.1): Aethelgard names its stock-twin gap, and Tviburi models the silicon behaviour for the guarded twin. Neither depends on the other's build.
- **The roadmap's row:** "I2C, USART and RNG starts ask for their kernel clock when their own guards exist" (`1300` (4)).
- `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every USART/UART register both firmwares write, in order, against `UE`, `TE` and `RE`: the 14 generated boots, ArduPilot's recorded boot, and **`AET-WO-4400h`'s armed Sentinel-03 run** (its queue in Aethelgard's `spike/4400h/out/queue/`);
>   - what the stock `STM32F7_USART` does with each: `IDLE`, the FIFO and its thresholds, `RTOF`, the receive request when `DMAR` = 1, and fields written while `UE` = 1;
>   - which consumer observers each write reaches (Aethelgard's serial capture and witness, by board);
>   - the one-file memory, as `1201`'s budget reports it.
> - **(2) The guard,** in the form `RUL-07` allows, as `1302`–`1304` did. Its rules, each cited to RM0433 Rev 9 by page (*from memory until cited: the held text decides*):
>   - **`predicted-fault`:** a field RM0433 says can be written only with `UE` = 0, changed while `UE` = 1;
>   - **`predicted-fault`:** a start (`UE`, then `TE` or `RE`) with no running kernel clock, by the RCC guard's existing kernel-clock query (as `1300a` wired SPI1's). Show that the case is covered, not duplicated;
>   - **modeled, cited:** `IDLE` raised after a received frame and an idle line, as silicon raises it;
>   - **modeled, cited:** the receive DMA request raised on received data with `DMAR` = 1, so `TVI-WO-1303`'s acknowledgement of request 41 can be withdrawn where the platform wires it;
>   - **`unmodeled`:** every field outside the allow-list (`RUL-17`), with LIN, smartcard, IrDA, synchronous mode and auto-baud included unless a consumer needs them.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the consumer's platform wiring or its stock-twin padding (`AET-WO-4400i`);
> - baud accuracy against the kernel clock (Aethelgard judges it, `AET-WO-2311`);
> - the I2C guard, or `LPUART1`'s low-power modes;
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the field table, and the fields to acknowledge;
   - each protected field and the `IDLE` and request behaviour, cited by page;
   - each consumer observer the guard would change, by board;
   - the one-file memory, like for like.
2. **Build (2)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that changes `M` or `OVER8` with `UE` = 1 refuses `predicted-fault`, naming the port, the field and the page. The same change made with `UE` = 0 passes.
- **ArduPilot's receive path** on the guarded twin ends a frame on `IDLE`, with no padding. A DMA receive moves the bytes where the platform wires the request.
- **ArduPilot's boot, recorded, and the armed run** show no USART `would-refuse` outside the acknowledged table.
- **The 14 generated boots:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, under the 1,572,864 KiB ceiling, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/`: the USART guard, its data, allow-list and restatement;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row, naming its instances;
  - `tests/`, planted `firmware/`;
  - the guard-name lists (`scripts/tviburi-spike` included);
  - `tests/ci/citations.tsv`: your own rows, appended;
  - `docs/work-orders/TVI-WO-1305.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar (a new field is a stop);
  - the RCC guard beyond calling its existing query;
  - the SPI, GPIO, timer, DMA and flash guards;
  - the steering documents;
  - Aethelgard.
- **Running beside you:** `TVI-WO-1304` (the flash guard). It also appends to the guard-name lists and `tests/mutants.tsv`: append only, and merge main before READY.
- One PR, left unmerged. Write `~/.cache/tvi-1305/pr-title-body.md`.

## 5. Adversarial pass, full

- **A frame that ends without `IDLE`, or `IDLE` without a frame:** a break, a framing error, a line held low, or `RE` cleared mid-frame.
- **A request that moves data silicon would not:** `DMAR` set with `RE` = 0, or a request raised by a byte the overrun dropped.
- **A configuration change that slips past `UE`:** a sub-word write, `UE` and a field in one write, or `UE` cleared and set in one write.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **Either firmware's boot, or the armed run, would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **A behaviour this WO models is not in the held RM0433 text.** Stop, and name the document that would show it.
- **The budget or the ceiling fails in CI.** Stop with the medians.
