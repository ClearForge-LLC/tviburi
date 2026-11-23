# TVI-WO-0100a §1.1 — the MODF truth table and the condition texts, checked against RM0433 Rev 9

**What was checked:** the rule `TVI-SPI-MODF` and every condition text in `model/GuardedSTM32H7_SPI.cs`, as of the blind build's head `e47e8d6`, with no code changed. Item (7) of the work order.
**Source:** ST RM0433 Rev 9 (October 2026), held on Furnace at `~/.cache/aet-held/rm0433-rev9.pdf`. Its sha256 is `672a9433d6602da224e5215f571beed2be0e683cb025526f4afe9db61c26b6a0`, re-hashed 2026-11-23 and matching `architecture.md` §2.2.
**How it was read:**
- The held text extract (`rm0433-rev9.txt`, sha256 `8e02c0c0…`) was used only to find pages.
- Every page cited below was then extracted from the PDF itself with Ghostscript 10.05.1 `txtwrite`. That ran in the tools image `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…8d1b58`, with `--network none`.
- Each quote was matched mechanically against its page. All 36 matched once the SR reset value was placed on p2224.
- The PDF's page index equals the printed footer (`2165/3329` is PDF page 2165).
- Figure 618 (p2167) was rendered to PNG and read by eye, because its truth table is a layout, not running text.

Reproduce one page:
`gs -q -dNOPAUSE -dBATCH -sDEVICE=txtwrite -dFirstPage=N -dLastPage=N -sOutputFile=- rm0433-rev9.pdf`.
In the image, run it on a copy under `/tmp`: Ghostscript refuses a file on a read-only bind mount with "Permission denied", even though `head` can read it.

All references are to RM0433 Rev 9, chapter 50 "Serial peripheral interface (SPI)".

## 1. The truth table

The rule is evaluated on every 32-bit CR1 write with `SPE=1`. The "Build" column is what `e47e8d6` does. The "Verdict" column says whether the manual agrees with the build.

| # | CFG2 copy and CR1 written | Build | What RM0433 Rev 9 says | Pages | Verdict |
|---|---|---|---|---|---|
| T1 | `MASTER=1, SSM=1, SSIOP=0, SSI=1` | **pass** | With `SSM=1` the SS input is the SSI bit. The SS level "is considered as active if it is equal to SSIOP". In Figure 618, `SS(SSI) <> SSIOP` gives "OK" in master mode. `SSIOP=0` means "low level is active". So `SSI=1` is inactive, and no mode fault occurs. | §50.4.7 p2165, Fig. 618 p2167; §50.11.4 p2221 (SSM, SSIOP); §50.5.2 p2184 | **Agrees, if `SPE=1` is the only time MODF is evaluated. The manual does not say that it is. See D1.** |
| T2 | `MASTER=1, SSM=1, SSIOP=0, SSI=0` | refuse: mode fault | "Mode fault occurs when the master device has its internal slave select logic input (… SSI bit in slave select software mode) pulled low". The effects are: MODF is set, "The SPE bit is forced to zero", and "The MASTER bit is cleared". | §50.5.2 p2184; §50.11.6 p2225 (MODF is SR bit 9) | **Agrees.** The condition text's three effects are each on p2184. |
| T3 | `MASTER=1, SSM=1, SSIOP=1` (either SSI) | refuse: polarity not handled | The manual is **inconsistent**. p2165 and Figure 618 (p2167) make "active" mean equal to SSIOP, so with `SSIOP=1` it is `SSI=1` that faults. p2184 says the fault is the input "pulled low", whatever SSIOP is. | p2165, p2167 vs p2184 | **Agrees, as a refusal.** The manual itself does not decide this case, so the refusal stays. Its citation should name the inconsistency. |
| T4a | `MASTER=1, SSM=0, SSOE=0` | refuse: SS is a pin level | With hardware SS and `SSOE=0`, "If the NSS pin is pulled into an active level in this mode, the SPI enters master mode fault state" and becomes a slave. The twin does not model the pin level. | §50.4.7 p2166; §50.11.4 p2221 (SSM=0: "NSS input value is determined by the NSS pad") | **Agrees.** |
| T4b | `MASTER=1, SSM=0, SSOE=1` | refuse: "a mode fault (SR.MODF) cannot be ruled out" | "When master uses hardware NSS output (SSM=0 and SSOE=1), the NSS signal input is forced to non active state internally to prevent master mode fault error." p2166 says the same: SSOE=1 prevents "any mode fault evaluation". | §50.11.4 p2221; §50.4.7 p2166 | **Disagrees, in the refusing direction.** The manual does rule out the mode fault here. The refusal is still defensible on other grounds: SSOE is a tag that the stock model drops, so NSS output is unmodeled. But the condition text is wrong. See D4. |
| T5 | `MASTER=0` | refuse: slave mode not modeled | MASTER is CFG2 bit 22 (p2222). The refusal rests on the stock model, which is master-only (measured on the blind branch). It makes no claim about the manual. | p2222 | **Agrees** (a twin limitation). |
| T6 | A 16- or 8-bit write overlapping CR1 or CFG2 | refuse | §50.11 gives no access-width rule for CR1 or CFG2; this was searched for and is a search result, not a citation. The refusal rests on the guard, which evaluates 32-bit writes only. | none | **Agrees** (a twin limitation, with no claim about the manual). |

