# TVI-WO-1300a — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1300a-clock-reaches-peripheral`, from `main` `604c3d6`.

**State:**
- **§1.1 measured and stopped** (`TVI-WO-1300a.measure.md`; bus 17:03Z).
- **The architect's rulings followed:** 17:16Z (the §7 stop: build on, with the nine `per_ck` boards expected to refuse; Q1–Q4; the flake) and 17:52Z (ADC). Each ruling's build is below.
- **No §7 stop fired in the build:**
  - ArduPilot's boot makes 0 `would-refuse`;
  - the generated boots refuse only where ruled;
  - the peak is under the bound;
  - no record field was needed.
- `model/Refusal.cs`, the record grammar, the steering documents, `docs/reviews/`, `spike/`, `scripts/` and Aethelgard are untouched. One PR, left unmerged.

---

## 1. What was built, by the work order's items

**(1) Enables off at reset** (p373).
- `GuardedSTM32H7_RCC` writes its nine `*ENR` copies, 0 at reset, into the owned stock RCC after each registration and at reset (`SyncEnables`).
- The stock write callbacks lock every registered peripheral whose enable is 0 (measured: a write is dropped and a read returns 0, for CPU, monitor and DMA alike, on every bus). The bus's lock survives `machine Reset`, which is why it is re-synced there.
- **Guarded peripherals stay unlocked on the bus** (`UnlockGuarded`, also after every `*ENR` write). Their own guard refuses instead: `TVI-SPI-ENABLE`, `predicted-fault`, naming the enable bit and p373, at every access with the enable at 0, reads included (ruled 17:16Z, SPI1 only).
- **The callers fixed:**
  - `tests/conditions.resc`: PLL1 and `SPI1EN` after each reset;
  - the `recorded` monitor test;
  - `1200`'s SPI sweep: `tools/tviburi-release cases` takes an optional enable, written first and after each reset;
  - `firmware/spi1.rs`: `PLL1ON` before `SPI1EN` in every variant, and `USART3EN` for `forge`.

**(2) SPI1's start asks.**
- At `SPE` rising, or `CSTART` rising with `SPE`, the SPI guard asks the RCC guard's query: `TVI-SPI-KERNEL` at `spi1`, `predicted-fault`, naming the selector, its source and the pages (p371–372, p414, p2159).
- The RCC guard is found by its platform name, `sysbus.rcc`, and the instance's own name keys the query, both at each use.
- With no RCC guard, or a name the query does not cover, the rule is `unmodeled`.
- `RequireKernelClock` (`TVI-RCC-KERNEL`) stays as the monitor-facing question for peripherals with no guard yet (the USART2 case; case `kernelq` keeps bench rows R1 and R2 produced).

**(3) A clock lost while SPI1 runs.**
- `TVI-RCC-CLOCK`, `predicted-fault` (p371; p376 for `SPI1EN`): a passed RCC write after which a guarded SPI with `SPE` = 1 loses the kernel clock it had. The query is evaluated on the state the write leaves: the copies take it, `RCC_CR` keeps what the stock model keeps, and the muxes settle.
- It sits after `TVI-RCC-FIELD`, so (5)'s PLL1-source clear stays `TVI-RCC-PLL-REF`, `unmodeled`.
- **The blocks are modeled as blocks** (ruled Q2):
  - oscillator and `PLL1ON` clears on the system clock's path are kept by the stock model and lose nothing (case `blocked`);
  - `TVI-RCC-PLL` keeps `DIVxyEN`, `PLLSRC` and `DIVMx`, and now also `RCC_PLLxDIVR` (p403–410, Q4).

**(4)** A `CFG2` write of the value `CFG2` holds, with `SPE` = 1, passes (p2221). A different value still refuses (`conditions` 8 and 8a; case `cfg2same`).

**(5)** Bench row R10: which of p384–386 and p340–342 holds for an HSI clear under PLL1 when PLL1 is not the system clock. `TVI-RCC-PLL-REF` stays `unmodeled` (case `pll1src`).

**(6) Reproducible.**
- **The allow-list.** The generator's two input tables are committed under `tests/1300/data/` (RM0433's field list and the stock kinds, 1,453 lines, byte-identical to `1300`'s scratch). `allowlist.py check` regenerates the guard's `Fields` and `tests/1300/fields.resc` byte for byte, and `spike.sh allowlist` runs it. Two ruled changes went through it:
  - `SPI123SEL` 101–111 refuse (Q4);
  - the ADC enables' reason names `ADCx_CCR.CKMODE` and p920 (17:52Z).
- **The harness:**
  - `replay-1300.sh` keeps `pure` and `accept`;
  - the prototype-only `trace`/`cost`/`cost-spi` modes and their files (`RccProto1300.cs`, `Records1300.cs`, `onefile.sh`, `smoke.resc`, `order.py`, `mdtable.py`) are deleted;
  - `tests/1300/boots.sh` `capture`s at the Aethelgard commit `boots.tsv` names, `replay`s on the guards, and `check`s each board against `tests/1300/boots.tsv`.
