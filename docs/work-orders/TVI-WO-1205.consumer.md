# TVI-WO-1205 (3): the consumer's checklist for Phase 2

**For:** Aethelgard's Phase 2 work orders, cut from this list in order. Each item says what the consumer must do, where it was found (R = `docs/roadmap.md` line; FB = that work order's FEEDBACK; arch = `docs/architecture.md`), and how Tviburi's release lets the consumer check it. **No check** means the release gives none: a row for Phase 2.

**What the release ships** (`scripts/build-release`): `Tviburi.cs` (the overlay), `MANIFEST` (its sha256 is the release hash), `LEDGER.jsonl`, the constant's template and a tarball. In this repository, not shipped: `tools/tviburi-check` (the reference records check), `tests/1301/replay-1301.sh` (the reference emitter for a foreign board), and the console fixtures.

## A. Pin it and prove which overlay ran

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| A1 | Pin the release by sha256 in its tools image, and record the model hash and the ledger hash in its trace header and verdict | R:190, R:195; arch `RUL-09` | `tviburi-release verify <dir> <hash>`: the manifest, overlay, ledger and constant agree (FB 1200) |
| A2 | Hash the overlay file it `include`s against the manifest, and run Renode with the compiler cache off (`compiler-cache-enabled = False`); with the cache on, a `const` reports the previous release | R:192; arch `RUL-10` (e)–(g); FB 1150 | The manifest's `source` lines; the witness's `model_version` is the release hash (`identity`) |
| A3 | Refuse a run with no load witness, a wrong release constant, or a recorded-mode witness (`mode: "recorded"`). A check that reads only the witness passes a recorded run and an edited model | R:192; arch `RUL-10`, `RUL-19`; FB 1150 | `tviburi-check` does exactly this (`unit`: R05, R07, R12) |
| A4 | Carry the raised memory ceiling, 1.5 GiB (Aethelgard `RUL-13`, `AET-WO-2313`) | R:156, R:44 | The budget job's medians, 941,656 KiB (0.90 GiB) on the fixture (#63's CI) |

## B. Emit the platform from the class map

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| B1 | Apply the manifest's class map in its platform emitter: cancel each listed stock instance and restate it on the guard, leaving no untyped update on the cancelled name. That covers the RCC's 58 registrations (`RUL-07`), and every `timer1:`, `timer8:`, `usartN:`, `rcc:` and `gpioPortX:` update and every `@ gpioPortX` child | R:191; arch `RUL-09` (b); FB 1150 (the `as "<stock name>"` trap); FB 1300 (n3); FB 1301 | `MANIFEST` `class`/`restate`/`retype` lines; `replay-1301.sh` does it for the fixture |
| B2 | Restate only what the manifest lists. Since `1203`, each class row names its stock and consumer instances, and the release refuses any other (`spi4` and twelve timers dropped). Don't assume every instance of a guarded type is guarded | FB 1302, FB 1100a F1, FB 1203 | `MANIFEST` `restate`/`retype` lines (`manifest` case) |
| B3 | Put every RCC property (`hseFrequency`, `csiFrequency`) on the guard's entry, never on the cancelled `rcc:`. Three of the 15 platforms emit it there today (`hse16-trap`) | R:172; FB 1300, FB 1300b | No check in the release: the trap loads silently (row C1) |
| B4 | Register each USART guard on the RCC guard under its own name, or every access refuses `TVI-USART-CONFIG`; and its own `spi1`/`spi2` registrations name the guard | FB 1300b; FB 1300; R:172 | The refusal itself, at the first access |
| B5 | Declare sensors `@ tviburiI2cN`, not `@ i2cN` (E39 on the guarded twin) | R:140; FB 1306 | Renode's load error |
| B6 | Map each guard's `peripherals` kind (`GuardedSTM32F7_USART`, `GuardedSTM32H7_ADC`, …) in its class map, or its untrusted-tool stop fires | FB 1305, FB 1307 | The `class` lines name each guard type |
| B7 | Repoint every `-> dmamux1@N` line its platforms carry (`spi1 DMARecieve -> @37`, `spi2 -> @39`, and `usart1 ReceiveDmaRequest -> @41`, which it must add: the replay's `ACCEPT_USART_DMA` stands in for Aethelgard `4400i`) to the DMAMUX1 guard's variable. Since `1308`, DMAMUX1 is cancelled and restated, so a line left on `dmamux1` binds to the cancelled entry with no error (`RUL-09` (b)) | R:165; FB 1308 §6; FB 1305; FB 1303.measure | `MANIFEST` `restate Miscellaneous.STM32_DMAMUX dmamux1`; `replay-1301.sh`'s DMA lines |
| B8 | Drop its generated platforms' own PWR script (`AET-WO-4240v` D-3 (a)): the cancel removes it, and on the guarded twin the PWR guard wins. Its foreign platforms have no PWR model of their own | R:166; FB 1308 §6 | `MANIFEST` `restate Python.PythonPeripheral pwr`; the PWR guard's witness |
| B9 | Expect three observable changes once the RCC is restated: `sysbus.rcc`'s access-log register names (restored by `1203`'s names), the stock RCC's warnings becoming sourceless (2,403 on ArduPilot's boot), and ArduPilot's `AHB2ENR` boot value changing to silicon's | R:172; FB 1300 | The boot block's `AHB2ENR`; `lognames` |

