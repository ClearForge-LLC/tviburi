# TVI-WO-1206 — The unaudited fields above the cut, read against the manual

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main` after `TVI-WO-1205` (#68) merges, named in the kickoff.
**Branch:** `wo/tvi-1206-unaudited-read`.
**Author:** Claude (co-architect) · **Builder:** strong tier (the `1205` builder, who built the join), with a full adversarial pass.
**Phase:** 1, closing. Written 2026-11-26. **Cadence:** read and record, then re-judge clause 1. A field whose reading puts it on the wrong side of the cut is a stop.

**Grounds:**
- **Scotty's ruling, 2026-11-26, on `1205`'s clause 1: (b).** RUL-17's "inherited, unaudited (acknowledged by name)" does not count as acknowledged for the gate. Clause 1 is **not met** until each such field on the flight paths is read, as `TIMx_SR` was (architect, 17:02Z; `TVI-WO-0100b.rm0433.md` A10). In his words: "I want to make sure that we stay as honest and accurate as possible."
- **The list:** `1205`'s join on `main` `5d4a61a` (`spike/1205/data/main-5d4a61a/states.tsv`). It is **105 rows above the cut and 24 armed registers outside rows, 129 items**, acknowledged only through `inherited-unaudited` fields:

  | Family | Rows | Example |
  |---|---|---|
  | USART1–3 | 57 | `CR1.OVER8`, `ICR.CMCF` |
  | GPIO A/B/D/E | 23 | `OSPEEDR` |
  | TIM1 | 9 | `CCMR1.CC1S` |
  | I2C1/2 | 10 | `CR1.ERRIE`, `CR2.ADD10` |
  | RCC | 6 | `APB1LENR.TIM2EN` |

  Registers: RCC 13, TIM1 3, USART 5, I2C1 1, SPI1/2 2.
- **The ledger's states** (`tools/tviburi-release/src/ledger.rs`): `guard`, `inherited-unaudited`, `acknowledged`, `tag`, `reserved`, `canned`, `read-only`, `stock-wrong`, `stock-undefined`. The rows live in each guard's field table in `model/`, and they are data, not behaviour.
- Northstar invariants 1 and 5; `RUL-16`, `RUL-17`, `RUL-18`.

> **What this is:** for every `inherited-unaudited` field behind the 129 items, a reading, recorded in its ledger row:
> - **(1) What silicon does,** from RM0433 Rev 9 by page, for every value the generated boards and ArduPilot's armed run write. The values are in the join's `arm_field_values` and `gen_writes`.
> - **(2) What the stock model does** with those values, from the pinned stock source by file and line (the build `1.17.0+20260907gitf1dd1b4af.aet1605`).
> - **(3) The row's new state, one of three:**
>   - **`acknowledged`:** stock behaves as silicon for every value written. The row cites both pages: the manual's and the stock source's.
>   - **`stock-wrong`:** stock differs, but the flight path is unaffected. The row says why, as `TIMx_SR`'s `BIF` rows do (what is never read, never enabled, or never written).
>   - **A stop:** stock differs, and the firmware's behaviour depends on it. That is a path above the cut that is neither guarded nor truly acknowledged, so it needs a guard rule. Stop with the field, the pages and the firmware's dependence.
>   - A field never written by either firmware stays `inherited-unaudited` only if it is not on a flight path. Then it isn't one of the 129. Say so per row.
> - **(4) Clause 1 re-judged:** the join re-run on the branch, with 0 items above the cut resting on `inherited-unaudited`. Then an addendum to `TVI-WO-1205.verdict.md`'s clause 1 line: **met**, or not met with each stop named.
>
> It is NOT:
> - a new guard rule (a stop names the need);
> - the 69 inherited rows and 82 registers off the cut (Phase 4's);
> - a change to the ledger's states or schema;
> - the steering documents.

## 1. Build

1. **Commit predictions first:** for each family, how many fields you expect to be `acknowledged`, `stock-wrong` and stops. That guards against reading toward a total.
2. **Read in parallel if it helps.** Reading workers may each take a family, but you check every row they return: page, line and value set, before it enters a table. Every row cites RM0433 Rev 9 by page and the stock file by line. Anything from memory is marked "from memory" and does not count as read.
3. **Edit the field tables' data only.** No behaviour line changes. Regenerate any generated table (for example `tests/1302/fields.py`'s) from its generator, so the `regen` cases hold. The release hash moves; say so.
4. **Re-run the join, the armed run and the 16 replays** on the branch: zero records, armed. Then the verdict addendum, and stop.

## 2. Acceptance

- **The join on the branch:** 0 above-cut items resting on `inherited-unaudited`. Each of the 129 now rests on `guard`, `acknowledged` (cited) or `stock-wrong` (cited, with why), or it is a named stop.
- **Every changed row** cites a manual page and a stock source line. A check (`spike/1206/`) greps each changed row for both and fails on a row that lacks either.
- **The armed run arms and the replays give 0 records.** Nothing behavioural moved, so any difference is a stop.
- **CI green;** the budget is unchanged within noise.
- **The verdict addendum** fits in the existing page.

## 3. Fences

- **Fence:**
  - the field-table rows of the guards in `model/` (the rows' state and text only);
  - their generated tables and generators' data;
  - `docs/work-orders/TVI-WO-1206.*`;
  - `spike/1206/`;
  - one addendum line, with its paragraph, in `TVI-WO-1205.verdict.md` and `TVI-WO-1205.FEEDBACK.md`.
- **Not:** any behaviour, `tests/` beyond regenerated tables, the release program, the steering documents.
- **Running beside you:** `TVI-WO-2100` (#69), parked on this verdict. Its notes fill from the release built after you merge.
- One PR, left unmerged. Write `~/.cache/tvi-1206/pr-title-body.md`.

## 5. Adversarial pass, full

- **A row read toward the answer:** an `acknowledged` whose cited page doesn't cover the value written, or whose stock line handles another register.
- **A `stock-wrong` whose "why unaffected" is false:** a field the firmware does read, or a bit set at reset that the firmware relies on.
- **A sibling read once and copied to every instance** (`usart2` and `usart3` from `usart1`) where the instances' stock code paths differ.
- **Re-read 20 % of rows independently,** chosen at random with the seed committed.

## 7. Flag-and-stop conditions

- **A field whose stock behaviour differs and that the firmware depends on.** Stop with it.
- **A reading that needs a ledger state the release program lacks.**
- **The armed run or a replay changes.**
- **At the addendum.**
