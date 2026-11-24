# TVI-WO-1302 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1302-advanced-timer-guard`, from `main` `9e61b81`.
**State:** §1.1 measured, then **stopped** (WO §1.1), with the questions in §5. Predictions committed at `1dba93d` before any run.
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

## 1. How it was measured (after §0 was committed at `1dba93d`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `65c74788` pins (`container/IMAGE`, fetched this session). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1302/` (`cap-gen/`, `m1/`, `probe/`, `ap-dshot/`, `budget/`, `rm/`, `pdf/`, and the scripts in `h/`), kept until the architect says.

| Run | What | How |
|---|---|---|
| **C** | the 14 generated boots, re-captured at Aethelgard `main` `65c74788` | 1301's `capture-gen.sh` (generate, build, `simulate --for 100ms`, the `/proc` watcher), CLI built in the image from a `git archive` export. All 14 verdicts `pass`. The on-disk captures (`75da8053`, `f42a0c88`) predate `4240e` and `4300j`, so none was reused |
| **M1** | each boot replayed on `main`'s release (every guard recorded), with `sysbus LogPeripheralAccess` on TIM1–TIM5 and TIM8 | `tests/1301/replay-1301.sh`'s `accept` mode, copied (`h/replay-m.sh`) with three changes: the timers logged; `collapse-repeated-log-entries` off; **the argv taken after `--`**, because Aethelgard `65c74788` launches Renode under its own reaper (`--aethelgard-internal-reaper … -- /usr/local/bin/renode …`), which the committed harness does not strip (§5 row) |
| **AP** | ArduPilot: the committed fixture (`aet-1c32c8ea-ardupilot`, ELF `a799dfb6`) at 5 s and 20 s; and **an unmodified ArduPilot with motor outputs**, built by Aethelgard's `container/ardupilot-build.sh` (sha256 `77cf09bd…`, the fixture's) in the pinned ArduPilot image from Aethelgard `65c74788`'s `serial-dshot-9600.hwdef.dat` (TIM1 CH1–CH4 `PWM(1)`–`PWM(4)` on PA8–PA11), ELF `2e686339…` | booted by `aethelgard simulate --foreign 5000ms` on a **scratch copy** of `serial-dshot-9600.json` given `sentinel-02-own.json`'s `mcu.foreign_clock` (a measurement input; nothing in Aethelgard changed), captured, then replayed as M1 at 5 s and 20 s with the four motor observers' log level lowered (the CLI lowers it only for generated firmware) |
| **M2** | the stock timer's behaviour, and the form a guard can take | a **probe** (`probe/ProbeSTM32_Timer.cs`, scratch only): a subclass of `STM32_Timer` that re-implements `IDoubleWordPeripheral` (logs, then the base) and `INumberedGPIOOutput` (its own four outputs, fed from the stock lines through a tap, passed through or held low by a `Gate` property). No rule. Cancelled and restated as `timer1` on Tviburi's Nucleo board (every guard recorded), monitor-driven, with LED observers on PA8 (CH1) and PA7 (CH1N); stamps virtual (Aethelgard's `renode.config`) |
| **B** | the one-file memory, by `1201`'s budget | `tests/1301/ci-memory.sh` from a scratch checkout whose replay instantiates the probe as `timer1`, `main`'s tree against `main` + the probe (§4) |

## 2. Results

### 2.1 The field table (M1, AP)

Every write to a timer in the 14 generated boots and both ArduPilots. **TIM8, TIM3 and TIM4 are never written.** TIM5 is ArduPilot's system tick only (`STM32_ST_USE_TIMER 5`). "Boards": `d1` = `d1-drv8876`, `djn` = `driver-judges-nothing`, `hse` = `hse-crystal-16` and `hse-external-8`, `dshot` = `motors-dshot600`, `sentinel-02-shaped`, `sentinel-03`, `serial-dshot-9600`, `pwm` = `motors-pwm`, `AP-m` = the ArduPilot with motor outputs. The five Sentinel-01/02-bench boots write no timer.

| Register (TIM1, RM0433 Rev 9 p) | Values written (boards) | Fields given a non-reset value | The stock model (`STM32_Timer.cs`) |
|---|---|---|---|
| `CR1` (p1588) | `0x81` (all 9 generated motor boards); `0x80` later (`dshot`, `hse`: the DShot window's stop at about 2 ms); `0x85` (AP-m) | `CEN`, `ARPE`; `URS` (AP-m) | `CEN` modeled (L144–152); `ARPE` modeled (L164, L365); `URS` stored, gates the UG interrupt (L283) |
| `CR2` (p1589) | `0x0` (AP-m) | none | `OISx`/`OISxN`, `CCPC`, `CCUS`, `CCDS`, `MMS` tags |
| `DIER` (p1594) | `0x0` (AP-m) | none | stored |
| `SR` (p1596) | `0x0` (AP-m) | none (rc_w0 clear) | modeled |
| `EGR` (p1598) | `0x1` (all 10) | `UG` | modeled (L266–295) |
| `CCMR1`/`CCMR2` (p1599, p1603) | `0x6868` (all 10; `CCMR2` `0x0` on the 2-channel `d1`, `djn`, `hse`) | `OCxM` = 0110 (PWM mode 1), `OCxPE` = 1 | `OCxM` modeled; **`OCxPE` a tag** (no preload: a `CCR` write takes effect at once, and the register reads back `0x6060`) |
| `CCMR3` (`0x54`, p1617) | `0x1016868` (AP-m only) | `OC5M`, `OC6M` (with bit 3), `OC5PE`, `OC6PE` | **no register:** "Unhandled write to offset 0x54", dropped. OC5/OC6 reach no pin (`CC5E`/`CC6E` stay 0) |
| `CCER` (p1605) | `0x1111` (4-channel boards, AP-m); `0x11` (2-channel) | `CCxE` | `CCxE` modeled, with leaks (§2.2); `CCxP`/`CCxNP` stored, **never applied to an output**; `CCxNE` a tag |
| `PSC` (p1609), `ARR` (p1609), `CCRx` (p1610) | per board (e.g. `pwm` 63 / 2499 / 1000; DShot 0 / 106 / 40; `d1` 0 / 3199 / 0; AP-m 199 / 19999 / never written) | — | modeled; **`CCRx` resets to `0xFFFF`** (the constructor's `initialLimit`), silicon `0x0000` (p1610); the period is `ARR` ticks (§2.2) |
| `BDTR` (p1612) | `0x8000` (all 10) | `MOE` only (`LOCK`, `OSSR`, `OSSI`, `AOE`, `BKE`, `DTG`, H7's `BK2*`/`BKF` all 0) | every field a tag (L377–386); bits 16–31 reserved |

**TIM2** (general-purpose, Table 342 p1695: no `MOE`, no complementary output) is driven by four boards (`d1`: CH1, a servo at 50 Hz; `djn`, `hse`: CH1–CH2): `CCMR1` `0x68`/`0x6868`, `CCER` `0x1`/`0x11`, `PSC`, `ARR`, `CCRx`, `EGR.UG`, `CR1` `0x81` (then `0x80` on `hse`). **ArduPilot's TIM5:** `PSC` 199, `ARR` `0xFFFFFFFF`, `CCMR1` 0 (frozen), `DIER` `CC1IE`, `CR2` 0, `UG`, `CEN`, then 43,814 `CCR1` and 43,193 `SR` writes in 5 s. `CCER` is never written.

**The order, on every timer and every board:** `PSC`, `ARR`, `CCRx`, `CCMR1`, `CCMR2`, `CCER`, **`BDTR` (`MOE`)**, `EGR.UG`, `CR1.CEN` (generated, from one routine at one PC per board; Aethelgard `svd-hal/src/board.rs` L4457–4478). AP-m: `CCMR1`, `CCMR2`, `CCMR3`, `PSC`, `ARR`, `CR2`, `CCER`, `UG`, `SR`, `DIER`, **`BDTR` (`MOE`)**, `CR1.CEN`. **`MOE` is set before `CEN` everywhere, and no motor board leaves it 0.**

### 2.2 The stock model, by probe (M2)

| # | Probe | Stock (measured) | Silicon (RM0433 Rev 9) |
|---|---|---|---|
| S0 | reset reads | `CCR1` `0xFFFF`, `CCER` 0, `BDTR` 0 | `CCR1` 0 (p1610) |
| S1 | `MOE` = 0, `CC1E` = 1, `CCR1` > `ARR` | PA8 high | Hi-Z (Table 337 p1608, `MOE` 0 / `OSSI` 0) |
| S2/T2 | leak L1: `CC1E` = 0, `CC1IE` = 1, PWM 1 | **14 PA8 edges in 3 µs** | Hi-Z (Table 337: `CCxE` = `CCxNE` = 0) |
| S3 | leak L2: `CC1E` = 0, `OC1M` = 101 (force active) | PA8 high | Hi-Z |
| S4 | `CC1P` = 1, `MOE` = 1, `CCR1` > `ARR` | PA8 high (not inverted) | low: `OCx` = `OCxREF` xor `CCxP` (p1608) |
| S5 | `CCR1` written 0 while the output is high | **PA8 stays high** (`CCR` = 0 disables the compare timer, and the update leaves the line alone, L398–402, L66–73) | low: "If the compare value is 0 then OCxRef is held at '0'" (p1558) |
| S5 | PA7 (AF1, TIM1_CH1N), `CC1NE` = 0 | PA7 = NOT CH1, driven (the port's `invertedAFPins`) | Hi-Z (`OSSR` 0) |
| S9 | `BDTR` = `0x8300` (`LOCK` 3), then `0x80FF` | reads 0 (tags) | `LOCK` frozen at the first write (p1615); `DTG` locked at level ≥ 1 |
| T1 | `PSC` 0, `ARR` 99, `CCR1` 50 at 250 MHz; `CEN` at 1.000 µs | **first rise 1.400 µs** (one period after `CEN`); rises every 0.396 µs (`ARR` ticks); high 0.200 µs | `OCxREF` high from `CEN` (`CNT` 0 < `CCR`, p1558); period `ARR` + 1 = 100 ticks (Figure 379, `ARR` = 8: counts 0…8) |
| S8 | `WriteByte 0x40010020 0x10`; `WriteWord 0x40010022` | each arrives as a read-modify-write of the double word, **through the subclass's re-implemented interface** | — |

### 2.3 The form (M2): a subclass works; the restatement must rename `timer1` in the GPIO restatement

- **`STM32_Timer` is not sealed.** The probe subclass loads, cancelled (`timer1: @none`) and restated `as "timer1"` at `0x40010000` with `tviburiRcc RegistrationPeripheralAlias.TIM1`: `peripherals` lists `timer1 (ProbeSTM32_Timer)`.
- **Every access reaches it** (byte, half-word, word, monitor and the access log), and `sysbus LogPeripheralAccess sysbus.timer1` still logs.
- **The platform's routes bind to the subclass's own outputs** (re-implemented `INumberedGPIOOutput.Connections`, plus a `new` public `Connections`): CH1's 7 routes are on the probe's output, and the stock line's only endpoint is the probe's tap. `Gate false` took PA8 low with the stock line high; PA7 (CH1N) went high, the port inverting the gated line.
- **Variant A** (the timer restated after the committed `stm32h7-gpio-restate.repl`, which keeps its `timer1:` routes and `-> timer1@NN` inputs on the cancelled variable): it loads and drives, but **the GPIO guard's AF entry breaks** (T3: PA8 output low, TIM1 high, then AF1: PA8 stays low; `1301a`'s `gpio-afenterhigh` rule fails). The routes land through phantom combiners, which the GPIO guard's source scan does not follow (the `1301` memory of `CombinedInput`).
- **Variant B** (the GPIO restatement's `timer1:` block and `timer1@` inputs renamed to the guard's variable, and the RCC restatement's `timer1: @ {…}` block dropped, the registration going on the guard's declaration): T3 gives PA8 high at the switch, as `1301a` rules.
- **The form against firmware (control):** four generated boots (`motors-pwm`, `motors-dshot600`, `hse-crystal-16`, `d1-drv8876`) replayed with the probe as `timer1` (variant B, passing through), against the same boots on `main`'s tree, same harness: the observer lines are identical with their stamps (304, 9,652, 9,648 and 8 lines), the `AETHELGARD-CR1` hook line is identical (it anchors every motor witness), the boot block is identical (instruction count included), and every observer's final `State` is equal (`d1`'s TIM1 observers `pa8obs`/`pa9obs` read `False`: measured, not only reasoned).
- **The frequency trap (`RUL-09` (b)), measured for the timer:** an untyped `timer1:` / `frequency: 125000000` after the restatement left the probe at its declared 250 MHz (same period). The consumer emits `timerN: frequency:` for all 14 timers (§2.5), so every replay and the consumer must move it onto the guard's variable.

### 2.4 Consumer observers, by board, against silicon (M1, AP)

Each LED observer edge was mapped to its pin and the timer channel the platform routes there, and silicon's output state was evaluated from the `CCER`/`BDTR`/`CR2` writes before it, by Table 337 (p1608; read from the rendered PDF page, since the text layer interleaves its rows) for TIM1 and Table 342 (p1695) for TIM2 (`h/m1-analyse.py`).

| Board | Observers (edges in 100 ms) | Edges where silicon's state differs under the gate |
|---|---|---|
| `motors-pwm` | `pa8obs`–`pa11obs`, TIM1 CH1–4 (76 each) | 0 |
| `motors-dshot600`, `sentinel-02-shaped`, `sentinel-03`, `serial-dshot-9600` | `pa8obs`–`pa11obs`, TIM1 CH1–4 (2,412–2,413 each) | 0 |
| `hse-crystal-16`, `hse-external-8` | `pa8obs`, `pa9obs` (TIM1 CH1–2), `pa0obs`, `pa1obs` (TIM2 CH1–2), 2,412 each | 0 |
| `driver-judges-nothing` | TIM1 CH1–2 (4,080 each), TIM2 CH1–2 (8 each) | 0 |
| `d1-drv8876` | TIM2 CH1 (`pa0obs`, 8); TIM1 CH1–2 (`pa8obs`/`pa9obs`, `DRV_EN`/`DRV_PH`, `CCR` 0: no edge, final `State` `False`, measured in §2.3's control) | 0 (and the low is silicon's: `CCR` 0 holds `OCxREF` 0, p1558) |
| 5 Sentinel-01/02-bench boots | none | — |
| ArduPilot, `sentinel-02-own` (5 s, 20 s) | none (the hwdef has no `PWM(`) | — |
| **AP-m (scratch)** | `pa8obs`–`pa11obs`: **each rises at 120.4 ms and stays high for the 20 s** | **all four, from 120.4 ms on:** in this boot, which never leaves its INS-error state (§2.6), ArduPilot writes no `CCRx`; the stock `CCRx` reads `0xFFFF` > `ARR`, so PWM mode 1 holds 100 % duty. Silicon's `CCRx` is 0, which holds `OCxREF` at 0 (p1610, p1558): **no pulse** |

**So the gate (`MOE`, `CCxE`, `CCxNE`) changes no consumer observer on any board:** `MOE` is set before `CEN` everywhere, every used channel has `CCxE` = 1, no `CCxP` = 1, and no `CHxN` pin is used (Aethelgard refuses an inverted route, `twin/src/lib.rs` L115). What does change an observer, on unmodified ArduPilot, is the stock `CCRx` reset (§2.2 S0), not the gate.

### 2.5 The period, the first edge, and the clock (WO (3))

- **Aethelgard's witness models Renode's conventions, not silicon's, on purpose** (`aethelgard-twin/src/timer.rs` L6–24 at `65c74788`: "never blended with silicon's"): a period of `ARR` ticks ("where silicon counts `ARR` + 1", `STM32_Timer.cs` L62), the first rise one period after `CEN`, `MOE` and `OCxPE` as tags. `witness_motor` (`aethelgard-cli/src/renode.rs` L1955–1992) predicts every edge from the first rise at that period and calls a spread of 10 ns or more a `mismatch`.
- **So the period reaches a consumer verdict, if Tviburi models it:** an `ARR` + 1 period would move each later edge by one tick a period (1 µs a period on `motors-pwm`; 15.625 ns on DShot), past the 10 ns grain within one or two periods, and **every motor witness on every motor board would become `mismatch`.** That is §7's "an observer would change, on a path the manual says is valid" if built. **The first rise at `CEN`** (silicon) only moves the anchor; the witness anchors on the first rise, so it alone changes no status, but the `DShot` window and `edges_before` counts move by a period.
- **Aethelgard's static check already judges silicon's `ARR` + 1** (`aethelgard-ir/src/motor.rs` L1028–1130), and its boot block reads `PSC`, `ARR`, `CCRx` back exactly, `CCMR1`/`CCMR2` under `0x7373` (no `OCxPE`) and `CCER` under `0xBBBB` (`aethelgard-twin/src/boot.rs` L896–916), and not `BDTR`. Serving `OCxPE` and `BDTR` from copies changes no verdict.
- **The clock** reaches the verdict only through the restatement: the platform's `timerN: frequency:` is the solved clock (64 MHz on the generated boards, 200 MHz on the foreign ones; `aethelgard-twin/src/platform.rs` L924–1012), and firmware and observer are computed from the same `timer_kernel_hz` (`aethelgard-ir/src/solve.rs` L1332–1345). A guard that does not take it runs at the stock 250 MHz and every witness mismatches (§2.3, the trap). **Moved onto the guard, the clock changes nothing.**

### 2.6 ArduPilot armed (`OQ-03`)

**An armed window cannot be produced today, without changing Aethelgard:**
- **The committed fixture** (`sentinel-02`) has no motor output; in 20 s it writes only TIM5 (2,497,355 accesses).
- **AP-m** has motors but no IMU (`serial-dshot-9600` has none): it prints `INS: unable to initialise driver` and stays in that state (from memory: ArduPilot's `config_error` loop, which still sends heartbeats; measured: MAVLink 2 heartbeats on USART1 about every 0.5 s, system status standby). A MAVLink 2 `COMMAND_LONG` `MAV_CMD_COMPONENT_ARM_DISARM` with the force code 21196, written into USART1 from the monitor at 10 s, was never read: **ArduPilot receives USART1 by DMA** (`USART1_CR3` = `0xC0`, `DMAR` = 1), which the twin does not carry (the next `13NN`). **Measured:** `DMAR` = 1 and no read of `USART1_RDR`; that DMA is the cause is inferred.
- **Sentinel-03** (motors and an IMU) has no ArduPilot build: `aethelgard generate hwdef` refuses its topology (`SenseNotEmitted`: `VBAT_SENSE`'s divider ratio has no source, `AET-WO-4400b` D7).
- **What AP-m writes before arming** is in §2.1: `MOE` before `CEN`, `CCMR3` (OC5/OC6, no pin), and no `CCRx`. An armed window needs an Aethelgard board with motors and an IMU whose hwdef emits, and a USART receive path (DMA).

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | held; Table 337 read from the rendered page (p1608) |
| P2 form | **held:** a subclass re-implementing `INumberedGPIOOutput` owns the routes (§2.3) |
| P2 tags, `MOE` ignored, polarity never applied, one line per channel | held (S1, S4, S5) |
| P2 leaks L1, L2 | held (S2/T2: 14 edges; S3) |
| P2 period, clock trap | held (T1; F) |
| P2 (not predicted) | **`CCR` = 0 freezes the line at its last level** (S5); `CCRx` reads `0xFFFF` at reset (S0); the first rise one period after `CEN` (T1) |
| P3 boards | **wrong in count:** 9 generated boots touch TIM1 (all four DShot boards, both `hse`, `pwm`, `djn`, `d1`), and 4 touch TIM2; none TIM8 |
| P3 fields and order | held, except that `CCRx` come before `CCMR` (`PSC`, `ARR`, `CCRx`, `CCMR`, `CCER`, `BDTR`, `UG`, `CEN`) |
| P3 no `CCxP`, `CCxNE`, `LOCK`, `AOE`, `OSSx`, `BKE`, `DTG` | held |
| P4 sentinel-02 | held (20 s: TIM5 only) |
| P4 AP-m fields | held in shape; also `CCMR3`, `CR2`, `SR`, `DIER`, and `CR1` `0x85` (`URS`); reached at 100 ms, not "within 10 s" |
| P4 disarmed `CCRx` = 0 | **wrong:** in AP-m's boot (never past its INS error) ArduPilot writes no `CCRx`, and the stock reads `0xFFFF` (§2.4) |
| P4 arming | **wrong:** not producible (§2.6) |
| P5 | held for the gate; **wrong for AP-m**, by the `CCRx` reset, not the gate |
| P6 clock | held (same function on both sides), with the trap |
| P6 period | **wrong:** the consumer's witness encodes `ARR`, so modelling `ARR` + 1 would change every motor verdict (§2.5) |
| P7 | §4 |

## 4. The one-file memory, by `1201`'s budget (B)

**A prototype figure, not the guard's,** with one instance (TIM1), where the build has two (TIM1 and TIM8): the probe of §1 (a subclass with its tap and four outputs, no rule, no field table), instantiated as `timer1` in every branch boot (`peripherals`: `timer1 (ProbeSTM32_Timer)`; its constructor logged `frequency=200000000`, the consumer's value moved onto it). `tests/1301/ci-memory.sh`, ArduPilot's 5 s boot five times a side, interleaved, on Furnace (load under 1), ELF `a799dfb6`:

| | Peaks (KiB) | Median |
|---|---|---|
| `main` (`9e61b81`'s tree) | 1,007,564; 1,010,348; 998,784; 972,428; 990,852 | 998,784 |
| `main` + the probe | 982,480; 990,188; 1,001,260; 1,009,732; 1,018,472 | 1,001,260 |

`MEDIANS main 998784 KiB, branch 1001260 KiB, difference 2476 KiB (+2.4 MiB)`, **PASS**; every boot ended with 14 witnesses of its own release and 0 GPIO would-refuse. The guard's field table and rules will add to this; the build reports the budget again, in CI.

## 5. The stop (WO §1.1, §7), and the questions

**No §7 stop fires on the gate:** neither firmware's boot would refuse on `MOE`, `CCxE` or `CCxNE`, and no observer changes (§2.4). **Two things would fire §7 if built as the WO's (3) reads,** and one is a stock reset the WO does not name; each needs a ruling:

1. **The period and the first edge (WO (3)).** Silicon's period is `ARR` + 1 (Figure 379, p1558) and `OCxREF` is high from `CEN`; the stock's is `ARR` ticks, its first rise one period later (T1). **Aethelgard's motor witness predicts Renode's convention by design** (§2.5), so modelling silicon's would turn every motor witness on all 9 motor boards to `mismatch`. **Recommendation:** the period and the first edge stay the stock model's in this WO, as acknowledged `stock-wrong` ledger rows citing p1558; changing them is a coordinated change with Aethelgard's witness (a row for both, as Phase 2). The clock is included: the guard takes its `frequency` and the replay moves the consumer's `timerN: frequency:` onto it (§2.3, §2.5).
2. **The `CCRx` reset and `CCR` = 0** (S0, S5): the stock reads `0xFFFF` at reset, so an unwritten channel in PWM mode 1 drives 100 % duty, and a `CCR` = 0 write freezes the line at its last level. Silicon: reset 0 (p1610), and `CCR` = 0 holds `OCxREF` at 0 (p1558). **This changes an observer only on unmodified ArduPilot with motors** (AP-m, in a boot stuck in its INS error: four motor pins high from 120.4 ms; silicon low), which is not a committed consumer run; no generated board leaves a `CCR` unwritten, and `d1`'s `CCR` = 0 is written before `CEN`. **Recommendation:** in scope as part of "the consumer's observers see silicon's level": the guard serves `CCRx` from copies with reset 0 and holds `OCxREF` at 0 for `CCR` = 0 (p1558), and AP-m becomes a planted case. Whether an armed or fully initialised ArduPilot leaves a channel unwritten is not shown (§2.6); the stock reset is wrong either way.
3. **The restatement's reach (fence).** Variant B is required (§2.3): the timer restatement must rename `timer1` (and `timer8`) in the generated `model/stm32h7-gpio-restate.repl` (its `timer1:` route block and the `-> timer1@NN` inputs) and drop the `timer1: @ {…}` block from `model/stm32h7-rcc-restate.repl`, through their generators (`stm32h7-gpio-restate.awk`, `stm32h7-rcc-restate.awk`). Without it the GPIO guard's AF entry stops taking the timer's level. **Ask:** is that "the restatement" of the WO's fence, or a change to the GPIO and RCC guards? I recommend the former: two generator lines, with no change to either guard's code.
4. **Hi-Z.** Renode's GPIO line is two-valued, so the guard cannot release a pin to the GPIO logic. When Table 337 says "not driven: Hi-Z", the guard drives its output low. That is silicon's level only where the pin is pulled down. **Every committed motor pin is**, by the board's own resistor: all 33 `motor_outputs` of the 9 motor boards declare `pull.resistor` 442 kΩ `to` GND with `fitment` `fitted` (the specifications at Aethelgard `65c74788`; `4300j` made it a switchable link, so a board with the link open would float, a case the row names). Handing a released pin to the GPIO guard's undriven rule (pull or declared level) needs the GPIO guard, which is fenced out. **Recommendation:** low, with a ledger row saying Hi-Z is shown as low, and a row for a released-pin query between the two guards.
5. **`CHxN`.** One stock line per channel; the port inverts it onto `CHxN` pins (`invertedAFPins`), so the timer cannot gate `CHxN` apart from `CHx`. Neither firmware uses `CHxN`, and Aethelgard refuses an inverted route. **Options:** (a) `CCxNE` = 1 and `CCxNP` = 1 refuse `unmodeled`; a `CHxN` pin in AF mode with `CCxNE` = 0 still shows NOT `CHx` (silicon Hi-Z), a row for the GPIO guard. (b) The guard adds outputs 4–7 for `CHxN`, and the GPIO restatement routes the inverted (pin, AF) pairs to them and drops them from `invertedAFPins`. That is a change to the GPIO restatement's data and to `1301`'s check of the 102 routes. **Recommendation:** (a) now, (b) as a row.
6. **TIM2–TIM5.** Four boards drive TIM2 CH1–CH2 into observers (§2.4); the stock gates them by `CCxE` with leaks L1/L2 and ignores `CCxP`. No silicon difference is reached. **Recommendation:** this WO guards TIM1 and TIM8 only, per its text; TIM2–TIM5 under Table 342 (p1695) is a row (the same class with no `BDTR`).
7. **Fields outside the gate (`RUL-17`), proposed classes:**
   - modeled by the guard: `CCER.CCxE`, `CCxNE` (0 only, per 5a), `CCxP`, `CCxNP` (0 only, per 5a); `BDTR.MOE`, `OSSR`, `OSSI`, `LOCK`; `CR2.OISx`/`OISxN` (served from copies; used only with `MOE` = 0 and `OSSI` = 1); `CCRx` (per 2);
   - inherited, **probed** (§2.2, T1, the control): `CR1.CEN`; `EGR.UG`; `CCMRx.OCxM` 0110 and 0101; `PSC`, `ARR`, `CCRx`;
   - inherited **by source only** (not probed; audited at the build or allowed only at the values measured): `CR1.ARPE`, `URS` (measured values 1, 1); `CR1.UDIS`, `OPM`, `DIR`, `CMS` (written 0 by both: allow 0 only, else `unmodeled`); `CCMRx.CCxS` (00 only: the input-capture paths are not audited); `CCMRx.OCxM[3]` (bits 16 and 24 of `CCMR1`/`CCMR2`, which the stock reserves: 0 only, so the H7 modes 1000–1111 refuse); `RCR`; the `SR` and `DIER` interrupt flags (`SR` written 0, `DIER` 0 and, on TIM5, `CC1IE`); `CNT` (never written);
   - acknowledged by name: **`CCMRx.OCxPE`** (no preload: a `CCR` write takes effect at once; both firmwares write 1; Aethelgard lists it unobservable); **`CCMR3`** (AP-m writes `0x1016868`; OC5/OC6 drive no pin while `CC5E`/`CC6E` = 0); the period and first edge (per 1);
   - `unmodeled` at any value but reset: `BDTR.DTG`, `BKE`, `BKP`, `AOE`, `BK2*`, `BKF`, H7's `BKDSRM`/`BKBID`; `CR2.CCPC`, `CCUS`, `CCDS`, `MMS`, `MMS2`; `SMCR`; the DMA enables (`DIER.UDE`, `CCxDE`, `COMDE`, `TDE`) and `DCR`/`DMAR` (DMA is the next `13NN`; **the DShot boards write none of them in their 100 ms**, measured); `CCER.CC5x`/`CC6x`; `AF1`, `AF2`, `TISEL`; `EGR.CCxG`, `COMG`, `TG`, `BG`, `B2G`.
   - **`LOCK`:** frozen by the first `BDTR` write after reset (p1615); a later write that would change a locked bit is a `predicted-fault` (silicon keeps the old value: `RUL-18`'s "does not do what the firmware wrote"), and the same value passes (`RUL-18`'s amendment). Neither firmware writes `LOCK` ≠ 0.

**Rows, outside this WO:**
- Tviburi's committed `tests/1301/replay-1301.sh` replays only captures from before Aethelgard's reaper (`… -- /usr/local/bin/renode`): the fixture at `1c32c8ea` is unaffected; a re-captured fixture would need the argv taken after `--`.
- For Aethelgard (findings, not changes): an armed ArduPilot window needs a board with motors and an IMU whose hwdef emits (Sentinel-03's refuses on `VBAT_SENSE`), and a USART receive path (DMA); its witness encodes Renode's period (§2.5), which a Tviburi period model would need to be coordinated with; AP-m on a `serial-dshot-9600` platform runs PWM at 50 Hz, not DShot600 (the hwdef's own note: `MOT_PWM_TYPE` waits for defaults.parm).
