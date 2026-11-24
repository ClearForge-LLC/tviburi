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

## 1. How it was measured (after §0 was committed at `ad4593a`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `29b3a97d` pins (`container/IMAGE`, fetched this session). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1304/` (`cap-gen/`, `cap-ap/`, `m1/`, `m2/`, `an/`, `budget/`, `pdf/`, `probe/`, and the scripts in `h/`), kept until the architect says.

| Run | What | How |
|---|---|---|
| **C** | the 14 generated boots and both ArduPilot captures | **reused from `TVI-WO-1303`'s §1.1** (`~/.cache/tvi-1303/cap-gen`, `cap-ap`, copied), captured at Aethelgard `1cb994c1` by 1302's `capture-gen.sh`, every verdict `pass`. Aethelgard `main` is now `29b3a97d` (`AET-WO-1610`), one commit later: `git diff --stat 1cb994c1 29b3a97d` touches no file of `aethelgard-svd-hal`, `aethelgard-twin` or the specifications, in `aethelgard-cli/src/main.rs` only `check_kicad`, the acknowledgements and the specification reader, and in `aethelgard-ir/src/lib.rs` only a new validation error (`VariantMissing`). The firmware and the platform each capture holds are therefore what `29b3a97d` emits (reasoned from the diff, not re-run). The ArduPilot captures are the committed fixture's (`a799dfb6`, Aethelgard `1c32c8ea`) and 1302's AP-m (`2e686339`) |
| **M1** | each boot replayed on `main`'s release (every guard recorded) with `sysbus LogPeripheralAccess` on `flashController`, `rcc`, `pwr` and `syscfg` (reads included), collapse off, and bank 2's eight sectors' first 32 bytes read before and after the run | `h/replay-f.sh`: 1303's copy of `tests/1301/replay-1301.sh` (the argv after `--`) with those peripherals logged. ArduPilot at 5 s. Decoded by `h/analyse-flash.py` into `an/` |
| **M2** | the form, and what a guard can read at a rule | a **probe** (`probe/ProbeSTM32H7_Flash.cs`, scratch only): a subclass of `STM32H7_FlashController` re-implementing the three bus interfaces, passing everything to the stock model, with two monitor methods that read the consumer's `pwr` (`D3CR`, `CSR1`), `SYSCFG_PWRCR` and the RCC registers from the C# objects, not through the bus. Cancelled and restated as `flashController` (`flash1`, `flash2`, `-> nvic@4`) in replays of `hse-crystal-16` and the fixture (`h/p-loop.sh`); and a probe firmware (`probe/fw/probe.rs`) on Tviburi's own board for the stock model's bank 2 behaviour |
| **B** | the one-file memory, by `1201`'s budget | `tests/1301/ci-memory.sh` from a scratch checkout whose `replay-1301.sh` restates the probe as `flashController` (moving the consumer's block onto it, §2.3) when the tree holds it; `main`'s tree against `main` + the probe, **11** boots a side |
| **RM** | Table 17 and Table 18 | read from the rendered PDF pages (p160, p164), since the text layer splits the 2 WS row; the rest of chapter 4 from the text, by a subagent with a page and line for each quote (`rm-flash.md`), spot-checked (p177 L7245, p182 L7437–7447, pp. 226–229) |

## 2. Results

### 2.1 The field table (M1, and for ArduPilot the probe's own trace)

**ArduPilot's FLASH writes are not in the access log.** Both foreign boots set `sysbus SetHookBeforePeripheralWrite sysbus.flashController …` (Aethelgard's `AETHELGARD-ERASE` watch on `START`, `MER` and `OPTSTART`), and a write hook displaces the access log's write lines (0 `WriteUInt32` lines; the reads, 989, all logged). The writes below for ArduPilot are the probe's (M2: it logs every access it takes; 423 writes on the fixture), whose 989 reads equal the log's. The generated boots set no such hook, and their writes are logged.

No access in any boot is at an alias offset (`0x1xx` of a common register), or narrower than 32 bits.

