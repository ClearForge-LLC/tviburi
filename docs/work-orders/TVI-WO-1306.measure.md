# TVI-WO-1306 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-25. **Branch:** `wo/tvi-1306-i2c-guard`, from `main` `48abb6b` (`1305a` merged; the WO merged).
**State:** predictions (§0), committed before any run of this work order.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/`: `Peripherals/Peripherals/I2C/STM32F7_I2C.cs` (sha256 `4c8e30b7…`), `Main/Core/Structure/SimpleContainer.cs`, `Main/Peripherals/I2C/II2CPeripheral.cs`. No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1, M2).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`. At the time of these predictions the I2C chapter (§47) had not been read: every RM0433 statement in §0 is **from memory**.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock `STM32F7_I2C.cs` (above): `sealed`, a `SimpleContainer<II2CPeripheral>` whose sensors register on it by address; `[AllowedTranslations(ByteToDoubleWord)]`; only `Read`/`WriteDoubleWord`; `TIMINGR` all tags; `CR1` `DNF`, `ANFOFF`, `TXDMAEN`, `SBC`, `WUPEN`, `GCEN`, `SMBHEN`, `SMBDEN`, `ALERTEN`, `PECEN` tags, `ERRIE` ignored; `TIMEOUTR` (0x14) and `PECR` (0x20) absent from the map; `NACKF` a tag, and the interrupt term for it `&& false`; `PE` cleared resets `STOPF`, `TC`, `TCR` and `TXIS` only; `START` with no device at the address logs "Unknown slave" and returns;
- the stock include chain's I2C entries (`stm32h7.repl`, sha256 `b85cb4af…`, as 1303 copied it): `i2c1`–`i2c3` at `0x40005400`/`5800`/`5C00`, `i2c4` at `0x58001C00`, each `I2C.STM32F7_I2C` on `rcc` with its alias, `EventInterrupt -> exti@N`, `ErrorInterrupt -> nvic@N` (`i2c4`: `exti@63`);
- the I2C devices in the captured platforms (`~/.cache/tvi-1300b/m1/*/…platform-booted.repl`, 1300b's replays of 1305's captures, read for their device lines only): `I2C.BME280 @ i2c1 0x76` on nine generated boards, `Sensors.BMP581 @ i2c1 0x46` on six (and the ArduPilot fixture and the armed run), `Sensors.BMP581 @ i2c2 0x47` on `sentinel-01-ext-bmp581`; none on `d1-drv8876` and `driver-judges-nothing`; the comment "Unmodelled I2C devices (no Renode 1.17.0 model): U3 (TMP117) at I2C1 0x48" on ArduPilot's two;
- the armed run's injection script (`~/.cache/tvi-1305/armed/inj/inject.resc`): it sets `sysbus.i2c1.u7 Pressure`;
- `RUL-13`'s queue rows for I2C1 (`spike/1150/data/queue-ap.tsv` rows 298–300, 339–340, 409–430, 460–464): ArduPilot writes `TIMINGR` 544 times (`SCLDEL` 0x3, `SCLH` 0xF, `SCLL` 0x38, `PRESC` and `SDADEL` 0), `CR1` 3,144 times (`PE`, `ERRIE` 1; every tagged `CR1` field 0), `CR2` 3,142 times, `ICR` 4,231 times; and `RCC.D2CCIP2R.I2C123SEL` written 1;
- `1300a` FEEDBACK §3 (ArduPilot reads, then writes 0 to, I2C1 `CR1` before `I2C1EN`);
- the RCC guard on `main`: `QueryKernelClock` answers `i2c1`/`i2c2` (`I2C123SEL` p416, `I2C1EN`/`I2C2EN` p463); `Register` holds only `GuardedSTM32H7_SPI` instances (kept unlocked, watched by `TVI-RCC-CLOCK`); every other registered peripheral is locked on the bus while its enable is 0. `1300b`'s USART registration (ruled 2026-11-25T03:03Z Q2) is not on `main`;
- `1202a` FEEDBACK F2 (seven shards hold 4,699 s at the reference at 1.53; they carried 4,061 s at its merge) and the USART guard's code (the form this guard would follow).

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9, from memory; §1 reads it)
- **Protected under `PE` = 1:** `TIMINGR` whole ("This register must be configured when the I2C is disabled (PE = 0)"); `CR1` `DNF` and `ANFOFF` ("can only be programmed when the I2C is disabled"); `CR1.NOSTRETCH` likewise. `TIMEOUTR`'s fields are protected by their own enables (`TIMOUTEN`, `TEXTEN`), `OAR1`/`OAR2` by `OA1EN`/`OA2EN`, not by `PE`.
- **Software reset (`PE` = 0):** SCL and SDA released; the state machines reset; `CR2`'s `START`, `STOP`, `NACK` and the status flags (`TXE`, `TXIS`, `RXNE`, `ADDR`, `NACKF`, `TCR`, `TC`, `STOPF`, `BERR`, `ARLO`, `OVR`) reset; the configuration registers kept. `PE` must stay low for at least three APB cycles.
- **Access width:** words only.
- **Kernel clock:** `I2C123SEL` resets to `rcc_pclk1`; a peripheral whose `*EN` is 0 gets no clock, and its registers cannot be written (p373, as the RCC guard cites).

