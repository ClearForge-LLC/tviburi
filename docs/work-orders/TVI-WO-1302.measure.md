# TVI-WO-1302 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1302-advanced-timer-guard`, from `main` `9e61b81`.
**State:** predictions (§0) committed before any run. Results follow in later sections.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/Timers/STM32_Timer.cs`, sha256 `7e99f48c…` (the file `1301a` read). No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock `STM32_Timer.cs` in full;
- `spike/1100/data/writes.tsv`'s `d1-drv8876` timer rows (Aethelgard `f42a0c88`): TIM1 `CCMR1` `0x6868`, `BDTR` `0x8000`, TIM2 `CCMR1` `0x68`;
- `spike/1150/data/writes-ap.tsv`: ArduPilot's 5 s boot writes only `timer5` among the timers;
- the committed ArduPilot fixture's `sentinel-02.hwdef.dat` (no `PWM(` line) and its platform (every timer `frequency: 200000000`);
- Aethelgard `main` `65c74788`'s `serial-dshot-9600.hwdef.dat` (TIM1 CH1–CH4 `PWM(1)`–`PWM(4)` on PA8–PA11);
- RM0433 Rev 9 Table 337 (p1608) and the `TIMx_BDTR` bit descriptions (pp. 1613–1615).

Nothing has been run in this work order, and no write sequence at Aethelgard `65c74788` has been read.

### P1. What silicon does (RM0433 Rev 9)
- **Table 337 (p1608),** TIM1/TIM8, a channel in output mode:
  - `MOE` = 1: `CCxE`/`CCxNE` = 00 with `OSSR` = 0 → both outputs disabled, "not driven by the timer: Hi-Z"; with `OSSR` = 1, off-state (driven inactive). 10 → `OCx` = `OCxREF` xor `CCxP`, `OCxN` Hi-Z (`OSSR` 0) or `CCxNP` (`OSSR` 1). 01 → mirror. 11 → complementary with dead time.
  - `MOE` = 0: `OSSI` = 0 → both Hi-Z. `OSSI` = 1 → "forced with their inactive level then forced to their idle level after the deadtime" (`OISx`/`OISxN`, `TIMx_CR2`; p1615 `OSSI`).
- **Hi-Z** means the GPIO logic takes the pin (p1614 `OSSR`: "taken over by the GPIO logic, which forces a Hi-Z state"): the pin's level is the pull or the board's external level, not a timer level.
- **`AOE` = 1** sets `MOE` at the next update event (p1614). **`LOCK`** "can be written only once after the reset. Once the `TIMx_BDTR` register has been written, their content is frozen until the next reset" (p1615); levels 1–3 freeze `DTG`/`OISx`/`BKE`/`BKP`/`AOE`, then `CCxP`/`CCxNP`/`OSSR`/`OSSI`, then `OCxM`/`OCxPE`.
- **TIM2–TIM5 have no `BDTR`, no `MOE` and no complementary output.** Their output is gated by `CCxE` alone (the standard-OCx table, about p1695; the page is confirmed in §1).

### P2. The stock model, by source (each probed in §1)
- **Form:** `public class STM32_Timer`, not sealed; `ReadDoubleWord`/`WriteDoubleWord` are not virtual. A subclass that re-implements `IDoubleWordPeripheral`, as the SPI guard does (`RUL-07`), takes every access. **I predict a subclass can also re-implement `INumberedGPIOOutput`, so that the platform's `timer1: 0 -> …` routes bind to outputs the guard owns,** and the stock channel's own `GPIO` becomes an internal signal the guard reads. Held loosely: it depends on how the pinned build resolves a `.repl` connection (the interface map or the class's property).
- **Tags** (writes discarded, reads the reset value): `BDTR` all of `DTG`, `LOCK`, `OSSI`, `OSSR`, `BKE`, `BKP`, `AOE`, `MOE`; `CCER.CCxNE`; `CR2.OISx`/`OISxN`, `CCPC`, `CCUS`, `CCDS`, `MMS`; `CCMRx.OCxFE`, `OCxPE`, `OCxCE`; `CR1.CKD`; every DMA enable in `DIER`; `EGR.CCxG`, `COMG`, `TG`. Bits 16–31 of `BDTR` (H7's `BKF`, `BK2F`, `BK2E`, `BK2P`, …) and of `CCER` (`CC5E`/`CC6E`), and the registers past `0x50` (`CCMR3`, `CCR5`, `CCR6`, `AF1`, `AF2`, `TISEL`), do not exist in the stock model.
- **`CCxE` gates the output partly.** `CCxE` = 0 unsets the connection (L695–704). **Two leaks:**
  - **L1:** with `CCxIE` = 1 and `CCxE` = 0, the channel timer still runs (`IsInterruptOrOutputEnabled`, L861), so PWM mode still sets the line at each update (L66–84) and clears it at the compare (L124–126): the pin pulses with its output disabled.
  - **L2:** an `OCxM` write of force-active (101) or set-inactive-on-match (010) sets the line whatever `CCxE` says (L723–737).
- **`MOE` is ignored** (1301a P14 measured it).
- **Polarity is never applied to an output.** `CCxP` and `CCxNP` are stored flags (L307–321), read only for input-capture edges (L855) and gated slave mode (L665). A firmware with `CCxP` = 1 sees the twin drive `OCxREF`, not its inverse.
- **One connection per channel.** `CHxN` exists only as the GPIO port's `invertedAFPins` inversion of that one line (`1301a` P2.2), so `CHxN` follows `NOT OCxREF` whenever its pin is in AF mode: never gated by `CCxNE`, never by `MOE`, and never polarised by `CCxNP`. **Applying `CCxNP` in the guard while the port also inverts would double an inversion:** the one place "applied twice" can happen.
- **The period off by one:** the counter's limit is `ARR` (L62, L367), so a period is `ARR` counts; silicon's is `ARR` + 1 (edge-aligned upcounting). A duty of `CCR`/`ARR` against silicon's `CCR`/(`ARR` + 1).
- **The clock:** the constructor's `frequency`, which the consumer's platform sets by an untyped `timerN: frequency:` update (`RUL-09` (b)'s trap: the replay must move it onto the guard's variable, or the guard runs at the stock file's value).
- **`EGR.UG`** zeroes the counter and reloads repetitions (L266–295), unless `UDIS`. **`CR1.CEN`** starts the counter only if `ARR` > 0 (L151).

### P3. The generated firmware at Aethelgard `65c74788`
- **Motor boards** are those whose specification has motor outputs: `motors-pwm`, `motors-dshot600`, `serial-dshot-9600`, `d1-drv8876` (an H-bridge on TIM1 and TIM2), and the Sentinel boards that carry motors. I predict 5 to 8 of the 14 boots touch TIM1, and none touches TIM8.
- **Fields written on TIM1,** each board: `PSC`, `ARR`, `CCMR1`/`CCMR2` (`OCxM` = 110 PWM mode 1, `OCxPE` = 1: `0x6868`), `CCER` (`CCxE` for each motor channel; `CCxP`, `CCxNE`, `CCxNP` 0), `CCRx`, `BDTR` = `0x8000` (`MOE` only: `LOCK`, `OSSR`, `OSSI`, `AOE`, `DTG` 0), `EGR.UG`, `CR1.CEN` (with `ARPE`). DShot boards also write `DIER` DMA enables and `DCR`/`DMAR` (DMA, outside this WO).
- **The order:** `CCMRx` → `CCER` → `CCRx` → `BDTR` (`MOE`) → `EGR.UG` → `CR1.CEN`. **`MOE` is set before `CEN` on every motor board, and no motor board leaves `MOE` 0.** Held with moderate confidence; a board that never sets `MOE` is Aethelgard's finding (WO).
- **`d1-drv8876`'s TIM2** writes `CCMR1` = `0x68`, `CCER.CC1E`, `ARR`, `PSC`, `CR1.CEN`; no `BDTR` write on TIM2.
- **No generated firmware writes `CCxP` = 1, `CCxNE`, `LOCK`, `AOE`, `OSSR`, `OSSI`, `BKE` or `DTG`.** No generated board routes a `CHxN` pin.

### P4. ArduPilot
- **On `sentinel-02-own`** (the committed fixture, ELF `a799dfb6`): its hwdef declares no `PWM(` output, so `RCOutput` has no group. **No window, however long, writes TIM1 or TIM8;** only TIM5 (`STM32_ST_USE_TIMER 5`). "Armed" changes nothing at the timers on this board.
- **An ArduPilot with motor outputs** can be built unmodified from Aethelgard's `serial-dshot-9600.hwdef.dat` in the pinned ArduPilot image (as CI builds `a799dfb6`). Booting it needs a specification with `mcu.foreign_clock`; a scratch copy of `serial-dshot-9600.json` with `sentinel-02-own`'s `foreign_clock` is a measurement input, not an Aethelgard change. I predict:
  - at `RCOutput::init`, ChibiOS's `pwm_lld_start` writes TIM1: `CR1` = 0, `PSC`, `ARR`, `CCMR1`/`CCMR2` PWM mode 1 with preload, `CCER` (`CCxE`, `CCxP` = 0), `BDTR` = `MOE`, `EGR.UG`, then `CR1` = `ARPE` | `CEN`; `MOE` before `CEN`;
  - disarmed, `CCRx` = 0 (no pulse) until armed;
  - it is reached inside a 10 s window; held loosely.
- **Arming** needs a MAVLink `COMMAND_LONG` (`MAV_CMD_COMPONENT_ARM_DISARM`, force code 21196) on `SERIAL0` (USART1), injected from the monitor. I predict it can be done in recorded mode, and that armed it writes only `CCRx` (pulse widths near `MOT_SPIN_ARM`) and no new `BDTR`/`CCER` field. Held loosely: arming may need more than prearm bypass (an RC failsafe or a motor-interlock state).

### P5. Consumer observers
- The generated boards observe motor pins with LED observers wired beside the timer route (`pa8obs` …, `1301a` §2.1). With `MOE` set before `CEN` on every motor board (P3), **gating on `MOE`/`CCxE` changes no generated observer line.** With no `CCxP` = 1 and no `CHxN` route, polarity and `CCxNE` change none either.
- ArduPilot's `sentinel-02-own` platform has no motor observer.

### P6. Period and clock (WO (3))
- **The clock:** after `4240e`, the platform's `timerN: frequency:` is the solved timer clock and the generated firmware's `PSC`/`ARR` are computed from the same solution, so the twin's PWM frequency equals the firmware's intent. A clock gap reaches no verdict unless the two disagree. I predict they agree on every board.
- **The period:** off by one count of `ARR` + 1. For any motor board, that is under 0.1 % of the period. I predict no Aethelgard verdict reads a motor pulse to that precision, so the period becomes a row, not a rule. To be checked against what the judge reads from the observers (Aethelgard's `twin` expectations).

### P7. Memory
- A timer guard on TIM1 and TIM8 (a subclass, its copies, a field table of about 120 rows) costs under 10 MiB at the median over `main` in `1201`'s budget.
