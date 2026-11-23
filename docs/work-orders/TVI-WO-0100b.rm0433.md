# TVI-WO-0100b §1.1 — SPI's field table, measured against both firmwares, and the truth-table rows at head

**Status:** measured 2026-11-23, at the §1.1 stop. **No model, tool, test or firmware code is changed on this branch yet.** §4 lists the fields the architect is asked to acknowledge by name, or to rule refused.
**Branch head measured:** `dc55f98` (`main`). All page citations are ST RM0433 Rev 9, chapter 50, unless they name DS12117 Rev 10.

## 1. Sources and how each was measured

| What | Identity | How |
|---|---|---|
| RM0433 Rev 9 | sha256 `672a9433…26b6a0` (matches `architecture.md` §2.2) | The held text extract (`rm0433-rev9.txt`) was split at its page footers (`NNNN/3329`) to find pages. Then pages 2155–2234 were extracted from the PDF itself with Ghostscript 10.05.1 `txtwrite`, in the pinned image with `--network none`, and every quote here was matched against the PDF page it cites (§7). |
| DS12117 Rev 10 | sha256 `607b9ead…84c032`, held at `~/.cache/aet-held/ds12117-rev10.pdf` | The same Ghostscript run, all 357 pages; Table 9 "Alternate functions", pp89–100 |
| The stock SPI model | `STM32H7_SPI.cs`, sha256 `bc3f1348…`, renode-infrastructure `066a7f13` | Six copies on Furnace agree. `TVI-WO-1100` (`queue.md` §1) measured that `src/Emulator/Peripherals` at `066a7f13` is identical to the tree that built the pinned `Infrastructure.dll` (`cd4b002a` plus three NVIC patches). Line numbers below are that file's. The behaviours that matter were then measured on the binary (§1.3) |
| Image and Renode | `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…8d1b58`, Renode `1.17.0+20260907gitf1dd1b4af.aet1605` | Aethelgard `main`'s `container/IMAGE` at `7dec1d7b`. Every container ran under `~/bin/capped-run`, `nice -n 10`, one at a time; every run with `--network none` |
| The generated firmware | `spike/1100/data/writes.tsv` (`TVI-WO-1100`, Aethelgard `f42a0c88`), 13 boards | Its SPI rows: `spi1` and `spi2`, 11 boards. **The file holds writes only**, so it cannot show a read-modify-write. Aethelgard's generator writes `CR1`, `CFG1` and `CFG2` whole, from the rule's words (`crates/aethelgard-cli/src/ledger.rs` L1236 at `7dec1d7b`) |
| ArduPilot | the held ELF, `~/.cache/aet-4240v/ardupilot/arducopter.elf`, sha256 `a799dfb6…dab1ef`: CI's `generated-sentinel-02` artifact (Aethelgard `AET-WO-4400e.FEEDBACK.md` L109) | **`TVI-WO-1150` (v) is not committed** (`1150`'s branch is at `main`), so this is an SPI and GPIO access log taken with `spike/1100/harness/`'s method. §1.1 |

### 1.1 ArduPilot's access log

- **The run.** Aethelgard `7dec1d7b` exported with `git archive`; the CLI and `aethelgard-bridge` built in the image (the only step with the network). Then `ci/foreign-judge.sh`'s positive case: `generate --spec crates/aethelgard-cli/tests/fixtures/sentinel-02-own.json`, then `simulate --spec <same> --platform <generated>/board/sentinel-02-own.repl --firmware arducopter.elf --foreign 5000ms`. **The verdict: exit 0, `pass`, judge `foreign`**, the ELF's sha256 in it, 87,993,253 instructions, 5.0 s of virtual time. Its findings are two `info` rows (locked DMA and I2C1 accesses; U3 has no model).
- **The capture.** `spike/1100/harness/capture.sh`, copied out of `spike/` and changed to run `simulate` instead of `verify`, captured Renode's argv (sha256 `77524385…`). `replay.sh`, copied likewise, replayed it with `sysbus LogPeripheralAccess` on every `STM32H7_SPI` instance and every GPIO port. Neither copy is committed: `spike/` is outside this work order's fence. They are in `~/.cache/tvi-0100b/ap/harness/`.
- **Two changes to the replayed argv, both needed for a complete log:**
  - The CLI's own `SetHookBeforePeripheralWrite` on `spi1` and `spi2` (its `AETHELGARD-CFG2` line) **replaces** the write hook `LogPeripheralAccess` installs. With it in place the log held no SPI writes. The replay removes those two commands. The instruction count and final PC are unchanged (87,993,253; `0x08061886`), so the replay is the verdict's run.
  - `collapse-repeated-log-entries` is set to false, so that identical accesses are not merged.
- **The log** (sha256 `15c03b5a…`): 105,781 SPI accesses. A second replay, with Python hooks in place of `LogPeripheralAccess`, gave the identical (instance, R/W, offset, value, PC) sequence, row for row. PCs are resolved against the ELF's symbol table.
- **Instances.** The platform has `spi1` (`0x40013000`), `spi2` (`0x40003800`) and the stock `spi4`. ArduPilot uses `spi1` (1,500 accesses) and `spi2` (104,281); `spi4` is never touched. No access reached an SPI address with no peripheral.

**ArduPilot's SPI start, as measured** (`spi1`, the first start; `spi2`'s is the same with `CFG1` = `0x5000C007`). Every row is a 32-bit access unless marked.

| # | Access | Value | Function |
|---|---|---|---|
| 1 | R `CR1` | `0x0` | `spi_lld_start+0xc0` |
| 2 | W `CR1` | `0x0` (`CR1 &= ~SPE`: read-modify-write of row 1) | `spi_lld_start+0xc6` |
| 3 | W `CR1` | `0x100` (`MASRX`), **a constant, written whole** (`MOV.W r2,#0x100` at `0x8100e84`) | `spi_lld_start+0xcc` |
| 4 | W `CR2` | `0x0` | `spi_lld_start+0xd0` |
| 5 | W `CFG1` | `0x2000C007` (`MBR`=010, `TXDMAEN`, `RXDMAEN`, `CRCSIZE`=00000, `DSIZE`=00111) | `spi_lld_start+0xde` |
| 6 | W `CFG2` | `0x20400000` (`SSOE`, `MASTER`; `SSM`=0) | `spi_lld_start+0xee` |
| 7 | W `IER` | `0x40` (`OVRIE`) | `spi_lld_start+0xf2` |
| 8 | W `IFCR` | `0xFFFFFFFF` | `spi_lld_start+0xf8` |
| 9 | R `CR1` | `0x0` (stock: `MASRX` is a tag and reads 0; silicon reads `0x100`) | `spi_lld_start+0xfa` |
| 10 | W `CR1` | `0x1` (`CR1 \|= SPE`: read-modify-write of row 9, `LDR`/`ORR #1`/`STR` at `0x8100eb6`–`ebc`; on silicon `0x101`) | `spi_lld_start+0x100` |
| 11–12 | R `CR1`; W `IFCR` `0xFFFFFFFF` | | `spi_lld_wait_complete` |
| 13–18 | W `TXDR` (8-bit) ×2; R/W `CR1` → `0x201` (`CSTART`); R `RXDR` (8-bit) ×2 | | `spi_lld_exchange` |
| 19–20 | R `CR1`; W `CR1` `0x601` (`CSUSP`) | | `spi_lld_serve_dma_rx_interrupt` |
| stop | R `CR1`; W `CR1` `0x0` ×2; W `CR2`, `CFG1`, `CFG2`, `IER` = `0x0` | | `spi_lld_stop` |

