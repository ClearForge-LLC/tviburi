# TVI-WO-1308 — §1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-1308-dmamux-pwr`, from `main` `bd9dbd5`.
**State:** predictions (§0), committed before any run of this work order.
**Stock source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/Peripherals/Peripherals/Miscellaneous/STM32_DMAMUX.cs` (130 lines), read whole by me. No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1). The stock PWR is not C#: it is a `Python.PythonPeripheral` in the pinned `stm32h7.repl` (`b85cb4af…`, L728–746, as `1303` copied it), read whole by me.
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`. At the time of these predictions the DMAMUX (§17) and PWR (§6) chapters had not been read. Every RM0433 or DS12117 statement in §0 is **from memory**, except the pages earlier work orders already cite (p305, p308, p310 by `1100a`; p696–698, p703–704, p708, p712 by `1303`; p160 Table 17 by `1304`).

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock sources above. **DMAMUX1** (`stm32h7.repl` L260–265): `STM32_DMAMUX` at `0x40020800`, 115 inputs, 16 channels, 8 generators; `CxCR` keeps `DMAREQ_ID` (bits 0–6) as a value field, and `SOIE`, `EGE`, `SE`, `SPOL`, `NBREQ`, `SYNC_ID` are tags; `CSR`/`CFR` hold flags no code sets; `RGxCR`, `RGSR`, `RGCFR` are tags; `OnGPIO(n)` drives every channel whose `DMAREQ_ID` = n. The class is `public`, not `sealed`, in that source. **PWR** (L728): a script that answers a read of `CR1` `0xF000C100`, `CSR1` `0x6000`, `CR3` `0x04000000`, `D3CR` `0x2000`, and warns "Unhandled access" for every other offset; it stores nothing;
- `1100a`'s measure §1.1, §2–§3 and the cut: the armed run writes PWR `CR1`, `CR2`, `CR3`, `CPUCR`, `D3CR` (`VOS` = 11) and ten DMAMUX1 channels (C0–C7 less none named, C10, C11: requests 41, 43, 45, 9, 38, 42, 40, 46, 39, 37); `1150`'s queue rows for PWR (`CR1` 93 writes, values `0xF000C000`/`0xF000C100`; `CR2` `0x1`; `CR3` `0x2`, `0x1000002`), each `ap5`-only;
- `1303`'s FEEDBACK §7 (the DMAMUX1 gaps, ruled Q1) and `1303a`'s measure (no generated boot touches DMA1/2, DMAMUX1 or BDMA);
- the DMA guard's route check (`model/GuardedSTM32DMA.cs` L694–757: `TVI-DMA-ROUTE`, `-MUX`, `-SHARED`, `-REQUEST`, reading `dmamux.ReadDoubleWord(4 * ch)` on the stock object it is handed as `dmamux`, typed `STM32_DMAMUX`);
- the flash guard's scale read (`model/GuardedSTM32H7_Flash.cs` L480–520: `PWR_D3CR` at `0x58024818` read through the bus's object; `VOS` 00 is "unknown", decided at VOS1). The RCC guard reads no voltage scale.

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9 and DS12117 Rev 10, from memory; §1 reads them)
- **DMAMUX1:** each of the 16 channels selects one of 107 inputs by `DMAREQ_ID` (0 none, 1–8 generators, then peripherals); synchronisation (`SE`, `SYNC_ID`, `SPOL`, `NBREQ`) and event generation (`EGE`) per channel; 8 request generators (`RGxCR`: `SIG_ID`, `GE`, `GPOL`, `GNBREQ`); overrun flags in `CSR`/`RGSR`, cleared through `CFR`/`RGCFR`. The registers are 32-bit access only (p708, as `1303` read it). Nothing in the chapter makes a `CxCR` write one-time; a channel may be re-routed at any time, and a route shared by two running streams is forbidden (p704).
- **PWR (RM0433, a device with no SMPS):** `CR3` holds `BYPASS`, `LDOEN`, `SCUEN` and the USB regulator (`USB33DEN`, `USBREGEN`, `USB33RDY`). **The supply configuration is written once after power-on; later writes are ignored until the next power-on reset** (from memory; whether a system reset re-opens it is to be read). `SDLEVEL`, which the WO names, belongs to the SMPS parts' manuals, **not** RM0433 (from memory; to be confirmed by search). `ACTVOSRDY` (`CSR1`) says the supply configuration is ready; `D3CR.VOS` reset is 01 (Scale 3), and `VOSRDY` rises after the regulator settles, with **no time stated in RM0433** (from memory) and perhaps none in DS12117. `CR1.DBP` = 0 at reset write-protects the backup domain (RCC `BDCR`, RTC, backup SRAM). The maximum clock per scale is a DS12117 table (rev V: Scale 1 400 MHz CPU / 200 MHz AXI, Scale 3 lower; from memory, low confidence on the numbers).

