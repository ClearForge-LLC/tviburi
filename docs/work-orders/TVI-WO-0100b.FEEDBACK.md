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
6. **Adversarial additions.** Accepted 11:45Z: the `IER` copy served on read (`OVRIE`, acknowledged, is a tag that reads 0, so a read-modify-write would drop it), and `TVI-SPI-ORDER`, `unmodeled`: `TXDR` with `SPE` = 0 (the stock model queues the data and sends it at the enable, where p2217 flushes the FIFOs), `CR2` with `SPE` = 1 (p2218), `CSTART` with `SPE` = 0 (p2216). Neither firmware meets them. `RUL-17`'s reason names the first.
7. **Inherited `IER` enables.** `RXPIE`, `TXPIE` and `EOTIE` are stock-implemented and allowed as inherited and unaudited, as ruled for B, although neither firmware sets them.
8. **The console fixture is copied** from `1150`'s `ef87d9b` into `tools/tviburi-check/tests/fixtures/aet-7dec1d7b/`, pinned by sha256. When `1150` is on `main`, the copy can give way to `spike/1150/fixtures/aet-7dec1d7b/`: a reduction for later, not made here, because `1150` is not merged.
9. **DS12117 Table 9's alternate-function columns** were not read reliably by position on every page. The routing answer rests on pin identity (`rm0433.md` §1.2). A GPIO guard built from that table (`RUL-13`) needs a better reading than `txtwrite`'s layout.
10. **Not modeled, measured:** `IOLOCK`'s hardware clear on `SPE` 1→0 (neither firmware sets `IOLOCK`); `SR.MODF` reads 0 while the D1 history is set (as built, `0100a` F2; `SR` is never read by either firmware); `TXCRC`, `RXCRC` and `IFCR` reads come from the stock model (none is read by either firmware).
11. **For a ruling: a `CFG2` write with `SPE` = 1 of the value `CFG2` already holds** refuses `TVI-SPI-CFG2-SPE`, `predicted-fault`, although silicon then holds what the firmware wrote. `RUL-18` names "a `CFG2` write with `SPE` = 1" as `predicted-fault`, so it is built as ruled; `CFG1` exempts the equal value (C3). Neither firmware does it.
12. **Backlog (the scope test; neither firmware meets any):**
    - the stock model zeroes `CR2.TSIZE` when `SPE` falls (L103). Whether silicon keeps it is not on p2217–2218, so this is an inference. A re-enable without rewriting `CR2` would run an endless transfer in the twin;
    - `IFCR.EOTC` clears `CSTART` and the packet count in the stock model, where p2227 says it clears `EOT`. It is reachable only while a transfer is ongoing;
    - the stock FIFOs never fill (`TXP` is always 1). `RXDR`'s side is acknowledged (A4, A7). `TXDR`'s 17th frame before `CSTART` is not refused;
    - `tests/model-version` skips symbolic links, which `GetFiles` counts. A version mismatch then fails closed.
13. **Refuted by measurement:** the independent review read an RCC reset of SPI1 as leaving the guard's copies set. `0100a` F3 measured that `APB2RSTR.SPI1RST` calls the guard's `Reset()`, and `conditions` case 15 still shows it.
14. **The generated firmware's reads come from its board crate, not from a replay.** Aethelgard `b9d5f4b6`, `boards/sentinel-01/src/board.rs` L255–285 and L632–716: 32-bit `SR` polls and 8-bit `RXDR` reads, no read-modify-write of an SPI register. The planted `generated` firmware makes them.

## 3. The adversarial pass (WO §5)

Run twice: by me, and by an independent reviewer on Opus who had the code and not my conclusions. Every reviewer finding was spot-checked against the code before it was acted on.

- **A field outside the allow-list that still passes:**
  - every bit of every register is in a row (checked by enumeration);
  - a sub-word access passes only at `TXDR` and `RXDR`, and only at the frame's width;
  - a read-modify-write cannot carry a stale bit, because `CR1` (with `IOLOCK` from the latch), `CFG1`, `CFG2` and `IER` are served from copies, and `CRCPOLY` reads as `0x107`. `IER` was a stale-value route, and is fixed.
- **Paths around the allow-list, now refused:**
  - **`TVI-SPI-ORDER`:** `TXDR` with `SPE` = 0, `CR2` with `SPE` = 1, `CSTART` or `CSUSP` with `SPE` = 0.
  - **`TVI-SPI-READ`, reads with a value silicon would not give:** an offset with no register; the write-only `TXDR` and `IFCR` (the stock `IFCR` reads back `SUSPC`); `RXDR` with the RxFIFO empty; and `SR` after `CSUSP`, where the stock model sets `EOT`. That last one was a concrete false pass: firmware waiting for `EOT` after a suspend passes in the twin and hangs on silicon (p2225, p2226).
