# TVI-WO-1202 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1202-ci-wall-time`, from `main` `9e61b81`, with
`main` merged at `4d9a063` (TVI-WO-1302). **PR:** #33, left unmerged.

**State:**
- §1.1 was measured and stopped (`TVI-WO-1202.measure.md`), then ruled at 2026-11-24T10:30Z (Q1–Q6).
- Built to the ruling after 1302 merged (13:20Z).
- No §7 stop is open:
  - nothing is selected away on a code PR;
  - no case's verdict differed in a shard;
  - the teardown race did not appear in 360 shard case runs.
- `model/`, `firmware/`, every case's assertions, every mutant's definition, `tests/1301/ci-memory.sh`'s judgement,
  `tools/tviburi-budget/` and the steering documents are untouched.

## 1. What was built

| Part | File |
|---|---|
| The route, the inventory, the shard plan and the aggregate (Rust, `RUL-14`; 11 tests) | `tools/tviburi-route/` |
| The route's, a shard's and the aggregate's harnesses; the citations; the weights | `tests/ci/` |
| The jobs: `route`, `shard-1`…`shard-5`, `budget`, `budget-tests`, `ci` | `.github/workflows/ci.yml`, `shard.yml` |
| `TVIBURI_TESTS=list`, and `RAN <test>` after each test returns | `tests/spike.sh` (the dispatcher only) |
| `TVIBURI_MUTANTS` selects rows; an unknown id refuses | `tests/mutants.sh` |

`tests/adversarial.sh` is unchanged. Its 12 cases are in `tests/spike.sh`'s `all`, so they run in the shards.

