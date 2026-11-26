# TVI-WO-1300c — The kernel clock's frequency in one place: the USART and ADC guards ask the RCC guard's `KernelHz`, and its rules are made whole

**Repo:** `ClearForge-LLC/tviburi` · **Base:** `main`, named in the kickoff.
**Branch:** `wo/tvi-1300c-kernel-hz`.
**Author:** Claude (co-architect) · **Builder:** strong tier, with a targeted adversarial pass.
**Phase:** 1, a refinement of `1300b` and a reduction. Written 2026-11-26. **Cadence:** a measurement, a stop, then the build.

**Grounds:**
- **Northstar** invariants 1, 5 and 6.
- **Architecture:** `RUL-07`, `RUL-17`, `RUL-18`.
- **`TVI-WO-1306a`** (#55) gave the RCC guard one read-only `KernelHz` (ruled 2026-11-26T00:22Z Q2) and left three copies of the PLL arithmetic.
  - The USART guard's `KernelHz`/`Vco` (`1305a`).
  - The ADC guard's `Vco` (`1307`).
  - The RCC guard's `KernelHz`/`PllHz` (`1306a`).
- **The three differ today, by reading the code at `ed70138`:**
  - **The USART guard's copy:**
    - integer Hz;
    - `RCC_PLLnDIVR` from the stock model's read-back, refused as "no rule" once a write did not read back as written (`pllUntrusted`);
    - no CSI;
    - `sys_ck` only from the HSI or PLL1.
  - **The ADC guard's copy:** double, the same read-back with `pllUntrusted`, and PLL3.
  - **The RCC guard's `KernelHz`:**
    - double;
    - its own `divr` copy, with no `pllUntrusted`;
    - the CSI, the HSE as `sys_ck`, and `per_ck`.
  - **The consequence:** the same register state can give three answers.
- **`1306a` FEEDBACK §6:**
  - **F4:** the RCC ledger rows that now feed `KernelHz` still say "no PLL frequency is modeled beyond PLL1's integer one" and "no side effect".
  - **F5:** `divr` against the truncated read-back.
  - **F6:** `PLLxRGE`, the VCO choice and `DIVP1`'s "not allowed" values are unchecked.
- **The roadmap's rows:**
  - "The PLL/VCO and `sys_ck` derivation now lives in the USART and ADC guards" (`1307`);
  - `PLL2DIVR`/`PLL3DIVR` lose each field's top bit silently (`1300a`);
  - the four `LPEN` rows' ledger `detail` (`1300b` §6).

> **What this is:**
> - **(1) Measure first, with predictions committed before the runs:**
>   - the three copies side by side: each input each reads (guard copy or stock read-back), its unit, and each reason it answers none;
>   - for every committed case, the 14 generated boots, ArduPilot's fixture and the armed run: the frequency each copy gives at each use (a USART start and each byte's time, an ADC start and its pacing, each I2C NACK), and every disagreement, by case;
>   - what RM0433 says of `PLLxRGE` (p400–402), `PLLxVCOSEL` against the reference (p346), and `DIVP1`'s values (p404), and whether any committed firmware sets them otherwise;
>   - the memory and each shard's projection at 1.53.
>   - **Stop** with the table.
> - **(2) One home.** The RCC guard's `KernelHz` is the only frequency rule, and the USART and ADC guards call it. Their private copies are deleted.
>   - **The divider:** silicon's `RCC_PLLnDIVR`, from the write the RCC guard passed. Where that write's value cannot be known as silicon holds it, the answer is "no rule" (`pllUntrusted`'s rule, kept once).
>   - **The unit:** whatever keeps every committed case's computed time unchanged. If double and integer Hz disagree on any committed path, stop with the case.
>   - **Each source** answers as the union of today's three, and a source one copy refused and another computed is named in (1)'s table.
> - **(3) The rules made whole (F6).** A `PLLxRGE`, a VCO choice or a `DIVP1` value that RM0433 does not allow gives "no rule", with the page. It is never a frequency. If (1) finds a committed firmware that sets one, stop.
> - **(4) The ledger made true (F4).** Restate every RCC row that now feeds `KernelHz`, and the four `LPEN` rows' `detail` (`1300b` §6), in `fields.py`'s words, each with its page.
> - **Each rule comes with a planted firmware, and each test with a mutant it kills.** The reduction pass is part of the build, and the diff is net negative in the guards' code.
>
> It is NOT:
> - the I2C guard's behaviour (its cases pass unchanged);
> - a new clock source (`rcc_pclk3`, `rcc_pclk4`, `pll3_q_ck` for the USART, the LSE): each stays "no rule" unless (1) finds a committed firmware on it;
> - `FRACN` (the allow-list keeps it 0);
> - CSleep;
> - `model/Refusal.cs`;
> - any change to Aethelgard.

## 1. Build

1. **Measure first** (1). **Stop** with:
   - the three-copy table;
   - every disagreement, by case;
   - the pages for (3);
   - the memory and the shard projection.
2. **Build (2)–(4)** to the ruling. Merge main before READY.

## 2. Acceptance

- **Every committed case passes unchanged:** `1300b`'s, `1305`'s, `1305a`'s, `1306`'s, `1306a`'s and `1307`'s. Any change that (1) named must be in the ruling.
- **The 14 generated boots, the fixture and the armed run:**
  - boot blocks are byte-identical;
  - there are no new records;
  - the armed run arms.
- **A planted firmware** for each source that a copy answered differently, and for each (3) rule, reads the one answer.
- **No guard but the RCC guard** reads an `RCC_PLL*` register or holds a PLL rule. A test proves it, for example by a grep over `model/`.
- **The budget:** within `1201`'s 32 MiB, judged in CI.
- **CI is green.** Every new case and mutant runs in a shard.

## 3. Fences

- **Fence:**
  - `model/GuardedSTM32H7_RCC.cs`, `model/GuardedSTM32F7_USART.cs` and `model/GuardedSTM32H7_ADC.cs`, their data and allow-lists;
  - `tests/1300/`, `tests/1305/`, `tests/1307/`;
  - planted `firmware/`;
  - `tests/ci/citations.tsv`, `tests/mutants.tsv` and `tests/ci/times.tsv`: your own rows;
  - `docs/work-orders/TVI-WO-1300c.*`.
- **Not:**
  - `model/GuardedSTM32F7_I2C.cs` (it already calls `KernelHz`);
  - `model/Refusal.cs` and the record grammar;
  - every other guard;
  - the steering documents;
  - Aethelgard.
- **Mutants:** run `tests/mutants.sh` for your rows exactly as CI's shard does before you push.
- One PR, left unmerged. Write `~/.cache/tvi-1300c/pr-title-body.md`.

## 5. Adversarial pass, targeted

- **A frequency where silicon has none:** a divider the stock model truncated, a VCO outside its range, a reference outside `PLLxRGE`. Each must answer "no rule", not a number.
- **A changed answer hidden by a passing case:** a source where two copies disagreed and no case reaches it.
- **A guard that still holds a copy:** a constant or a register offset of the PLL left behind in the USART or ADC guard.

## 7. Flag-and-stop conditions

- **After §1,** with the measurement.
- **A committed case's computed time would change,** or either firmware's boot or the armed run would refuse. Stop with the case and the page.
- **The budget fails in CI, or the shard projection passes 20 minutes.**
