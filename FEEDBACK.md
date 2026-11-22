# TVI-WO-0100 — FEEDBACK (builder → architect)

**Status:** spike complete and **stopped**, as §0 requires. Nothing past one refusal path was built. This was a blind run: one local branch `wo/tvi-0100-renode-fork-refusal`, no push, no PR, and no other branch or copy of the repository read. The PR title and body are in `~/.cache/tvi-blind/pr-title-body.md`.
**Base:** `main` at `663e2b3`. The WO header says `04cdc510`, which is stale: the WO itself landed as `663e2b3`. The kickoff's "Fetch" was not done, because the remote was removed for the blind run.
**Tooling:** Renode from the pinned image `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…8d1b58`. `renode --version` prints `1.17.0+20260907gitf1dd1b4af.aet1605`. It is **a patched build:** per the image's `/SOURCES.md`, `Renode.dll` was rebuilt for the build string, and `Infrastructure.dll` was rebuilt with three upstream NVIC commits (`AET-WO-1605`). Infrastructure reports `1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2`. Both strings are folded into the model version. The firmware is built with the image's `rustc 1.99.1` for `thumbv7em-none-eabihf`, with no crates. Every run is `podman run --rm --network none` under `~/bin/capped-run` at `nice -n 10`, one container at a time.

## 1. The four spike questions

**Q1. Can we get a buildable STM32H7 platform without pulling the Renode tree in?** Yes. Renode's portable release already ships `platforms/boards/nucleo_h753zi.repl`, which uses `stm32h753.repl`, then `stm32h743.repl`, then `stm32h7.repl`. The fork is a 10-line overlay, `model/tviburi-nucleo-h753zi.repl`, that `using`s the stock board, plus two C# files that Renode compiles at load (`include @file.cs`; the portable build bundles Roslyn, and it works offline). No Renode source is in the repository.

**Q2. Where is the narrowest insertion point?** Insertion point: a **subclass of the stock model that re-implements the bus access interfaces**, `GuardedSTM32H7_SPI : STM32H7_SPI, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral`, with `new Write*` methods. Each one checks the rule, then calls `base.Write*` for a modeled write. The stock model is untouched. Three options were measured:

| Option | Result |
|---|---|
| Override `WriteDoubleWord` | **Impossible.** In `STM32H7_SPI` every `Read*`/`Write*` is `virtual final` (by reflection). |
| Bus hook `sysbus.SetHookBeforePeripheralWrite<T>` | Works, but has three faults. (a) It needs one hook per access width; a word write to CR1 bypassed the dword and byte hooks (measured). (b) It can only be attached after registration, so it cannot be set from the repl constructor (measured: "not registered"). (c) **One monitor `SetHookBeforePeripheralWrite` silently replaces it** (measured), which disarms the guard. |
| Interface re-implementation (chosen) | The bus dispatches all three widths to the subclass (measured). A monitor hook set on the same peripheral does not displace it (measured, and adversarial B below). |

The cost of the chosen option is one subclass per stock model class that gets a guard. A rule-only refusal is not possible either, because the stock model **drops** the bits the rule needs: SSM, SSI, SSIOP and SSOE are register tags, written values are discarded, and reads return 0. The guard therefore keeps its own copy of the last 32-bit CR1 and CFG2 writes.

**Q3. What is the smallest concrete path, and what does the refusal contain?** The scarred one: **SPI1 `MODF`**. The firmware sets `CFG2 = MASTER|SSM` and `CR1 = SSI=0`, then sets `CR1.SPE`. On silicon that raises MODF and clears SPE and MASTER. Measured on the stock model (SPI4, the same class): CR1 reads `0x1` and SR reads `0x1002` (TXP, TXC), with no MODF and MASTER still set. The twin now emits:

```
{"tviburi":"refusal","rule":"TVI-SPI-MODF","peripheral":"sysbus.spi1","register":"SPI_CR1 / SPI_CFG2",
 "bit":"CR1.SSI (bit 12) = 0 with CR1.SPE (bit 0) = 1",
 "condition":"CR1.SPE set in master mode with software SS management and the internal SS input at its active level (SSM=1, SSI=0, SSIOP=0): silicon sets SR.MODF (bit 9) and clears CR1.SPE and CFG2.MASTER; the stock model implements none of these",
 "model_version":"sha256:0ca8aeba47fa2f34f9cbc712ced4c1c5d15857e5d81a55724a6e156cbf18fd1f",
 "citation":"RM0433 … SPI chapter: mode fault (MODF) … Section number and revision not verified …",
 "state":"CR1 written=0x00000001, CFG2 last written=0x04400000",
 "access":{"offset":"0x0","value":"0x1","width":32,"pc":"0x0800003E","virtual_time":"00:00:00.000000000"}}
```

The runner exits **3 (REFUSED)**. The rule is three-way with refuse as the default. On every 32-bit CR1 write with SPE=1, it **passes only** when MASTER=1, SSM=1, SSIOP=0 and SSI=1. It refuses in four cases: slave mode (the stock model is master-only), hardware SS (the pin level is not modeled), SSIOP=1 (polarity not handled), and SSI=0 (a mode fault). A sub-word write that overlaps CR1 or CFG2 also refuses. A refused write is not passed to the model.

