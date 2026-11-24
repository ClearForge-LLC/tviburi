# TVI-WO-1304 — FEEDBACK

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1304-flash-guard`, from `main` `d22dad8`, `main` `b06ba63` merged.

**State:**
- §1.1 was measured and stopped (`TVI-WO-1304.measure.md`), then ruled at 2026-11-24T15:50Z (Q1–Q7).
- The guard is built to the rulings. No §7 stop is open.
- Untouched: `model/Refusal.cs`, the record grammar, the SPI/GPIO/timer guards' C#, the steering documents and Aethelgard. The RCC guard has the one event (§2).
- One PR, left unmerged.

## 1. What was built

| Part | File |
|---|---|
| **The guard:** a subclass of `STM32H7_FlashController` that re-implements the three bus interfaces (`RUL-07`; not sealed, measure.md §2.4) | `model/GuardedSTM32H7_Flash.cs` |
| **The wait-state rule, `TVI-FLASH-WAIT`:** `LATENCY` against the AXI clock (`rcc_aclk` = `sys_ck` / `D1CPRE` / `HPRE`, p395) at the voltage scale in force, by Table 17 (p160, read from the rendered page). Judged at every `ACR` write (its `0x100` alias included) and after every RCC write that can change the clock (§2). `predicted-fault` (the order: p159–161).<br>**VOS unknown** (a `pwr` that reads `VOS` `00`): decided at VOS1, the most permissive scale the twin can know (ruled Q1); the record says so.<br>**`unmodeled`** above 225 MHz (VOS0's `ODEN` cannot be read), or when the clock cannot be computed | the same |
| **The programming-delay rule, `TVI-FLASH-PROG`:** `WRHIGHFREQ` below Table 17's value at a program or erase start (`PG` set, `START`/`FW` with `SER`/`BER`), or at an `ACR` or clock write while `PG` is set ("too tight", p164). One-sided: 11 passes (ruled Q2) | the same |
| **Bank 2 as silicon locks it:**<br>• `TVI-FLASH-KEY`: the keys (p182, p163). A wrong key, a key while unlocked, a key after a wrong sequence, a write to another bank register between the keys (the common registers excepted, Table 20 p172), a key of less than 32 bits.<br>• `TVI-FLASH-LOCK`: a `CR` write that changes a field while locked (pp. 182, 202–206, 226–229), or that sets `LOCK` and changes another bit ("the other bits … do not change", p206, p229).<br>• `PSIZE` served as written (p228; Table 18 p164: no value is an error).<br>• `SR` read-only (p207, p230) | the same |
| **`OPTSR_CUR`** (and `0x11C`) served from `optionBytes`, a restatement property holding the whole word (ruled Q3). An undeclared read is `TVI-FLASH-OPTSR`, `unmodeled`. Option-byte programming is `TVI-FLASH-REG`, `unmodeled`: `OPTKEYR`, `OPTCR`, `OPTSR_PRG`, `OPTCCR` | the same |
| **The allow-list:** 138 rows over 10 registers (every bit 31:0), generated from RM0433 Rev 9 §4.9's fields with the ruled classes. `flash-regen` checks it byte for byte. Every other register refuses by name (`TVI-FLASH-REG`, `TVI-FLASH-READ`) | `tests/1304/fields.py`, `tests/1304/data/rm0433-rev9-flash-fields.tsv` |
| **The restatement:** a generator from the pinned `stm32h7.repl` (`regen` checks it); the board's include, mode and `optionBytes` (the NUCLEO-H753ZI rev V's own word, `0x1BC6AAF0`, read on the bench by Aethelgard `AET-WO-3302`) | `model/stm32h7-flash-restate.{awk,repl}`, `model/tviburi-nucleo-h753zi.repl` |
| The class row | `tools/tviburi-release/pins.tsv` |
| **Bench rows** F1–F5: one per `predicted-fault` rule (`RUL-18`), and F3 for `WRHIGHFREQ` = 11 (ruled Q2). Every result "not run" | `model/bench-checks.tsv` |
| **Cases:** 37, each `flash-<case>` in `tests/spike.sh`'s acceptance group; 29 planted firmware paths, each refusal with its passing twin. The tests' VOS-storing `pwr` is the consumer's script, in the tests' platform only (ruled Q7) | `tests/1304/flash.sh`, `firmware/flash.rs`, `tests/1304/pwr-vos.repl` |
| **Mutants** FL01, FL03–FL41: one per guard added or changed, and one each for the table, the restatement and the RCC event | `tests/mutants.tsv` |
| **The replay:** the flash restatement on a tree that has it. The consumer's untyped `flashController:` block (E29 on the cancelled entry) is turned into `optionBytes` (its `OPTSR_PRG` word) and dropped (ruled Q3) | `tests/1301/replay-1301.sh` |
| `flashController` appended to each guard-name list | `tests/spike.sh` (6), `tests/1300/rcc.sh`, `tests/1301/gpio.sh`, `tests/1302/timer.sh`, `scripts/tviburi-spike`, `tests/mutants.tsv` (G17's text) |
| Citations appended (ten rows, each a comment or a record string, none a read) | `tests/ci/citations.tsv` |

## 2. The RCC guard's one change (ruled Q6)

- **The event:** `public event Action<long, uint> Written` on `GuardedSTM32H7_RCC`, raised at one call site, the last line of `Pass` (`Written?.Invoke(b, value)`): after every write the RCC guard passes, recorded mode's included. A write it refuses in enforcing mode never reaches `Pass`, so nothing changes and nothing is judged.
- **The listener:** the flash guard subscribes when the machine first starts.
  - It judges only the writes that can change the AXI clock: `CR`, `CFGR`, `D1CFGR`, `PLLCKSELR`, `PLLCFGR`, `PLL1DIVR`, `PLL1FRACR`. ArduPilot's other ~26,000 RCC writes in 5 s are enables and resets.
  - It reads the clock back through the RCC guard's own `ReadDoubleWord`, `HseFrequency` and `CsiFrequency`, which log nothing.
- **Nothing else in the RCC guard changed:** 6 lines added (the event, its three comment lines and a blank, and the call).

## 3. Acceptance (WO §2)

- **Raising the clock before `LATENCY`** refuses `predicted-fault` at the clock switch (`flash-clockfirst`). The access is at `0x58024410`, and the record names "the AXI clock at 200000000 Hz at VOS1: Table 17 needs 2", `LATENCY = 0` and Table 17 p160. The same firmware in the right order passes (`flash-order`).
  - Two other ways to raise the clock refuse the same way: by `HPRE` (`flash-prescaler`) and by `D1CPRE` (`flash-d1cpre`).
- **Lowering `LATENCY` under a fast clock** refuses at the `ACR` write (`flash-lowerfast`), and at its alias `0x100` (`flash-alias`).
- **ArduPilot's boot, recorded** (the fixture and AP-m, 5 s, replayed on the branch's release with every guard recorded):
  - **0 would-refuse**, from any guard;
  - observers, the boot block and bank 2's sectors identical to `main`'s.
  - **The flash access log differs only where the rulings say:** 163 / 147 `CR2` reads in `PSIZE2` (served as written, p228), and 4 `SR2` reads (`EOP2` no longer set by ArduPilot's `SR2` write, p230).
  - **Aethelgard's `AETHELGARD-ERASE 10c 6b4`/`7b4` lines read `6a4`/`7a4`** (ruled Q5, an Aethelgard row, §7).
  - Planted, enforcing: `flash-ap` passes. It replays ArduPilot's RCC sequence, its `ACR` = `0x32` with VOS unknown, and an erase and a program of bank 2.
- **The 14 generated boots** (Aethelgard `1cb994c1` captures; nothing relevant changed by `29b3a97d`):
  - The flash access log is identical to `main`'s, every value included. So `OPTSR_CUR` reads the declared word: the consumer's, carried by the replay's translation.
  - Observers, boot blocks and bank 2 are identical, with 0 flash would-refuse.
  - Planted, enforcing: `flash-gen` (hse-crystal-16's PWR, RCC and `ACR` writes, then its `OPTSR_CUR` read) passes.
- **The budget:** +3.4 MiB at the median over `main`, 11 ArduPilot boots a side on Furnace, PASS (§8). CI's run is in §8.
- **CI:** __CI__.

## 4. The adversarial pass (WO §5)

A fresh Opus reviewer read the code, the rulings and RM0433, with no container. **Its bottom line:** in enforcing mode, no false pass on a mainstream path before the first refusal. Its findings, and what was done:

| # | Finding | Done |
|---|---|---|
| A1 | A firmware writing `VOS` `00` on a `pwr` that stores it is decided at VOS1, like the stub; the ledger named only the stub | The `LATENCY` row names both cases ("Reserved (Scale 3 selected)", p310). They cannot be told apart without a name (`RUL-10`), so it stays the ruled Q1 behaviour |
| A2 | A VOS change is judged only at the next `ACR` or clock write, and attributed to it | Named in the `LATENCY` row ("a VOS write is not seen …, judged at the next ACR or RCC clock write, or never"); a row (§7) |
| A3 | A store into bank 1 with `PG1` = 0 lands in the twin | Named in bank 1's rows; a row |
| A4 | `OPTSR_CUR`'s reserved rows said "reads 0"; they serve the image | Row text corrected |
| A5 | `TVI-FLASH-LOCK` stated "silicon ignores the write" as fact; `TVI-FLASH-KEY` claimed a bus error for a key after lock-up | The records now say what is inferred (bench row F5 checks it) and that RM0433 does not say |
| A6 | An RCC-triggered record's `access.offset` is the RCC register's absolute address | Kept, pinned by `flash-clockfirst`; **for the architect** (§6) |
| A7 | `Judge` ran from the RCC path for a guard that failed to load or could not model its platform | It returns at once |
| A8 | Recorded mode: the latch hid a worse mismatch. Recorded state can also diverge from the stock model's | The latch now holds the mismatch's description, so a changed mismatch is recorded again (`flash-worse`). The divergence is stated in the header |
| A9 | Mutants likely to survive: programming while `PG` set, the band edge, VOS2, `D1CPRE`, `keyLost`, the common-register exemption | Cases `wrhrcc`, `edge`, `vos2`, `d1cpre`, `recorded` (keys after a wrong sequence), `keymid` (an `ACR` write between bank 1's keys); mutants FL35–FL41 |
| A10 | RCC writes from the monitor before the machine starts are not judged | A row: `ACR`'s reset `0x37` covers every clock ≤ 225 MHz at every scale |
| A11 | Above 225 MHz at a known VOS2/VOS3 was recorded as "only at VOS0" | The record names the scale in force |
| A12 | The key rule counted common registers as bank 1 (recorded mode) | The registers with a bank 2 alias, which are the common ones (Table 20 p172), are exempt |
| A13 | Invalid PLL configurations (`DIVN1` < 3, the reference or VCO out of range) are computed as clocks | A row for the RCC guard, where `DIVN1` is `Any` |
| A14 | The `FRACN` path is dead: the RCC guard serves `PLL1FRACEN` from a tag | Kept, harmless; a row |
| A15 | The replay's translation is strict about the block's shape | It fails closed (no image: `OPTSR_CUR` refuses); a row |
| A16 | Minor: CONFIG's page; the RCC event split the query's doc comment; `TVI-FLASH-KEY`'s register | p133 added; the event moved above the query's comment; the record names the register written |

**The review also confirmed,** and the guard now models: p206/p229's "When LOCKx changes from 0 to 1, the other bits … do not change". A write setting `LOCK` with another bit changed is a `TVI-FLASH-LOCK` (`flash-locking`). Neither firmware does it: ArduPilot locks in a separate write.

**WO §5's list:**
- **A clock raised by a prescaler** (`flash-prescaler`, `flash-d1cpre`) or by a PLL relock (judged at the `CFGR` switch back to PLL1; the RCC guard refuses a `PLL1DIVR` change while PLL1 runs) is refused at that write.
- **The voltage scale lowered under a fast clock** is a `pwr` write the guard cannot see: A2, a row.
- **`ACR` through its bank 2 alias:** `flash-alias`.
- **A write without the unlock:** `flash-lockedcr`. **A second key out of order:** `flash-badkey`, plus the second-key branch of the same rule.
- **A program to a sector being erased:** the twin's erase is instantaneous, so it cannot arise (a row).
- **An image the firmware can forge:** none. `OPTSR_CUR` is served from `optionBytes` in both modes, and every option-byte write refuses.

## 5. The reduction pass

A fresh subagent read the diff and proposed cuts; I made the ones inside the diff and proved them (the 37 cases and the 41 mutants re-run on the reduced code, §8).

- **The rule list was written twice** in `Write`: once as recorded mode's statements, once as enforcing's `||` chain. It is now one ordered list, which stops at the first refusal unless recorded.
- **The `Access` class** carried (offset, value, width) into `Judge` and `Program`, and every other rule passes them as plain arguments: removed.
- **`Common`, the registers outside the key rule's bank, was `Aliases` minus `0x100`:** the same fact twice. The key rule now reads `Aliases`.
- **The flash restatement generator's header comment** repeated the header it prints: cut to a pointer.
- **Found by the proof, not by reading:** FL41 survived the first run. `keymid` put its key sequence on bank 2, where a common register never counts, because it maps to bank 1's offsets. The case now uses bank 1, and FL41 is killed.

Reduction pass (diff): −21 net · 2 duplicates removed · 1 mechanism removed · 0 dead paths removed · 0 protected kept · 2 findings below.

**Findings (outside the diff, not done):**
1. `tests/spike.sh` holds four copies of one 11-line wrapper (`t_rcc`, `t_gpio`, `t_timer`, now `t_flash`), and four copies of its case-list setup. One `t_guardcase <prefix> <script> <case>` would serve all, about −30 lines. 1303 adds a fifth beside this one, so it is a change for after both merge.
2. The guard-name list has 11 copies: 6 in `tests/spike.sh`, plus `scripts/tviburi-spike`, `rcc.sh`, `gpio.sh`, `timer.sh` and `flash.sh`. This WO appended to each. The roadmap's existing row ("one list, after `1300a`") still stands, now with 11 copies.

## 6. Questions for the architect

1. **A6: the access fields of an RCC-triggered record.** `access.offset` is the RCC register's absolute address (`0x58024410`), on a record whose peripheral is `flashController`; every other record carries an offset within its own peripheral. It is unambiguous (no flash offset reaches `0x1000`), and the PC is the RCC store's. The record grammar is fenced, so the shape is the architect's call.
2. **CI time.** 37 cases and 41 mutants are added to five shards, roughly +5 min per shard at the measured medians. With 1303's guard as well, `1202`'s "room for about two more guards before a 6th shard" is used up.

## 7. Rows and findings

- **For Aethelgard (each a finding, not a change):**
  - Its foreign platforms keep the stock `pwr` stub. VOS is unknown there, so the flash guard decides at VOS1 (Q1). A VOS-storing `pwr` on foreign platforms gives the full rule.
  - Its option-byte declaration should become the `optionBytes` property. The untyped `flashController:` block is E29 on the cancelled entry.
  - Its declared word sets reserved bit 26 = 1 (Renode's `0x0406AAF0` + `BOR_LEV`), where Table 21 and the bench (`0x1BC6AAF0`) have 0 (ruled Q3).
  - Its erase watch (`AETHELGARD-ERASE`) reads `CR2`'s `PSIZE`, now silicon's (`6a4`/`7a4`, ruled Q5).
  - Its flash write hook hides every flash write from the access log on foreign boots.
  - The guard's `INFO` witness on `flashController` makes `flash_controller_access` read "log not whole": the Phase 2 seam (ruled Q5), the same class as `rcc` today.
- **Not seen by the guard:**
  - a store into a flash bank: with `PG` = 0, silicon raises `PGSERR` (p164); over a programmed word, an ECC error later (p161);
  - a VOS write (A2);
  - monitor RCC writes before the start (A10);
  - erase and program time: `BSY`/`QW` read 0, and an erase is instantaneous;
  - a sector being erased while programmed.
- **The flash banks start zeroed** (`MappedMemory`), where silicon's erased cell reads 1 (p161). ArduPilot erases its two storage sectors in its window. Whether it would on a virgin part is from memory.
- **The RCC guard:** invalid PLL configurations computed as clocks (A13); `PLL1FRACEN` from a tag (A14).
- **The replay's translation** of the consumer's block fails closed on any other shape (A15).
- **The records runtime:** `Refusal.cs`'s comment says recorded mode "evaluates no further rule". The flash, timer and GPIO guards evaluate every rule (A8).
- **The teardown race** lost every end record once in this WO's full-suite run (`flash-lockedsame`, which passed on its rerun): the known row.

## 8. Suites, mutants, memory and CI

**On Furnace** (one capped container at a time, `nice -n 10`):
- **The full acceptance suite** (224 cases), at `61eddd9`: every case passed but one. `flash-lockedsame` lost every guard's end record at quit (the teardown race, §7) and passed on its rerun.
- **The 37 flash cases**, at `b1315a1`, the reduced code: 37 passed.
- **The diff-sized mutants**, at `b1315a1`: FL01, FL03–FL41 and G17 (its text gained `flashController`), **41 of 41 killed**. FL02 was replaced by FL35 when the latch changed (A8). FL41 had survived the first run until `keymid` moved to bank 1 (§5).
- **The route inventory** passes (`tests/ci/route.sh`): 304 tracked files, ten citation rows appended. The `unit` case passes.
- **The acceptance replays** (§3): 16 boots, against `main`'s release, at `cb6b7ea`/`7cb5bcd` (the guard's rules as built, before the adversarial fixes, which touch no path either firmware takes).
- **The memory budget** (`tests/1301/ci-memory.sh`, 11 boots a side, at `7cb5bcd` plus the citations): `MEDIANS main 987900 KiB, branch 991364 KiB, difference 3464 KiB (+3.4 MiB)`, PASS. Every branch boot was under 1,572,864 KiB. The §1.1 prototype measured −0.1 MiB.

**CI:** __CI__.
