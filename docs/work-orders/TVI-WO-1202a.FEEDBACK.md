# TVI-WO-1202a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24/25. **Branch:** `wo/tvi-1202a-sixth-shard`, from `main` `d9bb3da`.
**PR:** #43, left unmerged.

**State:**
- Built to the WO with six shards, then rebuilt to the architect's ruling of 2026-11-25T00:00Z: **seven shards, planned against 1.53; 1.29 is superseded.**
- No §7 stop fired:
  - six shards met 20 min at the slowest factor measured (18.1 min a shard job at 1.53), and seven meet it with margin;
  - no case or mutant changed its verdict in a shard (§3).
- `model/`, `firmware/`, every case and mutant, `tools/tviburi-budget/`, `tools/tviburi-route/` and the steering documents are untouched.

## 1. What was built

| Part | File |
|---|---|
| The weights: every case and mutant on `main`, from CI's own logs, at the Intel Xeon 6975P-C's speed | `tests/ci/times.tsv` |
| The raw times per run, and each item's shard | `docs/work-orders/TVI-WO-1202a.times.tsv` |
| The method, the runner factors, both plans predicted (six in §3, seven in §4), committed before each plan's run | `docs/work-orders/TVI-WO-1202a.measure.md` |
| Seven shard jobs, and `ci` counting them: `needs`, the results (seven and the budget, 8 words), the records | `.github/workflows/ci.yml` |
| `of` is 7 | `.github/workflows/shard.yml` |

## 2. Acceptance (WO §2), measured on this PR

| Clause | Result |
|---|---|
| Every case and mutant on `main` runs exactly once across the shards; the aggregate proves it | Both PR runs: `aggregate: 646 expected (291 cases, 355 mutants), 646 ran across 7 shards, 0 failures` (six on the first). Every shard: `recorded N of N planned` |
| A planted plan that drops one case, or runs one twice, fails the aggregate | Run on the real 646-item lists, through the real aggregate with seven records from the real planner. As planned: exit 0. One case dropped: `FAIL case ardupilot did not run in any shard`, exit 1. One mutant twice: `ran 2 times`, exit 1. Shard 7's record empty, or absent: 93 failures, exit 1 |
| This PR's `ci` under 20 min of wall time; a slow runner named with its factor; the plan meeting 20 at the slowest factor | **16.8 min** (run 45516066323, seven shards), and an AMD EPYC 7763 was in it, at 1.47. The plan: 15.7 min a shard job at 1.53, and `ci` 17.4–17.7 |
| A docs-only PR still routes to the fast check | `route classify` on a docs-only change: `class=docs budget=no`. With seven `skipped` shards and a skipped budget, `ci`'s gate passes; with any shard not skipped, it fails. Not shown on a live PR (F6) |
| CI is green | Both PR runs (45513393843, six shards; 45516066323, seven) |

### 2.1 Each shard against the plan

The factor is a shard's measured items over its planned weight. A job is predicted as 53 s of setup plus the weight at 1.00.

**Seven shards, run 45516066323 (`3142dbd`), wall 16.78 min:**

| Shard | CPU | Items | Planned (s) | Measured (s) | Factor | Job (min) | Predicted at 1.00 / at 1.53 |
|---|---|---|---|---|---|---|---|
| 1 | AMD EPYC 7763 | 91 | 580 | 853 | **1.47** | **15.00** | 10.6 / 15.7 |
| 2 | AMD EPYC 9V74 | 96 | 580 | 665 | 1.15 | 11.98 | 10.6 / 15.7 |
| 3 | Intel 6975P-C | 91 | 580 | 597 | 1.03 | 10.65 | 10.6 / 15.7 |
| 4 | AMD EPYC 9V74 | 91 | 580 | 652 | 1.12 | 11.75 | 10.6 / 15.7 |
| 5 | Intel 6975P-C | 91 | 580 | 566 | 0.98 | 10.20 | 10.6 / 15.7 |
| 6 | Intel 6975P-C | 93 | 580 | 584 | 1.01 | 10.43 | 10.6 / 15.7 |
| 7 | Intel 6975P-C | 93 | 580 | 587 | 1.01 | 10.52 | 10.6 / 15.7 |

- Outside the longest shard: 1.78 min (1.03 from the run's creation to the shards' start, and 0.75 from shard 1's end to `ci`'s). The wall is 16.78 min.
- Predicted for this run's factors: 15.1 + 1.7–2.0 = 16.8–17.1.

**Six shards, run 45513393843 (`65adb7b`, before the ruling), wall 19.05 min:**

