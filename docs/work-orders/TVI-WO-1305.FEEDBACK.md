# TVI-WO-1305 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1305-usart-guard`, from `main` `1e9ce55`; `main` `9f5436d` (with `TVI-WO-1304`, the flash guard) merged before the build.

**State:**
- §1.1 was measured and stopped (`TVI-WO-1305.measure.md` §0–§5, predictions committed at `dff6e77` before any run).
- Ruled on the bus: the rulings headed 18:55Z (a hand-typed stamp, corrected at 19:13Z: it went out before 18:42Z), and the confirmations at 19:13Z.
- The guard is built to the rulings. No §7 stop is open.
- The ledger's model row was split for a partly guarded type (granted outside the fence at 19:57Z): a **ledger-format change**, schema 1 → 2 (§2a).
- Untouched: `model/Refusal.cs`, the record grammar, the RCC guard (its query is only called), the SPI/GPIO/timer/flash guards, the steering documents and Aethelgard.
- The DMA guard has exactly the three ruled items and the narrowed `TVI-DMA-OFFSET` (§2).
- One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| **The guard,** `RUL-07`'s wrapper over the sealed `STM32F7_USART` (owns an unregistered stock instance). It forwards every access it passes, the `IUART` surface (`WriteChar`, `CharReceived`: the consumer's injection and file backend) and reset. It drives its own `IRQ` (the stock interrupt ORed with its `IDLE`) and its own `ReceiveDmaRequest`. A nested `Registers` enum keeps the access log's register names | `model/GuardedSTM32F7_USART.cs` |
| **`TVI-USART-UE`:** a protected field ("can only be written when the USART is disabled (UE = 0)", pp. 2067–2083, 2099) changed with `UE` = 1, `unmodeled` (ruled). The same value passes. A `CR1` write that clears `UE` is judged as with `UE` = 0. **The ChibiOS re-init pattern** (a change confined to `BRR`, `CR1.M0`/`PCE`/`FIFOEN`, `CR2.RXINV`) is acknowledged by name with no record and taken as the stock model takes it, with bench row U1. The record names only the fields outside the pattern | the same |
| **`TVI-USART-KERNEL`:** a start (`UE` with `TE` or `RE`, from a state with none) asks the RCC guard's existing `QueryKernelClock`. Not running is `predicted-fault` (p2019, p2031, p371 and the query's pages); no rule, or no RCC guard, is `unmodeled`. No RCC code added | the same |
| **`TVI-USART-WIDTH`, `-READ`, `-FIELD`, `-CONFIG`:** sub-word accesses but `RDR`/`TDR` (ruled Q7); offsets with no register; the allow-list, plus `M0` = 1 with `PCE` = 0 (9 data bits); `lowPowerMode` (LPUART1). `ISR` and `RDR` writes are ignored with no record (read-only) | the same |
| **Modeled, cited:**<br>• **`IDLE`**: one idle frame after a received byte (start bit, data bits by `M`, stop bits by `STOP`, at the stock baud), cancelled by a newer byte or by `UE` or `RE` falling; never at a start (ruled Q5); `IDLEIE` raises the interrupt; `IDLECF` clears it (pp. 2021, 2090, 2097).<br>• **The receive request** as a level: asserted while `DMAR` = 1 and a byte waits, again after each `RDR` read, and once per frame while a byte waits (p2048).<br>• **`UE` = 0** flushes the queue and clears `IDLE` (p2070).<br>• **No clock, no reception:** a byte arriving while the query says the port's clock does not run is not received (p470, p2019, p371) | the same |
| **Reads:** `CR1`, `CR2`, `CR3`, `GTPR`, `PRESC` from copies (so ChibiOS's read-modify-write keeps `FIFOEN`, `IDLEIE`, `RXINV`, `LBDIE`, `EIE` as silicon); `ISR` from the stock model with the guard's `IDLE` | the same |
| **The allow-list:** 138 rows over 12 registers (every bit 31:0), extracted from RM0433 Rev 9 §48.8's held text (FIFO-enabled views) and classed by the ruling. `usart-regen` checks it byte for byte | `tests/1305/extract.py`, `tests/1305/fields.py`, `tests/1305/data/rm0433-rev9-usart-fields.tsv` |
| **The restatement:** the eight USART/UARTs cancelled and restated on `tviburiRcc` (generated from the pinned `stm32h7.repl`; `lpuart1` stays stock); the board's include and modes; the class row naming the eight | `model/stm32h7-usart-restate.{awk,repl}`, `model/tviburi-nucleo-h753zi.repl`, `tools/tviburi-release/pins.tsv` |
| **Bench row U1** (priority, ruled): each acknowledged field written with `UE` = 1 mid-reception on the Nucleo, read back and observed on the line. Result "not run" | `model/bench-checks.tsv` |
| **Cases:** 30 `usart-<case>` in `tests/spike.sh`'s acceptance group, 19 planted firmware paths (each refusal with its passing twin); DMA cases `offset` (rewritten), `offsetndt`, `reflect`, `usart23` | `tests/1305/usart.sh`, `firmware/usart.rs`, `tests/1303/dma.sh`, `firmware/dma.rs` |
| **The replay** restates the eight guards and moves the consumer's untyped `usartN:`/`uartN:` blocks onto them; `ACCEPT_USART_DMA` wires a receive request, standing in for Aethelgard `4400i` | `tests/1301/replay-1301.sh` |
| **Mutants** US01–US35, DM43–DM49 (DM41 retired with the old `TVI-DMA-OFFSET`; DM25 retargeted to the extended request list) | `tests/mutants.tsv` |
| The guard-name lists (×8 names), the citation rows | `scripts/tviburi-spike`, `tests/spike.sh`, `tests/130*/…`, `tests/ci/citations.tsv` |

## 2. The DMA fence (ruled Q6 (b), 19:13Z)

- **`TCIF` on a software clear of `EN` while a stream runs** (§15.3.16 p654), and **the stream's next transfer at its programmed addresses** (p654, p666), in place of the stock model's carried `dataOffset`. Both go through the stock `STM32DMA`'s private members by reflection: `streams`, `transferCompleteIrqStatus`, `UpdateInterrupts` and `Stream.dataOffset`.
- **Coupling to Renode internals, to re-check at any Renode bump:** those four member names and types. A build without them refuses every DMA access as `TVI-DMA-CONFIG` (`dma-reflect` plants a missing member and shows it by name; mutant DM48). It never passes silently.
- **Requests 43, 45 (`usart2_rx_dma`, `usart3_rx_dma`) and 46 (`usart3_tx_dma`)** are acknowledged by name, as 41 was (`dma-usart23`). 41's acknowledgement stays: it now moves data where the platform wires the USART guard's request, and nothing where it does not.
- **`TVI-DMA-OFFSET` stays, narrowed** (the ruling's "check that it stays right"). It now fires only for a re-enable after a software suspension with no `NDTR` write since. p654 has software rewrite `NDTR`; p665 says NDT reloads after a completed transfer, which `TCIF` declared this one. So whether silicon runs the remaining or the programmed count is not stated: `unmodeled` (`dma-offsetndt`, DM49). ArduPilot always rewrites `NDTR`.

## 2a. The ledger's model row (granted 19:57Z, outside the fence; a ledger-format change)

- **Before:** `tools/tviburi-release` wrote one model row per stock type, listing every instance the pinned chain declares, with `guarded_by` whenever the type had a class row. For `UART.STM32F7_USART` that named `lpuart1`, left stock (ruled Q1), as guarded.
- **Now:** a type whose class row names only some of its instances gets two rows, each true read alone:
  - `field` `"guarded"`: the restated instances, with `guarded_by`;
  - `field` `"unguarded"`: the rest, with no `guarded_by`.
  Every other type keeps its one row with `field` `""`. Keys stay unique on (peripheral, register, field).
- **The ledger's schema is 2.** Aethelgard's `main` reads neither `guarded_by` nor model-row `instances` (the architect's check).
- **Checked at build:** every instance a `guarded_by` row lists must be one the manifest restates for that type, or the release refuses by name. A unit test plants an overclaiming row (`unit`; mutant US37). `usart-ledger` asserts the split rows and schema 2 (mutant US36).

## 3. Acceptance (WO §2)

- **A planted firmware that changes `M` or `OVER8` with `UE` = 1** refuses `TVI-USART-UE`, **`unmodeled`**, naming the port, the field and the page (`usart-m1ue`, `-over8ue`). **The WO's "`predicted-fault`" is superseded by the ruling** (19:13Z: the manual does not say what silicon does). "The same change made with `UE` = 0 passes" holds for every allowed field (`usart-psoff`); a non-zero `M1` or `OVER8` is outside the allow-list at any `UE` (`TVI-USART-FIELD`).
- **ArduPilot's receive path on the guarded twin ends a frame on `IDLE`, with no padding,** and a DMA receive moves the bytes where the platform wires the request:
  - the armed run, unpadded (§3.1): ArduPilot answered every command and armed;
  - the mechanism is in its log: `IDLE` one frame after each frame, `rx_irq_cb` clearing `EN`, `TCIF`, the re-arm.
- **ArduPilot's boot, recorded, and the armed run show no USART `would-refuse`:**
  - the fixture boot (5 s): none;
  - the armed run (14 s): none;
  - other guards' records in the armed run are rows (§7).
- **The 14 generated boots:** each board's observers are unchanged (measure §6). Every write, every `RDR` value and every `ISR` poll count is identical to main. The boot block differs only in copy-served bits outside Aethelgard's masks (`boot.rs`:960–990 read). Two consumer steps change from the form, not the rules: the `peripherals` kind and the access parser reading the guard's INFO records (§7).
- **The budget:** within `1201`'s 32 MiB, judged in CI on the first push (run 45475257047): `MEDIANS main 902872 KiB, branch 908924 KiB, difference 6052 KiB (+5.9 MiB)`, PASS, every branch boot under the 1,572,864 KiB ceiling.
- **CI:** §8.

### 3.1 The armed run (Furnace; CI has only the fixture ELF, ruled Q9)

On the final tree (`8e1d2d1`), in Aethelgard `4400h`'s replay of its Sentinel-03 capture (`aet-4400h/cap/s03c/1`), re-rooted to the committed `tests/1301/replay-1301.sh`. **ELF sha256 `dbf598d8b77f57d1945315b9fff611848f7d7f57992443a33eec0790a511d451`** (unmodified Copter-4.7.2 `fb773567`, built from 4400h's hwdef `2b5a8fb2…`).
- **The twin:** the full guarded twin, recorded, with `usart1`'s `ReceiveDmaRequest -> dmamux1@41` (`ACCEPT_USART_DMA`, standing in for `4400i`).
- **The input:** 4400h's `s03-arm-s` plan, 21 frames and 837 bytes, **each frame its own bytes, unpadded**, at the same times; 14 s. 57 s wall and 2,978,944 KiB at load 14–18.
- **The result:**
  - ArduPilot answered every command: 10 `COMMAND_ACK`s and 13 `PARAM_VALUE`s (`~/.cache/tvi-1305/h/mav.py`);
  - it armed (`COMMAND_ACK` 400 result 0 at 9.003 s; `base_mode` 209 from 9.75 s);
  - 4400h's padded run's sequence holds: the motor check refused before `FRAME_CLASS`, the accelerometer calibration accepted, GUIDED refused for position.
- **No USART `would-refuse`, and no `TVI-DMA-REQUEST`.** The records the run does carry are other guards' rows (§7).
- **The frame's end, from the log:** `ISR` `IDLE` one frame after the frame, then `EN` cleared by `rx_irq_cb`, `TCIF`, and the re-arm on the other bounce buffer. It is the same at `3bb55d8` (measure §6).

## 4. The adversarial pass (WO §5)

A reviewer with no stake in the code (a fresh session, given the code and the manual, not my conclusions) read the guard, the DMA change and the tests. My dispositions, each checked against the code:

| Finding | Disposition |
|---|---|
| **B1:** the `UE` record named acknowledged fields as offences (`reinitstop` failed on it in the full suite, as the reviewer predicted) | fixed: only fields outside the pattern are named; `psue`, `reinitstop` |
| **B2:** `UE` = 0 cleared `TC`, whose reset value is 1 (p2085, p2091) | fixed: `TC` left to the stock model, which sets it as `TE` clears |
| **B3:** a running port with its clock gated kept receiving, raising `IDLE` and the request, and the DMA moved zeros through the bus lock | fixed: no reception without a clock (`usart-rxoff`, US33) |
| **G2:** a DMA re-enable after a suspension without an `NDTR` rewrite became a silent pass | fixed: `TVI-DMA-OFFSET`, narrowed (§2) |
| **G5:** `RE` cleared and set again inside an idle frame still gave that frame's `IDLE` | fixed: `UE` or `RE` falling cancels it (`usart-idlere`, US32); the timer's own `UE`/`RE` check became redundant and went |
| **T1:** `tdrbyte` could not fail on "is sent" (`TC` already 1 from reset) | fixed: `TC` cleared first |
| **T2:** the frame time's `M` and `STOP` terms were never exercised | fixed: `usart-idleframe` (12 bits: 37.5 µs), US34, US35 |
| G1 overrun, G3 `IDLE` timing, G4 `BRR` < 16 | questions (§6) |
| G6 (the guard's RCC registration not checked), G7 (`ISR` reserved bit 29 reads 1), T3, T4, N1–N5 | rows (§7) |

**WO §5's list:**
- **A frame that ends without `IDLE`:**
  - a break, a framing error or a line held low cannot be injected (the `IUART` surface carries whole bytes);
  - `RE` cleared mid-frame cancels the frame's `IDLE` (`idlere`).
- **`IDLE` without a frame:** never at a start (`idle` R-START).
- **A request that moves data silicon would not:**
  - `DMAR` with `RE` = 0 raises nothing (no byte is received);
  - a byte "the overrun dropped" is never dropped, because the queue is unbounded (acknowledged, ruled Q8; FIFOEN = 0 is §6 Q3);
  - a byte arriving without a clock is not received (B3).
- **A configuration change that slips past `UE`:**
  - a sub-word write refuses (`width`);
  - `UE` and a field in one write is a configuration from `UE` = 0 and is judged at `UE` = 1 (`psue`, `m1ue`);
  - `UE` cleared and set in one write is impossible; in two writes it is a disable, then a start (`disable`, `kernel`).

## 5. The reduction pass

A fresh subagent proposed seven cuts. I applied six, then re-proved them by both suites and every mutant (§8):
- `Copy()` folded into `Served()`;
- an unused `using`;
- a dead alignment clause;
- the DMA suspension's second stock write;
- the `kernel` firmware path merged with `config`;
- a duplicated awk header.

Earlier in the build, two dead branches went: the `SCARCNT` exception (unreachable behind the allow-list) and the request level's `UE` term (the `UE` = 0 flush makes it redundant). The seventh proposal, merging the replay's two awk passes, is kept as a finding: the file repeats that shape for every guard.

Reduction pass (diff): −17 net · 5 duplicates removed · 3 dead paths removed · 6 protected kept · 4 findings below (§7).

## 6. Questions for the architect

1. ~~The ledger's model row~~ (posted 19:27Z): **granted at 19:57Z and done** (§2a).
2. **`BRR` below 16** (adversarial G4). "USARTDIV must be greater than or equal to 16" (p2035), and the allow-list takes any `BRR`. ArduPilot's armed run starts `usart2` with `BRR` = 0 for one write (0.2017 s), so refusing it would refuse the armed run. `unmodeled`, acknowledge by name, or leave it?
3. **Overrun with `FIFOEN` = 0** (G1). Silicon loses the second unread byte (p2030), and the twin's queue is unbounded. Q8 acknowledged that for FIFO mode, which both firmwares run. Does the acknowledgement extend to `FIFOEN` = 0, or should that be `unmodeled`? With instantaneous injection, a firmware on silicon would lose bytes the consumer never paces.
4. **`IDLE`'s frame time uses the stock `frequency` property** (200 MHz or 125 MHz), not the RCC's selected kernel source (G3). Baud accuracy is ruled Aethelgard's. But `IDLE` timing is now the guard's own, and it is off by the same ratio whenever the two differ. Compute it from the RCC source, or a row?

## 7. Rows and findings

**For the consumer (Aethelgard Phase 2; the seam needs no user glue):**
- **The serial access parser** (`serial.rs`:347–391, `logged_by` :510) reads every `[INFO] usartN:` line as an access. The guard's witness and end records log at INFO under the port's name, so the generated judge's write reconstruction fails closed on every board with a port. Ruled a Phase 2 seam row, the same class as `rcc` and `flash`.
- **The `peripherals` kind** is `GuardedSTM32F7_USART`: the class map.
- **The owned instance's WARNING lines lose their source name,** as the RCC's and DMA's did.
- **For Aethelgard (findings, not changes):**
  - ChibiOS's `usart_init` re-run on an enabled port writes `BRR` and framing with `UE` = 1. It is acknowledged here; bench row U1 decides it.
  - ArduPilot's RC probing starts `usart2` with `BRR` = 0 for one write (§6 Q2).

**Other guards' records in the armed run** (each a row for its guard; measure §6):
- `rcc` `TVI-RCC-FIELD`: `AHB1ENR.ADC12EN` = 1 (77,076: Sentinel-03's ADC), `AHB4ENR.ADC3EN`, their `LPEN` clears, `APB1LLPENR.USART2LPEN` = 0 and `APB2LPENR.TIM1LPEN` = 0;
- `dma1` `TVI-DMA-FIELD` `CIRC` = 1 (an ADC stream). An armed Sentinel-03 run is not a pass on main's guards either.

**The USART guard's own rows:**
- **A kernel clock lost under a running USART** is not refused: the RCC guard's `TVI-RCC-CLOCK` watches SPI guards only. Reception stops (B3), but transmission goes on. A row for the RCC guard.
- **Bench row U1** is the priority ruled; its result decides the acknowledged pattern.
- **`lpuart1`** stays stock, and is declared without `lowPowerMode` (a stock-wrong declaration).
- **The receive line has no timing:** injected bytes arrive at once, so overrun cannot be modeled honestly (§6 Q3), and `IDLE`'s frame uses the stock baud (§6 Q4).
- **`ISR`:** reserved bit 29 reads 1 (the stock reset value, U1), and `TXFE` reads 0 in FIFO mode where silicon's empty FIFO reads 1. Neither firmware reads them.
- **`RDR` read with nothing received** returns 0 with a stock warning. Silicon keeps the last byte (from memory).
- **The configuration check** does not confirm the guard is registered on the RCC guard, on which the bus-lock "covered" story rests (adversarial G6). The restatement is generated, so the risk is a hand-written platform.
- **No case reaches the kernel query's "no rule" or "no RCC guard" answers:**
  - `usart6` and `uart4/5/7/8` cannot start without the RCC guard refusing their enable first;
  - a platform with no RCC guard is a consumer form this WO does not make.
- **Re-pulses log a stock DMA warning per frame** while a byte waits for a disabled stream. There were none in the armed run (0 lines).

**Cross-guard, for the records' owner:**
- **Recorded mode evaluates every rule for one access** (`usart-recorded` records two), as the DMA guard does. `model/Refusal.cs` says "evaluates no further rule". The code or the comment should move.
- **In recorded mode, a width-refused or no-register write is dropped, not passed on** (the timer and DMA guards share the pattern).
- **A foreign-thread `WriteChar` reaching the DMA's `RDR` read takes the bus lock under the guard's lock.** The stock model already has the same inversion with its own lock.
- **The kernel record pairs register `USART_CR1` with the RCC selector as its bit,** as `TVI-SPI-KERNEL` does.

**The teardown race** (roadmap row) cost six Furnace runs: three in the full suite (`rcc-alias17c`, `timer-lockbelow`, `flash-ap`) and three in reruns of my cases (`usart-tdrbyte`, `dma-offset`, `dma-usart23`, each passing on rerun or with its own assertions true). Each time the end records were lost from some guard on, with exit 0. Measured, empty boots, main `9f5436d` against this branch, at load 14–19: main 2/20, branch 1/20. Eight more end records do not show a worse rate.

**Reduction findings (not done):**
- `Protected()` and `Acknowledged()` restate as masks what the field table's text says. They agree today and no test ties them.
- `t_usart` is a third copy of `t_dma` and `t_flash`, and the harness helpers repeat across `tests/130*/`: a phase-gate item.
- `tests/1305/extract.py` repeats `tests/1300/rmpage.sh`'s page rule in Python.
- The replay's per-guard two-pass awk could be one pass for each guard: a phase-gate item.

## 8. Suites, mutants, memory and CI

**On Furnace** (one capped container at a time, `nice -n 10`):
- **The whole suite** (286 cases, at `01eb773`): 326 checks ok, 6 failed:
  - three teardown-race losses (§7);
  - `usart-reinitstop` (the adversarial B1 bug, then fixed);
  - `usart-fields` (a case-count floor I guessed at 400; the generator makes 394, and no record was wrong or missing). Now pinned to exactly 394, the generator's `# CASE` lines for this table, counted in that run (ruled 19:57Z);
  - `usart-ledger` (§6 Q1).
- **The final tree, `8e1d2d1`:**
  - `tests/1305/usart.sh` 30/30;
  - `tests/1303/dma.sh` 44/44;
  - **all 43 of this WO's mutants killed** (US01–US35, DM25 retargeted, DM43–DM49), each on the case named for it; then, with the ledger split, US36 and US37 killed, and `unit` plus all six guards' `ledger` cases and `usart-fields` pass (45 mutants in all);
  - the first mutant run left US02 and US05 alive, because their cases touched only acknowledged fields; both cases now carry an unacknowledged one.
- **The acceptance replays on `8e1d2d1`** (the 14 generated boots and the fixture; the armed run): as measure §6 and §3.1.
- **Memory:** a prototype measured −6.5 MiB (measure §4). **The built guard's budget is judged in CI** (ruled; Furnace is shared).

**CI:** FINAL-CI