### P2. The stock model, by source (each to be probed, §1 M2)
- **Form.** `STM32F7_I2C` is `sealed`: the guard is `RUL-07`'s wrapper. Unlike the USART, the stock model is also a **bus for its sensors** (`SimpleContainer`): a sensor in the platform registers on the peripheral named `i2cN`, and the stock model finds it in its own `ChildCollection` at `START`. **A wrapper that only forwards accesses loses every transfer** ("Unknown slave"), because the sensors register on the wrapper; forwarding `Register` to the owned instance will throw or double-register, since the owned instance is not in the machine's tree. The form needs the owned instance to see the wrapper's children. Held with moderate confidence; this is M3's question.
- **`TIMINGR`** written any value reads 0 (tags), with or without `PE`. **`CR1`** written all ones reads back without the tags and `ERRIE`.
- **No `PE` rule:** `TIMINGR` and `DNF`/`ANFOFF` written with `PE` = 1 are discarded exactly as with `PE` = 0, with no record; `NOSTRETCH` written with `PE` = 1 is taken.
- **`PE` cleared** resets `STOPF`, `TC`, `TCR`, `TXIS` only: `RXNE` (the receive queue), `ADDR` and a pending transmit queue survive it.
- **`START` with `PE` = 0** runs the transfer: the stock model never reads `PE`.
- **No `NACKF`:** a `START` to an address with no device logs a WARNING ("Unknown slave at address …"), sets nothing, raises nothing.
- **Sub-word:** a byte write is widened to a word (the attribute); a half-word access is refused by the bus with a warning and does nothing.
- **`TIMEOUTR`, `PECR`:** "Unhandled read/write" warnings; read 0.
- **Reset `ISR`:** `0x00000001`.

### P3. The generated firmware (14 boots)
- **Twelve boards write I2C** (each with a device on `i2c1`; `sentinel-01-ext-bmp581` on `i2c2` too); `d1-drv8876` and `driver-judges-nothing` write none.
- Each port, polled: `TIMINGR` with `PE` = 0, then `CR1` with `PE`, then per transfer `CR2` (`SADD`, `NBYTES`, `RD_WRN`, `AUTOEND`, `START`), `TXDR` bytes or `RXDR` reads, `ISR` polling and an `ICR` clear. No interrupt enable, no DMA, no 10-bit address, no `RELOAD`.
- **No protected field is written while `PE` = 1.** No read of `TIMINGR`. High confidence.
- So no generated observer changes from the guard's rules (P7).

### P4. ArduPilot, `sentinel-02-own` (the committed fixture, ELF `a799dfb6`, 5 s)
- **`i2c1` only** of the four I2C ports; the `CR1` read and write before `I2C1EN` (dropped by the RCC guard's bus lock on `main`, with no record; the read returns 0).
- **ChibiOS's `i2c_lld_start`, 544 times:** `CR1` written with `PE` = 0 (`ERRIE` and the interrupt enables), `TIMINGR` = `0x00300F38`, then `CR1` with `PE` set by a read-modify-write; `i2c_lld_stop` clears `PE` (and perhaps `I2C1EN`). **Every `TIMINGR` write is made with `PE` = 0; `DNF` and `ANFOFF` are always 0.** High confidence on the first.
- **The read-modify-write of `CR1`** reads back the stock model's view; with `CR1`'s tags all written 0 and `ERRIE` the only dropped bit, a copy-served `CR1` keeps `ERRIE` set where the stock model clears it on the next read-modify-write. That changes no protected field. Moderate confidence.
- **No read of `TIMINGR`** by ArduPilot, so serving it from a copy changes no control flow.
- **Transfers:** the BMP581 at `0x46` (reads of its registers, a few bytes each), and probes of addresses with no device (other barometers and compasses ArduPilot looks for on its I2C buses), each an "Unknown slave" WARNING with no `NACKF`, so each probe ends by ChibiOS's timeout, not a NACK. **Tens of distinct probed addresses.** Moderate confidence.
- **No DMA** (`TXDMAEN` written 0; `RXDMAEN` 0).

### P5. The armed run (Sentinel-03, 13 s; 1305's `armed` capture, replayed as 1300b did)
- As P4 on `i2c1`, for longer: no `TIMINGR`, `DNF` or `ANFOFF` write with `PE` = 1; the injected `Pressure` reaches the BMP581 through the same transfers. Arming adds no I2C register.

### P6. The clock (WO (2))
- **With `I2C1EN` = 0** the access is dropped by the RCC guard's bus lock (silicon's ignored access, p373), with no record: ArduPilot's pre-enable `CR1` read and write are covered this way on `main`.
- **Every start runs on a running kernel clock:** generated, `I2C123SEL` at reset (`rcc_pclk1`); ArduPilot, `I2C123SEL` = 01 (`pll3_r_ck`) with PLL3 and `DIVR3EN` set before the first `PE`. The query answers "running" at every first `PE`. Moderate confidence for ArduPilot (PLL3's enable is not seen yet).
- **`i2c3` and `i2c4` have no query rule** (`QueryKernelClock` covers `i2c1`/`i2c2`); no committed board uses them.

### P7. Consumer observers (Aethelgard `4e5b0d3b`; the survey is §2.4)
- **From the wrapper's form:** the `peripherals` listing's kind changes from `STM32F7_I2C` on every board, as for the earlier guards (the class map's consumer step), if Aethelgard checks I2C kinds; the owned instance's WARNING lines ("Unknown slave") lose their source name.
- **From the rules:** none on any board, if P3–P5 hold: a stored `TIMINGR` is read by no observer, and no write refuses.
- The guard's witness and end records log under `i2cN`: a consumer parser of `i2cN:` lines (if one exists, as the USART's serial parser) would see them.

### P8. Memory (`1201`'s budget, like for like)
- A thin wrapper for `i2c1`–`i2c4`: within ±12 MiB of `main` at the median, eleven boots a side, as 1303's (+8.0) and 1305's (−6.5).

### P9. The shard projection (`1202a` F2's method, at 1.53)
- The seven shards on `main` carry more than `1202a`'s 4,061 s (1305a's items were added), and this guard adds about 400–550 s at the reference (the USART guard's 402 s, the DMA guard's 542 s). **The slowest shard passes 20 minutes at 1.53 with this guard's items, so the eighth shard comes first, by the ruling.** Moderate confidence.

## 1. How it was measured (after §0 was committed at `b111160`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `4e5b0d3b` pins (`container/IMAGE`, fetched this session). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1306/` (`m1/`, `m3/`, `probe/`, `bench/`, `budget.log`, `shard/`, the scripts in `h/`), kept until the architect says.