## C. Declare the board's properties

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| C1 | Declare `hseFrequency` (the crystal) on each HSE-rooted board, on the guard's entry. There is no default: an undeclared or zero crystal refuses `TVI-RCC-HSE` at the HSE start (FB 1305a's "defaults to 8 MHz" is superseded) | FB 1300b (4); FB 1204 P2 | The refusal; the RCC guard's `HseFrequency` |
| C2 | Declare the MCU's `revision`, or the ADCs refuse at load | R:167; FB 1308 §6 (the PWR guard needs it too); R:133; FB 1307 | The ADC witness's `revision` |
| C3 | Declare an analog level for every ADC channel a run reads (`levels: "<ch>:<volts>"`), the battery's included. Without it, the armed run logs 805,530 `TVI-ADC-LEVEL` records and Renode aborts | R:133; FB 1307; FB 1100a F4 | The refusal; no witness field (row) |
| C4 | Declare each board's pulls and external levels from its own solved topology (`external`, e.g. `"PB6:up PB7:up"`); `ACCEPT_EXTERNAL` stands in meanwhile | R:157; FB 1301a | The GPIO witness's `external` |
| C5 | Declare the GPIO `package`, or the unbonded-pin rule does not run | R:148; FB 1301 | The GPIO witness's `package` (`none` today) |
| C6 | For the flash guard: move the option bytes to `optionBytes` (the untyped block is E29), and correct bit 26; `AETHELGARD-ERASE` reads silicon's `PSIZE`; its flash write hook hides flash writes from the access log. `1304`'s "emit its `pwr` override on foreign platforms" is superseded since `1308`: the restated PWR guard answers VOS (B8) | FB 1304; FB 1308 §6 | The flash guard's refusals; no check for the hook (row) |

## D. Run Renode, read the records

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| D1 | Keep its committed `renode.config` synchronous: the teardown race and the dead asynchronous logger after a flush never reach a synchronous run | R:111; FB 1204 R1, F2 | No check in the release (row): `tviburi-check` reads either |
| D2 | Read records in `RUL-11`'s grammar: trim `\r` at either end; anchor `<source>: ` right after the level; a console stamp is 21 bytes and one space, a `logFile` stamp two spaces | R:193; arch `RUL-11` | `tviburi-check` and its committed fixtures (`unit`: R16, R17, R19) |
| D3 | Pass a run only with each guard's witness and end and no refusal; map a refusal to a named gate failure | R:194; R:177; arch `RUL-12` | `tviburi-check`; the witness check (`tests/1301/witnessed.sh`) names every restated instance |
| D4 | Don't treat Renode's `[ERROR]` lines as a failure on their own. Refusals log at Error, witness and end at INFO; the clean-load judgement fails on any error line, so a refusal also reads as a failed load | R:112; FB 1204 R2; FB 0100a; arch `RUL-12` | No check in the release (row, Phase 2 seam) |
| D5 | Make the serial access parser (`serial.rs`:347–391, `logged_by` :510) and the flash access reader skip the guards' INFO witness and end lines | R:107; FB 1305; FB 1304 | Fails closed today |
| D6 | Confirm by a run that a witness line survives its console filter's budget. ArduPilot's stock console is 8,312,549 B against 8 MiB, and recorded mode adds 5.9 MB | R:197; FB 1150 | No check in the release (row) |