### P2. The stock models, by source (each probed in §1)
- **DMAMUX1:** a `CxCR` word write keeps `DMAREQ_ID` and drops every tag; a read returns `DMAREQ_ID` only. Byte and half-word writes are dropped (`BasicDoubleWordPeripheral`). A write with DMAMUX1's clock off is taken (the RCC guard locks only what it registers; DMAMUX1 registers on `sysbus`). Generators raise nothing.
- **PWR:** every write is dropped, with no log line for `CR1`, `CSR1`, `CR3`, `D3CR`, and an "Unhandled access" warning for every other offset (so `CR2` and `CPUCR` writes warn). `D3CR` reads `VOS` = 00 whatever was written; `CR3` reads `LDOEN` = 0 and `USB33RDY` = 1; `CSR1` reads `ACTVOSRDY` = 1 at once; `CR1` reads `DBP` = 1 before any write.

### P3. The generated firmware (14 boots, `1305`'s captures)
- **No DMAMUX1 access.** High (`1303a`).
- **No PWR write** (the queue's PWR rows are `ap5`-only). Moderate. **If so, they run at the reset scale (Scale 3) whatever their clock.** Their `sys_ck` is not known to me; if any boot raises `sys_ck` above Scale 3's limit (DS12117, from memory ~200 MHz on rev V), a scale rule refuses it on a path RM0433 says is invalid: a §7 stop and a row for Aethelgard. Low confidence either way.
- **The flash guard's observer moves for them:** `D3CR` would read `VOS` = 01 (Scale 3) instead of 00 ("unknown, decided at VOS1"), so a generated board's `LATENCY` sized for Scale 1 would become `TVI-FLASH-WAIT`. Low confidence; measured in §1 by computing each boot's AXI clock against Table 17's Scale 3 row.

### P4. ArduPilot, `sentinel-02-own` (the committed fixture, ELF `a799dfb6`, 5 s)
- **PWR, in this order, early in `stm32_init`:** `CR3` written once (`LDOEN`, `SCUEN` 0, `BYPASS` 0), `CSR1` polled for `ACTVOSRDY`, `D3CR` written `VOS` = 11, `D3CR` polled for `VOSRDY`, then the PLLs and `FLASH_ACR`; `CR1.DBP` set before the RCC's `BDCR`; `CR2` `BREN`; `CPUCR` written. **`CR3` is written a second time** later (`USB33DEN`, value `0x1000002`), and its `USB33RDY` polled. Moderate. **The second `CR3` write re-states `LDOEN` = 1 with the same supply bits**, so silicon's one-time rule ignores only what changes nothing. Moderate.
- **`CR1` is written tens of times** (`DBP` toggled around every backup-domain access: RTC, backup registers). High (`1150`: 93 writes in 5 s).
- **DMAMUX1:** the six streams `1303` measured, each `CxCR` a word write of a request id only (no `SE`, `EGE`, `SOIE`, generators, `RGxCR`, `CFR`). High.

### P5. The armed run (Sentinel-03, 14 s)
- **PWR:** as P4, plus nothing new after the arm. `D3CR` = `VOS` 11 before the PLL lock and before `FLASH_ACR`'s latency rises. High.
- **DMAMUX1:** ten channels; **some `CxCR` rewritten many times** (ArduPilot's shared-DMA locks re-allocate a stream per bus transfer, and ChibiOS writes the route at each allocation), every value a peripheral request id, none 0 written while its stream runs. Moderate on the count, high on the values.
- **No route a platform cannot raise** is written: every id written is one a stock or guarded model raises or one the DMA guard acknowledges (`RaisedToMemory`/`RaisedFromMemory`). Moderate.

### P6. Observers that would move (Aethelgard's, read at its `main`)
- **The console:** the stub's "Unhandled access" warnings for `CR2`/`CPUCR` (and any other offset) disappear or change text once PWR is guarded. Aethelgard reads console records by source; a warning line is not a record, so no gate moves. Moderate.
- **The flash guard's records:** unchanged on ArduPilot (VOS 11 = Scale 1, the same decision as "unknown → VOS1"). Changed on the generated boots if P3's flash case holds.
- **The boot block** (instructions, PC, the consumer's read registers): byte-identical for ArduPilot if the guard serves what the stub served wherever the firmware branches on it. **One risk:** a guard that serves `CR3.LDOEN`/`D3CR.VOS` as written changes what ChibiOS's read-modify-writes produce (today they read 0/00), so later `CR3`/`D3CR` writes differ in value; the code path does not. Moderate.

### P7. Cost
- **Memory:** two small guards in the one file: **+8 to +20 MiB** at the median of eleven boots a side (`1302`'s compile-garbage effect dominates; two classes of a few hundred lines). Under `1201`'s budget. Moderate.
- **Shards at 1.53:** the two suites add 40–70 cases and mutants, about 6–10 min of serial work at the reference. Ten shards at `main`'s weights project near 18–20 min at 1.53, so **the projection likely passes 20 minutes** and the eleventh shard comes first (WO §7). Low-moderate.

### P8. Form
- **DMAMUX1 can be guarded by a subclass** (not sealed), which keeps the DMA guard's `dmamux` property typed `STM32_DMAMUX` and its direct `ReadDoubleWord` calls working. A wrapper (the WO's word) would need the DMA guard's property re-typed, outside this fence: a ruling question. Whether the pinned build's `ReadDoubleWord` is overridable or `virtual final` decides what the DMA guard's direct read sees. Moderate.
- **PWR has no stock C# model to subclass or wrap:** the guard is a new peripheral that cancels `pwr` and is declared at `0x58024800` `as "pwr"`. Its serving the four stub values where the firmware has written nothing is the control for P6. Moderate.