| Register (offset; RM0433 Rev 9 p) | Generated firmware (14 boots, Aethelgard `1cb994c1`/`29b3a97d`) | ArduPilot (fixture `a799dfb6` 5 s; AP-m `2e686339` 5 s) | The stock model (measured, M2) |
|---|---|---|---|
| `ACR` (`0x000`/`0x100`; p201) | **3 boots** (`hse-crystal-16`, `hse-external-8`, `sentinel-02-bench-hse8`): `0x11` (`LATENCY` 1, `WRHIGHFREQ` 01) once, read back, then `CFGR.SW` = PLL1. **11 boots** never touch it (reset `0x37`) | `0x32` (`LATENCY` 2, `WRHIGHFREQ` **11**) once at 3.77 µs (PC `0x081038E4`), polled once, then `CFGR.SW` = PLL1 | stored and read back; nothing reads it. **A write at the `0x100` alias is dropped** ("Unhandled write to offset 0x100"; `ACR` stays `0x37`) |
| `KEYR1` (`0x004`; p201), `KEYR2` (`0x104`; p225) | none | 41 (fixture) / 36 (AP-m) unlock pairs each bank, always key 1 then key 2, always while locked | two keys in order unlock; a wrong key, or a key while unlocked, locks until reset (measured). **No bus error** (silicon: "locks up … and generates a bus error", p182) |
| `CR1` (`0x00C`; p202) | none | `0x31` (`LOCK1`, `PSIZE1` read back) after every operation: no bank 1 program or erase | as `CR2` |
| `CR2` (`0x10C`; p226–229) | none | per operation: `0x624`/`0x724` (`SER2`, `PSIZE2` 10, `SNB2` 6/7), read back `0x634`/`0x734`, then `0x6B4`/`0x7B4` (`START2`): **4 sector erases** (6, 7, 6, 7 at 4.39, 6.64, 12.71, 14.98 ms); then **39 / 34 program windows** `0x736`/`0x636` (`PG2` with `SER2` still set), `0x734` (`PG2` cleared), `0x735`/`0x635` (`LOCK2`). **Never written while locked** | **written while `LOCK2` = 1** (measured: an erase ran with the bank locked, and `PG2` was set after a relock); `PSIZE2` a tag: **a write of 10 reads back 11**, so ArduPilot's read-modify-write starts each erase with `PSIZE2` = 11 (`0x6B4`, where silicon gives `0x6A4`); `START2` erases at once and sets `EOP2` |
| `SR1` (`0x010`), `SR2` (`0x110`; p230) | none | polled: `0`, `0x2` (`WBNE2`, while a flash word fills), `0x10000` (`EOP2`). **`SR2` written `0xFFFFFFFF` 4 times** | `BSY2`, `QW2` tags reading 0 (an erase is instantaneous); **a write to `SR2` sets `EOP2`** (measured: the field is read-write in the stock model; silicon's `SR2` is read-only, p230) |
| `CCR1` (`0x014`), `CCR2` (`0x114`; p233) | none | `0xFFFFFFFF` (every clear), 82 / 72 times on bank 2 | clears `EOP`, `PGSERR`, `INCERR`, `OPERR`, the ECC flags; every other clear bit a tag ("Unhandled", dropped) |
| `OPTSR_CUR` (`0x01C`; p212–214) | **every boot reads it once**: `0x0406AAFC` (`BOR_LEV` 11) in 11 boots, `0x0406AAF0` (`BOR_LEV` 00) in `motors-pwm`, `sentinel-02-bench`, `sentinel-02-bench-hse8` | **never read** (P4 wrong) | `OPTSR_PRG`'s value at the last `OPTSTART`, else its reset `0x0406AAF0` |
| `OPTKEYR`, `OPTCR`, `OPTSR_PRG` (`0x008`, `0x018`, `0x020`) | **written by the consumer's platform, not the firmware:** every platform (both foreign ones too) carries an untyped `flashController:` block whose `init add:` writes the two option keys, `OPTSR_PRG` = the declared word, `OPTCR.OPTSTART`, then a wrong key "to lock FLASH_OPTCR again" (`AET-WO-4230` §1.6). That is how the consumer declares `BOR_LEV` today | none | as the init sequence expects: `OPTSTART` copies `OPTSR_PRG` into `OPTSR_CUR` |
| bank 2's memory (`0x0810_0000`–`0x081F_FFFF`) | untouched (eight sectors' first 32 bytes equal before and after) | sectors 6 and 7 (`0x081C0000`, `0x081E0000`): all zero before the run, ArduPilot's storage header after (`F1FFFFFF625B6851FF…`) | **zero at power-on** (a `MappedMemory`; silicon's erased cell reads 1, p161); **a store with `PG2` = 0 lands and raises nothing** (silicon rejects it and raises `PGSERR2`, p164); a program stores the value as written (silicon only clears bits, p161: measured `0x0F0F0F0F` then `0xF0F0F0F0` reads `0xF0F0F0F0`) |

