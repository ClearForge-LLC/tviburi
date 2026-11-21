# Tviburi — north star

**Status:** draft for ratification, 2026-11-21. **Supersedes nothing.** Read before `architecture.md`.
**Author:** Grok (external) · **Gate:** Scotty · **Builder:** a separate build session.

---

## 1. The one sentence

**Tviburi is a digital twin for commercial microcontrollers that refuses to claim it has simulated anything it has not actually modeled.**

The sentence names the capability (a usable twin) and the constraint that shapes every decision (unmodeled paths are explicit refusals, never silent passes). It names no language, no host, no vendor.

## 2. The problem, stated precisely

Existing instruction-level simulators (Renode is the best public option) let firmware run and produce traces. They also let firmware succeed on paths the model does not implement. The first silicon scar that Aethelgard recorded — SPI1 raising `MODF` and clearing `MASTER` on the NUCLEO-H753ZI while the twin passed — is the pattern. The twin measured the path it had modeled; the silicon measured the path that existed.

The capability gap is not "can we run the firmware." It is that an unmodeled side-effect is currently indistinguishable from a modeled success.

## 3. End state

A firmware engineer or a generating tool can point Tviburi at a vendor SVD + pin table, run a binary, and receive either:

- a replayable trace of every register access with a timestamp and a model-version hash, or
- a named refusal that cites the exact peripheral, register/bit, and reference-manual section the model does not yet implement.

The gap ledger for that model version is a separate, versioned artifact. Downstream consumers (Aethelgard or any other) record the hash; they do not own the models.

## 4. Invariants — true regardless of what any work order asks

| # | Invariant | How to falsify |
|---|---|---|
| 1 | An unmodeled path refuses by name. Silent success on an unimplemented flag, timing, or side-effect is a defect. *This invariant is the project.* | Find any path the model does not implement that still returns success |
| 2 | Every modeled side-effect cites the reference-manual section it implements. | Find a modeled behavior whose citation is missing or does not match the manual |
| 3 | The gap ledger is a property of the model version, not of any campaign. The same model build emits the same ledger. | Two runs of the same model hash produce different gap lists |
| 4 | The twin holds no credential and makes no network call that could affect real silicon or a production system. | Observe any outbound connection or credential use during a run |
| 5 | A refusal names the peripheral, the register or bit, the condition, and the model version. "Not supported" is not a refusal. | Read any refusal; one lacking those four fields falsifies |
| 6 | The trace records every register access the firmware issued, with a timestamp relative to virtual time. | Compare a known sequence of writes against the trace; a missing access falsifies |
| 7 | Downstream consumers record the gap-ledger hash; they do not restate the gaps. | Find a consumer that copies the gap list instead of the hash |

## 5. Non-goals — deliberately not built

- **Generating boards or firmware.** That is Aethelgard's job. Tviburi produces evidence; it does not produce the artifacts that are judged. Owning both would hide shared assumptions in both halves.
- **Proving the physical board.** Simulation narrows the search. Silicon remains the authority. A twin that claimed otherwise would be lying about its scope.
- **A clean-room CPU or bus.** Renode already supplies the expensive pieces. Reimplementing them before the refusal contract exists is a multi-year detour that does not serve the invariant.
- **Cycle-accurate timing as a requirement.** Instruction-accurate with named gaps is the starting contract. Cycle accuracy is a later, optional tightening, not a prerequisite for the refusal rule.

## 6. What would make this a failure

- It reports success on a path silicon would have faulted, and the gap is not named.
- The gap ledger drifts from the model that emitted it.
- It becomes so slow or heavy that people stop running it and go back to silent Renode.
- Downstream tools start treating its traces as proof of the physical board.

## 7. Amendments

| Date | Section | Change | Rationale |
|---|---|---|---|
| 2026-11-21 | — | Initial version | Seed from the 2026-11-21 conversation |

## 8. Provenance

Written 2026-11-21 by Grok after the Aethelgard review conversation with Scotty. Rests on the measured Renode gaps already recorded in Aethelgard (SPI1 `MODF`, incomplete I2C error models) and on the explicit decision that the twin is a sibling, not a component. No running system existed at the time of writing.
