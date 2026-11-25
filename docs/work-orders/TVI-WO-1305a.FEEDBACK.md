# TVI-WO-1305a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24/25. **Branch:** `wo/tvi-1305a-usart-followups`, from `main` `d9bb3da`.

**State:**
- (1) measured first, with predictions committed before any run (`7df3c3d`). The answer went to the bus before the build: **no byte moves under `BRR` < 16**, so §7's first stop did not fire (`TVI-WO-1305a.measure.md` §1).
- The architect accepted both readings on the bus (2026-11-25T00:00Z):
  - (1) as built, with the running-port rewrite kept as a rule;
  - (3) the frequency computed in the USART guard from the RCC guard's registers and its `Written` event, with HSI-rooted rules only.
- **Built differently from the letter of (3)'s ruling, after the adversarial pass** (posted on the bus):
  - `PLL2DIVR` is not computed from a copy kept from `Written`. A firmware's read-modify-write writes the stock's narrowed bits back, so such a copy can be wrong silently.
  - Instead, the read-back is used, and it is exact while no wide field was ever written. A wide write (`DIVN2` ≥ 256 or `DIVQ2` ≥ 64) makes `pll2_q_ck` `unmodeled` until the machine resets.
  - p407's reference and VCO ranges are new no-rule conditions.
- **Open: one question (§7 Q1).** The HSI-only scope was accepted on my statement that the 14 generated boards are HSI-rooted. That statement was wrong for two of them, `hse-crystal-16` and `hse-external-8`. I posted the correction with options and am waiting for a ruling.
- **The armed run arms** and is identical to `main`'s (§3.2). §7's second stop did not fire.
- **Untouched:**
  - the RCC guard: only its query, its bus reads and its existing event are used;
  - every other guard, `model/Refusal.cs`, `tools/`, the steering documents and Aethelgard;
  - `tests/ci/times.tsv`: it has no USART rows (each USART item weighs its kind's median), and `TVI-WO-1202a` re-weighs from CI.
- One PR, left unmerged.

## 1. What was built

All in `model/GuardedSTM32F7_USART.cs`, with cases in `tests/1305/usart.sh` and `firmware/usart.rs`.

| Rule | What it does |
|---|---|
| **`TVI-USART-BRR`** (p2035, "USARTDIV must be greater than or equal to 16"; OVER8 = 0 only, so USARTDIV = BRR) | **Refused `unmodeled`:**<br>• a start (`UE` with `TE` or `RE`, from neither) with `BRR` 1–15;<br>• a start with `BRR` = 0 and `DMAR` or `DMAT` set;<br>• `BRR` rewritten below 16 on a running port (the same value passes, `RUL-18`).<br>**ArduPilot's measured start is held, not refused:** `BRR` = 0 with no DMA request. It is acknowledged by name when `BRR` ≥ 16 is written. Until then, any of these refuses: a `TDR` write (CPU or DMA), an `RDR` read, a received byte, or `DMAR`/`DMAT` set. |
| **`TVI-USART-OVERRUN`** (p2030) | With `FIFOEN` = 0, a byte that arrives while the last one waits unread is refused `unmodeled`, and in enforcing mode it is not received. `FIFOEN` = 1 returns before the check: FIFO mode is unchanged. |
| **`TVI-USART-IDLE`** | `IDLE`'s frame time, and the DMA request's per-frame re-pulse, now come from the kernel clock over `BRR` (p2034), in the stock model's integer arithmetic, never from the stock `frequency`. See the table below. With no rule, `unmodeled` is refused at the receiver's start (`UE` and `RE`, at the `CR1` write), or at a byte's arrival if the source changed under a running port (a byte's record carries the byte, `pc` none, exact virtual time). The byte is judged before it is taken. |
| **Bench row U2** | ArduPilot's start on a Nucleo with `BRR` = 0, then rewritten with `UE` = 1, read back, and the line observed; plus a held `BRR` = 0 with the host sending. Result: "not run". |

