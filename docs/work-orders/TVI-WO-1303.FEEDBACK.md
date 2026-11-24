# TVI-WO-1303 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1303-dma-guard`, from `main` `4d9a063`, with `main` `d71d410` (TVI-WO-1202) merged in.

**State:**
- §1.1 was measured and stopped (`TVI-WO-1303.measure.md`), then ruled at 2026-11-24T14:10Z (Q1–Q9).
- The held edits (`tests/spike.sh`, `tests/mutants.tsv`) followed 1202's merge (14:45Z).
- The guard is built to the rulings. No §7 stop is open.
- Untouched: `model/Refusal.cs`, the record grammar, the RCC/SPI/GPIO guards' C#, `GuardedSTM32_Timer.cs` (ruled Q8), the steering documents and Aethelgard.
- One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| **The guard:** a wrapper over the sealed `STM32DMA` (ruled Q1). It owns an unregistered stock instance and forwards every access it passes, DMAMUX1's request lines, the stream interrupt lines and every reset. DMAMUX1 is not guarded: the routing rules run at each stream's enable, reading the stock `dmamux1`, which the platform hands the guard as `dmamux`. `CR` (all but `EN`) and `FCR` (reset `0x21`) are served from copies (ruled Q6) | `model/GuardedSTM32DMA.cs` |
| **Rules:** `TVI-DMA-CONFIG`, `-WIDTH` (p659), `-READ`, `-EN` (`predicted-fault`, pp. 662–667), `-DONE`, `-FIELD`, `-SIZE` (p663), `-OFFSET` (p666), `-TCM` (`predicted-fault`, p108), `-ADDRESS` (Table 7 p130–131), `-RELOAD` (p665), `-ROUTE` (`predicted-fault`, p703, Table 124 p697–698), `-MUX`, `-SHARED` (`predicted-fault`, p704), `-REQUEST` | the same |
| **The allow-list:** 135 rows over the 10 DMA registers (every bit 31:0; stream registers at stream 0's offset, applied to all eight), generated from RM0433 Rev 9 §15.5. The requests ArduPilot's recorded boot uses are named on `DIR`'s `stock-wrong` row (ruled Q5 (c)) | the same, `Fields`; `tests/1303/fields.py` |
| **The restatement:** `dma1`/`dma2` cancelled, the guard declared at each address `as "dmaN"` on `tviburiRcc` with the stock entry's interrupt lines, and DMAMUX1's routes restated onto it by an untyped `dmamux1:` update. Generated, and checked byte for byte | `model/stm32h7-dma-restate.{awk,repl}`; the board's include and modes |
| **The class row naming its instances** (ruled Q9 (c)): a `class` row may carry a fourth column. The MANIFEST restates only those instances; a named instance the stock chain does not declare refuses the build. Unit-tested. Only the DMA row uses it | `tools/tviburi-release/pins.tsv`, `src/main.rs` |
| **Bench row D1** for `TVI-DMA-TCM` (`RUL-18`): its expectation is marked "from memory", and it has no result (ruled Q4) | `model/bench-checks.tsv` |
| **41 cases,** each `dma-<case>` in `tests/spike.sh`'s acceptance group; 30 planted firmware paths | `tests/1303/dma.sh`, `firmware/dma.rs` |
| **Mutants DM01–DM42,** one per case (DM35 for the class row's filter, killed by `unit`) | `tests/mutants.tsv` |
| **The replay:** it appends the DMA restatement for a tree that has it (ruled Q9 (b)), and keeps only what follows Aethelgard's reaper separator (`1302` FEEDBACK §7 row) | `tests/1301/replay-1301.sh` |
| `dma1 dma2` appended to each guard-name list (ruled Q9 (a)), and to G17 | `scripts/tviburi-spike`, `tests/1300/rcc.sh`, `tests/1301/gpio.sh`, `tests/1302/timer.sh`, `tests/spike.sh` (6), `tests/mutants.tsv` |
| The citations F12 asks for | `tests/ci/citations.tsv` (8 appended rows) |

## 2. The rulings as built

- **Q2 (per field):** a changed protected field while the stream runs is `predicted-fault`, and the same value passes. ArduPilot's disables (`EN` with `TCIE` cleared in one write) pass (`dma-disable`).
- **Q3 (c):**
  - running → `TVI-DMA-EN`;
  - completed in the twin with `EN` still set → `TVI-DMA-DONE`, `unmodeled`;
  - `EN` = 0 → passes.
- **Q4:** the TCM check spans `M0AR` and `PAR` over the items the transfer would address.
  - A wrapped span and a reserved or unmapped span are `unmodeled`.
  - `NDT` = 0 addresses nothing.
  - A span that starts below DTCM and ends in it refuses (`dma-below`).
- **Q5:**
  - (a) id 0 or reserved → `TVI-DMA-ROUTE`;
  - (b) `TVI-DMA-SHARED`, across controllers too (`dma-sharedx`);
  - (c) 41 to memory and 38, 40, 42 from memory acknowledged by name; any other valid id no model raises is `TVI-DMA-REQUEST`, `unmodeled`;
  - (d) request generator ids → `TVI-DMA-MUX`.
- **Q6:** `PSIZE`/`MSIZE` = 01 probed (`dma-msize16`), and allowed.
- **Q7, the clock:** an access with `DMA1EN` = 0 is dropped by the RCC guard's bus lock, reads 0 and emits **no record** (`dma-clock`). That is silicon's ignored write (p373): silicon-faithful, not a refusal.
- **Q8:** the timer's DMA side stays `unmodeled`; see §7.

## 3. Acceptance (WO §2)

| Clause | Where shown | Result |
|---|---|---|
| A planted firmware pointing a DMA1 stream at DTCM refuses `predicted-fault` at enable, naming the stream, the address, the region and the page; the same firmware with an AXI SRAM address passes | `dma-tcm`, `dma-axi` | as stated: `DMA_S0M0AR = 0x20000400 (DTCM (0x20000000-0x2001FFFF))`, p108, stream 0, at the `CR` store |
| ArduPilot's recorded boot shows no DMA `would-refuse` outside the acknowledged table | the committed fixture (ELF `a799dfb6`) and AP-m, every guard recorded, `main` against this branch | 0 `would-refuse` from `dma1`/`dma2`; both guards witness; the console bytes and the boot block are identical to `main`'s (§8) |
| The 14 generated boots: each board's observers unchanged, or each difference cited | the 14 boots recaptured at Aethelgard `1cb994c1`, the same way | observer edges with their stamps, the boot block and USART1's lines identical on all 14; 0 DMA `would-refuse` (§8) |
| The budget, within `1201`'s 32 MiB, under the ceiling | §8 | §8 |
| CI green | the PR | §8 |

## 4. The adversarial pass (WO §5)

There were two passes: mine through the build, and an independent one by a fresh reviewer given the code, the stock source, RM0433 and the ruling, not my conclusions. I checked each of its findings in the stock source and RM0433 before acting on it.

| WO §5 item | Result |
|---|---|
| A double-buffer `M1AR` | `DBM`, `CT` and `M1AR` refuse unless 0 (`dma-fields`): no double-buffer stream runs |
| An address written after `EN` | `TVI-DMA-EN` (`dma-en`), or `TVI-DMA-DONE` once completed |
| A peripheral-to-memory stream whose memory side is TCM | `dma-itcm`; the peripheral side too (`dma-partcm`, p108 excludes both buses) |
| A sub-word write | `TVI-DMA-WIDTH` (`dma-width`), in either mode never reaching the stock model |
| `EN` and a field in one write | from `EN` = 0 a configuration, passes; from `EN` = 1 refuses (`dma-enoff`, judged against the state before the write) |
| `EN` cleared and set in one write | a `CR` write with `EN` = 1 to a running stream that changes a protected field refuses; unchanged passes (`dma-same`) |
| A request routed twice | `dma-shared`, `dma-sharedx` |
| **A DMAMUX channel left on a stream that a later write reuses: found, and left** (ruled Q1) | a `CxCR` rewrite under a live stream is unseen, because DMAMUX1 is not guarded; the routing stays the stock model's, which follows the rewrite as silicon does. `dma-shared` shows that the rule still catches a shared id at the second enable |

**Mine, found and fixed:**
- **`NDT` reload (p665).** Silicon reloads the programmed `NDT` when a completed stream is enabled again; the stock model never does, so the twin moved nothing, silently. Now `TVI-DMA-RELOAD`, `unmodeled` (`dma-reload`).
- **`EN` written 1 after the twin completed a transfer.** Silicon's `EN` was cleared, so the write starts a new transfer. Now `TVI-DMA-DONE` (`dma-reassert`).

**The independent review's findings, each checked, then fixed:**
1. **`PSIZE` ≠ `MSIZE`.** The stock model counts in `MSIZE` items (`STM32DMA.cs` L251–255) and corrupts `NDT` or throws, while silicon forces `MSIZE` to `PSIZE` in direct mode (p663). Now `TVI-DMA-SIZE`, `unmodeled`, at enable (`dma-sizes`).
2. **Memory-to-memory runs in FIFO mode only on silicon** (p645); the stock model moves `NDT` × `MSIZE`. With sizes equal (finding 1) the bytes match; `FCR.DMDIS` reads 0 where silicon reads 1 (a row, §7).
3. **A stale offset.** The stock model keeps its transfer offset when a stream is disabled part-way (L160, L256, L262), where silicon restarts at the base address (p666). Now `TVI-DMA-OFFSET`, `unmodeled` (`dma-offset`).
4. **`FTH` ≠ 01 refused in direct mode, where it is unused (p667):** a false refusal. `FTH` is now any value (`dma-fthzero`).
5. **`FTH` is protected under `EN` (p667):** my §1.1 text missed this line. Now in `TVI-DMA-EN`'s mask (`dma-fth`).
6. **Four smaller fixes:**
   - the reloaded span is checked for TCM before `RELOAD` (`dma-reloadtcm`);
   - `programmed` changes only while the stream is disabled;
   - the restart rule applies only after something was programmed;
   - system memory bank 2 is treated as unmapped (p131 prints it inside a Reserved row).
7. **Recorded mode** evaluates every rule and keeps protected fields as silicon keeps them, against `model/Refusal.cs`'s comment ("evaluates no further rule"). `1302`'s guard does the same. A row for `Refusal.cs`'s owner (§7).

Checked and found clean by the review:
- no route into a TCM that the rules miss;
- `M1AR`/`DBM`;
- writes after `EN`;
- sub-word accesses;
- the cross-controller lookup;
- reset, IRQ and request forwarding;
- every cited page it checked.

## 5. The reduction pass

**Reduction pass (diff): −8 net · 2 duplicates removed · 1 dead path removed · 0 protected kept · 3 findings below.** A fresh reviewer made the proposals over `origin/main..HEAD`; I checked each in the code before acting.
- **Duplicates removed:**
  - the request ids were stated in the header and beside their arrays: now beside the arrays only;
  - `TVI-DMA-ADDRESS` was listed twice in the header;
  - one record call bypassed the guard's own `Refuse` helper.
- **Dead path removed:** Mapped's ITCM and DTCM rows. `Unmapped` runs only after `Tcm` found no TCM byte, so they could never match.
- **One mechanism:** the release program's two TSV readers are now one.
- **Rejected:**
  - merging `Sizes` and `Directions` (the same `{0, 1, 2}`): a mutant and the ledger test name them;
  - the restatement's trailing count line: the sibling generators carry the same.
- **Proof:** the full suite and the DM mutants run on the reduced tree (§8).
- **Findings (outside this WO):**
  - **the guard-name list now lives in about 14 copies** (the roadmap row); each guard appends to every one;
  - the planted paths are described both in `firmware/dma.rs` and in `tests/1303/dma.sh`, the house style of the sibling guards;
  - the class map's instances sit in a map parallel to the class list (one structure would do).

## 6. The other guards' cases

- Unchanged apart from `dma1 dma2` in each guard-name list (granted Q9 (a)).
- `tests/1301/replay-1301.sh` gains two additive blocks (the DMA restatement, the reaper separator), inside the `tests/` fence.
- The full suite on HEAD: §8.

## 7. Rows and findings

**Ruled rows:**
- **(3), the timer's DMA side (Q8).** The stock timer raises no request (measure.md D10), no firmware routes a timer id, and the stock DMA moves a memory-to-peripheral transfer whole at `EN`. DShot stays unwitnessable until a model raises the timer's requests and the DMA paces by them.
- **The DMAMUX1 gaps (Q1):**
  - writes with both clocks off are taken (p455, p373);
  - byte and half-word writes are dropped (p708);
  - generators and synchronisation are tags;
  - a `CxCR` rewrite under a live stream is unseen;
  - **new in the build:** the stock model drops `SE`, `EGE`, `SOIE`, `SPOL`, `NBREQ` and `SYNC_ID` before the guard can read them, so `TVI-DMA-MUX` catches the generator ids only.
- **`EN` never cleared by the stock model** at the end of a transfer (`stock-wrong`, p665), and memory-to-peripheral transfers unpaced (acknowledged for 38, 40, 42 only).
- **ArduPilot's USART RX has no request source** (41): a USART guard's, with `OQ-03`'s arming.
- **For Aethelgard** (noted by the architect): its `usart1` log stamps a DMA-moved byte with the `EN` store's PC; a Sentinel-03 ArduPilot capture.

**New in the build:**
- **`FCR.DMDIS` reads 0 for a memory-to-memory stream,** where silicon sets it at `EN` (p667); the guard serves the copy.
- **System memory bank 2:** p131 prints `0x1FF40000`–`0x1FF5FFFF` under "System memory" and inside the Reserved row `0x1FF20000`–`0x1FFFFFFF`. The guard treats it as unmapped (`unmodeled`) until ruled.
- **`model/Refusal.cs`'s recorded-mode comment** ("evaluates no further rule for that access") disagrees with both the timer and the DMA guards, which evaluate every rule (RUL-19: "measure ArduPilot's real queue"). For the records owner.
- **The wrapper's prices** (as the RCC's): the access log names its registers `unknown`, and the owned model's warnings lose their source name.
- **The WO's Q7 sentence** ("already refuses through `1300a`'s lock") is the architect's to correct (14:10Z).

## 8. Suites, mutants, memory and CI

All on Furnace, in the pinned image (`aethelgard-tools@sha256:1e0ab105…`), one capped container at a time.
- **The DMA suite:** FILL.
- **The full suite and the adversarial group:** FILL.
- **Mutants:** DM01–DM42, 42 of 42 killed on `55773ea` through `tests/mutants.sh`; G17 (re-keyed) killed. After the reduction: FILL.
- **Memory, `1201`'s budget,** eleven boots a side, `main` `d71d410` against HEAD: FILL.
- **Acceptance:** FILL.
- **CI:** see the PR. A green CI is not live acceptance.
