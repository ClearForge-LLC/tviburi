# TVI-WO-1300a — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1300a-clock-reaches-peripheral`, from `main` `604c3d6`.
**State:** §1.1 measured, then **stopped** (WO §1.1 and §7), with the questions in §3.
**Image:** `aethelgard-tools@sha256:1e0ab105…` (Aethelgard `main` `1c32c8ea` pins it), Renode `1.17.0+20260907gitf1dd1b4af.aet1605`.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf`. Its `STM32H7_RCC.cs` and `STM32H7_SPI.cs` match the sha256 that `1300` pinned (`7f10d491…`, `bc3f1348…`). `SystemBus.cs` is read from the same tree, but no hash pins it to the build. Every claim drawn from it is measured below as well.

## 0. Predictions, committed before any measurement

Written after reading the WO, `1300`'s FEEDBACK and handoff, the RCC and SPI guards, and the stock `STM32H7_RCC.cs` and `SystemBus.cs`, but before running anything.

**M1. What the stock model does for an access to a peripheral whose enable is 0, and how the guard can lock it.**
- P1.1. In the stock model, a lock is per peripheral, not per bus. `STM32H7_RCC`'s `*ENR` write callback calls `sysbus.SetPeripheralEnabled(peri, bit)` for a **registered** alias. Every access through `SystemBus` to a locked peripheral is dropped: a write is ignored, and a read returns 0. Each logs `Tried to write/read a locked peripheral` at WARNING under `sysbus`, which is not a record. So I predict the same behaviour on all eight buses (AHB1–4, APB1–4).
- P1.2. The stock write callback runs on **every** write to the field, not only on a change. So writing 0 into the owned stock RCC locks the peripheral even though the stock bit reads 1.
- P1.3. A peripheral that the platform declares but that is not one of the 59 registrations (58 restated, plus `spi1`) cannot be locked by this mechanism. I predict at least `spi2`, `spi3`, `pwr`, `exti`, `flash` and the SRAMs. `rtc` is locked by `RCC_BDCR.RTCEN` (stock L494), not by `RCC_APB4ENR.RTCAPBEN`.
- P1.4. A monitor `sysbus WriteDoubleWord` and a DMA transfer reach a locked peripheral through the same `SystemBus` path, so both are dropped too.
- P1.5. `machine Reset` does not clear the bus's locked set (`SystemBus.Reset` does not touch `lockedPeripherals`). Today, after a reset, the guard's copies are 0 while the bus keeps whatever the firmware left.
- P1.6. A locked **guarded** peripheral (`spi1`) never reaches its guard, so it emits no record: the access is silently dropped. A refusal that names `SPI1EN` therefore needs one of two designs:
  - (a) the RCC guard leaves its guarded peripherals unlocked on the bus, and each guarded peripheral asks the RCC guard for its enable at every access;
  - (b) a bus-level observer, which `RUL-07` rules out as a guard.
- P1.7. The way to lock is `1300`'s proposal: after each `Register()` and in `Reset()`, write each `*ENR` copy through the owned stock RCC. It locks every registered alias except `rtc`, which waits on `BDCR`.

**M2. Every Tviburi test, script and generator that drives a peripheral it never enabled.**
- P2.1. **Monitor scripts with no `SPI1EN`:** `tests/conditions.resc` (every case), `t_recorded`'s script in `tests/spike.sh`, and `1200`'s generated SPI sweep (`tools/tviburi-release`). With the lock, each one's `spi1` accesses are dropped and its assertions fail.
- P2.2. **Firmware:**
  - `firmware/spi1.rs` enables `SPI1EN` before any SPI1 access, in every variant.
  - **`path="forge"` writes USART3 with `USART3EN` never set.** With the lock, its `TXE` poll reads 0 forever, so the run never reaches its marker and the `forge` tests fail. That would be a vacuous pass of the forging test, so it is a test to fix, not a guard defect.
  - `firmware/rcc.rs` enables each peripheral it touches.
- P2.3. `tests/1300/` monitor cases that touch `spi1` (`reset`, `spi1rst`, `registrations`) change behaviour. The adversarial cases that drive `spi1` from the monitor (`adv-hook`, `adv-spray`) need `SPI1EN` too.
- P2.4. The consumer's runs are unchanged. Its cold writes zero every `*ENR` first (`1300`: 15/15 boots), and the firmware then enables what it uses.

**M3. For (3): each source, output and selector change that RM0433 blocks in hardware, against those it allows.**
- P3.1. **Blocked ("cannot be cleared", p384–386):**
  - `HSION`, `CSION` and `HSEON` while that oscillator is the system clock, directly or through PLL1 as the system clock;
  - `PLL1ON` while PLL1 is the system clock (p383).
  - The stock model already reverts these (`KeptCr`).
- P3.2. **Blocked ("can be written only when … disabled"):**
  - `DIVxyEN` while PLLx is on (p400–401);
  - `PLLSRC` while any PLL is on, and `DIVMx` while PLLx is on (p398–399).
  - `1300` refuses each of these today as `TVI-RCC-PLL` `predicted-fault`.
- P3.3. **Allowed, so they need (3)'s refusal at the RCC write:**
  - clearing a source oscillator that is not the system clock's path (for example `HSION` under `CKPERSEL` = `hsi_ker_ck`, or `HSEON` feeding PLL2 or PLL3);
  - `PLL2ON`/`PLL3ON` cleared, and `PLL1ON` cleared when PLL1 is not the system clock;
  - any kernel-clock selector change, including `CKPERSEL` (p371: the mux waits for both clocks, and no hardware lock is stated);
  - the peripheral's own `PERxEN` cleared while it runs (Table 62, p376).
- P3.4. RM0433 says nothing explicit about what silicon does to a running SPI whose kernel clock stops. A refusal there is `predicted-fault` on p371 ("no clock will be provided to the peripheral"), not a modeled fault.

**M4. The teardown flake (`1300` §5.5).**
- P4.1. It is rare: at most 2 losses in 50 runs of one case (≤ 4%).
- P4.2. It is Renode's, a race at `quit` between disposing the machine's peripherals and closing the `logFile` backend. Nothing in `scripts/` sits between `quit` and the log. The `tviburi-check` side is deterministic.

**M5. Either firmware's boot, against (1)–(3), before the build** (read from `1300`'s measurement, not re-run here):
- ArduPilot's boot and the generated boots enable each peripheral before using it (0 locks);
- ArduPilot changes no selector and no source under a running SPI1;
- the 9 `per_ck` generated boards start SPI1 with no kernel clock (`1300` §5), which `1300` already ruled is not a stop.

## 1. Results (measured 2026-11-23, after §0 was committed at `c7c592b`)

Evidence: `~/.cache/tvi-1300a/{m1,m2,m3,m3probe,m4,base}` (scripts, monitor output and logs, kept until the architect says). One container at a time, under `~/bin/capped-run`, with `--network none`. Each prediction is marked **held**, **wrong** or **partly**.

### 1.0 Baseline

`tests/spike.sh` with `TVIBURI_TESTS=all`, on an export of `c7c592b` (code identical to `main` `604c3d6`): **112 ok, 0 FAIL, 299 s.** 57 runs carried both witnesses, and every one of them both end records. The runs without both are the cases built to lack them: load failures, `crash`, `adv-loglevel-*`, and the stock and no-overlay cases.

### 1.1 M1: an access to a peripheral whose enable is 0

**Stock model, on the stock board** (`m1/stock.resc`, `m1/stock2.resc`):

| What | Result | Prediction |
|---|---|---|
| `*ENR` at reset | Each registered peripheral's bit reads 1. AHB3ENR `0x4010`, AHB1ENR `0x8023`, AHB2ENR `0x40`, AHB4ENR `0x032807FF`, APB3ENR `0x8`, APB1LENR `0xC0FE03FF`, APB1HENR `0`, APB2ENR `0x00072033`, APB4ENR `0x00011E8A`. RCC_BDCR reads `0x8000`, that is `RTCEN` = 1; RM0433 resets it to 0 at a backup-domain reset (p427, `rm/fields.tsv`) | held (P1.1) |
| 0 written to every `*ENR` and `BDCR` | Every enable bit reads 0, except APB4ENR `RTCAPBEN`, a stored flag with no callback | P1.2 held: the write locks although the bit read 1 |
| A locked peripheral on each bus: DMA1 (AHB1), RNG (AHB2), QSPI (AHB3), GPIOA (AHB4), TIM2 (APB1L), TIM1 (APB2), LTDC (APB3), SYSCFG (APB4), RTC (`BDCR`) | Each read returns 0 and each write is ignored. Each logs `[WARNING] sysbus: Tried to read/write a locked peripheral: machine-0.<name>`, and nothing else. Identical on every bus. GPIOA's `MODER` reads 0 unlocked too (a stock value; silicon resets it to `0xABFFFFFF`), so for GPIOA the evidence is the WARNING line, not the value. APB1H has no registered peripheral, so nothing on it can be locked | held (P1.1) |
| 32-, 16- and 8-bit monitor accesses | All dropped, reading 0 | held |
| CPU `LDR`/`STR` (hand-placed Thumb at `0x24000000`) on locked TIM2.ARR | `r0` = 0 and the store is dropped. Unlocked, `r0` = `0xFFFFFFFF` and the store lands | held (P1.4) |
| DMA1 stream 0, memory to TIM2.ARR, TIM2 locked | The transfer completes (`NDTR` 0), but the write is dropped (ARR stays `0xFFFFFFFF`). Unlocked, it lands (`0xA5A5`) | held (P1.4) |
| `machine Reset` after locking TIM2 | Still locked: APB1LENR reads 0 and ARR reads 0. The peripheral itself is reset (ARR is `0xFFFFFFFF` once unlocked) | held (P1.5) |

**Platform peripherals the stock RCC cannot lock** (not among the 59 registrations), from `peripherals` on the stock board:
- **with an RM0433 enable bit:** `crypto` (`CRYPTEN`), `fdcan1`/`fdcan2` (`FDCANEN`), `fmc` (`FMCEN`), `sdmmc` (`SDMMC1EN`), `usb1`/`usb2` (`USB1OTGHSEN`, `USB2OTGHSEN`);
- **with none:** `dmamux1`, `exti`, `pwr`, `flashController`, `watchdog` (IWDG), `rcc`, and the memories.
- **`spi2`, `spi3`, `spi5` and `spi6` are not on the stock board at all.**

P1.3 is partly wrong. I named `spi2`/`spi3` (absent, not unregistered), and missed the seven above.

**Stock enable bits against RM0433** (`1300`'s RM0433 field list, `rm/fields.tsv`, not the PDF directly): all 98 `*ENR`/`BDCR` flags with an enable callback (`m1/stock-enables.tsv`) sit at RM0433's bit, by name (p427, p452–473). Every one of the 59 registered aliases has one.

**The guard today, on the Tviburi board** (`m1/tvi.resc`, HEAD):
- **The false pass,** measured:
  - APB2ENR reads `0x00000000` (the copy) while `spi1` takes and returns `CFG2 = 0x20400000`;
  - UART4, never enabled, takes and returns `BRR = 0x1234`.
- **A locked guarded peripheral is silent.** APB2ENR written 0 through the guard reaches the owned stock RCC and locks `spi1`. The `CFG2` write, the `CFG2` read and a `CR1` write with `SPE` each log only the `sysbus` WARNING: **no `spi1` record**, so the check would pass. P1.6 held.
- **The alias.** `SPI1EN` set through `RCC_C1_APB2ENR` (`0x150`) unlocks `spi1`, as p382 says.
- **The bus lock survives `machine Reset`, while the guard's copy is 0.** After reset, APB2ENR reads 0 from the copy, and `spi1` is still locked.

**How the guard can lock, by these measurements:**
- **The sync.** Write each `*ENR` copy into the owned stock RCC after each `Register()` and in `Reset()`. It locks all 59 registrations except `rtc`, whose lock is `BDCR.RTCEN` (no copy; stock `RTCEN` = 1 at reset) (P1.7).
- **Any locked access, CPU, monitor or DMA, is then dropped as p373 says for a write.** A read returns 0 where p373 says "invalid data": the twin returns a value silicon does not define, with no record.
- **For a guarded peripheral, the lock hides the access from its own guard.** So a refusal naming the enable bit needs one of these:
  - **(a)** the guarded peripheral stays unlocked on the bus, and asks the RCC guard for its enable at every access;
  - **(b)** something on the bus emits the record, which `RUL-07` rules out as a guard.
- **For an unguarded peripheral, nothing in Tviburi sees the access.** The lock is the whole model: silicon's write behaviour, and a 0 for silicon's undefined read.

### 1.2 M2: every Tviburi test, script and generator that drives a peripheral it never enabled

**Method.** I prototyped the sync on a scratch copy, never on the branch:
- `SyncEnables()` writes the nine `*ENR` copies into the owned stock RCC, after each `Register()` and in `Reset()`. The diff is in `~/.cache/tvi-1300a/m2/src`.
- The whole suite ran on it (`TVIBURI_TESTS=all`).
- Each test's `Tried to read/write a locked peripheral` lines were counted from its log. That count, not pass/fail, is the measure.

**Result: 102 ok, 10 FAIL, 280 s.** Every access dropped by the lock, by test:

| Test (`tests/spike.sh` unless named) | Locked accesses | What it is | Its checks |
|---|---|---|---|
| `conditions` (`tests/conditions.resc`) | 162 to `spi1` | monitor, no `SPI1EN`, `machine Reset` between cases | 4 FAIL (the branch table, the bench pairing, two copy-read checks) |
| `fields` (`1200`'s generated SPI sweep, `tools/tviburi-release cases`) | 348 to `spi1` | `machine Reset` after each case | 1 FAIL |
| `load` (`conditions.resc` under a bad mode) | 162 to `spi1` | the same script | 1 FAIL |
| `recorded` (`recorded-monitor.resc`, spike.sh L439) | 5 to `spi1` | monitor, no `SPI1EN` | 1 FAIL |
| `forge` (`firmware/spi1.rs` `path="forge"`) | 6 to `usart3` | the firmware writes USART3 with `USART3EN` never set | 2 FAIL |
| `stock-forge` (the same firmware, stock `spi1`) | 6 to `usart3` | as above | 1 FAIL |
| `rcc-reset` (`tests/1300/rcc.sh`) | 1 to `spi1` | its intended check that an unclocked peripheral locks | passes |

- **No test passed while its accesses were dropped,** apart from `rcc-reset`, whose drop is the check itself. `rcc-fields`' 35 matching lines are refusal texts that contain the word "locked", the same 35 as the baseline. Every firmware case other than `forge` enables each peripheral it uses. No adversarial case touches an unenabled peripheral.
- **P2.1 held.**
- **P2.2 held:** `forge` and `stock-forge` would otherwise become vacuous, since the forgery never reaches the log.
- **P2.3 wrong:** `rcc-spi1rst`, `rcc-registrations`, `adv-hook` and `adv-spray` are unaffected. `load` and `stock-forge` I missed.
- **P2.4 is not measured here** (§1.5).
- **The prototype does not lock `rtc`** (`BDCR`, P1.7). No test touches it.

### 1.3 M3: (3), each clock change under a running SPI1

**RM0433** (read by a subagent into `~/.cache/tvi-1300a/m3/table.md`, every quote with its txt line; I re-checked every quote this table uses against the text by `rmpage.sh`: L15437, L13721, L15028–15029, L15102, p383 L15407–15409, p371 L14952, p414 L16481/L16485–16486/L16493, p412 L16407, p398–399 L15894/15908/15924/15938, p401 L16006–16018, p403–410 L16094–16317, p2159 L84674, p2177 L85432, p2186 L85855, p386 L15506):

| Change | RM0433 | Pages |
|---|---|---|
| `HSION`/`CSION`/`HSEON` = 0 while that oscillator is `sys_ck`, or PLL1's source with PLL1 as `sys_ck` | **blocked** ("cannot be cleared"; "the hardware does not allow programming … to '0'") | p384–386; p340–342 |
| The same, PLL1's source with PLL1 on and **not** `sys_ck` | **contradictory**: the register text blocks it, §8.5.2 does not | p384–386 against p340–342 (that is (5)) |
| The same, the source of PLL2/PLL3 only | **silent**: every clear rule names PLL1 only | p384–386 |
| The same, or `HSI48ON` = 0, feeding a kernel clock directly (`hsi_ker_ck`, `csi_ker_ck`, `hse_ck` as `per_ck`, `hsi48_ck` as RNG) | **silent**: "Set and cleared by software." That `hsi_ker_ck` stops at `HSION` = 0 is inferred (`HSIKERON` acts in Stop only, p386) | p384–386 |
| `PLL1ON` = 0 with PLL1 as `sys_ck` | **blocked** ("the hardware prevents writing this bit to '0'") | p383; p347 |
| `PLL1ON` = 0 otherwise; `PLL2ON`/`PLL3ON` = 0 | **allowed** ("Set and cleared by software") | p383 |
| `DIVxyEN`, `PLLSRC`, `DIVMx` with the PLL on | **blocked** ("can be written only when … disabled") | p398–401; p347 |
| `PLLxDIVR` (`DIVN`, `DIVP`, `DIVQ`, `DIVR`) with PLLx on | **blocked** (pp. 403–410). The stock model enforces it for PLL1 only and takes PLL2/3 writes; the guard has no rule | p403–410 |
| Any kernel-clock selector, `CKPERSEL` included | **allowed**; nothing forbids changing one while the peripheral runs. The mux switches only with a clock on both inputs, and until then "no clock will be provided to the peripheral" | p371–372; field texts p412–417 |
| `SPI123SEL` left from `I2S_CKIN` once that clock stops | **blocked** ("it will not be possible to switch to another clock") | p414 |
| `SPI123SEL` = 101–111; `CKPERSEL` = 11 | "reserved, the kernel clock is disabled". The guard's allow-list refuses only 011 (`I2S_CKIN`); 101–111 pass and the query answers "disabled" | p414; p412 |
| The peripheral's own `PERxEN` = 0 | **allowed**; it removes both clocks ("No clock provided to the peripheral, because PERxEN='0'") | p376 Table 62 |
| What SPI does when `spi_ker_ck` stops while it runs as master | **silent**: the master "needs the spi_ker_ck kernel clock … active during communication" | p2159; p2177 |

**Measured on HEAD** (`m3probe/`, both guards in recorded mode):
- **Setup:** HSI and HSE on, `sys_ck` = HSE, PLL1 from HSI (`DIVM1` = 32), `PLL1ON`, `SPI123SEL` = `pll1_q_ck`, `SPI1EN`, SPI1 master with `SSOE` and `SPE`. The query says running before each change, and the setup emits no record.

| Case | Change | Query after | Record today | Stock |
|---|---|---|---|---|
| A | `HSION` = 0 (PLL1's source, PLL1 not `sys_ck`) | NOT running | `TVI-RCC-PLL-REF` `unmodeled` | takes it (CR `0x03030020`) |
| B | `PLL1ON` = 0 (PLL1 not `sys_ck`) | NOT running | **none** | takes it |
| C | `DIVQ1EN` = 0 (PLL1 on) | NOT running in recorded mode; enforcing keeps the copy | `TVI-RCC-PLL` `predicted-fault` | (copy) |
| D | `PLLSRC` → HSE (PLL1 on) | running | `TVI-RCC-PLL` `predicted-fault` | (copy) |
| E | `SPI123SEL` → `pll2_p_ck`, PLL2 off | NOT running (mux pending) | **none** | tag |
| F | `SPI1EN` = 0 | NOT running | **none** | locks `spi1` |
| G1 | `HSION` = 0 under `per_ck` = `hsi_ker_ck` | NOT running | `TVI-RCC-PLL-REF` only, because PLL1 is also from HSI in this setup; the `per_ck` loss itself has no rule | takes it |
| G2 | `CKPERSEL` → `hse_ck` (HSE on) | running (switched): a valid change | none | tag |
| G3 | `CKPERSEL` → `csi_ker_ck` (CSI off) | NOT running (`per_ck` mux pending) | **none** | tag |
| G4 | the same `per_ck` setup with **PLL1 from HSE** (`PLLSRC` = 10), then `HSION` = 0: the unconfounded case | NOT running | **none** | takes it (CR `0x03030020`) |
| H | `HSEON` = 0 (HSE is `sys_ck`) | running | none | **blocks** (keeps `HSEON`) |
| J | SPI1 stopped (`SPE` = 0), then `SPI123SEL` → `pll2_p_ck` | NOT running | none (valid: nothing runs) | tag |
| K | `CFGR.SW` = PLL1, then `PLL1ON` = 0 | running | none | **blocks** (keeps `PLL1ON`) |

- **P3.1 held** (H, K: the stock model blocks, and the guard mirrors it in `KeptCr`).
- **P3.2 held, and incomplete:** `PLLxDIVR` is blocked too, with no guard rule.
- **P3.3 held:** B, E, F, G3 and G4 lose the clock silently today.
- **P3.4 held.**
- **New:**
  - Clearing an oscillator that feeds only PLL2/PLL3 is silent in RM0433 (`TVI-RCC-PLL-REF` covers it, `unmodeled`);
  - `SPI123SEL` 101–111 pass the allow-list.

### 1.4 M4: the teardown flake (`1300` §5.5)

**Runs, each one a Renode run whose check names `spi1 rcc` and which carries both witnesses:**

| Shape | Runs | End record lost | Host load (1 min) |
|---|---|---|---|
| Baseline suite, `TVIBURI_TESTS=all` (`base/`) | 57 | 0 | — |
| Prototype suite (`m2/`) | 57 | 0 | — |
| One container, Renode 60× in sequence: `gen-hc16` (the case that flaked in `1300`) | 60 | 0 | 2.1–3.8 |
| The same: `conditions.resc` (monitor only, 13 `machine Reset`s, refusals) | 60 | 0 | 2.1–3.8 |
| One container, 6 Renodes in parallel × 10 rounds: `gen-hc16` | 60 | 0 | to 5.4 |
| The same: `conditions.resc` | 60 | 0 | to 7.4 |
| **Total** | **354** | **0** | |

- **The rate is under about 0.85% per run** (3/354, the rule of three at 95%). Renode exited 0 in every run.
- The suites' runs without both end records are all built to lack one: `crash` kills Renode, and `adv-loglevel-*` raises the log level above INFO.
- **The loops differ from the suite's shape:** the release and the check are built once, and Renode runs repeatedly in one container. Each run's log and output are checked as the suite checks them (`m4/loop.sh`, `m4/par.sh`).

**P4.1 held** (≤ 4%). **P4.2 is neither confirmed nor refuted.**
- I could not reproduce the loss, so I cannot place its cause.
- What I can say from the code:
  - `Records.End()` emits only if the guard was `named` (`model/Refusal.cs` L97–103) and is called from `Dispose`;
  - `scripts/` has nothing between `quit` and the log;
  - the check reads the log after Renode has exited.
- `1300`'s one loss (rcc's end written, spi1's not, Renode exit 0) fits a disposal path that skips `spi1` or a log line lost at exit. Both are Renode's, but that is inference, not evidence.
- **Proposal for the build:** the runner retries nothing (WO (7)). The suite keeps failing closed. A future loss's log is kept by the suite as it is today.

### 1.5 M5: either firmware's boot, against (1)–(3)

**(1), ArduPilot.** Aethelgard's own verdict of `1300`'s capture (`cap-ap/verdict.json`, `boot.locked`, with `log_lines_dropped` = 0) lists **22 accesses to a locked peripheral**, under the stock model and the consumer's cold writes:
- 20 DMA writes at 923,860 ns: DMA1 and DMA2 `S0CR`–`S7CR`, `LIFCR`, `HIFCR`, written before `DMA1EN`/`DMA2EN`;
- an I2C1 `CR1` read and write at 164,652,020 ns, the virtual time of the I2C1EN write (#965), with the order inside the quantum not resolved.

On silicon those writes are ignored (p373), and ArduPilot flies. So:
- if (1)'s refusal applies to every locked access, **ArduPilot's boot refuses at 0.92 ms**, a §7 stop;
- if it applies to guarded peripherals only (`spi1`), ArduPilot never touches `spi1` unenabled, and nothing refuses.

**(1), the generated boots.** The boards are as `1300` captured them at Aethelgard `75da8053`; Aethelgard `main` is now `1c32c8ea`, and I have not regenerated them. Their `verify` JSON keeps no lock list, and `1300`'s Renode logs are gone (`~/.cache/tvi-1300` does not exist). **Not measured.**

**(2), the generated boots. A §7 stop.** `1300`'s order table (`draft/order.md`) shows nine generated boards starting SPI1 (and SPI2 on two) with no kernel clock: `SPI123SEL` → `per_ck` while the mux's input `pll1_q_ck` never ran (p371–372). Under (2) each is a `predicted-fault` at SPI1. The boards and their first start:

| Board | SPI1 starts (all unclocked) | First at |
|---|---|---|
| motors-dshot600 | 2 | 15,760 ns |
| motors-pwm | 2 | 15,760 ns |
| sentinel-01 | 2 | 23,760 ns |
| sentinel-01-ext-bmp581 | 2 | 27,470 ns |
| sentinel-01-minimal | 2 | 21,330 ns |
| sentinel-02-bench | 10 | 15,040 ns |
| sentinel-02-shaped | 2, and SPI2 10 | 40,030 ns |
| sentinel-03 | 2, and SPI2 10 | 70,990 ns |
| serial-dshot-9600 | 2 | 24,770 ns |

`1300`'s architect ruled this not a stop for `1300` (13:45Z). This work order's §2 and §7 make it one. **I ask for the ruling and do not carry `1300`'s over.** ArduPilot's 55 SPI1 starts are all clocked (`pll1_q_ck` running).

**(3), ArduPilot. Unresolved, and possibly a §7 item.**
- `1300`'s field table (`TVI-WO-1300.rm0433.md` §4; 26,021 RCC writes in the 5 s boot) shows ArduPilot writing both 0 and 1 to:
  - `SPI1EN`, `SPI2EN` and `I2C1EN`;
  - `PLL1ON`, `PLL2ON`, `PLL3ON` and `HSI48ON`.
- So ArduPilot clears SPI enables during the boot. Its 55 SPI1 and 7,062 SPI2 starts suggest a clock enable and disable per `spiStart`/`spiStop`.
- **`SPE` is cleared before the stop.** The measured `spi_lld_stop` (`0100b`; `firmware/spi1.rs` `path="ardupilot"`) clears `SPE`, then zeroes `CR1`, `CR2`, `CFG1`, `CFG2` and `IER`.
- **Whether the RCC disable always comes after that** is ChibiOS's `spi_lld_stop` order from memory, not measured. `1300`'s per-write traces are not on disk (`~/.cache/tvi-1300` is gone).
- If ArduPilot ever clears `SPI1EN`/`SPI2EN`, or a selector or PLL, while `SPE` = 1, then Q2's case F (or B/E) refuses its boot.
- **The first thing the build re-runs, recorded, before any (3) rule is kept.**

## 2. Predictions, scored

| | Held | Wrong or partly |
|---|---|---|
| M1 | P1.1, P1.2, P1.4, P1.5, P1.6, P1.7 | P1.3 partly: `spi2`/`spi3` are absent from the board, not unregistered; I missed `crypto`, `fdcan1/2`, `fmc`, `sdmmc`, `usb1/2`, `watchdog` |
| M2 | P2.1, P2.2 | P2.3 wrong: `rcc-spi1rst`, `rcc-registrations`, `adv-hook` and `adv-spray` are untouched; I missed `load` and `stock-forge`. P2.4 not measured |
| M3 | P3.1, P3.3, P3.4 | P3.2 incomplete: `PLLxDIVR` is blocked too, with no guard rule |
| M4 | P4.1 | P4.2 unresolved |
| M5 | ArduPilot's SPI1 starts all clocked | My prediction of 0 locks was wrong: ArduPilot makes 22 locked accesses (DMA1/DMA2, I2C1). The `per_ck` starts are a stop under this WO (§3) |

## 3. Stops and questions for the architect

**§7 stop: a firmware's boot would refuse under a rule here.** Under (2), nine generated boards start SPI1 (two of them also SPI2) with no kernel clock, which is a `predicted-fault` at SPI1 (§1.5; p371–372, p414).
- The boards: motors-dshot600, motors-pwm, sentinel-01, sentinel-01-ext-bmp581, sentinel-01-minimal, sentinel-02-bench, sentinel-02-shaped, sentinel-03, serial-dshot-9600.
- `1300`'s 13:45Z ruling covered `1300`, where only a test hook asked. This WO's §2 says "any such is a stop".
- **Ask:** build on, with these boots expected to refuse at SPI1 as the firmware's defect for Aethelgard's queue? Or not?

**Possibly a second §7 item, unresolved: ArduPilot under (3).**
- ArduPilot clears `SPI1EN`/`SPI2EN` and `PLLxON` during its boot (§1.5). Whether any clear lands while `SPE` = 1 is not on disk.
- If one does, Q2's case F or B refuses the boot.
- I will measure it first in the build (a recorded boot) and stop if it fires.

**Q1. The scope of (1)'s refusal.**
- The stock lock is per peripheral and silent. Once a guarded peripheral is locked, its own guard never sees the access.
- An unguarded peripheral has nothing in Tviburi that sees the access.
- ArduPilot makes 22 accesses to locked DMA1/DMA2/I2C1 that silicon ignores (§1.5).
- **Proposal:**
  - **(a) Lock every registered peripheral** by the sync (§1.1), as silicon does for a write, except the guarded ones. A guarded peripheral (`spi1`) stays enabled on the bus, and its guard asks the RCC guard for its enable at every access: `predicted-fault` naming `SPI1EN` and p373.
  - A write is "ignored" and a read "will return invalid data", both on p373.
  - ArduPilot and the generated firmware never touch `spi1` unenabled.
  - **Generalised to every future guard, it would refuse ArduPilot at 164,652,020 ns once I2C1 is guarded:** that I2C1 `CR1` read and write happen with `I2C1EN` at 0 in the twin, inside the same quantum as the enable. **Ask:** is this rule SPI1's only, or general, with I2C's order measured when its guard comes?
- **Not proposed:** refusing every locked access. ArduPilot would refuse at 0.92 ms on DMA writes that silicon ignores.
- **Which kind for a guarded access with its enable at 0?** The `RUL-18` amendment (a write silicon ignores that changes nothing) applies to a write of the value already held. I propose `predicted-fault` for every guarded access with its enable at 0. A same-value write is not exempted, because the firmware believes the peripheral is running.

**The other §7 conditions:**
- **No rule proposed here needs a record field `0100b`'s contract lacks.** Each proposed refusal fills `Refuse(rule, kind, register, bit, condition, citation, state, offset, value, width)` (`model/Refusal.cs` L67), plus the `pc` and `virtual_time` it already adds:
  - (1) names `SPI1EN` in `bit`;
  - (2) names the selector and source in `bit`/`condition`, as `TVI-RCC-KERNEL` does;
  - (3) is a write at the RCC.
- **The one-file peak is not measured at §1.1.** `1300` measured 955,512–980,696 KiB against 1,048,576 KiB (66–91 MiB under). The build re-runs it.

**Q2. Block or refuse, for (3).**
- RM0433 blocks `DIVxyEN`, `PLLSRC` and `DIVMx` with the PLL on. `TVI-RCC-PLL` refuses those today, as `predicted-fault`. The block means the clock is *not* lost.
- **Proposal:** keep `TVI-RCC-PLL` as it is (the firmware's ignored write, `RUL-18`), and say in the header that the clock survives.
- (3)'s "model the block" then applies to the oscillator and `PLL1ON` clears that the stock model already blocks and `KeptCr` mirrors (cases H and K). No record is emitted there, and the query keeps answering running.
- **(3)'s refusal at the RCC write (`predicted-fault`, p371 "no clock will be provided to the peripheral"),** for a running SPI1:
  - B: `PLLxON` cleared;
  - E: a selector change that leaves the mux pending;
  - G3: `CKPERSEL`;
  - an oscillator clear that is not blocked and not PLL1's source (that one is (5));
  - F: `SPI1EN` cleared (Table 62, p376).
- Cases A and C keep their current rules. The first rule that fires wins.

**Q3. GPIO and `TVI-WO-1301`.** The sync locks GPIOA–K at reset, as silicon does.
- If 1301's GPIO guard is a wrapper over the stock ports, then under the lock its accesses with `GPIOxEN` = 0 never reach it (Q1 (a) applies to it too).
- 1301's monitor tests will need the enables.
- I will tell 1301 on the bus once you rule.

**Q4. Rows, not this WO (the scope test).**
- `rtc` locks on `BDCR.RTCEN`, which the stock model reads as 1 from reset (silicon: 0, p427). The guard keeps no `BDCR` copy, so the sync does not lock it.
- `SPI123SEL` 101–111 pass the allow-list. The query answers "disabled", so (2) refuses at the start anyway.
- `PLLxDIVR` changed with PLLx on: blocked by RM0433 (pp. 403–410); no guard rule, and the stock model takes PLL2/3 writes.
- Witness and end records log at INFO (`model/Refusal.cs` L101, L114), while `RUL-12` says records log at Error. It fails closed (`adv-loglevel-*`), and it is outside my fence (the record grammar).

**What I will build after the ruling,** per Q1–Q2 and the WO, fixing (§1.2):
- `conditions.resc`, the `recorded` monitor script and `1200`'s sweep generator: `SPI1EN` after each `machine Reset`;
- `firmware/spi1.rs` `forge`: `USART3EN` first;
- `rcc-reset`'s lock assertion. Under Q1 (a), `spi1` is no longer locked on the bus: its guard refuses instead.

`tests/1300/smoke.resc` (a `1300` prototype that no test runs, and (6)'s to keep or delete) enables before each access, except its own lock check.
