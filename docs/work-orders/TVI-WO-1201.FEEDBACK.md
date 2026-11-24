# TVI-WO-1201 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23/24. **Branch:** `wo/tvi-1201-memory-budget`, from `main` `e891211`, with `main` merged before READY (§7). **PR:** #26.

**State:**
- §1's measurement ran first. **The §7 stop did not fire.** Main against main differed by −0.4, +3.1 and +1.1 MiB at the median, in three CI jobs on two clouds and three CPUs (§1).
- (1)–(4) are built, and the acceptance is met in CI (§3). The planted 64 MiB branch is refused by `budget` alone, at +62.0, +56.8 and +64.3 MiB, and main against main passes.
- **Ruled at 23:12Z (architect):** `tests/1301/replay-1301.sh` is in the fence. Its GPIO condition is restored (§2), and it now snapshots the peak.
- `model/`, `tools/`, the steering documents, `README.md` and `docs/reviews/` are untouched. One PR, left unmerged.

---

## 1. What was measured (WO §1.1)

Every figure is a peak in KiB, as defined in §2: the sampled sum of Renode's VmRSS, every 0.1 s. Each boot is ArduPilot's full foreign boot on the ELF `a799dfb6`, which every job rebuilt and checked.

| Where | Comparison | main median | branch median | Difference | Every boot (main / branch) |
|---|---|---|---|---|---|
| CI 45285950556, Azure westus3, AMD EPYC 9V45 | main against main (both releases `3aab17f5…`) | 809,404 | 809,000 | **−404 (−0.4 MiB)** | 801,024–809,568 / 801,000–809,500 |
| CI 45285950556 | main against main + the plant | 808,588 | 872,088 | **+63,500 (+62.0 MiB)**, refused by `budget` | 805,736–809,552 / 866,808–877,008 |
| CI 45286469273, AWS us-west-2, Intel Xeon 6975P-C | main against main | 991,280 | 994,420 | **+3,140 (+3.1 MiB)** | 989,948–1,007,148 / 989,348–999,460 |
| CI 45286469273 | main against main + the plant | 998,488 | 1,056,648 | **+58,160 (+56.8 MiB)**, refused by `budget` | 981,848–1,022,820 / 1,055,448–1,076,592 |
| CI 45294287114 (final head), Azure westus2, AMD EPYC 9V74 | main (`0d3b4af`) against main | 784,420 | 785,552 | **+1,132 (+1.1 MiB)** | 777,620–784,784 / 780,768–804,196 |
| CI 45294287114 | main against main + the plant | 784,572 | 850,368 | **+65,796 (+64.3 MiB)**, refused by `budget` | 784,296–795,480 / 846,844–866,092 |
| CI 45286469273 | **1301 (`d598442`) against the commit before it (`b019105`)** | 989,392 | 995,116 | **+5,724 (+5.6 MiB)**, passes | 980,652–997,376 / 989,996–1,006,300 |
| Furnace, unshared moment, 10–11 s a boot | main against main + the plant | 1,005,456 | 1,075,992 | +70,536 (+68.9 MiB), refused by `budget` | 994,168–1,171,940 / 1,040,728–1,088,520 |