- **Read-modify-write,** from the log and the instructions (Thumb-2, decoded by hand from the ELF at `0x8100e7c`–`0x8100ebc`): `CR1` is read twice in `spi_lld_start`, and both `SPE` changes are read-modify-writes (`BIC #1`, `ORR #1`). `MASRX` is a constant written whole. `CFG1` and `CFG2` are computed from the driver's configuration (`LDR r2,[r0,#0x14]`), not from a register read. `CR2`, `CFG1`, `CFG2`, `IER` and `IFCR` are never read. `SR`, `CRCPOLY`, `UDRDR` and `I2SCFGR` are never accessed. Every other `CR1` write (`spi_lld_exchange`, `spi_lld_serve_dma_rx_interrupt`, `spi_lld_stop`) follows a `CR1` read in the same function; those were not decoded.
- **Row 9 is a stale-value route the stock model already takes.** On silicon `CR1` reads `0x100` there, so ChibiOS would write `0x101`. The stock model reads `MASRX` as 0, so the twin's word is `0x1`. Whatever is ruled for `MASRX` (§4), a guard that allows it must serve it from its copy on read (`RUL-17`).
- **No write to `CFG1`, `CFG2`, `CR2`, `CRCPOLY`, `UDRDR` or `I2SCFGR` happens while `SPE` = 1, in either firmware, and neither firmware ever sets `IOLOCK`** (measured over every SPI write of both logs).
- **The generated firmware's SPI start** (11 boards, `spi1` and `spi2`): `CR1` = `0x1000` (`SSI`), `CFG1` = `0x10070007` or `0x40070007` (`MBR` = 001 or 100), `CFG2` = `0x84400000` (`AFCNTR`, `SSM`, `MASTER`), `CR2` = `TSIZE`, `CR1` = `0x1001`, then `0x1201` (`CSTART`); 8-bit `TXDR` writes; `IFCR` = `0x8` (`EOTC`); `CR1` = `0x1000`.

### 1.2 (1)'s routing question: does any pin carry `SPIx_NSS` by its alternate function?

**No, on either firmware's platform.**
- **The NSS pins** (DS12117 Rev 10 Table 9): `SPI1_NSS` is PA4, PA15 and PG10 at AF5 (pp89, 90, 96). The pins that carry any `SPIx_NSS` are PA4, PA11, PA15, PB4, PB9, PB12, PE4, PE11, PF6, PG8, PG10, PH5, PI0 and PK1 (pp89–100). **The conclusion below rests on pin identity:** the AF columns of the other instances' cells were taken from each page's header by position, which misplaces some (it gives AF2 for PF6 and AF0 for PH5), so they are not stated.
- **ArduPilot**, from the final and every intermediate `MODER`/`AFRx` state in the run: PA5–PA7 and PB13–PB15 at AF5 (SPI1 and SPI2 SCK, MISO, MOSI), PA9 and PA10 at AF7, PB6 and PB7 at AF4. No NSS pin is in alternate-function mode; how the chip selects are driven was not decoded.
- **The generated firmware**, per board, from `writes.tsv`: AF5 on PA5–PA7, PA12, PB5, PB13–PB15; AF7, AF4 and AF1 elsewhere. Two NSS-capable pins appear, PA11 (motor boards) and PE11 (`sentinel-03`), both at AF1, which is `TIM1_CH4` and `TIM1_CH2` (p89, p94). No pin carries NSS.
- **What the manual says of exactly this case:** "Even if SPI AF is not applied to the NSS pin (it can be used as a standard GPIO then), SSOE=1 setting should be kept anyway to assure default NSS input level and prevent any mode fault evaluation" (p2166). With NSS unrouted, `SSOE` = 1 has no pin to drive, and its one remaining effect, preventing the mode fault, is what (1) models. **Whether that makes `SSOE` = 1 modeled is the architect's (§4, A2).** The twin cannot see routing itself: GPIO is not guarded (`RUL-13`'s `13NN`).

### 1.3 The stock model, measured on the pinned binary

One run of the stock `STM32H7_SPI` at `spi1` (each register written all-ones after a reset, then read back):

| Register | After reset | RM0433 reset | After all-ones (`SPE` kept 0) |
|---|---|---|---|
| `CR1` | `0x0` | `0x0` (p2215) | `0x0` |
| `CR2` | `0x0` | `0x0` (p2217) | `0x0000FFFF` (`TSIZE` stored; `TSER` dropped) |
| `CFG1` | **`0x0`** | **`0x00070007`** (p2218) | `0x0000C01F` (`DSIZE`, `RXDMAEN`, `TXDMAEN` stored) |
| `CFG2` | **`0x00400000`** | `0x0` (p2221) | `0x00C00000` (`MASTER` always 1; `LSBFRST` stored) |
| `IER` | `0x0` | `0x0` | `0x0000000B` (`RXPIE`, `TXPIE`, `EOTIE`) |
| `SR` | `0x00001002` | `0x00001002` (p2224) | unchanged |
| `IFCR` | `0x0` | `0x0` | `0x00000800` (`SUSPC` reads back; on silicon `IFCR` is write-only) |
| `CRCPOLY` | **`0x0`** | **`0x107`** (p2228) | `0x0` (no register: write dropped, "Unhandled read") |
| `UDRDR`, `I2SCFGR` | `0x0` | `0x0` | `0x0` |