- **(7)** 0 losses in 354 two-end runs: under about 0.85% per run. Closed as ruled; it still fails closed (`measure.md` §1.4).

**Planted firmware** (`firmware/rcc.rs`), one for each refusal and for each valid order the rules must pass:
- refusals: `unenabled`, `lost-pll`, `lost-sel`, `lost-en`, `lost-osc`, `lost-ckper`, `sel5`, `divr`;
- passes: `stopped`, `ckper-live`, `blocked`, `cfg2same`;
- `gen-s03` now starts SPI1 as the generated firmware does, and refuses at SPI1; `gen-hc16` starts and passes.

**Bench rows,** added only: B5–B7 (SPI) and R4–R10 (RCC).

## 2. Acceptance (WO §2)

| Clause | Where shown | Result |
|---|---|---|
| An access to an unenabled peripheral refuses `predicted-fault`, naming the enable bit; with the enable it passes | `rcc-unenabled`, `rcc-clocked`, `rcc-spi1off`, `rcc-dma`, `conditions` 32 | as stated (SPI1). Unguarded peripherals lock as silicon's write (`rcc-tim2`) |
| SPI1 started with no kernel clock refuses at SPI1, naming the selector, the source and the pages; with a running source it passes | `rcc-scar`, `-switch`, `-stuck`, `-divq`, `-gen-s03`, `conditions` 33–34; `rcc-clocked`, `-switched`, `-gen-hc16` | as stated |
| A source turned off under a running SPI1 refuses at the RCC write, or is blocked | `rcc-lost-*` (5), `rcc-lost-alias`; `rcc-blocked`, `rcc-stopped`, `rcc-ckper-live` | as stated |
| A same-value `CFG2` write with `SPE` = 1 passes; a different value refuses | `conditions` 8/8a, `rcc-cfg2same` | as stated |
| The allow-list regenerates from a clean checkout, byte for byte | `allowlist` | as stated |
| ArduPilot's boot and the generated firmware pass as `1300` measured, except where these rules predict a fault (a stop) | `tests/1300/boots.sh` on `1300`'s captures (Aethelgard `75da8053`) | ArduPilot and five boards: 0 records. The nine `per_ck` boards: `TVI-SPI-KERNEL` at SPI1 (SPI2 on two), as ruled 17:16Z. `d1-drv8876`: its `ADC3EN` refusal, `unmodeled`, as ruled. Peaks: ArduPilot 921,408–977,304 KiB; boards 504,788–967,372 KiB; bound 1,048,576 |
| CI green | the PR | see the PR |

**Suites on HEAD:** `TVI-WO-1300a`'s run of `tests/spike.sh` (`all`): 130 ok, 0 FAIL. **Mutants** (diff-sized: C24–C32, A01, S01–S05, F03): 16/16 killed, each by the test its row names.

## 3. Findings and rows for the architect