| Run | What | How |
|---|---|---|
| **M1** | the 14 generated boots, ArduPilot's fixture and the armed run, each replayed on `main`'s release (every guard recorded), `rcc` and `i2c1`–`i2c4` access-logged, the 28 I2C registers and the RCC guard's `QueryKernelClock "i2c1"`/`"i2c2"` read after `RunFor` | `h/loop.sh` and `h/replay-l.sh` (`1300b`'s harness, its paths changed) over `1305`'s captures (`~/.cache/tvi-1305/cap-gen`, `cap-ap`, `armed`; read-only). The armed run is `1305`'s `armed` capture with its injection, as `1300b` replayed it. Analysed by `h/i2c-analyse.py`, `h/clock-at-pe.py`, `h/xfer-state.py` |
| **M2** | the stock `STM32F7_I2C` by probe | monitor-driven on `main`'s Nucleo board, `i2c1` stock with a stock `I2C.BME280` at `0x76`, the RCC guard recorded (`probe/probes.body`, I0–I15; `probes-n0.body`, I8b–c) |
| **M3** | the form | the same body through a scratch wrapper (`probe/ProbeSTM32F7_I2C.cs`), `i2c1`–`i2c4` cancelled and restated (`probe/stm32h7-i2c-restate.repl`); board B shares the owned instance's children, board C forwards `Register`. Then the form control: five M1 boots (four generated, ArduPilot's fixture) replayed on that wrapper, against M1 |
| **B** | the one-file memory, by `1201`'s budget | `tests/1301/ci-memory.sh`, eleven boots a side, interleaved: this branch (`main` and this document) against `bench/`, the same tree plus M3's wrapper as all four ports, its restatement, and a replay that repoints the sensors |
| **S** | the shard projection | `tools/tviburi-route plan` (built from this tree) on `main`'s `times.tsv`, cases and mutants, with this guard's items added at the earlier guards' sizes (`shard/`) |

## 2. Results

### 2.1 The field table, in order against `PE` and `I2CxEN` (M1)