- **`IOLOCK` is inverted.** A write of `IOLOCK` with `SPE` = 0 logs "Attempted to set IOLOCK while peripheral is enabled" and does not latch. With `SPE` = 1, in the same write or after, it latches. It then survives `SPE` 1→0 (`0x10000` read back). Silicon does the opposite on every count (p2215).
- **One `TXDR` access is one frame.** With `DSIZE` = 7, a 16-bit write and a 32-bit write each count one packet (`SR.CTSIZE` steps by 1, from `0xFFFF` to `0xFFFE`), where silicon packs 2 and 4 frames (p2227). Both firmwares write `TXDR` 8-bit only.
- **`LSBFRST` is stored and used, but as byte order** (L352), not bit order (p2222). `RUL-17`'s reason calls it a tag (FEEDBACK).

## 2. The field table (`RUL-17`), every field of every SPI register (Table 429, p2233)

**Classes.** *guard*: modeled by the guard (copies and rules). *stock*: implemented by the stock model, inherited and unaudited (invariant 2); in the allow-list only once acknowledged by name. *tag*: the stock model drops the written value and reads 0. *reserved*: "must be kept at reset value". *undefined*: the stock model defines no register at the offset.
**"Behaves as"** is the value the model's behaviour corresponds to (`RUL-17`), compared with RM0433's reset, not with 0.
**The firmware columns** list every value of the field written by the generated firmware (11 boards, `spi1` and `spi2`) and by ArduPilot (`spi1` and `spi2`). **Bold** marks a field §4 asks about.
**As data:** this table was rendered from a field list and a classification list (scratch: `~/.cache/tvi-0100b/fields.tsv`, `gen/annot.tsv`) joined to the two logs; the build keeps it as data keyed by these names (`RUL-16`), in one place, so `TVI-WO-1200`'s ledger can emit it.

