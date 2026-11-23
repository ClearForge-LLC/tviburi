# TVI-WO-1201 — The memory budget, judged where it is stable: each pull request against main in the same CI job, under the consumer's 1.5 GiB ceiling

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1201-memory-budget`.
**Author:** Claude (co-architect) · **Builder:** standard tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of `1200`'s release and `1301`'s memory step. **Cadence:** a measurement, then the build. **After `1301` merges** (it owns `tests/1301/ci-memory.sh`).

**Grounds:**
- **Northstar §6:** "so slow or heavy that people stop running it".
- **Architecture:** `RUL-09` (a), the one-file overlay and the consumer's bound.
- **Scotty, 2026-11-23 evening** (the architect's recommendation):
  - the consumer's resident cap rises to 1.5 GiB (Aethelgard `RUL-13` as amended, `AET-WO-2313`);
  - Tviburi judges its own growth by a relative budget.
- **`TVI-WO-1301`'s measurements** (the roadmap's memory row):
  - ArduPilot's boot on the one file spans 768,856–1,049,556 KiB across CI runs of nearly the same tree, for a cause not yet known;
  - .NET's GC settings change nothing;
  - side by side in one job, the GPIO guard costs about 10 MiB.

> **What this is:**
> - **(1) A CI step that measures main and the pull request in the same job.** Build main's one file and the branch's. Boot ArduPilot (the ELF built and hash-checked as `1301` does) at least 5 times on each, interleaved, on the same runner.
>   - **The ceiling:** every boot of the branch stays at or under 1,572,864 KiB.
>   - **The budget:** the branch's median exceeds main's median by at most 32 MiB.
>   - Report every run's peak and wall time.
> - **(2) The record:** the step writes the two medians and their difference to the job summary. FEEDBACK of every guard work order quotes them, so growth stays visible guard by guard.
> - **(3) `1301`'s absolute step** (each peak ≤ 1,015,808 KiB) is replaced by (1).
> - **(4) The spread, measured (a row's first step):** record what you can about why the same tree peaks 200 MiB apart between runs. Candidates: the runner's CPU model, memory, and the Renode process's own breakdown (`/proc/<pid>/status`, `smaps_rollup`) at the peak. Report it; fix nothing.
>
> It is NOT:
> - a change to any guard;
> - the consumer's cap (Aethelgard's);
> - northstar's wording (§6 states no number).

## 1. Build

1. **Measure first:** the step on main against itself. The budget must pass with the difference near 0. Then on `1301`'s merged tree against the commit before it; the budget should see the ~10 MiB.
2. **Build (1)–(4).** A main-against-main run is the step's own test. So is a planted branch that allocates 64 MiB more at boot, which the budget refuses by name.

## 2. Acceptance

- **The budget refuses the planted 64 MiB branch**, and passes main against main.
- **CI is green on the PR,** with the step's summary in FEEDBACK.

## 3. Fences

- **Fence:** `.github/workflows/` (the memory step), `tests/1301/ci-memory.sh` or its successor, a planted-allocation test fixture, and `docs/work-orders/TVI-WO-1201.*`.
- **Not:** `model/`, `tools/` beyond reading, the steering documents.
- One PR, left unmerged. Write `~/.cache/tvi-1201/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A budget that passes by measuring the wrong file:** main's file built from the branch, or the branch's from main.
- **A median over too few boots,** or a boot that failed counted as a low peak.

## 7. Flag-and-stop conditions

- **Main against main differs by more than 16 MiB at the median.** The comparison is too noisy to judge 32 MiB. Stop with the runs.
