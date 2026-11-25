# TVI-WO-1306a — The I2C guard's NACK: an absent address is NACKed as silicon does, not hung and acknowledged by name

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1306a-i2c-nack`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, a `13NN` follow-up. Written 2026-11-25. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **`TVI-WO-1306`** (#49), Q9 (c): the stock `STM32F7_I2C` raises no `NACKF`. A `START` to an address with no device hangs the transfer. Silicon NACKs it: `NACKF` set, and with `AUTOEND` a `STOP` (RM0433 Rev 9 p1975 and the `ISR`/`ICR` pages).
  - `1306` acknowledged the measured absent addresses by name, per port (`TVI-I2C-ABSENT` for any other), and ended an acknowledged `START`'s data phase as silicon's NACK would.
  - Modelling `NACKF` was left as a roadmap row.
- **The consumer seam:** Aethelgard's faulted-bus scenarios drop a sensor from the platform. On the guarded twin they now meet `TVI-I2C-ABSENT`, where the stock twin gave `i2c_timeout`. A modeled NACK is what silicon gives, and what those scenarios mean to test.
- `RUL-07`, `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - every absent-address `START` both firmwares make (the 14 generated boots, ArduPilot's recorded boot and the armed run): the port, the address, the direction, `AUTOEND`/`RELOAD`, and what the firmware does next (reads `ISR`, clears `NACKF` by `ICR`, retries, gives up);
>   - what silicon does, cited by page: `NACKF`'s timing against the address phase, `STOPF` with `AUTOEND`, `TXIS`/`TXE` and `TXDR`'s flush on a NACK, and `NACKIE`'s interrupt;
>   - each consumer observer a modeled NACK would move, by board, including Aethelgard's `absent_address` and its faulted-bus scenarios;
>   - the one-file memory and each CI shard's projected time at 1.53.
> - **(2) The model,** in `1306`'s guard. Each rule is cited (*from memory until cited*):
>   - **modeled:** a `START` to an address with no device on that port's bus sets `NACKF` at the end of the address phase. With `AUTOEND` = 1 a `STOP` follows and `STOPF` is set; with `AUTOEND` = 0 the bus waits. `NACKIE` raises the event interrupt;
>   - **`TVI-I2C-ABSENT`** (the acknowledgement by name) is withdrawn once the NACK is modeled. Name every board and firmware whose records change;
>   - **"Unknown slave at address N."** is still written under the guard's name, so Aethelgard's `absent_address` stays readable. If the measurement shows the line should change, stop and say why;
>   - **a NACK in the data phase** (a device that NACKs a byte) is `unmodeled` unless §1 finds a firmware relying on it.
> - **Each refusal and each modeled behaviour comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - the consumer's scenario changes (an Aethelgard row);
> - SMBus, PEC, slave mode or 10-bit addressing (still `unmodeled`);
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the table of absent-address starts and what each firmware does next;
   - the pages;
   - each observer that would change, by board;
   - the memory and the shard projection.
2. **Build (2)** to the ruling. Merge main before READY.

## 2. Acceptance

- **A planted firmware** that starts a write to an absent address with `AUTOEND` = 1 reads `NACKF` and `STOPF` set, at the page's timing, and its `ICR` clears them.
- **ArduPilot's boot and the armed run** probe their absent sensors with no I2C record, and the armed run still arms.
- **The 14 generated boots and the fixture:** each board's observers are unchanged, or each difference is cited and named.
- **The budget:** within `1201`'s 32 MiB, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard, and the aggregate shows it.

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32F7_I2C.cs`, its data and allow-list;
  - `tests/1306/`, planted `firmware/`;
  - `tests/ci/citations.tsv`, `tests/mutants.tsv` and `tests/ci/times.tsv`: your own rows;
  - `docs/work-orders/TVI-WO-1306a.*`.
- **Not:**
  - `model/Refusal.cs` and the record grammar;
  - every other guard;
  - the steering documents;
  - Aethelgard.
- **Mutants:** run `tests/mutants.sh` for your rows exactly as CI's shard does before you push (`1307`'s lesson).
- One PR, left unmerged. Write `~/.cache/tvi-1306a/pr-title-body.md`.

## 5. Adversarial pass, full

- **A NACK the bus would not give:** an address a declared device answers, NACKed by a stale table.
- **A NACK that hangs anyway:** `AUTOEND` = 0 with no `STOP` from the firmware, against the guard's own end of transfer.
- **A flag that lies:** `NACKF` set before the address phase ends, or surviving an `ICR` clear.

## 7. Flag-and-stop conditions

- **After §1,** with the measurement.
- **Either firmware's boot or the armed run would refuse,** or an observer would change, on a path RM0433 says is valid. Stop with the order and the page.
- **The budget fails in CI, or the shard projection passes 20 minutes.**