**Fields given a non-reset value, to acknowledge or model:** `ACR.LATENCY`, `ACR.WRHIGHFREQ` (11 by ArduPilot); `KEYR1`, `KEYR2`; `CR1.LOCK1`; `CR2.LOCK2`, `PG2`, `SER2`, `PSIZE2` (10), `START2`, `SNB2` (6, 7); `CCR1`/`CCR2` every clear bit (`0xFFFFFFFF`); `SR2` written (a read-only register). No generated firmware writes a flash register but `ACR`.

### 2.2 The wait-state table (RM0433 Rev 9 Table 17, p160, read from the rendered page)

| `LATENCY` | `WRHIGHFREQ` | VOS3 | VOS2 | VOS1 | VOS0 |
|---|---|---|---|---|---|
| 0 | 00 | [0; 45] MHz | [0; 55] | [0; 70] | [0; 70] |
| 1 | 01 | ]45; 90] | ]55; 110] | ]70; 140] | ]70; 140] |
| 2 | 01 | ]90; 135] | ]110; 165] | ]140; 185] | ]140; 185] |
| 2 | 10 | – | – | ]185; 210] | ]185; 210] |
| 3 | 10 | ]135; 180] | ]165; 225] | ]210; 225] | ]210; 225] |
| 4 | 10 | ]180; 225] | 225 | – | ]225; 240] |

