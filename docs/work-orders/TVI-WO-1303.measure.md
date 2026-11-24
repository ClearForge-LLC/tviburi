# TVI-WO-1303 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1303-dma-guard`, from `main` `4d9a063` (`1302` merged).
**State:** §0 written and committed before any run. §1 onward follows the runs.
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
