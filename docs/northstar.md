# Tviburi — north star

**Status:** ratified by Scotty on 2026-11-23, by the merge of the rework (PR #5) (see §7); first drafted 2026-11-21. **Supersedes nothing.** Read before `architecture.md`.
**Authors:** Grok (seed, 2026-11-21); Claude, co-architect (rework, 2026-11-23) · **Gate:** Scotty · **Builder:** a separate build session.
**Citing across repositories:** Aethelgard has its own `INV-01`…`INV-07`. From outside this repository, cite these invariants as `TVI-INV-n`. Inside it, "invariant n" is enough.

---

## 1. The one sentence

**Tviburi is a digital twin for commercial microcontrollers that refuses to claim it has simulated anything it has not actually modeled.**

The sentence names the capability (a usable twin) and the constraint that shapes every decision (unmodeled paths are explicit refusals, never silent passes). It names no language, no host, no vendor.

## 2. The problem, stated precisely

Existing instruction-level simulators (Renode is the best public option) let firmware run and produce traces. They also let firmware succeed on paths the model does not implement.

**The first silicon scar Aethelgard recorded is the pattern.** On the NUCLEO-H753ZI, SPI1 raised `MODF` and cleared `MASTER`, while the twin passed. The twin measured the path it had modeled; the silicon measured the path that existed.

The gap is not "can we run the firmware." It is that an unmodeled side-effect is currently indistinguishable from a modeled success. A consumer that gates on the twin's result therefore inherits every silent gap as a false pass.

**Evidence that the gap is easy to reintroduce,** from the first build of this project (`TVI-WO-0100`, built twice and judged blind; `docs/reviews/2026-11-22-tvi-wo-0100-verdict.md`):
- a refusal that was printed, while the process the consumer runs exited 0;
- a "modeled" control path that was itself a silent pass;
- a model hash that named a file, not the code that ran;
- a check that passed on a log left over from an earlier run.

Each is a refusal that existed but could not be seen. Invariants 3, 8 and 2 below exist because of them.

## 3. End state

A firmware engineer or a generating tool points Tviburi at a firmware binary for a supported board and receives exactly one of:
- **a pass:** a replayable trace of every register access, with a timestamp and the hash of the model that ran; or
- **a refusal:** a record that names the peripheral, the register or bit, the condition, and the model, at the first access the model cannot answer for, and a run status that no consumer can mistake for a pass.

The gap ledger for that model is a separate, versioned artifact. A consumer records its hash and may read it by that hash, for example to predict, before running, which of its own register writes will refuse. Consumers never own the models.

**For Aethelgard, the first consumer, this means:**
- a verdict that today rests on a silently incomplete model instead rests on one that refuses;
- each refusal it meets is a named, ordered piece of model work;
- each new silicon scar becomes a permanent regression case in the twin, rather than a note.

## 4. Invariants — true regardless of what any work order asks

| # | Invariant | How to falsify |
|---|---|---|
| 1 | An unmodeled path refuses by name. Silent success on an unimplemented flag, timing, or side-effect is a defect. *This invariant is the project.* | Find any path the model does not implement that still returns success |
| 2 | Every side-effect Tviburi models cites the reference-manual section it implements. A model inherited unchanged from upstream cites nothing, so the gap ledger lists it as inherited and unaudited until it is audited. | Find a Tviburi-authored behaviour with a missing or wrong citation, or an inherited model the ledger presents as audited |
| 3 | The gap ledger is a property of the model build, not of any campaign. The model's identity is a hash of everything that executed: the compiled models, the platform description and the host simulator's build. | Two runs of the same model hash produce different gap lists; or a model file changes and the hash does not |
| 4 | The twin holds no credential and makes no network call that could affect real silicon or a production system. | Observe any outbound connection or credential use during a run |
| 5 | A refusal names the peripheral, the register or bit, the condition, and the model version, and locates the access (program counter, and virtual time where it can). "Not supported" is not a refusal. | Read any refusal; one lacking those fields falsifies |
| 6 | The trace records every register access the firmware issued, including the refused one, with a timestamp relative to virtual time. | Compare a known sequence of writes against the trace; a missing access falsifies |
| 7 | Downstream consumers record the gap-ledger hash and read the ledger by it. They do not copy or restate the gaps. | Find a consumer that copies the gap list instead of the hash |
| 8 | A run fails closed at the boundary its consumer invokes. If any refusal was recorded, or the run cannot show that the refusal runtime and the hashed model are what executed, the run's status is not a pass. | Remove the refusal runtime, crash the simulator, or trigger a refusal, and observe a pass status at the consumer's entry point |

## 5. Non-goals — deliberately not built

- **Generating boards or firmware.** That is Aethelgard's job. Tviburi produces evidence; it does not produce the artifacts that are judged. Owning both would hide shared assumptions in both halves.
- **Proving the physical board.** Simulation narrows the search. Silicon remains the authority. A twin that claimed otherwise would be lying about its scope.
- **A clean-room CPU or bus.** The host simulator already supplies the expensive pieces. Reimplementing them before the refusal contract exists is a multi-year detour that does not serve invariant 1.
- **Modelling a whole chip before the twin is useful.** Tempting, because a complete model feels honest. It is the wrong order: invariant 1 makes a partial model usable, and the consumer's refusals say which part to model next.
- **Cycle-accurate timing as a requirement.** Instruction-accurate with named gaps is the starting contract. Cycle accuracy is a later, optional tightening, not a prerequisite for the refusal rule.
- **Fixing silent gaps only upstream.** Upstream fixes are welcome, and each is still re-checked here. Waiting on them leaves the false passes in place meanwhile.

## 6. What would make this a failure

- It reports success on a path silicon would have faulted, and the gap is not named.
- It records a refusal that the consumer's gate does not see.
- The gap ledger drifts from the model that emitted it.
- It becomes so slow or heavy that people stop running it and go back to silent Renode.
- Downstream tools start treating its traces as proof of the physical board.

## 7. Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed from the 2026-11-21 conversation |
| 2026-11-23 | header, §2, §3, §5, §6 | The status names its ratification; `TVI-INV-n` for cross-repository citations; the blind build's four failures added to §2 as evidence; Aethelgard's use stated in §3; two non-goals and one failure added | The repo-genesis rework (Scotty, 2026-11-23: "give us a solid foundation for consuming Tvi output to make Aet better and not fully reliant upon Renode alone") |
| 2026-11-23 | §4: 2, 3, 5, 7 | **2** scoped to Tviburi-authored models, with inherited models listed as unaudited. **3** defines the model's identity as everything that executed. **5** locates the access. **7** allows reading the ledger by hash | 2 was false on the first day: every inherited Renode model cites nothing (review of PR #2, ruling 6). 3 failed in practice: a hash followed an environment variable, not the compiled shim (verdict run Y5b). 5: the blind build's record carries a PC, and virtual time is quantum-granular mid-access (`TVI-WO-0100` FEEDBACK Q4). 7: a consumer that cannot read the ledger cannot predict refusals |
| 2026-11-23 | §4: 8 (new) | **8** a run fails closed at the consumer's entry point | Both builds of `TVI-WO-0100` printed a refusal through a process that exited 0 in some path (verdict §2, item 10 and R9). The consumer gates on exit status, so a refusal it cannot see is a silent pass one layer out |

## 8. Provenance

- **Seed:** written 2026-11-21 by Grok, after the Aethelgard review conversation with Scotty. It rests on the Renode gaps already measured in Aethelgard (SPI1 `MODF`, incomplete I2C error models) and on the decision that the twin is a sibling, not a component.
- **Rework:** 2026-11-23, by Claude as co-architect, at Scotty's request, using the repo-genesis skill. It rests on:
  - the two builds of `TVI-WO-0100` and the blind verdict (`docs/reviews/2026-11-22-*`);
  - Aethelgard's `main` at `f42a0c88`, read for how it consumes Renode today (`architecture.md` §2).
