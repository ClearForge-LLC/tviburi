# TVI-WO-1202a — the re-weigh and the six-shard plan, predicted before this PR's run

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. Written and committed before this PR's first CI run, which measures it
(`TVI-WO-1202a.FEEDBACK.md` §2). **Ruled 2026-11-25T00:00Z (the architect): seven shards, planned against 1.53; 1.29 is
superseded** (§4). §3 is the six-shard plan the ruling replaced, kept because this PR's first run measured it.

## 1. What was measured

- **The runs.** Every run on `main` after `TVI-WO-1305` merged, every case and every mutant:

  | Run | Commit | Wall (min) | Longest shard (min) | Outside it (min) |
  |---|---|---|---|---|
  | 45493309831 | `d839934` (1305) | 23.15 | 21.17 (shard 4, AMD EPYC 7763) | 1.98 |
  | 45494954973 | `7048e19` (docs) | 22.00 | 20.30 (shard 4, AMD EPYC 7763) | 1.70 |
  | 45509971801 | `d9bb3da` (docs) | 22.52 | 20.67 (shard 5, AMD EPYC 7763) | 1.85 |

  The two docs commits change no code, so all three ran the same 646 items: 291 cases and 355 mutants. That is exactly the
  tree's lists (`TVIBURI_TESTS=list tests/spike.sh`, `tests/mutants.tsv`), with none missing and none extra.
- **The method.** `1202`'s method: each shard job's log, read with its timestamps. An item's time is the gap from the
  previous item's last line (`RAN <id>`, or `killed`/`ALIVE <id>`) to its own. A shard's first case starts at its `cpu`
  line, and its first mutant starts at its cases' `ALL PASS`. The gaps tile each job to within 1 s (one job, 6.7 s), with
  the job's setup and tail left over: 48.1 s on average and 53.3 s at most, over the 15 jobs. Raw times are in
  `TVI-WO-1202a.times.tsv`.
- **Each shard's speed.** A shard's speed is its median item against `1202`'s old weights (items of 1 s or more), divided
  by the median of the same run's Intel Xeon 6975P-C shards. An item's weight is the mean of its three times, each
  divided by its shard's speed. So `tests/ci/times.tsv` is at the 6975P-C's speed, 1.00.

  | CPU | Shard jobs | Speed against the 6975P-C |
  |---|---|---|
  | Intel Xeon 6975P-C | 7 | 0.95–1.01 (the reference) |
  | AMD EPYC 9V45 | 1 | 0.97 |
  | AMD EPYC 9V74 | 2 | 1.17, 1.18 |
  | Intel Xeon Platinum 8573C | 1 | 1.35 |
  | **AMD EPYC 7763** | 3 | **1.43, 1.50, 1.53** |

- **The cross-check.** `1305`'s PR run 45481212731 is not on `main`, so it is not in the weights. Its 7763 ran shard 1's
  items, not shard 4's, at 1.47 × that run's 6975P-C shards, and shard 4's items took 830 s on a 6975P-C there, against
  1218–1270 s on the 7763. So the CPU makes the shard slow, not the items it was given.

## 2. The factor `1.29` is below the slowest runner now measured

- `1.29` (`1202` FEEDBACK §3) was a shard's time against `1202`'s weights, which were the slower of two runs.
- On that same scale, the 7763 measured 1.33–1.40 here, and the 6975P-C 0.89–0.94. On this re-weigh's scale, `1.29` is
  about 1.29 ÷ 0.92 ≈ 1.41.
- **So a plan judged at 1.29 meets 20 minutes only on a faster runner than one already seen.** That is the adversarial
  pass's third attack (WO §5). This plan is judged at the slowest factor measured, **1.53**, with 1.29 and 1.41 beside
  it.

## 3. The plan: six shards, predicted

- `tools/tviburi-route plan 6 k` on the committed `tests/ci/times.tsv`, for k = 1..6.
- A shard job is predicted as 53 s of setup, the worst seen and not scaled (it was 48–52 s on the 7763), plus its items
  times the factor.

| Shard | Cases | Mutants | Items (s, at 1.00) | Job at 1.00 (min) | at 1.29 | at 1.41 | **at 1.53** |
|---|---|---|---|---|---|---|---|
| 1 | 52 | 58 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |
| 2 | 47 | 58 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |
| 3 | 51 | 57 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |
| 4 | 45 | 62 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |
| 5 | 49 | 60 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |
| 6 | 47 | 60 | 677 | 12.2 | 15.4 | 16.8 | **18.1** |

- **Total:** 646 items, 4,061 s (67.7 min at 1.00).
- **WO (2): met.** The longest shard is 15.4 min at 1.29, and 18.1 min at 1.53. Both are under 20.
- **`ci`'s wall time** is the longest shard plus 1.7–2.0 min (route before it, the aggregate after it; §1):

  | Plan | Longest shard job | `ci` wall, predicted |
  |---|---|---|
  | six, every shard on a 6975P-C | 12.2 | 13.9–14.2 |
  | six, the slowest shard at 1.29 | 15.4 | 17.1–17.4 |
  | **six, a shard on a 7763 (1.53)** | **18.1** | **19.9–20.1** |
  | seven, a shard on a 7763 | 15.7 | 17.4–17.7 |

- **The margin.** At 1.53 the wall has none. The 7763 took 4 of the 20 shard jobs seen on these four runs, so most runs
  of six will meet one. That is a finding for the architect (FEEDBACK), not a stop: the stop condition and (2) are stated
  on the shard, and the shard meets 20 at 1.53.

## 4. The ruling, and the plan built: seven shards

- **The architect, 2026-11-25T00:00Z:** with six shards the target would be met by luck. GitHub's minutes cost nothing on
  a public repository, so build seven, plan against 1.53, and name 1.29 as superseded.
- `tools/tviburi-route plan 7 k` on the same `tests/ci/times.tsv`, predicted as in §3: 53 s of setup, unscaled, plus
  the items times the factor.

| Shard | Cases | Mutants | Items (s, at 1.00) | Job at 1.00 (min) | at 1.29 | **at 1.53** |
|---|---|---|---|---|---|---|
| 1 | 41 | 50 | 580 | 10.6 | 13.4 | **15.7** |
| 2 | 43 | 53 | 580 | 10.6 | 13.4 | **15.7** |
| 3 | 39 | 52 | 580 | 10.6 | 13.4 | **15.7** |
| 4 | 39 | 52 | 580 | 10.6 | 13.4 | **15.7** |
| 5 | 42 | 49 | 580 | 10.6 | 13.4 | **15.7** |
| 6 | 44 | 49 | 580 | 10.6 | 13.4 | **15.7** |
| 7 | 43 | 50 | 580 | 10.6 | 13.4 | **15.7** |

- **Total:** 646 items, 4,061 s.
- **`ci`'s wall, predicted:**
  - 12.3–12.6 min with every shard on a 6975P-C;
  - **17.4–17.7 min with a shard on a 7763 (1.53)**, which leaves 2.3–2.6 min of margin.