**How `TVI-USART-IDLE` finds the kernel clock:**
- **The source** comes from `D2CCIP2R` (copy-served, p417), not from the query's text.
- **The registers** are read through the RCC guard's bus methods, as the flash guard does (1304).
- **Rules (HSI-rooted only, as ruled):**
  - `hsi_ker_ck` = 64 MHz >> `HSIDIV` (p341, p385).
  - `rcc_pclk2` (USART1/6) or `rcc_pclk1` = `sys_ck` (the HSI, by `SWS`, p393) / `D1CPRE` / `HPRE` / `D2PPRE2` or `D2PPRE1` (pp. 394–396).
  - `pll2_q_ck` = HSI / `DIVM2` × (`DIVN2` + 1) / (`DIVQ2` + 1) (p398, p407). It applies only with `ref2_ck` in 1–16 MHz and the VCO in `PLL2VCOSEL`'s range (p402, p407).
- **`PLL2DIVR`'s read-back is trusted only while no wide field was ever written.** The RCC guard's `Written` event marks a written `DIVN2` ≥ 256 or `DIVQ2` ≥ 64, which the stock model narrows to 8 and 6 bits (p407 has 9 and 7). The mark holds until the machine resets.
- **`PLL2FRACR` is not tracked:** the RCC guard's allow-list takes `FRACN2` only at 0, so a fraction refuses at the RCC write.
- **No rule:** `csi_ker_ck`, `pll3_q_ck`, `lse_ck`, and `sys_ck` or a PLL reference from the CSI, the HSE or PLL1.

## 2. Acceptance (WO §2)

| Clause | Case | Result |
|---|---|---|
| (1) the measured `usart2` pattern passes, acknowledged by name | `brr0`: ArduPilot's exact sequence (init with `BRR` = 0, `sdStop`, init with `BRR` = 0, reads, `BRR` `0x271` with `UE` = 1, `CR3` `0xC1`, `CR1` `0x2000013D` and its read-modify-write), then a byte sent, enforcing | passes; `BRR` reads `0x271`, `TC` set |
| (1) a planted start with `BRR` = 8 is `unmodeled`, with p2035 | `brr8` (firmware) | `TVI-USART-BRR` at the `CR1` write, PC located |
| (1) the other ways out | `brrheld` (recorded) | six would-refuses, in order: a start with `DMAT`; in the hold, a `TDR` write, an `RDR` read, `DMAR` set, a byte received, and `BRR` rewritten to 5. None for the same `BRR` again, and none after `BRR` `0xC8` |
| (2) a planted overrun with `FIFOEN` = 0 is `unmodeled`, with p2030; the same traffic with `FIFOEN` = 1 passes | `overrun` | `FIFOEN` = 1: both bytes kept (0x41, 0x42). `FIFOEN` = 0: one refusal, at the second byte, and it is not received |
| (3) `IDLE` at the kernel clock's baud, not the stock frequency's | `idlekernel` | five clock trees, each from reset:<br>• `rcc_pclk2` through `D1CPRE`, `HPRE` and `D2PPRE2`;<br>• `hsi_ker_ck` through `HSIDIV`;<br>• **ArduPilot's own PLL2 at exactly 72 MHz** (ruled: kept as a test), after a `USART1RST` pulse and a machine reset;<br>• `usart2` on `rcc_pclk1` through `D2PPRE1`.<br>The stock `frequency` (200 or 125 MHz) gives another time in each |
| (3) a planted source with no rule gives `unmodeled` | `idlenorule` (firmware, `csi_ker_ck`); `idlenone` (recorded) | `idlenorule`: refused at the start, PC located. `idlenone`: five no-rule paths: a source switched to `csi_ker_ck` under a running port (at the byte); `sys_ck` on the CSI; PLL2 from the CSI; a wide `PLL2DIVR` kept through `USART1RST`; a VCO outside p407's range |
| Bench row U2, named by the ledger | `ledger` | `U1` and `U2`; the `BRR` and `FIFOEN` rows name the new rules |

