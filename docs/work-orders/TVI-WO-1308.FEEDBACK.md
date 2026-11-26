# TVI-WO-1308 — FEEDBACK

**Builder:** Claude (Opus 5.5) as project manager, Furnace, 2026-11-26. Opus workers built the two guards, and an independent Opus reviewer made the adversarial pass. **Branch:** `wo/tvi-1308-dmamux-pwr`, from `main` `bd9dbd5`, with `main` `89301d0` merged (`TVI-WO-1203`, #61).
**The measurement:** `TVI-WO-1308.measure.md` (predictions `4b1608f`, measured `89f010e`). **Rulings:** Q1–Q16 (2026-11-26T09:31Z), Q17 and its two conditions (10:08Z, 10:19Z), Q18 (10:43Z), Q19–Q21 (11:06Z), the 1305 race (11:26Z), cargo at six jobs (13:53Z).

**Contents:** §1 what was built · §2 acceptance · §3 the replays · §4 consumer observers · §5 adversarial pass · §6 rows and findings · §7 gates · §8 budget and CI

## 1. What was built

**DMAMUX1** (`model/GuardedSTM32H7_DMAMUX.cs`, `model/stm32h7-dmamux-restate.{awk,repl}`, `tests/1308/dmamux.sh`, `firmware/dmamux.rs`)

| Ruling | Built |
|---|---|
| **Q1** the form | A subclass of `STM32_DMAMUX` that re-implements the three bus interfaces, with a copy of the stock register enum so the access log keeps its names. It is cancelled and restated `as "dmamux1"`. The generated restate file restates every other reference to the variable by untyped updates in its own text: the DMA guards' `dmamux`, the ADC guards' `dmaPeripheral`, `spi4`'s `DMARecieve -> …@83` (`stm32h7.repl` L197) and `crypto`'s `OutDMA -> …@77` (`stm32h753.repl` L5, named with its pin). No other guard's restate file changed. Every restated reference reads back as the guard (measured) |
| **Q2** width | Byte and half-word writes are merged into the copy and passed to the stock model as a word (p708). A misaligned access is `TVI-DMAMUX-WIDTH`, `unmodeled` (p708: "must be aligned", no outcome stated) |
| **Q3** the route against `PAR` | The DMA guard's one granted call, at its existing route check on `EN`: `TVI-DMAMUX-PAIR`, `unmodeled`, when the stream's `PAR` lies in the peripherals' region and is not the routed request's data register. The table has 13 pairs (Table 124 p697–698; Table 8 p132–135; each data register's page). As ruled at Q18 (b), a `PAR` in memory passes. `TVI-DMAMUX-LIVE`, `unmodeled`: another id written while the channel's stream is enabled. A same-id rewrite passes (ArduPilot's 77,402) |
| **Q4** clock and reset | With `DMA1EN` = `DMA2EN` = 0, a write is dropped and a read returns 0 (p455, p373), read through the RCC guard's public `ReadDoubleWord(0x0D8)`. `DMA1RST`/`DMA2RST` reset the guard and its stock channels (p433), through the RCC guard's `Written` event. A platform with no RCC guard is `TVI-DMAMUX-CONFIG` at every access |
| **Q18 (a)** | The write-time reserved-id refusal (my Q3 addendum) is **withdrawn**. A reserved id is stored, and the DMA guard's `TVI-DMA-ROUTE` (`predicted-fault`, p697–698) judges it at `EN`, as before. **Why it reversed:** it pre-empted that stronger judgement with a weaker kind, and a reserved id with no stream started moves nothing |
| allow-list | Sync, event and generator fields (`SOIE`, `EGE`, `SE`, `SPOL`, `NBREQ`, `SYNC_ID` (3 bits, p708), every `RGxCR` field) and reserved bits allowed at 0 only (`TVI-DMAMUX-FIELD`). `CSR` and `RGSR` read-only; `CFR` and `RGCFR` any. An offset with no register is refused (`TVI-DMAMUX-READ`) |

**PWR** (`model/GuardedSTM32H7_PWR.cs`, `model/stm32h7-pwr-restate.{awk,repl}`, `tests/1308/pwr.sh`, `firmware/pwr.rs`)

| Ruling | Built |
|---|---|
| **Q13** the form | A Tviburi-authored model; the stock stub is a Python script with no C# to subclass. It is cancelled and restated `as "pwr"` on the replays and in the PWR suite. **The Nucleo board keeps the stock stub** (Q17 (a)) |
| **Q7** revision | `revision: "V"` or `"Y"`, required (`TVI-PWR-CONFIG`). `CR3` resets to `0x46`/`0x06` (p307). The replays declare "V", as the ADC block does |
| **Q6** the one-time write | `CR3`'s lower byte is kept at its first write (p308). A later write that changes it is ignored, and is `TVI-PWR-SUPPLY` `predicted-fault` in both modes. One that leaves it unchanged passes (ArduPilot's second write). An invalid `LDOEN`/`BYPASS` pair locks the register and leaves `ACTVOSRDY` 0 (p308, Table 34 p261). The state survives `machine Reset`. **p307:** "This register is reset only by POR. It is not reset by wakeup from Standby mode and by the RESET pad." **p265:** "After a system reset, the software shall configure the used supply configuration in PWR control register 3 (PWR_CR3) register before changing VOS in PWR D3 domain control register (PWR_D3CR) or the RCC ck_sys frequency." Ruled: configure once, until a power-on reset. A bench row times a power cycle against a system reset |
| **Q5** ready flags | `ACTVOSRDY`, `VOSRDY` and `USB33RDY` rise at the write that makes them due, acknowledged by name ("no figure in RM0433 Rev 9 or DS12117 Rev 10"). A bench row times `VOSRDY` on the Nucleo |
| **Q9** scale and clocks | `VOS` is kept and served. A `VOS` write before `ACTVOSRDY` is `TVI-PWR-VOS`, `unmodeled` (p262). A `VOS` lowered under running clocks is `TVI-PWR-VOS`, `predicted-fault` (p310, p286, DS12117 p210). **`TVI-RCC-VOS`** in the RCC guard: CPU, AHB or APB above DS12117 Rev 10 p210's limit for the scale in force is `predicted-fault`. Above VOS1's CPU limit it is `unmodeled` where only VOS0 (`ODEN`) would fit. Rev Y judges CPU only: its datasheet gives no AHB/APB rows (p129–130) |
| **Q20 (A)** | `TVI-RCC-VOS` judges only on a platform with a PWR guard. Its ledger row and bench row R12 say "judged only with a PWR guard; not judged otherwise". A planted case on the board (no PWR guard), with APB above every scale's limit, emits none |
| **Q21** the flash guard | It asks the PWR guard's `Scale` when one is present. Otherwise it reads `PWR_D3CR` through the bus, as `1304` did, and every `TVI-FLASH-WAIT` record on an unknown scale names it ("scale unknown: no PWR guard … judged at VOS1"). Flash `vos2`/`vos3` keep their verdicts |
| **Q10, Q12** `DBP` | `CR2` is write-protected while `DBP` = 0 (p306): `TVI-PWR-DBP`, `unmodeled`. ArduPilot's measured `CR2` = `0x1` under `DBP` 0 is acknowledged by name, and not taken (`BREN` stays 0). `RCC_BDCR` written with `DBP` 0 is `TVI-RCC-DBP`, `unmodeled` (p427), through the RCC guard's one granted read. Its ledger rows' text is corrected at their generator (`tests/1300/allowlist.py`) |
| **Q11** `CR1` | Bits 31:28 allowed `{0x0, 0xF}`: p304 prints the reset value `0xF000 C000`, while the map on p313 gives `0x0000 C000`. That is **an inconsistency in RM0433 itself**. ChibiOS `d103983f` `hal_lld.c` L110 writes `0xF0000000U` on purpose |
| **Q19** release | `tools/tviburi-release/src/main.rs` `declared_for`, with one unit test and mutants MF11–MF12. A canned stock instance named by a class row counts as declared for that row only; `fmc` stays canned. **This edit is outside the fence, granted at 11:06Z** |
| allow-list | 69 rows. Every other field is allowed at its reset value only (`TVI-PWR-FIELD`); read-only flags are served 0; a register-less offset is `TVI-PWR-READ`; widths are modeled (p304) |

**Both:** the eleventh shard first (`db76d9f`), with `times.tsv` re-weighed from run 45774342008 (Q16).

## 2. Acceptance

| Clause | Evidence |
|---|---|
| A stream started on a channel whose route names another request refuses | `dmamux-pair`: SPI1's `RXDR` as `PAR` on USART1's request 41 is `TVI-DMAMUX-PAIR`, `unmodeled`, located. `parmem`: a `PAR` in AXI SRAM on 41 passes, and one request moves the byte |
| A second supply-configuration write is ignored as silicon ignores it, and named | PWR suite: a changed lower byte is `TVI-PWR-SUPPLY`, `predicted-fault`, and `CR3` reads the first write. An unchanged one passes. Both hold across `machine Reset` and in recorded mode |
| A clock raised before `VOSRDY` (the scale) refuses | PWR suite: VOS3, then 400 MHz, is `TVI-RCC-VOS`, `predicted-fault`, p210. ArduPilot's order (VOS1, `VOSRDY`, then 400/200/100) passes, and so do the generated boots' (Scale 2 at 64 MHz) |
| The armed run, the fixture and the 14 boots: no new record, boot blocks identical, the armed run arms | §3 |
| The manifest restates both instances only as `1203`'s rule allows | `tests/spike.sh manifest` passes with `dmamux1` and `pwr` restated, each declared in its committed restate file and loaded by `tests/1301/replay-1301.sh` |
| The budget, judged in CI; CI green; `times.tsv` re-weighed for this WO's rows from its run | §8 |

## 3. The replays

**Method:** `1305`'s captures, every guard recorded, the armed run with `1100a`'s knobs. `dmamux1`, `pwr`, `rcc`, `flashController`, `dma1` and `dma2` are access-logged, and the PWR and DMAMUX1 registers are read after `RunFor`. The branch head is compared line by line with a fresh replay of `main` `89301d0`, made with the same harness. The `bd9dbd5` baseline no longer compares, because `1203` names the RCC registers in the access log.

| Run | Against `main` | Records |
|---|---|---|
| 14 generated boots | identical but for the new guards' witness, end and `peripherals` type lines, and my after-run monitor reads of PWR | 0 |
| ArduPilot's fixture (5 s) | the same, plus **the two firmware reads Q14 predicted:** `D3CR` reads `0xE000` (the stub gave `0x2000`), and `CR1` after ArduPilot's `0xF000C000` write reads `DBP` 0 (the stub gave 1). Every later line is identical, so no branch ArduPilot takes moves. The stub's two warnings (`CR2`, `CPUCR`) are gone | 0 |
| The armed run (14 s) | the fixture's differences; arms at **9.332 s**, as on `main`. Four `spi2` warning stamps move by 10 ns with identical content: the code-shape effect `1300c` measured, also seen in Q1's form run with pass-through probes | 0 |

On every boot, witnesses and ends go from 32 to 34 (`dmamux1`, `pwr`), and they match one for one.

## 4. Consumer observers

Aethelgard's judge reads records, the boot block and MAVLink. **The boot block reads neither peripheral** (checked on all 16 argvs). No MAVLink frame moves. The `peripherals` listing names two new types, as each earlier guard's did. Aethelgard's other observers were not re-read in this work order.

## 5. Adversarial pass (WO §5)

An independent Opus reviewer worked from the code, the WO's three attack classes and RM0433, not from my conclusions. It planted counterexamples in its own copy of `9ed5b91`.

| Class | Finding | Fixed |
|---|---|---|
| a voltage scale assumed | **`VOS` lowered under the flash wait states went unjudged.** Rev V, HSI 64 MHz, `LATENCY` 0 at Scale 1, then Scale 2: no record at the `D3CR` write. `TVI-FLASH-WAIT` appeared only at the next, unrelated RCC write, located there, or never | the PWR guard raises a `Written` event; the flash guard re-judges at every `VOS` change, located at the `PWR_D3CR` write (p160, p310), and its `LATENCY` row says so. `pwr-flashlow`, PW29 |
| a voltage scale assumed | **`sys_ck` changed before the supply configuration passed.** No `CR3` write (`ACTVOSRDY` 0), then PLL1 at 160 MHz: no record (p308, p262) | `TVI-RCC-VOS` refuses a `sys_ck` change while the PWR guard's `ACTVOSRDY` is 0, `unmodeled` (p308, p262), only with a PWR guard. The generated boots and ArduPilot write `CR3` first and still pass. `pwr-clockearly`, PW30 |
| a voltage scale assumed | **VOS0 with the LDO bypassed had the wrong kind:** `unmodeled` "needs ODEN", where DS12117 p210 note 4 makes it impossible ("VOS0 is available only when the LDO regulator is ON") | `Exceeds` sees the supply. Above VOS1 with the LDO off is `predicted-fault` (DS12117 p210 note 4, Table 112). `pwr-vos0byp`, PW31 |
| mutant coverage | Lines no test killed: the RCC guard's sub-word `BDCR` `DBP` check; `TVI-PWR-VOS`'s branches with no RCC guard and no computable clock; both guards' address `CONFIG` checks | planted: `pwr-bdcrbyte` PW32, `pwr-norcc` PW33 (`tests/1308/pwr-norcc.repl`), `pwr-noclock` PW34, `pwr-addr` PW35, `dmamux-addr` DX31 |
| the records contract | `TVI-DMAMUX-PAIR`'s `access` described the `CxCR` and not the `DMA_SxCR` `EN` write (the pc was right) | the record locates the `DMA_SxCR` `EN` write (its offset and value); the DMA guard still makes one call, with two more arguments. `dmamux-pair`, DX32 |
| the records contract | With no `pwr` at all, the flash record claimed "PWR_D3CR reads VOS 00" | it now says "no peripheral at PWR_D3CR". `pwr-nopwr`, PW36 |
| — | With `dmamux1` enforcing and `dma1` recorded, the refusal latches but the stream starts. Not changed: after the first refusal nothing is claimed (`RUL-08`) | named |

**What held:**
- **A route that lies: none found.** Every passed, refused (recorded), clocks-off, sub-word and reset path leaves the guard's copy equal to the stock register the DMA guard reads.
- **The `PAIR` table** matches Table 124 for ids 9, 37–43, 45, 46, 77, 83 and 115.
- **A one-time write that is not one-time: none found.** Covered: byte 0 then byte 1, a half-word, an upper-byte-only first write, the invalid-pair lock in both modes, recorded mode, and `machine Reset`.
- **`DBP`:** the Q12 acknowledgement is narrow.
- **`TVI-RCC-VOS`'s trial** covers HSIDIV, `SW`, `D1`–`D3CFGR`, the APB enables and the `C1_` aliases.

The builders' own passes, before it:
- DMAMUX1: a recorded `PAIR` lets the stream start, and `RG3CR` and the reserved rows are pinned (`08c0b66`).
- PWR: VOS0's kind and rev Y's table planted, and the bench rows' bit names fixed (`9ed5b91`).

## 6. Rows and findings

**For Aethelgard:**
- **Its platforms' own `-> dmamux1@N` lines** (`spi1`/`spi2` `DMARecieve`, and a USART `ReceiveDmaRequest` where it wires one) must be repointed to the guard's variable. Otherwise they bind to the cancelled entry with no error (`RUL-09` (b)). The same trap would catch the stock `spi4`/`crypto` lines if the restate file did not restate them (measured: an unrestated reference loads silently). This is also a row for `1203`'s class map.
- **Its generated platforms' PWR script** (`AET-WO-4240v` D-3 (a)) is dropped by the cancel, and the guard supersedes it: on the guarded twin the guard wins. Its foreign platforms have no PWR model of their own.
- **Its platforms declare no silicon revision;** the replay stands in with "V" (Q7).

**Our own fixtures (Q17 Condition 2, measured, not fixed):**
- The board's planted firmware runs APB above VOS3's p210 limit at the reset scale. With the PWR guard declared on the board, runs newly refusing `TVI-RCC-VOS`:
  - i2c 45/48, usart 47/54, timer 35/37, rcc 37/69 (plus 2 `TVI-RCC-DBP`: `rtcen`, `rtcen-reset`), SPI 25/47, gpio 6/50, adc/dma/dmamux 0;
  - flash 17/40, plus 3 that refuse `TVI-PWR-VOS` alone: `divp1zero`, `divp1odd`, `gen`.
- Real silicon would be out of specification there.
- `firmware/flash.rs`'s `pll`/`pll400` run APB at 200 MHz, above p210 at **every** scale; `over225` runs AHB at 400 MHz. A row for a later work order.

**DS12117 p210, read on the rendered page:** footnote 6 ("when at least one peripheral is enabled") sits on VOS3's APB cell only, so VOS2–VOS0's APB limits are unconditional. `RTCAPBEN` resets to 1 (p471), so APB4 always has a peripheral enabled at reset.

**Upstream (ChibiOS `d103983f`, ArduPilot's pinned image), `os/hal/ports/STM32/STM32H7xx/hal_lld.c`:**
- L111 `PWR->CR2 = STM32_PWR_CR2;` runs before `DBP` is set (L61, in a function called later), so `BREN` never takes on silicon (p306), and L131's `BRRDY` wait is commented out.
- L110 writes `CR1`'s reserved nibble on purpose.
- On rev V, ArduPilot writes `CR3`'s lower byte `0x02`, clearing reserved bit 6, which reset sets (Q7, acknowledged by name).

**RM0433 Rev 9's own inconsistencies:**
- `PWR_CR1`'s reset value, p304 against p313.
- `D3CR`/`CSR1` reset values show `VOSRDY`/`ACTVOSRDY` 0, while p310 says `VOSRDY` "will be read 1".
- `CR3`'s rev V reset sets bit 6, which is "Reserved".

**Bench rows (`model/bench-checks.tsv`):** P1 `VOSRDY`/`ACTVOSRDY` time; P2/P5 the one-time write across a power cycle and a system reset, and the invalid pair; R12 `TVI-RCC-VOS`.

**Not modeled, rows:**
- `DBP`'s protection of the RTC registers and backup SRAM (both unguarded);
- RM0433 Table 60's per-peripheral kernel-clock maxima by scale (p354);
- DMAMUX1's request generators and synchronisation (refused at any non-zero value);
- `DMAMUX2` and BDMA (Phase 4, by the cut);
- the stock `SYNC_ID` is 2 bits where RM0433 has 3 (p708), which is moot while sync is refused.

**Named changes outside the two guards' files:**
- the DMA guard's one call (Q3);
- the RCC guard's `TVI-RCC-VOS` and `DBP` read, and its generated ledger text (Q9, Q10);
- the flash guard's scale read and its records' wording (Q21);
- the release program's `declared_for` (Q19);
- `tests/1305/usart.sh:96`: its platform line `ReceiveDmaRequest -> dmamux1@41` repointed to `tviburiDmamux1@41`, otherwise it binds to the cancelled entry;
- `tests/1304/flash.sh`'s `vosunknownlow` wording and `tests/1300/rcc.sh`'s ledger count (bench row R12);
- every suite's guard list gains `dmamux1`.

**The 1305 teardown race (ruled 11:26Z):**
- `TVI-WO-1204`'s measure (`wo/tvi-1204-records-harness` `78276ca`) places it: the old emulation's logger is disposed with its end records queued. It loses 16 of 50 loaded quits on `main`, and none with a flush.
- My counts, five runs of `tests/1305/usart.sh` on `main` at Furnace load 10–30: 0, 6, 11, 0, 11 cases failed, every one "no end record".
- One branch run: 0 failed.
- Both workers saw the same class in other suites, and every such case passed on rerun.

**The measurement's corrections** (predictions table, measure §7): the generated platforms carry the consumer's own PWR script, all 14 boots write PWR, and DMAMUX1 allows sub-word access.

## 7. Gates

- **Reduction pass (diff): −18 net · 1 duplicates removed · 2 dead paths removed · 0 protected kept · 7 findings below** (`8a10582`, by a fresh Opus subagent). Removed: a `WriteCr3` early return that could not run and its parameter; `RefusedPair`'s channel range check, which its only caller already guarantees; `t_dmamux`/`t_pwr`, now one `t_1308` (the 507 case ids unchanged). Proven: 65 of 66 dmamux-*/pwr-* cases, and `pwr-ap` on its rerun after a lost `end` (1204's race); all 9 mutants of the changed lines killed.
- **Reported, not removed:**
  - DMAMUX1's `Pairs` repeats the DMA guard's `RequestNames` and its raised-direction sets, held equal by the `pairs` case.
  - The guarded-sources list has about 10 copies (TVI-WO-1204 lands one list).
  - Each suite has its own harness copy, and `spike.sh` has 8 copies of `t_<suite>`.
  - RCC `Clocks` repeats `BusHz`/`SourceHz` divider formulas.
  - `Row`/`R()`/`Unloaded`/address checks repeat in every guard; their home would be `Refusal.cs`, outside this fence.
  - Each guard has its own lookup of another guard, and Flash restates PWR's base address.
  - Repeated idioms in the planted firmware; the limit numbers there are independent oracles and stay.
- **Gates run locally**, all from `git archive` snapshots with cargo at six jobs (13:53Z): the suites and mutants above; `tests/ci/route.sh`'s inventory; the 16 replays on `26d7136` (§3; zero records, armed 9.332 s, identical to the previous head).

## 8. Budget and CI

Written at the first push: CI's run, `1201`'s budget (judged there; the measurement's floor was −23.7 MiB, §5 of the measure), and this work order's rows re-weighed into `tests/ci/times.tsv` from that run (Q16) follow in the READY commit.
