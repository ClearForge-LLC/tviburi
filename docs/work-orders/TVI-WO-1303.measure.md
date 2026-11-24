# TVI-WO-1303 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1303-dma-guard`, from `main` `4d9a063` (`1302` merged).
**State:** §1.1 measured, then **stopped** (WO §1.1), with the questions in §5. Predictions committed at `bb50435` before any run.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/`: `DMA/STM32DMA.cs` (sha256 `f7105075…`), `Miscellaneous/STM32_DMAMUX.cs` (`5d54137d…`), the first lines of `DMA/STM32LDMA.cs`. No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock `STM32DMA.cs` and `STM32_DMAMUX.cs` in full, and `STM32LDMA`'s class line (`sealed`);
- the stock include chain's DMA entries (Aethelgard `1cb994c1`, `crates/aethelgard-twin/tests/renode-1.17.0/platforms/cpus/stm32h7.repl`): `dma1`/`dma2` (`DMA.STM32DMA`, IRQs to the NVIC), `dmamux1` (`[0-7] -> dma1@[0-7]`, `[8-15] -> dma2@[0-7]`, no RCC registration), `bdma` (`DMA.STM32LDMA`), and the only request sources wired to `dmamux1`: `spi4` `DMARecieve -> dmamux1@83`, `stm32h753.repl`'s `OutDMA -> dmamux1@77`, and the ADCs' `dmaPeripheral: dmamux1` with `dmaChannel` 9 and 115;
- RM0433 Rev 9 pp. 105–109 (§2.1.2, §2.1.6), p131 (Table 7), p136, p455 (`DMA1EN`/`DMA2EN`), pp. 659–667 (§15.5), pp. 696–709 (§17.3, §17.6);
- `TVI-WO-1300a.measure.md` §1.5: ArduPilot's 20 DMA writes at 923,860 ns, before `DMA1EN`/`DMA2EN`;
- `TVI-WO-1302.measure.md`: the DShot boards write no `DIER` DMA enable or `DCR`/`DMAR` in 100 ms; ArduPilot's `USART1_CR3` = `0xC0` (`DMAR` = 1) and no `RDR` read; AP-m's `DIER` = 0;
- Aethelgard `1cb994c1`'s `crates/aethelgard-svd-hal/README.md` L64 and L72 ("DShot is a continuous zero-throttle pattern, with no DMA"; the USART "no interrupt, no DMA"), and its DMA mentions in `ledger.rs`, `renode/foreign.rs` and `twin/src/boot.rs`.

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9)
- **TCM.** "The memory bus can access all internal memories except ITCM and DTCM … The peripheral bus can access all internal memories except ITCM and DTCM" (§2.1.6, p108), for DMA1 and DMA2. Only the MDMA reaches the TCMs, through the CPU's AHBS (§2.1.2, p105). The regions (Table 7, p131): ITCM `0x0000_0000`–`0x0000_FFFF`, DTCM `0x2000_0000`–`0x2001_FFFF`; AXI SRAM `0x2400_0000`–`0x2407_FFFF`; SRAM1–3 `0x3000_0000`–`0x3004_7FFF`; SRAM4 `0x3800_0000`–`0x3800_FFFF`. So `PAR` as well as `M0AR`/`M1AR` counts. **What silicon then does** (a bus error, `TEIF`, `EN` cleared by hardware, p665 "if a transfer error occurs on the AHB master buses") is inferred, not read; the restriction itself is cited.
- **Writes under `EN` = 1.** Stated per field: `MBURST`, `PBURST`, `DBM`, `PL`, `PINCOS`, `MSIZE`, `PSIZE`, `MINC`, `PINC`, `DIR`, `PFCTRL` "protected and can be written only if EN = 0" (pp. 662–664); `CT` "can be written only if EN = 0" (p662); `NDT` "can be written only when the stream is disabled" (p665); `PAR` "write-protected" (p666); `M0AR` only with `EN` = 0, or `EN` = 1 with `DBM` and `CT` = 1; `M1AR` the same with `CT` = 0 (p666); `FCR.DMDIS` (p667). **No such line** on `TCIE`, `HTIE`, `TEIE`, `DMEIE`, `CIRC`, `TRBUFF`, `FTH` or `FEIE`. But `EN`'s own text says "It is forbidden to write these registers when the EN bit is read as 1" of "the configuration and FIFO bits registers" (p665). **That tension decides whether a stream's disable refuses,** because a disable is a `CR` write while `EN` reads 1 (P4).
- **`EN` is cleared by hardware** at the end of a transfer, on a bus error, and on a FIFO/burst mismatch; `NDT` reloads on a re-enable or in circular mode (p665).
- **DMAMUX1.** 107 request inputs (Table 123, p696). `DMAREQ_ID` 0 selects no request (p703). Ids 54 and 116–127 are `Reserved` (Table 124, pp. 697–698). Tables 124 and 125 read identically in the text layer (a question for the rendered pages). One non-null id on two channels is a "Caution", allowed "when the application guarantees that the two connected DMA channels are not simultaneously active" (p704). Registers take byte, half-word and word accesses (p708).
- **The clock.** `DMA1EN` is "DMA1 and DMAMUX1 clock enable", `DMA2EN` "DMA2 and DMAMUX1" (p455). A write with the clock off is ignored (p373). DMAMUX1 is clocked if either is set (inferred from the two descriptions).

### P2. The stock models, by source (each to be probed)
- **Form.** `STM32DMA` is `sealed` (so a guard is `RUL-07`'s wrapper, owning an unregistered stock instance); `STM32_DMAMUX` is not (a subclass is possible); `STM32LDMA` (BDMA) is `sealed`.
- **Transfers.** Memory-to-peripheral and memory-to-memory move all of `NDT` at the `EN` write, with no request. Peripheral-to-memory moves one data unit (or the FIFO threshold with `DMDIS` = 1) per rising request.
- **Never modeled:** `EN` cleared at the end of a transfer (it stays 1), `NDT` reload, circular mode (`CIRC` a tag), double-buffer (`DBM`, `CT`, `M1AR` tags; `M1AR` reads 0), `PL`, `PBURST`, `MBURST`, `PFCTRL`, `PINCOS`, `TRBUFF`, `HTIF`/`TEIF`/`DMEIF`/`FEIF` and their enables, `FCR.FS`/`FEIE`. **`FCR` resets to `0x00`** (silicon `0x21`, p667).
- **A `CR` write with `EN` = 1 while `EN` is already 1 fires the transfer again** (the write callback runs on every write); with `NDT` already 0 it logs "no data to send". **A configuration write under `EN` = 1 is taken.**
- **DMAMUX1** accepts any 7-bit `DMAREQ_ID`, reserved or 0, with no record; `SE`, `EGE`, `SOIE`, `SPOL`, `NBREQ`, `SYNC_ID` and the request generators are tags. It has no RCC registration, so **it is never locked:** a write with both `DMA1EN` and `DMA2EN` 0 is taken.
- **Requests the stock platform can raise:** only `cryp` out (77), `spi4` RX (83), and the ADCs (9, 115). No USART, timer, I2C, or SPI1/2/3/5 request exists. **So a peripheral-to-memory stream for any other request never moves data.**
- **The timer** has no DMA output; its `DIER` DMA enables are tags and `DMAR` is undefined. **No `UDE` or `CCxDE` ever fires a stream** (WO (3)).

### P3. The generated firmware (14 boots, recaptured at Aethelgard `1cb994c1`)
- **No boot reads or writes DMA1, DMA2, DMAMUX1 or BDMA.** High confidence: the HAL has no DMA path.
- So the guard changes no generated observer.

### P4. ArduPilot, `sentinel-02-own` (the committed fixture, ELF `a799dfb6`)
- **Before the clocks:** the 20 writes at 0.92 ms are ChibiOS's DMA init (`S0CR`–`S7CR` = 0, `LIFCR`/`HIFCR` all clear), dropped by the bus lock with a WARNING, and no Tviburi record.
- **After `DMA1EN`/`DMA2EN`:** between 2 and 8 streams are configured, for USART1 RX (request 41, peripheral-to-memory), USART1 TX (42, memory-to-peripheral), and SPI1/SPI2 RX/TX (37–40). Held loosely: I2C may also take streams (33–36).
- **The order:** `DMAMUX1_CxCR`, then `PAR`, `M0AR`, `NDTR`, `CR` (mode, `EN` = 0), then `CR` with `EN` set. **Configuration precedes `EN` in every case.** Held with moderate confidence.
- **The disable:** one `CR` write that clears `EN` and the interrupt enables together while `EN` reads 1 (ChibiOS's `dmaStreamDisable`, from memory). **Under a rule read from p665's general sentence, every disable would refuse** (P1).
- **Re-arming after a completed transfer:** the stock `EN` is still 1 (P2), so a firmware that reprograms `NDTR`/`M0AR` after a transfer without first writing `EN` = 0 would meet a "configuration under `EN`" rule keyed on the stock `EN` where silicon's `EN` is 0. I predict ChibiOS always disables first, so this does not occur. Held loosely.
- **Memory addresses:** every `M0AR` in AXI SRAM or SRAM1–3; **none in DTCM or ITCM** (ArduPilot allocates DMA buffers outside the TCM on the H7). Held with moderate confidence; one at a TCM address is a stop (WO §7).
- **What runs:** USART1 TX moves its bytes at `EN` (the console sees them). **USART1 RX never moves data** (no request). SPI RX streams, if used, never complete (no request). No timer DMA.

### P5. ArduPilot with motor outputs (AP-m, `1302`'s scratch build, ELF `2e686339`)
- TIM1 runs 50 Hz PWM, `DIER` = 0: **no `UDE`, no `DMAR`, no timer request.** Its other streams are as P4.

### P6. Consumer observers each write reaches
- Generated boots: none (P3).
- ArduPilot: the console bytes sent by USART1 TX's stream. Aethelgard's foreign boot reads `USART1_CR3` under a mask without `DMAR`/`DMAT` and calls the DMA enables unjudged. **No observer reads a DMA register.** A guard that models `EN`'s hardware clear, or `FCR`'s reset, changes no observer.

### P7. The clock (WO (2))
- **An access to `dma1` with `DMA1EN` = 0 is dropped by the bus lock with a WARNING and no Tviburi record** (`1300a` held SPI guards only in the RCC guard's unlocked set). So the case is silicon's ignored write, not a refusal: "already refuses through `1300a`'s lock" will measure as a silent drop.
- `dmamux1` is never locked (P2).

### P8. The form
- **A wrapper over an unregistered `STM32DMA`** loads cancelled and restated as `dma1`/`dma2` on `tviburiRcc`; its owned instance's engine still transfers on the system bus; `dmamux1`'s routes to it and its IRQs to the NVIC pass through. Held loosely: the owned instance's `GetSystemBus(this)` on an unregistered peripheral is untested.
- `dmamux1`'s `[0-7] -> dma1@[0-7]` routes must be restated onto the wrappers (`1302`'s variant B).

### P9. Memory (`1201`'s budget, like for like)
- `main` now collects once at the first start (`1302`'s timer guards), so a new class's compile garbage is collected on both sides. **A guard of two wrappers, a DMAMUX subclass and its table costs +5 to +15 MiB at the median**; a thin prototype under +5 MiB. A thin prototype does not predict the guard (`1302`: +2.4 MiB, then +35.6).

## 1. How it was measured (after §0 was committed at `bb50435`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `1cb994c1` pins (`container/IMAGE`, fetched this session). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1303/` (`cap-gen/`, `cap-ap/`, `m1/`, `probe/`, `budget/`, and the scripts in `h/`), kept until the architect says.

