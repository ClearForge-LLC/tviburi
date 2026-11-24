# TVI-WO-1303 — The DMA guard: a stream moves data only where and when silicon would

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1303-dma-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the fourth `13NN`. **Cadence:** a measurement, a stop, then the build. **Measure now. Build after `TVI-WO-1202` merges**: it restructures `tests/spike.sh` and `tests/mutants.sh` into shards, so merge main first.

**Grounds:**
- **Northstar:** invariants 1, 2, 5 and 8; §6.
- **Architecture:**
  - **`RUL-04`:** the first peripherals for explicit refusal are the advanced-control timers, then DMA (memory-to-peripheral and circular).
  - **`RUL-13`** as amended.
  - **The consumer's recorded gaps** (architecture L52): "DMA enables as tags, `DMAR` undefined, and DShot frames unwitnessable".
  - Also `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18` and `RUL-19`.
- **`TVI-WO-1300a` (roadmap L58):** ArduPilot touches DMA1/DMA2 at 0.92 ms while their clock is locked. Silicon ignores those writes, and each guard decides what that means.
- **`TVI-WO-1302` FEEDBACK §7:**
  - the timer guard refuses `DMAR` and the DMA enables as `unmodeled`;
  - ArduPilot's USART1 receives by DMA (`CR3.DMAR` = 1);
  - the ledger cannot say "no value allowed".
- **The STM32H7 fact the guard must hold:** DMA1 and DMA2 cannot reach the TCM memories. Cite the RM0433 Rev 9 bus-matrix or memory-map page that says so, by page. If the held text does not say it, stop.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every DMA1, DMA2, DMAMUX1 (and BDMA, if written) register both firmwares write: the generated firmware, ArduPilot's recorded boot, and the motor/DShot boards;
>   - the order of stream configuration against `EN`;
>   - each memory address a stream is given, and the region it lies in;
>   - what the stock model does with each write;
>   - which consumer observers each write reaches.
>   - Also: whether ArduPilot's USART RX and the DShot boards' timer-update DMA run in the recorded window; whether `TIMx_DMAR` or `DIER.UDE` ever fires a stream in the stock model; and the one-file memory, as `1201`'s budget reports it.
> - **(2) The guard** over DMA1/DMA2 (and DMAMUX1 if the measurement needs it), in the form `RUL-07` allows for the stock class, as `1302` did. Its rules:
>   - **`predicted-fault`:**
>     - a stream enabled with a memory address DMA1/DMA2 cannot reach (DTCM, ITCM), cited;
>     - a configuration field written while its stream's `EN` = 1, where RM0433 says the write is ignored or forbidden, cited by page (the field takes silicon's behaviour);
>     - a request routed through DMAMUX to a peripheral that does not exist, cited.
>   - **The clock:** an access while the DMA clock is off already refuses through `1300a`'s lock. Do not duplicate it; show the case is covered.
>   - **`unmodeled`:** every other field outside the allow-list (`RUL-17`), double-buffer mode and FIFO thresholds included, unless a consumer needs them.
> - **(3) The timer's DMA side:** with the DMA guard in place, decide from the measurement whether `TIMx_DIER.UDE`, `CCxDE` and `DCR`/`DMAR` can move from `1302`'s `unmodeled` to modeled.
>   - If they can, build it.
>   - If the stock timer never raises the request, keep them refused and make it a row: DShot stays unwitnessable until a model raises it.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - BOR or the voltage detector (the next family);
> - MDMA;
> - the I2C or SPI guards' own DMA requests beyond routing;
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the field table, and the fields to acknowledge;
   - each address region, cited;
   - each consumer observer the guard would change, by board;
   - the one-file memory, as `1201`'s budget reports it, like for like (`1302`'s GC after load included).
2. **Build (2)–(3)** to the ruling, after `1202` merges. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that points a DMA1 stream at DTCM refuses `predicted-fault` at enable, naming the stream, the address, the region and the page. The same firmware with an AXI-SRAM address passes.
- **ArduPilot's boot, recorded,** shows no DMA `would-refuse` outside the acknowledged table.
- **The 14 generated boots:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, under the 1,572,864 KiB ceiling, reported like for like.
- **CI is green.**

## 3. Fences

- **Fence:**
  - `model/`: the DMA guard, its data and allow-list, and the restatement;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row, naming its instances (`1302` FEEDBACK §7);
  - `tests/`, planted `firmware/`;
  - `docs/work-orders/TVI-WO-1303.*`;
  - for (3), `model/GuardedSTM32_Timer.cs`'s DMA rows only.
- **Not:** `model/Refusal.cs` and the record grammar (a new field is a stop); the RCC, SPI and GPIO guards; the steering documents; Aethelgard.
- **Running beside you:** `TVI-WO-1202` (CI sharding, in `tests/spike.sh`, `tests/mutants.sh` and `.github/workflows/`). Do not edit those until it merges.
- One PR, left unmerged. Write `~/.cache/tvi-1303/pr-title-body.md`.

## 5. Adversarial pass, full

- **A transfer that still reaches TCM:** a double-buffer `M1AR`, an address written after `EN`, or a peripheral-to-memory stream whose memory side is TCM.
- **A configuration change that slips past `EN`:** a sub-word write, `EN` and a field in one write, or `EN` cleared and set in one write.
- **A request routed twice,** or a DMAMUX channel left on a stream that a later write reuses.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **Either firmware's boot would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **The TCM restriction is not in the held RM0433 text.** Stop, and name the document that would show it.
- **The budget or the ceiling fails.** Stop with the medians.
