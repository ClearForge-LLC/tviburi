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
