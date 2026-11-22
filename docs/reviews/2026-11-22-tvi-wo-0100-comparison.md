# TVI-WO-0100 built twice: a blind comparison

**Written by:** Claude, Aethelgard's co-architect, on 2026-11-22, at Scotty's request.
**Files:**
- [`2026-11-22-tvi-wo-0100-rubric.md`](2026-11-22-tvi-wo-0100-rubric.md): the rubric, verbatim.
- [`2026-11-22-tvi-wo-0100-verdict.md`](2026-11-22-tvi-wo-0100-verdict.md): the judge's verdict, verbatim (sha256 `c1090623…0aef6e34`). Every score cites a file and line, and every run is reproduced there.
- [`2026-11-22-pr2-review.md`](2026-11-22-pr2-review.md): my review of PR #2, written before the comparison.

## 1. What was compared

The same work order, `TVI-WO-0100` at `main` `663e2b3`, was built twice:

| Label in the verdict | Builder | Where it is |
|---|---|---|
| **X** | A Claude builder started fresh on Furnace, told not to read PR #2 or its branch | Branch `wo/tvi-0100-renode-fork-refusal`, head `e47e8d6` (the verdict names `7c8ffd9`, the last code commit; the two after it are FEEDBACK only). Local to Furnace. **Not pushed.** |
| **Y** | Grok's team, through Cursor | PR #2, head `2e73bb0` |

A third session, the judge, scored both against a fixed rubric. It saw only:
- `main`;
- each output's diff and tree, labelled X and Y;
- the rubric;
- the pinned tools image `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…`.

It ran both outputs in that image with no network.

## 2. Result

**X 37/40, Y 27/40.** The judge's recommendation is to merge X first, after seven fixes, and gives Y six fixes (verdict §5).

**The items that decided it** (verdict §2):

| # | Item | X | Y | Why |
|---|---|---|---|---|
| 7 | Control path sound | 2 | 0 | Y's "modeled succeeds" control is the same `SPE` write on SPI4's stock model, which Y's own FEEDBACK records as only tagged. The control certifies the scar as a pass (run Y4). |
| 12 | Harness reads its own run | 2 | 0 | Y's `--hide-log` check passes on a stale `/tmp` log from an earlier run (run Y3). |
| 6 | Silicon claim with preconditions | 2 | 1 | Y says a bare `SPE` write raises MODF; it states no precondition. |
| 15 | Hash binds what ran | 2 | 1 | Y's hash follows an environment variable, not the compiled shim (run Y5b). |
| 10 | Missing shim fails at the exit code | 2 | 1 | With Y's shim removed, raw Renode exits 0 (run Y6). |
| 2, 3, 9, 20 | Insertion point, path, support check, reduction | 2 | 1 | See the verdict. |

**Where Y was better:**
- **Item 13, no silent path:** X 1, Y 2. Y refuses every SPI1 access. X reproduces a false pass through IOLOCK (R8, run X4), and it widens SPI1 from a tag to the stock model's gaps.
- **The exit code:** Y's raw Renode exits 1 on a refusal; X's exits 0, and only X's runner returns 3 (R9). A consumer that runs Renode directly, as the README says Aethelgard does, would miss X's refusal. The judge names this X's likeliest integration hazard (verdict §4).
- **The README:** Y updated its status line; X did not.

## 3. Confounds: read these before the score

The judge's own list is verdict §6. I add mine, because two of them bear on me:

1. **I wrote the rubric after reviewing PR #2.** The rubric's line "fixed before either output was examined" is true of the judge, not of me. Items 6, 10, 12 and 15 test the exact defects my review had found in Y. X's builder never saw the rubric (no copy is in its tree or its scratch directory), but the rubric still leans toward catching Y's failure modes over X's. A rubric written cold might weigh item 13 and the exit code (both Y's strengths) more.
2. **Claude judged Claude.** X and the judge are both Claude sessions under the same co-architect skills, so they share habits of thought. The judge did not know which output was which, but authorship markers were visible in the PR footers (verdict §6), and X used Furnace conventions a Claude reader would recognize.
3. **The environments differed.** X ran in the pinned, patched image. Y was written against a host Renode install with a GUI session. The judge ran both in the image; Y on its own host may behave differently.
4. **No one checked RM0433.** X's 2s on items 6 and 7 rest on X's own truth table, which X labels as from memory. If that table is wrong, those two items fall. Invariant 2 makes this a ruling for Tviburi's architect.
5. **Entry point.** Judged at raw Renode for both, X loses up to 4 points (to 33). The order does not change.

**My reading:** the gap is real, but narrower than 37–27 suggests. Y's two zeros (7 and 12) are claims the proof makes and cannot back, which is the failure Tviburi's northstar exists to prevent. X's main defect (R8) is a disclosed silent path and an exit code that hides the refusal, which is the same failure one layer out. Neither is mergeable as it stands.

## 4. Decisions this leaves open

These are for Scotty and Tviburi's architect. This file changes nothing.
- **Which output to build on.** If X, it must be pushed first: it exists only on Furnace.
- **R9:** must raw Renode exit non-zero on a refusal? Both outputs and the judge point the same way: give refusals their own exit code at the invocation a consumer runs.
- **R8 (IOLOCK):** refuse it, or accept it as a recorded gap.
- **RM0433:** check the MODF truth table against the manual before any condition text claims silicon behaviour.
- **The rubric:** whether to reuse it for later Tviburi comparisons, with item 13 and consumer-visible exit semantics weighted up.
