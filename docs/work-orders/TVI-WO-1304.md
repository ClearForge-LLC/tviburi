# TVI-WO-1304 — The flash guard: wait states the clock can run on, bank 2 programmed as silicon programs it, and option bytes the twin does not invent

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1304-flash-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the fifth `13NN`. **Cadence:** a measurement, a stop, then the build. Written 2026-11-24.

**Grounds:**
- **Northstar:** invariants 1, 2, 5 and 8; §6.
- **Architecture:**
  - **`RUL-13` as amended:** after the RCC, GPIO routing, the timers and DMA come "the rest", ranked by whether ArduPilot takes the path. In `spike/1150/data/queue-ap.tsv`, `FLASH` is next: 24 rows, every one taken by ArduPilot.
    - `ACR.LATENCY` and `ACR.WRHIGHFREQ` (fields the stock model holds);
    - `CR2.PSIZE2`;
    - `SR2`'s `BSY2`, `QW2`, `CRC_BUSY2` and error flags;
    - `CCR1`/`CCR2`'s clears (tags).
  - `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18` and `RUL-19`.
- **The silicon fact behind (1)** (*from memory; cite RM0433 Rev 9 by page, or stop*): the flash read latency (`LATENCY`) and the programming delay (`WRHIGHFREQ`) must cover the AXI clock at the voltage scale in force. Software raises the latency before it raises the clock, and lowers the clock before it lowers the latency. Too few wait states fetch wrong instructions or data on silicon. The stock model serves every read at any latency: a false pass on the path that runs before any peripheral starts.
- **The consumer:**
  - Aethelgard's generated firmware reads `FLASH_OPTSR_CUR` once, for `BOR_LEV`. Any other flash access is already an Aethelgard finding (`flash_controller_access`, `aethelgard-chaos/src/campaign.rs`).
  - The twin answers that read with the stock model's reset value. Silicon answers with the chip's programmed option bytes.
  - The brown-out family (`BOR`) has waited for a consumer row (`RUL-13`); this is that row.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs.** For the generated firmware's 14 boots and ArduPilot's recorded boot (the fixture, and AP-m):
>   - every `FLASH` register written or read, in order, with the clock and the voltage scale in force at each write;
>   - what the stock `STM32H7_FlashController` does with each (held, tag, dropped, or programmed into the memory);
>   - whether ArduPilot programs or erases bank 2 inside the recorded window, and what the stock model does when it does;
>   - how the AXI clock and the voltage scale can be known at an `ACR` write and at a clock switch. The RCC guard's frequency state, and the consumer's `pwr` peripheral (a Python peripheral Aethelgard owns), are the candidates. Say which, and how.
>   - the one-file memory, as `1201`'s budget reports it.
> - **(2) The guard,** in the form `RUL-07` allows for the stock class, as `1302` and `1303` did. Its rules:
>   - **`predicted-fault`:** the AXI clock exceeds what `LATENCY` (and `WRHIGHFREQ`, for programming) allow at the voltage scale in force, at the write that creates the mismatch, whichever register that is, cited by page;
>   - **`predicted-fault`:** a bank 2 program or erase that silicon refuses or errors on (locked, wrong `PSIZE`, a write while `BSY`/`QW`), each cited;
>   - **`unmodeled`:** a mismatch the guard cannot decide, for example with the voltage scale unknown. Also every field outside the allow-list (`RUL-17`): bank 1 programming, ECC, CRC, the secure areas, and option-byte programming.
> - **(3) `OPTSR_CUR` served from a declared option-byte image.** The image is a restatement property, not the stock model's reset value. What an undeclared read does is a question for the stop: refusing it refuses every generated boot until the consumer declares.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the clock tree itself (the RCC guard's) or the voltage scale's own rules (Aethelgard's `pwr`);
> - option-byte programming (a row);
> - BOR's analog behaviour (a supply below the level is Aethelgard's droop campaign);
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the field table, and the fields to acknowledge;
   - the wait-state table, cited by page;
   - how the clock and the voltage scale are read at each rule;
   - each consumer observer the guard would change, by board;
   - (3)'s question;
   - the one-file memory, like for like.
2. **Build (2)–(3)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that raises the AXI clock before raising `LATENCY` refuses `predicted-fault` at the clock switch, naming the clock, the latency, the voltage scale and the page. The same firmware in the right order passes.
- **A planted firmware** that lowers `LATENCY` under a fast clock refuses at the `ACR` write.
- **ArduPilot's boot, recorded,** shows no flash `would-refuse` outside the acknowledged table.
- **The 14 generated boots:** each board's observers are unchanged, or each difference is cited and named. `OPTSR_CUR` reads the declared image.
- **The budget:** within `1201`'s 32 MiB, under the 1,572,864 KiB ceiling, reported like for like.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/`: the flash guard, its data, allow-list and restatement;
  - the RCC guard: **one query at a clock switch into the flash guard, and nothing else**. A wider change there is a stop;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row, naming its instances;
  - `tests/`, planted `firmware/`;
  - the guard-name lists (as granted to `1302` and `1303`, `scripts/tviburi-spike` included);
  - `tests/ci/citations.tsv`: your own rows, appended;
  - `docs/work-orders/TVI-WO-1304.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar (a new field is a stop);
  - the SPI, GPIO, timer and DMA guards;
  - the steering documents;
  - Aethelgard.
- **Running beside you:** `TVI-WO-1303` (the DMA guard, building). It also appends to the guard-name lists and `tests/mutants.tsv`: append only, and merge main before READY.
- One PR, left unmerged. Write `~/.cache/tvi-1304/pr-title-body.md`.

## 5. Adversarial pass, full

- **A mismatch that slips through:** the clock raised by a prescaler rather than a source switch (`D1CPRE`, `HPRE`), a PLL relock to a higher frequency, the voltage scale lowered under a fast clock, or `ACR` written through its bank 2 alias.
- **A program that silicon refuses and the twin takes:** a write without the unlock, a second unlock key out of order, a program to a sector being erased.
- **An image the firmware can forge:** an `OPTSR_PRG` write that changes what `OPTSR_CUR` reads without the option-byte start sequence.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **The wait-state table is not in the held RM0433 text.** Stop, and name the document that would show it.
- **Either firmware's boot would refuse, or an observer would change,** on a path RM0433 says is valid. Stop with the order and the page.
- **The clock or the voltage scale cannot be known at a rule** without editing the RCC guard beyond one query, or Aethelgard's `pwr`. Stop with the options.
- **The budget or the ceiling fails.** Stop with the medians.