| Shard | CPU | Items | Planned (s) | Measured (s) | Factor | Job (min) | Predicted at 1.00 / at 1.53 |
|---|---|---|---|---|---|---|---|
| 1 | Intel 6975P-C | 110 | 677 | 692 | 1.02 | 12.22 | 12.2 / 18.1 |
| 2 | AMD EPYC 7763 | 105 | 677 | 990 | **1.46** | **17.30** | 12.2 / 18.1 |
| 3 | Intel 6975P-C | 108 | 677 | 668 | 0.99 | 11.97 | 12.2 / 18.1 |
| 4 | Intel 6975P-C | 107 | 677 | 695 | 1.03 | 12.27 | 12.2 / 18.1 |
| 5 | AMD EPYC 9V45 | 109 | 677 | 607 | 0.90 | 11.03 | 12.2 / 18.1 |
| 6 | Intel Platinum 8573C | 107 | 677 | 919 | 1.36 | 16.28 | 12.2 / 18.1 |

- This run met six shards' risk directly. The 7763 took 17.3 min, which put the wall at 19.05 min: 57 s from the target, at a factor of 1.46, below the 1.53 already seen.
- **The weights hold across both plans:**
  - the 6975P-C shards ran at 0.98–1.03 of their plan;
  - every CPU's factor is close to the measure file's table (9V74 1.12–1.15 against 1.17; 8573C 1.36 against 1.35; 7763 1.46–1.47 against 1.43–1.53).

## 3. The adversarial pass (WO §5), targeted

| Attack | Result |
|---|---|
| A case newly added with no time, or missing from `times.tsv` | Planted: no rows for `identity` and `U07`; then every row removed. Each time all 646 were planned exactly once (aggregate 0 failures). The expected set is the tree's lists, and a missing item weighs its kind's median |
| A renamed case | Planted: `modf`'s row renamed `modf-old`. The stale row is ignored, `modf` weighs the median, and all 646 still run once |
| A weight that breaks the packing | `NaN`, `inf` and `-500`: all 646 planned once, because a weight moves only balance, never membership. A malformed weight (`fast`) makes the plan refuse, so that shard's job fails |
| A shard skipped, cancelled or failed | `ci`'s gate, run from `ci.yml` on 17 planted result sets: each fails, and the three good sets pass |
| A shard with no record | Its `ran` output is empty, so its items are reported as not run (93 failures, exit 1). The gate fails it on its result too |
| A shard dropped from `needs` or from the results list | Its result is an empty word. The count (`[ $# = 8 ]`) fails 7 words or 9. Planted: one missing, seven, nine |
| `of` left behind the job count | `tviburi-route plan 6 7` refuses (`shard 7 of 6`, exit 2), so shard 7's job fails |
| **A plan that meets 20 min only on a fast runner** | **Found:** 1.29 was below the slowest runner already measured. The AMD EPYC 7763 is 1.43–1.53 × the reference, or 1.33–1.40 on 1202's scale, where 1.29 sits at about 1.41. On six shards it gives a 19.9–20.1 min wall. **Ruled at 00:00Z:** seven, planned against 1.53. This PR's first run hit the case: 19.05 min with a 7763 at 1.46 |
| A case passes alone and fails in a shard, or the reverse (§7) | Both PR runs: every case passed and every mutant was killed in its new grouping. Of 646 items, one strayed from its shard's factor by more than 5 s and 1.6 × (F3), and only in cost |

**Mutation (sized to the diff).** One mutant per guard changed, each killed:
- `[ $# = 8 ]` back to 7: 6 of the planted sets flip;
- the records loop without 7: `93 failures`, exit 1;
- `of` left at 6: the plan refuses, exit 2.

## 4. The reduction pass

