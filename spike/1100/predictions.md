# TVI-WO-1100 — the ranking rule and the predictions, committed before any run

Written 2026-11-23, before the CLI was built and before any ledger was collected or any firmware run (WO §1.1).

**What I had read before predicting** (so the predictions are not blind):
- architecture §2.0's list of recorded Renode gaps;
- a grep of `crates/aethelgard-cli/src/ledger.rs` at Aethelgard `f42a0c88`, which shows many SPI rows whose class is judged and whose note says "also ignored-by-model";
- the two board specifications, which declare `motor_outputs: []`.

## 1. Inputs, fixed

- **Aethelgard:** `main` at `f42a0c88`, cloned to `~/tvi-1100-aet` and mounted read-only in every container.
- **The committed boards** (`boards/`, as `workspace_shape.rs` L724 and `aethelgard-svd-hal/tests/board.rs` require):

  | Board | Generated from |
  |---|---|
  | `boards/sentinel-01` | `examples/sentinel-01-minimal.json` (`board.rs` L2, L40) |
  | `boards/sentinel-01-bench` | `examples/sentinel-01.json` (`board.rs` L129–133) |

- **The image:** Aethelgard main's `container/IMAGE`, `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…`. Its Renode is `1.17.0+20260907gitf1dd1b4af.aet1605`.

## 2. The ranking rule

**Unit.** One row per (peripheral class, register, field), the WO's join key (§1.3). Rows from the two boards with the same key merge, and the row counts its boards.

**Class, by function, not by peripheral family.** A row takes the first class that matches it:
1. **Motor output.** A timer that a `motor_outputs` entry drives, the DMA stream or DMAMUX request that feeds it (DShot), the motor pins' GPIO fields, and the RCC enable or reset bits of those peripherals.
2. **Clocks.** Every other RCC field: the kernel-clock selectors (`D1CCIPR`, `D2CCIP1R`, `D2CCIP2R`, `D3CCIPR`), oscillators, PLLs, the bus prescalers, and peripheral enables and resets. Also `FLASH_ACR`'s wait states, which only matter because of the clock.
3. **Power.** PWR (supply configuration, `VOS`, PVD), the BOR level, and the flash option bytes that carry it.
4. **Sensor buses.** SPI and I2C instances, the GPIO fields of their pins, and their kernel-clock dependence where it is not an RCC field.
5. **The rest.** USART, SYSCFG and the compensation cell, other GPIO, everything else.

**Order within a class:**
1. more boards first (2 before 1);
2. a row seen in both views before one seen in one view;
3. a `written` row before a `reset` row;
4. `ignored-by-model` before `same-source` before a judged row that carries an "also ignored-by-model" note;
5. the row id, byte-wise.

**Reasons a row can be one-sided, closed before the join.** Any other reason found during the join is itself a finding:
- **R-reset:** a ledger `@reset` row. The firmware writes nothing there, so no write exists to log.
- **R-window:** the path is not executed in the boot window that `simulate` runs.
- **R-never-issued:** a write silicon would see that the CPU never issues, for example a DMA-driven write.
- **R-judge:** an access by the judge (a monitor `sysbus` command), not the firmware. These are excluded from view (2).
- **R-judged-class:** the ledger classes the field as judged and names no drop, while the simulator shows one. That is a consumer-side finding (WO §5).
- **R-not-in-ledger:** the firmware writes a field the ledger has no row for.
- **R-log-only:** an unhandled-write or tag warning, or an access to an unmapped address, with no ledger row to match. Used as a cross-check only.

## 3. Predictions

1. **The motor class is empty for the committed boards.** Neither specification declares a motor output, so no timer and no DShot DMA appears in either view. The class the rule puts first will have no measured rows. The cut will have to say what that means for `OQ-03`.
2. **The top of the queue is clocks:** `RCC_D2CCIP1R` (`SPI123SEL` and the I2C/USART selectors at reset), the PLL and oscillator tags, and the `FLASH_ACR` notes. Then **power:** the BOR level (`silicon-only` in the ledger, so outside the two classes the WO asks for, but named) and PWR supply configuration.
3. **Sensor buses are the largest class:** SPI1's CFG1/CFG2 tags (`MBR`, `SSM`, `SSI`, `AFCNTR`, `MASTER` behaviour) and I2C's unstored `TIMINGR`. Most SPI rows are judged rows with an "also ignored-by-model" note, not rows classed `ignored-by-model`. They are therefore invisible to a class-only filter, which is the WO's first adversarial case.
4. **Row counts, per board:** about 150–250 ledger rows in all. About 60–120 of them classed `same-source` or `ignored-by-model`, and about 40–80 judged rows that carry an "also ignored-by-model" note. Merged across the two boards: about 80–150 queue rows, most of them on both boards.
5. **View (2):** most dropped writes are tag writes on RCC, SPI and GPIO (`OSPEEDR`, `PUPDR`), plus a few unimplemented-register writes, for example the compensation cell and `SYSCFG`. I expect the tag warnings to fire only on non-zero writes, so a zero written to a tag will be in the ledger and missing from the warnings, but present in the raw access log.
6. **The cut:** the clock rows that decide whether a sensor bus has a kernel clock (`D2CCIP1R` and the enables), and the BOR/PWR rows. Not the sensor-bus tags yet, unless view (2) shows the bus works only because a dropped field was ignored.