- **A wrong kind, fixed:**
  - `IOLOCK`'s kind is now evaluated at the refusal, not when the latch is taken.
  - `MASTER` unknown is checked before the truth table, so `SSI` = 0 after `MODFC` is `unmodeled`, not `predicted-fault`.
- **A record that yields a pass:** each of these fails, by a unit test and a run:
  - a record from an unnamed source, a recorded-mode or mode-less witness, a `would-refuse` record, a wrong or missing version argument;
  - a record line the check cannot frame (a prompt prefix, a broken stamp). That used to be skipped, so a refusal behind a prompt would have been lost; it now fails the run.
- **A log line under `spi1` whose text the firmware controls:** none. The stock model's log calls (L137, L192, L288, L302, L333, L404), Renode's "Unhandled" and tag lines, and `LogPeripheralAccess` lines all start with fixed text, and the firmware controls only hex values in them. A child device logs under its own name, which the check fails. The guard escapes every string it records.

## 4. The reduction pass (diff), by a fresh subagent; I applied what is below

- **Taken:**
  - two `conditions` writes that `fields.resc` already covers;
  - a duplicate unit assertion;
  - two `REFUSAL` greps that the runner's exit 0 already implies;
  - `ioLockCertain`'s write-time doubt, which the refusal-time test covers, since a doubt at latch time lasts while latched;
  - `Cr1Stock` from the named bits.
- **Kept:**
  - the console fixtures byte-for-byte (provenance);
  - the explicit `would-refuse` arm and its test (the architect asked for them by name);
  - two cuts that were taste, not reduction.
- **Made earlier, at the advisor's suggestion:** the `CRCPOLY` copy, which could only ever hold `0x107`, became the constant.

## 5. A flaky test from `0100a`, made deterministic

`adv-noend` (`0100a`) failed once in about five runs on Furnace. Its fixture stops the script with an unknown monitor command. Renode then exits on standard input's EOF, and it sometimes exits before it disposes the machine: no `Disposed` line, no end record, exit 0. The check then fails as well, so the run was still not a PASS. But the test's claim, that the check passes and only the runner's end-of-script evidence catches the run, did not hold that time.
- The strong claim now uses a script that quits cleanly without its end marker, which is deterministic and still kills `S04`.
- The monitor-error route stays as its own case, asserting only that the run does not PASS.
- Nothing was weakened: both claims are kept, each on a route that shows it every time.

## 6. Gates (WO §2), on `08a559f`, run on Furnace

Every container ran under `capped-run` at `nice -n 10`, one at a time, with `--network none`, in the image re-pinned to `1e0ab105…`.

- **ArduPilot's SPI start** (`spi_lld_start` through `spi_lld_stop`, twice, with each read-modify-write as decoded from the ELF): **PASS, enforcing.** Its enable carries `MASRX` as silicon would (`CR1` = `0x101`). **The generated firmware's SPI1 sequence,** with its `SR` and `RXDR` reads: **PASS.**
- **Every field outside the allow-list refuses** `TVI-SPI-FIELD`, `unmodeled`, by name: 58 cases from the monitor (`fields`), and one planted firmware (`field`, `CFG2.CPOL`) (gate clause 6).
- **The `MODF` scar still refuses,** `predicted-fault`, citing RM0433 Rev 9 §50.5.2 p2184 (gate clause 4). Every other branch refuses with its kind, in order (`conditions`, 44 refusals).
- **The check exits non-zero** on:
  - a wrong expected version, or none (`adv-version`);
  - a record from an unnamed source (`adv-version`, unit);
  - a recorded-mode witness (`adv-recorded`, unit), a mode-less witness (unit), a `would-refuse` record (unit);
  - a record it cannot frame (unit).
  
  It reads `1150` (ii)'s console fixture in both framings (unit; gate clause 7).
- **Suites:** acceptance and adversarial, 48 checks, all pass. Reference check: 20 unit tests.
- **Mutation, diff-sized:** 92 mutants, one per guard this work order adds or changes, plus `0100a`'s rows re-anchored. **All 92 killed.** Two mutants survived dry runs and drove fixes: `N13` (`IOLOCK` certainty, no case isolated it) and one wrong-kind route.
- **Reduction pass (diff):** −5 lines net · 2 duplicates removed · 0 dead paths removed · 1 protected kept (the console fixture's verbatim copy) · 2 findings (§4).
- **CI:** on the PR.
