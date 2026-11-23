# TVI-WO-1301 — The GPIO guard: alternate-function routing checked against DS12117's own table

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1301-gpio-af-guard`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a full adversarial pass.
**Phase:** 1, the second `13NN`. **Cadence:** a measurement and a transcription, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 2, 5, 8; §6 ("so slow or heavy").
- **Architecture:** `RUL-04` and `RUL-13` as amended (GPIO alternate-function routing is second, "by a guard built from DS12117's own alternate-function table"); `RUL-07`, `RUL-09` (b), `RUL-16`, `RUL-17`, `RUL-18`, `RUL-19`.
- **Roadmap:** Phase 1, the `13NN` row; gate clauses 1 and 6.
- **Evidence:** the `GPIO*` rows of `spike/1150/data/queue-ap.tsv` (ArduPilot) and `spike/1100/data/writes.tsv` (the generated firmware), with their stock source lines (`STM32_GPIOPort.cs`); `TVI-WO-1300`'s wrapper and restatement (`model/stm32h7-rcc-restate.{awk,repl}`) as the pattern.
- **Held documents** on Furnace, read by page: DS12117 Rev 10 (`~/.cache/aet-held/ds12117-rev10.pdf`, sha256 `607b9ead…`) for the alternate-function table; RM0433 Rev 9 (`rm0433-rev9.pdf`, `672a9433…`) for the GPIO registers.

> **What this is:** the guard on the path that connects a peripheral to copper. A wrong alternate-function number passes in stock Renode, which routes nothing by AF, and on silicon the signal reaches a different peripheral or none. Aethelgard's AF numbers come from ST's open pin data; this guard's come from DS12117's table, transcribed independently, so two transcriptions must agree before a run passes (`RUL-13`'s reason).
> - **(1) The table** (`RUL-16`). Transcribe DS12117 Rev 10's alternate-function tables for the STM32H743 into data keyed by port, pin and AF number, each row citing its table and page. A blank cell is data too: no function. Never read Aethelgard's pin data, ArduPilot's, or Renode's while transcribing. **After** the table is committed, a test compares it with ST's open pin data as Aethelgard holds it (read-only, from a named Aethelgard commit): every disagreement is a finding with both sources, never silently resolved. Read whether Rev 10 carries the `GPIOx_AFRH` offset correction ST's errata names, and record what the page says.
> - **(2) The guard** (`RUL-07`). A wrapper per port, or one class for all ports, owning the stock `STM32_GPIOPort` the way `1300`'s guard owns the RCC, with the ports' registrations restated (`RUL-09` (b)), generated from the pinned `stm32h7.repl`. Measure first whether the stock GPIO model is sealed, and which form the class map allows.
> - **(3) The rules:**
>   - **`predicted-fault`:** a pin in alternate-function mode (`MODER` = 10) whose `AFSEL` names an AF with no function on that pin in the table. The record names the port, pin, AF number, and the table and page.
>   - **The query** "which signal does pin P carry now", answered from `MODER`, `AFSEL` and the table, for later guards to ask at their own start (as `1300a` wires the clock query). This work order builds the query and tests it directly; wiring it into SPI1's start is a later row.
>   - **`unmodeled`:** every field value the allow-list does not list (`RUL-17`), measured from both firmwares: `MODER`, `OTYPER`, `OSPEEDR`, `PUPDR`, `AFRL`/`AFRH`, `BSRR`, `ODR`, `LCKR`. `LCKR`'s key sequence and lock are modeled or refused, never passed silently.
>   - **Package:** a pin the platform's package does not bond is refused if configured. The package comes from the platform description, never from a variable or a path (`RUL-10`). If the platform names no package, stop.
> - **Each refusal comes with a planted firmware in `firmware/`, and each test with a mutant it kills.**
>
> It is NOT:
> - the peripherals' starts asking which signal their pins carry (a later row);
> - EXTI, the pins' electrical behaviour, or analog mode beyond the allow-list;
> - any change to Aethelgard.

## 1. Build

1. **Measure and transcribe first.**
   - Every GPIO field either firmware, or the consumer's cold writes, gives a value, with its page, from the evidence and a fresh recorded run of ArduPilot's full boot and the generated firmware.
   - Whether the stock GPIO model is sealed; the restatement's shape; the registrations to restate.
   - **The one-file overlay's memory** with a GPIO guard prototype on ArduPilot's full boot. `1300` left 66–91 MiB under 1,048,576 KiB; say what this costs.
   - The DS12117 table, committed in `docs/work-orders/TVI-WO-1301.ds12117.md` (or as data with that document citing it), then the comparison with ST's open pin data.
   **Stop** with the field table, the fields to acknowledge, the memory, and every disagreement between the two transcriptions.
2. **Build (2)–(3)** to the ruling.
3. **Merge `main` before READY.**

## 2. Acceptance

- **ArduPilot's full 5 s boot, recorded, with the guard:** no `would-refuse` under any GPIO port outside the acknowledged table; the one-file peak under 1,048,576 KiB, with the runs and the margin.
- **The generated firmware's GPIO sequences pass,** enforcing.
- **A planted firmware** that selects an AF with no function on its pin refuses `predicted-fault`, naming the pin, the AF and the page; the same firmware with the table's AF passes.
- **Every field outside the allow-list** refuses `unmodeled` (gate clause 6).
- **The table and ST's open pin data agree,** or each disagreement is ruled by the architect.
- **CI is green on the PR.**

## 3. Fences

- **Fence:**
  - `model/`: the GPIO guard, its table and allow-list as data, the generated restatement, the board description's GPIO entries, and `model/bench-checks.tsv` (new rows);
  - `scripts/prelude.resc`: one `include` line;
  - `tools/tviburi-release/pins.tsv`: the GPIO class row (granted now, as `1300` needed the RCC's);
  - `tests/`, `firmware/` (planted firmware);
  - `docs/work-orders/TVI-WO-1301.*`.
- **Not:** `model/Refusal.cs` and the record grammar (a new field is a stop); the RCC and SPI guards (`1300a`'s); the steering documents, `docs/reviews/`, or Aethelgard (read-only, by a named commit).
- **Running beside you:** `TVI-WO-1300a`. You share `tests/spike.sh`, `model/bench-checks.tsv` and the board description: add, never rewrite, and merge `main` before READY.
- One PR, left unmerged. Write `~/.cache/tvi-1301/pr-title-body.md`.

## 5. Adversarial pass, full

- **A route the guard misses:** a sub-word write to `AFRH`, a `BSRR` write that changes `ODR`, `LCKR` written out of sequence, a pin set to AF mode before its `AFSEL` (and the reverse order).
- **A transcription error that passes:** a table row whose page does not say it; a blank cell read as AF0.
- **A path to the stock port that bypasses the guard:** a second address, a registration left on a cancelled instance, a monitor command.
- **A record under a port's name whose text the firmware controls.**

## 7. Flag-and-stop conditions

- **After §1.1,** with the measurement, the table, and the disagreements.
- **Either firmware's boot would refuse on a path DS12117 or RM0433 says is valid.** Stop with the order and the page.
- **The one-file peak exceeds 1,048,576 KiB** on ArduPilot's full boot. Stop: northstar §6. Name what the guard costs and what could shrink it.
- **The platform names no package.** Stop.
