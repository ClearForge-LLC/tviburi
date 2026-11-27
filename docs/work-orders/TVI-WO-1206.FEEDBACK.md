# TVI-WO-1206 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-27. **Branch:** `wo/tvi-1206-unaudited-read`, from `main` `8e15435`, with `b032a2d` (#71) merged.

**Rulings followed** (architect, 2026-11-26/27):
- **23:26Z:** the kickoff.
- **23:36Z:** (a), `(TVI-WO-1206 A<n>)` resolves here; the release must refuse an acknowledgement naming no row of its home.
- **00:10Z:** `Res2_1`'s test (a); points 2 and 3 accepted; the existence check's shape (A); PLL3 (a), acknowledged on field evidence.
- **00:43Z:** (a), the work order widens to every field the per-field measure finds open, and that measure is the gate's.
- **00:59Z:** `tests/1300/rcc.sh` `ledger` granted; a reading appends to a row and never erases a true fact.
- **01:37Z:** (a) for the four field-evidence items (A25–A28).
- **02:12Z:** `TSIZE` (a), A29 with the ambiguity in the open; DMAMUX2 (a) and (b): the measure lists unmapped writes, and DMAMUX2 is off the cut by its text.

**The deliverables:** `TVI-WO-1206.rm0433.md` (the readings' sources, method and acknowledgements A1–A28); the ledger rows themselves (295 fields in seven guards); the addendum in `TVI-WO-1205.verdict.md` and `TVI-WO-1205.FEEDBACK.md`.

## 1. What was built

**The readings, 295 fields in two work lists:**
- **The first:** the 121 distinct ledger fields behind the ruled 129 items (`spike/1206/data/items.tsv`).
- **The second:** 174 fields the per-field measure found open above the cut (`items2.tsv`), after the adversarial pass showed the 129 undercounted (§4).
- **The method, both lists:** predictions committed first (`predictions.md`, `predictions2.md`); a 20 % re-read drawn with its seed before reading (`reread.txt` seed 1206, `reread2.txt` seed 12062); ten reading workers returning verbatim quotes with page and `file Ln`, no state; every quote checked mechanically (`check-reads.py`); every state assigned by the builder in one table (`readings.py`), which also writes §3's A-rows.
- **What a row now says:** its text on main, kept, then its reading: `acknowledged (TVI-WO-1206 A<n>; p…; File.cs L…)`, or `stock, wrong (what differs; unaffected: why)` with its page and line. One false clause was replaced: `I2C_CR2.AUTOEND`'s "a STOP after NBYTES" held for AUTOEND = 1 only.
- **The result:** 169 `acknowledged`, 126 `stock-wrong`, no stop left. 18 of the acknowledged rest on field evidence by ruling, where RM0433 is silent (A11, A12, A13, A25–A29; `TVI-WO-1206.rm0433.md` §4).

**The gate's measure** (`spike/1206/reader/fields.py`, ruled 00:43Z): every field of every register the armed run writes, judged guarded, read or open. DMA streams 1–7 are judged by stream 0's rows and DMAMUX channels by C0's, as the guards do. 1206's join (`reader/join.py`) counts RUL-17 as amended: every state but `inherited-unaudited` is read.

**The evidence, per firmware:** the armed run's every-access log, and the 14 generated boots and ArduPilot's fixture replayed with every access logged (`harness/gen-log.sh`), summarised in `data/main-8e15435/`: `reads.tsv`, `gen-reads.tsv`, `instance-accesses.tsv`, `gen-writes.tsv` and `reset-windows.tsv`.

**Edits outside the WO's first fence, each granted:**
- **`tools/tviburi-release`** (23:36Z, 00:10Z): `(TVI-WO-1206 A<n>)` resolves to this WO's §3. The release now reads the three acknowledgement homes as sources and refuses, by name, an acknowledgement whose A-number names no row of its home. Unit tests plant one per home; mutant `U11-ackHomeRow` (skip the check) is killed by `unit`; its weight is in `tests/ci/times.tsv`. Today's ledger passed the check before any new row: no existing citation fails.
- **`tools/tviburi-route`** (00:10Z (A)): the three homes route as code (`READ_DOCS`, with its test). In `tests/ci/citations.tsv`, their rows are dropped, the older homes' own citations of `FEEDBACK.md` and `architecture.md` listed, `main.rs`'s `docs/` listed, and `readings.tsv`'s three kept citations listed.
- **`tests/1306/i2c.sh` L261** (00:10Z): `I2C_ICR.Res2_1`'s assertion is the read state, `acknowledged`.
- **`tests/1300/rcc.sh` `ledger`** (00:59Z): an RCC acknowledgement may be homed in `TVI-WO-1300.rm0433.md` §8 or `TVI-WO-1206.rm0433.md` §3, still never 0100b's; the case's name says so.
- **The generators' data:** one `READ_1206` block and one hook line each in `tests/1300/allowlist.py` and `tests/1302`/`1303`/`1305`/`1306` `fields.py`, as `ACKED_1300B` (00:10Z, point 3). The allowed sets are never touched.

## 2. Acceptance

All on `f398a8b`, the branch's last tree change (the one commit after it records these results and CI).
- **0 above-cut fields on `inherited-unaudited`,** by the gate's per-field measure: 1,982 fields above the cut at 208 registers, 399 guarded, 1,583 read, 0 open; none unlisted, none stock-bound (`spike/1206/data/branch/coverage.tsv`). On `main` the same measure finds 434 open.
- **Every changed row cites a manual page and a stock source line:** `spike/1206/check-rows.py 8e15435 …`, 295 rows changed, 0 failures. Every A-number cited is a §3 row, and the release refuses any other.
- **Every reading's quotes are on their page and at their lines:**
  - `check-reads.py`: the first list 121 rows, 0 failures (with `builder-values.tsv` for four fields' extra values).
  - The second list: 174 rows. Its failures are `from_memory` notes no row rests on, and DMAT's quotes from the DMA guard, checked by hand (§4).
- **Nothing behavioural moved:**
  - **The 16 replays** are identical to the baseline in records (0), witnesses, boot hash and boot lines (`data/branch/replays.txt`). The armed run arms at 9.332 s with 34 witnesses, 34 ends and 0 records.
  - **`LEDGER.jsonl`, `main` against the branch:** 1,946 rows each side, 295 changed, exactly the two work lists (`data/branch/ledger-diff.txt`). Keys changed: `state` 295, `behaves_as` 295, `reason` 169, `detail` 126. Every other key of every row is identical.
  - **The model tables:** 295 rows changed in class and text only; allowed sets, bits and everything outside the tables are byte-identical.
- **CI:** 45927404214 on `f398a8b`, success: "1167 expected (518 cases, 649 mutants), 1167 ran across 11 shards, 0 failures"; the budget's medians main 950,828, branch 944,112 KiB (−6.6 MiB, within noise; the +68.8 MiB line is the budget tests' own planted case).
- **Unmapped writes:** the measure lists the armed run's three, each judged off the cut or on no flight path (`TVI-WO-1206.rm0433.md` §5).
- **The addendum fits the page:** `TVI-WO-1205.verdict.md` is 840 words with it (722 before).
- **Off the cut, not counted:** `FLASH_CCR1/2.Res15_0` (path `other`) stay `inherited-unaudited` (R8).

## 3. Predictions, scored

| List | Predicted (ack / stock-wrong / stop or held) | Read | Where it missed |
|---|---|---|---|
| First (121) | 104 / 16 / 1 | 93 / 28 / 0 | TIM1 (16 ack predicted, 9 read): flag-setting and timing differ in 8 fields, each unobservable. `AUTOEND`, the predicted stop, is stock-wrong: ArduPilot clears TCIE and PE straight after its STOP |
| Second (174) | 146 / 24 / 4 | 76 / 98 / 0 | RCC reset bits (80 ack predicted, 2 read): the twin resets once at the write of 1 where RM0433 says "resets"; unobservable by `reset-windows.tsv`. The four held went to rulings, with three more SPI items |

A miss is not a defect: the predictions guard against reading toward a total, and neither list was.

## 4. Adversarial pass

**The first review** (a fresh Opus reviewer of the 121):
- **F1, PLL2:** filed under A6 ("equals the formula"), but PLL2 runs VCOL on an 8 MHz reference, outside p346's 1–2 MHz. Moved to its own field-evidence A-row (A13, by 1300c's Q1 and bench row R11), with PLL3's VCOL added to A11.
- **F2:** SRAM1EN moved to stock-wrong, as SRAM2/3.
- **F3:** LPUART1EN cited APB4Reset's line; corrected.
- **F4:** the "never accessed" evidence cited tables of 13 instances; `instance-accesses.tsv` now covers every instance in every run.
- **F5:** AUTOEND was read for 0 only; the generated boards write 1.
- **F6:** wording: A2 (ethernet is present), RTCEN (its own reading, A3), A10 (uart4/5/7/8), D3PPRE.
- **The finding that widened the WO:** the join this WO inherited, 1100a's, could not see a field written at a register that has another row (R1). On main, 218 items above the cut, 175 fields at 42 registers, sat in that hole (`spike/1206/gap.py`, `data/main-8e15435/gap.tsv`).