- **The median is stable within a job; single boots are not.**
  - Main's release was booted twice in each job, once in the gate and once as the plant test's main. The two medians are 0.8 MiB apart in the AMD job (809,404 and 808,588) and 7.0 MiB apart in the Intel job (991,280 and 998,488).
  - Main against main lands within 3.1 MiB of zero, well under the 16 MiB stop.
  - Single boots of one release span up to 40 MiB within a job (the Intel job's plant-test main: 981,848–1,022,820). On Furnace, one boot a side of main against main differed by −15.6 MiB. The judge therefore never decides on fewer than 5 a side.
- **The plant's cost is 57–69 MiB, not exactly 64 MiB.** The array is 64 MiB and is written page by page. **A guess, not measured:** the GC's other garbage at the peak shifts around it. Its window in the test is 48–80 MiB (§2).
- **1301 measured +5.6 MiB here,** where the WO expected about 10. 1301's own side-by-side measurement (run 45257810560, `e831735`'s `ci-memory-gc.sh`) gave about 10 MiB. That used the same kind of harness (the branch's `replay-1301.sh` for every configuration) but no witness check, and a different pair of trees: 1301's pre-merge branch against the main of its time. The gap between 5.6 and 10 MiB is inside the within-job spread of medians above. The `b019105` boots carry 3 witnesses (SPI1, SPI2, RCC) and the `d598442` boots carry 14 (11 GPIO ports more), so each side ran its own guard set.
- **The first boot of a job can be an outlier.** Furnace's main boot 1 reached 1,171,940 KiB at 16 s. The median absorbs it, and the alternating order (main first, then branch first) spreads it across sides.

## 2. What was built

| Item | Where |
|---|---|
| **(1) The step** | `tests/1301/ci-memory.sh [MAIN BRANCH]`, rewritten (`HEAD^1` and `HEAD` by default; a commit or a directory each). |
| **(2) The record** | `$GITHUB_STEP_SUMMARY`: the two release hashes, the verdict with both medians and their difference, and a row for every boot. |
| **(3) 1301's absolute step** | Replaced in `.github/workflows/ci.yml`; `fetch-depth: 2`. |
| **(4) The spread** | Every job records the runner. Every boot records Renode's breakdown at its peak (§4). |
| **The judge** (ruled 01:05Z, Q1) | `tools/tviburi-budget`, a std-only Rust program with 17 unit tests. The step builds it from the checkout and runs its tests before every verdict. |
| **Tests** | `tests/1201/test-budget.sh`, in CI after the step; the fixture `tests/1201/fixtures/Planted64MiB.cs`. |

**What (1) does:**
- Each tree's release is built once, by that tree's own `scripts/build-release`, into its own directory, and mounted read-only at `/tmp/release` for that side's boots.
- The harness (`replay-1301.sh`), the captured run and the ELF come from the checkout, the same for both sides. So only the release differs.
- The boots are interleaved, with the order alternating by round. There are at least 5 a side, enforced by the judge.
- **The shell decides nothing** (`RUL-14`, ruled 01:05Z). It writes one row per boot of what it sampled, under a header that names the columns: the peak (empty if none was sampled), the wall time, the count of boot ends, the witnesses of the boot's own release and of another, the GPIO would-refuse, and the peak's breakdown. `tools/tviburi-budget` reads the row by header name and decides.

**A boot counts, in the judge, only if all of these hold:**
- it reached `AETHELGARD-BOOT-END` exactly once;
- a peak was sampled;
- at least one witness on the console reports the release hash of the side the boot is labelled with, and none reports another;
- on the branch, it has no GPIO would-refuse (1301's check, kept).

**The judge** (`tviburi-budget RUNS.tsv`) refuses by name:

| Refusal | When |
|---|---|
| `failed-boot` | Any boot that does not count, on either side. It is never dropped silently from a median. |
| `too-few-boots` | Fewer than 5 boots a side, or no counted boot. |
| `ceiling` | A branch boot over 1,572,864 KiB. |
| `budget` | The branch's median more than 32,768 KiB over main's. |

The median of an even count is the mean of the middle two, rounded down.
- **Exit statuses:** 0 only on `PASS`; 1 on a refusal; 2 on a table it cannot read.
- **What counts as unreadable:** no header, a missing column, a row of the wrong length, an unknown side, or a field that is not a count. An unreadable table fails the step, never passes it.
- **When a boot left no whole snapshot,** the shell still writes the row's breakdown fields, empty, so the judge names that boot instead of stopping at a short row.

**What CI compares:**
- **On a pull request,** `HEAD` is GitHub's merge commit and `HEAD^1` is main's tip. `TVIBURI_MAIN_SHA` is set to the pull request's base, and the step stops before any boot if `HEAD^1` is not that commit.
- **On a push to main,** `HEAD^1` is main before the push. No base SHA is set there, so a push of several commits at once compares only the last against its parent.

**`replay-1301.sh` (ruled 23:12Z):**
- **The GPIO condition is restored.** The GPIO restatement is wired only when the tree has `model/stm32h7-gpio-restate.repl`.
  - 1301 had this condition at `e831735`, for its side-by-side GC measurement. Its reduction pass removed it once that measurement step was deleted, because every tree it then replayed had the guard.
  - It is back because one harness now wires two trees. A guard PR's main has no restate file for the new guard, and the pre-1301 leg in §1 needed exactly this.
  - **Every later guard PR** should wire its guard under the same kind of condition.
- **The peak snapshot.** At each new peak sample, it keeps the largest matched process's `/proc/<pid>/status` and `smaps_rollup`, but only when the snapshot has `RssAnon`.
- **The release is mounted read-only.** The script's existing `[ -s /tmp/release/Tviburi.cs ]` skips its own release build, so cargo no longer runs inside every boot.

**The tests** (`test-budget.sh`):
- **The judge's own cases are `tools/tviburi-budget`'s 17 unit tests,** which the step runs before every verdict. They replace the 12 canned shell tables.
  - **The four the ruling names:** the planted 64 MiB, on CI 45294287114's medians; main against main, on that run's ten boots; a boot with no sample, both absent and 0; and fewer than 5 boots.
  - **The boundaries:** the 32 MiB and 1,572,864 KiB limits pass exactly and refuse 1 KiB over. A main boot over the ceiling is not the branch's.
  - **The median:** two high boots of five do not move it, three do. The even-count median is tested directly.
  - **Failed boots:** a failed boot with a low peak is left out of the median; each of the five faults, alone, fails a boot; a would-refuse on main does not; a failed boot on main does; every branch boot failed.
  - **The table:** it is read by header name, an empty peak is none, and six malformed tables are errors.
  - **The report:** its exact lines.
- **The main-SHA binding:** a base that is not main stops the step before any boot.
- **The planted branch:**
  - main's tree with the fixture added to a scratch copy's `model/` (`tools/tviburi-release` concatenates every `model/*.cs`), booted against main by the real step;
  - it must be refused by `budget` and by nothing else, with the medians 48–80 MiB apart;
  - `TVIBURI-PLANTED 67108864` must be on the console of every branch boot.
- **How the fixture runs:** a `[ModuleInitializer]`, which runs before the first use of any type in the compiled assembly. It writes every page of a 64 MiB array and holds it in a static field.

## 3. Acceptance (WO §2)

| Criterion | Evidence |
|---|---|
| **The budget refuses the planted 64 MiB branch** | CI 45285950556: `REFUSE budget` at +63,500 KiB, alone, the plant in 5 of 5 boots. CI 45286469273: +58,160 KiB, the same |
| **…and passes main against main** | CI 45285950556 (−404 KiB), 45286469273 (+3,140 KiB) and 45294287114 (+1,132 KiB): `PASS`. This PR changes no `model/`, so its own gate is main against main |
| **CI green on the PR, the step's summary here** | Runs 45285950556, 45286469273 and **45294287114 (head `ca14cbd`)** are green |

**The step's summary, CI 45286469273, gate** (this PR against main):

```
main release sha256:3aab17f5b083207474030ebadfb8a28d6f0162a47f02a823d37091c22594ee11
branch release sha256:3aab17f5b083207474030ebadfb8a28d6f0162a47f02a823d37091c22594ee11
MEDIANS main 991280 KiB, branch 994420 KiB, difference 3140 KiB (+3.1 MiB)
PASS: every boot counts, every branch boot at or under 1572864 KiB, the median within 32 MiB of main's
```

**For every guard WO's FEEDBACK (WO (2)):** quote the `MEDIANS` line of the gate step. 1301's cost on this harness is +5.6 MiB (§1).

## 4. The spread between runs (WO (4)): measured, not fixed

**Six CI jobs of nearly the same tree.** The cloud and region come from each job's "Set up job" log. The CPU is from `/proc/cpuinfo`, recorded only from this WO's step on.

| Run | Cloud, region | Runner image | CPU, nproc, L3 | Peaks (KiB) | Wall (s) |
|---|---|---|---|---|---|
| 45249617507 (1301) | Azure centralus | 20261116.376.1 | not recorded | 1,018,148–1,049,556 | 18.9–20.3 |
| 45257810560 (1301, GC) | AWS us-east-1 | 20261116.133.1 | not recorded | 960,464–1,000,328 | 13.1–14.6 |
| 45265963786 (1301) | Azure centralus | 20261116.376.1 | not recorded | 768,856–776,020 | 15.2–16.2 |
| 45273210903 (1301) | Azure westus3 | 20261116.376.1 | not recorded | 775,968–795,552 | 15.9–17.1 |
| 45285950556 (this) | Azure westus3 | 20261116.376.1 | AMD EPYC 9V45, 4, 32 MiB | 801,000–809,568 (main against main) | 12.4–13.4 |
| 45286469273 (this) | AWS us-west-2 | 20261116.133.1 | Intel Xeon 6975P-C, 4, 480 MiB | 989,348–1,007,148 (main against main) | 13.9–14.6 |
| 45294287114 (this) | Azure westus2 | 20261116.376.1 | AMD EPYC 9V74, 4, 32 MiB | 777,620–804,196 (main against main) | 16.2–17.7 |
| Furnace | — | — | Intel Core Ultra 7 265K, 20, 30 MiB | 990,088–1,009,648 unslowed; 1,180,932–1,208,196 at 18–19 s | 10–19 |

**What the data rule out:**
- **Wall time alone.** The two AWS jobs ran 13–15 s and peaked high; four of the five Azure jobs ran 12–18 s and peaked low. The final AMD job was the slower (16–18 s) and still peaked about 200 MiB under the Intel one (about 14 s). On Furnace, though, a contended boot does rise with wall time, as `1301` §8 found.
- **The cloud alone.** Azure centralus gave both 1.03 GB (45249617507) and 0.77 GB (45265963786).
- **The runner image.** Its version follows the cloud (`.376` Azure, `.133` AWS), not the peak.
- **L3 size, as a single cause.** Furnace (30 MiB of L3) peaks high, and so does the Xeon with 480 MiB, while the EPYC with 32 MiB peaks low. The link between L3 and .NET's gen0 budget is from memory, which is why it was a candidate at all.
- **Huge pages, shared memory, file pages and threads,** measured on a high runner (Intel Xeon, 45286469273) and a low one (AMD EPYC 9V74, 45294287114):
  - THP is `madvise` on both, and `AnonHugePages` is 0 in every snapshot.
  - `RssShmem` is 0, and `RssFile` is 91.5–93 MiB on both.
  - Renode runs 15 threads on both. It runs 16–17 on Furnace.

**What it is:**
- **Anonymous memory, measured on both kinds of runner.** At the peak of main against main, `RssAnon` is 888,832–914,736 KiB on the Intel runner and 685,076–711,392 KiB on the AMD runner, about 208 MiB less. `RssFile` is the same on both. So the whole runner-to-runner difference is anon.
- On Furnace, `RssAnon` is 870 MiB at 10 s and 1,091,004 KiB at 18 s: a contended boot's extra is also all in anon.
- This harness cannot say whether that anon is the managed heap or native memory (tlib's translation cache, Renode's own buffers). That needs `GC.GetGCMemoryInfo()` at the peak, or an `smaps` split by mapping.

**The leading candidate is the CPU model,** not established:
- Both AMD jobs are low (about 809 and 784 MiB). Both Intel machines measured are high (about 991 MiB on the Xeon; about 990–1,005 MiB on Furnace unslowed).
- Every older job is consistent with this if the slow Azure runner (45249617507) was an Intel part, but that job recorded no CPU.
- **The step now records the CPU, nproc, L3, memory, THP, kernel and image on every PR.** So the next few PRs accumulate the table that confirms or breaks this candidate, at no extra cost.

**Proposed row (for the architect):** "the 200 MiB spread follows the runner's CPU?":
- collect main's median and the CPU from the next N gate runs;
- then split anon into managed and native at the peak, with a GC probe through Renode's monitor after the boot, so the captured argv stays unchanged.

The budget no longer depends on the answer, because each job compares against itself.

## 5. The adversarial pass (WO §5), targeted

| Attack | Result |
|---|---|
| **Main's file built from the branch, or the branch's from main** | Each side is its own `git archive`, built by its own tree's release program. Each boot's witnesses must carry its side's hash. **Mutant:** the branch's boots given main's release (Furnace, against the plant): `REFUSE failed-boot: … no witness of sha256:e065cb4c…; 14 witnesses of another release`. Killed |
| **Main is not main** (checkout gives the PR head, so `HEAD^1` is the branch's previous commit) | `TVIBURI_MAIN_SHA` is the PR's base: `FAIL main is …, but the pull request's base is 0000…`, before any boot. Now a test case in `test-budget.sh` |
| **A median over too few boots** | The judge counts boots itself, not `TVIBURI_MEMORY_RUNS`. Mutant `n < MIN_BOOTS - 1`: killed |
| **A failed boot counted as a low peak** | **Real case, run twice:** a branch whose C# does not compile peaked at 642,044 KiB (shell judge), then 641,148 KiB (the Rust judge), in about 2.5 s, with no witness and no boot end. Both gave `REFUSE failed-boot: branch boot 1 does not count (boot end 0; no witness of its release)`. **Mutants:** failed boots in the median, and each of the five faults off: killed |
| **The Rust judge's guards** (one mutant each, against its 17 unit tests) | **13 of 13 killed.** The mutants: no-peak check off; boot end `!= 1` to `> 1`; own-witness check off; other-witness check off; would-refuse moved to main; the 5-boot minimum lowered; failed boots in the median; the no-median refusal off; ceiling `>` to `>=`; the ceiling applied to main; budget `>` to `>=`; the even median taken as the upper middle; the row length `!=` to `<`. **The last first survived:** no test had a row longer than its header, so a test was added and it is now killed. The shell judge's 8 mutants (killed in the earlier pass) went with it |
| **A plant that passes for the wrong reason** | It must be shown running (its console line, every boot), resident (a difference of at least 48 MiB) and refused by `budget` alone. A plant that broke the boot would refuse by `failed-boot` and fail the test |
| **The same sampler on both sides** | One `replay-1301.sh`, from the checkout, for both; its snapshot overhead falls on both alike |

**Not defended, by design:** a branch that edits the harness so that it measures the two sides differently. One harness runs both, but a PR can change that harness. That is review's job, not a gate's.

**Reported, not built** (the scope test): a guard that silently fails to load on some boots would lower that side's peak. Every boot's witness count is in the summary, and in every boot measured it was constant within a side (3 or 14), but no rule refuses a count that varies within a side. **Proposed row** if the architect wants it.

## 6. Findings

**Asked of the architect at READY, ruled 2026-11-24T01:05Z:**
- **Q1 — `RUL-14`: port the judge to Rust now. Done.**
  - `tools/tviburi-budget` is std only. Its unit tests include the four the ruling names.
  - The shell keeps the boots and the file handoff. It no longer decides whether a boot counts either: that moved into the judge too, because it is the same gate logic.
- **Q2 — no standing main against main on guard PRs.** This PR's three measurements are the calibration. A row proposes a weekly scheduled calibration run.
- **Q3 — the PR body:** the architect replaces it from `~/.cache/tvi-1201/pr-title-body.md` at merge.
- **(4) is recorded as the row's first measurement.**

- **F1 — my own error, corrected.**
  - **What I got wrong:** commit `add5f6f` says the empty peak snapshots (`Threads: 2`, no Rss lines; 2 of 30 boots in 45286469273) came from a transient second process.
  - **What it actually was:** on Furnace, with the process count recorded, 1 process matched and the snapshot was still empty. The cause is a new peak sampled while Renode exits, when its memory is already gone. The sum came from the line read just before.
  - **The fix:** `62dad14` keeps only a snapshot that has `RssAnon`, and reports that snapshot's own sum beside the peak.
  - **Still open:** whether the exit-time sample is itself the peak in some boots. The snapshot's sum makes that visible.
- **F2 — CI time, measured in run 45286469273.**
  - The gate took 4 min 51 s and the plant test 4 min 58 s. 1301's absolute step took 4 min 41 s (45273210903). So the memory steps cost about 5 min more a job.
  - Each comparison builds the ELF again (the plant test runs a second step invocation); sharing it was not worth a cache.
  - That job took 45 min in all, including the 4 min 52 s temporary step since removed. The timeout is 150 min.
- **F4 — the plant's window (48–80 MiB) can be breached. For the architect: left unchanged.**
  - **What happened:** the window was mine, 64 MiB ± the 16 MiB stop line. The three CI runs measured the plant at +62.0, +56.8 and +64.3 MiB, all inside it.
  - On Furnace, after the port to Rust (01:0xZ, 10–11 s a boot, load average 7.7), it measured **+88.4 MiB** (main 985,296, branch 1,075,844). The test failed on the upper bound. The step itself was right: it refused by `budget` alone.
  - **It is not double counting:** one process matched, and the branch's RssAnon sat about 90 MiB over main's. An earlier Furnace run gave +68.9.
  - **The range so far:** +56.8 to +88.4 MiB across six runs.
  - **What the bounds are for:** the lower bound shows the step sees most of the 64 MiB. The upper bound guards against a sampler that over-counts.
  - **The options:** keep 80 (a CI flake is possible but not yet seen); widen the upper bound to 96 MiB (64 + 32); or drop it, keeping only the lower bound and the refusal-by-name. Widening after a failure is a weakened test, so I have not done it.
- **F3 — for 1301a (running beside this WO):** it touches `model/` only. Its PR will be the first guard PR this budget judges. Its FEEDBACK should quote the `MEDIANS` line (WO (2)). If it adds a restate file, the harness must wire it under a condition (§2).

## 7. Close

- **Reduction pass (diff), by a fresh subagent, checked against the files:** −1 net line · 0 duplicates removed · 1 dead path removed · 3 protected kept · 2 findings below.
  - **Removed:** two TSV columns that nothing read, the host's MemAvailable and loadavg. MemAvailable is still printed per boot, from its own variable; load stays in `cost.txt`.
  - **Kept, though they look duplicated:**
    - the 32 MiB and 1,572,864 KiB literals in messages, the workflow names and the tests, as human-readable output, with the tests pinning the numbers independently;
    - the judge's two `too-few-boots` paths, both exercised;
    - `ACCEPT_MODE=recorded`, stated beside the harness's own default, which is explicit at the call.
  - **Findings, not acted on:**
    - `replay-1301.sh`'s `vmhwm_kib` and `load` in `cost.txt` are read by nothing now. They predate this WO and match 1300's and 1150's records, so they are kept for comparability.
    - `test-budget.sh` repeats the 5-boot minimum (`-ge 5`), because the step has no sourcing mode.
  - The judge's mutants were rerun after the cut: all killed. The summary table was checked by a one-boot run.
- **Main merged:** `0d3b4af` (#25, docs only).
- **Final CI: run 45294287114 on `ca14cbd`, green.** The runner was Azure westus2, AMD EPYC 9V74.
  - `MEDIANS main 784420 KiB, branch 785552 KiB, difference 1132 KiB (+1.1 MiB)`, then `PASS`.
  - The planted test refused by `budget` alone at +65,796 KiB, with the plant in 5 of 5 boots. `main-not-the-base` passed.
  - **The base binding, observed with main moved:** main is `0d3b4af` (`HEAD^1`), and the PR's `base.sha` is also `0d3b4af`.
  - The gate took 6 min 0 s and the plant test 5 min 55 s, on this slower runner. The job took 47 min.
- **What follows `ca14cbd`:** one docs-only commit (this FEEDBACK, completed from that run). The measured code is `ca14cbd`'s.