## 2. Bits, offsets and reset values the build uses

| Field | Build | RM0433 Rev 9 | Page |
|---|---|---|---|
| `SPI_CR1` offset, reset value | `0x00`; the guard has no CR1 copy | offset `0x00`, reset `0x0000 0000` | p2215 |
| `CR1.SPE` | bit 0 | bit 0 | p2217 |
| `CR1.SSI` | bit 12 | bit 12. "This bit has an effect only when the SSM bit is set." | p2215 |
| `CR1.IOLOCK` | not used (the blind build quotes it from memory as bit 16) | bit 16 | p2215 |
| `SPI_CFG2` offset, reset value | `0x0C`; `Reset()` sets the copy to 0 | offset `0x0C`, reset `0x0000 0000` | p2221 |
| `CFG2.MASTER` | bit 22 | bit 22 | p2222 |
| `CFG2.SSM` | bit 26 | bit 26 | p2221 |
| `CFG2.SSIOP` | bit 28 | bit 28 | p2221 |
| `CFG2.SSOE` | not used | bit 29 | p2221 |
| `SR.MODF` | named in condition text as bit 9 | bit 9; SR offset `0x14`, reset `0x0000 1002` | p2225; p2224 |
| `IFCR.MODFC` | not used | bit 9 | p2227 |

All of these agree. The register map (Table 429, p2233) is consistent with them, as far as its extracted columns can be read.

## 3. Rows the static table does not show

| # | Question | RM0433 Rev 9 | Pages | Bearing on the build |
|---|---|---|---|---|
| R-SPE | Is MODF evaluated only while `SPE=1`? | **Not stated anywhere.** p2184 and p2166 state the fault for "the master device" with its SS input active, and give no enable condition. Table 423, "SPI wake-up and interrupt requests" (p2189), gives none either. Against that, the manual's own configuration procedure (§50.4.9, p2171) writes CFG2 at step 2 and configures SSI at step 6, all before enabling. If MODF were evaluated while disabled, that order would fault every software-SS master, because SSI resets to 0 (p2215). | p2184, p2166, p2189, p2171, p2215 | **D1.** This is the pass row's unstated premise. |
| R-IOLOCK | What does IOLOCK lock? | "When this bit is set, the SPI_CFG2 register content cannot be modified." SPI_CFG2's own description agrees: it "is write protected when SPI is enabled or IOLOCK bit is set". IOLOCK "can be set when SPI is disabled only else it is write protected". It is "cleared by hardware whenever SPE bit is changed from 1 to 0", and it "is cleared and cannot be set when the MODF bit is set". | p2215, p2221, p2217, p2225 | **D2.** IOLOCK locks **the whole of SPI_CFG2**, and nothing in CR1 except itself. |
| R-SPE-WP | Can CFG2 be written while `SPE=1`? | No. "When SPE=1 … SPI_CFG2 … and the IOLOCK bit in SPI_CR1 are write protected. They can be changed only when SPE=0." | p2217, p2221 | **D3.** |
| R-MODF-SPE | Can SPE be set while MODF is set? | No: "hardware does not allow the SPE bit to be set while the MODF bit is set", and "SPE is cleared and cannot be set when MODF error flag is active". | p2184, p2217, p2225 | Matters only together with D1. Under RUL-08, a mode fault the guard sees already latches the run. |

## 4. Disagreements and open points, for the architect

**D1. The pass row assumes MODF is evaluated only at `SPE=1`, and the manual does not say so. This is in the passing direction, and I am treating it as the §7 stop.**
- **What a literal reading gives:** p2184 says a mode fault occurs whenever "the master device" has its SS input active.
- **Why the passing control path is exposed:** the path `firmware/spi1.rs`, `path="modeled"`, writes `CFG2 = MASTER|SSM` while CR1 is still at reset (`SSI=0`, `SPE=0`). Only then does it write `SSI=1`, and then `SSI|SPE`.
- **What silicon would then do, on that reading:**
  - set MODF and clear MASTER at the CFG2 write;
  - then refuse to set SPE (p2184, p2217).
