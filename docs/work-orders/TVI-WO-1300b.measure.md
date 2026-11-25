# TVI-WO-1300b — measurement (WO §1 step 1)

**Builder:** Claude (Opus 5.5), Furnace. **Branch:** `wo/tvi-1300b-rcc-rows`, from `main` `87c4164`.
**Held references:** RM0433 Rev 9 (`~/.cache/aet-held/rm0433-rev9.{pdf,txt}`, sha256 `672a9433…` / text `8e02c0c0…`); pages by `tests/1300/rmpage.sh`.
**Stock source read:** `STM32H7_RCC.cs` at `~/.cache/aet-4202/renode-src/inf`, sha256 `7f10d491…`, the file `1300` pinned.
**Inputs, read-only:** `1305`'s armed run (`~/.cache/tvi-1305/armed`: capture `cap/s03`, injection `inj/inject.resc`, console `run2`, ELF `dbf598d8…`); `1305`'s captures of the 14 generated boots (`cap-gen`, Aethelgard `fa12abf4`) and ArduPilot's fixture boot (`cap-ap/fx`).

## 0. Predictions (committed before any run, 2026-11-25T02:06Z)

Written after reading the WO, `1300a` and its FEEDBACK, `1305`'s FEEDBACK, the RCC guard, the stock `STM32H7_RCC.cs`, RM0433 p427 and p471, ChibiOS's `init_bkp_domain` (ArduPilot `fb773567`, `modules/ChibiOS/os/hal/ports/STM32/STM32H7xx/hal_lld.c`, read in Aethelgard's pinned `aethelgard-ardupilot` image), and the armed run's existing records (offline only: no run yet).

**P1. The armed run's RCC records** are the six `1305` listed, and no others: `AHB1ENR.ADC12EN` = 1, `AHB4ENR.ADC3EN` = 1, `AHB1LPENR.ADC12LPEN` = 0, `AHB4LPENR.ADC3LPEN` = 0, `APB1LLPENR.USART2LPEN` = 0, `APB2LPENR.TIM1LPEN` = 0. Each record names only the first field a write fails (`RefusedField` stops there), so I predict that no refused write hides a second failing field.
*Already checked offline (not a run):* the nine distinct refused (offset, value) pairs in `run2`'s 77,108 records, evaluated against every allow-list row, each fail exactly one field (`h/allfields.py`).

**P2. The 14 generated boots and the fixture** make the RCC records `tests/1300/boots.tsv` lists and no others: `d1-drv8876`'s `ADC3EN`, and the nine `per_ck` boards' `TVI-SPI-KERNEL` (SPI, not RCC). The fixture makes none. No generated boot writes an `*LPEN` bit.

**P3. hseFrequency.**
- **Tviburi's committed platforms:** `model/tviburi-nucleo-h753zi.repl` declares 8 MHz (the stock Nucleo's `rcc: hseFrequency: 8_000_000`, restated), `tests/1300/hse16*.repl` 16 MHz, `tests/1300/unregistered.repl` 8 MHz. Every other Tviburi test platform includes the Nucleo board and inherits its 8 MHz.
- **Aethelgard's generated platforms** (`platform.rs` `emit_hse` at Aethelgard `main` `fd12b2f8`): `hseFrequency` only for a board whose specification has an HSE. Of the captured 14, `hse-crystal-16` (16 MHz), `hse-external-8` and `sentinel-02-bench-hse8` (8 MHz) declare one; **the other 11, ArduPilot's fixture and the armed run declare none**, and take the guard's 8 MHz default today.
- **Whether any relies on it** (WO §7's stop): I predict **none**. No platform that declares nothing sets `HSEON`, selects `PLLSRC` = 10, `SW` = 010 or `CKPERSEL` = 10, or sets `HSEBYP`. ArduPilot on Sentinel-03 runs from the HSI (the armed run's first record's state: `CR=0x3F003025`, `HSEON` = 0; `PLLCKSELR=0x00408040`, `PLLSRC` = 00).

**P4. RTCEN.**
- **RM0433:** `RCC_BDCR` "Reset value: 0x0000 0000, reset by Backup domain reset"; `RTCEN` "0: rtc_ck clock is disabled (default after backup domain reset)"; its bits "are only reset after a backup domain reset … Any other internal or external reset will not have any effect on these bits" (§8.7.26 p427; §8.4.6 p334). So silicon's `RTCEN` is 0 at power-on and survives a system reset.
- **The stock model** reads `RTCEN` as `IsPeripheralEnabled(RTC)`: 1 from reset whenever `rtc` is registered (stock L493-495), and writing it locks or unlocks `rtc` on the bus.
- **What `RTCEN` gates on silicon is `rtc_ck`, not the RTC's registers.** "The rtc_ck clock is enabled through RTCEN … The RTC bus interface clock (APB clock) is enabled through RTCAPBEN and RTCAPBLPEN" (p366); `RCC_APB4ENR.RTCAPBEN` "1: The register clock interface of the RTC (APB) is enabled (default after reset)" (p471). **I predict this changes (5)'s premise:** silicon does not lock the RTC's registers at reset (RTCAPBEN = 1). With `RTCEN` = 0 the RTC's calendar does not count, which the stock RTC model does not show.
- **No committed boot reads `RCC_BDCR.RTCEN` and acts on it.** ArduPilot reads `BDCR` in `init_bkp_domain` and compares only `RTCSEL` (`STM32_RTCSEL_NOCLK`, so no backup-domain reset); `HAL_USE_RTC` is `FALSE` in ArduPilot's `halconf.h`, so the `RTCEN` branch is not compiled. No firmware accesses `rtc`.

**P5. (2), a running USART's kernel clock lost:** no committed boot makes an RCC write, after any USART's `UE` = 1, that changes `D2CCIP2R`, the oscillator or PLL2/PLL3 the USART's mux selects, or its `USARTxEN`. So the new watch changes no record.

## 1. The runs

All on Furnace in Aethelgard's pinned tools image (`aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` pins), `~/bin/capped-run`, `--network none`, `nice -n 10`, one container at a time. The harness is the committed `tests/1301/replay-1301.sh` plus `1305`'s logging (`~/.cache/tvi-1300b/h/replay-l.sh`): `rcc`, `rtc` and the eight UARTs access-logged (and `pwr` in E1), with every guard recorded. Analysed by `h/analyse.py`, `h/ueorder.py` and `h/allfields.py`.

| Run | Tree | What |
|---|---|---|
| M1 | `main` `87c4164` | the 14 generated boots (`1305`'s captures, Aethelgard `fa12abf4`) and ArduPilot's fixture boot (5 s) |
| M1-armed, M1b-armed | `main` `87c4164`, twice | the armed Sentinel-03 run: `1305`'s capture and injection (`s03-arm-s`, 21 frames, unpadded), 14 s, `usart1` → `dmamux1@41` |
| E1 | `main` `a24df6f` (with `1305a`) | M1-armed with `RCC_BDCR` written 0 by the monitor before the run: the stock model then reads `RTCEN` = 0 and locks `rtc`, which emulates (5) as the WO states it |

**Determinism.** M1-armed and M1b-armed are identical: 976,059 `rcc` access lines with one sha256, the same `usart1` transmissions, and 76,898 `would-refuse` records each. `1305`'s `run2` (tree `8e1d2d1`) differs from both (77,076 `ADC12EN` records against 76,864; armed from 9.750 s against 9.775 s). So "identical to `main`" is judged against a `main` run in this harness, never against `run2`.

### 1.1 Verdicts on §0

| | Verdict | Measured |
|---|---|---|
| **P1** | **holds** | M1-armed's RCC records are the six (`ADC12EN` 76,864; `USART2LPEN` 27; `ADC3EN` 2; `ADC12LPEN`, `ADC3LPEN`, `TIM1LPEN` 1 each), plus `dma1`'s `CIRC` (2), not the RCC's. Each refused write fails exactly one allow-list row (all 677 rows evaluated). |
| **P2** | **partly wrong** | No generated boot writes an `*LPENR`, and the fixture makes no record. But on these captures (`fa12abf4`) the nine `per_ck` boards make **no** `TVI-SPI-KERNEL`: Aethelgard `4240e` fixed the generator after `boots.tsv`'s commit (`75da8053`). The only record on the 15 boots is `d1-drv8876`'s `ADC3EN`. |
| **P3** | **holds** | Below (§2.3). No platform that declares nothing uses the HSE. |
| **P4** | **wrong** | ArduPilot reads `RTCEN` and acts on it in the armed run: 88 `BDCR` reads, 87 at pc `0x8107A9C` (`set_rtc_backup`), each followed by 19 RTC backup-register writes (`0x54`–`0x9C`, pc `0x8107ACC`), from 5.342 s. The source branches on `RTCEN` (§2.4). |
| **P5** | **holds** | No `USARTxEN` cleared while that port's `UE` = 1, on any of the 16 runs. The one `D2CCIP2R` write (ArduPilot, `0xE01009`, 3.77 µs) comes before any `UE`. No `CR` or PLL write after the first start. |

## 2. The table

### 2.1 Every RCC record in the armed run, with a proposal for each

| Record (M1-armed) | Count, first | RM0433 Rev 9 | Proposal |
|---|---|---|---|
| `RCC_AHB1ENR.ADC12EN` (bit 5) = 1 | 76,864; 0.97 ms, pc `0x08103F40` (ArduPilot's ADC poll writes `0x21`/`0x23` throughout) | p455: "1: ADC1 and 2 peripheral clocks enabled … the kernel clock selected by ADCSEL … and the rcc_hclk1 bus interface clock" | **Modeled as an enable**, as `DMA1EN` and `TIM1EN` are: the guard's copy, served on read, written into the owned stock RCC, which unlocks `adcM1S2` on the bus (it is registered, `stm32h7-rcc-restate.repl`). **What "modeled" claims: the bus enable only.** The ADC's kernel clock (`CKMODE`, p920; `ADCSEL`, p419) stays unanswered. The ADC is unguarded, so its conversions are the coverage queue's (`RUL-17`), and the roadmap's ADC-guard row keeps the `CKMODE` question. This supersedes `1300a`'s 17:52Z ruling (`unmodeled`, naming `CKMODE`). |
| `RCC_AHB4ENR.ADC3EN` (bit 24) = 1 | 2; 0.97 ms | p458, as above (`rcc_hclk4`) | As `ADC12EN` (`adc3` is registered). |
| `RCC_AHB1LPENR.ADC12LPEN` (bit 5) = 0 | 1; 0.97 ms, pc `0x08103F84` | p477: "0: ADC1/2 peripheral clocks disabled during CSleep mode" | **Acknowledged by name:** the twin does not model CSleep (the roadmap row), so the clock runs while the CPU sleeps, whatever this bit says. This is `1300`'s A6 reason, as for `DMA2LPEN`, `RNGLPEN`, `SPI1LPEN` and `SPI2LPEN`. |
| `RCC_AHB4LPENR.ADC3LPEN` (bit 24) = 0 | 1; 0.97 ms | p480 | As `ADC12LPEN`. |
| `RCC_APB1LLPENR.USART2LPEN` (bit 17) = 0 | 27; 0.2017 s, pc `0x08105C5A` | p485 | As `ADC12LPEN`. |
| `RCC_APB2LPENR.TIM1LPEN` (bit 0) = 0 | 1; 8.177 s, pc `0x08105952` | p492 | As `ADC12LPEN`. |

- **Nothing else is found.** No refused write hides a second field. Exactly these four `LPEN` bits are newly acknowledged (WO §5: an acknowledgement no wider than the measurement). ArduPilot also clears four `LPEN` bits already acknowledged in `1300` (`DMA2LPEN`, `RNGLPEN`, `SPI1LPEN`, `SPI2LPEN`), on both the fixture and the armed run. The generated boots write none.
- **The rows come from `tests/1300/allowlist.py`.** So `tests/1300/fields.resc` changes with them: `ADC12EN`/`ADC3EN` and the four `LPEN` leave gate clause 6's refusing cases.
- **A committed expectation moves:** `d1-drv8876`'s row in `tests/1300/boots.tsv` (`ADC3EN`, `unmodeled`) becomes `none`, and `boots.sh check` no longer requires an ADC refusal to name `CKMODE`.

### 2.2 `TVI-RCC-CLOCK` over the USART guards (WO (2))

- **The query already covers `usart1`, `usart2` and `usart3`** (`Clocked`: `USART16SEL` and `USART234578SEL` in `RCC_D2CCIP2R`, p417; `USARTxEN`, p463, p470).
  - `uart4`, `uart5`, `usart6`, `uart7` and `uart8` have no rule.
  - Their starts already refuse `unmodeled` (`TVI-USART-KERNEL`).
  - Their enables are outside the allow-list.
  - So none of them can run unwatched.
- **The watch is a separate set, not `guarded`.** `guarded` holds the SPI guards, which `UnlockGuarded` keeps unlocked on the bus. The USART guards rely on the bus lock (they have no enable rule of their own), so they must not join it. `Register` adds a `GuardedSTM32F7_USART` to the watch under its alias's name.
- **"Running" is `UE` = 1**, as the WO says. This is stricter than the USART guard's start (`UE` with `TE` or `RE`), and it covers a port enabled with neither. The USART guard gains one public read of its `UE` copy, its only change.
- **The record:** `predicted-fault` at the RCC write (p371; p2019 `usart_ker_ck`; Table 62 p376 for `USARTxEN`), naming the port and `UE` = 1.
- **The boots:** no write in the 16 runs triggers it (P5), so no record is expected.

### 2.3 Which platforms declare `hseFrequency`

| Platform | Declares | Uses the HSE |
|---|---|---|
| **Tviburi** `model/tviburi-nucleo-h753zi.repl` and every test platform that includes it (`scripts/prelude.resc`, `tests/1300/recorded.repl`, and the cases of `tests/1301`, `1302`, `1303`, `1304`, `1305`, `spike.sh`) | 8 MHz (the stock Nucleo's, restated on the guard) | as each case plants |
| `tests/1300/hse16.repl`, `hse16-trap.repl` | 16 MHz | yes |
| `tests/1300/unregistered.repl` | 8 MHz | no |
| `spike/1150/retype/forms/*` | stock types and spike classes only, not the guard | — |
| **Aethelgard** (`emit_hse`, `main` `fd12b2f8`: only for a specification with an HSE, never under `foreign_clock`) `hse-crystal-16` | 16 MHz | `HSEON`, `PLLSRC` = HSE |
| `hse-external-8`, `sentinel-02-bench-hse8` | 8 MHz | `HSEBYP`, `HSEON`, `PLLSRC` = HSE |
| the other 11 generated boards; ArduPilot's fixture (`--foreign`); the armed Sentinel-03 run | **none** (the guard's default) | **never**: no `HSEON`, `HSEBYP`, `PLLSRC` = 10, `SW` = 010, `CKPERSEL` = 10 or MCO from the HSE in any of their RCC writes |

**No committed platform relies on the 8 MHz default** (so WO §7's stop does not fire).

**Who reads the HSE frequency today:**
- the flash guard (`GuardedSTM32H7_Flash.cs` L495, `sys_ck` = HSE; L521, PLL1's reference);
- the USART guard (`GuardedSTM32F7_USART.cs` L600, `Vco`, merged with `1305a`);
- the owned stock RCC (`STM32H7_RCC.cs` L114, L1529: `sys_ck` and PLL1, hence the systick and every bus frequency).

### 2.4 `RTCEN`

- **RM0433, held:**
  - "Reset value: 0x0000 0000, reset by Backup domain reset … Any other internal or external reset will not have any effect on these bits" (§8.7.26 p427);
  - `RTCEN` "0: rtc_ck clock is disabled (default after backup domain reset)" (p427);
  - a backup domain reset is `BDRST` or V<sub>SW</sub> out of range (§8.4.6 p334);
  - "The rtc_ck clock is enabled through RTCEN … The RTC bus interface clock (APB clock) is enabled through RTCAPBEN and RTCAPBLPEN" (p366);
  - `RCC_APB4ENR.RTCAPBEN` "1: The register clock interface of the RTC (APB) is enabled (default after reset)" (p471).
- **The stock model:**
  - it reads `RTCEN` as `IsPeripheralEnabled(RTC)`: 1 from reset (L493–495);
  - writing 0 locks `rtc` on the bus, and 1 unlocks it;
  - `RTCAPBEN` is a tag (L1041).
  - **So today any `RCC_BDCR` write the allow-list passes (every field at 0) locks `rtc`.** That is latent: no committed boot writes `BDCR`.
- **The stock PWR is a canned stub:** `PWR_CR1` always reads `0xF000C100`, so `DBP` reads 1 whatever was written (`stm32h7.repl` L728–734). `BDCR` is write-protected until `DBP` = 1 (p427), and the twin cannot see whether a firmware set it.
- **What ArduPilot does with `RTCEN`** (ArduPilot `fb773567`, read in its pinned image):
  - `init_bkp_domain` (ChibiOS `hal_lld.c`) sets `DBP`, compares only `RTCSEL` with `STM32_RTCSEL_NOCLK`, and compiles out its `RTCEN` branch (`HAL_USE_RTC FALSE`). That is the fixture's and the armed run's one boot-time read: pc `0x8103838`, and `0x81083C4` in the armed ELF.
  - `set_rtc_backup` (`hwdef/common/stm32_util.c` L231–236) does `if ((RCC->BDCR & RCC_BDCR_RTCEN) == 0) { RCC->BDCR |= STM32_RTCSEL; RCC->BDCR |= RCC_BDCR_RTCEN; }`, then writes the RTC backup registers. In the armed run it runs 87 times from 5.342 s, and always reads `0x8000` today.
- **E1, (5) as the WO states it:** §2.4.1.

#### 2.4.1 E1: `RTCEN` at 0 in the armed run (on `main` `a24df6f`, emulated by a monitor write of `BDCR` = 0)

- **The setup:** `BDCR` reads back `0x0`, and `rtc` is locked by the stock callback. ArduPilot's boot read (pc `0x81083C4`) then returns 0. It follows `PWR_CR1` writes of `0xF000C000` and `0xF000C100` (pc `0x810839E`, `0x81083C0`: `DBP` set first, as `init_bkp_domain`'s source orders it). Nothing else changes at boot.
- **At 5.342 s, `set_rtc_backup`'s first call** reads `RTCEN` = 0 and writes `BDCR` twice: `0x0` (pc `0x8107AA4`, the `|= STM32_RTCSEL`), then `0x8000` (pc `0x8107AAC`, `RTCEN` = 1).
  - **The second write is a new record:** `TVI-RCC-FIELD`, `unmodeled`, `RCC_BDCR.RTCEN (bit 15) = 1`.
  - In recorded mode it then passes: the stock model unlocks `rtc`, the 19 backup writes land, and the later 86 calls read `0x8000` and write nothing.
- **Still arms:** `COMMAND_ACK` 400 result 0 at 9.001 s, armed from 9.775 s, 19 heartbeats, the same as M1-armed. Records: M1-armed's 76,898 plus that one.
- **In enforcing mode** that record is the first refusal. If the rule were only "read 0, lock `rtc` while `RTCEN` = 0", `rtc` would stay locked and ArduPilot's backup writes would be dropped. On silicon they land: `RTCAPBEN` = 1 (p471), and `DBP` was set at pc `0x81083C0`.

## 3. Proposals and questions for the ruling

Each proposal is numbered by the WO's item. Where there is a choice, my recommendation comes first.

**Q1 (3), the armed run's RCC records.** As §2.1:
- `ADC12EN`/`ADC3EN` modeled as bus enables (the bus lock only; the kernel clock stays the ADC guard's row). This supersedes `1300a`'s 17:52Z ruling.
- The four `LPEN` clears acknowledged by name for CSleep, those four only.
- Generated by `allowlist.py`, with `fields.resc` following.
- `d1-drv8876`'s `boots.tsv` row becomes `none`.

**Q2 (2), the watch.** As §2.2:
- a separate watch set of the USART guards registered on the RCC guard;
- "running" is `UE` = 1;
- one public read of `UE` added to the USART guard;
- `predicted-fault` (p371, p2019, p376).

**A question within (2):** a USART guard declared on the bus but not registered on the RCC guard (`1305` adversarial G6) would run unwatched. The SPI guard refuses every access then, `unmodeled` (`Holds`, `1300a` F1). Should the USART guard do the same?
- **I recommend yes:** otherwise (2) has a silent path, which WO §5's third hunt names.
- It is a USART-guard rule, outside "only where (2) registers it", so it needs your grant. Without the grant, it stays a row.

**Q3 (4), where "no crystal" refuses.**
- **(a), recommended: at the RCC write that sets `HSEON` = 1,** on a platform that declares no `hseFrequency`. A new rule, `TVI-RCC-HSE`, `unmodeled`, naming `HSEON` and p384. Nothing derived from the HSE can run without `HSEON`:
  - `SW` = HSE refuses `TVI-RCC-SW`;
  - PLLx on from the HSE refuses `TVI-RCC-PLL-REF`;
  - `per_ck` from the HSE is "not running" in the query.
  So one rule covers `sys_ck`, the PLLs, `per_ck`, the MCOs and the RTC. `HSEBYP` alone runs nothing and stays acknowledged (A3).
- **(b): at each reader** (the flash guard's AXI clock, the USART guard's `Vco`, the stock model's systick). That means more places and a later refusal. Not recommended.
- **Either way, the readers must never see a number.**
  - The guard's `HseFrequency` becomes "none" when nothing is declared (`ulong?`, null), never 8 MHz and never 0. A 0 would give the flash guard 0 Hz, which passes every latency check (WO §5's first hunt). The constructor's default changes to match; whether Renode's `.repl` takes a nullable property, or 0 must stand for "none" at the entry, is measured at build.
  - The USART guard compiles unchanged: L600 already takes `ulong?` and treats null as "no rule".
  - **The flash guard needs one line** (L521, `ulong reference` to `ulong?`, returning null): outside my fence. **Grant?**
  - The owned stock RCC gets 0, reachable only after a refusal (recorded mode).
- **A committed expectation moves (the architect's note, 02:15Z):** `usart-idlehse`'s second half deletes the 8 MHz line and expects the default's `TVI-USART-IDLE` (VCO 128 MHz). Under (a) its first record becomes `TVI-RCC-HSE` at the `HSEON` write. Its 16 MHz half is unchanged.

**Q4 (5), `RTCEN`. The WO's premise does not hold as stated:** silicon locks the RTC's registers by `RTCAPBEN` (p471, 1 at reset; p366), not by `RTCEN`, which gates only `rtc_ck`. "`rtc` locked as silicon locks it" therefore means **never locked** in any committed boot, since `RTCAPBEN` is held at 1 by its row. Three options:
- **(a), recommended:**
  - **`RTCEN` is served from the guard's copy,** 0 at construction (a power-on is a backup domain reset, p427, p334) and kept across `machine Reset` (p427: no other reset affects it).
  - **Writes of 0 and 1 go into the copy.** The stock model's `RTCEN` is held at 1, so `rtc` stays reachable as `RTCAPBEN` = 1 says, and no `BDCR` write locks it (that also closes today's latent lock, §2.4).
  - **Why `RTCEN` = 1 is safe to take:** with `RTCSEL` held at 00 by its row ("No clock", p427), `rtc_ck` has no source whatever `RTCEN` says, so the bit changes no clock silicon would give.
  - **What remains is `BDCR`'s write protection.** `DBP` is in the PWR, a canned stub that always reads 1. This is acknowledged by name, as a row for a PWR guard.
  - **Effect:**
    - the armed run gains ArduPilot's two `BDCR` writes at 5.342 s (`0x0`, `0x8000`), with no record, the backup writes unchanged, and every later read `0x8000`;
    - the fixture's one read returns 0, with no write after (`RTCSEL` compare only);
    - the generated boots never touch `BDCR`.
- **(b), the WO as written:** `RTCEN` reads 0, written 0 into the stock model so `rtc` is locked, and `RTCEN` = 1 stays `unmodeled`. E1 measured it: the armed run gains `RCC_BDCR.RTCEN = 1`, `unmodeled`, at 5.342 s. That is a new unexplained-looking record against (3)'s acceptance. In enforcing mode the RTC's backup registers would be dropped where silicon keeps them.
- **(c): `RTCEN` keeps the stock's 1** (`stock-wrong`, acknowledged by name), and the row stays open. No boot moves.

**Q5, `boots.tsv`.** It names Aethelgard `75da8053`, whose captures still show the nine `per_ck` refusals; `1305`'s captures (`fa12abf4`) show none.
- My replays (WO §1 step 3) compare `main` and the branch on `1305`'s 15 captures plus the armed run, all in this harness.
- `boots.tsv` changes only `d1-drv8876`'s row. Re-pinning it to `fa12abf4` is separate work; I propose a row, not this WO.

**Budget:** judged in CI, as the WO says.