- **The clock is the AXI clock**, `rcc_aclk` = `sys_ck` / `D1CPRE` / `HPRE` (p395: "rcc_hclk3 = rcc_aclk"; Table 17's footnote names `F_ACLK`). The flash chapter calls it `sys_ck` (Table 13 p150, p159, p173: "the microcontroller system clock (sys_ck), here the AXI interface clock"), which is not the RCC chapter's `sys_ck` (before `D1CPRE`). On both firmwares the two differ only on ArduPilot (`HPRE` /2: 400 MHz `sys_ck`, 200 MHz AXI). I read the column as the AXI clock.
- **The order** (p160–161): raise `LATENCY` (and `WRHIGHFREQ`) before raising the clock; lower the clock before lowering them. "No check is performed by hardware" (p201). The manual states no consequence of too few wait states: only that `LATENCY` "must be correctly programmed" (p159). **That too few fetch wrong data is from memory** (the WO's own grounds mark it so).
- **`WRHIGHFREQ`:** Table 17 uses 00, 01 and 10 only. **No page says what 11 means** (all ten mentions read: `rm-flash.md` §7); 11 is the reset value (`0x37`, p201). p164: "If timing constraints are too tight, the non-volatile memory will not operate correctly, if they are too lax, the programming speed will not be optimal."
- **`PSIZE`** (Table 18, p164): "There is no hardware limitation on programming parallelism." **No `PSIZE` value is an error**; changing it during an operation "might corrupt the flash memory content" (a caution, no flag).

### 2.3 The clock and the voltage scale at each write

| Boot | `D3CR` written | VOS the twin reads | `ACR` write: AXI then | Switch | AXI after; `LATENCY`; Table 17 needs |
|---|---|---|---|---|---|
| the 3 HSE boards | `0x8000` (Scale 2), first | Scale 2 (the consumer's `pwr` override: `0xA000`) | 64 MHz (HSI) | `SW` = PLL1 (64 MHz from HSE 16/8 MHz) | 64 MHz; 1; VOS2: 1 WS, `WRHIGHFREQ` 01. **Valid** |
| the other 11 | `0x8000`, first | Scale 2 | no write (`0x37`) | none (HSI 64 MHz; `sentinel-03` starts PLL1 and never switches) | 64 MHz; 7. Valid |
| ArduPilot (both) | `0xC000` (Scale 1), first | **`00`** (the stock stub: `D3CR` `0x2000`; `CSR1` `0x6000`, `ACTVOS` 01) | 32 MHz (HSI 64 / `HPRE` 2: `D1CFGR` = `0x48` written first) | `CFGR` = `0x8003` (PLL1 from HSI: `DIVM1` 4, `DIVN1` 50, `DIVP1` 2: 400 MHz) | **200 MHz; 2.** Needs 2 WS at VOS1/VOS0 (`WRHIGHFREQ` 10), 3 at VOS2, 4 at VOS3 |

Every ArduPilot erase and program runs after the switch: AXI 200 MHz, `LATENCY` 2, `WRHIGHFREQ` 11, VOS1 as written.

**How a guard can know them (M2, measured on the probe):**
- **The AXI clock:** read from the RCC guard's own object, not the bus, at an `ACR` write and after each RCC write: `CR` (`HSIDIV`), `CFGR` (`SWS`), `D1CFGR`, `PLLCKSELR`, `PLLCFGR` (`PLL1FRACEN`), `PLL1DIVR`, `PLL1FRACR`, and its public `HseFrequency`/`CsiFrequency`. The reads log nothing and record nothing (the probe's values: `hse-crystal-16` 64 MHz; the fixture 400/200 MHz, as the table). The guard decodes `D1CPRE`/`HPRE` by p394–395 itself (the stock model's dividers are wrong from `1100` up: /32… where silicon divides /64…; by the stock source, `STM32H7_RCC.cs` L242–245, not run, since neither firmware writes such a value).
- **The RCC guard's one change** is a notification: an event raised at the end of its `Pass` (one declaration, one call; prototype shape, not built), so an RCC write that passes tells the flash guard to re-read the clock. The flash guard pulls nothing else from it.
- **VOS on a generated platform:** `D3CR` bits 15:14 read from the `pwr` object directly (not the bus: no `pwr` access-log line, measured, so Aethelgard's `judge_pwr` sees nothing new).
- **VOS on ArduPilot's platform: not knowable.** The consumer keeps the stock stub for a platform with `mcu.foreign_clock` (`aethelgard-twin` `platform.rs` `pwr_override`), which drops the write and reads `VOS` `00` ("Reserved (Scale 3 selected)", p310) and `ACTVOS` Scale 3. A guard cannot tell the stub's `00` from a firmware that wrote `00` without a name (`RUL-10`).
- **VOS0 is not knowable anywhere:** the stock SYSCFG keeps only `EXTICR` and drops `PWRCR.ODEN` (p280–281), and reading it from the object logs "syscfg: Unhandled read from offset 0x2C" (measured): a new log line, so the guard must not read it.
- **A VOS write itself is unseen:** "the voltage scale lowered under a fast clock" (WO §5) happens at a `pwr` write, which no Tviburi guard takes.

### 2.4 The form (M2)

- **A subclass works.** `STM32H7_FlashController` is not sealed. The probe, cancelled (`flashController: @none`) and restated `as "flashController"` with `flash1`, `flash2` and `-> nvic@4`, loads; `peripherals` lists `flashController (ProbeSTM32H7_Flash)`; **its access-log lines are byte-identical to `main`'s, register names and stamps included**, on `hse-crystal-16` and on the fixture (`(AccessControl)`, `(OptionStatusCurrent)`, …). **The whole `hse-crystal-16` console is identical** to `main`'s: 0 of 23,381 lines differ, leaving out the probe's own lines, the echoed command line and the monitor's byte dumps (the boot block and the instruction count included). The consumer's write hook (`AETHELGARD-ERASE`) still fires on it: the same 4 lines.
- **The trap (`RUL-09` (b)) fails the load.** The consumer's untyped `flashController:` block, left on the cancelled entry, is `Error E29: The init section is only allowed for peripherals that are registered`; the platform does not load and Renode exits 0. Moved onto the guard's variable (the replay's job, as for `timerN: frequency:`), it runs on the guard: `OPTSR_CUR` reads the declared `0x0406AAFC`.
- **A guard's own records would fail the consumer's flash judge** (by Aethelgard's source, not run). Aethelgard reads the flash controller's log as accesses only: "from a logged source, every such line must be a whole access, or the judge … fails closed" (`aethelgard-twin` `serial.rs` `logged_by`; `aethelgard-cli` `renode.rs` `access_boots`). The records runtime logs the witness and the end at `INFO` under the guard's name (`model/Refusal.cs`; the roadmap's records-log-level row), so a guard on `flashController` makes every generated boot's `flash_controller_access` say the log "could not be read whole": `serial::access` returns `None` for a line with no `[cpu: …]` part, and `access_boots` then returns `None` for the boot. Only the witness falls in the judged window (it is logged at the platform's load; the end comes after `AETHELGARD-BOOT-BEGIN`). The RCC guard already does the same to `rcc`, which `judge_pwr` reads. This is the consumer's integration (Phase 2) and `Refusal.cs`'s owner's, not this WO's; it is named here because it is an observer the guard would change.

### 2.5 The stock model against silicon, by probe (M2, `probe/fw/probe.rs` on Tviburi's board)

| # | Probe | Stock (measured) | Silicon (RM0433 Rev 9) |
|---|---|---|---|
| F1 | a store to bank 2 with `PG2` = 0 | lands (`0x11111111` read back); `SR2` 0 | rejected, `PGSERR2` raised (p164) |
| F2 | `CR2` = `SER2` \| `SNB2` 7 \| `START2` with `LOCK2` = 1 | written; sector 7 erased | `CR2` fields "can be programmed only when LOCK2 is cleared" (pp. 226–229); writes "not allowed after reset" until unlocked (p182) |
| F3 | a program over a programmed word | stored as written (`0xF0F0F0F0`) | bits only cleared; "not recommended … may lead to an inconsistent ECC code" (p161) |
| F4 | `ACR` at `0x100` | dropped; `ACR` unchanged | the same register (p201: "0x000 or 0x100"; Table 20 p172) |
| F5 | `SR2` written `0xFFFFFFFF` | `EOP2` set | read-only (p230) |
| F6 | a key while unlocked, then `CR2.PG2` | relocked, **then `PG2` set anyway** | "remain locked until next system reset" (p163), with a bus error (p182) |
| F7 | `PSIZE2` = 10 | reads 11 | as written (p228) |
| F8 | an erase | instantaneous, `BSY2`/`QW2` never 1 | takes time (not in RM0433's chapter 4; the datasheet's figures, not read here) |

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | held; Table 17 from the rendered page. **Not predicted:** no page states what too few wait states do (§2.2), and Table 18 says no `PSIZE` is an error |
| P2 | held in every probed point (F1–F7). **Not predicted:** an `SR2` write sets `EOP2` (F5); `PSIZE2`'s tag changes ArduPilot's next `CR2` write |
| P3 | held: one `OPTSR_CUR` read per boot; `D3CR` Scale 2 first; `ACR` only on the 3 HSE boards, raised before the switch. **Not predicted:** the `BOR_LEV` the boots read comes from the consumer's own option-key sequence in the platform (§2.1) |
| P4 | `ACR`, clock, erases, programs, key order, no locked write, `SR2` written: held. **Wrong:** ArduPilot never reads `OPTSR_CUR`; and the access log shows none of its writes (the consumer's write hook) |
| P5 | held: AXI from the RCC guard's object, VOS from the `pwr` object on generated platforms, unknown on the foreign one, VOS0 nowhere |
| P6 | held for the generated firmware; **wrong in what changes an observer:** the guard's own `INFO` records (§2.4) and, on ArduPilot, `PSIZE2` served as written (`AETHELGARD-ERASE 10c 6b4` would read `6a4`) |
| P7 | held (§4) |

## 4. The one-file memory, by `1201`'s budget (B)

**A prototype figure, not the guard's:** the probe of §2.4 (a subclass, no rule, no table), restated as `flashController` in every branch boot with the consumer's block moved onto it (`peripherals`: `flashController (ProbeSTM32H7_Flash)`). `tests/1301/ci-memory.sh`, ArduPilot's 5 s boot, **11 a side**, interleaved, on Furnace (load 6–16, shared):

`MEDIANS main 986400 KiB, branch 986304 KiB, difference -96 KiB (-0.1 MiB)` — **PASS**: every boot counts, every branch boot under 1,572,864 KiB (highest 1,006,524), 16 witnesses of its own release each. Main's one outlier (1,095,180) is beside a median. The guard's rules and table will add to this; 1302 measured most of a guard's cost as compile garbage, which its collection at start already reclaims.

## 5. The stop (WO §1.1, §7), and the questions

**Which §7 conditions fire.** The wait-state table is in the held text (p160): no stop there. The budget passes (§4). Two fire, and both are in Q1–Q2:
- **"The clock or the voltage scale cannot be known at a rule without editing … Aethelgard's `pwr`":** VOS is unknowable on ArduPilot's platform (§2.3).
- **"Either firmware's boot would refuse … on a valid path":** ArduPilot's boot, with the rules as the WO reads them, would refuse at its switch (VOS unknown) and at each erase and program (`WRHIGHFREQ` 11).

The generated firmware refuses nowhere: every boot keeps `LATENCY` ahead of the clock at a known Scale 2.

**Q1. VOS where the twin cannot see it (ArduPilot's platform).** ArduPilot writes Scale 1, the stub reads `00`, and `LATENCY` 2 at 200 MHz is valid at VOS1/VOS0 and not at VOS2/VOS3 (Table 17).
- (a) **The WO's own rule** ("`unmodeled`: a mismatch the guard cannot decide, for example with the voltage scale unknown"): a mismatch that depends on an unknown VOS is `unmodeled`. ArduPilot's recorded boot then would-refuses once, at its switch (PC `0x081038F8`). That is honest, and the acceptance's "no flash would-refuse outside the acknowledged table" fails unless the row is acknowledged.
- (b) **Recommended, an amendment of the WO's text:**
  - Decide at the most permissive scale the twin can know, VOS1. A `predicted-fault` only where `LATENCY` is short even at VOS1, since silicon faults at every scale then.
  - The case "short at some scale, enough at VOS1" with VOS unknown is an acknowledged ledger row: "VOS unknown: judged at VOS1".
  - A `pwr` that reads `00` counts as unknown, never as Scale 3 (no name used). An AXI clock above 225 MHz is `unmodeled` (VOS0's `ODEN` is unknowable).
- (c) A row for Aethelgard: emit its `pwr` override on foreign platforms too (`AET`'s change, not this WO's), after which (b)'s row stops firing.
- **A VOS write itself is unseen** (WO §5's "voltage scale lowered under a fast clock"): a row for whoever guards PWR. The flash guard checks VOS at its own events only (an `ACR` write, an RCC write, an erase or program start).

**Q2. `WRHIGHFREQ` = 11.** ArduPilot (ChibiOS) writes 11 at 200 MHz, and Table 17 says 10. No page defines 11, and 11 is the reset value.
- (a) **Recommended:** the rule is one-sided, as `LATENCY`'s. A `predicted-fault` when `WRHIGHFREQ` is numerically below Table 17's value for the clock at a program or erase start ("too tight … will not operate correctly", p164). 11 passes as the reset's own setting, with a ledger row saying that 11 ≥ 10 meaning "at least as lax" is inferred from the reset value, not stated.
- (b) Any value other than the table's is `unmodeled`: ArduPilot would-refuses at every erase and program (4 + 39).

**Q3. `OPTSR_CUR` and the declared image.** **The word the consumer declares is Renode's reset value with `BOR_LEV` set** (`0x0406AAFC`, or `0x0406AAF0`), not silicon's. Aethelgard's own ledger row names three words at `BOR_LEV` 00: the bench's `0x1BC6_AAF0` (NUCLEO-H753ZI rev V), Table 21's "`000XX000000XX110` `1010101011110000`" (p177, with four bits X) and Renode's `0x0406_AAF0`. Renode's sets bit 26, which is reserved, "must be kept at reset value" (p212–214), and 0 in Table 21 and on the bench. Under (a), the property serves whatever word the consumer declares; a translated block keeps Renode's bits above `BOR_LEV`. Which word the consumer should declare is its choice (Phase 2), not the guard's. The consumer already declares the word, through the stock register interface: its platform's untyped `flashController:` block runs the option-key sequence, `OPTSR_PRG` and `OPTSTART` at `init` (§2.1). Cancelling the stock entry turns that block into E29, and no platform loads (§2.4). Options:
- (a) **Recommended:**
  - The guard takes the image as a restatement property (`optionBytes`, the whole `OPTSR_CUR` word, never a name or a path). `OPTSR_CUR` and its `0x11C` alias are served from it.
  - Every option-byte register write is `unmodeled` (the allow-list; option-byte programming is a row), and so is the consumer's init sequence.
  - The replay translates the consumer's block (the `OPTSR_PRG` word it writes) into the property, as it moves `timerN: frequency:`.
  - The consumer's emitter changes in Phase 2 (a row): it must move or replace the block in any case, or E29.
- (b) The guard accepts the consumer's init sequence as the declaration only before the CPU's first instruction. This is narrower, but it is option-byte programming under another name.
- **An undeclared read:** (a) recommends `unmodeled`, refused at the read. Every generated boot reads it, so every consumer platform must declare, which it already does in substance. The other choice, serving the stock `0x0406AAF0`, is the invented value the WO excludes.
- **`OPTSR_PRG` forgery** (WO §5): with (a), no write reaches `OPTSR_CUR`.

**Q4. Bank 2: what silicon refuses that the guard can decide.** From RM0433 (`rm-flash.md`), not from memory:
- **Decidable, `predicted-fault`:**
  - a `CR1`/`CR2` write that changes a field while locked (pp. 226–229 "can be programmed only when LOCK2 is cleared"; p182);
  - a wrong key, or a key written while unlocked (p182 "locks up … and generates a bus error"; p163);
  - `WRHIGHFREQ` short for the clock at `START2`, `FW2` or `PG2` set (Q2).

  ArduPilot does none of these.
- **Not an error on silicon:**
  - **`PSIZE`** (Table 18 p164: "no hardware limitation"). The WO's "wrong `PSIZE`" has no page, so I propose no `PSIZE` rule: the guard serves `PSIZE2` as written (F7, `RUL-17`).
  - **`SR2` written** (read-only, p230): ignored as silicon ignores it (no record, `RUL-18` as amended), and `EOP2` no longer set by it (F5).
  - **`PG2` set while `SER2` is still set** (ArduPilot, 39 times): the RM states nothing (`rm-flash.md` (g)), so I propose an acknowledged row, not a refusal.
- **"A write while `BSY`/`QW`":** the RM states no error for it (`rm-flash.md` (b), (c)). In the twin an erase is instantaneous (F8), so `BSY2`/`QW2` are never 1 and the case never arises. I propose a ledger row (erase and program time not modeled; `BSY2`/`QW2` read 0), no rule.
- **Undecidable here, a question:**
  - **A store to bank 2 with `PG2` = 0** (silicon rejects it and raises `PGSERR2`, p164; the twin stores it, F1).
  - **A program over a programmed word** (F3).

  The guard sees no store into the memory: the stock model installs the CPU's one memory-access hook only while `PG` is set (by the stock source, `HandleProgramWriteEnableChange`, not run). Options:
  - (a) A row now. **Recommended.** Neither firmware does it.
  - (b) The guard keeps a memory-access hook installed always. This costs speed, and it displaces any other user of the CPU's single hook (the stock's own `PG` path sets and clears it).
  - (c) Restate `flashBank2` as a guarded memory. That is beyond the fence.
- **Bank 2 starts zeroed** (a `MappedMemory`); silicon's erased flash reads `0xFF` (p161). ArduPilot erases its two storage sectors in the window. Whether it would on a virgin part (all `0xFF`) is from memory (AP_FlashStorage erases both sectors when neither holds a valid header), not measured. I propose a row for the consumer's platform, which declares the memory.

**Q5. What the guard changes for the consumer, by board, if built as recommended:**
- **The 14 generated boots:**
  - The flash access lines are unchanged (no rule fires; byte-identical form).
  - `OPTSR_CUR` reads the same word, if the replay's translation gives the property the word the platform declares.
  - **But** the guard's `INFO` witness and end on `flashController` fail Aethelgard's `flash_controller_access` judge ("could not be read whole") on every boot. That is §2.4: the records' log level, `Refusal.cs`'s owner's and the consumer's row, as it already is for `rcc`. I cannot change it in this WO (`Refusal.cs` is fenced).
- **ArduPilot (measured, M2: the probe serving `PSIZE` as written and dropping `SR1`/`SR2` writes, against the stock, on the fixture):**
  - **The path does not change:** the same 423 writes and 989 reads at the same offsets, in the same order; the same sector 6 and 7 contents after the run; the same boot block.
  - **`CR2` differs in `PSIZE2` only:** 123 writes and 163 reads (`0x624`, not `0x634`, read back; `0x6A4`, not `0x6B4`, to start an erase). So **Aethelgard's four `AETHELGARD-ERASE 10c 6b4`/`7b4` lines read `6a4`/`7a4`**, the silicon value (p228).
  - **Four `SR2` reads change** from `0x10000` to 0: the polls right after ArduPilot's `SR2` write, which no longer sets `EOP2`.
  - Each difference is cited. Is that acceptable as "cited and named", or should `PSIZE2` stay the stock's (an acknowledged `stock-wrong` row) until the consumer's erase witness is coordinated?

**Q6. The fence.**
- **The RCC guard's one change:** an event raised after a passed write (§2.3), and nothing else. The AXI clock is computed in the flash guard from the RCC guard's registers, read through its own object. Is that inside "one query at a clock switch"?
- **The replays and the budget harness** (`tests/1301/replay-1301.sh`) must cancel and restate `flashController` and move the consumer's block (E29), as they do for the timers. That is in the fence (`tests/`).

**Q7. A known VOS on Tviburi's own board.** The planted firmwares run on `model/tviburi-nucleo-h753zi.repl`, which keeps the stock `pwr` stub, so VOS is unknown there too: no planted case can name a scale or exercise VOS2/VOS3 (the acceptance's "naming the clock, the latency, the voltage scale"). **Ask:** may the tests give their platform a VOS-storing `pwr` (the consumer's `pwr_override` script, restated in the test's platform by `tests/`, as `PLATFORM_SED` already edits it), with the board file left as it is? Or may the board file itself carry it (`model/`, the restatement)? Without one, the planted cases run at the "VOS unknown" rule only.

**Rows, outside this WO:**
- Aethelgard's foreign platforms keep the stock `pwr` stub (Q1 (c)).
- The consumer's option-byte declaration moves to the property (Q3).
- The records' `INFO` level on a logged source (Q5).
- Aethelgard's flash write hook displaces the access log's write lines on ArduPilot boots, so a flash write there is seen only through the hook's `START`/`MER`/`OPTSTART` filter (a finding for Aethelgard's `flash_controller_access`, which it does not apply to foreign firmware).
- Bank 2's power-on content (Q4).
- Erase and program time (F8).