**The Nucleo `IDLE` cases that moved (named, as ruled):**
- `idle`, `idleframe`, `idlere` and `dmarx` change `BRR` from `0x271` to `0xC8`, so that their timings hold.
- The Nucleo board's kernel clock at reset is `rcc_pclk2` from the HSI, 64 MHz; 64 MHz / 200 is 320,000 baud, the rate the cases were written for.
- The board's stock `frequency` is 200 MHz, which at `0xC8` would give 1,000,000 baud, so each case now also fails on the stock frequency.
- Their assertions are unchanged.

## 3. The identity and the armed run (WO §1.2, §1.3)

### 3.1 The 14 generated boots and ArduPilot's fixture boot

- **Method:** each boot replayed on `main`'s tree (`d9bb3da`) and on this branch (`c69382b`), every guard recorded, the nine UARTs access-logged, and the UART registers dumped after `RunFor`.
- **Compared:**
  - every UART access, with its value and virtual time;
  - every Tviburi record (model hash normalised);
  - every monitor output (the boot block, `ExecutedInstructions`, the dump), except the host-speed `PerformanceInMips`.
- **Scripts:** `~/.cache/tvi-1305a/h/ident.sh` and `ident-cmp.py`, scratch.

**Results:**
- **Identical: 12 generated boards and the fixture.**
- **Different: `hse-crystal-16` and `hse-external-8`:**
  - their `sys_ck` is PLL1's P output from the HSE (`hse-crystal-16`: 16 MHz / `DIVM1` 5 × 80 / 4 = 64 MHz, the declared frequency);
  - under the HSI-only rules, `usart2`'s `rcc_pclk1` has no rule, so each receiver start and each echo byte is `TVI-USART-IDLE` (18 would-refuses);
  - `ISR` then reads without `IDLE` (`0x206000C0` against `0x206000D0`).
- This is §7 Q1.

### 3.2 The armed run (Furnace; CI has only the fixture ELF)

- **Setup:** 1305's Sentinel-03 capture, ELF `dbf598d8…`, its plan of 21 unpadded frames, and `usart1`'s request wired as 1305 did. Run in the same harness on `main` and on this branch (`c69382b`), recorded.
- **Identical to `main`:**
  - 4,815 UART accesses;
  - 76,812 records;
  - the monitor outputs, `ExecutedInstructions` included.
- **ArduPilot answered every command and armed:**
  - 10 `COMMAND_ACK`s and 13 `PARAM_VALUE`s;
  - `COMMAND_ACK` 400 result 0 at 9.001 s;
  - `base_mode` 209 from 9.798 s.
- No USART record and no `TVI-DMA-REQUEST`.