"Under `PE` = 1" means the last `CR1` written had `PE` = 1. A protected change is `TIMINGR` written, or `CR1` `DNF`, `ANFOFF` or `NOSTRETCH` changed, under `PE` = 1 (§2.2's pages).

**The 14 generated boots** (100 ms). Twelve write I2C; `d1-drv8876` and `driver-judges-nothing` write none.

| Board | Ports | Each port's writes, in order | Transfers (7-bit address, direction, bytes) |
|---|---|---|---|
| `sentinel-01`, `-minimal`, `motors-pwm`, `motors-dshot600`, `serial-dshot-9600` | `i2c1` (`TIMINGR` `0x10C7123C`); `sentinel-01` also `i2c2` (`0x50D32A40`, no device) | `CR1` read once (`PE` = 0); `CR1` = 0; `TIMINGR`; `CR1` = 1. Per transfer: `CR2` (whole word: `SADD`, `NBYTES`, `RD_WRN`, `START`, `AUTOEND` on the read), `TXDR` or `RXDR`, `ISR` polls, `ICR` = `0x30` | BME280 `0x76`: W 1, R 1 (chip id). **TMP117 `0x48`: W 1, "Unknown slave"**, then `ISR` polled to the end of the window (≈20,000 reads) |
| `sentinel-01-ext-bmp581` | `i2c1` as above; `i2c2` (`0x50D32A40`) | the same per port | as above, and BMP581 `0x47` on `i2c2`: W 1 ×2, W 3, R 1, R 6 |
| `hse-crystal-16`, `hse-external-8` | `i2c1` (`0x10C7123B`) | the same | BME280 `0x76`, BMP581 `0x46`, TMP117 `0x48` (unknown) |
| `sentinel-02-bench`, `-bench-hse8` (`0x10D81340`), `sentinel-02-shaped`, `sentinel-03` (and `i2c2`, no device) | `i2c1` | the same | BMP581 `0x46`; `-shaped` and `sentinel-03` also `0x48` (unknown), `-shaped` `0x76` |

- **No protected field is written while `PE` = 1** on any port of any board. `PE` is set once and never cleared. Every access is a word. No `OAR1`, `OAR2`, `TIMEOUTR` or `PECR` access, no `RELOAD`, no 10-bit address, no DMA or interrupt enable. No read of `TIMINGR`.
- **No transfer the bus would not make** (`h/xfer-state.py`, checked against M2's planted cases): no `START` or `TXDR` with `PE` = 0, no `NBYTES` = 0 without `RELOAD`, no `START` while the previous transfer is open.

**ArduPilot, the fixture** (5 s, `i2c1` only; ChibiOS's I2Cv2 driver).
- **Before `I2C1EN`:** one `CR1` read and one `CR1` write, both dropped by the RCC guard's bus lock ("Tried to read/write a locked peripheral"), with no Tviburi record. The read returns 0.
- **544 starts** (`i2c_lld_start`): `CR1` = `0x90` (`ERRIE`, `NACKIE`; `PE` = 0), `TIMINGR` = `0x00300F38`, `CR1` = `0x91`. **543 stops** clear `PE` (`i2c_lld_stop`). **Every `TIMINGR` write is made with `PE` = 0; `DNF`, `ANFOFF` and `NOSTRETCH` are always 0.** Between, `CR1` is rewritten whole with `PE` = 1 (`0x92`–`0xD7`: interrupt enables only), and read 6,831 times. No `TIMINGR` read.
- **Transfers:** BMP581 `0x46`: W 1 ×483 (a register address), R 6 ×458, R 1 ×25, W 2 ×3; with `STOP` written by software (no `AUTOEND`). **Probes of absent addresses, 11 each:** `0x30` (W 2), `0x38`, `0x39`, `0x54` (W 1), `0x55` (W 2), and `0x76` ×3: 66 "Unknown slave" lines. No `NACKF` is ever set, so each probe ends by ChibiOS's timeout, then `STOP`, then a stop and restart of the driver.
- **`ICR`:** besides the error clears, ChibiOS writes `ICR` = `0x2`, `0x4` and `0x40`, bits 1, 2 and 6, which are **reserved in `ICR`** (p2012: "31:14, 7:6, 2:0"), 16,522 times over the fixture and the armed run (the `TXIS`/`RXNE`/`TC` positions of `ISR`).

**The armed run** (Sentinel-03, 13 s): `i2c1` as the fixture, longer (1,461 starts, `TIMINGR` = `0x00707CBB`, BMP581 `0x46`: R 6 ×1,334), and **`i2c2` too** (55 starts, the same probes of `0x30`–`0x55`, no device); one locked `CR1` read and write before each port's enable. **No protected change, no transfer the bus would not make.**

### 2.2 What silicon does (RM0433 Rev 9, read; `~/.cache/tvi-1306/rm/i2c-fields.md`, its load-bearing quotes checked against the text)

- **Protected by `PE`:** `TIMINGR`: "This register must be configured when the I2C peripheral is disabled (PE = 0)" (p2008 L78452). `CR1.NOSTRETCH` (p2002 L78167), `ANFOFF` (p2002 L78182), `DNF` (p2003 L78193): "can be programmed only when the I2C peripheral is disabled (PE = 0)". The caution "Changing the timing configuration and the NOSTRETCH configuration is not allowed when the I2C peripheral is enabled" (p1959), and Figure 542's flow (p1959). **No sentence says what silicon does with such a write** (ignored, taken, or undefined).
- **Protected by other conditions:** `CR2` `NBYTES`, `HEAD10R`, `ADD10`, `RD_WRN`, `SADD`: "Changing … when the START bit is set is not allowed" (pp. 2004–2006); `TXDR`: "can be written only when TXE = 1" (p2014); `OAR1`/`OAR2` by `OA1EN`/`OA2EN` (pp. 2006–2007); `TIMEOUTR` by `TIMOUTEN`/`TEXTEN` (p2009); `PECEN` and `SBC` by chapter cautions (pp. 1964, 1987).
- **Software reset** (§47.4.6, p1959–1960): "The reset of the I2C peripheral is performed by clearing the PE bit … releasing the SCL and SDA lines. Internal state machines are reset and the communication control bits and the status bits revert to their reset values. This reset does not impact the configuration registers. The impacted register bits are: 1. I2C_CR2 register: START, STOP, PECBYTE, and NACK 2. I2C_ISR register: BUSY, TXE, TXIS, RXNE, ADDR, NACKF, TCR, TC, STOPF, BERR, ARLO, PECERR, TIMEOUT, ALERT, and OVR". `TXE` "is set by hardware when PE = 0" (p2011). "PE must be kept low during at least three APB clock cycles … 1. Write PE = 0 2. Check PE = 0 3. Write PE = 1" (p1960).
- **Access width:** "The registers are accessed by words (32-bit)" (p2001). Nothing on byte or half-word accesses.
- **Clocks:** "The I2C kernel is clocked by i2c_ker_ck" (p1954); `I2C123SEL` (`RCC_D2CCIP2R` 13:12, p416) resets to `rcc_pclk1`; `I2C1EN`–`I2C3EN` are `RCC_APB1LENR` bits 21–23 (p463); **`I2C4EN` is `RCC_APB4ENR` bit 7** (p472) and `I2C4SEL` is in `RCC_D3CCIPR` (p420). With the bus clock off: "read or write accesses to the peripheral registers are not supported. A read access will return invalid data. A write access will be ignored and will not create any bus errors" (p373).
- **A NACK:** "When a NACK is received, the TXIS flag is not set and a STOP condition is automatically sent. The NACKF flag of the I2C_ISR register is set. An interrupt is generated if the NACKIE bit is set" (p1975, controller transmitter); "The START bit is reset by hardware when the target address is sent on the bus, whatever the received acknowledge value" (p1974).
- **Not in the text:** what a `START` written with `PE` = 0 does (only "0: Peripheral disabled", p2003); `NBYTES` = 0 without `RELOAD`; a `START` while a transfer is open; what a write of 1 to a reserved bit does ("Reserved, must be kept at reset value").

### 2.3 The stock model, by probe (M2)

| # | Probe | Stock (measured) | Silicon (RM0433 Rev 9) |
|---|---|---|---|
| I0 | `CR1`, `TIMINGR` written and read with `I2C1EN` = 0 | dropped by the RCC guard's bus lock; reads 0; **no Tviburi record** | write ignored, read "invalid data" (p373) |
| I1 | reset reads | all 0, `ISR` `0x1`; `TIMEOUTR` (0x14), `PECR` (0x20) "Unhandled read"; `RXDR` empty: "Receive buffer underflow!" | reset values 0, `ISR` `0x00000001` (pp. 2001–2014) |
| I2 | all ones written with `PE` = 0 | `CR1` reads `0x000280FE`: `TXIE`…`TCIE`, **`ERRIE` kept**, `RXDMAEN`, `NOSTRETCH`; **dropped** `DNF`, `ANFOFF`, `TXDMAEN`, `SBC`, `WUPEN`, `GCEN`, `SMBHEN`, `SMBDEN`, `ALERTEN`, `PECEN`. `CR2` `0x03FF0FFF` (drops `HEAD10R`, `NACK`, `PECBYTE`). `OAR1` `0x87FF`, `OAR2` `0x87FE`. **`TIMINGR` 0.** `TIMEOUTR`, `PECR` unhandled | every field stored |
| I3 | `TIMINGR` and `CR1` `DNF`/`ANFOFF`/`NOSTRETCH` written with `PE` = 1 | `TIMINGR` discarded (as with `PE` = 0); `DNF`, `ANFOFF` dropped; `NOSTRETCH` **taken**; no record | "must be configured when … (PE = 0)" (p2002–2003, p2008) |
| I4 | a read of 2 left one byte unread; then `PE` = 0 | **`RXNE` survives** (`ISR` `0x5`); `RXDR` then returns the byte (0); **`TC` set after** (`ISR` `0x41`) | `RXNE`, `TC` reset by `PE` = 0 (p1959) |
| I5 | `START` with `PE` = 0 | **the transfer runs**: the BME280 is written and read | "0: Peripheral disabled" (p2003) |
| I6 | `START` to `0x48` (no device) | "Unknown slave at address 72."; **`ISR` stays `0x1`: no `NACKF`, no `STOPF`**; `TXDR` then "Trying to send byte 0 to an unknown slave"; `NACKCF` a tag | `NACKF`, automatic `STOP` (p1975) |
| I7 | byte write to `CR1`+1; half-word write and read | byte: widened to the word (read-modify-write); **half-word: "isn't supported", ignored**; half-word read 0 | "accessed by words" (p2001) |
| I8b | `NBYTES` = 0, `START`, write, to the BME280, from a clean state | **Renode aborts** ("Sequence contains no elements", core dumped, exit 134): the stock model hands the sensor an empty write. A sensor-dependent crash; in I8 (later in the body, with a byte left in the transmit queue) it did not crash | not in the text |
| I9 | `START` while a write of 2 has sent 1 byte | the new transfer runs; the open one is abandoned | not in the text |
| I11 | `NBYTES` changed with `START` set | taken; the stock model's own "not permitted" check never fires (it clears `START` in the same write) | "not allowed" (p2004) |
| I12 | `PE` = 0 then `PE` = 1 in consecutive writes | taken | at least three APB cycles, by write–check–write (p1960) |
| I14 | `I2C123SEL` = 01 with PLL3 off, then `PE` | the query says "NOT running"; **the stock model starts, with no record** | no kernel clock (p371, p1954) |
| I15 | `I2C1EN` cleared with `PE` = 1 | locked; a `TIMINGR` write dropped | p373 |

### 2.4 The form (M3) and the form control

- **`STM32F7_I2C` is `sealed`,** so the guard is `RUL-07`'s wrapper. It must also be its sensors' bus: a `SimpleContainer<II2CPeripheral>` on which `bme: … @ i2c1 0x76` registers.
- **The owned instance must see the wrapper's children.** Forwarding `Register` to it throws (`KeyNotFoundException`: the owned instance is not in the machine's tree; board C). Sharing its `ChildCollection` with the wrapper's own (one reflection write in the constructor; board B) works: every transfer reaches the sensor.
- **A sensor declared `@ i2c1` binds to the cancelled entry** and the load fails (E39, "Unable to finish the registration of 'bme'"): the consumer must repoint each sensor to the guard's variable (`@ tviburiI2c1`), as it repoints GPIO references (`RUL-09` (b)). The sensor keeps its name under the port (`i2c1.bme`, `i2c1.u7`), so the consumer's `logLevel -1 sysbus.i2c1.u2` and stimulus commands still resolve.
- **M2 through the wrapper is identical to the stock run** (every value read, every access-log line with its register name, every sensor line), apart from the owned instance's own lines, which lose their source name: "Unhandled read/write …", "Slave address …", "Receive buffer underflow!", and **"Unknown slave at address N."**.
- **The form control:** `sentinel-01`, `sentinel-01-ext-bmp581`, `sentinel-03`, `hse-crystal-16` and ArduPilot's fixture replayed on the wrapper against M1: **identical** on every I2C and RCC access line, sensor line, dumped value and boot-block line (20,076 to 98,764 lines a boot), and in their `[ERROR]` count. The one difference: each "Unknown slave" line (1 a generated boot, 58 in ArduPilot's) without `i2c1:`. The `peripherals` listing shows `i2c1 (ProbeSTM32F7_I2C)` with `u7 (BMP581)` under it.

### 2.5 Consumer observers (Aethelgard `4e5b0d3b`; `~/.cache/tvi-1306/observers.md`, its two load-bearing lines read)

| Observer | Reads | Changed by the form | Changed by the rules |
|---|---|---|---|
| `peripherals` listing (`aethelgard-cli/src/renode.rs`:2834–2843; `renode/foreign.rs`:1572–1581; the kind `I2C.STM32F7_I2C`, `twin/src/platform.rs`:70–73) | each solved `i2cN` with kind exactly `STM32F7_I2C` | **yes, every board with an I2C port:** the class name (the class map's consumer step, `RUL-09`) | — |
| sensor declarations in the emitted `.repl` (`platform.rs`:615, :1203) | `uN: … @ i2cN 0xAA` | **yes, every board with an I2C sensor:** each must be repointed to the guard's variable, or the load fails E39 (§2.4). **A new consumer step** | — |
| `absent_address` (`renode.rs`:2257–2268; `AET-WO-3305`) | `[WARNING] i2c1: Unknown slave at address N.`, by source | **yes:** the owned instance's line has no source, so the "wrong address" evidence goes empty, silently (every generated board probing `0x48`; ArduPilot's 66) | — unless the guard writes that line under its own name (§5 Q9) |
| `i2c_timeout` (Aethelgard `docs/architecture.md`:319) | `TXIS` never rising for a missing device | no | no, unless the guard models `NACKF` (§5 Q9) |
| `renode_error_log` (`renode.rs`:2895; `foreign.rs`:1257) | any `[ERROR]` line | no | a refusal record (`RUL-12`: Error level) fails the run, as intended |
| sensor witnesses (`renode.rs`:2404–2424) | `[NOISY] i2c1.uN: …` | no (M3) | no |
| boot block (`boot.rs`:1182–1220) | `RCC_APB1LENR`, `APB4ENR` only; no I2C register | no | no |
| `RUL-19`'s rise time (`verdict.rs`:1178–1180) | ngspice from declared values; never `TIMINGR` | no | no |
| ledger rows (`ledger.rs`:1133–1191), `unobservable` (`boot.rs`:1043) | static text: `TIMINGR` "not stored (measured)" | — | a stored `TIMINGR` makes the text stale (a consumer row, `OQ-04`) |

No Aethelgard code access-logs an I2C port or reads an I2C register back; nothing reads `TIMINGR` or `CR1` from the twin.

### 2.6 The clock (WO (2))

- **`I2CxEN` = 0:** an access is dropped by the RCC guard's bus lock, a read returns 0, with no record (I0, I15): silicon's ignored write (p373). ArduPilot's two pre-enable accesses per port are covered this way today (§2.1).
- **Every port of every run starts on a running kernel clock** (`h/clock-at-pe.py`, the RCC fields as last written before each first `PE`): generated, `I2C123SEL` = 00 (`rcc_pclk1`) with `I2CxEN` set; ArduPilot, both runs, `I2C123SEL` = 01 (`pll3_r_ck`) with `PLL3ON` and `DIVR3EN` set. The query answers "running" for `i2c1` at the end of ArduPilot's run (and `i2c2`'s "NOT running" there is `I2C2EN` = 0: the fixture never enables it).
- **The query covers `i2c1` and `i2c2` only;** `i2c3` would answer "not modeled", and `i2c4`'s enable and selector are in the D3 domain (`APB4ENR`, `D3CCIPR`).

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | **held** on the protected set (`TIMINGR`, `DNF`, `ANFOFF`, `NOSTRETCH`), the reset scope (plus `BUSY`, `PECBYTE`, `PECERR`, `TIMEOUT`, `ALERT`, and `PECR` cleared, p2013) and the access width. Not predicted: the text never says what silicon does with a protected write; `I2C4EN` is in `APB4ENR` (p472), not `APB1LENR`; the reset sequence is write–check–write (p1960) |
| P2 form | **held:** forwarding `Register` throws (`KeyNotFoundException`); the shared `ChildCollection` works. Not predicted: sensors declared `@ i2cN` fail E39 on the cancelled entry and must be repointed |
| P2 `TIMINGR`, tags, no `PE` rule, `PE` clear, `START` with `PE` = 0, no `NACKF`, `TIMEOUTR`/`PECR`, reset `ISR` | **held** (I1–I6, I14). Detail wrong: `ERRIE` reads back (kept as an ignored bit), so only the ten tags are dropped from `CR1` |
| P2 sub-word | **held** (byte widened, half-word ignored with a warning) |
| — | **Not predicted:** a write transfer with `NBYTES` = 0 crashes Renode (I8b) |
| P3 | **held, two details wrong:** the generated firmware reads `CR1` once before `PE`, and it also configures `i2c2` on `sentinel-01`, `-ext-bmp581` and `sentinel-03`; and it probes the unmodelled TMP117 at `0x48` (an "Unknown slave"), then polls `ISR` to the end of the window |
| P4 | **held:** the pre-enable pair is locked with no record; 544 starts, every `TIMINGR` with `PE` = 0; no `TIMINGR` read; no DMA. Wrong: `CR1` is written whole, never read-modify-written, and `ERRIE` is stored by the stock model anyway, so a copy-served `CR1` changes nothing ArduPilot reads. **Probes of absent addresses: six addresses, 66 probes** ("tens of addresses" was wrong). Not predicted: `ICR` written with reserved bits 1, 2 and 6 |
| P5 | **held, one detail wrong:** the armed run also starts `i2c2` (55 times; no device on it) |
| P6 | **held** (§2.6) |
| P7 | **held** on the listing; **wrong in kind on two:** the sensor declarations must be repointed (E39), and the source-less "Unknown slave" line empties Aethelgard's `absent_address` evidence. No consumer parser reads `i2cN:` access lines |
| P8 | §4 |
| P9 | §5: **held in substance:** at 1.53 seven shards' jobs stay under 20 min, but `ci`'s wall reaches 19.7–20.5 min |

