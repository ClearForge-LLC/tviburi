# TVI-WO-1202 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1202-ci-wall-time`, from `main` `9e61b81`.
**State:** predictions only (§0). Measurements follow in a later commit.

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