**Q4. Does the trace still record the refused access?** Yes. `sysbus LogPeripheralAccess spi1` logs `spi1: [cpu: 0x800003E] WriteUInt32 to 0x0 (Control1), value 0x1`, which is the refused write at the same PC as the refusal record. Two caveats follow, both measured:

- **The halt is not instruction-precise.** `RequestReturn()` and `PauseAndRequestEmulationPause(true)` stop the CPU at the end of the translation block, so the firmware's marker store *after* the refused write still ran (`0xAF7E0002`). This held with `IsHalted = true` as well. The design therefore makes the **refusal record the verdict, and it latches**; it does not rely on the halt. Nothing after a refusal is claimed.
- **`machine.ElapsedVirtualTime` is quantum-granular** inside an access. It reads 0 here, and `ExecutedInstructions` also reads 0 mid-block. The PC is the exact locator today. Invariant 6 ("timestamp relative to virtual time") needs a better clock source in a later WO.

## 2. Gates

(filled in below)

## 3. Renode behaviour that differed from the architecture's expectations

1. **In stock Renode 1.17.0, SPI1 on the H7 is not modeled at all.** `stm32h7.repl` only `Tag`s `0x40013000` ("SPI1_/_I2S1"). A write is dropped with a WARNING, a read returns 0, and Renode exits 0. The architecture's "SPI1 raised MODF … Renode did not model it" therefore implies that the consumer registered a model at SPI1 itself. This was not checked, because the run was blind. Only SPI4 is `STM32H7_SPI` in stock.
2. **Renode already knows some of its gaps, and only warns.** Writing CFG2 logs `Unhandled bits: [26] … Tags: SSM`, and CR1 logs `Tags: SSI`. The warning fires on the configuring write, not at the side effect, and execution continues. `STM32H7_SPI` is also master-only: writing MASTER=0 logs an ERROR and MASTER stays 1.
3. **Renode's exit status cannot be trusted as a verdict.** When a monitor command fails, the script stops, the console takes over, stdin EOF quits, and the exit status is **0** (measured several times). An exception while loading the platform is different: it **core-dumps (134)**. The runner therefore returns PASS only on positive evidence.
4. Monitor output lines end in `\r\r\n`.
5. Not measured: the architecture's "1 ns virtual resolution", and Landlock/seccomp reuse. Neither was needed for the spike.

## 4. Rulings needed (each with my lean)

| # | Question | Lean |
|---|---|---|
| R1 | **OQ-02, vendor vs submodule.** | **Neither, for now.** Nothing of Renode needs to be in the repository: Renode comes from the pinned image digest, and the model version folds in its build strings. Revisit only when a model needs a change to Renode source, which RUL-05 and the seams section say the refusal contract does not. **This does not block the next WO** if you accept "Renode by pinned image" as a third option. |
| R2 | **SPI1 placement widens the surface.** SPI1 goes from a Tag, which warns on every access, to stock `STM32H7_SPI` fidelity plus one guard. Paths that the stock model handles silently but incompletely now run unwarned on SPI1, exactly as they already do on SPI4: CRC, I2S, SSOE, and FIFO thresholds, among others. I judged this **not** flag-and-stop #1, because no path went from refused to passing (nothing refused before), but that is your call. | Keep SPI1, since it is the scar, and take R3 next. |
| R3 | **Deny-list or allow-list?** At the peripheral level the shim is **a deny-list of one rule**: every write TVI-SPI-MODF does not cover passes to the stock model. Within the rule it is allow-list (default refuse). | The next refusal WO should turn Renode's own self-knowledge (tagged and unhandled bits, `UnhandledAccessBehaviour`, and the per-access "Unhandled write" path) into refusals for guarded peripherals. That is a cheap, generic allow-list by Renode's own register definitions. Then add side-effect rules like this one on top. Not built: it is outside this WO. |
| R4 | **Halt semantics.** The refusal latches the run; it does not stop on the instruction. | Accept for Phase 1. If an exact stop is ever needed, measure `cpu MaximumBlockSize 1` and its cost first (northstar §6, "so slow people stop running it"). |
| R5 | **Citation (invariant 2).** I could not verify the RM0433 section number or revision offline, so I did not invent one. Bit positions are from memory of the RM0433 register maps. They agree with the tag names Renode's model reports at bits 12, 26, 28 and 29, and with the MASTER readback at bit 22. | Someone with the manual should fill in the section and revision. Invariant 2 is not met until then. |
| R6 | **Model version scope.** The model version is sha256 over `model/` plus the Renode and Infrastructure build strings. It does not cover the image digest or the stock `.repl` files inside the image; a build string only stands in for an unmodified portable tree. | Record the image digest alongside the model version once the ledger format exists (OQ-01). |
| R7 | **False refusals by design.** The common HAL configuration of master with hardware NSS *output* (SSM=0, SSOE=1) refuses, because I could not confirm offline that RM0433 rules out MODF there. | Have someone confirm in the manual, then turn it into a pass case. |

## 5. Scope notes

- **One addition beyond the literal scope:** `tests/conditions.resc`, which drives every branch of the rule from the monitor. Without it, the mutants of four refusal branches survive the acceptance suite. See §2.
- The three peripheral families, the ledger format, any Aethelgard integration, and any trace-format change were not touched.