- **What the twin does:** the guard passes, because its rule looks only at the `SPE=1` write.
- **Why it may be fine:** the manual's own procedure (p2171) is in the same order as the firmware, which implies MODF is not evaluated while the SPI is disabled. But that is an implication, not a statement.
- **Lean:** treat it as undecided, which means refuse (invariant 1).
  - The guard would also refuse when its copies show a master with its software SS input active, (`MASTER=1, SSM=1`, SSI equal to the polarity), at any write since the last reset, not only at the `SPE=1` write.
  - The control firmware would set SSI before writing CFG2. From memory, not verified: ST's H7 HAL sets SSI in CR1 before it writes CFG2.
  - Alternatively, the architect accepts p2171 as sufficient. The rule's citation would then name p2171 as the basis for the gate.
- **Either way this changes the rule the build passes on,** so it needs a ruling before I build.

**D2. IOLOCK: a confirmed false pass. This is fix (1), now cited.**
- **Measured facts** (blind branch FEEDBACK R8): the stock model drops IOLOCK, while the guard's copy follows every CFG2 write.
- **Why that is a false pass:** with IOLOCK set, silicon keeps the old CFG2 (p2215, p2221). Take the sequence `CFG2=MASTER`, then `CR1=IOLOCK`, then `CFG2=MASTER|SSM`, then `CR1=SSI|SPE`. The guard passes it. Silicon is still on hardware SS with `SSOE=0`, where an active NSS pin faults (p2166).
- **The narrowing the WO allows,** now that it is cited: IOLOCK locks exactly the SPI_CFG2 register (p2215, p2221). Proposed `TVI-SPI-IOLOCK`:
  - Once the guard has seen a CR1 write carrying IOLOCK, every later write that overlaps CFG2, at any width, refuses.
  - CR1 writes stay with `TVI-SPI-MODF`, since IOLOCK locks no CR1 field but itself.
- **Question for the architect: when does the latch clear?** Silicon clears IOLOCK when SPE goes from 1 to 0 (p2215). The guard cannot see from records alone whether an earlier `SPE=1` actually took effect.
  - **Lean:** clear the latch only on `Reset()`. A firmware that locks, disables and then reconfigures would get a false refusal, not a false pass.

**D3. A CFG2 write while `SPE=1`: the guard's copy diverges from silicon, in the refusing direction by argument only.**
- **The divergence:**
  - Silicon drops the write (p2217, p2221).
  - The stock model also drops it, with an ERROR (measured, R8).
  - The guard's copy takes it.
- **Why the direction is refusing:** the argument runs as follows.
  - The silicon value retained while enabled is the one the guard checked when SPE went to 1. That value is a passing configuration.
  - Every later `SPE=1` CR1 write is judged on the guard's copy plus its own SSI.
  - So a divergent copy can only cause a false refusal.
- **Why that is not enough:** this is an induction that the guard does not itself check. Adversarial §5 names this route: "a write the stock model drops".
- **Lean:** the same mechanism as D2. A CFG2 write while the guard's last passed CR1 write had `SPE=1` refuses, citing p2217 and p2221. Then the copy never takes a write that silicon drops.

**D4. T4b's condition text claims what the manual denies, in the refusing direction.**
- With `SSM=0, SSOE=1`, RM0433 says the mode fault is prevented (p2221, p2166). This is the blind FEEDBACK's R7.
- **Lean:**
  - Split the refusal. For `SSOE=0`, keep "a mode fault cannot be ruled out", citing p2166.
  - For `SSOE=1`, the text becomes "NSS output (SSOE) is not modeled by the stock model; RM0433 Rev 9 p2221 rules out MODF here". It still refuses, because invariant 1 covers the unmodeled output.
  - Turning it into a pass is R7, which is not in this WO.

**D5. The citation constant is false today.** It says "Section number and revision not verified", and it cites the same text for every branch. The build will replace it with a per-branch citation from §1 above: revision, section and page. This is item (7), and it is not a disagreement.

**Inconsistencies inside the manual,** recorded so that no one "fixes" the guard toward them:
- p2171, step 7, says "Program the IOLOCK bit in the SPI_CFG1 register". p2215 and the register map (p2233) put IOLOCK at SPI_CR1 bit 16. The build follows p2215.
- p2184 says MODF happens when the SS input is "pulled low". p2165 and Figure 618 (p2167) say it happens when the input equals SSIOP. The guard refuses `SSIOP=1` outright, so neither reading can make it pass (T3).

## 5. Stop status

- **The §7 stop "RM0433 contradicts the rule the build passes on": D1.**
  - The literal text (p2184) contradicts the pass on the control path.
  - The procedure (p2171) implies there is no contradiction.
  - I stop here for a ruling.
- **No row of the static table (T1–T6) contradicts the manual in the passing direction.**
- **D2 is the known false pass that fix (1) closes.**
- **D3 and D4 refuse where silicon would not**, so they are false refusals, not false passes.