Reduction pass (diff): −1 net · 3 duplicates removed (the records' list, now the glob `ran-?.txt` of what the loop wrote; the count in the gate's message; the runner factors in `times.tsv`'s header, which live in the measure file) · 0 dead paths removed · 0 protected kept · 0 findings.

Run by a fresh reviewer, on the six-shard diff, before the ruling. It proposed three changes, and all three were taken. The seven-shard edit then added the same lines a seventh time, which I re-read under the same four questions. Its `ci.yml` comment cites the two rulings, not the old count, so it stays. The aggregate's step was then run from `ci.yml` itself on the real records (646 of 646), and the mutants above were rerun.

The reviewer also rejected some changes, and I agree:
- deriving `[ $# = 8 ]` from `needs`, because the explicit count is what catches an empty word;
- passing `of` per job (more copies);
- merging the two `times.tsv` files (raw evidence against derived weights).

## 5. Rows and findings

- **F1. 1.29 is superseded, as ruled.**
  - The slowest runner seen is the AMD EPYC 7763, at 1.43–1.53 × the Intel Xeon 6975P-C, in six shard jobs over six runs, on five different item sets.
  - Others: Intel Xeon Platinum 8573C 1.16–1.36; AMD EPYC 9V74 1.12–1.18; AMD EPYC 9V45 0.90–0.97.
  - The 7763 took 6 of the 33 shard jobs seen, so most runs of seven will meet one.
- **F2. Headroom under seven: about one more guard.**
  - At 1.53, with 2.0 min outside the shards, seven shards hold 4,699 s at the reference. They carry 4,061 s, leaving 638 s, about 10.6 min.
  - The last three guards added, at the reference: DMA 542 s, flash 484 s, USART 402 s (cases and mutants).
  - **An eighth shard is due at the second guard work order from now**, or at the first if it is as large as DMA plus 1305a's additions. The next re-weigh can rerun `TVI-WO-1202a.measure.md`'s method on the new runs.
- **F3. A shard's first use of a shared fixture costs a boot.**
  - `stable` reuses `modf`'s run when `modf` ran earlier in the same invocation (`tests/spike.sh` `t_stable`). Main's old plan split them, so `stable` weighs 15.2 s from two boots. Seven shards put both in shard 7, where it took 6.8 s.
  - Apart from that, two items had one slow run in three, unexplained: `generated` 20.4 s (else 9.2, 6.6) and `field` 19.9 s (else 6.3, 6.2; it was first in its shard in all three, so it is not the shard's start). The mean takes them in, a few seconds each.
  - These move a shard by seconds, never a verdict, and the plans landed within 3 % on the reference CPU. Not changed: weighing by grouping would couple the planner to the cases.
- **F4. The aggregate's image pull (1202 F2) was not trimmed** (the ruling made it optional, if small and safe).
  - It pulls the pinned image the Rust aggregate runs in (`RUL-14`, `RUL-15`).
  - The two ways to trim it are not small and safe: run the binary on the bare runner, outside the pinned image, or hand a built binary between jobs, which is a new mechanism and another trusted input.
  - It costs about 45 s of the 1.7–2.0 min outside the shards.
- **F5. PR #43's title and body say six shards.** It was opened before the ruling, and `gh-write` cannot edit a PR (1202 F9). The body as built is in `~/.cache/tvi-1202a/pr-title-body.md`, for the squash message.
- **F6. The docs-only route was not shown on a live PR.** That would be a second PR (1202 showed it with PR #34, then closed it). Shown locally instead:
  - the classifier on a docs-only change;
  - the gate run from `ci.yml` with seven skipped shards.

  Say if you want a live demonstration.
- **F7. Running beside `TVI-WO-1305a`.** Its new cases and mutants weigh their kind's median until re-weighed, and they still run, by the tree's lists.
  - Whichever of the two merges second merges `main` and weighs those rows from its own run (WO §3).
  - The medians are 6.3 s for a case and for a mutant, near a USART item's measured mean (5.9 s a case, 6.1 s a mutant), so the balance error stays small meanwhile.
- **F8. Runner time.** A code PR now runs 11 jobs (route, seven shards, budget, budget-tests, `ci`). Summed job time:
  - 95.3 min on run 45516066323 (seven shards);
  - 99.1 min on 45513393843 (six);
  - 102.7 min on main's 45509971801 (five, on slower CPUs).

  The CPUs drawn move it more than the shard count, since each shard adds only about 50 s of setup. It is free on a public repository (the ruling). The seven shards all started within 1 s of one another, beside Aethelgard's runs.
- **F9. Tokens.** Three mints, each revoked right after its last use, each `revoked (HTTP 204)`:
  - the first push and the PR;
  - the seven-shard push;
  - this FEEDBACK's push.

  None was in a command line: each was filled from the transcript by pattern.
- **F10. Protected documents are stale, for the architect** (CLAUDE.md: findings, not edits):
  - `docs/roadmap.md`'s `TVI-WO-1202a` row still says "six shards … at the slowest runner factor (1.29)";
  - its `1202` FEEDBACK row ("0.85–1.29 × plan") and "A sixth CI shard, now due";
  - the WO itself ("Six shards", "1.29", "The aggregate counts six").

  The ruling of 00:00Z replaces all of them with seven at 1.53.

## 6. Suites, mutants and CI

- `tools/tviburi-route`: 11 tests pass in the pinned image, and its inventory is clean (329 files, 62 documentation, 0 failures).
- PR #43, both green:
  - run 45513393843, six shards, 19.05 min;
  - run 45516066323, seven shards, 16.78 min.

  The run on this FEEDBACK's commit is in the READY note.
- Every local container ran through `~/bin/capped-run`, one at a time, with `--network none`.