| Register.field | Bits | Reset | Page | Stock (`066a7f13`) | Class | Behaves as | Generated writes | ArduPilot writes | Allow-list proposed, before §4's rulings |
|---|---|---|---|---|---|---|---|---|---|
| `SPI_CR1.Res31_17` | 31:17 | 0x0 | 2215 | reserved L143 | reserved | 0 (stock reserved bits) | 0x0 | 0x0 | 0 only |
| `SPI_CR1.IOLOCK` | 16 | 0x0 | 2215 | flag L133-142, set only while SPE=1 (inverted against p2215); read from iolockValue | guard | the guard's latch (§3.2); stock latches inversely (L133) | 0 | 0 | 0, 1 (guard) |
| `SPI_CR1.TCRCINI` | 15 | 0x0 | 2215 | tag L132 | tag | 0 (no CRC) | 0 | 0 | 0 only |
| `SPI_CR1.RCRCINI` | 14 | 0x0 | 2215 | tag L131 | tag | 0 (no CRC) | 0 | 0 | 0 only |
| `SPI_CR1.CRC33_17` | 13 | 0x0 | 2215 | tag L130 | tag | 0 (no CRC) | 0 | 0 | 0 only |
| `SPI_CR1.SSI` | 12 | 0x0 | 2215 | tag L129 | guard | the guard's copy, served on read | 1 | 0 | 0, 1 (guard) |
| `SPI_CR1.HDDIR` | 11 | 0x0 | 2215 | tag L128 | tag | 0 | 0 | 0 | 0 only |
| `SPI_CR1.CSUSP` | 10 | 0x0 | 2216 | flag L116, implemented (EndTransfer, SUSP) | stock | implemented (L116) | 0 | 0, 1 | **ack: inherited** |
| `SPI_CR1.CSTART` | 9 | 0x0 | 2216 | flag L108, implemented (TryTransmitData) | stock | implemented (L108) | 0, 1 | 0, 1 | **ack: inherited** |
| `SPI_CR1.MASRX` | 8 | 0x0 | 2217 | tag L107 | tag | 0; the stock FIFO never fills, so it never overruns or suspends | 0 | 0, 1 | 0 only — **ArduPilot writes 1** |
| `SPI_CR1.Res7_1` | 7:1 | 0x0 | 2217 | reserved L106 | reserved | 0 | 0x0 | 0x0 | 0 only |
| `SPI_CR1.SPE` | 0 | 0x0 | 2217 | flag L92, implemented (FIFO flush; CanWriteToRegister L323) | stock + guard | implemented (L92); the enable is the mode-fault rule's | 0, 1 | 0, 1 | **ack: inherited**; guard on 1 |
| `SPI_CR2.TSER` | 31:16 | 0x0 | 2218 | tag L149 | tag | 0 | 0x0 | 0x0 | 0 only |
| `SPI_CR2.TSIZE` | 15:0 | 0x0 | 2218 | value L146, implemented (EOT count) | stock | implemented (L146; 0 is endless, L376) | 0x2, 0x4, 0x7 | 0x0 | **ack: inherited** |
| `SPI_CFG1.Res31` | 31 | 0x0 | 2218 | reserved L172 | reserved | 0 | 0 | 0 | 0 only |
| `SPI_CFG1.MBR` | 30:28 | 0x0 | 2218 | tag L169 | tag | no clock: a transfer completes in zero virtual time | 001, 100 | 000, 010, 101 | 000 only — **both write others** |
| `SPI_CFG1.Res27_23` | 27:23 | 0x0 | 2218 | reserved L168 | reserved | 0 | 00000 | 00000 | 0 only |
| `SPI_CFG1.CRCEN` | 22 | 0x0 | 2218 | tag L167 | tag | 0 (no CRC) | 0 | 0 | 0 only |
| `SPI_CFG1.Res21` | 21 | 0x0 | 2218 | reserved L166 | reserved | 0 | 0 | 0 | 0 only |
| `SPI_CFG1.CRCSIZE` | 20:16 | 0x7 | 2219 | tag L165 | tag | 00111, the reset (p2219: "must be kept at its default setting" while CRCEN=0); reads 00000 (measured) | 00111 | 00000 | 00111 only — **ArduPilot writes 00000 (reserved)** |
| `SPI_CFG1.TXDMAEN` | 15 | 0x0 | 2219 | flag L164, stored only | stock | stored only (L164); the DMA moves TXDR on its own request | 0 | 0, 1 | **ack: inherited** |
| `SPI_CFG1.RXDMAEN` | 14 | 0x0 | 2219 | flag L162, implemented (DMARecieve blink) | stock | implemented (L162, L366) | 0 | 0, 1 | **ack: inherited** |
| `SPI_CFG1.Res13` | 13 | 0x0 | 2219 | reserved L161 | reserved | 0 | 0 | 0 | 0 only |
| `SPI_CFG1.UDRDET` | 12:11 | 0x0 | 2219 | tag L160 | tag | 00 (slave only) | 00 | 00 | 00 only |
| `SPI_CFG1.UDRCFG` | 10:9 | 0x0 | 2219 | tag L159 | tag | 00 (slave only) | 00 | 00 | 00 only |
| `SPI_CFG1.FTHLV` | 8:5 | 0x0 | 2220 | tag L153 | tag | 0000: one frame per packet (each access is one frame) | 0000 | 0000 | 0000 only |
| `SPI_CFG1.DSIZE` | 4:0 | 0x7 | 2220 | value L152, implemented as whole bytes (DSIZE/8+1, L350) | stock | whole bytes only (L350): 00111, 01111, 10111, 11111; reads 00000 after reset (measured; silicon 00111) | 00111 | 00000, 00111 | **ack: inherited, for 00111** — **ArduPilot writes 00000 at stop** |
| `SPI_CFG2.AFCNTR` | 31 | 0x0 | 2221 | tag L203 | tag | 0: the model has no pins | 1 | 0 | 0 only — **generated writes 1** |
| `SPI_CFG2.SSOM` | 30 | 0x0 | 2221 | tag L202 | tag | 0 | 0 | 0 | 0 only |
| `SPI_CFG2.SSOE` | 29 | 0x0 | 2221 | tag L201 | tag + guard | mode-fault side: guard (§3.1); the NSS output: none | 0 | 0, 1 | 0 only — **ArduPilot writes 1** |
| `SPI_CFG2.SSIOP` | 28 | 0x0 | 2221 | tag L200 | tag + guard | guard copy; 1 refuses at the enable (T3) | 0 | 0 | 0, 1 (guard) |
| `SPI_CFG2.Res27` | 27 | 0x0 | 2221 | reserved L199 | reserved | 0 | 0 | 0 | 0 only |
| `SPI_CFG2.SSM` | 26 | 0x0 | 2221 | tag L198 | tag + guard | guard copy (T1, T4) | 1 | 0 | 0, 1 (guard) |
| `SPI_CFG2.CPOL` | 25 | 0x0 | 2221 | tag L197 | tag | none: no clock edge is modeled | 0 | 0 | 0 only |
| `SPI_CFG2.CPHA` | 24 | 0x0 | 2222 | tag L196 | tag | none: no clock edge is modeled | 0 | 0 | 0 only |
| `SPI_CFG2.LSBFRST` | 23 | 0x0 | 2222 | flag L195, used as byte order (L352), not bit order | stock | byte order of a multi-byte frame (L352), not bit order (p2222) | 0 | 0 | 0 only |
| `SPI_CFG2.MASTER` | 22 | 0x0 | 2222 | flag L188, always reads 1; 0 logs an error | stock + guard | guard copy; stock reads 1 (L188); 0 refuses at the enable (T5) | 1 | 0, 1 | 0, 1 (guard) |
| `SPI_CFG2.SP` | 21:19 | 0x0 | 2222 | tag L186 | tag + guard | guard copy; not 000 refuses at the enable (T7) | 000 | 000 | any (guard) |
| `SPI_CFG2.COMM` | 18:17 | 0x0 | 2222 | tag L185 | tag | 00: every frame is sent and received (L354-364) | 00 | 00 | 00 only |
| `SPI_CFG2.Res16` | 16 | 0x0 | 2222 | reserved L184 | reserved | 0 | 0 | 0 | 0 only |
| `SPI_CFG2.IOSWP` | 15 | 0x0 | 2222 | tag L183 | tag | 0 | 0 | 0 | 0 only |
| `SPI_CFG2.Res14_8` | 14:8 | 0x0 | 2222 | reserved L177, L182 | reserved | 0 | 0x0 | 0x0 | 0 only |
| `SPI_CFG2.MIDI` | 7:4 | 0x0 | 2222 | tag L176 | tag | 0000 (timing) | 0000 | 0000 | 0000 only |
| `SPI_CFG2.MSSI` | 3:0 | 0x0 | 2222 | tag L175 | tag | 0000 (timing) | 0000 | 0000 | 0000 only |
| `SPI_IER.Res31_11` | 31:11 | 0x0 | 2223 | reserved L219 | reserved | 0 | — | 0x0 | 0 only |
| `SPI_IER.TSERFIE` | 10 | 0x0 | 2223 | tag L218 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.MODFIE` | 9 | 0x0 | 2223 | tag L215 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.TIFREIE` | 8 | 0x0 | 2223 | tag L214 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.CRCEIE` | 7 | 0x0 | 2223 | tag L213 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.OVRIE` | 6 | 0x0 | 2223 | tag L212 | tag | 0; OVR is never raised (SR.OVR is a tag, L235) | — | 0, 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IER.UDRIE` | 5 | 0x0 | 2223 | tag L211 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.TXTFIE` | 4 | 0x0 | 2223 | tag L210 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.EOTIE` | 3 | 0x0 | 2223 | flag L209, implemented | stock | implemented (L209, L401) | — | 0 | **ack: inherited** |
| `SPI_IER.DXPIE` | 2 | 0x0 | 2224 | tag L208 | tag | 0 | — | 0 | 0 only |
| `SPI_IER.TXPIE` | 1 | 0x0 | 2224 | flag L207, implemented (IRQ while set: TXP always 1) | stock | implemented: the IRQ is held while set, since TXP always reads 1 (L228, L403) | — | 0 | **ack: inherited** |
| `SPI_IER.RXPIE` | 0 | 0x0 | 2224 | flag L206, implemented | stock | implemented (L206, L400) | — | 0 | **ack: inherited** |
| `SPI_SR.(all)` | 31:0 | 0x1002 | 2224 | read-only; RXP DXP EOT SUSP TXC CTSIZE implemented, TXP always 1, others tags L226-247 | read-only | written: ignored by silicon and by the stock model | — | — | any (no effect) |
| `SPI_IFCR.Res31_12` | 31:12 | 0x0 | 2227 | reserved L274 | reserved | none (write-only register) | 0x0 | 0xfffff | 0 only — **ArduPilot writes all ones** |
| `SPI_IFCR.SUSPC` | 11 | 0x0 | 2227 | flag L267, implemented | stock | implemented (L267) | 0 | 1 | **ack: inherited** |
| `SPI_IFCR.TSERFC` | 10 | 0x0 | 2227 | tag L266 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.MODFC` | 9 | 0x0 | 2227 | tag L263 | tag + guard | the guard: with the D1 history set, clears it and makes `MASTER` unknown; with none, no effect (§3.2, H0) | 0 | 1 | 0, 1 (guard) |
| `SPI_IFCR.TIFREC` | 8 | 0x0 | 2227 | tag L262 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.CRCEC` | 7 | 0x0 | 2227 | tag L261 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.OVRC` | 6 | 0x0 | 2227 | tag L260 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.UDRC` | 5 | 0x0 | 2227 | tag L259 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.TXTFC` | 4 | 0x0 | 2227 | tag L258 | tag | clears a flag the model never sets | 0 | 1 | 0 only — **ArduPilot writes 1** |
| `SPI_IFCR.EOTC` | 3 | 0x0 | 2227 | flag L251, implemented | stock | implemented (L251) | 1 | 1 | **ack: inherited** |
| `SPI_IFCR.Res2_0` | 2:0 | 0x0 | 2227 | reserved L250 | reserved | none (write-only register) | 000 | 111 | 0 only — **ArduPilot writes 111** |
| `SPI_TXDR.TXDR` | 31:0 | 0x0 | 2227 | value L277, implemented: one access = one frame | stock | one access is one frame (L277, measured): silicon packs 2 or 4 frames into a wider access (p2227) | 8-bit | 8-bit | **ack: inherited, for an access as wide as the frame** |
| `SPI_RXDR.RXDR` | 31:0 | 0x0 | 2228 | value L284, implemented: one access = one frame | read-only | read: one access is one frame (L284) | — | — | (read) |
| `SPI_CRCPOLY.CRCPOLY` | 31:0 | 0x107 | 2228 | not defined (enum L450): write dropped, reads 0 | undefined | write dropped; reads 0x0 (measured; silicon 0x107, p2228) | — | — | none — no firmware writes it |
| `SPI_TXCRC.TXCRC` | 31:0 | 0x0 | 2229 | not defined (enum L451): reads 0 | undefined | reads 0x0 | — | — | (read-only) |
| `SPI_RXCRC.RXCRC` | 31:0 | 0x0 | 2229 | not defined (enum L452): reads 0 | undefined | reads 0x0 | — | — | (read-only) |
| `SPI_UDRDR.UDRDR` | 31:0 | 0x0 | 2230 | not defined (enum L453): write dropped, reads 0 | undefined | write dropped; reads 0x0 | — | — | 0 only |
| `SPI_I2SCFGR.Res31_26` | 31:26 | 0x0 | 2231 | reserved L319 | reserved | 0 | — | — | 0 only |
| `SPI_I2SCFGR.MCKOE` | 25 | 0x0 | 2231 | tag L318 | tag | 0 (I2S fields: unused in SPI mode, p2230; read 0) | — | — | 0 only |
| `SPI_I2SCFGR.ODD` | 24 | 0x0 | 2231 | tag L317 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.I2SDIV` | 23:16 | 0x0 | 2231 | tag L316 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.Res15` | 15 | 0x0 | 2231 | reserved L315 | reserved | 0 | — | — | 0 only |
| `SPI_I2SCFGR.DATFMT` | 14 | 0x0 | 2231 | tag L314 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.WSINV` | 13 | 0x0 | 2231 | tag L313 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.FIXCH` | 12 | 0x0 | 2231 | tag L312 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.CKPOL` | 11 | 0x0 | 2231 | tag L311 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.CHLEN` | 10 | 0x0 | 2231 | tag L310 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.DATLEN` | 9:8 | 0x0 | 2232 | tag L309 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.PCMSYNC` | 7 | 0x0 | 2232 | tag L308 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.Res6` | 6 | 0x0 | 2232 | reserved L307 | reserved | 0 | — | — | 0 only |
| `SPI_I2SCFGR.I2SSTD` | 5:4 | 0x0 | 2232 | tag L306 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.I2SCFG` | 3:1 | 0x0 | 2232 | tag L305 | tag | 0 | — | — | 0 only |
| `SPI_I2SCFGR.I2SMOD` | 0 | 0x0 | 2232 | flag L298, always reads 0; 1 logs an error | stock + guard | guard: 1 refuses (`TVI-SPI-I2SMOD`); stock reads 0 (L298) | — | — | 0 (guard) |