**Two things this run caught.**
- **A harness slip of mine (not a refusal):** my first run left out 1305's injection plan, so nothing was received and nothing armed. With the plan, the argv is identical to 1305's.
- **A bug of mine, which only this run showed.** My guard reset its copy of `PLL2DIVR` in `Reset()`. The stock RCC's `USARTxRST` also runs `Reset()`, and ArduPilot pulses it after setting PLL2 up. As a result:
  - `IDLE` came at 12.1 µs (`PLL2DIVR`'s reset value, 516 MHz) instead of 86.8 µs;
  - there was no record, and the run still armed;
  - every Nucleo case passed, because they all enabled the port before writing PLL2.
- **The fix, and what replaced it after the adversarial pass:** I fixed it (`a2b51d7`). The adversarial pass then replaced the copy altogether (§4, finding 2). `idlekernel`'s ArduPilot scenario now pulses `USART1RST`, and `idlenone` keeps a wide mark through one.

## 4. The adversarial pass (WO §5)

A fresh reviewer read the code and the manual, not my conclusions. It worked by reading only, before the `a2b51d7` fix landed. My dispositions:

| Finding | Disposition |
|---|---|
| **1 (high):** a USART reset restores the `PLL2DIVR` copy | already found by the armed run and fixed (`a2b51d7`); the reviewer's independent finding agrees |
| **2 (medium):** a read-modify-write of `PLL2DIVR` writes the stock's narrowed bits back, so `Written` carries a wrong value and `IDLE` is mistimed silently | **fixed:** the copy is gone. The read-back is used, and is exact while no field wider than the stock's was written. A wide write marks "no rule" until the machine resets (`idlenone`, US58, US59, US62) |
| **3:** `pll2_q_ck` outside p407's ranges | **fixed:** `ref2_ck` must be in 1–16 MHz and the VCO in `PLL2VCOSEL`'s range (`idlenone`, US63) |
| **4:** the frame-time record from a byte carried value 0 and let the byte in under enforcing | **fixed:** judged before the byte is taken, with the byte in the record. In recorded mode, overrun is now evaluated after a held refusal (it was short-circuited) |
| **5:** `usart2`, `rcc_pclk1`, `D2PPRE1` and `USART234578SEL` were untested | **fixed:** `idlekernel` U (US56, US64) |
| 7: held records hard-coded `BRR` = 0 | fixed: they carry the `BRR` in force |
| 8: "USARTDIV = BRR" cited p2034 | fixed: p2035 |
| 6, 7 (over-refusals), 8 (the RCC by name against by event) | rows (§8) |

**WO §5's three targets:**
- **An acknowledgement wider than the measurement:** none found by either of us. Every start or running state with `BRR` < 16 either refuses or is the held pattern, and no byte moves under a hold without a record. Each way out of the hold has its mutant (US38–US47).
- **A default where there is none:** US52 plants one (64 MHz for an unruled source) and `idlenorule` kills it. Each no-rule path is in `idlenone`.
- **The FIFO-mode path changed by the overrun rule:** `overrun`'s first half (US48). `dmarx` (FIFO mode, DMA) is unchanged.

## 5. The reduction pass

A fresh subagent proposed nine cuts. I applied eight, then re-proved them with the full USART suite and every mutant of this WO (§6):
- one home each for `BRR`'s 16 bits (×3), `UE`|`RE` (×2), `SWS` (×2), the APB prescaler field (×2), `ref2_ck` (×3) and the written `BRR` value (×3);
- the `TVI-USART-BRR` text once, in `RefusedBrr` and the generated field row, not again in the header;
- the IDLE baud note once, at the `idle` case, not again in the harness header.

**Not taken:** shortening `brr0` to one start. It is the measured pattern the WO says to acknowledge, and its fidelity to the measurement is the point.

**Not dead:** `BaudRate`, which the reviewer flagged as unread since `FrameTime` stopped using it. It is a member `IUART` requires.

**Earlier, in the build:** a read-back cross-check went once the event subscription was moved to platform load, and the `PLL2DIVR` copy was replaced by a one-bit mark (§4, finding 2).

Reduction pass (diff): +4 net · 11 copies of 8 facts removed · 2 dead paths removed (the cross-check, the copy) · 0 protected kept · 2 findings (§8).

## 6. Suites, mutants and CI

**On Furnace** (one capped container at a time, `nice -n 10`; each run from a `git archive` of the commit named):
- **`tests/1305/usart.sh`**, 37 cases:
  - 37/37 at `e048f4b`, the guard as shipped (`c69382b` changes docs only);
  - `tdrbyte` and `idlere` lost end records to the teardown race in the full run (exit 1, no refusal), and both passed on rerun;
  - earlier full runs: 37/37 at `6f8bb63`.
- **`tests/1303/dma.sh`**, the cases that drive the USART guard (`usart23`, `txother`, `offset`, `offsetndt`, `reflect`, `ledger`): pass at `e048f4b`.
- **`tests/1300/rcc.sh`** `usart2` and `ardupilot`: pass at `c69382b`.
- **Mutants:**
  - this WO's 27 (US38–US64): all killed at `e048f4b`;
  - every earlier USART mutant (US01–US37): all killed at `c69382b`.
- **`tests/ci/route.sh origin/main`:** its tests and the citation inventory pass; `class=code`, `budget=yes`.
- **The identity (§3.1) and the armed run (§3.2):** rerun at `c69382b`, with the results as stated there.

**CI:** the budget is judged there. The run on the pushed head is reported in the READY note on the bus: a FEEDBACK-only commit would rerun every shard.

## 7. Questions for the architect

1. **The two HSE boards** (§3.1; on the bus with options). Should their `sys_ck` from PLL1's P output from the HSE get a rule, at the RCC guard's `HseFrequency` (the board's declared crystal, as the flash guard reads it), with `PLL1DIVR` from its read-back? Or do they refuse `TVI-USART-IDLE` at `usart2`'s start as a named change? **My recommendation is the rule**, with one caveat: the RCC guard defaults `hseFrequency` to 8 MHz when a platform declares none. That default is the RCC guard's, not something the USART guard can see.

## 8. Rows and findings

**For bench rows U1 and U2 (as ruled):**
- RM0433 has two readings of a `BRR` write with `UE` = 1:
  - p2082: "can only be written when the USART is disabled (UE = 0)";
  - p2035: "The baud counters are updated to the new value in the baud registers after a write operation to USART_BRR. Hence the baud rate register value should not be changed during communication".
- Under p2082's reading, ArduPilot's `usart2` runs on silicon with `BRR` = 0 for a full second (0.2017–1.2038 s, until the next `BRR` written with `UE` = 0). Nothing moves in that second either (measure §1).

**For the RCC guard (outside this fence):**
- **Its mux state survives a machine reset.** After a reset that returns `D2CCIP2R` to 0, a mux left on `pll2_q_ck` (with PLL2 now off) refuses the switch back to `rcc_pclk1`, so a start is `TVI-USART-KERNEL` `predicted-fault`. Found by `idlekernel` U; the case selects `USART16SEL` only, to stay clear of it.
- **`hseFrequency` defaults to 8 MHz** when undeclared (§7 Q1).
- **`TVI-RCC-CLOCK` does not watch the USART guards** (1305's row). This WO adds the arrival-time `TVI-USART-IDLE` for a source changed under a running port, but a source change that keeps a rule (`hsi_ker_ck` to `rcc_pclk2`) passes, and is timed at the new clock.
- **`PLL2DIVR`/`PLL3DIVR` narrowed by the stock model** (roadmap row): this guard now refuses its own use of a wide value. The RCC guard still serves the narrowed read-back to firmware.

**For the USART guard (later work orders):**
- **Over-refusals, none of them a silent pass:**
  - overrun judges the twin's injection pacing: two `WriteChar`s at one instant with `FIFOEN` = 0 refuse, though on silicon a frame separates them;
  - the receiver-start `TVI-USART-IDLE` refuses a start on an unruled source even if no byte ever arrives;
  - a hold survives `UE` = 0, so a `TDR` write on a stopped port after a held start refuses.
- **The frame-time record from the request's re-pulse has no access** (width 0). Its PC is whatever the CPU is executing.
- **`TVI-USART-KERNEL` finds the RCC guard by name** (`sysbus.rcc`), and `TVI-USART-IDLE` by the event subscription. On a platform where the two differ, the record says "no RCC guard on this platform" while the kernel query answered.
- The held-start refusals are tested in recorded mode only. Enforcing's early returns (the byte not queued, `TDR` not sent) have no case of their own.

**For the process (mine):**
- **P7 was checked on one board's source.** The identity replay caught it. A claim about "the 14" is checked on the 14.
- **`tests/1305/usart.sh` edited while bash was running it** broke a full run's last line twice. The harness now runs from an export.
