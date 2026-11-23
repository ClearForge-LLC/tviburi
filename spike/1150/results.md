# TVI-WO-1150 — the seam spike: results

**Status:** (i) measured. **WO §7's first stop fired at (i):** no `.repl` form re-types a stock instance. (ii)–(vi) wait on the architect's ruling (see the end of this file).
**Predictions:** `predictions.md`, commit `f26ed6c`, before any run.
**Setup, for every item:**
- **Aethelgard:** `main` at `7dec1d7b`, cloned read-only to `~/tvi-1150-aet` with its push URL disabled. Its CLI and `aethelgard-bridge` were built from that clone (`cargo build --locked`) into this worktree's untracked `target-aet/`.
- **Image:** `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddb…`, the one `7dec1d7b`'s `container/IMAGE` names. Renode `1.17.0+20260907gitf1dd1b4af.aet1605`.
- **Containers:** each under `~/bin/capped-run` (8G, swap 0), at `nice -n 10`, `--network none`, one at a time. The CLI build was the only container with a network.
- **Renode configuration:** exactly what Aethelgard writes. It was taken from a captured `simulate --foreign` boot (`harness/capture-foreign.sh`), with only `history-path` moved to the container's scratch.
- **Marking:** "read, not run" marks a fact taken from source.

---

## (i) Re-typing stock instances (`RUL-09`)

**Method.** `harness/run-retype.sh` loads each form in `retype/forms/`, in the pinned build under Aethelgard's `renode.config`. Each form is `using "platforms/cpus/stm32h753.repl"` plus one change. Then, per form:
- `peripherals`;
- `retype/checks.py`, which reads back by reflection each instance's class, bus address, parents, `Frequency` and `Limit`, interrupt endpoints, and the RCC's registration table;
- a monitor write to TIM1 and to RCC;
- the poke firmware (`fw/pokes.rs`), whose CPU writes are RCC `APB2ENR`, `D2CCIP1R`, and TIM1 `PSC`, `ARR`, `DIER.UIE`, `CR1.CEN`;
- a read of NVIC `ISPR0`;
- a TIM1 block reset through `RCC_APB2RSTR.TIM1RST` (RM0433 Rev 9 §8.7.35 p442: offset 0x098, bit 0), checked by `PSC` reading 0 afterwards.

The spike classes:
- `retype/SpikeTimer.cs`: a subclass of `STM32_Timer` in `RUL-07`'s shape, which logs each write and passes it on.
- `retype/SpikeRcc.cs`: a wrapper that holds a stock `STM32H7_RCC` and forwards every access to it, because no subclass of that class can exist (below).

Excerpts of every form's console are in `retype/excerpts.txt`.