## 3. Truth-table rows at this branch's head (`dc55f98`), for (1), (3) and (4)

"Head" is what `model/GuardedSTM32H7_SPI.cs` does at `dc55f98`. "Proposed" is what this work order would build, subject to the stop. Every page is RM0433 Rev 9.

### 3.1 (1) The NSS output: `SSM` = 0 with `SSOE` = 1

| # | State at the `SPE` = 1 write | Head | RM0433 | Proposed |
|---|---|---|---|---|
| T4b | `MASTER`=1, `SP`=000, `SSM`=0, `SSOE`=1 | refuse, `TVI-SPI-MODF`, bit "CFG2.SSM (bit 26) = 0, CFG2.SSOE (bit 29) = 1"; also sets the D1 history at the `CFG2` write (`NoteMasterState`), so a `CFG2` read refuses after it | "When master uses hardware NSS output (SSM=0 and SSOE=1), the NSS signal input is forced to non active state internally to prevent master mode fault error" (p2221). "Even if SPI AF is not applied to the NSS pin (it can be used as a standard GPIO then), SSOE=1 setting should be kept anyway to assure default NSS input level and prevent any mode fault evaluation" (p2166) | **The mode-fault rule passes**, and the state no longer starts the D1 history. Whether the write then refuses is `RUL-17`'s: `SSOE`=1 refuses `unmodeled` unless acknowledged (§2, row `SPI_CFG2.SSOE`) |
| T4a | `MASTER`=1, `SSM`=0, `SSOE`=0 | refuse, `TVI-SPI-MODF` | "If the NSS pin is pulled into an active level in this mode, the SPI enters master mode fault state" (p2166) | unchanged; `kind: unmodeled` (the pin level is not modeled) |
| T1-SSOE | `MASTER`=1, `SP`=000, `SSM`=1, `SSIOP`=0, `SSI`=1, `SSOE`=1 | pass (`SSOE` is not read when `SSM`=1) | the internal input is `SSI` (p2221 `SSM`); `SSOE`=1 does not make it active | unchanged for the mode-fault rule; `RUL-17` as T4b |

### 3.2 (3) The `IOLOCK` latch, and the D1 history against `IFCR.MODFC`

Silicon, by page: `IOLOCK` "can be set when SPI is disabled only else it is write protected. It is cleared and cannot be set when the MODF bit is set" (p2215); it is "cleared by hardware whenever SPE bit is changed from 1 to 0" (p2215). "When MODF is set, the SPE and IOLOCK bits at the SPI_CR1 register are reset and their setting is blocked" (p2225). `MODFC`: "Writing a 1 into this bit clears MODF flag in the SPI_SR register" (p2227). The stock model latches `IOLOCK` only while `SPE` = 1, the inverse of p2215 (`STM32H7_SPI.cs` L133–142; its warning text, "Attempted to set IOLOCK while peripheral is enabled", fires when `SPE` = 0).

