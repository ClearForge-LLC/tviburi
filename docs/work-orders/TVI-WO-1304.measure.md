# TVI-WO-1304 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1304-flash-guard`, from `main` `d22dad8`.
**State:** predictions (§0), committed before any run.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/MTD/STM32H7_FlashController.cs`, sha256 `0e031ca2…`, and its base `STM32_FlashController.cs` (the `LockRegister`). No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock `STM32H7_FlashController.cs` and `STM32_FlashController.cs` in full; the stock `STM32H7_RCC.cs`'s `Pll1Frequecy`, `SysCkBaseFrequency`, `D1CFGR` dividers; the stock `STM32_SYSCFG.cs` (EXTICR only); the stock `pwr` stub (`stm32h7.repl` L728–745);
- Aethelgard `main` `29b3a97d`, read through git: `aethelgard-svd-hal/src/board.rs`'s `hse_api` (the ACR write between `PLL1RDY` and `SW`), `pwr_api` (`D3CR` = Scale 2) and `Flash::option_status` (`OPTSR_CUR` read once); `aethelgard-twin/src/platform.rs`'s `pwr_override` (a generated platform stores `VOS`; "a platform with `mcu.foreign_clock` keeps the stock stub");
- `spike/1150/data/writes-ap.tsv` and `queue-ap.tsv`'s `FLASH` rows (ArduPilot's 5 s boot: `ACR` `0x32` once; `KEYR1`/`KEYR2` 82 writes each; `CR2` 127 writes of 10 values `0x624`…`0x735`; `CCR2` 82; `SR2` 4; `CR1` 41 of `0x31`);
- RM0433 Rev 9 Table 17 (p160), §4.3.8–4.3.10 (pp. 158–165), `FLASH_ACR` (p201), `FLASH_KEYR1`/`OPTKEYR` (pp. 201–202), Table 13 (p150), "Clock management" (p173), `RCC_D1CFGR.HPRE` (p395), `PWR_D3CR` (p310), VOS0 (pp. 280–281).

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9)
- **Table 17 (p160)** gives, per voltage scale, the AXI interface clock each `LATENCY` covers, and the `WRHIGHFREQ` for each band: VOS3 0 WS to 45 MHz … 4 WS to 225; VOS2 0 WS to 55 … 3 WS to 225, 4 WS at 225; VOS1 0 WS to 70, 1 WS to 140, 2 WS to 185 (`WRHIGHFREQ` 01) and to 210 (10), 3 WS to 225; VOS0 as VOS1, plus 4 WS to 240. "The number of wait states (`LATENCY`) must be correctly programmed … according to the embedded flash memory AXI interface clock frequency (sys_ck) and the internal voltage range" (p159). The order of the two writes is p160–161 (raise `LATENCY` first; lower the clock first). Reset `ACR` = `0x37` (7 WS, `WRHIGHFREQ` 11; p201), which covers every row.
- **Which clock.** The flash chapter calls it `sys_ck`, "D1 domain bus clock (embedded flash memory interface clock)" (Table 13, p150), "here the AXI interface clock" (p173); the RCC chapter's `sys_ck` is the clock before `D1CPRE`, and `HPRE` divides `rcc_aclk` (p395: "rcc_hclk3 = rcc_aclk"). I read the flash's clock as `rcc_aclk` = `sys_ck` / `D1CPRE` / `HPRE`, and name the two meanings of `sys_ck` in the stop.
- **`WRHIGHFREQ`:** Table 17 lists 00, 01 and 10 only. `11` (the reset value) has no row. "No check is performed by hardware" (p201). Too tight a delay and "the non-volatile memory will not operate correctly"; too lax and "programming speed will not be optimal" (p164).
- **`VOS`** (`PWR_D3CR`, p310): `00` reserved (Scale 3 selected), `01` Scale 3 (reset), `10` Scale 2, `11` Scale 1. VOS0 is VOS1 with `SYSCFG_PWRCR.ODEN` (pp. 280–281).

### P2. The stock model, by source (each probed in §1)
- **`ACR`:** `LATENCY` and `WRHIGHFREQ` stored and read back, reset `0x37`; nothing reads them. Bits 6–31 reserved.
- **The bank 2 aliases** (`0x100` `ACR`, `0x108` `OPTKEYR`, `0x118` `OPTCR`, `0x11C` `OPTSR_CUR`, …): no register. A write is "Unhandled" and dropped, a read 0. **So `ACR` written through `0x100` changes nothing in the twin** (it does on silicon).
- **`KEYR1`/`KEYR2`:** two keys in order unlock; a wrong key, or any key while unlocked, locks until reset (`LockRegister`), as p163's note.
- **`CR2` while `LOCK2` = 1: written anyway.** No field checks the lock. `PSIZE2` is a tag (a write warns and is dropped; it reads `11`). `SER2`/`BER2`/`SNB2` stored; `START2` erases at once (128 KiB to `0xFF`, `EOP2` set); `FW2` sets `EOP2`; `PG2` installs a CPU memory hook.
- **`SR2`:** `BSY2`, `QW2`, `CRC_BUSY2` are tags that read 0, so a firmware waiting on them never waits. `WBNE2` computed. `WRPERR2`, `STRBERR2`, `RDPERR2`, `RDSERR2`, `CRCEND2` tags. `PGSERR2`, `INCERR2`, `OPERR2` read-only flags, set only through a `Trigger*` method or the hook.
- **`CCR2`:** clears `EOP2`, `PGSERR2`, `INCERR2`, `OPERR2`, the ECC flags; every other clear bit is a tag ("Unhandled" warning).
- **A program:** a store to bank 2's memory lands in the `MappedMemory` whatever `PG2` says. With `PG2` = 0 no hook is installed, so **no `PGSERR2`, and the data are written** (silicon rejects it and raises `PGSERR2`, p164). With `PG2` = 1 the hook counts 32 bytes, then `EOP2`. The data are stored as written, not ANDed into the cell (silicon programs 1 → 0 only, p161).
- **`OPTSR_CUR`:** `OPTSR_PRG`'s reset value `0x0406AAF0`, copied at reset: `BOR_LEV` (bits 3:2) `00`.
- **The form:** `public class STM32H7_FlashController : STM32_FlashController` (a `BasicDoubleWordPeripheral`), not sealed, with `[AllowedTranslations(ByteToDoubleWord | WordToDoubleWord)]`. A subclass that re-implements `IDoubleWordPeripheral`, `IWordPeripheral` and `IBytePeripheral` takes every access (`RUL-07`), as the SPI and timer guards. It is declared in the stock `stm32h7.repl` (L66–69, `flash1`, `flash2`, `-> nvic@4`), so the guard is cancel-and-restate (`RUL-09` (b)), with no RCC registration.

### P3. The generated firmware (14 boots, Aethelgard `29b3a97d`)
- **Every boot reads `FLASH_OPTSR_CUR` once** (`0x1C`), whole, and reads `BOR_LEV` `00` (the stock value).
- **The boards with an HSE** (`hse-crystal-16`, `hse-external-8`, `sentinel-02-bench-hse8`, held loosely on which others) write `PWR_D3CR` = `0x8000` (Scale 2) before any RCC write, then the HSE and PLL1, then **`ACR` once, read back, then `CFGR.SW` = PLL1**: the latency raised before the clock. At the `ACR` write the AXI clock is HSI 64 MHz and `VOS` is Scale 2; after the switch the AXI clock is the solved one, and `LATENCY` covers it at Scale 2 (Aethelgard checks its word against Table 17).
- **The other boards write no `ACR`:** they run from HSI at 64 MHz with the reset `0x37`. Each still writes `D3CR` = Scale 2.
- **No generated firmware writes `KEYR`, `CR`, `CCR`, `OPTKEYR`, `OPTCR` or `OPTSR_PRG`** ("the firmware writes nothing to the flash controller", `board.rs`).

### P4. ArduPilot (the fixture, ELF `a799dfb6`, 5 s; AP-m, ELF `2e686339`)
- **`ACR` is written once, `0x32` (`LATENCY` 2, `WRHIGHFREQ` 11),** and polled until it reads back, after `D1CFGR` and before `CFGR.SW` = PLL1. At the switch the AXI clock is 200 MHz (`sys_ck` 400 MHz, `HPRE` /2). `D3CR` is written Scale 1 before any of it, and **the stock stub drops it** (the fixture's platform has `mcu.foreign_clock`), so the twin reads `VOS` `00`.
- **By Table 17, `LATENCY` 2 covers 200 MHz at VOS1 or VOS0 only** (2 WS to 210 MHz with `WRHIGHFREQ` 10); at VOS2 or VOS3 it does not. So the rule at ArduPilot's switch is decidable only if `VOS` is known.
- **`WRHIGHFREQ` 11 is outside Table 17** at every frequency: a stop question if the rule is "Table 17's value".
- **Bank 2 is erased and programmed in the 5 s window:** sector erases (`SER2`, `SNB2` 6 and 7, then `START2`) and 32-byte flash-word programs (`PG2`, `PSIZE2` = 10), each after `KEYR2`'s two keys in order, each followed by a `CCR2` clear of everything (`0xFFFFFFFF`) and a lock. I expect it is ArduPilot's parameter storage initialising a blank area. **Every program and erase runs at 200 MHz with `WRHIGHFREQ` 11.** `CR2` is never written while `LOCK2` = 1, no store reaches bank 2 with `PG2` = 0, and `BSY2`/`QW2` are polled and read 0.
- **`SR2` is written `0xFFFFFFFF` four times** (a read-only register; the stock warns). Silicon ignores the write (p230, to be confirmed).
- **Bank 1:** `KEYR1` unlocked and `CR1` = `0x31` (`LOCK1`) written, 41 times each, with no program or erase.
- **`OPTSR_CUR`:** held loosely: ArduPilot reads it (the roadmap says both firmwares do). It writes no option byte.
- **AP-m** does the same as the fixture (the same ChibiOS and storage code; a different hwdef).

### P5. How the clock and the voltage scale can be known at a rule
- **The AXI clock:** from the RCC guard's owned stock RCC, through **one new public query on the RCC guard**: `CFGR.SWS`, `CR.HSIDIV`, the `hseFrequency`/`csiFrequency` properties, the stock `Pll1Frequecy` (`DIVM1`, `DIVN1`, `DIVP1`, the source), and `D1CFGR`'s `D1CPRE` and `HPRE` read from the owned instance and decoded by p394–395 (the stock decodes `1100`–`1111` as /32…/256, silicon /64…/512). A fractional PLL1 (`PLL1FRACEN`) is unknown. **At a clock switch** the RCC guard calls the flash guard with the clock its write leaves: that call is the "one query at a clock switch" the fence allows.
- **`VOS`:** on a generated platform, from the consumer's `pwr` override, read directly from the peripheral object (not through the bus, so no access-log line): `D3CR` bits 15:14. **On ArduPilot's foreign platform it cannot be known:** the stock stub drops the write and reads `00`. VOS0 cannot be known on either (the stock SYSCFG drops `PWRCR`). **I expect this to be a §7 stop:** ArduPilot's switch to 200 MHz is decidable only at a known VOS1.

### P6. Consumer observers
- **No generated observer changes:** every boot keeps `LATENCY` ahead of the clock, and no generated firmware programs the flash. `OPTSR_CUR` from a declared image changes what the firmware reads only if the image's `BOR_LEV` differs from `00`.
- **ArduPilot's recorded boot,** with the rule as Table 17 reads, would-refuses at its first erase on `WRHIGHFREQ` 11, and at its switch on the unknown `VOS`. Both are §7 questions.

### P7. Memory
- A prototype flash guard (a subclass re-implementing the three bus interfaces, no rule), cancelled and restated as `flashController` in every branch boot, costs under 5 MiB at the median over `main` in `1201`'s budget.