| Form | What it declares | Result |
|---|---|---|
| `c0-control` | spike classes under new names at free addresses | **loads.** `peripherals` lists `spiketimer (SpikeTimer)` and `spikercc (SpikeRcc)`. The control rules out a compile or constructor failure being read as "cannot re-type" |
| `u0-update-only` | `timer1:` / `frequency: 200000000`, no type (Aethelgard's own form, `AET-WO-4400g`) | **merges.** The class stays `STM32_Timer`, `Frequency` reads 200000000, and the interrupts and the `TIM1` alias are kept |
| `t1-full` | the stock entry restated in full, with the type changed | `Error E02: Variable 'timer1' was already declared, previous declaration was at :316:1.` |
| `t1b-full-update-first` | as `t1`, with Aethelgard's update placed first | E02 |
| `t2-type-only` | `timer1: Tviburi1150.SpikeTimer` | E02 |
| `t3-type-registration` | type and registration | E02 |
| `t4-type-ctor` | type and constructor arguments | E02 |
| `t5-type-registration-ctor` | type, registration and constructor arguments | E02 |
| `l1-local` | `local timer1: …` | E02 |
| `r1-full`, `r2-type-only`, `r3-type-registration` | the same for `rcc` | E02 (`previous declaration was at :513:1`) |
| `n1-timer-unregister-rename` | **not re-typing:** `timer1: @none`, then `tim1g: Tviburi1150.SpikeTimer` at `0x40010000`, with the alias, constructor arguments and interrupts restated | **loads.** `peripherals` lists `tim1g (SpikeTimer)` and no `timer1`. The CPU's writes reach the spike, each with its PC (`pc=0x08000028` and on). The monitor write reaches it with `pc=none`. `ISPR0` = `0x02000000`, so IRQ 25 is pending from the spike's update. The RCC's table maps `TIM1=sysbus.tim1g`, and `TIM1RST` resets it (`PSC` reads 5, then 0) |
| `n2-rcc-unregister-rename` | **not re-typing:** `rcc: @none`, then `rccg: Tviburi1150.SpikeRcc` at `0x58024400` | `Error E39: Unable to finish the registration of 'adc3'. Are all parents: ['sysbus', 'rcc'], registered?` Every stock entry's `rcc RegistrationPeripheralAlias.X` names the stock variable |

**Renode exited 0 on every failed load** (architecture §2.0 already says so). The failure is visible only on the console.

**Why, from source (read, not run).** In Renode `f1dd1b4af`, `src/Renode/PlatformDescription/CreationDriver.cs` L109–127 (`ProcessVariableDeclarations`) treats any entry with a type as a declaration, and allows "only one" "anywhere in the usings hierarchy". A second one is `HandleDoubleDeclarationError`, E02. Entries without a type are merged as updates (L131–168). `VariableStore.TryGetVariableInLocalScope` (L45–47) checks globals before locals, so `local` cannot shadow an included global. The `l1` run agrees.

**`STM32H7_RCC` is `sealed`,** measured by reflection in the pinned build: `STM32H7_RCC=True STM32_Timer=False`. It is also so in the source (renode-infrastructure `066a7f13`, `STM32H7_RCC.cs` L24). So `RUL-07`'s guard, a subclass of the stock model, cannot exist for the RCC, under any `.repl` form. A guard there would have to be a wrapper that holds the stock model, as `SpikeRcc` does, or a reimplementation. Only the wrapper's loading was run (`c0`). Whether it behaves as the stock RCC was not run, because `n2` could not place it at the RCC.

**The shortest form that works: none re-types.** The nearest working change is `n1`, and it is a consumer change, not a class map:
- the stock instance's registrations are cancelled (`timer1: @none`);
- a new name is declared at the same address, restating the alias, the constructor arguments and every interrupt line;
- the instance's name changes (`timer1` → `tim1g`), and Aethelgard judges the `peripherals` listing by instance name and model (review A3).

Not determined: whether the cancelled stock `timer1` object is still constructed and still connected to `nvic@24–27`. It is not on the bus and receives no writes, so it raises nothing here.

For `rcc` even that fails. Swapping it means restating the registration of each of the 58 entries the RCC's table holds (`checks.py`: "RCC registered 58"), or Aethelgard no longer including the stock file's entries. That restatement was not run.

**Against the predictions.**
- "A second entry with a type is an update": **wrong.** It is a load error.
- "`peripherals` names the new class whenever the load succeeds": holds for `n1`.
- "`rcc` is harder": holds, and for a stronger reason than predicted. The class is sealed, and every stock entry registers under the `rcc` variable.

**Ruling: contradicts `RUL-09` as amended.** "A class map in the manifest … the consumer's emitter applies it to the instances it declares" can serve only instances the consumer declares itself (the SPI instances, as `sentinel-02-own`'s `spi1` and `spi2`). For an instance a pinned stock file declares, no `.repl` form re-types it. This is WO §7's first stop condition: `RUL-09`'s class map needs Aethelgard to stop including the stock entries, or to cancel and restate each one (`n1`), which is a consumer change. For the RCC, `RUL-07`'s subclass shape is impossible too, and `RUL-04` and `RUL-13` make the RCC the first family.

---

## (ii)–(vi)

**Not answered yet: the WO's §7 stop fired at (i).** Each waits on the architect's ruling on whether to continue. None of them depends on re-typing a stock instance:
- (ii) and (iv) use the guarded `spi1` on the Nucleo board, which stock Renode only tags;
- (iii) is about identity;
- (v) and (vi) put a guard only on `spi1` and `spi2`, which Aethelgard's `sentinel-02-own` platform declares itself. If ArduPilot's access log shows writes to the stock `spi4`, guarding that instance would need the `n1` form, and the results will say so.