## 5. The shard projection (S)

`1202a`'s method: a shard job is 53 s of setup, unscaled, plus its items' weights × 1.53; `ci`'s wall adds 1.7–2.0 min outside the shards (`1202a.measure.md` §4). `main` now carries **4,304 s** at the reference (299 cases, 386 mutants, every item weighed in `times.tsv`; `1202a` merged with 4,061 s). This guard's items are projected at the last three guards' sizes (`1202a` F2: USART 402 s, flash 484 s, DMA 542 s).

| Plan | Total (s, at 1.00) | Longest shard (s) | Shard job at 1.53 (min) | `ci` wall at 1.53 (min) |
|---|---|---|---|---|
| `main`, seven shards | 4,304 | 615 | 16.6 | 18.3–18.6 |
| + this guard at 402 s, seven | 4,704 | 672 | 18.0 | **19.7–20.0** |
| + this guard at 542 s, seven | 4,843 | 692 | 18.5 | **20.2–20.5** |
| + this guard at 542 s, **eight** | 4,843 | 605 | 16.3 | 18.0–18.3 |
| + this guard at 542 s and `1300b`'s ~200 s, eight | 5,040 | 630 | 17.0 | 18.7–19.0 |

- **The slowest shard's job stays under 20 minutes at 1.53 with seven shards (18.0–18.5 min); `ci`'s wall does not** (19.7–20.5 min). `1202`'s 20-minute target is `ci`'s wall, and `1202a` F2 measured its headroom by the wall ("about one more guard"; it was the USART's 402 s, and `1305a` took 243 s of it). `1300b`, beside this, adds items too.
- **So, by the WO's rule read as `ci`'s wall, the eighth shard comes first.** It is two lines in the shard matrix and the aggregate's count, as `1202a` built seven (§6 Q12).

## 6. The stop (WO §1.1, §7), and the questions

**Stops.** §1.1's stop, with the measurement. Three findings bear on §7 and the fence:
- **Registration needs an RCC edit** (WO §3: "If `1300b`'s registration API is not on main when you build, stop and ask"). `1300b`'s API is not on `main`. On its branch (`wo/tvi-1300b-rcc-rows`, read from the shared object store) it is typed to the USART guard: `Register` puts a `GuardedSTM32F7_USART` in `watched` (a `Dictionary<string, GuardedSTM32F7_USART>`), and `Holds` reads `guarded` and `watched`. An I2C guard can be held only by a change to that code (Q4).
- **The absent-address path cannot follow `RUL-17` without refusing both firmwares' boots** (Q9): the stock model never sets `NACKF`, and every generated board with the TMP117 and every ArduPilot run probe an address with no model (§2.1). RM0433 says silicon NACKs (p1975), so refusing is honest but fails the WO's acceptance; passing is a silent difference unless it is acknowledged by name.
- **ChibiOS writes reserved `ICR` bits** (Q10): 16,522 writes in ArduPilot's runs. `RUL-17` refuses them; RM0433 says only "must be kept at reset value".

Nothing in the generated boots, ArduPilot's fixture or the armed run writes a protected field with `PE` = 1, starts without a running kernel clock, or makes a transfer the bus would not make (§2.1). No consumer observer changes from the proposed rules; three change from the form (§2.5).

**Q1. The form.** `STM32F7_I2C` is sealed and is its sensors' bus (§2.4).
- **Proposal:** `RUL-07`'s wrapper, `GuardedSTM32F7_I2C`, a `SimpleContainer<II2CPeripheral>` owning an unregistered stock instance whose `ChildCollection` is the wrapper's own (one reflection write, measured); forwarding every access it passes, `EventInterrupt`, `ErrorInterrupt`, `DmaReceive` and reset; a nested `Registers` enum so the access log keeps its names. The stock instances cancelled and restated on `tviburiRcc` with their alias and both interrupt lines (`model/stm32h7-i2c-restate.repl`, generated from the pinned file, as `1305`'s). The consumer's sensors repointed from `@ i2cN` to `@ tviburiI2cN` (`RUL-09` (b)), as the replay does.
- **Which instances:** `i2c1`, `i2c2` and `i2c3` (the RCC query covers `i2c1`/`i2c2`; `i2c3` would answer every start `unmodeled`, as `1305`'s unqueried UARTs). **`i2c4` stays stock** (WO: not `I2C4` unless a committed board uses it; none does, and its clock is `APB4ENR`/`D3CCIPR`).
- **Prices, named for the consumer:** the `peripherals` kind (`STM32F7_I2C` → `GuardedSTM32F7_I2C`, the class map); each sensor repointed (new for this guard: E39 otherwise); the owned instance's lines without a source name (Q9 restores the one Aethelgard parses).
- **Ask:** this form, and three instances?

**Q2. Fields protected while `PE` = 1.**
- **The fields:** `TIMINGR` whole (p2008); `CR1` `DNF` (p2003), `ANFOFF` (p2002), `NOSTRETCH` (p2002; outside the allow-list at 1 anyway, Q11). The same value passes (`RUL-18`). A `CR1` write that clears `PE` is judged as written with `PE` = 0 (`1305`'s ruling Q2 (b)); none is measured. A byte write is judged on the word it makes.
- **The kind.** The WO and its acceptance say `predicted-fault`; RM0433 says only "can be programmed only when … (PE = 0)" and "must be configured when … (PE = 0)", never what silicon does with the write — the case `1305`'s Q2 ruled `unmodeled` for the USART. Options: **(i)** `predicted-fault`, as the WO says, with a bench row (`RUL-18`): on the Nucleo, I2C1 enabled, `TIMINGR` written with `PE` = 1, read back, and SCL timed on a scope, expecting the old value (from memory; no result); **(ii)** `unmodeled` until that row has a result, as the USART's. No firmware measured writes one, so neither changes a verdict today. **No recommendation on silicon's behaviour**; (i) matches the WO's acceptance.
- **Ask:** the fields, and (i) or (ii).

**Q3. A start with no kernel clock.** At `PE` 0 → 1, the guard asks the RCC guard's `QueryKernelClock` with its port's name: not running is `predicted-fault` ("The I2C kernel is clocked by i2c_ker_ck", p1954; p371; the query's citation); not modeled (`i2c3`) is `unmodeled`. With `I2CxEN` = 0 the bus lock already drops the write with no record (I0): covered, not duplicated, and a test shows both. No firmware starts without its clock (§2.6). A kernel clock lost under a running I2C (`TVI-RCC-CLOCK`) is not in this WO: a row. **Ask:** accept.

**Q4. Registration** (WO: "a guarded I2C port not registered on the RCC guard refuses every access `unmodeled`").
- `1300b`'s `watched` set is typed `GuardedSTM32F7_USART`, and its `TVI-RCC-CLOCK` reads `UeSet` from it. Holding an I2C guard needs one of: **(a)** after `1300b` merges, a granted change to the RCC guard: `watched` keyed by an interface (`bool Running` for `TVI-RCC-CLOCK`) both guards implement, or a third set beside it; **(b)** this guard asks `Holds` and `1300b`'s builder adds the I2C type to its `Register` before READY; **(c)** registration left to a later RCC work order, the I2C guard refusing nothing on it meanwhile (not the WO).
- **Recommendation: (a), after `1300b`'s merge,** with the smallest change: `Register` also accepts the I2C guard into `watched` through an interface; `TVI-RCC-CLOCK`'s watch of a running I2C stays out (a row). This WO then builds against `main` with `1300b` in it.
- **Ask:** (a), (b) or (c), and the RCC fence for it.

**Q5. Reads** (`RUL-17`'s read rule). `CR1` and `TIMINGR` from the guard's copies (so `DNF`, `ANFOFF` and `TIMINGR` read back as written; the rest of `CR1` equals the stock model's, which stores it); everything else from the stock model. No observer reads either (§2.5). **Ask:** accept.

**Q6. The software reset, modeled** (p1959–1960, p2011, p2013).
- **Proposal:** at a `CR1` write that clears `PE` (1 → 0), the guard resets the owned instance (the stock `Reset`: both byte queues, every `ISR` flag, `TXE` = 1 as p2011 says, the interrupt lines) and restores the configuration it keeps: `CR1` (with `PE` = 0), `OAR1`, `OAR2` (0 under Q11), and `CR2`'s `SADD`, `RD_WRN`, `ADD10`, `NBYTES`, `RELOAD`, `AUTOEND` (p1959: only `START`, `STOP`, `PECBYTE`, `NACK` reset in `CR2`; "does not impact the configuration registers"). `TIMINGR` is the guard's own copy. That removes I4's survivors (`RXNE`, then `TC`).
- **"At least three APB clock cycles"** (p1960): the twin cannot count them. A `CR1` write setting `PE` as the very next access to the port after the one that cleared it (no access between, so no "Check PE = 0" read) is `unmodeled`. No firmware does it (§2.1: ArduPilot always writes `CR1` = `0x90` and `TIMINGR` in between).
- **Ask:** accept.

**Q7. The access before `I2CxEN`** (ArduPilot's one `CR1` read and one `CR1` = 0 write per port, §2.1).
- **Recommendation: modeled as silicon ignores it, by the bus lock `1300a` made,** with no record: the guard stays locked while its enable is 0, as `1300b`'s USART guard is ("watched, never unlocked"), so the write is dropped (p373) and no new code is needed. The one deviation is the read: silicon returns "invalid data" (p373), the lock returns 0. ArduPilot writes 0 whatever it read. The ledger row names it.
- **Not** `1305a`'s acknowledgement by name: the lock already covers every port and every pattern, so naming ArduPilot's pc would narrow nothing.
- **Ask:** accept.

**Q8. Transfers the bus would not make** (WO §5), each `unmodeled` and not passed to the stock model: a `START` or a `TXDR` write with `PE` = 0 (I5: the stock transfers on a disabled peripheral; p2003 "Peripheral disabled"); a `START` with `NBYTES` = 0 and `RELOAD` = 0 (I8b: the stock model **crashes Renode**; not in the text); a `START` while the previous transfer is open, by the stock model's `ISR` (no `TC`, `TCR` or `STOPF`) and with no `STOP` or `PE` clear since (I9; not in the text); `CR2`'s `NBYTES`, `SADD`, `RD_WRN`, `ADD10`, `HEAD10R` changed while a transfer is open (p2004–2006 "not allowed"). None is measured in either firmware (§2.1). **Ask:** accept.

**Q9. An address with no device (`NACKF`).** The stock model logs "Unknown slave", sets no `NACKF` and no `STOPF`, and the firmware times out; silicon NACKs and sends a STOP (p1975). Both firmwares do this on every boot (TMP117 at `0x48`, unmodelled; ArduPilot's 66 probes). Options:
- **(a) `unmodeled` at the `START`:** honest, and it refuses every generated board with the TMP117 and every ArduPilot boot: the WO's acceptance fails.
- **(b) Acknowledged by name, as the stock model behaves:** the `START` passes, `NACKF` stays 0; the ledger row names p1975 and the stock line. The guard writes the stock model's line, "Unknown slave at address N.", under its own name, so Aethelgard's `absent_address` (and its `i2c_timeout`, which rests on `TXIS` never rising) is unchanged; the owned instance's source-less copy stays beside it.
- **(c) `NACKF` modeled** (set, automatic STOP, `NACKIE` interrupt): silicon's behaviour, outside this WO's list, and it changes ArduPilot's control flow and Aethelgard's `i2c_timeout` observer. A later WO (the architecture already lists "I2C error flags, NACK" as a recorded gap).
- **Recommendation: (b) now, (c) as a roadmap row.** **Ask:** (a), (b) or (c).

**Q10. `ICR`'s reserved bits.** ChibiOS writes `ICR` = `0x2`, `0x4`, `0x40` (bits 1, 2, 6, reserved in `ICR`, p2012). Options: **(a)** acknowledged by name, those three bits only, as a write that changes nothing (`RUL-18` as amended); any other reserved bit `unmodeled`; **(b)** `unmodeled`: refuses every ArduPilot transfer. **Recommendation: (a).** **Ask:** accept.

**Q11. The allow-list** (`RUL-17`; the WO's `unmodeled` list).
- **Modeled:** `CR1` `PE`, `TXIE`, `RXIE`, `STOPIE`, `TCIE` (stock), `DNF`, `ANFOFF` (stored, Q5); `CR2` `SADD` (7-bit), `RD_WRN`, `START`, `STOP`, `NBYTES`, `AUTOEND` (stock); `TIMINGR` (stored); `ICR` `ADDRCF`, `STOPCF` (stock); `TXDR`, `RXDR`; `ISR` read (written `TXE` and `TXIS` only as the stock model takes them).
- **Acknowledged by name:** `CR1.NACKIE` and `ERRIE` at 1 (ArduPilot sets both: the twin raises no NACK and no error, so neither interrupt has anything to raise; with Q9 (b)); `ICR`'s `NACKCF`, `BERRCF`, `ARLOCF`, `OVRCF`, `PECCF`, `TIMOUTCF`, `ALERTCF` at 1 (clears of flags the twin never sets: a write that changes nothing); `ICR` bits 1, 2, 6 (Q10).
- **0 only, else `unmodeled`:** `CR1` `ADDRIE`, `TXDMAEN`, `RXDMAEN`, `SBC`, `NOSTRETCH`, `WUPEN`, `GCEN`, `SMBHEN`, `SMBDEN`, `ALERTEN`, `PECEN`; `CR2` `ADD10`, `HEAD10R`, `NACK`, `RELOAD`, `PECBYTE`; `OAR1`, `OAR2`, `TIMEOUTR` whole; `PECR` write; every reserved bit (but Q10's); a byte or half-word access other than the one the stock model widens (a byte to a register byte, judged on the word; a half-word is ignored by the stock model, so `unmodeled`), an offset with no register (0x2C–0x3FC). `RELOAD` is modeled by the stock model but used by no firmware, so it is held at 0 rather than audited.
- **Ask:** accept, or a different line for `RELOAD`.

**Q12. The eighth shard** (§5). Seven shards' jobs stay under 20 min at 1.53 (18.0–18.5 min); `ci`'s wall reaches 19.7–20.5 min. **Recommendation:** build the eighth shard first, in this WO (the fence allows it "only if the ruling adds the eighth shard"), re-planning `times.tsv` unchanged with eight. **Ask:** eight, or seven with the wall at 20.
