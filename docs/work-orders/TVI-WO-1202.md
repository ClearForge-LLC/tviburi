# TVI-WO-1202 — CI wall time back under 20 minutes: parallel jobs, a docs-only route, and the mutants sharded, with nothing skipped that judges code

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1202-ci-wall-time`.
**Author:** Claude (co-architect) · **Builder:** standard tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of the CI that `1200`, `1201` and the `13NN` guards grew. **Cadence:** a measurement, a stop, then the build. **Measure now; build after `TVI-WO-1302` merges.** `1302` edits `tests/spike.sh`'s guard lists and `tests/mutants.tsv`, so merge main first.

**Grounds:**
- **Scotty, 2026-11-24:** "If tvi's CI is beginning to grow exponentially like aet's did then do a reduction pass on it … to bring us back to as minimum CI time as possible within reason."
- **Northstar §6:** "so slow or heavy that people stop running it".
- **The measurement that triggers this** (the architect, from the runs' own timestamps). `ci` is one serial job, `suite`, on every pull request and every push to main.
  - **Run 28–30** (2026-11-23, about 17:00Z): 18.5–21.3 min.
  - **Runs 49–56** (1201): 39.9–54.3 min.
  - **Run 67** (main after `1301a`, 45329914754): **52.6 min**, made of:

    | Step | Minutes |
    |---|---|
    | the mutation run | 22.2 |
    | the acceptance suite | 15.1 |
    | the memory budget | 6.3 |
    | the budget's own tests | 6.1 |
    | the adversarial cases | 1.9 |
    | the two image pulls | 1.0 |

  - **A docs-only pull request** (#29, run 59) took 34.2 min, since nothing routes.
  - Every guard work order adds about 30 mutants (`mutants.tsv` holds 204 rows) and about 30 acceptance cases, so the serial sum grows with each guard.
- **Aethelgard's answer to the same growth:** `AET-WO-1911`–`1917`, a route by diff with an inventory guard, a shared build, and shards weighed by measured time. This work order takes the pattern, not the code.
- **`RUL-14`** (programs in Rust; shell only for thin harnesses), **`RUL-09`** and **`RUL-15`** (the pinned tools image, no network for a run, GitHub-hosted runners).

> **What this is:**
> - **(1) Parallel jobs; the wall time is the longest job, not the sum.**
>   - The acceptance suite is sharded across runners by measured case time, with the plan checked: every case in exactly one shard, and none dropped.
>   - The mutation run is sharded the same way. **Every mutant still runs on every code pull request.** None is selected away by what the diff touches: a change to shared code (`Refusal.cs`, a guard's base) can let an old mutant survive.
>   - The memory budget stays **one job**, since main and the branch must share a runner (`1201`'s ruling).
>   - An aggregate job fails if any shard is missing or failed, or if a case or mutant ran twice or not at all.
> - **(2) A docs-only route.** A pull request whose diff touches only paths an allow-list classifies as documentation runs a fast check instead of the suite. Any unclassified path routes to the full run. An inventory test fails when a path that any test, script or model reads is classified as documentation. Pushes to main always run everything. The classifier is a Rust tool (`RUL-14`).
> - **(3) The budget's own tests** (`tests/1201/test-budget.sh`, 6.1 min) run on a pull request only when the diff touches the budget's code (`tests/1201/`, `tools/tviburi-budget/`, `tests/1301/ci-memory.sh`, `.github/workflows/`), and always on main. The path list is checked by the same inventory test.
> - **(4) Measure, do not guess:** the time of every case and every mutant from the last runs, the runner's cores, and whether one runner can run two Renode cases at once inside the memory ceiling. If cases can share a Renode instance, or a mutant can reuse a build, report it as an option with its saving. Build it only if ruled.
> - **(5) The target:** a code pull request's wall time **under 20 minutes**, and a docs-only one **under 3 minutes**, both measured on the PR itself. Say what each part costs and what limits it.
>
> It is NOT:
> - dropping, weakening, skipping or quarantining any case, mutant or check on a code pull request;
> - a self-hosted runner, a cache that crosses pull requests without a key on the pinned image and sources, or the network inside a run;
> - any change to `model/`, a guard, a case's assertions or a mutant's definition.

## 1. Build

1. **Measure first** (4). Commit predictions in `docs/work-orders/TVI-WO-1202.measure.md`, then record:
   - per-case and per-mutant times;
   - the shard plans for 2, 3 and 4 runners, with each plan's longest shard;
   - the cost of a job's fixed setup (checkout, image pull, build of the one file);
   - the route's path classes.

   **Stop** with the plan and its projected wall time.
2. **Build (1)–(3)** to the ruling, after `1302` merges. Merge main before READY.

## 2. Acceptance

- **On this PR itself:** a code PR's `ci` finishes in under 20 minutes of wall time. A docs-only PR, pushed as a separate commit or PR to show it, finishes in under 3 minutes.
- **Nothing lost:** every case and mutant from main runs, each exactly once, on a code PR. The aggregate check proves it, and a planted shard that drops one fails it.
- **The route's inventory test** fails on a planted path (a `.resc` or `.cs` file classed as documentation).
- **The memory budget** still runs in one job, main against the branch, on every code PR.
- **CI is green.**

## 3. Fences

- **Fence:**
  - `.github/workflows/`;
  - `tests/spike.sh`, `tests/mutants.sh` and `tests/adversarial.sh`: the selection and sharding harness only, never a case;
  - `tests/ci/` (new);
  - `tools/tviburi-route/` (new, Rust);
  - `docs/work-orders/TVI-WO-1202.*`.
- **Not:** `model/`, `firmware/`, `tests/1301/ci-memory.sh`'s judgement, `tools/tviburi-budget/`, any case or mutant, or the steering documents.
- One PR, left unmerged. Write `~/.cache/tvi-1202/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A case or mutant that silently stops running:** a shard plan that drops one, a renamed case, a new case with no measured time.
- **A code change routed as docs:** a `.md` file that a test reads, or a script that sits under `docs/`.
- **A green that is not:** a failed shard reported as skipped, or an aggregate that passes with a shard missing.

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement and the plan.
- **The target needs dropping or selecting away a check on code PRs.** Stop with the numbers.
- **Parallel jobs change a verdict:** a case that passes alone and fails in a shard, or the reverse. Stop with the case.
