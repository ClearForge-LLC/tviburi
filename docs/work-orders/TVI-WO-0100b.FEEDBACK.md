# TVI-WO-0100b — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-0100b-spi1-guard` from `main` `dc55f98`.
**The measurement and its rulings:** `TVI-WO-0100b.rm0433.md` (the §1.1 stop, ruled on the bus 2026-11-23T11:00Z).

## 1. Bench checks, one per `predicted-fault` (`RUL-18`), until `TVI-WO-1200`'s ledger holds them

Each runs on NUCLEO-H753ZI SPI1 with its kernel clock on, from reset, and reads back what silicon did. None has been run.

| # | Refusal | Procedure | Result that confirms the kind | Result that refutes it |
|---|---|---|---|---|
| B1 | `TVI-SPI-MODF`, `CR1.SSI` = 0 | the `modf` firmware: `CFG2` = `MASTER`\|`SSM`, then `CR1` = `SPE`; read `SR`, `CFG2`, `CR1` | `SR.MODF` = 1, `CFG2.MASTER` = 0, `CR1.SPE` = 0 (p2184) | `MODF` = 0 with `SPE` = 1 |
| B2 | `TVI-SPI-CFG2-SPE` | enable as the `modeled` firmware does, write `CFG2` with `SSIOP` = 1, read `CFG2` | `CFG2` unchanged (p2217, p2221) | `SSIOP` reads 1 |
| B3 | `TVI-SPI-IOLOCK`, latch certain | the `iolock` firmware: `CFG2` = `MASTER`, `CR1` = `IOLOCK`, `CFG2` = `MASTER`\|`SSM`; read `CFG2`, `CR1` | `CFG2` = `MASTER` only; `CR1.IOLOCK` = 1 (p2215) | `SSM` reads 1 |
| B4 | `TVI-SPI-CFG1-SPE`, a protected field | enable, write `CFG1` with `MBR` changed, read `CFG1` | `MBR` unchanged (p2217, p2218) | `MBR` reads the new value |
| B5 | `IFCR` reserved bits (A9; acknowledged, `unmodeled`) | ArduPilot's start: `IFCR` = `0xFFFFFFFF` at `SPE` = 0, then enable and transfer | no flag or behaviour changes: the twin's assumption holds | any effect of a reserved bit |
| F1 | `0100a` F1, unchanged | is `MODF` evaluated at `SPE` = 0? | | |

## 2. Findings for the architect (the steering documents are unchanged)

1. **`RUL-17`'s reason calls `LSBFRST` a tag.** At `066a7f13` it is a stored flag that the transmit path uses as byte order (`STM32H7_SPI.cs` L195, L352), not bit order (RM0433 p2222). Only 0 is allowed. (Ruled 11:00Z: the architect corrects `RUL-17`.)
2. **The stock `IOLOCK` is inverted** (L133–142): it latches only while `SPE` = 1, and survives `SPE` 1→0 (measured, `rm0433.md` §1.3). The guard serves `IOLOCK` from its own latch.
3. **The stock `CFG1` reads `0x0` after reset,** where silicon reads `0x00070007` (p2218); `CRCPOLY` reads `0x0` where silicon reads `0x107` (p2228). Both are now served from the guard's copies.
4. **Aethelgard's foreign run hooks `spi1` and `spi2` writes** (`AETHELGARD-CFG2`), which displaces `LogPeripheralAccess`. A hook does not displace the guard (`0100a`'s `adv-hook`); whether Aethelgard's hook and a guarded `spi1` coexist is Phase 2's to measure.
5. **The expected version a consumer computes** (`tests/model-version`) needs both build strings the model hashes: `Renode.dll`'s `1.17.0+20260907gitf1dd1b4af.aet1605` and `Infrastructure.dll`'s `1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2`, read from the assemblies in both pinned images (`e5fc7ddb…` and `1e0ab105…` carry the same `Infrastructure.dll`, sha256 `ddb19d7b…`). The second is not the build string `renode --version` prints. `TVI-WO-1200` replaces this with the manifest hash.
6. **Two adversarial additions, accepted 11:45Z:** the `IER` copy served on read (`OVRIE`, acknowledged, is a tag that reads 0, so a read-modify-write would drop it), and `TVI-SPI-ORDER`, `unmodeled`: `TXDR` with `SPE` = 0 (the stock model queues the data and sends it at the enable, where p2217 flushes the FIFOs), `CR2` with `SPE` = 1 (p2218), `CSTART` with `SPE` = 0 (p2216). Neither firmware meets them. `RUL-17`'s reason names the first.
7. **Inherited `IER` enables.** `RXPIE`, `TXPIE` and `EOTIE` are stock-implemented and allowed as inherited and unaudited, as ruled for B, although neither firmware sets them.
8. **The console fixture is copied** from `1150`'s `ef87d9b` into `tools/tviburi-check/tests/fixtures/aet-7dec1d7b/`, pinned by sha256. When `1150` is on `main`, the copy can give way to `spike/1150/fixtures/aet-7dec1d7b/`: a reduction for later, not made here, because `1150` is not merged.
9. **DS12117 Table 9's alternate-function columns** were not read reliably by position on every page. The routing answer rests on pin identity (`rm0433.md` §1.2). A GPIO guard built from that table (`RUL-13`) needs a better reading than `txtwrite`'s layout.
10. **Not modeled, measured:** `IOLOCK`'s hardware clear on `SPE` 1→0 (neither firmware sets `IOLOCK`); `SR.MODF` reads 0 while the D1 history is set (as built, `0100a` F2; `SR` is never read by either firmware); `TXCRC`, `RXCRC` and `IFCR` reads come from the stock model (none is read by either firmware).
