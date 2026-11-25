# TVI-WO-1306 — The I2C guard: a sensor bus takes its timing and filters only as silicon does, starts only on a running clock, and reads back what it was given

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1306-i2c-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the seventh `13NN`. Written 2026-11-25. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **`RUL-13`'s queue** (`spike/1150/data/queue-ap.tsv`): `I2C` is the next family with rows ArduPilot takes. It writes I2C1 `TIMINGR` (`SCLDEL`, `SCLH`, `SCLL`; 544 writes), `CR1` (`PE`, `ERRIE`), `CR2` (`STOP`, `AUTOEND`, `RELOAD`, `ADD10`) and `ICR`'s clears. `1100`'s queue lists the same fields from the generated boards' ledgers.
- **The stock `STM32F7_I2C` does not store `TIMINGR`** (Aethelgard `AET-WO-4300b` FEEDBACK §10, measured): every field is a tag that reads 0. So does most of `CR1`'s filter and mode set (`DNF`, `ANFOFF`, `TXDMAEN`, `SBC`, `WUPEN`, `GCEN`, `SMBHEN`, `SMBDEN`, `ALERTEN`, `PECEN`). A firmware that reads back its own bus timing gets 0 in the twin.
- **`1300a` §1.1 (FEEDBACK):** ArduPilot reads `CR1`, then writes 0 to it, before `I2C1EN`, in program order (the enable is at pc `0x08100B02`). Silicon ignores both, because the peripheral has no clock. The roadmap's row leaves the enable rule to this guard.
- **The roadmap's row:** "I2C, USART and RNG starts ask for their kernel clock when their own guards exist" (`1300` (4)). The RCC guard's query already answers `i2c1` and `i2c2` (`I2C123SEL`, p416; `I2C1EN`/`I2C2EN`, p463).
- **The consumer:** Aethelgard judges each I2C line's rise time against the specification's declared bus speed (its `RUL-19`). A twin that stores `TIMINGR` would let a later work order check the firmware's written timing against that speed. That check is not this WO's.
- `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every I2C register both firmwares write, in order, against `PE` and the port's `I2CxEN`: the 14 generated boots, ArduPilot's recorded boot, and the armed Sentinel-03 run that `1305` and `1300b` used;
>   - what the stock model does with each write: stored, tag or callback, and what a read returns;
>   - the bus transfers each boot makes (address, direction, length, `NACK`), and which consumer observers they reach, by board;
>   - the one-file memory, as `1201`'s budget reports it, and **each CI shard's projected time** at the slowest runner (1.53, `1202a`).
> - **(2) The guard,** in the form `RUL-07` allows, as `1302`–`1305` did. Its rules, each cited to RM0433 Rev 9 by page (*from memory until cited: the held text decides*):
>   - **`predicted-fault`:** a field RM0433 says may be written only with `PE` = 0, written while `PE` = 1. From memory, that is `TIMINGR` as a whole, and `CR1`'s `ANFOFF` and `DNF`;
>   - **`predicted-fault`:** a start (`PE` written 1) with no running kernel clock, by the RCC guard's existing query, as `1300a` wired SPI1's and `1305` wired the USART's. Show that the case is covered, not duplicated;
>   - **modeled, cited:** `TIMINGR` and the `CR1` fields the guard allows are stored, and read back as written (`RUL-17`'s read rule, stated);
>   - **modeled, cited:** clearing `PE` resets the state RM0433 names for a software reset, and keeps what it says is kept;
>   - **the access before `I2CxEN`:** decide from §1 and RM0433 whether it is acknowledged by name for ArduPilot's measured pattern only (`1305a`'s precedent), or modeled as silicon ignores it. Every other access to an unclocked port stays locked, as `1300a` made it;
>   - **`unmodeled`:** every field outside the allow-list (`RUL-17`), with SMBus, PEC, wake-up from Stop, slave mode and 10-bit addressing included unless a consumer needs them.
> - **Each refusal comes with a planted firmware, and each test with a mutant it kills.**
> - **Registration:** a guarded I2C port not registered on the RCC guard refuses every access `unmodeled`, as SPI's `Holds` does and `1300b` adds for the USART.
>
> It is NOT:
> - the SCL frequency against the declared bus speed (a later consumer work order);
> - the I2C pins or their pull-ups (the GPIO guard, `1301a`);
> - the DMA side of I2C (`TXDMAEN`/`RXDMAEN` stay `unmodeled` unless §1 finds a firmware sets them);
> - `I2C4` (D3 domain) unless a committed board uses it;
> - any change to the RCC guard beyond calling its query, or to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the field table, and the fields to acknowledge;
   - each protected field, the software-reset behaviour and the pre-enable access, cited by page;
   - each consumer observer the guard would change, by board;
   - the one-file memory, like for like;
   - the shard projection. If the slowest shard would pass 20 minutes at 1.53 with this guard's cases and mutants, the stop says so, and the eighth shard (`1202a` F2) comes first, by the ruling.
2. **Build (2)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that writes `TIMINGR` with `PE` = 1 refuses `predicted-fault`, naming the port, the field and the page. The same write with `PE` = 0 passes, and reads back as written.
- **A planted firmware** that sets `PE` with `I2C123SEL` on a stopped source refuses at the start, by the RCC guard's query.
- **ArduPilot's boot, recorded, and the armed run** show no I2C `would-refuse` outside the acknowledged table, and their sensor transfers are unchanged.
- **The 14 generated boots:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, under the 1,572,864 KiB ceiling, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/`: the I2C guard, its data, allow-list and restatement;
  - `scripts/prelude.resc`: one include;
  - `tools/tviburi-release`: the class row, naming its instances;
  - `tests/`, planted `firmware/`;
  - the guard-name lists (`scripts/tviburi-spike` included);
  - `tests/ci/citations.tsv`: your own rows, appended;
  - the CI shard plan and `times.tsv`, **only if the ruling adds the eighth shard**;
  - `docs/work-orders/TVI-WO-1306.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar (a new field is a stop);
  - the RCC guard beyond calling its existing query and registering on it;
  - the SPI, GPIO, timer, DMA, flash and USART guards;
  - the steering documents;
  - Aethelgard.
- **Running beside you:** `TVI-WO-1300b` (the RCC guard's open rows). It changes the RCC guard, including registration. Do not edit it: build against main, and merge main before READY. If `1300b`'s registration API is not on main when you build, stop and ask rather than add a second one. Both append to the guard-name lists and `tests/mutants.tsv`: append only.
- One PR, left unmerged. Write `~/.cache/tvi-1306/pr-title-body.md`.

## 5. Adversarial pass, full

- **A timing change that slips past `PE`:** a sub-word write, `PE` cleared and a field written in one write, or `PE` cleared and set again without the reset RM0433 requires between them.
- **A transfer the bus would not make:** `START` with `PE` = 0, `NBYTES` = 0 without `RELOAD`, or a transfer started while the previous one has not ended.
- **A read that lies:** a field the guard stores read back differently from silicon (a reserved bit, a write-only field), or a flag the software reset should clear that survives it.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement.
- **Either firmware's boot, or the armed run, would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **A behaviour this WO models is not in the held RM0433 text.** Stop, and name the document that would show it.
- **The budget or the ceiling fails in CI, or the shard projection passes 20 minutes.** Stop with the medians.