## E. Judge accesses, not just records

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| E1 | Treat `pc: "none"` as a harness access. Exclude a refusal its own harness caused only by naming that access, never by a blanket rule. A DMA-caused refusal carries the enabling store's PC | R:196; FB 1150; FB 1300 | The record's `pc` |
| E2 | Not attribute a DMA-moved access to a CPU instruction (`usart1`'s access log stamps a DMA byte with the `EN` store's PC) | R:90; FB 1303, FB 1303a | No check (row) |
| E3 | Not read only `[cpu:]` access lines: writes refused at the clock lock (`Tried to write a locked peripheral`) and writes to unmapped addresses carry none | FB 1100a F10 | No check (row) |
| E4 | Update any line-for-line access-log comparison: the DMA guard names streams 1–7 (`Stream1Configuration`, where stock logged `StreamFIFOControl+0x4`), and the RCC's names come back | R:116; FB 1203 | `lognames` |
| E5 | Not compare stamps: eight stock `spi2` warnings move 200–220 ns | R:121; FB 1300c | — |
| E6 | Keep reading the I2C stock "Unknown slave" line where its judge uses it: the guard keeps it beside its NACK | R:56; FB 1306a | The guard logs it |

## F. Update its own prose and expectations

| # | The consumer must | Source | How it checks |
|---|---|---|---|
| F1 | Drop "the twin never signals NACK" (`docs/architecture.md`, `cli/analog.rs`, `cli/README.md`, `aethelgard-svd-hal`, its ledger's I2C rows). ArduPilot runs about 0.46 s ahead on the guarded twin, and a faulted `sentinel-03` gives `i2c_timeout` where `1306` refused | R:131; FB 1306a | No check (its own text) |
| F2 | Replace its ledger rows that say the twin "logs the write as an unhandled tag" | FB 1307 | No check (its own text) |
| F3 | Know its switchable pull-down is open: a disabled output floats, Tviburi shows it low, and neither sees fitment | R:78 | No check (its own text) |
| F4 | Correct its pin data where it comes from ST's open pin data: `DSIHOST_TE` (from ST's shared H747 file) and `SPDIFRX1_INn` are wrong there; no committed board uses either | R:150; FB 1301 | No check (its own text) |
| F5 | Update its ledger rows that call `TIMINGR`, `DNF` and `ANFOFF` "not stored (measured)" (`ledger.rs`:1133–1191) and `boot.rs`:1043's `unobservable` entry: the I2C guard stores and serves them | FB 1306 §4 | No check (its own text) |
| F6 | Expect the I2C owned instance's WARNING and INFO lines ("Unhandled write …", "Slave address …") to lose their source name; none is parsed today | FB 1306 §4 | No check (row) |

## G. Carried over, not Phase 2 gate items

- **Timer period and first edge** (stock-wrong): Aethelgard's motor witness encodes Renode's convention, so one coordinated work order in both repositories (R:77–78; FB 1302).
- **An undeclared floating input reads 0;** listing them needs a grammar work order that adds a field to `end`, and Aethelgard's read of it (R:158; FB 1204 R3).
- **B4, Aethelgard's own sensor models** under the contract: decided at Phase 2 (R:205, R:212).
- **The ELF source:** if `aethelgard-ardupilot` stops reproducing `a799dfb6…`, committing the ELF is Scotty's call (R:154; FB 1307).
- **Optional:** collect after its `include`, about 24 MiB of 36 is compile garbage (FB 1302).

**Coverage check:** every roadmap line naming Aethelgard or the consumer is cited above or listed as not an obligation in `TVI-WO-1205.FEEDBACK.md` §3. The check is `spike/1205/consumer-coverage.sh`.
