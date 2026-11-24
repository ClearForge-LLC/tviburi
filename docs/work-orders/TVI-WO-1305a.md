# TVI-WO-1305a — The USART guard's three open questions, as ruled: a start with BRR below 16, overrun without the FIFO, and IDLE timed by the real kernel clock

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1305a-usart-followups`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of `TVI-WO-1305`. Written 2026-11-24. **Cadence:** a short measurement for (1), then the build. Stop only if a flag-and-stop condition fires.

**Grounds:**
- **`TVI-WO-1305`** (merged, #40): its FEEDBACK §6 asked three questions, Q2–Q4. The architect ruled them at merge, and the roadmap records the rulings (Phase 1 rows, "From `TVI-WO-1305`").
- **RM0433 Rev 9:**
  - p2035: "USARTDIV must be greater than or equal to 16";
  - p2030: with the FIFO disabled, a second byte received before `RDR` is read is lost (overrun);
  - `IDLE`, as `1305` cites it.
- `RUL-07`, `RUL-13`, `RUL-17`, `RUL-18`; northstar invariants 1, 5 and 8.

> **What this is:**
> - **(1) `BRR` below 16 at a start** (ruled Q2):
>   - **Measure first, predictions committed:** in ArduPilot's armed run (`1305`'s, ELF `dbf598d8…`), `usart2` starts with `BRR` = 0 for one write at 0.2017 s. Does any byte move, transmitted or received, while `BRR` < 16? Read it from the access log and the guard's records.
>   - **Then:** that measured pattern is acknowledged by name, with its record.
>   - **Any other** start (`UE` with `TE` or `RE`) with `BRR` < 16 is `unmodeled`.
>   - **Add bench row U2** (`model/bench-checks.tsv`): a Nucleo starts a USART with `BRR` = 0, then rewrites it, and the line is observed.
> - **(2) Overrun with `FIFOEN` = 0** (ruled Q3): a second byte arriving while `RDR` is unread, with the FIFO disabled, is `unmodeled`, with the page. `1305`'s acknowledgement covers FIFO mode only; that mode passes unchanged.
> - **(3) `IDLE`'s frame time from the RCC guard's kernel-clock query** (ruled Q4), as the start already asks it, not from the stock `frequency`.
>   - Show a case where the two differ: an HSI kernel clock against the stock 200 MHz, and `IDLE` at the right time.
>   - With no rule for the kernel clock, `IDLE`'s timing is `unmodeled`, never a default (invariant 6).
> - **Each rule comes with a planted firmware, and each test with a mutant it kills.**
>
> It is NOT:
> - a kernel clock lost under a running USART (a row for the RCC guard);
> - baud accuracy (Aethelgard judges it);
> - `LPUART1`, which stays stock;
> - any change to Aethelgard.

## 1. Build

1. **Commit predictions for (1)'s measurement, then measure.** Post the answer to the bus before building (1), but do not stop: build to the ruling either way.
2. **Build (1)–(3).** Then the 14 generated boots and ArduPilot's recorded boot: every write, `RDR` value and instruction count is identical to `main`, except where a rule here changes a record, and each such change is named.
3. **The armed run** (Furnace, ELF `dbf598d8…`, as `1305` ran it): ArduPilot still answers every command and arms.
4. **The budget** is judged in CI.

## 2. Acceptance

- (1): the measured `usart2` pattern passes, acknowledged by name. A planted start with `BRR` = 8 is `unmodeled`, with p2035.
- (2): a planted overrun with `FIFOEN` = 0 is `unmodeled`, with p2030. The same traffic with `FIFOEN` = 1 passes.
- (3): `IDLE` arrives one frame after the last byte at the kernel clock's baud, not the stock frequency's. A planted kernel-clock source with no rule gives `unmodeled`.
- Bench row U2 exists, and the ledger names it.
- Mutants for each rule are killed; CI is green.

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32F7_USART.cs` (and the RCC guard only through its existing query);
  - `model/bench-checks.tsv` (U2);
  - `tests/1305/` and `firmware/usart.rs`;
  - `tests/mutants.tsv` and `tests/ci/times.tsv` (your rows only);
  - `docs/work-orders/TVI-WO-1305a.*`.
- **Not:** the RCC guard's rules, any other guard, `tools/`, the steering documents, or Aethelgard.
- **Running beside you:** `TVI-WO-1202a` (the sixth shard) changes `tests/ci/`. Whichever merges second merges `main` and re-weighs.
- One PR, left unmerged. Write `~/.cache/tvi-1305a/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **An acknowledgement wider than the measurement:** any `BRR` < 16 start that passes but is not the measured pattern.
- **A default where there is none:** `IDLE` timed by a fallback frequency when the kernel clock is unknown.
- **The FIFO-mode path changed by the overrun rule.**

## 7. Flag-and-stop conditions

- **(1)'s measurement shows a byte moving under `BRR` < 16.** Stop: the acknowledgement would then cover traffic, and that needs a ruling.
- **The armed run no longer arms.** Stop with the first refusal.
