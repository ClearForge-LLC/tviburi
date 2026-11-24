# TVI-WO-1202 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1202-ci-wall-time`, from `main` `9e61b81`.
**State:** §1.1 measured, then **stopped** (WO §1.1 and §7) with the plan in §3 and the questions in §4. Predictions
committed at `5f9bcd2` before the measurements they predict. Every time below is from a run's own log timestamps or
from Furnace, and says which.

## 0. Predictions, committed before the measurements they predict

**Already read when this was written,** so not predicted here: the step times of runs 67 (`45329914754`, main at
`9e61b81`), 66 (`45322946066`, the same tree on its pull request), 64 (`45322337262`) and 63 (`45317502457`), parsed
from each run's own log timestamps; each serial case's gap and each mutant's gap in those logs; the runner
(4 vCPU AMD EPYC 9V74, 16 GiB, from `ci-memory.sh`'s own line); the job queue (2–3 s); and the image pull (37–44 s).
Run 67's acceptance step took 15.1 min and run 66's, the same tree, 12.1 min: a runner-speed spread of about 25 %.

**What these predict (not yet measured):**

- **P1. A case's ~7 s is mostly per-container build, not Renode.** Every case container compiles the firmware
  (`rustc`), the reference check (`cargo build`) and the release program (`cargo build`, then the release), then boots
  Renode. Predicted split of a `modeled` case: container start ≤ 1 s; the three builds 3–4 s; Renode 2–3 s; the check
  < 0.2 s. So a build reused across cases would save about half of every case's time. Reporting it only (it changes
  the cases' own scripts, outside the fence).
- **P2. A mutant's time is its tree copy plus the named tests:** about 0.5 s of copy and `git`-free tar, then each
  named test at its case time; `need_check` and the `modeled` fixture are paid again in every mutant.
- **P3. The serial gaps are close to standalone times,** except the first case that pays a shared fixture
  (`need_check`'s build, the `modeled` run, `id-records.tree`): those fixtures cost each shard about 10–15 s more than
  the serial log shows.
- **P4. Shard plans** (all cases and all mutants as one pool, packed by longest-first onto N runners, each runner's
  list kept in the suite's own order), at run 67's speed with `1302`'s 36 timer cases and 32 mutants added at today's
  median times (about 47 min of serial work):
  - 2 runners: longest shard about 24 min. Over 20 with the setup; **not enough**.
  - 3 runners: about 16 min of work, about 17.5 min wall time with setup. Under 20 with **about 2.5 min of headroom**.
  - 4 runners: about 12 min of work, about 13.5 min wall time.
- **P5. A job's fixed cost** is queue (≤ 10 s) + setup (2 s) + checkout (1–6 s) + the tools image pull (37–44 s) +
  the shard's own first fixtures (10–15 s): **about 1.0–1.3 min**.
- **P6. The memory budget job** stays one job at about 7 min (two pulls, the ELF build about 3.3 min, ten boots about
  2.9 min), so it is never the longest job once the suite is in 3 or more shards. **The budget's own tests,** in their
  own parallel job, take about 6.5 min and do not set the wall time either: (3) saves runner time, not wall time.
- **P7. The route** (checkout, the tools image pull, a `cargo build` of a dependency-free Rust crate, the
  classification): about 1.2 min, and it sits on a code PR's critical path because every shard waits on its answer.
  So a code PR: route 1.2 + longest shard + aggregate (0.2) ≈ **15 min with 4 runners**, about 19 with 3.
- **P8. A docs-only PR:** the route job (about 1.2 min), then the fast check (the route's inventory test, in the same
  job) and the aggregate: **about 1.5–2 min**.
- **P9. Two Renode cases at once on one 4-vCPU runner fit in memory** (a case's Renode peaks well under 1 GiB, about
  600–800 MiB; 16 GiB on the runner), and finish in about 1.2–1.4 × one case's time, not 2 ×, since a case's builds
  are mostly one core. **The verdicts are unchanged** in 5 of 5 paired runs; the teardown race (`roadmap` Phase 1)
  is the one risk, predicted not to appear on 4 cores that nothing else loads.
- **P10. Path classes:** documentation is `docs/**` but the data files beside the work orders, plus the root's
  `README.md`, `FEEDBACK.md`, `CLAUDE.md` and the two licence files. Everything else, every `.md` under `tests/`,
  `tools/` or `spike/` included, is code: `tests/1300/boots.sh` mounts `spike/1100/harness`, and `SOURCE.md` files sit
  inside fixtures. Predicted: no test, script, model or tool reads a file under `docs/`; the model sources cite
  `docs/work-orders/*.rm0433.md` in comments only.
- **P11. The budget's own tests read more than the WO's four paths:** `tests/1301/ci-memory.sh` mounts the whole of
  `tests/1301/` (its `replay-1301.sh` and the ArduPilot fixture), and takes the tools image from `tests/spike.sh`'s
  `IMAGE` line. So the trigger list needs `tests/1301/` and `tests/spike.sh` too, or a change there could break the
  budget's tests unseen until main.

## 1. Measured

**Method.** `gh run view --log` of runs 67, 66, 64 and 63, parsed by a scratch script that attributes each output line
to the case or mutant that printed it, in the suite's own order, and takes each item's time as the gap from the
previous item's last line to its own. Every list name received a line; the gaps tile each step to within 0.8 s
(mutants: 1331.5 s of 1332.3). The table of all 340 items is `TVI-WO-1202.times.tsv`, beside this file.

### 1.1 The steps (minutes)

| Step | run 67 (main, `9e61b81`) | run 66 (same tree, its PR) | run 64 | run 63 |
|---|---|---|---|---|
| queue + set up + checkout | 0.1 | 0.2 | 0.1 | 0.2 |
| tools image pull | 0.7 | 0.7 | 0.6 | 0.7 |
| acceptance (131 cases) | 15.1 | 12.1 | 10.8 (113) | 10.5 (113) |
| ArduPilot image pull | 0.3 | 0.5 | 0.5 | 0.6 |
| memory budget | 6.3 | 4.8 | 4.9 | 4.8 |
| the budget's own tests | 6.1 | 4.9 | 5.0 | 4.9 |
| adversarial (12 cases) | 1.8 | 1.5 | 1.6 | 1.6 |
| mutants (197; 176 before `1301a`) | 22.2 | 18.9 | 17.0 | 16.9 |

**The same tree took 52.6 min on run 67 and 43.7 on run 66: the runner's speed varies by about 20 %** (both AMD EPYC
9V74, 4 vCPU, 16 GiB). Every plan below is sized on the slower run of the two, item by item.

### 1.2 Cases and mutants

| | n | serial (run 67) | median | p90 | max |
|---|---|---|---|---|---|
| cases (acceptance + adversarial) | 143 | 17.0 min | 6.9 s | 7.6 s | 28.3 s (`identity`) |
| mutants | 197 | 22.2 min | 6.7 s | 10.4 s | 34.7 s (`U07-sourcesInManifest`) |

The cases are nearly uniform: 133 of 143 take 5.5–8.5 s. The outliers are `identity` (28.3), `adv-removed` (20.5),
`adv-loglevel` (20.4), `adv-release` (17.0), `adv-noend` (13.4), `rcc-hse16` (13.1) and `recorded` (12.3); the
host-only checks (`allowlist`, `adv-extra-arg`, `adv-bad-variant`, `adv-noname`) take under 0.2 s. A mutant costs its
named tests plus a tree copy: the 19 `R` mutants, killed by `unit`, take 0.5–0.9 s; the 4 that name two or more
Renode tests take 14.7–34.7 s.

### 1.3 What one case is made of (Furnace, a proxy)

One capped container pinned to 4 CPUs (`taskset -c 0-3`, as many as a runner has), the pinned image, `--network none`;
`tviburi-spike`'s own steps timed one by one, 13 runs each of `ardupilot` and `modf` (`~/.cache/tvi-1202/m/`):

| Step | seconds |
|---|---|
| firmware (`rustc`) | 0.03–0.23 |
| the reference check (`cargo build`) | 0.22–0.73 |
| the release (`cargo build` + the release program) | 0.61–1.19 |
| Renode | 3.06–6.01 (one 10.9) |
| the check on the log | ≤ 0.02 |

**Renode is 65–80 % of a case; the three builds are 1.0–1.6 s.** CI's ~7 s gap also carries podman's container start.

### 1.4 A job's fixed cost (CI)

Queue 2–3 s; set up 1–2 s; checkout 1–6 s; the tools image pull 37–44 s. **About 50 s.** Each shard also repeats the
shared fixtures the serial run paid once (`need_check`'s build, the `modeled` run, the `id-records` tree): estimated
at 20 s, not yet measured.

### 1.5 Two Renode cases at once on one runner (Furnace, a proxy)

The kickoff allows one container of mine at a time, so both cases ran **inside one container** (4 pinned CPUs, 8 GiB
cap), each in its own directory, either one after the other or both at once:

| | serial | at once |
|---|---|---|
| runs (`ardupilot` + `modf`) | 13 | 13 |
| wall per pair | 8.1–19.8 s | 4.5–11.1 s (about 0.55 ×) |
| the container's `memory.peak` (3-round runs) | 626 MiB | 1,131 MiB |
| `ardupilot`'s verdict PASS | 9 of 13 | **4 of 13** |
| `modf`'s verdict REFUSED | 13 of 13 | 13 of 13 |

Every `ardupilot` failure is the teardown race (roadmap Phase 1): Renode exits with the `end` records of `spi1` and
several ports unwritten, `PROBLEM …: no end record`, `CHECK FAIL`. **Memory fits easily** (two at once is 1.1 GiB of
16; each boot far under the 1.5 GiB ceiling). **But running two at once moved a PASS to a FAIL in 9 of 13 pairs,
against 4 of 13 serially, on a loaded host.** On CI, serially, the race has not been seen in these runs. So two at once
on a runner would change verdicts (§7's stop), and the plan does not use it.

### 1.6 The route's path classes

- **Nothing reads `docs/`.** The release program reads `model/` (every file), `tools/tviburi-release/` and
  `tools/tviburi-check/src/json.rs`; its `rm0433.md` names are strings in acknowledgements, not reads. The models cite
  `docs/work-orders/*.rm0433.md` in comments. `docs/work-orders/TVI-WO-1301.st-compare.tsv` is read by nothing
  (`TVI-WO-1301.ds12117.md` cites it).
- **`.md` is not a class.** `SOURCE.md` files sit inside fixtures (`tests/1301/fixtures/*/`,
  `tools/tviburi-check/tests/fixtures/`, `spike/1150/fixtures/`); `tests/1300/boots.sh` mounts `spike/1100/harness`,
  and `firmware/spi1.rs`'s generated path is `spike/1100/data/writes.tsv`. So `spike/` and every `.md` outside `docs/`
  are code.
- **The budget's own tests read more than the WO's four paths.** `tests/1301/ci-memory.sh` mounts the whole of
  `tests/1301/` into the boot (`/h`: `replay-1301.sh`, the ArduPilot fixture), takes the tools image from
  `tests/spike.sh`'s `IMAGE` line, and builds `tools/tviburi-budget/`. `tests/1201/test-budget.sh` boots main's tree
  and the plant, so a branch's `model/` does not reach it.

## 2. Predictions, scored

| | Predicted | Measured | |
|---|---|---|---|
| P1 | builds 3–4 s of a case, Renode 2–3 s | builds 1.0–1.6 s, Renode 3–6 s | **wrong:** Renode dominates; a reused build saves ~20 %, not half |
| P2 | a mutant is its copy plus its tests | yes: `R` mutants 0.5–0.9 s, the two-test ones 14.7–34.7 s | right |
| P3 | each shard repeats 10–15 s of fixtures | not measured yet | open; 20 s used in the plan |
| P4 | 2 / 3 / 4 runners: 24 / 17.5 / 13.5 min | 25.8 / 18.0 / 14.1 min (§3.2) | close |
| P5 | fixed cost 1.0–1.3 min | about 50 s, plus the shard's fixtures | right |
| P6 | budget job ~7 min, never the longest | 6.3 + 1.0 of pulls, setup | right |
| P7, P8 | route ~1.2 min; docs PR 1.5–2 min | not built | open |
| P9 | two at once: fits; verdicts unchanged | fits; **verdicts changed** (9/13 against 4/13) | **wrong** on the part that matters |
| P10 | nothing reads `docs/`; `.md` is not a class | yes | right |
| P11 | the budget's tests read `tests/1301/`, `tests/spike.sh` | yes | right |

## 3. The plan

### 3.1 Shape (`.github/workflows/ci.yml`)

1. **`route`** (always): checkout, the tools image, `tools/tviburi-route` built in it and run on the diff
   (`git diff --no-renames --name-only` against the base; on a push to main, `code` unconditionally). It prints one
   class, `docs` or `code`, and whether the budget's code changed. Its own tests, the inventory test among them, run
   here on every PR.
2. **`shard-1` … `shard-N`** (`code` only): each runs its slice of the cases (`tests/spike.sh`) and of the mutants
   (`tests/mutants.sh`), as `tools/tviburi-route plan` assigns them, in the suite's own order within the slice. The
   harness prints one `RAN <id>` line per case and the mutant run its existing `killed <id>`; the job hands its list
   to the aggregate.
3. **`budget`** (`code` only): the memory budget, one job, unchanged (two pulls, ELF, ten boots).
4. **`budget-tests`**: on a push to main always; on a PR when the route says the budget's code changed.
5. **`ci`**, the aggregate (`if: always()`): fails unless `route` succeeded; on `code`, every shard and `budget`
   succeeded (a skipped one is a failure) and `budget-tests` succeeded or was not required; and the shards' lists,
   joined, equal the tree's own lists (`tests/spike.sh`'s `ALL`, `tests/mutants.tsv`'s ids) with each id exactly once.
   The expected set comes from the tree, never from the times file: a case with no measured time gets the median
   and still runs. Branch protection, if any, should name `ci`.

### 3.2 Shard plans (longest first, slower of runs 66 and 67 per item; with `1302`'s 36 cases and 32 mutants at today's medians)

| runners | today: longest shard | after `1302`: longest shard | projected code PR (route 1.0 + fixed 1.2 + shard + aggregate 0.25) |
|---|---|---|---|
| 2 | 19.6 min | 23.4 min | 25.8 min: **over** |
| 3 | 13.1 | 15.6 | 18.0 min: 2.0 min of headroom |
| 4 | 9.8 | 11.7 | **14.1 min**: 5.9 min of headroom |
| 5 | 7.8 | 9.4 | 11.8 min |

The packing is near-perfect (every plan's shards are within 0.1 min of each other), because the items are small and
uniform. The budget job (about 7.3 min) and the budget's tests (about 7.3 min) are never the longest. Each guard work
order adds about 8 min of serial work, so 4 runners stay under 20 min for about five more guards at today's speed;
the runner count is one number in the workflow.

**Recommended: 4 shards.** Cost: about 4 × 1.2 min of extra setup per code PR in runner time (free on a public
repository's standard runners), and up to 7 concurrent jobs per PR against the organisation's concurrent-job limit,
which Aethelgard's CI shares.

### 3.3 The docs route

- **Docs:** `docs/**/*.md`, and at the root `README.md`, `FEEDBACK.md`, `CLAUDE.md`, `LICENSE-MIT`, `LICENSE-APACHE`.
  **Everything else is code,** including a path the list does not name, a deletion, and both sides of a rename.
- **The inventory test** (Rust, in `tools/tviburi-route`, over the checked-out tree) fails when: a docs path has any
  extension but `.md` (or is not one of the root files); or any tracked non-docs file names a docs path, except the
  comment citations listed in `tests/ci/citations.tsv`; or a path any budget script names
  (`ci-memory.sh`, `test-budget.sh`, `replay-1301.sh`: `$here/…`, the `/h` mount) lies outside the budget's trigger
  list. Its planted cases: a `.cs` and a `.resc` under `docs/`, a test that reads a `docs/` file, and a budget path
  dropped from the list.
- **A docs-only PR:** `route` (with the inventory test) then `ci`: projected **about 1.5 min**.

### 3.4 Options measured, not in the plan (WO (4))

- **Two cases at once per runner:** about 0.55 × the wall, 1.1 GiB; **changes verdicts** under load (§1.5). Not
  until the teardown race is placed.
- **One build per shard instead of per case** (release, check, firmware): saves 1.0–1.6 s of a 5–7 s case, about 2
  min per shard at 4 shards. It edits the case runners (`tests/1300/rcc.sh`, `tests/1301/gpio.sh`, `tests/1302/`),
  outside this WO's fence.
- **Cases sharing one Renode instance:** not measured. Each case's verdict rests on a fresh machine and its own log,
  so it would change every case; out of scope.

## 4. Questions for the ruling

1. **4 shards** (recommended, 14.1 min projected after `1302`), or 3 (18.0 min, 2 min of headroom)?
2. **The budget's tests' trigger list** grows from the WO's four paths to `tests/1201/`, `tests/1301/`,
   `tools/tviburi-budget/`, `tests/spike.sh`, `.github/workflows/`, `tests/ci/` and `tools/tviburi-route/` (§1.6)?
3. **The docs class is `docs/**/*.md` plus the five root files;** `docs/work-orders/*.tsv` routes as code?
4. **How the aggregate receives each shard's list:** explicit shard jobs with job outputs (no new action; a matrix
   cannot carry per-leg outputs), or `actions/upload-artifact` and `download-artifact` pinned by SHA? Recommended:
   job outputs.
5. **A harness hazard, flagged, not in scope to fix:** in `tests/spike.sh`, `identity` and `adv-version` create the
   `modeled` run if it is missing, and `t_modeled` then fails on `mkdir` under `set -e`, aborting with no `FAIL`
   line. The suite's order hides it; the plan keeps that order inside every shard.