**The first re-read** (25 fields, seed 1206, an independent Opus reader that saw neither the readings nor the rows' text):
- **Agreed on 23 of 25.**
- **Disagreed on 2, recorded, not averaged:** SRAM1EN (it: same for the value 1; the builder: stock-wrong for the window before the enable) and PSC (it: same, since every write precedes UG; the builder: stock-wrong, since stock applies PSC at the write).
- **It also found** that 1100's queue kept one value per board, so the generated boards' values now come from every write.

**Found by the second readers and fixed:**
- **`spike/1100/data/writes.tsv` lacks writes:** some RCC `CR` writes, and all of one board's `CR` and `CFGR`. Every generated value and claim now comes from the 15 logged boots (`gen-writes.tsv`), and each claim was re-checked there: TIM1's write order, I2C's CR1, PCE, the PLL1 ranges. All hold.
- **The rows' text had been replaced, not appended to,** dropping true facts about the guard's rules. #72's first CI failed two ledger cases on it. Fixed as ruled at 00:59Z.

**The second re-read** (35 fields, seed 12062, an independent Opus reader):
- **Agreed on 19 of 35.**
- **Disagreed on 16, recorded, not averaged:**
  - **11 registered reset bits:** it says the same, since the twin resets the block; the builder says stock-wrong, since silicon's hold while the bit is 1 is not quoted.
  - **I2C RXIE:** it says it differs, because RXNE rises at START; the builder credits that to RXNE's and START's model (R4), with RXIE's gate matching.
  - **BDMAEN:** it says it differs, through DMAMUX2 and BDMA channel 7; that is the DMAMUX2 item raised with the architect.
  - **DSIZE 0 and USART `Res16_14`:** it says the manual is silent; A20 and A28 say so.
  - **SPI SPE:** it found that the stock model also clears TSIZE and EOT at the disable; A26 now states it.

**The second review** (a fresh Opus reviewer of the 174's rows; no acknowledged row cites another register's line):
- **Fixed:**
  - the reset windows' scan missed `APB1ResetLow/High` (82,558 writes, not 4,074; the conclusion stands);
  - DMAT's count (15–22 of 102) and its computed 1.8 ms;
  - the ready flags and SW now cite ArduPilot's ChibiOS source (Aethelgard 4400h's tree, `hal_lld.c` L266, L299, L306, L398, L465), no longer from memory, and SW is stock-wrong alike;
  - wording: USB1OTGRST (stock resets USB2's alias), CSUSP, A1 (no locked read of an A1 peripheral), A12 (NACKCF), A22 (i2c1 only), the TSIZE and MINC cites.
- **Raised:**
  - **TSIZE,** whose "unaffected" rested on p2179's ambiguous "the RXP flag is not set when EOT is set". Ruled at 02:12Z: A29, with the ambiguity in the open, and BR7.
  - **DMAMUX2,** an unmapped route (`C7CR` = 0x11, ADC3 to BDMA channel 7) the per-field measure could not see. Ruled at 02:12Z: the measure lists every unmapped write, and DMAMUX2 is off the cut (`TVI-WO-1206.rm0433.md` §5).

## 5. Findings and rows

- **R1. The method's hole** (§4; ruled at 00:43Z): 1100a's join judged queue rows and unrowed registers, not fields. The per-field measure replaces it as the gate's; `gap.py` is the evidence.
- **R2. The DMA guard, `DMA_SxCR.MINC` = 0:** on a memory-to-peripheral stream with NDT > 1 the stock model sends the item once and then zeros, where silicon repeats it (p663). ArduPilot's 32 such streams (SPI1 TX, NDT 256) are U4's Read dummy bytes, which `GenericSpiFlash` ignores. A device that reads MOSI there would see it. A finding row for the DMA guard.
- **R3. I2C, TC is not cleared by a STOP in the stock model** (p2011 says it is), and the guard does not mask it. No run acts on it: ArduPilot clears TCIE and PE straight after. A row for the I2C guard's STOP path.
- **R4. Flags the twin never sets:** CMF, TXFE and possibly TCBGT on USART, and RXNE's timing on I2C. Silicon sets them on this platform; their own read-only rows say "never set in the twin", true but incomplete. Also, the stock USART_ISR resets to 0x200000C0 where p2085 gives 0x0X8000C0.
- **R5. Stock decodes:** HPRE and D1CPRE 0xC–0xF as /32–/256 where p394–395 gives /64–/512 (the guard decodes correctly; no run writes them). An EGR write of 0 resets the counter in the stock model (p1599: "No action"). RCC_RSR uses a dual-core bit layout.
- **R6. Row pages:** several rows cite a register's first page where the bit text is on the next: UIE p1595, TI1S p1591, CC1S p1603, LIFCR p661, OPM p1589, UG p1599. Their readings cite the right pages; the rows' own page numbers are unchanged, as the allowed-set data they sit beside.
- **R7. `I2C_CR2.SADD` 0x90:** ten generated boards address a TMP117 the platform has no model for (TVI-WO-1306 Q9). Silicon would ACK, the twin NACKs, and the firmware times out.
- **R8. Off the cut:** `FLASH_CCR1/2.Res15_0` stay `inherited-unaudited` (path `other`).
- **Bench rows:**
  - **BR1:** `pll3_r_ck` with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (A11).
  - **BR2:** `I2C_ISR` after ICR = 0x2, 0x4 and 0x40, and `USART_ISR` after ICR = 0xFFFFFFFF (A12, A28).
  - **BR3:** spi1's restart after a suspend without an SPE toggle (A25).
  - **BR4:** disable with SUSP set (A26).
  - **BR5:** SPI_SR and the DMA flags after TXDMAEN is set before the stream (A27).
  - **BR7:** RXP on the last frame of a TSIZE transfer, FTHLV 0 (A29).
  - R11 (1300's) stands for PLL2 (A13).
- **R9. A Phase 4 row: DMAMUX2.** The twin has no DMAMUX2, and the stock BDMA leaves channel 7's registers unhandled, so ADC3's internal channels (VBAT/4, VSENSE, VREFINT) never reach memory by BDMA. That is off the ruled cut (its L31), and the measure lists it (`TVI-WO-1206.rm0433.md` §5).

## 6. Reduction pass

Diff-sized, over what this WO added in `spike/1206/` and its edits elsewhere.
- **Removed:** the generated-value helpers repeated in `items.py` and `items2.py`, moved to `genvals.py` (−50 lines, +4, +29 for the module; both work lists byte-identical after); `RULING`, unused once 1206's join took RUL-17 as amended.
- **Kept, each a copy with a reason in its header:** `reader/fields.py`'s six-line `ledger_offset` (as `join.py`'s, which runs on import); `harness/gen-inner.sh` (`inner.sh`'s patch point, with the boot's own RunFor kept); `reader/join.py` (1205's with one change).
- **Findings:**
  - `items.py` and `items2.py` still share their model-row parser; merging them would change two tools' inputs for no fewer facts.
  - `check-reads.py`'s value test is a substring match, coarse by design; the builder's own pass is the check.

Reduction pass (diff): −18 net · 2 duplicates removed · 1 dead path removed · 0 protected kept · 2 findings above

## 7. Gates

- **WO §7's stops, each taken and ruled:**
  - where an acknowledgement's reason lives (23:31Z, ruled 23:36Z);
  - the existence check's route conflict (23:38Z, ruled 00:10Z);
  - PLL3's unguaranteed frequency (00:02Z, ruled 00:10Z);
  - the 129's undercount (00:38Z, ruled 00:43Z);
  - the four field-evidence items (01:18Z, ruled 01:37Z).
- **TSIZE and DMAMUX2** (my 01:41Z note, ruled 02:12Z).
- **Clause 1, re-judged (WO §1.4):** met by the gate's per-field measure on `f398a8b`. 0 of 1,982 fields above the cut are open; 18 rest on field evidence by ruling, each with a bench row. The three unmapped writes are each judged off the cut or on no flight path. Written as the addendum in `TVI-WO-1205.verdict.md` and `TVI-WO-1205.FEEDBACK.md`.
- **The addendum is the WO's last stop.**
- **Not merged by me.**
