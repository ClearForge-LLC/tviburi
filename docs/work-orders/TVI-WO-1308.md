# TVI-WO-1308 — The last two families on the flight path: a DMAMUX1 guard and a PWR guard

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1308-dmamux-pwr`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, a `13NN` slice: two guards in one work order. Written 2026-11-26. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **`TVI-WO-1100a`'s cut** (PR #59, `docs/work-orders/TVI-WO-1100a.cut.md`): on the armed ArduPilot run, everything on the four paths that fly is guarded or acknowledged, except two families.
  - **DMAMUX1:** stock and unaudited. It routes every DMA stream the flight uses: IMU, barometer, battery, MAVLink and RC. 10 channels are configured in the armed run (the cut page, revised at `53b365e`).
  - **PWR:** a stock stub that answers "ready" to everything. `VOS` goes to Scale 1 (RM0433 Rev 9 p310), the LDO and the USB regulator are enabled (p308), and backup-domain access is opened (p305).
- **Both are in the recommended cut and in the cautious alternative.** `OQ-03`'s ruling (Scotty's) decides only whether TIM5 joins them, so this work order does not wait for it.
- `RUL-07`, `RUL-13`, `RUL-16`, `RUL-17`, `RUL-18`; northstar invariants 1, 5 and 6.

> **What this is:**
> - **(1) Measure first, with predictions committed:**
>   - every DMAMUX1 and PWR access in the armed run, ArduPilot's fixture and the 14 generated boots: the register, the value, the order, and what the firmware does next;
>   - what the stock models do with each;
>   - RM0433's rules for each, by page.
>     - DMAMUX1: the request-to-channel routing, the request generators and synchronisation, and what the DMA guard's start check already reads.
>     - PWR: `VOS` and its `VOSRDY`, the supply configuration's one-time write (`SDLEVEL`, `LDOEN`, `BYPASS` and its lock), `DBP`, `USB33DEN` and its ready flag.
>   - the memory and each shard's projection at 1.53.
>   - **Stop** with the field tables, the pages, the observers that would move, and a ruling question for anything ambiguous.
> - **(2) The DMAMUX1 guard:** a wrapper over the stock model, allow-listed by the ledger (`RUL-17`).
>   - Each channel's route is served on read and judged against the stream the DMA guard starts.
>   - A route the platform cannot raise is a refusal by name, never a silent stream.
>   - The request generators and synchronisation are `unmodeled` unless (1) finds a firmware that uses them.
> - **(3) The PWR guard:** the supply configuration is a one-time write, as silicon's is.
>   - `VOS` takes its `VOSRDY` time from the page, or `unmodeled` if no figure is stated.
>   - The flash guard and the RCC guard read the voltage scale from it, not an assumption of Scale 1. A clock raised above the scale's limit before `VOSRDY` is refused by page.
>   - `DBP` gates the backup domain as RM0433 says.
> - **Each rule comes with a planted firmware, and each test with a mutant it kills.** The armed run arms with no record, as on `main`.
>
> It is NOT:
> - TIM5 (that waits for the ruling);
> - DMAMUX2 or BDMA (left for Phase 4 by the cut);
> - the gate-proof fixes (`TVI-WO-1203`, running beside you);
> - `model/Refusal.cs`;
> - Aethelgard.

## 1. Build

1. **Measure (1).** Stop on the bus.
2. **Build (2)–(3) to the ruling,** one guard after the other in one branch. Merge `main` before READY.

## 2. Acceptance

- **Planted firmware:**
  - a stream started on a channel whose route names another request refuses;
  - a second supply-configuration write is ignored as silicon ignores it, and named;
  - a clock raised before `VOSRDY` refuses.
- **The armed run, the fixture and the 14 boots:** no new record, boot blocks byte-identical, and the armed run arms. Or each difference is named and ruled.
- **The release manifest restates both guards' instances** only as `1203`'s rule allows (an instance a replay has run guarded), and its test passes for them.
- **The budget is judged in CI. CI is green.** Re-weigh `times.tsv` for your rows from this run, since the plan under-reads by about 15 %.

## 3. Fences

- **Fence:**
  - new `model/GuardedSTM32H7_DMAMUX.cs` and `model/GuardedSTM32H7_PWR.cs`, their data and allow-lists;
  - the flash and RCC guards' voltage-scale read only;
  - `tests/1308/`, planted `firmware/`;
  - your own rows in `tests/ci/*.tsv` and `tests/mutants.tsv`;
  - `docs/work-orders/TVI-WO-1308.*`.
- **Not:**
  - the class map's generator (`1203` owns it; add your instances only through the API it lands, and merge `main` after it);
  - every other guard's logic;
  - the steering documents.
- One PR, left unmerged. Write `~/.cache/tvi-1308/pr-title-body.md`.

## 5. Adversarial pass, full

- **A route that lies:** a channel served on read from a copy that silicon would not hold.
- **A voltage scale assumed:** any reader that still takes Scale 1 without asking the PWR guard.
- **A one-time write that is not one-time** across a reset, or that a recorded mode lets through.

## 7. Flag-and-stop conditions

- **After §1,** with the measurement.
- **The armed run would refuse,** or an observer would change, on a path RM0433 says is valid.
- **The budget fails in CI, or the shard projection passes 20 minutes.** Then the eleventh shard comes first.
