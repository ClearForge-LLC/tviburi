# TVI-WO-1300a — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-23. **Branch:** `wo/tvi-1300a-clock-reaches-peripheral`, from `main` `604c3d6`.
**Image:** `aethelgard-tools@sha256:1e0ab105…` (Aethelgard `main` `1c32c8ea` pins it), Renode `1.17.0+20260907gitf1dd1b4af.aet1605`.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf`. Its `STM32H7_RCC.cs` and `STM32H7_SPI.cs` match the sha256 that `1300` pinned (`7f10d491…`, `bc3f1348…`). `SystemBus.cs` is read from the same tree, but no hash pins it to the build. Every claim drawn from it is measured below as well.

## 0. Predictions, committed before any measurement

Written after reading the WO, `1300`'s FEEDBACK and handoff, the RCC and SPI guards, and the stock `STM32H7_RCC.cs` and `SystemBus.cs`, but before running anything.

**M1. What the stock model does for an access to a peripheral whose enable is 0, and how the guard can lock it.**
- P1.1. In the stock model, a lock is per peripheral, not per bus. `STM32H7_RCC`'s `*ENR` write callback calls `sysbus.SetPeripheralEnabled(peri, bit)` for a **registered** alias. Every access through `SystemBus` to a locked peripheral is dropped: a write is ignored, and a read returns 0. Each logs `Tried to write/read a locked peripheral` at WARNING under `sysbus`, which is not a record. So I predict the same behaviour on all eight buses (AHB1–4, APB1–4).
- P1.2. The stock write callback runs on **every** write to the field, not only on a change. So writing 0 into the owned stock RCC locks the peripheral even though the stock bit reads 1.
- P1.3. A peripheral that the platform declares but that is not one of the 59 registrations (58 restated, plus `spi1`) cannot be locked by this mechanism. I predict at least `spi2`, `spi3`, `pwr`, `exti`, `flash` and the SRAMs. `rtc` is locked by `RCC_BDCR.RTCEN` (stock L494), not by `RCC_APB4ENR.RTCAPBEN`.
- P1.4. A monitor `sysbus WriteDoubleWord` and a DMA transfer reach a locked peripheral through the same `SystemBus` path, so both are dropped too.
- P1.5. `machine Reset` does not clear the bus's locked set (`SystemBus.Reset` does not touch `lockedPeripherals`). Today, after a reset, the guard's copies are 0 while the bus keeps whatever the firmware left.
- P1.6. A locked **guarded** peripheral (`spi1`) never reaches its guard, so it emits no record: the access is silently dropped. A refusal that names `SPI1EN` therefore needs one of two designs:
  - (a) the RCC guard leaves its guarded peripherals unlocked on the bus, and each guarded peripheral asks the RCC guard for its enable at every access;
  - (b) a bus-level observer, which `RUL-07` rules out as a guard.
- P1.7. The way to lock is `1300`'s proposal: after each `Register()` and in `Reset()`, write each `*ENR` copy through the owned stock RCC. It locks every registered alias except `rtc`, which waits on `BDCR`.

**M2. Every Tviburi test, script and generator that drives a peripheral it never enabled.**
- P2.1. **Monitor scripts with no `SPI1EN`:** `tests/conditions.resc` (every case), `t_recorded`'s script in `tests/spike.sh`, and `1200`'s generated SPI sweep (`tools/tviburi-release`). With the lock, each one's `spi1` accesses are dropped and its assertions fail.
- P2.2. **Firmware:**
  - `firmware/spi1.rs` enables `SPI1EN` before any SPI1 access, in every variant.
  - **`path="forge"` writes USART3 with `USART3EN` never set.** With the lock, its `TXE` poll reads 0 forever, so the run never reaches its marker and the `forge` tests fail. That would be a vacuous pass of the forging test, so it is a test to fix, not a guard defect.
  - `firmware/rcc.rs` enables each peripheral it touches.
- P2.3. `tests/1300/` monitor cases that touch `spi1` (`reset`, `spi1rst`, `registrations`) change behaviour. The adversarial cases that drive `spi1` from the monitor (`adv-hook`, `adv-spray`) need `SPI1EN` too.
- P2.4. The consumer's runs are unchanged. Its cold writes zero every `*ENR` first (`1300`: 15/15 boots), and the firmware then enables what it uses.

**M3. For (3): each source, output and selector change that RM0433 blocks in hardware, against those it allows.**
- P3.1. **Blocked ("cannot be cleared", p384–386):**
  - `HSION`, `CSION` and `HSEON` while that oscillator is the system clock, directly or through PLL1 as the system clock;
  - `PLL1ON` while PLL1 is the system clock (p383).
  - The stock model already reverts these (`KeptCr`).
- P3.2. **Blocked ("can be written only when … disabled"):**
  - `DIVxyEN` while PLLx is on (p400–401);
  - `PLLSRC` while any PLL is on, and `DIVMx` while PLLx is on (p398–399).
  - `1300` refuses each of these today as `TVI-RCC-PLL` `predicted-fault`.
- P3.3. **Allowed, so they need (3)'s refusal at the RCC write:**
  - clearing a source oscillator that is not the system clock's path (for example `HSION` under `CKPERSEL` = `hsi_ker_ck`, or `HSEON` feeding PLL2 or PLL3);
  - `PLL2ON`/`PLL3ON` cleared, and `PLL1ON` cleared when PLL1 is not the system clock;
  - any kernel-clock selector change, including `CKPERSEL` (p371: the mux waits for both clocks, and no hardware lock is stated);
  - the peripheral's own `PERxEN` cleared while it runs (Table 62, p376).
- P3.4. RM0433 says nothing explicit about what silicon does to a running SPI whose kernel clock stops. A refusal there is `predicted-fault` on p371 ("no clock will be provided to the peripheral"), not a modeled fault.

**M4. The teardown flake (`1300` §5.5).**
- P4.1. It is rare: at most 2 losses in 50 runs of one case (≤ 4%).
- P4.2. It is Renode's, a race at `quit` between disposing the machine's peripherals and closing the `logFile` backend. Nothing in `scripts/` sits between `quit` and the log. The `tviburi-check` side is deterministic.

**M5. Either firmware's boot, against (1)–(3), before the build** (read from `1300`'s measurement, not re-run here):
- ArduPilot's boot and the generated boots enable each peripheral before using it (0 locks);
- ArduPilot changes no selector and no source under a running SPI1;
- the 9 `per_ck` generated boards start SPI1 with no kernel clock (`1300` §5), which `1300` already ruled is not a stop.