| # | Sequence | Head | Silicon (RM0433) | Proposed |
|---|---|---|---|---|
| L1 | `CR1`=`IOLOCK` (SPE was 0), then `CFG2`=x | refuse `TVI-SPI-IOLOCK` | write-protected: `CFG2` keeps its value (p2215, p2221) | unchanged; `kind: predicted-fault` |
| L2 | `CR1`=`0x1000` (`SSI`), `CFG2`=`0x04400000`, `CR1`=`0x1001` (passes, T1), `CR1`=`0x11001` (`IOLOCK` with `SPE`=1), `CR1`=`0x1000` (`SPE` 1→0), `CFG2`=`0x04400000` | latch set at the 4th write; the last write refuses `TVI-SPI-IOLOCK` (over-strict, review A12) | 4th write: `IOLOCK` write-protected, not set (p2215, p2217); the last `CFG2` is writable | **latch not set**: `IOLOCK` latches only when the last passed `CR1` had `SPE`=0. The last write passes. A `CR1` read serves `IOLOCK` from the latch (0), not the stock value (1) |
| L3 | `CR1`=`SPE`\|`IOLOCK` from `SPE`=0, in one write | latch set | not settled: "can be set when SPI is disabled only" does not say whether the write that enables counts | latch set (the refusing side: later `CR1` reads refuse until reset; a `CFG2` write needs `SPE`=0 first, which clears `IOLOCK` on silicon either way). Recorded as undecided |
| L4 | `CR1`=`IOLOCK` while the D1 history is set | latch set | if silicon raised `MODF`, `IOLOCK` "cannot be set" (p2215, p2225) | latch set (the refusing side), recorded |
| L5 | `CR1`=`IOLOCK` (`SPE` was 0), `CR1`=`SPE`…, `CR1`=0 (`SPE` 1→0), then `CFG2`=x | refuse `TVI-SPI-IOLOCK` | `IOLOCK` was cleared by the 1→0 edge; `CFG2` is writable, so silicon takes the write | still refuses (the clear is not modeled: neither firmware meets it), **but `kind: unmodeled`**, not `predicted-fault` (§3.3) |
| H0 | no history: `CR1`=`0x1000`, `CFG2`=`0x04400000` (T1), `IFCR`=`0xFFFFFFFF` (`MODFC` among the bits), `CR1`=`0x1001` | pass (`MODFC` is a stock tag, dropped) | no `MODF` can be set, so `MODFC` clears nothing (p2227) | **pass: `MODFC` has no effect without the history; `MASTER` stays known.** ArduPilot does this on every start (§1.1 rows 8 and 12, `SSOE` = 1). The WO's "after `MODFC` the copy's `MASTER` is unknown" is read as applying only while the history is set (§4 C6) |
| H1 | `CFG2`=`MASTER`\|`SSM` with `SSI`=0 (history set), `IFCR`=`MODFC`, then `CR1`=`SSI`\|`SPE` | refuse on the history; `MODFC` is a stock tag (dropped) | if `MODF` was raised, `MODFC` clears it (p2227), but the fault also cleared `MASTER` (p2184) and `MODFC` does not set it | **history cleared by `MODFC`; `MASTER` unknown**: the enable refuses (`kind: unmodeled`), and so does a `CFG2` read, until `CFG2` is written again |
| H2 | as H1, then `CFG2`=`MASTER`\|`SSM` with `SSI`=1, then `CR1`=`SSI`\|`SPE` | refuse | `MASTER` written again; the new state is T1 | **pass** |
| H3 | the history built from a `CR1` write: `CR1`=`0x1000` (`SSI`), `CFG2`=`0x04400000` (T1: no history), `CR1`=`0x0` (`SSI`=0 while master with `SSM`=1), `CR1`=`0x1000`, `CR1`=`0x1001` | the 3rd write sets the history (`NoteMasterState` after a `CR1` write); the enable refuses on it. No test covers it (review A12) | p2184: the internal input active while master | unchanged; **add this test**, and the mutant that removes that call (the enable would then pass) |

### 3.3 (4) A `kind` on every refusal (`RUL-18`)

`predicted-fault` needs a page that says silicon faults or ignores the write, and a bench check named in FEEDBACK. Everything the twin cannot decide is `unmodeled`.

