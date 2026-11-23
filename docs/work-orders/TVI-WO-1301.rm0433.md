# TVI-WO-1301 §1.1 — the GPIO field table measured against both firmwares, the stock port, the restatement, and the memory

**Status:** measured 2026-11-23; **stopped at §1.1**. **Two §7 conditions have also fired:**
- **the platform names no package** (§6.1);
- **the one-file peak exceeded 1,048,576 KiB once in 20 runs** on ArduPilot's full boot with the GPIO prototype (§5).
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
- Every access is 32-bit, except ArduPilot's `BSRR`, and one 32-bit **read** of `BSRR` by `d1-drv8876` (it reads 0). It writes 16-bit halves: 2,371 to `BS` (offset `0x18`) and 4,740 to `BR` (offset `0x1A`).
- RM0433 allows it: "The peripheral registers can be written in word, half word or byte mode" (p541), and the ports "have to be accessed as 32-bit words, half-words or bytes" (p531).
- The exception is `LCKR`: "Only word access (32-bit long) is allowed during this locking sequence" (p544).

| Register (page) | The stock port (`STM32_GPIOPort.cs`) | Silicon's reset; the stock reset | ArduPilot writes | The generated firmware writes |
|---|---|---|---|---|
| `MODER` (p541) | enum fields, stored; `10` connects the pin's AF receiver (L141–145, L161–165) | `0xABFFFFFF` (A), `0xFFFFFEBF` (B), `0xFFFFFFFF` (others), i.e. analog. **Stock: 0 (input) on every port**: the stock `.repl` passes no `modeResetValue` | `00`, `01`, `10`, 4,867 writes; reads it 4,918 times | `00`, `01`, `10`, `11` (`11` once: PF3, `d1-drv8876`) |
| `OTYPER` (p541) | **16 tagged flags** (L166–184): a write is dropped, and a read gives 0 | 0 | `OT6`, `OT7` = 1 on PB6/PB7 (I2C1); reads it 4,916 times | `OT` = 1 on PB6/PB7 (12 boards) and PB10/PB11 (3 boards) |
| `OSPEEDR` (p542) | enum fields, stored; no effect | `0x0C000000` (A), `0x000000C0` (B), 0. **Stock: 0** | `10` on most pins, PK8–PK15 included (board init: `0xAAAAAAAA` to port K; not PA0, PA1, PA5–PA7, PA9, PA10, PB6, PB7, PB13–PB15), `01` on PA5/PA7 | `00`, `01` (PA5, PA7) |
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
- **The AF rule, recorded on all 15 boots** (every write, every pin): **0 `would-refuse`**. `fields.py`'s own evaluation at every write agrees.
- **The rule does fire** (`tests/1301/smoke/afrule.resc`, on Tviburi's board):
  - PA5 with AF6 (`-`, p89), then `MODER` = 10, gives one `would-refuse` naming `PA5 AF6`, `GPIOx_MODER`, p89;
  - AF5 (`SPI1_SCK`) gives none;
  - PA6 put in AF mode first, on AF0 (`-`), gives one at the `MODER` write;
  - then AF5 (`SPI1_MISO`) gives none.
- **The port clocks (for 1300a's lock, the architect's 17:16Z note):** in `traceclk` mode the GPIO accesses are logged in order with `RCC_AHB4ENR`'s writes. **No access to a port while its `GPIOxEN` is clear:**
  - ArduPilot: 0 of 56,117; it writes `AHB4ENR` = `0x7FF`, all 11 ports, before its first GPIO access;
  - the generated firmware: 0 on all 14 boards (`0x3`, `0x7`, `0xB`, `0x17`, `0x23` as they go).
  So the GPIO guard can refuse an access to an unclocked port without refusing either firmware. With 1300a's lock, though, the bus drops that access before the guard sees it, as for SPI1. **Proposed:** the GPIO guard does not add its own rule for it.

**Reads, and why they matter.** ArduPilot read-modify-writes `MODER`, `OTYPER`, `OSPEEDR` and `PUPDR` after board init (about 4,900 reads of each). The generated firmware reads `MODER`, `OSPEEDR`, `AFRL` and `AFRH`, never `OTYPER` or `PUPDR`. On the stock port:
- `MODER`, `OSPEEDR` and `PUPDR` read the stock reset (0) where silicon reads its reset;
- `OTYPER` reads 0 always;
- `IDR` reads the output state.

So the values written after a read carry whatever the stock port returned. This is `RUL-17`'s case for serving those fields from the guard's copy.

**Measured again with the ports reset to silicon's values** (`replay-1301.sh tracesi`: the stock constructor's `modeResetValue`, `outputSpeedResetValue` and `pullUpPullDownResetValue`, p541–542, emitted by `gpio-restate.awk resets=1`), on all 15 boots:
- **The AF rule still fires on no boot** (the prototype, and `fields.py`'s own count at every write).
- **The values change:**
  - the generated firmware now writes `MODER` = `11` on most untouched pins, carried from the analog reset (stock reset: only PF3, once);
  - it writes `OSPEEDR` = `11` on PA13 and PB3, the reset's "very high";
  - ArduPilot's later read-modify-writes carry the reset pulls of PA13–PA15 and PB4 (`PUPDR` `01`/`10`).
  So the allow-list must take `MODER` `11` and `OSPEEDR` `11` on a valid path (A2, A4 below).
- **The consumer's boot block changes on every board** (`sysbus ReadDoubleWord` of `MODER`, `AFRx` and `PUPDR` of A, B and others):
  - ArduPilot: `PUPDR` A `0x00140000` → `0x64140000`;
  - each generated board: `MODER` A and B, for example `sentinel-03` `0x002AA8A1` → `0xABEAABAD` and `0xA9A0A000` → `0xA9AFAEBF`.
  Every value that changes is silicon's reset carried through, so A5 is a consumer-visible change, as 1300's `AHB2ENR` was.

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
- **Phantom inputs.** A cancelled port's connections survive as inert inputs of Renode's `CombinedInput` (an OR; the cancelled port is never built, so they never drive). The guard reaches each destination through the combiner. Measured with the RCC restatement first and the GPIO restatement after it; **no other order was measured.**
- **On the consumer's 15 platforms:**
  - ArduPilot's boot block is identical to today's one file's, and differs from stock only in 1300's accepted `AHB2ENR` (`0xE0000040`).
  - Its console, minus records, differs from today's one file's only in the `include` line and the 11 ports' class names in the `peripherals` listing.
  - All 14 generated boots' boot blocks are identical to stock, and so are their motor-observer lines (`pa8obs` and on: timer, then pin, then LED).

## 5. The one-file overlay's memory on ArduPilot's full boot — **the bound was exceeded once**

ArduPilot's 5 s foreign boot, replayed with the modes interleaved, peak tree `VmRSS` as 1300 measured it. Two sessions: 9 runs at 17:00Z and 24 at 17:16Z.

| Mode | What compiles | Runs | Peak (KiB): min, median, max |
|---|---|---|---|
| `pure` | stock | 2 | 858,264; —; 868,484 |
| `base` | today's one file (the SPI and RCC guards, the release built from this branch) | 11 | 958,432; 968,804; 983,156 |
| `cost` | today's one file **plus the GPIO prototype**, 11 ports restated | 20 | 983,288; 1,019,896; **1,065,516** |

- **One `cost` run of 20 peaked at 1,065,516 KiB, 16,940 KiB over the bound,** at host load 4.22 (the last of the second session). The next highest is 1,036,712. Every run, that one included, finished the boot (`AETHELGARD-BOOT-END` in all 24 consoles of the second session).
- **§7 stop:** "The one-file peak exceeds 1,048,576 KiB on ArduPilot's full boot." I report it as fired: the bound is a per-run reaper limit in the consumer, so one run in twenty over it is a boot the consumer would kill.
- **What the guard costs:** about 50 MiB over today's one file at the median (1,019,896 against 968,804), in one compilation.
- **What could shrink it** (none measured):
  - the table as one string, parsed at load, instead of 2,688 object initialisers;
  - the allow-list as a short table of per-register rules, instead of 161 rows;
  - one port class with no per-port state beyond the copies.
- **Load matters:** `base` and `pure` spread by about 25 MiB and 10 MiB across runs too. 1300a adds to the same file.

## 6. For the architect: what to rule, and what to acknowledge

### 6.1 Stop (§7): the platform names no package

- **No package anywhere in the platforms.** Not in stock `stm32h753.repl`, `stm32h7.repl`, `nucleo_h753zi.repl`, Tviburi's board, or any of the consumer's 15 emitted platforms.
- **Only the consumer's specification names the part:** `"mcu": {"part": "STM32H753ZITx"}`. ST's `STM32H753ZITx.xml` (Aethelgard `1c32c8ea`) gives `Package="LQFP144"`, 114 I/O pins.
- **Which pins a package bonds is a second DS12117 transcription** (the pin/ball definition table and its package columns). It is not started.
- **A finding for "configured":** ArduPilot's board init writes `OTYPER`, `OSPEEDR` (`0xAAAAAAAA` on port K), `PUPDR`, `ODR` = `0xFFFF`, `AFRL`, `AFRH` and `MODER` (input) for all 11 ports, PK8–PK15 included, which exist on no package (Table 9 lists PK0–PK7). If "configured" means any write, the package rule refuses ArduPilot's boot on a valid path.
- **The pins ArduPilot puts in output or AF mode:** 12 of them. PA0 and PA1 are output (the chip selects); PA5 is output and AF; PA6, PA7, PA9, PA10, PB6, PB7, PB14 and PB15 are AF; PB13 is AF and output. All are ports A and B, which LQFP144 bonds in full (from memory; the bonding table is not transcribed).
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
| A2 | `OSPEEDR` = `01`, `10`, and `11` (carried from silicon's reset on PA13, PB3) | both | stored, no effect | acknowledged, inherited; no timing model |
| A3 | `PUPDR` = `01`, `10` | both | stored, no effect on `IDR` | acknowledged, inherited; `11` (reserved, p542) refused |
| A4 | `MODER` = `11` (analog) | generated (PF3 under the stock reset; most untouched pins under silicon's) | stored; nothing analog | acknowledged (WO: "analog mode beyond the allow-list" is out of scope) |
| A5 | `MODER`, `OSPEEDR`, `PUPDR` reset values | — | 0 where silicon is analog, and A/B differ (p541–542) | the guard's copies reset to silicon's and are served on read (`RUL-17`). **Measured (§2): this changes the consumer's boot block on all 15 boards** |
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

### 6.7 The memory stop (§5)

- **Raise the bound, or shrink the guard:** the consumer's decision (`RUL-09` (a)), or the three reductions in §5 measured first.
- **Or split:** a guard for only the ports the boards use.

### 6.8 The bench row, for the build

**`TVI-GPIO-AF`, `predicted-fault`.** On a NUCLEO-H753ZI:
- **Procedure:** PA5 in AF mode with AF6 (`-` on p89), SPI1 started.
- **Confirms:** no SCK on PA5.
- **Refutes:** SCK on PA5.

## 7. The rulings (architect, 2026-11-23T17:52Z), and what the build does with each

| Item | Ruling | The build |
|---|---|---|
| Stop 1, the package | The package is a property of the guard in the platform description, cited (`RUL-10`). A pin the declared package does not bond, set to output or AF (`MODER` 01 or 10), is `predicted-fault`, naming the pin, the package and the page. Input and analog writes pass. With no package declared, the rule does not run, and the witness says `package: none` | Tviburi's board declares the Nucleo's package. The bonding comes from DS12117's pin/ball table, transcribed for that package and cited by page |
| Stop 2, the memory | Measure the reductions on the combined one file (1300a's branch plus GPIO), at least 20 runs under load like CI's. Peak, median and margin. Under 1,048,576 KiB in every run, with at least 32 MiB to spare, or stop again with what each reduction buys | §8, before any guard code |
| Form | A subclass (`RUL-07`): the stock port is not sealed | `Tviburi.GuardedSTM32_GPIOPort`, one class for all ports |
| A1–A10 | As classified. A5: serve silicon's reset values (invariant 1); the consumer's boot block changing is expected; MODER 11 and OSPEEDR 11 acknowledged on the valid paths. BSRR with both halves set: model RM0433's set priority (p544), or refuse as `unmodeled` naming the stock defect; never pass it | as ruled |
| D1–D5 | DS12117 is the guard's authority. Each ST disagreement is a finding. D3 and D5 also go to FEEDBACK as rows for Aethelgard | `tests/1301/st-ruled.tsv` lists them; `tools/tviburi-afcheck` fails on anything else |
| The comparison in CI | Yes, in Rust beside `tools/` (`RUL-14`); ST's two files committed with their BSD-3 notice and source commit | `tools/tviburi-afcheck`, `tests/1301/fixtures/st-open-pin-data/`, the suite's `afcheck` |
| Records | One record source for the family if it fits the existing grammar; a new field is a stop; otherwise per port | see §8 |
| AFRH | The "errata" was from the architect's research note, from memory. Record RM0433 p546 ("x = A to J") against p548 ("A to K") as a finding, and close the row | closed (`TVI-WO-1301.ds12117.md` §4) |
| 1300a's lock | The GPIO guard adds no rule for an unclocked port | none added |

## 8. The reductions on the combined one file, as ruled — **stopped again**

**Method** (`tests/1301/memory-1301.sh`, 17:53–18:20Z):
- **The base is 1300a's latest tree.** A read-only copy of its `git archive` export at `~/.cache/tvi-1300a/m3src` (17:44Z): `GuardedSTM32H7_RCC.cs` `b8f89469…`, `GuardedSTM32H7_SPI.cs` `69809b7e…`, `Refusal.cs` `bb1ab829…`. Its release was built in the image, and to that one file each variant's GPIO C# was appended, `using` lines hoisted.
- **The variants** (`tests/1301/proto1301.py`):

| Variant | What it adds |
|---|---|
| `base` | nothing: 1300a's file alone |
| `R` | the 11 ports restated as the stock class, no new C# |
| `Vmin` | the guard subclass alone (three widths, the records) |
| `V0` | the §1.1 prototype (2,688 object initialisers, 161 rows) |
| `V1` | V0 with the table as one string, parsed at the first lookup |
| `V2` | V1 with the allow-list as 10 per-register rules |

- **Each restated with silicon's reset values** (A5, as ruled).
- **The runs:** 20 rounds of the six, interleaved, 120 runs. Each replay was pinned to 4 CPUs (`taskset -c 0-3`, a GitHub runner's 4 vCPUs) on Furnace, which other builders shared at host load 4 to 31.
- **Every run booted to `AETHELGARD-BOOT-END`.** Every GPIO run emitted 14 witnesses and loaded with no error.

**Results** (peak tree `VmRSS`, KiB; the bound is 1,048,576):

| Variant | All 20: median | All 20: max | Over the bound | The runs the host did not slow (wall ≤ 11.5 s): n, median, max, margin |
|---|---|---|---|---|
| `base` | 964,216 | 1,910,576 | 4 | 16; 960,486; 986,688; 60.4 MiB |
| `R` | 978,650 | 2,196,616 | 6 | 11; 965,316; 981,140; 65.9 MiB |
| `Vmin` | 975,706 | 1,716,308 | 3 | 15; 972,160; 1,027,656; 20.4 MiB |
| `V0` | 987,852 | 2,105,876 | 4 | 14; 985,580; **1,052,820**; −4.1 MiB |
| `V1` | 979,668 | 1,984,084 | 2 | 17; 979,320; 1,036,448; 11.8 MiB |
| `V2` | 971,240 | 1,740,464 | 6 | 13; 970,784; 1,006,616; **41.0 MiB** |

**What the runs show:**
1. **The peak follows contention, not only the code.**
   - Peak against wall time: correlation 0.978. A peak is one process's (`VmHWM` ≈ the tree's).
   - Of the 34 runs the host slowed (wall over 11.5 s, against about 10 s), **24 went over the bound**, `base` included: 1,910,576 KiB at 30 s wall, with no GPIO code. Of the 86 it did not slow, 1 went over (`V0`).
   - So **no variant, and not 1300a's file alone, stays under 1,048,576 KiB in every run on a contended host.** By the ruling, that is a stop.
2. **What each reduction buys,** at the median of the unslowed runs, over `base` (960,486):

| Variant | Over `base` | What it adds |
|---|---|---|
| `R` | +4.7 MiB | the restatement |
| `Vmin` | +11.4 MiB | a guard class at all |
| `V2` | +10.1 MiB | the string table and per-register rules |
| `V1` | +18.4 MiB | |
| `V0` | +24.5 MiB | |

   The 2,688 object initialisers cost about 6 MiB more than the string (V0 against V1). The 161 rows cost about 8 MiB more than 10 rules (V1 against V2). **`V2` is the only GPIO variant whose unslowed runs keep 32 MiB** (41.0 MiB, n = 13).
3. **Contention here is heavier than CI's.** The 4 pinned CPUs were shared with the rest of the host. A GitHub runner's 4 vCPUs are not shared, so the unslowed runs are the closer proxy for CI. A faithful "CI-like" peak needs an unshared 4-CPU host, or the measurement run in CI itself.

**For the architect and Scotty (northstar §6):**
- the bound against host contention, which reaches 1300a's file without GPIO;
- whether `V2`'s unslowed margin (41.0 MiB) is the measure to build on;
- where a CI-like peak is measured.

**My lean:** build with `V2`'s shape, and measure the peak in CI or on an unshared 4-CPU host before READY.
