# TVI-WO-1301 §1.1 — the GPIO field table measured against both firmwares, the stock port, the restatement, and the memory

**Status:** measured 2026-11-23; **stopped at §1.1**. One §7 condition has also fired: **the platform names no package** (§6.1).
- No model, script or firmware file is changed on this branch. `tests/1301/` holds the measurement harness and a scratch prototype.
- The table is `model/ds12117-rev10-table9.tsv` (its own document is `TVI-WO-1301.ds12117.md`).
- §6 lists what the architect is asked to acknowledge or rule.

**Pages** are ST RM0433 Rev 9 (sha256 `672a9433…`), chapter 11, unless stated. They were checked against the table of contents: `GPIOx_MODER` and `GPIOx_OTYPER` p541; `OSPEEDR`, `PUPDR` p542; `IDR`, `ODR` p543; `BSRR` p544; `LCKR` pp544–545; `AFRL` p545; `AFRH` p546; the map (Table 95) pp547–548.

## 1. Sources

| What | Identity | How |
|---|---|---|
| Image and Renode | `aethelgard-tools@sha256:1e0ab105…` (Aethelgard `main`'s `container/IMAGE`), Renode `1.17.0+20260907gitf1dd1b4af.aet1605` | `~/bin/capped-run`, `nice -n 10`, one container at a time, `--network none` for every run (the CLI build fetched crates, as 1150's and 1300's did) |
| Aethelgard | `main` `1c32c8ea0069fba5ba1dc7a3ed7e126b20ac540c`, exported with `git archive` | CLI and `aethelgard-bridge` built in the image into the worktree's untracked `target-aet/` |
| ArduPilot | the held ELF `a799dfb6…dab1ef`, `sentinel-02-own.json`, `--foreign 5000ms` | `spike/1150/harness/capture-foreign.sh`, unchanged. Verdict `pass`; the emitted platform's sha256 `3927f28a…` (as 1150 (v) and 1300) |
| The generated firmware | 14 specifications: 1100's 13 and `sentinel-02-bench-hse8` | `tests/1301/capture-gen.sh`: 1100's watcher, reached by `generate`, `build` and `simulate --for 100ms` instead of `verify`, so Kani does not run. `verify` boots the same 100 ms (`RunFor "0.1000000005"` in 1300's captures). All 14 verdicts `pass` |
| The stock port | `STM32_GPIOPort.cs` sha256 `16ffaed5…`, from the same renode-infrastructure tree whose `STM32H7_RCC.cs` is 1300's pinned `7f10d491…` | read by line; sealed-ness by reflection in the pinned build (§3) |
| The stock platform | `stm32h7.repl` `b85cb4af…`, `stm32h753.repl` `13c5571e…`, `nucleo_h753zi.repl` `979d7175…` | copied out of the image |
| Each boot, measured | `tests/1301/replay-1301.sh` (from 1300's `accept`) | the prototype (§4) on all 11 ports, recorded, tracing every access; `tests/1301/fields.py` reads the traces |

**Control: the fresh writes reproduce the committed evidence.**
- ArduPilot's GPIO writes give 81 rows by port and offset. 77 are identical to `spike/1150/data/writes-ap.tsv`. The other 4 are `BSRR`, where 1150 keyed the two 16-bit halves at `0x18` and `0x1A` and I key both at `0x18` (2,371 + 2,387 = 4,758; 2,353 = 2,353).
- Each of the 13 boards in `spike/1100/data/writes.tsv` writes the same GPIO sequence, value for value. `sentinel-02-bench-hse8` is newer than 1100.

## 2. The field table

Every access either firmware makes to a GPIO port. **There are no cold writes to GPIO** (no `pc: none` access in any of the 15 boots).

**Widths.**
- Every access is 32-bit, except ArduPilot's `BSRR`. It writes 16-bit halves: 2,371 to `BS` (offset `0x18`) and 4,740 to `BR` (offset `0x1A`).
- RM0433 allows it: "The peripheral registers can be written in word, half word or byte mode" (p541), and the ports "have to be accessed as 32-bit words, half-words or bytes" (p531).
- The exception is `LCKR`: "Only word access (32-bit long) is allowed during this locking sequence" (p544).

| Register (page) | The stock port (`STM32_GPIOPort.cs`) | Silicon's reset; the stock reset | ArduPilot writes | The generated firmware writes |
|---|---|---|---|---|
| `MODER` (p541) | enum fields, stored; `10` connects the pin's AF receiver (L141–145, L161–165) | `0xABFFFFFF` (A), `0xFFFFFEBF` (B), `0xFFFFFFFF` (others), i.e. analog. **Stock: 0 (input) on every port**: the stock `.repl` passes no `modeResetValue` | `00`, `01`, `10`, 4,867 writes; reads it 4,918 times | `00`, `01`, `10`, `11` (`11` once: PF3, `d1-drv8876`) |
| `OTYPER` (p541) | **16 tagged flags** (L166–184): a write is dropped, and a read gives 0 | 0 | `OT6`, `OT7` = 1 on PB6/PB7 (I2C1); reads it 4,916 times | `OT` = 1 on PB6/PB7 (12 boards) and PB10/PB11 (3 boards) |
| `OSPEEDR` (p542) | enum fields, stored; no effect | `0x0C000000` (A), `0x000000C0` (B), 0. **Stock: 0** | `10` on every pin of every port (board init, PK8–PK15 included), `01` on PA5/PA7 | `00`, `01` (PA5, PA7) |
| `PUPDR` (p542) | enum fields, stored (`11`, reserved, too); no effect on `IDR` | `0x64000000` (A), `0x00000100` (B), 0. **Stock: 0** | `01` on PA9, PA10 | `01` on PA13, PA15, PB4; `10` on PA14 (14 boards) |
| `IDR` (p543) | read-only; returns the **output** state `State`, not the pin's input (L195–198) | input | read 58 times, all GPIOB, reading `0xFFFF` | read once (`d1-drv8876`) |
| `ODR` (p543) | stored as `State`; drives the pins' connections (L199–202) | 0 | `0xFFFF` once to each of the 11 ports (board init) | none |
| `BSRR` (p544) | `BS` then `BR` field callbacks on `State` (L203–210). **If both are set for a pin, BR wins** (measured: `0x00010001` to GPIOB `BSRR` leaves `ODR` = 0 and `GreenLED` off); silicon: "If both BSx and BRx are set, BSx has priority" (p544) | 0 (write-only) | both halves, 16-bit; never both set for one pin | 32-bit; never both set for one pin |
| `LCKR` (pp544–545) | a lock-sequence state machine (L211–266); locks `MODER`, `OSPEEDR`, `PUPDR` and `AFSEL` (L147–155). Not `OTYPER`, a tag | 0 | **never written** | **never written** |
| `AFRL`/`AFRH` (pp545–546) | stored per pin; sets the AF receiver's active function (L267–278, L345–357) | 0 | AF 0, 4, 5, 7 | AF 0, 1, 4, 5, 7 |
| `0x28` | **`BRR`, a bit-reset register** (L279–284, "Source: Chapter 7.4 in RM0090 … STM32F40xxx"); measured: `0x1` written there clears `ODR0` | — (RM0433's map ends at `0x24`, pp547–548) | never written | never written |

**The order, measured.** Every pin either firmware leaves in AF mode was put there with its AF already selected. ArduPilot's board init writes whole words: `OTYPER`, `OSPEEDR`, `PUPDR`, `ODR`, `AFRL`, `AFRH`, then `MODER`. The generated firmware also writes AFSEL first.
- **153 pin end-states** across the 15 boots, **none on a blank cell, none whose AF changed after AF mode began.**
- **ArduPilot's 10 AF pins:** PA5/PA6/PA7 AF5 (`SPI1_SCK`, `MISO`, `MOSI`, p89); PA9/PA10 AF7 (`USART1_TX`/`RX`); PB6/PB7 AF4 (`I2C1_SCL`/`SDA`, p90); PB13/PB14/PB15 AF5 (`SPI2`, p91).
- **The AF rule, recorded on all 15 boots** (every write, every pin): **0 `would-refuse`**.

**Reads, and why they matter.** Both firmwares read-modify-write `MODER`, `OTYPER`, `OSPEEDR` and `PUPDR` after board init (ArduPilot about 4,900 reads of each). On the stock port:
- `MODER`, `OSPEEDR` and `PUPDR` read the stock reset (0) where silicon reads its reset;
- `OTYPER` reads 0 always;
- `IDR` reads the output state.

So the values ArduPilot writes after board init carry whatever the stock port returned. This is `RUL-17`'s case for serving those fields from the guard's copy.

## 3. The stock port, measured

- **Not sealed.** `STM32_GPIOPort` is `public class` (source L22), and `IsSealed = False` by reflection in the pinned build. Its `ReadDoubleWord`/`WriteDoubleWord` are public and not virtual, `[AllowedTranslations(WordToDoubleWord)]`.
- **`RUL-07`'s first form, a subclass that re-implements the three bus interfaces, therefore applies,** as the SPI guard's does. The WO's text says "a wrapper … the way 1300's guard owns the RCC". Asked in §6.2.
- **The stock platform declares all 11 ports** (`stm32h7.repl` L100–184), so the class map's case is cancel and restate (`RUL-09` (b)). Each has `numberOfAFs: 16`, `invertedAFPins` on 7 ports, `[0-15] -> syscfg#N@[0-15]` (EXTI), and a registration `rcc RegistrationPeripheralAlias.GPIOx`.
- **Stock routing by AF exists for the timers.** 12 timer blocks route 102 channel outputs into the ports' AF receivers (`timer1: 0 -> gpioPortA#07@01`), and 10 untyped `gpioPortX:` blocks route pins into the timers' inputs. All 102 agree with DS12117 (ds12117 document §3.2).
- **Also measured, outside this WO's scope:**
  - In the stock platform, a pin with a `k -> timerN@m` update loses its `-> syscfg` EXTI route, because a later connection for a source replaces the earlier one. PB1 is an example.
  - `CombinedInput`, Renode's OR of several sources into one input, carries most timer inputs.

## 4. The restatement and the prototype, measured

- **`tests/1301/gpio-restate.awk`** generates the restatement from the pinned `stm32h7.repl`:
  - each port: `gpioPortX: @none`, then the guard at its address, `as "gpioPortX"`, with the same block, its registration on `tviburiRcc`, its properties and its `-> syscfg` line;
  - each of the 22 untyped blocks that name a port: restated with the guard's variable.
  The connections it emits equal the stock file's, token for token.
- **The prototype** (`tests/1301/proto1301.py` → `GpioProto1301.cs`, 518 lines):
  - a transparent subclass of `STM32_GPIOPort` with all three widths;
  - the full table compiled in (2,688 cells);
  - 161 allow-list rows in the RCC guard's `R(...)` shape, so the compilation costs what the guard's would;
  - the AF rule evaluated in recorded mode;
  - the "which signal" query.
- **On Tviburi's Nucleo board** (`smoke`, against `smoke-stock`; the readout is `tests/1301/checks.py`):
  - all 11 addresses hold the guard under the stock name, and no stock `STM32_GPIOPort` instance remains;
  - following each `CombinedInput` to its destinations, **every port's outbound destinations, every timer's AF routes into the ports, and the children (the LEDs, the button) equal the stock board's**;
  - `GreenLED` follows `BSRR` through the guard;
  - the RCC guard's registry maps `GPIOA`…`GPIOK` to the guards (59 entries, as 1300's).
  **The RCC restatement's untyped `gpioPortX: @ { … tviburiRcc … }` updates bind to the cancelled entries, harmlessly:** the guard's own declaration carries the registration.
- **On the consumer's 15 platforms:**
  - ArduPilot's boot block is identical to today's one file's, and differs from stock only in 1300's accepted `AHB2ENR` (`0xE0000040`).
  - Its console, minus records, differs from today's one file's in nothing but the `include` line.
  - All 14 generated boots' boot blocks are identical to stock, and so are their motor-observer lines (`pa8obs` and on: timer, then pin, then LED).

## 5. The one-file overlay's memory on ArduPilot's full boot

ArduPilot's 5 s foreign boot, replayed in one container, the modes interleaved, peak tree `VmRSS` as 1300 measured it:

| Mode | What compiles | Peak (KiB), each run | Under 1,048,576 by |
|---|---|---|---|
| `pure` | stock | 858,264; 868,484 | — |
| `base` | today's one file (the SPI and RCC guards, the release built from this branch) | 968,736; 968,804; 977,852 | 69–78 MiB |
| `cost` | today's one file **plus the GPIO prototype**, 11 ports restated | **997,900; 1,020,420; 1,028,332; 1,032,992** | **15–49 MiB** |

- **The GPIO guard costs about 29–55 MiB over today's one file,** in one compilation. The `trace` run, which also logs, peaked at 1,020,504.
- **Not a stop: the peak does not exceed 1,048,576 KiB.** But the worst margin is 15 MiB, and 1300a adds to the same file.
- **What could shrink it** (none measured):
  - the table as one string, parsed at load, instead of 2,688 object initialisers;
  - the allow-list as a short table of per-register rules, instead of 161 rows;
  - a single port class with no per-port state beyond the copies.

## 6. For the architect: what to rule, and what to acknowledge

### 6.1 Stop (§7): the platform names no package

- **No package anywhere in the platforms.** Not in stock `stm32h753.repl`, `stm32h7.repl`, `nucleo_h753zi.repl`, Tviburi's board, or any of the consumer's 15 emitted platforms.
- **Only the consumer's specification names the part:** `"mcu": {"part": "STM32H753ZITx"}`. ST's `STM32H753ZITx.xml` (Aethelgard `1c32c8ea`) gives `Package="LQFP144"`, 114 I/O pins.
- **Which pins a package bonds is a second DS12117 transcription** (the pin/ball definition table and its package columns). It is not started.
- **A finding for "configured":** ArduPilot's board init writes `OSPEEDR` = `10` and `ODR` = `0xFFFF` to every pin of all 11 ports, including PK8–PK15, which exist on no package (Table 9 lists PK0–PK7). If "configured" means any write, the package rule refuses ArduPilot's boot on a valid path.
- **To rule:**
  - where the package comes from: a property on the guards' entries, emitted by the consumer from `mcu.part`, or another source;
  - what "configured" means: `MODER` output or AF, measured as above;
  - whether to transcribe the bonding table now.

### 6.2 The form

- **(a)** A subclass, `RUL-07`'s form for an unsealed model, which I recommend. It keeps the stock connections, `OnGPIO`, `GetLocalReceiver` and the parent registrations without forwarding.
- **(b)** A wrapper, as the WO's text says. It would have to forward the port's `Connections`, its local AF receivers (102 timer routes) and its children.

### 6.3 The fields given values the model does not implement (acknowledge by name, or rule refused)

| # | Field, value | Who writes it | The stock port | Proposed |
|---|---|---|---|---|
| A1 | `OTYPER.OTy` = 1 (open-drain) | both (PB6/7, PB10/11) | a tag: dropped, reads 0 | acknowledged: no electrical model (WO: not the pins' electrical behaviour); the guard keeps a copy and serves reads |
| A2 | `OSPEEDR` = `01`, `10` | both | stored, no effect | acknowledged, inherited; no timing model |
| A3 | `PUPDR` = `01`, `10` | both | stored, no effect on `IDR` | acknowledged, inherited; `11` (reserved, p542) refused |
| A4 | `MODER` = `11` (analog) | generated (PF3, `d1-drv8876`) | stored; nothing analog | acknowledged (WO: "analog mode beyond the allow-list" is out of scope) |
| A5 | `MODER`, `OSPEEDR`, `PUPDR` reset values | — | 0 where silicon is analog, and A/B differ (p541–542) | the guard's copies reset to silicon's and are served on read (`RUL-17`). This changes the consumer's boot block (`MODER` reads, as `AHB2ENR` did in 1300) |
| A6 | `IDR` reads | ArduPilot (58, GPIOB) | the output state, not the input | a read rule to rule: served by the stock port (inherited, acknowledged), or refused. No pull model exists either way |
| A7 | `BSRR` with BS and BR set for one pin | nobody | BR wins; silicon BS (p544) | the guard gives BS priority (a cited model), or refuses `unmodeled` |
| A8 | `LCKR`, any write | nobody | modeled partly | **refused `unmodeled`** ("LCKR … modeled or refused"; pp544–545). Modelling waits for a firmware that writes it |
| A9 | offsets `0x28`–`0x3FF` | nobody | `0x28` is a stock `BRR` from RM0090 | refused `unmodeled`: no register on the H7 (pp547–548) |
| A10 | sub-word writes | ArduPilot (`BSRR` halves) | translated through a 32-bit read and write | the guard takes all three widths. `BSRR`'s half is written as that half (write-only, reads 0), and every other register is merged into its copy |

### 6.4 The ST comparison: rulings asked (ds12117 document §3.1)

- **D1:** AF15 `EVENTOUT`. DS gives every pin a function; ST lists it as a mode.
- **D2:** six AF0 system functions in DS only.
- **D3:** `DSIHOST_TE` at AF13 on PA15, PB11 and PJ2 in ST's shared H747 GPIO file only. With DS they are `predicted-fault`.
- **D4:** 189 alias rows (the map is committed).
- **D5:** the `SPDIFRX1_INn` off-by-one, 8 rows.
- **As a CI test:** the comparison reads ST's files from an Aethelgard export. To run in CI it needs ST's two files committed here with their BSD-3 licence (a fixture pinned by the Aethelgard commit, as `spike/1150/fixtures/aet-7dec1d7b/`). It also needs a verdict it can fail on: a disagreement not in a ruled list.
- **`RUL-14`:** the comparison and the generators are Python, as 1300's `allowlist.py` is. Say if a committed test must be Rust.

### 6.5 Outside my fence, needed by the build

- **`scripts/tviburi-spike`:** its check names `spi1 rcc`. With 11 guarded ports, the records contract needs 11 more witnesses and 11 more end records per run. That multiplies 1300's teardown flake exposure (FEEDBACK §5.5) by 6.5. Grant the names line, or rule that the ports share one record source.
- **`model/tviburi-nucleo-h753zi.repl`:** the board's LEDs and button are registered on the ports and connected to them, so they must be restated onto the guards (measured: without it the load fails at E39, and Renode exits 0). That line is in the fence ("the board description's GPIO entries"); I note it for 1300a, who shares the file.

### 6.6 The consumer side (Aethelgard Phase 2), measured, not changed

Each consumer platform refers to the ports in up to three ways, and each must name the guard's variable:
1. **Untyped `gpioPortX:` blocks** (all 15 platforms: the chip selects `0 -> spi1mux@0`, the motor pins' observers). This is the n3 trap: on the cancelled name they bind with no error.
2. **Children registered on a port:** `pa8obs: Miscellaneous.LED @ gpioPortA 8` (9 platforms).
3. **Connections into a port:** none in the 15 today.

`replay-1301.sh` moves (1) after the restatement and repoints (2).

### 6.7 The bench row, for the build

**`TVI-GPIO-AF`, `predicted-fault`.** On a NUCLEO-H753ZI:
- **Procedure:** PA5 in AF mode with AF6 (`-` on p89), SPI1 started.
- **Confirms:** no SCK on PA5.
- **Refutes:** SCK on PA5.