**How a code PR runs:**
1. `route`: its tests, the inventory, then the class.
2. Five shards in parallel, with `budget` and `budget-tests` (the latter when the budget's list is touched; always on main).
3. `ci`: every job's result, then the aggregate.

**How a docs-only PR runs:** `route`, then `ci`.

## 2. Acceptance (WO §2)

| Clause | Result |
|---|---|
| A code PR's `ci` under 20 min, measured on this PR | **15.2 min** (run 45402235650), **14.5 min** (45405253648) and **15.0 min** (45408008635), wall time from the run's creation to its end |
| A docs-only PR under 3 min | **65 s** (run 45405252803, PR #34: one Markdown line against this branch, so it ran this workflow; branch deleted, PR closed, never merged). `route` said `class=docs`, `budget=no`; every other job was skipped |
| Every case and mutant from main runs exactly once on a code PR, proven by the aggregate | All three runs: `aggregate: 410 expected (180 cases, 230 mutants), 410 ran across 5 shards, 0 failures` |
| A planted shard that drops one fails it | `aggregate_refuses_a_dropped_a_doubled_and_a_foreign_item`. Also run on the real lists: one case dropped from a shard's record gives `FAIL case unit did not run in any shard`, exit 1 |
| The inventory test fails on a planted path | `inventory_refuses_code_classed_as_documentation` (`docs/board.resc`, `docs/Guard.cs` under a broadened classifier), plus a test that reads `docs/`, a stale citation, a symlink into `docs/`, and each budget path dropped |
| The memory budget still runs in one job, main against the branch, on every code PR | `budget`: 6.1, 5.9 and 6.2 min, `PASS` each time |
| CI green | Three code runs and the docs run: every job green |

## 3. Measured on this PR

| Job | run 1 (45402235650) | run 2 (45405253648) | run 3 (45408008635) |
|---|---|---|---|
| route | 0.8 | 0.8 | 0.9 |
| shard 1 / 2 / 3 / 4 / 5 | 9.3 / **13.5** / 12.2 / 8.8 / 9.2 | **12.6** / 9.0 / 9.3 / 10.3 / 8.9 | 9.2 / 12.1 / 8.7 / **13.2** / 9.1 |
| budget | 6.1 | 5.9 | 6.2 |
| budget-tests | 5.9 | 7.5 | 6.1 |
| ci (pull, then the aggregate) | 0.8 | 0.9 | 0.8 |
| **wall** | **15.2** | **14.5** | **15.0** |
| runner time, summed | about 67 min | about 65 min | about 66 min (the serial run took 44–53) |

- **The limit is the slowest runner, not the plan.** The plan balances weights to within 0.1 min, so a shard's time is
  its runner's speed. Measured, not fully explained:
  - Run 1's five shards ran at 0.85–1.29 × their weights.
  - Run 2's shards name their CPUs:
    - the three Intel Xeon 6975P-C shards ran at 0.86–0.89 × (median item);
    - the two AMD EPYC 9V74 shards ran at **1.23 ×** and **0.97 ×**.
  - The slow shard was slow across the board (p10–p90 1.17–1.29 ×), not in a few items.
  - So the CPU model accounts for part of the spread; two runners of one model still differed by a quarter.
  - Run 3 met two more CPU models. Its slowest shard (13.2 min) ran on an AMD EPYC 7763, and the next (12.1) on an Intel Xeon Platinum 8573C. Its three others ran 8.7–9.2 min, on an AMD EPYC 9V45 and two Intel 6975P-C.
- **Headroom.** At the slowest shard seen (13.5 min, about 1.3 × the weights), each guard work order adds about 9–10 min of serial work. That is about 2 min a shard, so about two more guards fit under 20 min before a sixth shard is needed.
- **The re-measure (asked at 13:20Z).**
  - Every item's time on this PR's run is in `TVI-WO-1202.times.tsv`, column `run33`.
  - 1302's 70 items now carry measured weights in `tests/ci/times.tsv`, each divided by its shard's speed against the rest.
  - Its slowest: `timer-half` 10.5 s, `TM09-passDrives` 20.5 s, `TM19-field` 17.6 s.
- **The fixtures a shard repeats (measure P3).** `identity` took 46 s in its shard, against 28 s serially, because it built the `modeled` run itself.
- **The teardown race (Q5).**
  - 0 lost end records in 3 × 180 shard case runs, and 0 in the serial runs before.
  - The rate did not rise, so there is no stop.
  - Nothing reruns itself.

## 4. The adversarial pass (WO §5), targeted

| Attack | Result |
|---|---|
| A shard plan that drops a case or mutant | The aggregate fails it (tested on the real 410-item lists, and in unit tests) |
| A renamed case, or a new case with no measured time | The expected set is the tree's own lists, never `times.tsv`. A new id weighs its kind's median and runs; a stale id in `times.tsv` is ignored. A renamed id that a shard ran under its old name: "not in the tree's lists" |
| A mutant row with an empty id | Run: bash's `read` collapses the leading tab, so `tests/mutants.sh` sees a row with four fields and refuses it ("bad row", exit 2). `items` keeps the empty id, so the aggregate also fails "did not run". Fail closed twice |
| A case that fails, or a mutant that survives, in a shard | Run on a fixture tree (two cases, one broken; one mutant made a no-op), through `tests/ci/shard.sh 1 1` on Furnace. `FAIL …`, `ALIVE R01-refusalFirst`, and the shard exited 1. Its record still held all three items, so the aggregate passes exactly-once, as designed. The failure reaches `ci` through the shard job's result, which the gate requires to be `success` |
| A push to main | `tests/ci/route.sh --all` run locally: the inventory, then `class=code`, `budget=yes` |
| A `.md` file a test reads | The inventory fails on any code file naming a docs file or `docs/` that is not a listed citation. 48 citations, each read in its file: comments, and strings that records or the ledger carry. 1302's ten new ones were caught by the inventory on the merge, then read and listed |
| A script under `docs/` | Not `.md`, so code. A broadened classifier fails the inventory's check 1 |
| **A symlink in code that points into `docs/`** | **Found:** the inventory read through the link and saw only the target's bytes, so a docs-only change could have changed what a test reads. **Fixed:** the inventory refuses a symlink that is or reaches documentation, or that leaves the repository. No symlink is tracked today |
| A failed shard reported as skipped | `ci` wants every shard `success` on code. Tested on 14 planted result sets |
| **An aggregate that passes with a shard missing** | **Found:** a shard result that is missing (a job dropped from `needs`, or renamed) expands to an empty word, and the gate's loop passed one word short. **Fixed:** the gate counts five shard results and the budget's. A shard whose record is missing still fails the exactly-once check |
| A route failure read as docs | An empty class fails `ci` ("the route gave no class"); shards are skipped only on `docs` |
| A changed path git quotes (unusual characters) | Starts with `"`, so code |
| A diff with `..` components | Code |

**Mutation (sized to the diff).** One mutant per guard this WO adds, run against `tools/tviburi-route`'s tests: 17 of 17 killed.
- `G10` (a budget script itself outside the list) survived at first, because every prefix-dropping test also tripped the reads check. A test with only the script outside the list now kills it.
- The shell guards were checked directly:
  - `TVIBURI_MUTANTS` with an unknown id exits 2 before any container;
  - the `ci` gate was run on 14 planted result sets.

## 5. The reduction pass

Reduction pass (diff): +1 net (19 added, 18 removed: the shard count's one home carries a comment naming its callers) · 2 duplicates removed (the shard count, five copies to one; the case-list call, two to one) · 2 dead paths removed (`classify`'s unvaried classifier parameter; a log line nothing reads) · 0 protected kept · 2 findings below.

Run by a fresh reviewer. Of its six proposals, four were taken. After them, the tests pass and 17 of 17 mutants are still killed. Two were rejected:
- **`LICENSE-*` by prefix in the inventory's check 1.** It would let a broadened classifier class `LICENSE-x.cs` as documentation. Check 1 exists to be stricter than the classifier.
- **The `#`-comment skip moved into the shared line reader.** `classify` reads the diff through it, so a changed path starting with `#` would vanish. A diff of `docs/a.md` and `#x.cs` would route as docs.

## 6. Rows and findings

- **F1. Runner speed sets the wall time.**
  - GitHub's 4-vCPU runners seen here: AMD EPYC 9V74, 9V45 and 7763; Intel Xeon 6975P-C and Platinum 8573C.
  - The same shard takes about 1.3 × as long on the slowest.
  - The shard count is one number (`shard.yml`'s default, and the five jobs in `ci.yml`).
  - A sixth shard is the remedy when the slowest projected shard passes about 17 min: roughly two guard work orders from now.
- **F2. The aggregate's image pull sits on the critical path** (about 45 s of `ci`'s 50 s). It is the price of running the Rust aggregate in the pinned image (`RUL-14`, `RUL-15`). Not changed.
- **F3. Runner time rose** from 44–53 min serial to about 65–67 min, because of five shards' setup plus the route and the aggregate. That is free on a public repository's standard runners (from memory, not checked). A PR now uses up to 8 jobs at once, against the organisation's concurrent-job limit, which Aethelgard shares. Both runs started every job within 3 s.
- **F4. `cancel-in-progress` for superseded PR runs** would free runners when a branch is pushed twice in a row, as this one was. Not added: outside the WO.
- **F5. One build per shard instead of per case** (measure §3.4): about 1–2 s of each case. It edits the case runners, so it was not built.
- **F6. `tests/spike.sh`'s `modeled` hazard (Q6)** stands. `identity` or `adv-version` before `modeled` in one invocation aborts with no `FAIL` line. The plan keeps the suite's order in every shard, so it is not reached.
- **F7. `TVIBURI_MUTANTS=""` runs no mutant** and prints `ALL MUTANTS KILLED`. `tests/ci/shard.sh` never passes an empty selection, and the aggregate would miss its items anyway.
- **F8. A required status check, if branch protection has one, should name `ci`.** The read-only token cannot read branch protection (403). The organisation's rulesets it can read name no status check.
- **F9. The PR body cannot be edited by `gh-write`,** so it was written once, at opening, to hold through READY. The measured numbers are here, not in the body.
- **F10. A breach, owned:** in §1.1 I ran one bare `podman run` outside `capped-run` (a 1-second `ls` in the pinned image). Every other container went through `capped-run`, one at a time.
- **F11. Tokens.** Four mints, each revoked right after its last use, each `revoked (HTTP 204)`:
  - the first push and the PR;
  - the second push, the docs demonstration, and its branch's deletion;
  - the FEEDBACK push;
  - the final push.

  No token was ever in a command line: each was filled from the transcript by pattern.
- **F12. `tests/ci/citations.tsv` is a cost on every later work order.** The inventory fails on any code comment that newly names, or stops naming, a documentation file, until the list is edited. 1302 alone needed ten rows. The file is under `tests/ci/`, which is in the budget's list, so editing it also runs the budget's tests (about 6–7 min of runner time, off the critical path). It is also outside a guard work order's usual fence. **A ruling is wanted:** a standing fence note that any WO may add its own citation rows, or a narrower check (for example, references only from scripts, not from model comments).
- **F13. The Q5 remedy is not exercised.** A person re-running one failed shard ("re-run failed jobs") also re-runs `ci`. Whether `ci` then receives the four passed shards' outputs from the first attempt is not tested here: the read-only token cannot re-run jobs. From memory, not checked: outputs of jobs that are not re-run are kept. If they are not, the aggregate reports their items as not run, which fails closed, and the remedy is to re-run all jobs.
- **F14. A protected document is stale:** `docs/roadmap.md` Phase 1's row "CI runs the full suite (about 19 minutes) on a docs-only pull request" describes what this WO replaces. It is for the architect to update.

## 7. Suites, mutants and CI

- `tools/tviburi-route`: 11 tests pass in the pinned image. Its 17 guard mutants are all killed.
- PR #33: three code runs, all green, with the aggregate at 410 of 410 each time; the third ran the reductions and this FEEDBACK's first version. The docs run is green. The run on this FEEDBACK's final commit is in the READY note.
- One local shard (1 of 40) ran through the real harness on Furnace: 7 of 7 mutants killed and recorded.
