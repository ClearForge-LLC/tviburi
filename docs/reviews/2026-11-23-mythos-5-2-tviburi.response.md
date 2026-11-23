# Response to the Mythos 5.2 review of Tviburi, with its ledger

**Responds to:** `docs/reviews/2026-11-23-mythos-5-2-tviburi.md`, kept verbatim and never edited.
**Rulings:** the architect's, 2026-11-23. Drafted by the co-architect's assistant. **Gate:** Scotty.
**Read against:** Tviburi `main` `40d7c71` (with `0100a` merged as PR #6 and `1100` as PR #8); Aethelgard `origin/main` `7dec1d7b`. The review read Tviburi at `963238f9`, the `0100a` branch at `2d23ab5`, and Aethelgard at `7fbe5d11`.
**Where the rulings live:** `architecture.md` §2.1, §3 and §5 (`RUL-04`, `RUL-09`, `RUL-10`, `RUL-11` and `RUL-13` amended; `RUL-17`, `RUL-18` and `RUL-19` new; `OQ-03` and `OQ-04` noted), and `roadmap.md` Phases 1, 2 and 4. This file records why and who owns each finding; it does not restate the rulings.

---

## 1. In short

- **The review is accepted in substance.** Its top three (Part C) become, in order: `TVI-WO-1150`, a spike that measures the seam before `TVI-WO-1200` builds on it; `RUL-17` and `RUL-18`, the ledger as the allow-list and a kind on every refusal; and `RUL-13`'s order amended to clocks first, measured from ArduPilot.
- **Two findings were overtaken by work merged after the review read the code:** most of A7, and the Sentinel-03 half of A10 (§2).
- **One item is Scotty's:** northstar invariant 6 has no owner, and a replacement is proposed in §5.
- **`OQ-03` is not ruled.** `1100`'s proposed cut waits for ArduPilot's measurement.

## 2. Verified at `main`, where it changes the picture

- **A7 is closed at `main` for both routes the review names.** `0100a` was squash-merged after the review's read of `2d23ab5`, which is no longer reachable. At `main`, a `CFG2` read returns the guard's copy (`model/GuardedSTM32H7_SPI.cs` L150–158), and a `CR1` read takes `SSI` from it (L149). So the false pass (`MASTER` read as 1 from the stock model and written back) and the false refusal (`SET_BIT(CR1, SPE)` carrying `SSI` = 0) are both gone, and tested (`0100a` FEEDBACK F2: `rmw`, `conditions` 7a, mutant M31). **The remainder** is any copy `0100b` adds, and `SR.MODF` read while D1's history is set (F2, open).
- **A6 stands, with its text corrected.** At `main` the `SSM` = 0, `SSOE` = 1 branch already says RM0433 rules the mode fault out (L248–250), and still refuses because NSS output is not modeled.
- **A8 stands:** any well-formed version is accepted (`tools/tviburi-check/src/main.rs` L115), and an unnamed source is skipped (L80).
- **A12's IOLOCK half stands:** the latch is taken from any passed `CR1` write (L79). A `CR1` read while latched refuses (L142), so the latch that never clears is over-strict, not silent.
- **A10's Sentinel-03 half is done.** `1100` (PR #8) measured 13 specifications, the Sentinel-03 fixture among them (710 ledger rows; `spike/1100/queue.md` §1), after the architect widened its scope. The review read before any `1100` branch was pushed. The ArduPilot half stands.
- **Aethelgard's Renode configuration, re-read at `7dec1d7b`** (`crates/aethelgard-cli/src/renode.rs` L73–94), sets more than the virtual stamp:
  - `use-synchronous-logging = true`, which should remove `0100a` FEEDBACK F7's mid-line interleave under the consumer;
  - `collapse-repeated-log-entries = True`, so `seq` is still needed;
  - `compiler-cache-enabled = False`, so every boot recompiles the overlay. A11's cost question is real.
- **The memory margin is thin.** ArduPilot's foreign run peaks at 833 MiB under the 1 GiB bound (Aethelgard architecture §2.27). The overlay's compile has at most about 190 MiB.

## 3. Ledger

| Finding | Verdict | Owner | Note |
|---|---|---|---|
| A1 record dialect | accepted | `TVI-WO-1150` (ii), then `0100b` (5) | `RUL-11` amended: the record is the text after `<source>: `, in two framings. The reset half (one witness per process) goes to `1150` (vi) |
| A2 identity needs the environment | accepted | `1150` (iii), then `1200` | `RUL-10` amended. See §4.1: the constant must not hash itself |
| A3 swapping stock instances | accepted | `1150` (i) | `RUL-09` amended to a class map, pending the spike |
| A4 refusals already fail Aethelgard's boot | noted | Aethelgard, Phase 2 | A named finding ahead of `renode_error_log`; a run confirming the Info-level witness survives the console filter (roadmap Phase 2) |
| A5 deny-list guard | accepted | `0100b` (6) for SPI; each `13NN` | `RUL-17` |
| A6 ArduPilot refused at SPI start | accepted | `0100b` (1) | See §4.2: `RUL-17` may refuse the same write again |
| A7 copies diverge from read-back | closed at `main` (§2) | `0100b` (2), remainder | |
| A8 the check's version and sources | accepted | `0100b` (5) | Its third bullet, an `end` from `SIGTERM`, is `0100a` F6: open, the architect's |
| A9 same hash, different model | accepted | `1200` | Most of it falls away under `RUL-10` as amended; the Renode build enters through the consumer's own pin |
| A10 the wrong firmware measured | accepted | `1150` (v) | `RUL-04` and `RUL-13` amended. The Sentinel-03 half was done by `1100` |
| A11 (a) invariant 6 | for Scotty | Scotty | §5 |
| A11 (b) Phase 2 gate clause 5 | accepted | roadmap | Now "no Tviburi model code" |
| A11 (c) reads; monitor vs CPU writes; reset; cost | accepted | reads: each `13NN`; harness writes: Phase 2, and `1150` (vi); reset and cost: `1150` (vi) | `RUL-17` covers reads of copied fields |
| A11 (d) `OQ-04`'s prior | accepted | Aethelgard's architect, ratified by Scotty | Prior amended: "refused if wrong", a new class keyed to the Tviburi ledger hash |
| A11, invariant 8's second clause | accepted | `1150` (iii), `1200` | It rests on `RUL-10`, now amended |
| A12 over-strict rules, weak tests | accepted | `0100b` (3) | The `0100a` truth table is a dated snapshot of `e47e8d6` by its own header; `0100b` writes rows at its head. See §4.3 on `MODFC` |
| B1 refusal kinds | accepted | `0100b` (4) | `RUL-18` |
| B2 model the fault | deferred | roadmap Phase 4 | Only behind `RUL-18`'s evidence rule |
| B3 the ledger as allow-list | accepted | as A5 | `RUL-17` |
| B4 Aethelgard's sensor models | deferred | decided at Phase 2 | Roadmap Phase 2 |
| B5 recorded mode | accepted | `1150` (iv) prototype; the build is named after `1150` | `RUL-19` |
| B6 a console fixture | accepted | `1150` (ii) | Pinned by Aethelgard commit (`RUL-11`) |
| Part C, 1–3 | accepted | as A1–A3; A5 and B1; A10 | `1200` now follows `1150` |
| "On forging a record" | accepted as read | `0100b` §5 | The residue, a log line with guest-controlled text under a guarded name, is an adversarial case |
| "What Aethelgard's Phase 2 would change" | input | Aethelgard's architect | For its own Phase 2 work orders; nothing changes there from Tviburi |

## 4. Points the rulings leave open, raised in drafting

1. **`RUL-10`'s constant would hash itself.** The manifest holds every file's sha256, so a constant written into a hashed file changes the manifest it names. The ruling leaves the scheme to `1150` (iii), with one candidate named there.
2. **`RUL-17` is written per field value, not per write.** A rule on every write to a register that holds an unmodeled field would refuse all firmware. Even so, as ruled it refuses any firmware that sets an SPI baud rate (`CFG1.MBR` is a tag whose only effect is timing, written on 11 boards: `spike/1100/queue.md` §5), and the `SSOE` write that A6 lets pass the mode-fault rule. Both are named in advance, so `0100b` stops after its table for the architect to acknowledge fields by name. **Expect the stop.**
3. **Clearing D1's history on `IFCR.MODFC` alone would open a false pass.** A fault clears `MASTER`, and `MODFC` does not restore it. So `0100b` treats the copy's `MASTER` as unknown after `MODFC`, until `CFG2` is written again.
4. **`RUL-18`'s `predicted-fault` is widened to "or would not do what the firmware wrote".** Without that, a write silicon ignores (`CFG2` with `SPE` = 1, or under `IOLOCK`) has no kind, although it is the firmware's defect.
5. **`RUL-19` fails closed on a witness with no `mode`,** so a run of an older overlay cannot pass a check that expects one. Records after a recorded run's first `would-refuse` rank paths; they do not prove them.
6. **A GPIO guard from DS12117 is a second transcription, not an independent derivation.** Aethelgard's alternate-function numbers come from ST's open pin data, which agrees with ArduPilot's table on all 597 shared pairs (Aethelgard architecture §2.23). Whether that makes it independent under Aethelgard `INV-07` is Aethelgard's call.
7. **"`pc: none` is a harness access" holds only if no other access shows `none`.** A DMA write may. `1150` (vi) measures it, and the Phase 2 row says so.

## 5. For Scotty: northstar invariant 6

**The problem.** Invariant 6 says the trace records every register access. `architecture.md` §1 and §2.1 say the interface is not a trace and a Tviburi trace has no consumer. Nothing in the roadmap builds one, and Aethelgard's console filter drops access lines it does not judge. §3's pass bullet promises the same trace. This is a northstar change, so it is proposed here and not made.

**Proposed replacement for row 6:**
- **Invariant:** "Every record the twin emits locates the access that caused it; the consumer's trace carries the records and the model and ledger hashes. Tviburi owns no trace."
- **How to falsify:** "Find a record that does not locate its access, or a consumer's trace of a guarded run that lacks its records or either hash."

**Proposed replacement for §3's first bullet:** "a pass: a load witness and an end record, with no refusal, from a model whose hash the consumer pinned, carried in the consumer's own trace of the run."

**Why this keeps the substance:** the refused access still carries its PC and virtual time (invariant 5), and the access it names is the one the old invariant protected. What moves is ownership, to the consumer that already writes a trace.

## 6. What the review did not cover

- **The merged code.** It read `0100a` at `2d23ab5`, not the merged head (§2), and no `1100` branch existed.
- **No run.** Every finding is from reading. ArduPilot's SPI order rests on a hand-written Thumb decoder, not on source or a log; `1150` (v) and `0100b` §1.1 measure it.
- **A second guarded peripheral.** No end-to-end case exists (`0100a` F8), and the review did not test the source filter with two.
- **DMA writes to a guarded register,** and how they are located.
- **The `canned` state of `RUL-16`,** the chip research, and Phase 5.
- **The power seam:** a guarded PWR model beside Aethelgard's own PWR script (`1100` FEEDBACK §3.3).
- **The verdict documents of 2026-11-22,** which it searched rather than read in full (its own coverage note).
- **The overlay's release path:** a reproducible tarball and its pin (§4.1); nothing is built yet.