| Run | What | How |
|---|---|---|
| **C** | the 14 generated boots, recaptured at Aethelgard `main` `1cb994c1` | `1302`'s `capture-gen.sh` (generate, build, `simulate --for 100ms`, the `/proc` watcher), with the CLI and `aethelgard-bridge` built offline in the image from a `git archive` export. Recaptured, not reused: `solve.rs` and `power_parts.rs` changed since `1302`'s `65c74788`. All 14 verdicts `pass` |
| **M1** | each boot replayed on `main`'s release (every guard recorded), with `sysbus LogPeripheralAccess` on `dma1`, `dma2`, `dmamux1`, `bdma`, those three DMAs at `logLevel -1`, and every stream's `CR`/`NDTR`/`PAR`/`M0AR`/`FCR`, `LISR`/`HISR` and DMAMUX1's 16 channels read after `RunFor` | `h/replay-d.sh`: the committed `tests/1301/replay-1301.sh` (timer restatement included) plus the argv taken after `--` (Aethelgard's reaper), the logging, and `collapse-repeated-log-entries` off. Analysed by `h/dma-analyse.py` |
| **AP** | ArduPilot: the committed fixture (`aet-1c32c8ea-ardupilot`, ELF `a799dfb6`) and `1302`'s AP-m (Aethelgard's `serial-dshot-9600.hwdef.dat`, TIM1 CH1–CH4 PWM, ELF `2e686339`), 5 s each | as M1, twice each: **locked** (the run as the consumer makes it) and **unlocked** (`DMA1EN`/`DMA2EN` set after the consumer's cold `*ENR` writes: a measurement pass that lets the 0.92 ms writes reach the stock model) |
| **M2** | the stock DMA and DMAMUX1 by probe | monitor-driven, on `main`'s Nucleo board (`probe/probes.body`, D0–D14), before any `cpu Step` |
| **M3** | the form | the same body through a scratch wrapper (`probe/ProbeSTM32DMA.cs`: owns an unregistered `STM32DMA`, forwards every access, request, interrupt line and reset; no rule), cancelled and restated as `dma1` on `tviburiRcc`, with an untyped `dmamux1:` update routing channels 0–7 to it |
| **B** | the one-file memory, by `1201`'s budget | `tests/1301/ci-memory.sh`, eleven boots a side, from a scratch checkout whose replay appends the wrapper's restatement for `dma1` and `dma2` (§4) |

## 2. Results

### 2.1 The field table (M1, AP)

**The 14 generated boots read and write no register of DMA1, DMA2, DMAMUX1 or BDMA** (0 accesses each in 100 ms, `1cb994c1`). **No firmware touches BDMA.** Everything below is ArduPilot's; the fixture (F) and AP-m (M) agree in shape, and AP-m lacks the IMU stream pair (no IMU on `serial-dshot-9600`).

| Stream (F; M) | DMAMUX1 id (Table 124, p697–698) | Direction | Enables in 5 s (F) | `CR` values written, in order | `M0AR` (region, Table 7 p131) | `PAR` |
|---|---|---|---|---|---|---|
| DMA1 S0; S0 | 41 `usart1_rx_dma` | P→M | 1 | `0x100416`, then `0x411` | `0x3800_1920` (SRAM4) | `0x4001_1024` (`USART1_RDR`) |
| DMA1 S4; S3 | 42 `usart1_tx_dma` | M→P | 28 | `0x100456`, `0x451`, `0x440` | `0x3800_1980` (SRAM4) | `0x4001_1028` (`USART1_TDR`) |
| DMA1 S1; S2 | 38 `spi1_tx_dma` | M→P | 19 | `0x10446`, `0x441`, `0x440`, `0` | `0x3800_19E0`… (SRAM4) | `0x4001_3020` (`SPI1_TXDR`) |
| DMA2 S1; S2 | 37 `spi1_rx_dma` | P→M | 19 | `0x10416`/`0x10016`, `0x411`/`0x11`, `0x400`, `0` | `0x3800_1A20`… (SRAM4) | `0x4001_3030` (`SPI1_RXDR`) |
| DMA1 S3; — | 40 `spi2_tx_dma` | M→P | 2,354 | `0x10446`, `0x441`, `0x440`, `0` | `0x3800_2240`, `0x3800_19E0` (SRAM4) | `0x4000_3820` |
| DMA1 S2; — | 39 `spi2_rx_dma` | P→M | 2,354 | `0x10416`/`0x10016`, `0x411`/`0x11`, `0x400`, `0` | `0x3800_2080`… (SRAM4) | `0x4000_3830` |

- **Every address is in SRAM4 or a peripheral. None is in ITCM or DTCM** (and none in AXI SRAM or SRAM1–3: §3 marks P4). SRAM4 is "internal memory" that DMA1/DMA2 reach (p108).
- **The order, every enable:** `FCR` = `0x21`, `PAR`, `M0AR`, `NDTR`, `CR` = mode with `EN` 0, then `CR` with `EN` 1. **No configuration field is written while its stream's `EN` (as written) is 1.** The DMAMUX1 channel is written before the stream's first `PAR` each time.
- **Every disable is one `CR` write** from `EN` = 1 that clears `EN` and `TCIE` together (`0x451` → `0x440`; `0x411` → `0x400`), then a wait reading `CR` (`S2CR`/`S3CR` 11,770 reads each). **`TCIE` carries no "protected" line (p664), but p665's sentence forbids writing the register while `EN` reads 1** (§5 Q2).
- **ChibiOS sets `EN` by read-modify-write, and the stock model's tags read 0,** so `CR` = `0x10446` (`PL` = 01, `TEIE`, `DMEIE`) reads back `0x440` and is rewritten `0x441`. On silicon the stream runs with `PL` = 01, `TEIE`, `DMEIE` and (UART streams) `TRBUFF`; in the twin with all four 0. Neither is modeled, so no behaviour differs today; a guard serving `CR` from its copy (`RUL-17`) makes the read-back silicon's.
- **Before the clocks (0.92 ms):** 20 writes, `S0CR`–`S7CR` = 0 on both controllers and `LIFCR`/`HIFCR` = `0xFFFF_FFFF`, each dropped by the bus lock with a `WARNING` naming address and value (unlocked: the stock model takes the `CR` zeros and warns on `LIFCR`'s reserved and tagged bits). All are reset values.
- **Fields given a non-reset value:** `CR.EN`, `TCIE`, `TEIE`, `DMEIE`, `DIR` (00, 01), `MINC`, `PL` (01), `TRBUFF`; `FCR` = `0x21` (its reset value, written; `FS` is read-only, p667); `NDTR`, `PAR`, `M0AR`; `LIFCR`/`HIFCR` clear bits (`CTCIF`, `CHTIF`, `CTEIF`, `CDMEIF`, `CFEIF`); DMAMUX1 `CxCR.DMAREQ_ID` (37–42). **Never written non-zero:** `HTIE`, `CIRC`, `DBM`, `CT`, `M1AR`, `PBURST`, `MBURST`, `PFCTRL`, `PINCOS`, `PSIZE`/`MSIZE` (bytes throughout), `FCR.DMDIS`/`FEIE`, DMAMUX1's `SE`, `EGE`, `SOIE`, `SPOL`, `NBREQ`, `SYNC_ID` and its request generators.

### 2.2 The stock models, by probe (M2)

| # | Probe | Stock (measured) | Silicon (RM0433 Rev 9) |
|---|---|---|---|
| D0 | reset reads | `CR`, `NDTR`, `M1AR`, `LISR` 0; **`FCR` `0x00`** | `FCR` `0x21` (p667) |
| D1 | `NDTR` written with `DMA1EN` = 0 | dropped; `WARNING … locked peripheral`; reads 0; **no Tviburi record** | ignored; a read is invalid (p373) |
| D1b | DMAMUX1 `C3CR` written with `DMA1EN` = `DMA2EN` = 0 | **taken** (reads 5): DMAMUX1 has no RCC registration and is never locked | ignored (p455: DMAMUX1's clock is `DMA1EN`/`DMA2EN`; p373) |
| D2 | M→M, 4 words, AXI → AXI | **all 4 moved at the `EN` write**; `EN` reads 1 after; `NDTR` 0; `TCIF0` set | `EN` cleared by hardware at the end (p665) |
| D3 | `CR` rewritten with `EN` = 1 | the transfer fires again ("no data to send") | — |
| D4 | `NDTR`, `PAR`, `M0AR`, `CR.DIR` written under `EN` = 1 | **all taken**; the `CR` write (`EN` = 1) fired a 7-word M→P transfer | ignored or forbidden (pp. 662–666) |
| D6, D7 | M→M **from DTCM**; M→M **to ITCM** | **both move the data** (`dtcm`, `itcm` are plain memories on the stock platform) | not reachable (p108) |
| D8 | `CR` = `0x009F_013E`, `M1AR`, `FCR` = `0x87` | `CR` reads `0x10`: `CIRC`, `DBM`, `CT`, `PL`, `PBURST`, `MBURST`, `TRBUFF`, `PFCTRL`, `HTIE`, `TEIE`, `DMEIE` dropped; `M1AR` reads 0; `FEIE` dropped | stored (rw) |
| D9 | M→P to `TIM2_ARR` with **no DMAMUX1 route** (id 0) | **moved at `EN`** | no request is selected (p703): nothing moves |
| D10 | P→M on `TIM2_UP` (22), `TIM2_DIER.UDE` = 1, `CEN`, 1 ms | **`NDTR` unchanged: the stock timer raises no request** (`UDE` is a tag) | one transfer per update |
| D11 | the same request raised by hand at DMAMUX1 input 22 | one data unit moved (`NDTR` 4 → 3) | — |
| D12 | DMAMUX1: id 54 (reserved), `0x7F`, all ones; a byte and a half-word write | **54 and `0x7F` taken with no log;** only `DMAREQ_ID` stored; **byte and half-word writes dropped** (`WARNING`) | 54 and 116–127 reserved (Table 124); byte and half-word allowed (p708) |
| D13 | one id on two channels, raised once | both channels see it | allowed only if the two streams are never active together (p704) |
| D14 | byte and half-word writes to DMA1 | dropped (`WARNING`) | "accessed by words" (p659) |

**Requests the stock models can raise.** In the stock include chain: `cryp` (77), `spi4` (83), the ADCs (9, 115). **The consumer's own platforms add `spi1` `DMARecieve -> dmamux1@37` and `spi2` `-> @39`** (12 of the 14 generated platforms wire `@37` and 4 of them `@39`; both ArduPilot platforms wire `@37`, the fixture `@39` too; §3 marks P2). The generated firmware never enables a stream, so only ArduPilot uses them, and the stock SPI model raises them: 552 (SPI1) and 25,073 (SPI2) requests in ArduPilot's 5 s. **Nothing raises `usart1_rx_dma` (41):** ArduPilot's USART1 RX stream is enabled once and never moves a byte. **No timer, I2C or other USART request exists.**

### 2.3 The form (M3)

- **`STM32DMA` is `sealed`,** so the guard is `RUL-07`'s wrapper. The probe loads cancelled and restated (`dma1: @none`; `tviburiDma1: … as "dma1"`, registered on `tviburiRcc` as `DMA1`, `[0-7] -> nvic@[11-17, 47]`); `peripherals` lists `dma1 (ProbeSTM32DMA)`.
- **The owned stock instance transfers** on the system bus (D2–D9 identical to the stock run), **is locked with `DMA1EN` = 0** (the RCC guard's sync applies to the wrapper's registration), and **receives DMAMUX1's requests** through the untyped `dmamux1: [0-7] -> tviburiDma1@[0-7]` update, exactly once per request (no delivery to the cancelled entry, unlike `1302`'s phantom combiners).
- **DMAMUX1 needs no restatement for this:** it is not cancelled, so the consumer's `DMARecieve -> dmamux1@37`/`@39` still bind. A DMAMUX1 guard would have to cancel it, and then every consumer reference to `dmamux1` (each SPI's `DMARecieve`, the ADCs' `dmaPeripheral`) binds to the cancelled entry unless repointed (`RUL-09` (b)).
- **The prices, as for the RCC:** the access log names the wrapper's registers `unknown`, and the owned model's warnings lose their source name.
- **A wrapper sees every width:** D14's byte and half-word writes reached it (the probe merged them; the stock model drops them).

### 2.4 Consumer observers each write reaches

- **Generated boots:** none (no access).
- **ArduPilot's console.** Aethelgard logs `usart1`'s accesses and reads the serial output from them. **USART1 TX is a DMA stream:** its 547 `TDR` byte writes in 5 s are the stock model's synchronous copy at each `EN` write, and the access log stamps each with the PC of that `CR` store (`0x080FA1FA`). **A guard that refused, deferred or paced those transfers would change what the consumer reads.**
- **ArduPilot's sensors.** SPI1 (19 exchanges, at 0.16 s) and SPI2 (2,354, the IMU, from 4.35 s) run by DMA, with the RX side paced by the stock SPI's requests. Their results reach the consumer only through ArduPilot's own behaviour and console.
- **The boot block** reads `USART1_CR3` under a mask without `DMAR`/`DMAT`, and no DMA register (Aethelgard `twin/src/boot.rs` L938).
- **The motor witness** names "a DMA write of `CR1` is not observed" (`aethelgard-cli/src/renode.rs` L3471): it anchors only on CPU stores. No firmware writes a timer by DMA.

### 2.5 The timer's DMA side (WO (3))

- **The stock timer never raises a request** (D10: `UDE` = 1 and `CEN` for 1 ms, `NDTR` unchanged), and **no firmware asks for one:** neither ArduPilot routes a timer id (11–32, 47–60, 105–112) through DMAMUX1; the generated DShot boards use no DMA (Aethelgard `svd-hal` README L64); AP-m runs 50 Hz PWM with `DIER` = 0 (`1302`).
- **And a memory-to-peripheral stream needs no request in the stock model:** a DShot burst to `TIMx_DMAR` would be copied whole at the `EN` write (D9), unpaced.
- **So (3) stays a row:** `UDE`, `CCxDE`, `COMDE`, `TDE` and `DCR`/`DMAR` stay `1302`'s `unmodeled`; DShot is unwitnessable until a model raises the timer's requests and the DMA paces memory-to-peripheral transfers by them.

### 2.6 The clock (WO (2))

- **An access to DMA1/DMA2 with its clock off is not refused:** the RCC guard locks the peripheral on the bus, the write is dropped and a read returns 0, with a `WARNING` and **no Tviburi record** (D1; ArduPilot's 20 writes at 0.92 ms). That is silicon's ignored write (p373), not a refusal. `1300a` keeps only SPI guards unlocked and refusing; extending that to DMA would refuse ArduPilot at 0.92 ms (`1300a` §1.5). **So "covered" holds as silicon's behaviour, and "refuses" does not.**
- **DMAMUX1 is never locked** (D1b), though `DMA1EN`/`DMA2EN` clock it (p455). Neither firmware writes it unclocked.

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | held; the TCM restriction is in the held text (p108), so §7's third stop does not fire |
| P2 form, transfers, tags, DMAMUX1 accepting any id, never locked | held (D0–D14) |
| P2 "re-fires on a rewrite with `EN` = 1" | held (D3) |
| P2 **requests** | **wrong in part:** the consumer's platforms wire SPI1/SPI2 RX (37, 39), and those streams run; USART, timer and I2C requests are absent as predicted |
| P2 timer | held (D10) |
| P3 | held: 0 accesses in all 14 |
| P4 before the clocks | held (dropped; reset values) |
| P4 streams | held in kind (USART1 RX/TX, SPI1, SPI2: six streams), no I2C |
| P4 order | held: configuration before `EN`, every time |
| P4 disable | held: one write clearing `EN` and `TCIE` under `EN` = 1 |
| P4 re-arm after completion without a disable | held: never |
| P4 addresses | **wrong in region, right in kind:** all SRAM4, not AXI or SRAM1–3; none in the TCMs |
| P4 USART1 RX never moves; TX moves at `EN` | held |
| P4 "SPI RX never completes" | **wrong:** it runs (the consumer wires the request) |
| P5 | held |
| P6 | held in kind; the observer the guard can reach is USART1 TX's console bytes (§2.4) |
| P7 | held: a silent drop, not a refusal |
| P8 | held (§2.3); the routes need no DMAMUX1 restatement |
| P9 | **wrong in size:** +8.0 MiB for the thin wrapper, not "under +5" (§4) |

## 4. The one-file memory, by `1201`'s budget (B)

**A prototype figure, not the guard's:** the §2.3 wrapper (no rule, no table, its per-access logging removed), instantiated as `dma1` and `dma2` in every branch boot (`peripherals`: `dma1 (ProbeSTM32DMA)`, `dma2 (ProbeSTM32DMA)`; main: the stock `STM32DMA`). **Like for like:** both sides carry `1302`'s timer guards, whose collection at the first start reclaims the whole overlay's compile garbage on both sides. `tests/1301/ci-memory.sh` from a scratch checkout whose replay appends the wrapper's restatement; ArduPilot's 5 s boot (ELF `a799dfb6`, rebuilt and checked), eleven boots a side, interleaved, on Furnace (load 15–25 from other builders).

| | Peaks (KiB), in boot order | Median |
|---|---|---|
| `main` (`bb50435`'s tree: `main` `4d9a063` and this document) | 986,828; 981,912; 989,856; 981,168; 995,848; 989,872; 998,412; 990,584; 1,005,180; **1,818,296**; 1,047,852 | 990,584 |
| `main` + the wrapper as `dma1`/`dma2` | 973,580; 1,005,148; 982,160; 989,140; 981,364; 998,744; 1,016,592; 1,013,512; 1,012,788; 989,620; 1,080,308 | 998,744 |

`MEDIANS main 990584 KiB, branch 998744 KiB, difference 8160 KiB (+8.0 MiB)`, **PASS**. Every boot counted; every branch boot is under the 1,572,864 KiB ceiling. One main boot (10) peaked at 1,818,296 KiB, all of it anonymous memory (`RssAnon` 1,728,492), with a wall of 40.1 s against about 11 s for the others: the roadmap's row "Renode's peak memory rises with wall time" on a contended host. The ceiling is judged on the branch's boots, so it does not count; it is reported, not explained.
- **More than P9's "under +5".** The thin wrapper is two instances of a small class, so most of the +8.0 is the class's compiled code, which the collection cannot reclaim. **The built guard will cost more** (rules, a field table, records), and `1302`'s like-for-like +11.6 MiB is the nearest measured guide. The build reports the budget again, in CI.
- **The form control, from the same boots:** in all 11 pairs, ArduPilot's USART1 `TDR` writes (547, with their stamps, PCs and values: what the consumer reads as the console) and the boot block (instruction count included) are **identical** between `main` and the wrapper (`h/form-control.sh`). The wrapper changes nothing the consumer reads.

## 5. The stop (WO §1.1, §7), and the questions

**No §7 stop fires on the measurement as it stands.** The TCM restriction is in the held text (p108). Neither firmware gives a stream a TCM address, writes a protected field under `EN`, or routes a reserved request (§2.1). **Three readings would make ArduPilot's boot refuse on a path RM0433 calls valid,** so each is a question, not a build choice: Q2 (p665's general sentence), Q3 (`EN` as the stock model holds it) and Q5 (c) (a request no model raises).

**Q1. The form.** `STM32DMA` is sealed (§2.3).
- **Proposal:** a wrapper for `dma1` and `dma2`, restated as `1302` did for the timers. Each is cancelled, declared `as "dmaN"` on `tviburiRcc` with the stock entry's interrupt lines, and given the routes by an untyped `dmamux1:` update.
- **DMAMUX1 is not guarded.** The routing rules (Q5) run at each stream's enable, reading that stream's channel from the stock `dmamux1`, which is handed to the wrapper as a property. So the consumer's `DMARecieve -> dmamux1@37`/`@39` and the ADCs' `dmaPeripheral: dmamux1` stay bound, and the consumer repoints nothing for DMAMUX1.
- **DMAMUX1's own gaps become rows:**
  - a write with both clocks off is taken (p455, p373; D1b);
  - byte and half-word writes are dropped, though silicon takes them (p708; D12);
  - its synchronisation, event and generator fields are tags (Q6 refuses them at the stream's enable).
- **Ask:** accept this form, or guard DMAMUX1 itself? Guarding it means cancelling and restating it, with every consumer reference to `dmamux1` repointed.

**Q2. Writes under `EN`: per field, or p665's sentence.**
- **The two readings.** pp. 662–667 state per field which bits "can be written only if EN = 0". `EN`'s own text (p665) says "It is forbidden to write these registers when the EN bit is read as 1".
- **Under p665, ArduPilot's boot refuses.** Every one of its disables (§2.1) is a `CR` write from `EN` = 1 that also clears `TCIE`, which has no line of its own (p664). The first is at 4.6 ms (USART1 TX).
- **Recommendation: per field.** A `predicted-fault` (`RUL-18`: silicon does not do what the firmware wrote) for a write, while the stream is enabled (Q3), that **changes** a stated field:
  - `CR`: `MBURST`, `PBURST`, `CT`, `DBM`, `PL`, `PINCOS`, `MSIZE`, `PSIZE`, `MINC`, `PINC`, `DIR`, `PFCTRL`;
  - `NDTR`, `PAR`, `M0AR` and `M1AR` (their double-buffer exceptions stay unreachable while `DBM` refuses, Q6);
  - `FCR.DMDIS`.
- **The edges.**
  - The same value passes (`RUL-18` as amended).
  - The field keeps its old value, which is what recorded mode passes on.
  - `TCIE`, `HTIE`, `TEIE`, `DMEIE`, `CIRC`, `TRBUFF`, `FTH` and `FEIE` take the write; whether their values are modeled is Q6's question.
  - `EN` set together with a protected change, from `EN` = 0, is a configuration and passes. From `EN` = 1, the change refuses.

**Q3. What "enabled" means.**
- **The gap.** The stock model never clears `EN` (D2), while silicon clears it at the end of a transfer, "stream ready to be configured" (p665).
- **The false refusal.** A rule keyed on the stock's `EN` refuses a firmware that reprograms a stream after it completed without writing `EN` = 0. That is valid on silicon. Neither firmware does it, and a planted one would.
- **Recommendation:**
  - the guard clears `EN` when the owned model completes the transfer, writing `EN` = 0 into the stock `CR` and serving `EN` from its copy. Completion is `NDT` reaching 0; a memory-to-peripheral or memory-to-memory transfer completes at the `EN` write itself (D2);
  - the rule keys on that `EN`;
  - **the price is a row:** the stock model compresses a memory-to-peripheral transfer into the `EN` instant, so a write silicon would see mid-transfer lands after completion in the twin, and passes.
- **Not measured: whether clearing `EN` changes ArduPilot's run.** ChibiOS clears `EN` itself after every transfer and waits on it (§2.1). The build re-runs both ArduPilots recorded and compares the console bytes and the boot block with `main`, and **stops if any differs**.
- **Ask:** model the clear as proposed, or key the rule on `EN` as written and accept the false refusal as a row?

**Q4. TCM (`predicted-fault`, p108, Table 7 p131).**
- **The rule:** at the write that sets `EN` (0 → 1), refuse if any byte the stream would address lies in ITCM (`0x0000_0000`–`0x0000_FFFF`) or DTCM (`0x2000_0000`–`0x2001_FFFF`). The record names the stream, the address, the region and p108.
- **The bytes it checks:**
  - the memory side, `M0AR` over `NDT` × `MSIZE` when `MINC` = 1, or `M0AR` alone;
  - `PAR` the same way. Both buses are excluded, so a peripheral-to-memory stream given a TCM `PAR`, and a memory-to-memory source in TCM, refuse too.
- **Not in the rule:**
  - `M1AR`, because `DBM` refuses first (Q6);
  - an address written after `EN`, which refuses under Q2.
- **Ask:**
  - the kind for an address in Table 7's `Reserved` ranges, or with nothing mapped. I propose `unmodeled`.
  - The bench row `RUL-18` requires: a DMA1 memory-to-memory transfer from DTCM on the Nucleo, expecting `TEIF` set, `EN` cleared and the destination unchanged. That expectation is from memory, so the row is written with no result.

**Q5. Routing through DMAMUX1, checked at each stream's enable.** M→M uses no request and is exempt.
- **(a) `predicted-fault` when the channel holds no request:** a reserved id (54, 116–127; Table 124 pp. 697–698), or 0, "no DMA request line selected" (p703).
  - On silicon, nothing moves. A stock memory-to-peripheral stream moves everything at `EN` (D9), the false pass this rule exists for.
  - Tables 124 and 125 are printed twice, identically (read from the rendered pp. 697–699, `~/.cache/tvi-1303/pdf/`). No footnote or caption distinguishes them, and both list the same reserved ids, so citing Table 124 loses nothing.
- **(b) `predicted-fault` for one non-null id on two channels whose streams are both enabled:** "a same non-null DMAREQ_ID cannot be programmed to different x and y DMAMUX request multiplexer channels … except when … not simultaneously active" (p704). The stock model delivers the request to both (D13).
- **(c) A valid id that no model raises** (every id except 37, 39, 77, 83, 9 and 115 on the measured platforms). ArduPilot enables USART1 RX on id 41 at 3.1 ms.
  - Refusing it `unmodeled` refuses ArduPilot's boot.
  - **Recommendation:** a ledger row acknowledged by name (`RUL-17`): a peripheral-to-memory stream on a request no model raises moves no data in the twin, so a firmware that receives by DMA sees no reception.
  - A memory-to-peripheral stream on a valid id moves at `EN`, unpaced. That is a second row (`stock-wrong`). ArduPilot's USART1 and SPI TX depend on it, and the consumer reads USART1 TX's bytes (§2.4).
  - The USART RX request belongs to a USART guard (the `OQ-03` arming path).
  - **Ask:** acknowledge both, or refuse?
- **(d) `unmodeled`** for ids 1–8 (DMAMUX1's request generators, all tags) and for a channel whose `SE`, `EGE`, `SOIE`, `SPOL`, `NBREQ` or `SYNC_ID` is non-zero.

**Q6. The allow-list (`RUL-17`), proposed classes:**
- **modeled by the guard:**
  - `CR.EN`, with Q3's clear;
  - Q2's protected fields;
  - Q4's addresses;
  - Q5's routing.
- **inherited, probed** (§2.2, AP):
  - `DIR` 00, 01 and 10;
  - `MINC` and `PINC`;
  - `PSIZE`/`MSIZE` 00 and 10. 01 is by source only, so 01 is allowed only if the build probes it;
  - `NDT`, `PAR` and `M0AR`;
  - `TCIE` with `TCIFx`/`CTCIFx`;
  - `FCR` = `0x21` (`DMDIS` 0, `FTH` 01, served as silicon's reset value; the stock model resets to `0x00`).
- **acknowledged by name** (ArduPilot writes them; the stock model drops them):
  - `CR.PL` (01; priority is not modeled);
  - `TEIE` and `DMEIE`: no transfer or direct-mode error is modeled, so neither interrupt ever fires;
  - `TRBUFF`;
  - `LIFCR`/`HIFCR`'s `CHTIF`, `CTEIF`, `CDMEIF` and `CFEIF`, whose flags never set.
- **`unmodeled` at any value but reset:**
  - `HTIE` (the half-transfer flag never sets);
  - `CIRC`, `DBM`, `CT` and `M1AR`;
  - `PBURST`, `MBURST`, `PFCTRL` and `PINCOS`;
  - `FCR.DMDIS` = 1 and `FTH` ≠ 01 (FIFO mode; the WO names FIFO thresholds);
  - `FEIE`;
  - `DIR` = 11 and `PSIZE`/`MSIZE` = 11 (reserved).
- **`unmodeled` for any byte or half-word access** to DMA1/DMA2 ("accessed by words", p659; `1302`'s `TVI-TIM-WIDTH` precedent).
- **Served from copies on read:** every `CR` field, so ChibiOS's read-modify-write keeps `PL`, `TEIE`, `DMEIE` and `TRBUFF` as on silicon (§2.1), and `FCR`.
- **Neither firmware writes a field this list refuses,** so I predict 0 DMA `would-refuse` on ArduPilot's recorded boot.

**Q7. The clock (WO (2)).**
- **An access with `DMA1EN`/`DMA2EN` = 0 is a silent drop, not a refusal** (§2.6). The WO's "already refuses through `1300a`'s lock" does not hold.
- Making it refuse would refuse ArduPilot at 0.92 ms, which `1300a` declined to do.
- **Proposal:** a test shows the drop and the absence of any record, and the case is named covered as silicon's ignored write (p373), not as a refusal. DMAMUX1's missing lock is a row (Q1).
- **Ask:** is that what "show the case is covered" means?

**Q8. (3): a row.**
- The stock timer raises no request (D10), and no firmware asks for one (§2.5).
- `UDE`, `CCxDE`, `COMDE`, `TDE` and `DCR`/`DMAR` stay `unmodeled`.
- `GuardedSTM32_Timer.cs` is untouched.

**Q9. Fences.**
- **(a) The guard-name lists.** The witness and end records add `dma1 dma2` to every guard-name list: `scripts/tviburi-spike`, `tests/1300/rcc.sh`, `tests/1301/gpio.sh`, the six in `tests/spike.sh`, and `tests/mutants.tsv` G17. `1302` asked and was granted this (06:52Z). **Ask:** granted again?
  - `tests/spike.sh` waits for `1202`.
  - `scripts/tviburi-spike` is outside this WO's `scripts/` fence (`prelude.resc` only).
- **(b) The replay.** `tests/1301/replay-1301.sh` appends the DMA restatement, as it does the timers', so that the budget and the acceptance replays instantiate the guard. This is inside the `tests/` fence.
- **(c) The class row.** "The class row naming its instances" is `tools/tviburi-release`'s reader and `pins.tsv`, with a manifest test. I read it as `class DMA.STM32DMA Tviburi.GuardedSTM32DMA dma1 dma2`, applied to this row only. Rewriting the timer and SPI rows is left alone, which keeps it inside the fence.

**Rows, outside this WO:**
- **ArduPilot's USART RX has no request source.** No model raises `usart1_rx_dma`: a USART guard's, with `OQ-03`'s arming.
- **The twin does not pace memory-to-peripheral transfers.** A transfer completes at `EN`, with no request needed (D2, D9). DShot's unwitnessability is one consequence.
- **Hi-Z and errors.** No transfer error, FIFO error, direct-mode error or half-transfer is ever raised.
- **For Aethelgard (a finding, not a change):** the consumer's `usart1` access log stamps a DMA-moved byte with the PC of the `EN` store, not of any instruction that moved it.