| Rule, branch (bit text at head) | Kind | Why, by page | Bench check (FEEDBACK) |
|---|---|---|---|
| `TVI-SPI-MODF` "CR1.SSI (bit 12) = 0" (T2) | `predicted-fault` | "Mode fault occurs when the master device has its internal slave select logic input (… SSI bit in slave select software mode) pulled low" (p2184); `SSIOP`=0, so low is active on both readings (p2165, p2184) | yes: the `modf` firmware on the Nucleo, read `SR.MODF` and `CFG2.MASTER` |
| `TVI-SPI-CFG2-SPE` | `predicted-fault` | "When SPE=1 … SPI_CFG2 … write protected" (p2217); `CFG2` "is write protected when SPI is enabled" (p2221) | yes: write `CFG2` with `SPE`=1, read it back |
| `TVI-SPI-IOLOCK` on a `CFG2` write, latch certain (L1: set from `SPE`=0, no history, and no `SPE` 1→0 since) | `predicted-fault` | "When this bit is set, the SPI_CFG2 register content cannot be modified" (p2215; p2221) | yes: the `iolock` firmware, read `CFG2` back |
| `TVI-SPI-IOLOCK` on a `CFG2` write, latch uncertain (L3, L4) or after an `SPE` 1→0 edge (L5) | `unmodeled` | p2215 contradicts `predicted-fault` here: "cleared by hardware whenever SPE bit is changed from 1 to 0" (L5); L3 and L4 latch without knowing that silicon did. **This is the WO §7 "RM0433 contradicts a `kind`" item:** the WO expects every `CFG2` write under `IOLOCK` to be `predicted-fault` | — |
| `TVI-SPI-MODF` "CFG2.MASTER (bit 22) = 0" (T5) | `unmodeled` | slave mode is not modeled by the stock model (L188); p2222 | — |
| `TVI-SPI-MODF` "CFG2.SP[2:0] … != 000" (T7) | `unmodeled` | TI mode: the manual does not describe a TI master's mode fault (p2181, p2171) | — |
| `TVI-SPI-MODF` "SSM = 0, SSOE = 0" (T4a) | `unmodeled` | the NSS pin level is not modeled (p2166) | — |
| `TVI-SPI-MODF` "SSM = 0, SSOE = 1" (T4b) | removed: the rule passes (§3.1) | p2221, p2166 | — |
| `TVI-SPI-MODF` "CFG2.SSIOP (bit 28) = 1" (T3) | `unmodeled` | the manual is inconsistent (p2165, Figure 618 p2167, against p2184) | — |
| `TVI-SPI-MODF` the D1 history, at the enable and at a `CFG2` read | `unmodeled` | the manual does not settle whether `MODF` is evaluated at `SPE`=0 (p2184 against p2217, p2171) | `0100a` F1 (unchanged) |
| `TVI-SPI-MODF` `MASTER` unknown after `MODFC` (new, H1) | `unmodeled` | p2227, p2184 | — |
| `TVI-SPI-IOLOCK` on a `CR1` read while latched | `unmodeled` | the clear on `SPE` 1→0 (p2215) is not modeled | — |
| `TVI-SPI-I2SMOD` (missing from the WO's expected list) | `unmodeled` | I2S/PCM mode is not modeled; `I2SMOD` "needs to be kept at 0 in SPI mode" (p2230) | — |
| `TVI-SPI-WIDTH` | `unmodeled` | a guard limitation; §50.11 states no access width for these registers | — |
| `TVI-LOAD` | `unmodeled` | the records contract (`RUL-12`) | — |
| the allow-list (new, `RUL-17`) | `unmodeled` | per row of §2 | — |

## 4. The stop: fields to acknowledge by name, or rule refused

`RUL-17` refuses, with `kind: unmodeled`, any write that gives a field a value outside the allow-list. **Each item below is a field that one of the firmwares writes with a value the model does not implement.** If it is refused, that firmware refuses at the access named. "Whole" means the register is written without a read first. Each lean is mine, and each is a proposal.

### A. Tag, reserved and reserved-value writes

| # | Field | Who writes what, where | Page | The model behaves as | Lean |
|---|---|---|---|---|---|
| A1 | `SPI_CFG1.MBR` | generated: 001, 100 (whole). ArduPilot: 010 (`spi1`), 101 (`spi2`) (whole, `spi_lld_start`) | p2218 | no clock: a transfer completes in zero virtual time | **acknowledge.** Timing only; cycle accuracy is a non-goal (northstar §5). `RUL-17`'s drafting note (response §4.2) names this field |
| A2 | `SPI_CFG2.SSOE` = 1 | ArduPilot, whole, `spi_lld_start` (row 6), with `SSM`=0 and no NSS pin routed (§1.2) | p2221, p2166 | the mode-fault side is modeled by (1); the NSS output has no pin to drive | **acknowledge**, on p2166's own statement of this case. The twin cannot check the routing (GPIO is unguarded). The alternative, refused, makes every ArduPilot SPI start refuse, which is the §7 stop |
| A3 | `SPI_CFG2.AFCNTR` = 1 | generated, whole, every master (Aethelgard's `AET-WO-4230` R4) | p2221 | 0: the model has no pins; the bit "is taken into account when SPE=0 only" | **acknowledge.** Pin-level only |
| A4 | `SPI_CR1.MASRX` = 1 | ArduPilot, a constant written whole (row 3); the enable's read-modify-write (rows 9–10) carries it on silicon, and the stock read loses it | p2217 | the stock FIFO is a queue that never fills, so the model neither overruns nor suspends | **acknowledge, with the guard serving `MASRX` from its copy on read**, so that row 9 reads `0x100` and the enable is `0x101`, as on silicon |
| A5 | `SPI_CFG1.CRCSIZE` = 00000 | ArduPilot, whole, every start and stop (`CFG1`=`0x…C007` and `0x0`) | p2219: 00000 is "reserved"; "If CRC calculation is disabled by CRCEN=0, the CRCSIZE field must be kept at its default setting" | 00111; CRC is not modeled, and `CRCEN` is 0 | **acknowledge, with `CRCEN`=0 as its condition**: the manual gives the field no effect without CRC. Refusing it refuses every ArduPilot start (the §7 stop). The breach of "must be kept" is recorded, not modeled |
| A6 | `SPI_CFG1.DSIZE` = 00000 | ArduPilot, whole, `spi_lld_stop` (`CFG1`=`0x0`), with `SPE`=0 | p2220: 00000 "not used" | whole bytes only; 00000 behaves as 8-bit | **acknowledge 00000 as a value at rest**, or rule C1. Every start rewrites 00111 before the enable. Refusing it refuses ArduPilot's first `spi_lld_stop`, not its start |
| A7 | `SPI_IER.OVRIE` = 1 | ArduPilot, whole, `spi_lld_start` (row 7) | p2223 | 0; `SR.OVR` is a tag, so the interrupt never fires | **acknowledge**: the gap is `OVR`'s, which is never raised, not the enable's |
| A8 | `SPI_IFCR` `TXTFC`, `UDRC`, `OVRC`, `CRCEC`, `TIFREC`, `TSERFC` = 1 | ArduPilot, `IFCR`=`0xFFFFFFFF`, `spi_lld_start` and `spi_lld_wait_complete` | p2227 | each clears a flag the model never sets, so the flag after the write is 0, as on silicon after the clear | **acknowledge 1** for these write-1-to-clear tags |
| A9 | `SPI_IFCR` bits 31:12 and 2:0 = 1 | ArduPilot, `IFCR`=`0xFFFFFFFF` | p2227: "Reserved, must be kept at reset value" | none: `IFCR` is write-only | **architect's call.** The manual states no effect. Refusing it refuses every ArduPilot start (the §7 stop). Lean: acknowledge by name for `IFCR` only, because no reserved bit there is readable |

### B. Inherited fields both firmwares use (stock, unaudited: invariant 2)

`RUL-17` admits an inherited field only "acknowledged by name". Each is implemented by the stock model; none is audited here.

| Field | Written by | Stock |
|---|---|---|
| `SPI_CR1.SPE` | both | L92; the enable is also the mode-fault rule's |
| `SPI_CR1.CSTART` | both | L108 |
| `SPI_CR1.CSUSP` | ArduPilot | L116 |
| `SPI_CR2.TSIZE` | generated 2, 4, 7; ArduPilot 0 (endless, L376) | L146 |
| `SPI_CFG1.DSIZE` = 00111 | both | L152, L350 |
| `SPI_CFG1.TXDMAEN`, `RXDMAEN` | ArduPilot | L164 (stored only), L162 |
| `SPI_IFCR.EOTC`, `SUSPC` | generated `EOTC`; ArduPilot both | L251, L267 |
| `SPI_TXDR`, 8-bit with `DSIZE` = 00111 | both | L277 |
| `SPI_RXDR`, 8-bit reads | ArduPilot (generated: reads not logged) | L284 |

`IER`'s `RXPIE`, `TXPIE` and `EOTIE` are written only at their reset value (0), so they need no acknowledgement for these firmwares.

### C. Rulings the build depends on

- **C1. Per write, or at the enable?** `RUL-17` judges each write. A6 and A5's stop-time write are values at rest that no transfer uses. Lean: keep per write, as ruled, and acknowledge A5 and A6 by name. The alternative, judging the configuration registers at the `SPE` 0→1 edge, would be a change to `RUL-17`.
- **C2. Reads of copied fields.** `RUL-17`: "Every field the guard keeps a copy of is served from that copy on read." Lean: the guard keeps a whole copy of `CR1` (other than `CSTART`, `CSUSP` and `SPE`, which the stock model drives), `CFG1` and `CFG2`, and serves those reads from it. `CFG1`'s copy resets to `0x00070007` (p2218), which fixes the read §1.3 measured (`0x0`). Neither firmware reads `CFG1`.
- **C3. A `CFG1` write while `SPE` = 1.** Silicon keeps everything but `TXDMAEN` and `RXDMAEN` (p2218). The stock model drops the whole write (L329), DMA bits included. Lean: refuse, `predicted-fault` if a protected field changes, otherwise `unmodeled`. Neither firmware does it.
- **C4. `CRCPOLY`.** No firmware touches it. Its read is wrong (`0x0`, silicon `0x107`). Lean: the guard serves `0x107` from a copy, and a write of any other value refuses (`unmodeled`: CRC is not modeled).
- **C6. `MODFC` without the history.** Lean: no effect, and `MASTER` stays known (§3.2 H0). Read literally for every `MODFC` write, the WO's "after `MODFC` the copy's `MASTER` is unknown" would refuse every ArduPilot start, because `IFCR` = `0xFFFFFFFF` comes between `CFG2` and the enable (§1.1 rows 6, 8 and 10).
- **C7. `TVI-SPI-IOLOCK`'s kind** is split, by RM0433 p2215 (§3.3): `predicted-fault` only while the latch is certain and no `SPE` 1→0 edge has followed; otherwise `unmodeled`.
- **C5. Wider data-register accesses.** One `TXDR` or `RXDR` access is one frame (§1.3). Lean: an access narrower or wider than the frame refuses (`unmodeled`). Silicon forbids the narrower one (p2227, p2228) and packs the wider one. Both firmwares use 8-bit accesses with 8-bit frames.

### D. Measured and not built, per the work order

- **The generated firmware's reads are not measured** (`writes.tsv` holds writes only). C2 and C5 change what a read returns, so before building them I will take its SPI reads, and their widths, from one SPI-only replay of a `1100` capture.

- **`IOLOCK`'s hardware clear on `SPE` 1→0** (p2215) is not modeled: neither firmware sets `IOLOCK` (§1.1), so modeling it removes no refusal either meets.
- **`SR.MODF` reads 0 while the D1 history is set,** as built (`0100a` F2). `SR` is not guarded, and ArduPilot never reads it.
- **ArduPilot's SPI start under the acknowledged table.** With every item of A and B acknowledged, and C6 ruled as leaned, every access in §1.1's start, exchange and stop passes on paper. With A6 refused, the first `spi_lld_stop` refuses; with any other item of A refused, the first start does. It is replayed as a planted firmware in the build (WO §2).

## 5. Findings for FEEDBACK (the steering documents are not changed here)

1. **`RUL-17`'s reason lists "tagged `LSBFRST`".** At `066a7f13` `LSBFRST` is a stored flag that the transmit path uses as byte order (L195, L352), not as bit order (p2222). It is a wrong implementation, not a tag. Only the value 0 is in the allow-list above.
2. **The stock `IOLOCK` is inverted** (L133–142; measured in §1.3), and it survives `SPE` 1→0. A stock `CR1` read can therefore show `IOLOCK` = 1 where silicon shows 0. The guard's read must take `IOLOCK` from its own latch.
3. **The stock `CFG1` reads `0x0` after reset,** where silicon reads `0x00070007` (§1.3). A read-modify-write of `CFG1` would carry `DSIZE` = 00000 and `CRCSIZE` = 00000. C2 closes it for SPI1.
4. **Aethelgard's foreign run installs a write hook on `spi1` and `spi2`** that replaces `LogPeripheralAccess`'s (§1.1). `0100a`'s `adv-hook` showed that a hook does not displace the guard. Whether Aethelgard's `AETHELGARD-CFG2` hook coexists with a guarded `spi1` is Phase 2's to measure.
5. **ArduPilot's `spi_lld_stop` writes `CFG2` = 0** (`MASTER` = 0) at every stop. The stock model logs an `[ERROR]` for each (2,369 in the 5 s window), which Aethelgard's foreign judge exempts (`renode/foreign.rs` L141). The guard is unaffected: `MASTER` = 0 refuses only at an enable.

## 6. Files (scratch, not committed)

| File | sha256 |
|---|---|
| ArduPilot verdict, `~/.cache/tvi-0100b/ap/out3/capture/verdict.json` | `06e7d24f50914dba8e3b0337cda917ec34f4e030b9dba719267d5ea9c008e7c5` |
| Captured Renode argv, `…/ap/out3/capture/2/argv.bin` | `775243853db8c8c4a83b0137397300860bfc7879f58d3727028eb9366a999887` |
| Replay log, `…/ap/out8/replay-2-log.log` | `15c03b5af6add802ea8514571c81bff929825bd33e7c04836485d88ceea2bc5b` |
| SPI accesses, `…/ap/spi-accesses.tsv` | `673439233fadd35dc60895b6adbef626c565a11b8a05cfefafb4b1c9c1a45207` |
| GPIO accesses, `…/ap/gpio-accesses.tsv` | `57b6b8311d7bde5865a267a4689958e955b019bb60d816a87d92a56effb4fc96` |

## 7. Quote check

Every quotation from RM0433 in this file was matched, after whitespace was normalised, against the text Ghostscript extracted from the PDF for the page its line cites. That is 17 quotations on pages named in the same line, plus "must be kept at reset value" (on every SPI register page, p2215–p2232) and "can be set when SPI is disabled only" (p2215), whose lines name no page. The other quoted strings are code text (the stock model's log messages, the guard's bit texts), a ruling's words (`RUL-17`), or a table title in DS12117. In DS12117, which pins carry an `SPIx_NSS` cell was read from Table 9 (pp89–100); the cell's AF column was not relied on (§1.2). The two pins either firmware uses that can carry NSS, PA11 and PE11, were read by eye: at AF1 each is `TIM1` (p89, p94).