1. **Rows for later guards** (ruled Q1: not general):
   - **DMA:** ArduPilot writes DMA1/DMA2 `S0CR`–`S7CR`, `LIFCR` and `HIFCR` at 0.92 ms, before `DMA1EN`/`DMA2EN`. Silicon ignores them; they are reset values.
   - **I2C:** ArduPilot reads, then writes 0 to, I2C1 `CR1` before `I2C1EN`, in program order (pc `0x08100B02` is the enable; the access precedes it in the CPU's log order).
   - Each later guard decides its enable rule from its own ArduPilot measurement.
2. **An ADC guard** (17:52Z): it reads `ADCx_CCR.CKMODE` and asks the query for `ADCSEL` only when `CKMODE` = 00 (p920). Until it exists, `ADC12EN`/`ADC3EN` refuse `unmodeled`, naming `CKMODE`. That premise needed a correction, and the bus has it (17:50Z): the `d1-drv8876` refusal was already `unmodeled` (`TVI-RCC-FIELD`, 1300's F8), never a `predicted-fault`. What changed is its reason.
3. **Rows, not here** (ruled):
   - `rtc` locks only through `RCC_BDCR.RTCEN`, which the stock model reads as 1 at reset (silicon: 0, p427), and the guard keeps no `BDCR` copy;
   - witness and end records log at INFO against `RUL-12`'s "Error level" (the architect records it for the records owner).
4. **Also measured:**
   - `CKPERSEL` = 11 ("per_ck clock is disabled", p412) still passes the allow-list. The query answers "disabled", so a start on it refuses at SPI1. Refusing the write itself, as `SPI123SEL` 101–111 now does, was not ruled.
   - A `CFG2` write of the same value under the `IOLOCK` latch still refuses. Only `SPE`'s write protection was ruled here.
5. **The sweep generator's enable** (`tools/`, granted as "the SPI sweep generator's enable"): an optional `<enable address> <enable value>` pair. No kernel-clock line was needed, because the sweep's one start case (`CR1.SPE`) is answered first by `TVI-SPI-MODF`.
6. **spi2.** The consumer's platform types `spi2` as the same guard class. The SPI rules key on the instance's own name, so `spi2` asks for `SPI2EN` and its own selector. ArduPilot's SPI2 meets none of them (0 records, 0 locked accesses).
7. **The harness's one network access.** Aethelgard's crates are not in the tools image, so `boots.sh capture` runs `cargo fetch --locked` on the host into its output directory. Every container stays `--network none`. Capturing also builds Aethelgard's `bridge/` crate, because `verify`'s simulate gate needs `aethelgard-bridge` beside the CLI. `1300`'s own build of these was not recorded.

## 4. Adversarial pass (WO §5)

**Done twice:** by me, as cases, and by a fresh subagent given the code and the work order, not my conclusions (Opus, read-only). I checked every finding against the code before acting.

**By §5's four hunts, with the cases that answer each:**
- **A peripheral that still works unenabled:**
  - the alias (`rcc-alias`, `rcc-lost-alias`);
  - DMA (`rcc-dma`: the DMA's write reaches SPI1's guard, which refuses);
  - sub-word accesses (the bus locks every width; the SPI guard refuses them by `TVI-SPI-WIDTH`);
  - a reset (`rcc-tim2`: locked again after `machine Reset`).
  - **Found, fixed (review F1):** a guarded SPI declared on the bus but not registered on the RCC guard was answered by name, yet never watched for a clock loss. It now refuses every access, `unmodeled` (`Holds`; case `rcc-unheld`; mutant S06).
- **A start that skips the question:**
  - `CSTART` without `SPE`, and `SPE` with `CSTART` in one write, refuse `TVI-SPI-ORDER` (`conditions` 27);
  - `CSTART` with `SPE` is a start (`rcc-cstart`, mutant S03);
  - both lookups run at every use, and a reset leaves the instances in place (`conditions` 33–34 start after a reset).
- **A clock lost by a path (3) misses:**
  - `CKPERSEL`, `PLL1ON`, `SPI1EN` (also through the alias), an oscillator under `per_ck` and a selector all refuse (`rcc-lost-*`);
  - `DIVQ1EN` and `PLLSRC` are blocked (`TVI-RCC-PLL`).
  - **Found, fixed (review F2):** the trial's restore after a refused loss was untested. Now `rcc-relost-sel` and `rcc-relost-pll` refuse a second loss after the first; mutants C33 and C34.
- **A false refusal on a valid boot order:**
  - enable, then source, then start, and a source changed while stopped, pass (`rcc-clocked`, `rcc-stopped`, `rcc-ckper-live`);
  - ArduPilot's boot and every generated boot record only what is ruled (§2).
  - **Found, fixed (review F3):** `TVI-RCC-PLL`'s new `PLLxDIVR` check compared a write only with the last value written. The stock model drops the top bit of each PLL2/PLL3 divider field, so a read-modify-write while the PLL runs would have refused. A change must now differ from both the value written and the value read back (`rcc-divr2rmw`; mutant C35).

**Findings for the architect, not fixed here:**
1. **(review F3, pre-existing: 1300's data) `RCC_PLL2DIVR`/`RCC_PLL3DIVR` lose a bit silently.** The stock fields are one bit narrower than RM0433's (`DIVN2` 8:0 is stored as 7:0, and likewise P, Q, R; stock L372–388, `tests/1300/data/stock/fields.tsv` L102–109). The allow-list classes them "stored and read back" because `allowlist.py`'s `skind` ignores the gap. `PLL2DIVR = 0x0101018F` reads `0x0101008F`, with no record. ArduPilot's values set none of those bits. A row, or a generator rule for partly stored fields.
2. (review N2) A same-value `CFG2` write under the `IOLOCK` latch still refuses (§3).
3. (review N3) A 16-bit `CR1` write that sets `SPE` is refused by `TVI-SPI-WIDTH` (so the run fails closed), but `Started` never sees it in recorded mode.
4. (review N4, fixed) `TVI-RCC-CLOCK` cited p371's mux sentence for every loss. It now does so only when a mux waits; otherwise it cites the source's own page (p383, p384, Table 62 p376).

## 5. Process

- **Measure first, then a stop** (17:03Z), then the build after 17:16Z. The possible second stop (ArduPilot under (3)) was measured first, on a scratch prototype with a positive control, and did not fire (bus 17:21Z).
- **One container of mine at a time,** each under `~/bin/capped-run`, `--network none`, `nice -n 10`. `podman ps` was checked by mounts after the heavy runs.
- **Subagents:**
  - one for the RM0433 reading (§1.1 M3; I re-checked every quote by line);
  - one for the allow-list regeneration (I re-ran its check and one mutation myself);
  - one for an independent adversarial review (§4).
- **Tokens:** one mint for the §1.1 push (`tviburi` only, revoked HTTP 204). The PR's mint is recorded on the bus.

## 6. Close

(reduction pass, filled at close)
