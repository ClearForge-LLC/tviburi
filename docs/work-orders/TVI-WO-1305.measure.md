# TVI-WO-1305 — §1.1 measurement

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-24. **Branch:** `wo/tvi-1305-usart-guard`, from `main` `1e9ce55` (`1303` merged; the WO merged).
**State:** predictions (§0), committed before any run of this work order.
**Renode source read:** `~/.cache/aet-4202/renode-src/inf/src/Emulator/`: `Peripherals/Peripherals/UART/STM32F7_USART.cs` (sha256 `d62666e1…`), `Main/Peripherals/UART/UARTBase.cs` (`89acf7d4…`), the `IUART` interface, `Main/Peripherals/Bus/SystemBus.cs` (access logging), `Bus/Wrappers/WriteLoggingWrapper.cs` and `RegisterMapper`. No hash pins that tree to the pinned build, so each claim drawn from it is probed (§1).
**RM0433:** Rev 9, `~/.cache/aet-held/rm0433-rev9.txt`, pages by `tests/1300/rmpage.sh`.

## 0. Predictions, committed before any run

**Seen before writing them** (so these are not blind on those points):
- the stock `STM32F7_USART.cs`, `UARTBase.cs`, `IUART`, and how the access log names a register (`RegisterMapper`: a nested enum of the *registered* peripheral's type whose name contains "register");
- the stock include chain's UART entries (`stm32h7.repl`, as 1303 copied it from Aethelgard `1cb994c1`): `usart1/2/3/6`, `uart4/5/7/8` and `lpuart1`, each `UART.STM32F7_USART` with `rcc RegistrationPeripheralAlias.<NAME>`, a `frequency` and `IRQ -> exti@N`; `lpuart1` without `lowPowerMode`;
- the 14 captured generated platforms (1303's, Aethelgard `1cb994c1`) and ArduPilot's fixture platform each carry an untyped block for every UART instance (generated: all nine; ArduPilot: `usart1`–`uart8`, `frequency: 72000000`);
- Aethelgard `1cb994c1`→`fa12abf4`: `aethelgard-twin`, `aethelgard-svd-hal` and the generator are unchanged; the CLI's judging code and one IR error variant changed. So 1303's generated captures hold today's firmware and platforms;
- Aethelgard `AET-WO-4400h` (`fa12abf4`, `spike/4400h/RESULTS.md`, `out/queue/README.md`, `patches/usart1-rx-request.diff`, `py/frames.py`), and the first **six** `usart1` writes of its armed Sentinel-03 run's access log (`~/.cache/aet-4400h/run/s03-arm-s/console.txt.gz`): `BRR` `0x271`, `CR2` `0x40`, `CR3` `0xC1`, `CR1` `0x2000013D`, `ICR` `0xFFFFFFFF`, `CR1` `0x10D`, at 3.2 ms; then `TDR` bytes stamped 4.76 ms. Nothing else of that log;
- the board definitions' serial ports: Sentinel-02 `SERIAL_ORDER USART1`; Sentinel-03 `USART1 USART2 USART3` (console, CRSF RC input, MAVLink);
- the RCC guard's kernel-clock query (`QueryKernelClock`), which covers `usart1`, `usart2` and `usart3` and no other UART; `UART4EN`, `UART5EN`, `UART7EN`, `UART8EN`, `USART6EN` and `LPUART1EN` allowed only at 0 in its list;
- RM0433 Rev 9's USART chapter, field by field (§48.8, pp. 2066–2090; the functional sections on reception, DMA, FIFO, idle and receiver timeout), extracted by a reader into `~/.cache/tvi-1305/rm/usart-fields.md` with page and line for each quote; I checked the quotes P1 rests on against the text;
- a survey of Aethelgard `fa12abf4`'s USART observers (`~/.cache/tvi-1305/observers.md`), whose load-bearing lines I read: the `peripherals` listing check (`aethelgard-cli/src/renode.rs`:2834–2842) and the serial access parser (`aethelgard-twin/src/serial.rs`:347–391, `logged_by` :510; `renode.rs`:3830–3840).

Nothing has been run in this work order.

### P1. What silicon does (RM0433 Rev 9, read)
- **Protected under `UE` = 1** ("can only be written when the USART is disabled (UE = 0)"): `CR1` `FIFOEN`, `M1`, `M0`, `OVER8`, `WAKE`, `PCE`, `PS`, `DEAT`, `DEDT` (pp. 2067–2069; the FIFO-off view pp. 2071–2072 says the same); most of `CR2` (`STOP` and others; `ADD` when `RE` = 0 or `UE` = 0; `ABRMOD` when `ABREN` = 0 or `UE` = 0; pp. 2075–2077); in `CR3` `OVRDIS`, `DDRE`, `ONEBIT`, `WUS` and others (pp. 2079–2081); `BRR`, `GTPR` and `PRESC` whole (pp. 2082–2083, 2099). **Not protected:** `UE`, `TE`, `RE`, every interrupt enable, `DMAR`, `DMAT`, `EIE`, `RTOEN`, `RTOR.RTO`. No sentence says `TE`/`RE` may be written while `UE` = 1, and the procedures set them after `UE` (p2025, p2029).
- **Access width:** "The peripheral registers have to be accessed by words (32 bits)" (p2067). No sentence on byte or half-word accesses.
- **`IDLE`:** "set by hardware when an Idle Line is detected. An interrupt is generated if IDLEIE = 1 … cleared by software, writing 1 to the IDLECF"; "not set again until the RXFNE bit has been set (i.e. a new idle line occurs)" (ISR, p2090). An idle character is "an entire frame of '1's (the number of '1's includes the number of stop bits)" (p2021). **And:** "If the USART is activated while the line is IDLE, the idle state is detected after the duration of one IDLE frame (not only after the reception of one character frame)" (p2040), printed in the mute-mode section. Whether `IDLE` sets once at a start with nothing received is therefore a reading, not a quote.
- **The receive DMA request:** with `DMAR` = 1, "the data is moved from this address to the memory after each RXNE (RXFNE in case FIFO mode is enabled) event" (p2048). "The DMA requests are also reset when UE = 0 so the DMA channel must be disabled before resetting the UE bit" (p2070). No sentence ties `RXFT` (the threshold) to the request: it is an interrupt.
- **FIFO:** depth 16 for TX and RX (Table 402, p2018); thresholds 1/8 to 7/8 and full/empty (`CR3`, pp. 2078–2079). `ORE` when a byte arrives with the RX FIFO full (FIFO on) or `RDR` unread (off), unless `OVRDIS`.
- **`RTOF`:** set when `RTOEN` = 1 and the line has been idle for `RTO` bit times after the last character; `RTOIE` only gates the interrupt.
- **Kernel clock:** the registers stay readable and writable "even when the usart_ker_ck clock is stopped" (p2019). No sentence says what reception or transmission do without it. `USART16SEL` and `USART234578SEL` (`RCC_D2CCIP2R`, p417) reset to `rcc_pclk2`/`rcc_pclk1`.

### P2. The stock model, by source (each to be probed, §1 M2)
- **Form.** `STM32F7_USART` is `sealed`: the guard is `RUL-07`'s wrapper. It must also be an `IUART` (file backends, `WriteChar` injection, analyzers bind to the registered peripheral), and pass `IRQ` and `ReceiveDmaRequest` through.
- **Dropped or absent:** `CR1` tags `UESM`, `IDLEIE`, `WAKE`, `M0` (named `MO`), `MME`, `CMIE`, `DEDT`, `DEAT`, `M1`, `EOBIE`, and bits 29–31 (`FIFOEN`, `TXFEIE`, `RXFFIE`) as *reserved*; `CR2` everything but `STOP` (bits 0–3, H7's `SLVEN`/`DIS_NSS`, reserved); `CR3` everything but `DMAR`/`DMAT`, bits 25–31 (H7's FIFO thresholds) reserved; `GTPR` (0x10) and `PRESC` (0x2C) not defined. **Stored:** `UE`, `RE`, `TE`, `RXNEIE`, `TCIE`, `TXEIE`, `PEIE`, `PS`, `PCE`, `OVER8`, `RTOIE`, `STOP`, `DMAR`, `DMAT`, `BRR` (16 bits), `RTO`.
- **No `UE` rule:** every field written while `UE` = 1 is taken, with no record.
- **`IDLE` is never set** (a tag reading 0), whatever is received.
- **No FIFO and no overrun:** the receive queue is unbounded; `ORE` never sets; 20 bytes unread are all read back.
- **`RTOF`** is raised only when `RTOIE` = 1 (whatever `RTOEN`), after `RTO` × 8 / baud seconds, 8 times silicon's time (`STM32F7_USART.cs`:129).
- **The request** is set on a change of the receive buffer's state, gated by `DMAR` at that moment: not raised by `DMAR` set after a byte waits; not reset by `UE` = 0; and a byte that arrives while its DMA stream is disabled is never moved, nor is any byte after it (the state stays "ready", so no edge follows).
- **Receive with `RE` = 0 or `UE` = 0** drops the byte (`UARTBase`, `IsReceiveEnabled`). `TXE` always reads 1; `TC` is set at each `TDR` write and when `TE` is cleared; `TEACK` = `TE`, `REACK` = `RE`.
- **Reset `ISR`** reads `0x200000C0`.

### P3. The generated firmware (14 boots)
- Each writes one USART (or none), polled: `BRR`, then `CR1` with `UE` and `TE` (and perhaps `RE`), then `TDR` bytes. `CR2`/`CR3` stay at reset. No interrupt enable, no DMA (`aethelgard-svd-hal` README: "no interrupt, no DMA").
- **No field is written while `UE` = 1** other than `TDR`. High confidence.
- So the guard changes no generated observer, unless an observer reads a register name or a warning line the wrapper changes (P7).

### P4. ArduPilot, `sentinel-02-own` (the committed fixture, ELF `a799dfb6`, 5 s)
- USART1 only. The sequence of 4400h's run (P0's six writes), then DMA-driven transmission (`TDR` written by DMA1, stamped with the `EN` store's PC) and an `ICR` clear per transfer.
- **The second `CR1` write (`0x10D`) is a read-modify-write on the stock model's read-back**, which drops `FIFOEN` (reserved) and `IDLEIE` (a tag). On silicon the same code writes `0x2000011D`: `FIFOEN` and `IDLEIE` stay set, and only `RXNEIE` is cleared. **So, on the stock model as it stands, ArduPilot changes `FIFOEN` while `UE` = 1, a field RM0433 protects; a guard that serves `CR1` from its copy (`RUL-17`'s read rule) removes the change.** High confidence on the mechanism.
- No other configuration write after the first 5 ms; no `RDR` read (DMA reception, which no request moves on this platform: no `ReceiveDmaRequest` line).

### P5. The armed run (4400h `s03-arm-s`: Sentinel-03, ELF `dbf598d8`, 13 s, the request line added)
- USART1 as P4, plus USART2 (CRSF at 416,666 baud) and USART3 (MAVLink).
- **USART2 is where configuration changes:** ArduPilot's RC input sets its UART itself and may cycle baud rates and inversion. I predict each change goes through `UE` = 0 (ChibiOS stops and restarts the driver), so no protected field changes under `UE` = 1 beyond P4's read-back artefact. Held with moderate confidence; a change under `UE` = 1 here is a §7 stop.
- `RDR` is read by DMA1 stream 0 for each injected byte (4400h: 64 per frame); arming adds no USART register.

### P6. ArduPilot's receive path with the guard (WO §2)
- With the request raised as silicon raises it and `IDLE` raised one frame after the last received byte, ArduPilot's IDLE interrupt ends each frame and an unpadded frame is answered. This needs `IDLEIE` = 1, which only the copy-served `CR1` keeps (P4).

### P7. Consumer observers each write reaches (Aethelgard `fa12abf4`; the survey, its key lines read)
- **Every board, from the wrapper's form, not its rules:**
  - the `peripherals` listing must show each solved USART with kind exactly `STM32F7_USART` (`renode.rs`:2834–2842; the foreign judge the same, `foreign.rs`:1545–1553). A wrapper's class name changes it on every board with a serial port: the class map's consumer step (`RUL-09`), as for 1301's ports;
  - **the serial access parser** requires every `[INFO] usartN:` line of a logged port to be one whole access (`serial.rs`:347–391, `logged_by` :510; `renode.rs`:3830–3840). A guard's own witness and end records log under its name (`RUL-11`), so on the guarded twin the generated judge's write reconstruction fails closed (`serial_tag_mismatch`) on every board with a serial witness. A consumer step too (it must take Tviburi's records out first), but a new one: no earlier guard's port is parsed this way.
- **Unchanged by a wrapper that keeps the access log's register names and forwards `IUART`:** the serial witness (a file backend per port, `renode.rs`:587–612); `judge_writes`, which reads offset, width and value only; the injection (`WriteChar`, `anchor.rs`:433–467).
- **Changed by copies served on read:** the boot block reads `CR1` under a mask of the stored bits only, `CR2` under `STOP`, `CR3` under `DMAR`/`DMAT`, `BRR` under `0xFFFF` (`boot.rs`:931–1000). A copy-served `FIFOEN`, `M0`, `M1`, `IDLEIE` falls outside every mask, so the boot block is unchanged. Held with moderate confidence (the masks are the survey's).
- **ArduPilot:** `judge_options` holds the last `CR2` written with `UE` = 0 to its `SWAP`/`TXINV`/`RXINV` expectation; the boot block uses `CR1` under `UE|TE|PS|PCE` and `CR2` under `STOP`. Unchanged.
- **The ledger's USART rows** (`ledger.rs`:1498–1593) class `IDLEIE`, `FIFOEN` and the dropped `CR2` fields `ignored-by-model`: a modeled `IDLE` makes Aethelgard's row text stale (a consumer row, `OQ-04`).

### P8. The clock (WO (2))
- With `USARTxEN` = 0 the RCC guard locks the wrapper on the bus (it unlocks SPI guards only), so an access is silently dropped, as 1303 found for DMA. With `USARTxEN` = 1 and `USART16SEL`/`USART234578SEL` on a source that is not running, the stock model starts; the RCC guard's query answers "not running" for `usart1`–`usart3` and "not modeled" for the others.
- Neither firmware starts a USART with no running kernel clock.

### P9. Memory (`1201`'s budget, like for like)
- A thin wrapper for the eight USART/UARTs (not `lpuart1`): +5 to +12 MiB at the median, eleven boots a side, as 1303's thin wrapper (+8.0).

## 1. How it was measured (after §0 was committed at `dff6e77`)

**Image:** `aethelgard-tools@sha256:1e0ab105…`, the digest Aethelgard `main` `fa12abf4` pins (`container/IMAGE`, fetched this session). Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. One container at a time, under `~/bin/capped-run`, `nice -n 10`, `--network none`.
**Evidence:** `~/.cache/tvi-1305/` (`m1/`, `probe/`, `budget/`, `arm-usart-writes.txt`, the scripts in `h/`), kept until the architect says.

| Run | What | How |
|---|---|---|
| **M1** | the 14 generated boots, each replayed on `main`'s release (every guard recorded), the nine UARTs access-logged, each UART's `CR1` `CR2` `CR3` `BRR` `GTPR` `RTOR` `ISR` read after `RunFor` | `h/replay-u.sh` (the committed `tests/1301/replay-1301.sh` plus the logging; `diff` in the script's header) over 1303's captures at Aethelgard `1cb994c1`, which hold today's firmware and platforms (§0). Analysed by `h/ue-analyse.py` |
| **AP** | ArduPilot: the committed fixture (`aet-1c32c8ea-ardupilot`, ELF `a799dfb6`, `sentinel-02-own`), 5 s | as M1 |
| **ARM** | the armed run: Aethelgard `4400h`'s `s03-arm-s` (Sentinel-03, ELF `dbf598d8`, 13 s, armed at 9.0 s, `ReceiveDmaRequest -> dmamux1@41` added, `sysbus LogAllPeripheralsAccess` with reads dropped) | **not re-run:** its access log (`~/.cache/aet-4400h/run/s03-arm-s/console.txt.gz`, the 4400h builder's evidence, read-only) is the run the WO names. The ELF there hashes `dbf598d8…`, as `RESULTS.md` records |
| **M2** | the stock `STM32F7_USART` by probe | monitor-driven on `main`'s Nucleo board, `usart1`, its receive request wired to `dmamux1@41`, the RCC and DMA1 guards recorded (`probe/probes.body`, U0–U16) |
| **M3** | the form | the same body through a scratch wrapper (`probe/ProbeSTM32F7_USART.cs`: owns an unregistered `STM32F7_USART`; forwards every access, `IRQ`, `ReceiveDmaRequest`, the `IUART` surface and reset; a nested `Registers` enum; no rule), the eight USART/UARTs cancelled and restated (`probe/stm32h7-usart-restate.repl`) |
| **B** | the one-file memory, by `1201`'s budget | `tests/1301/ci-memory.sh`, eleven boots a side, interleaved, main against a scratch tree carrying M3's wrapper as all eight instances, whose replay moves the consumer's untyped `usartN:`/`uartN:` blocks onto the wrappers |

## 2. Results

### 2.1 The field table, in order against `UE`, `TE` and `RE` (M1, AP, ARM)

"Under `UE` = 1" means the last `CR1` written had `UE` = 1. A change is a protected field (P1's masks: `CR1` `0x33FF9E00`, `CR2` `0x000FFF30`, `CR3` `0x003EFB3E`, `BRR`/`GTPR` whole) whose value differs from the last word written. A write from a read-modify-write site is taken as a guard serving copies would see it: the word written plus the previous copy's bits the stock model drops (P4; `h/ue-analyse.py`).

**The 14 generated boots** (100 ms). Ten write a USART; four write none (`motors-dshot600`, `motors-pwm`, `sentinel-02-bench`, `sentinel-02-bench-hse8`).

| Board | Ports | Each port's writes, in order | Data |
|---|---|---|---|
| `d1-drv8876`, `driver-judges-nothing`, `sentinel-01`, `sentinel-01-ext-bmp581`, `sentinel-01-minimal` | `usart1` (`BRR` `0x22C`) | `BRR`; `CR2` = 0; `CR1` = `0x2000000C` (`FIFOEN`, `TE`, `RE`); `CR1` = `0x2000000D` (the same with `UE`) | 16–20 `TDR` writes; 8 `RDR` reads (the consumer's injected echo, one byte per 100 µs); 4,438–4,442 `ISR` reads |
| `hse-crystal-16`, `hse-external-8`, `sentinel-02-shaped` | `usart2` (`0x457`), `usart3` (`0x98`) | the same, per port | the same per port; 8,331 and 4,438 `ISR` reads |
| `sentinel-03` | `usart1` (`0x22C`), `usart2` (`0x98`), `usart3` (`0x457`) | the same | the same |
| `serial-dshot-9600` | `usart1` (`0x1A0B`, 9600 baud) | the same | 20 `TDR`, 8 `RDR` (one per 1.1 ms), 43,331 `ISR` |

- **No configuration field is written while `UE` = 1** on any port of any board, and no `CR3`, `GTPR`, `RTOR`, `RQR`, `ICR` or `PRESC` write. Every access is a word.
- **`FIFOEN` = 1 on every configured port:** the generated firmware runs the USART in FIFO mode, and the stock model drops the bit (reserved, M2 U2).

**ArduPilot, the fixture** (5 s, `usart1` only). ChibiOS's `usart_init` runs four times (3.1, 9.0, 93.9, 98.8 ms): `BRR` `0x271`, `CR2` `0x40` (`LBDIE`), `CR3` `0xC1` (`EIE`, `DMAR`, `DMAT`), `CR1` `0x2000013D` (`FIFOEN`, `PEIE`, `RXNEIE`, `IDLEIE`, `TE`, `RE`, `UE`), `ICR` all ones; then a read-modify-write of `CR1` that **reads `0x12D`** (the stock model drops `FIFOEN` and `IDLEIE`) and writes `0x10D`, clearing `RXNEIE`. On silicon the same code writes `0x2000011D`: `FIFOEN` and `IDLEIE` stay set. Then 547 `TDR` bytes, all moved by DMA1 (1303), and no `RDR` read.
- **With copies served, no protected field changes under `UE` = 1.** The three later `usart_init` calls run on the enabled port and rewrite `BRR`, `CR2`, `CR3` and `CR1` with the values it already holds (same values pass, `RUL-18`).

**The armed run, Sentinel-03** (13 s; `ARM`, 373 configuration writes; `TDR` 2,687 on `usart1`, 409 on `usart3`).
- **`usart1` (console, MAVLink, the injected port)** and the first `usart3` init: as the fixture. No change under `UE` = 1.
- **`usart2` (CRSF/RC input), every second from 0.2 s, ArduPilot's RC-protocol detection cycles the port:** `BRR` `0x271`, `0x2D0`, `0x168`, `0xAD`, `0x24` (115,200; 100,000; 200,000; 416,666; 2,000,000 baud at 72 MHz) and, for SBUS, 8E2 inverted (`CR1` `M0` + `PCE`, `CR2` `RXINV` + `STOP` = 10). Two code paths:
  - **ChibiOS `sdStop` then `sdStart`** (`CR1`/`CR2`/`CR3` = 0 at `0x8105C12`, then `usart_init`), and **ArduPilot's `set_options`** (`CR1` `UE` cleared at `0x80FE514`, `CR2` `RXINV`, `CR3`, `UE` set again): both with `UE` = 0. Valid.
  - **`usart_init` on a port already enabled** (`sdStart` without `sdStop`): `BRR`, `CR2`, `CR3`, `CR1` written with `UE` = 1. **16 protected changes on `usart2` in 13 s:** `BRR` 9 times (0.20, 2.21, 3.21, 4.21, 5.21, 6.21, 8.22, 9.22, 10.23 s), `CR1` `M0`/`PCE` cleared 4 times (2.21, 3.21, 8.22, 9.22 s), `CR2` `RXINV` cleared twice (4.21, 10.23 s), `CR1` `FIFOEN` set once (0.20 s). The first is at **0.2018 s: `BRR` 0 → `0x271` with `UE` = 1** (the port was started with `BRR` = 0 one write earlier).
- **`usart3` (MAVLink2, `SERIAL2`):** `BRR` `0x4E2` → `0x271` at 0.611 s and back at 0.616 s, each by `usart_init` with `UE` = 1.
- **ChibiOS's `sdStop` writes `CR1` = 0 from `UE` = 1** (26 times on `usart2`): one write clears `UE` and `FIFOEN` (and `M0`/`PCE` after an SBUS step) together. Whether such a write changes a protected field under `UE` = 1 is a reading (§5 Q2 (b)).
- **On silicon, each change under `UE` = 1 is ignored** ("can only be written when the USART is disabled (UE = 0)": `BRR` p2082, `M0` p2069, `PCE` p2069, `RXINV` p2075, `FIFOEN` p2067), so the RC port keeps its previous baud and framing, and `usart3` keeps 57,600 baud through the 5 ms in between. The stock model takes each at once (M2 U3). **That silicon ignores them is the manual's statement, not a bench result** (§5 Q2 (c)).

### 2.2 The stock model, by probe (M2)

| # | Probe | Stock (measured) | Silicon (RM0433 Rev 9) |
|---|---|---|---|
| U0 | `CR1` written with `USART1EN` = 0 | dropped by the RCC guard's bus lock; reads 0; **no Tviburi record** | ignored (p373) |
| U1 | reset reads | `ISR` `0x200000C0`; `GTPR` (0x10) and `PRESC` (0x2C) "Unhandled read" | `ISR` `0x0X80_00C0` / `0x0000_00C0` (FIFO on / off, p2085, p2091); `GTPR`, `PRESC` defined (pp. 2082, 2099) |
| U2 | all ones written to each register with `UE` = 0 | `CR1` reads `0x040087EC`: stored `RE`, `TE`, `RXNEIE`, `TCIE`, `TXEIE`, `PEIE`, `PS`, `PCE`, `OVER8`, `RTOIE`; **dropped** `UESM`, `IDLEIE`, `WAKE`, `M0`, `MME`, `CMIE`, `DEDT`, `DEAT`, `M1`, `EOBIE`, and `FIFOEN`, `TXFEIE`, `RXFFIE` (reserved in the model). `CR2` reads `0x3000` (`STOP` only). `CR3` reads `0xC0` (`DMAR`, `DMAT` only). `BRR` 16 bits. `RTOR` 24 bits. `GTPR`, `PRESC` unhandled | every field stored (§48.8) |
| U3 | `M1`, `OVER8`, `PCE`, `BRR`, `STOP` written with `UE` = 1 | **all taken** (`OVER8`, `PCE`, `BRR`, `STOP` read back; `M1` dropped as a tag); no record | ignored (pp. 2068–2069, 2076, 2082) |
| U4 | a byte write to `CR1`, a half-word to `CR1`+2 | taken (the bus translates to the word) | "accessed by words (32 bits)" (p2067) |
| U5 | a byte received with `RE` = 0 | dropped (DEBUG line) | not received |
| U6 | three bytes received, `IDLEIE` = 1, then 1 ms, the bytes read, 1 ms | `RXNE` set; **`IDLE` never set** | `IDLE` one idle frame after the last byte (p2090, p2021) |
| U7 | `ICR` all ones | clears `TC` only (`IDLECF`, `ORECF` and the rest tags) | clears each flag (p2096–2097) |
| U8 | 20 bytes received, none read, `FIFOEN` = 1 | **all 20 kept and read back in order; `ORE` never set** | 16-deep RX FIFO (p2018), then `ORE` and the byte lost (p2090) |
| U9 | `RTOEN` = 1, `RTO` = 10, `RTOIE` = 0, then `RTOIE` = 1 | `RTOF` not set with `RTOIE` = 0; set with `RTOIE` = 1 | set by `RTOEN` whatever `RTOIE` (p2088) |
| U10 | receive by DMA: stream armed, then `DMAR`, then three bytes | **all three moved** (`NDTR` 8 → 5) | moved after each `RXFNE` (p2048) |
| U11 | a byte arrives with the stream disabled; the stream is enabled; a second byte | **neither byte is ever moved** (`NDTR` stays 8; both stay readable in `RDR`): the request is raised only on the buffer's change from empty | the request stays asserted while `RXFNE` = 1, so both move once the stream runs (p2048; the level is from memory) |
| U12 | a byte waits; then `DMAR` = 1 | **not moved** | moved (as U11) |
| U13 | `UE` cleared with a byte waiting | `RXNE` stays set; a byte received then is dropped | "The DMA requests are also reset when UE = 0" (p2070) |
| U14 | `TDR` with `TE` = 0; with `TE` = 1 | dropped with a WARNING; sent, `TC` set at once, `TEACK` = `TE` | — |
| U15 | `RQR.RXFRQ` with two bytes waiting | both flushed | flushes (p2085) |
| U16 | one byte, then 1 µs, then 100 µs (a frame is 31 µs at the board's 200 MHz / 625) | `IDLE` never set | set after one idle frame |

### 2.3 The form (M3) and the form control

- **`STM32F7_USART` is `sealed`,** so the guard is `RUL-07`'s wrapper. The probe loads cancelled and restated (`usartN: @none`; `tviburiUsartN: … as "usartN"`, registered on `tviburiRcc` with the stock alias, `frequency` and `IRQ -> exti@N`); `peripherals` lists `usart1 (ProbeSTM32F7_USART)`.
- **M2 through the wrapper is identical to the stock run** (every value read and every access-log line, register names included), apart from two prices: the owned instance's own WARNING lines lose their source name (`[WARNING] Unhandled write …`), and its DEBUG lines (U5, U13) follow the owned instance's log level, not the wrapper's.
- **The access log keeps its names** (`ControlRegister1`, `TransmitData`, …) through the wrapper's nested `Registers` enum: `RegisterMapper` reads the registered peripheral's type. 1303's wrapper logged `unknown`.
- **`IUART` forwarded** (`WriteChar`, `CharReceived`, `BaudRate`, `StopBits`, `ParityBit`): the monitor's `sysbus.usart1 WriteChar` (the consumer's injection, `anchor.rs`:433–467) reaches the owned instance, and `CreateFileBackend` (the consumer's serial witness) binds to the wrapper.
- **The form control:** the 14 generated boots and ArduPilot's fixture boot replayed on the wrapper tree against M1 (main). **Identical on all 15:** every USART access line (stamp, PC, register name, value; 587 to 43,363 lines a boot), the boot block (instruction count included) and the 63-value register dump.
- **The consumer's untyped `usartN:`/`uartN:` blocks** must move onto the wrapper's variable (`RUL-09` (b)): every captured platform has one per instance (generated: all nine, `lpuart1` included; ArduPilot: eight). The replay moves them, as for the timers.

### 2.4 Consumer observers each write reaches (Aethelgard `fa12abf4`)

By board: the ten generated boards with a serial port (§2.1), ArduPilot's two (fixture: `usart1`; Sentinel-03: `usart1`–`usart3`); none on the four boards without one.

| Observer | Reads | Changed by the wrapper's form | Changed by the guard's rules |
|---|---|---|---|
| `peripherals` listing (`renode.rs`:2834–2842; `foreign.rs`:1545–1553) | each solved USART's kind, exactly `STM32F7_USART` | **yes, every board with a port:** the class name. The class map's consumer step (`RUL-09`), as 1301's ports and 1302's timers | — |
| serial access parser (`serial.rs`:347–391, `logged_by` :510; `renode.rs`:3830–3840) | every `[INFO] usartN:` line of a logged port must be one whole access | no (M3) | **yes, every generated board with a port:** the guard's witness and end records log at INFO under `usartN` (`RUL-11`), so the write reconstruction fails closed (`serial_tag_mismatch`). A consumer step, new to this guard: Aethelgard must take Tviburi's records out of the port's lines first (`RUL-11` names the record grammar) |
| serial witness (a file backend per port, `renode.rs`:587–612) | the transmitted bytes | no (M3) | no: `TDR` is passed through |
| `judge_writes` (`serial.rs`:731), `judge_options` (:971) | offset, width and value of each write | no | no: a guard never changes what the firmware writes |
| boot block (`boot.rs`:931–1000) | `CR1` under the stored bits, `CR2` under `STOP`, `CR3` under `DMAR`/`DMAT`, `BRR` under `0xFFFF` | no (§2.3) | no: copies served for `FIFOEN`, `M0`, `M1`, `IDLEIE`, `RXINV` fall outside every mask |
| injection (`anchor.rs`:433–467) | `WriteChar`, one byte per 100 µs | no | `IDLE` set one frame after each byte; the generated driver polls bits 7 and 5 only (`board.rs`:4803–4905) |
| ledger rows (`ledger.rs`:1498–1593) | static text: `IDLEIE`, `FIFOEN`, the dropped `CR2` fields `ignored-by-model` | — | a modeled `IDLE` and served copies make that text stale (a consumer row, `OQ-04`) |
| ArduPilot's console (`usart1` `TDR`, by DMA) | — | no (M3, AP) | no, unless a rule refuses |

### 2.5 The clock (WO (2))

- **`USARTxEN` = 0:** an access is dropped by the RCC guard's bus lock, a read returns 0, with no Tviburi record (U0): silicon's ignored write (p373). Covered as silicon's, as 1303 found for DMA.
- **Every port of every run starts with its kernel clock running:** at each port's first `UE` its `*EN` is set and its selector's source runs (generated: the reset selectors, `rcc_pclk1`/`rcc_pclk2`; ArduPilot, both boards: `USART16SEL` = `USART234578SEL` = 001, `pll2_q_ck`, with `PLL2ON` and `DIVQ2EN` set at 3.1 ms). The RCC guard's `QueryKernelClock` answers "running" for each used port at the end of every run (`fc/*/…console.txt`, `Q-KERNEL`).
- **The query covers `usart1`–`usart3` only.** `usart6`, `uart4/5/7/8` answer "not modeled"; the RCC guard already allows their `*EN` only at 0, so their start refuses at the RCC first.
- **A kernel clock lost under a running USART** is not refused: the RCC guard's `TVI-RCC-CLOCK` watches its registered SPI guards only (§5 Q3).
- **RM0433 says little on the USART's side:** the registers stay accessible "even when the usart_ker_ck clock is stopped" (p2019); no sentence says what reception or transmission do without it. The refusal rests on p371 ("no clock will be provided to the peripheral"), as SPI's did.

## 3. Predictions, marked

| Prediction | Verdict |
|---|---|
| P1 | held, read field by field (§0, `rm/usart-fields.md`). Not predicted: `M1`/`M0`/`OVER8`/`PCE`/`PS` exist identically in both `CR1` views; `RTOEN` carries no `UE` condition; the access width is "by words" (p2067); the p2040 sentence on `IDLE` at a start |
| P2 form, tags, no `UE` rule, `IDLE` never set, no FIFO or overrun, request on a change only | held (U0–U16) |
| P2 `RTOF` | held in kind (set only with `RTOIE`); the 8× timing is by source, not probed |
| P2 reset `ISR` `0x200000C0` | held |
| P3 | **held, one detail wrong:** each port writes `CR2` = 0 explicitly, and `RE` and `FIFOEN` are set; the firmware also receives (8 bytes a port, polled) |
| P4 | **held:** the read-modify-write reads `0x12D` and writes `0x10D`; with copies, no protected change. **By analysis, not by run:** no copy-serving prototype was booted, so that ArduPilot's control flow is unchanged when `CR1`–`CR3` read back silicon's values is a prediction for the build, which boots the guard recorded on the fixture and the armed capture |
| P5 USART1, USART3's first init | held |
| P5 "each USART2 change goes through `UE` = 0" | **wrong:** 16 changes on `usart2` and 2 on `usart3` are made with `UE` = 1 by ChibiOS's `usart_init` on a running port (§2.1). This is §5 Q2 (c) |
| P6 | not measured: it needs the guard (§5 Q5, Q6) |
| P7 | held, with the access parser's whole-line rule added (§2.4) |
| P8 | held (§2.5) |
| P9 | **wrong in sign:** the thin wrapper measured −6.5 MiB (§4), inside the noise |

## 4. The one-file memory, by `1201`'s budget (B)

**A prototype figure, not the guard's:** M3's wrapper as the eight USART/UARTs in every branch boot, main `dff6e77` (main `1e9ce55` and this document) against the same tree plus the wrapper, its restatement, and a replay that moves the consumer's untyped blocks. ArduPilot's 5 s boot (ELF `a799dfb6`, rebuilt by the harness and checked), eleven boots a side, interleaved, on Furnace (load 3–9).

| | Peaks (KiB), in boot order | Median |
|---|---|---|
| `main` | 1,022,448; 1,015,168; 1,015,856; 1,032,232; 1,013,100; 983,436; 1,012,148; 1,016,504; 1,004,732; 1,000,776; 1,021,612 | 1,015,168 |
| `main` + the wrapper ×8 | 1,016,232; 1,024,660; 1,058,304; 1,006,212; 983,140; 1,007,408; 1,011,868; 1,000,368; 1,005,816; 1,008,524; 1,029,552 | 1,008,524 |

`MEDIANS main 1015168 KiB, branch 1008524 KiB, difference -6644 KiB (-6.5 MiB)`, **PASS**; every branch boot under the 1,572,864 KiB ceiling; walls 10.5–12.3 s. The wrapper costs nothing this measurement can see: main already collects the overlay's compile garbage at its first start (1302). The built guard (rules, a field table, scheduled `IDLE`, records) will cost more; the build reports the budget again, in CI.

### 2.6 ArduPilot's receive path, end to end (after the advisor's review; M2b and ArduPilot's source)

- **ArduPilot's code** (`/opt/ardupilot/libraries/AP_HAL_ChibiOS/UARTDriver.cpp` in Aethelgard's pinned ArduPilot image `e1809b90…`, sha256 `cdc7e370…`): on the H7, `rx_irq_cb` (the USART interrupt, with receive DMA on) clears the RX stream's `EN`, "triggering DMA transfer complete interrupt"; `rxbuff_full_irq` (the DMA's `TCIF`) takes `64 − NDTR` bytes, then re-arms the stream on the other bounce buffer (`dma_rx_enable`: new `M0AR`, `NDTR` = 64, `EN`).
- **Silicon:** "A transfer complete interrupt flag (TCIF …) is set to indicate the end of transfer due to the stream interruption" (§15.3.16 "DMA transfer suspension", p654); a re-enabled stream starts at its programmed base address (p666).
- **The stock DMA, by probe (`probe/probes-dma.body`, through main's DMA guard, recorded):** three bytes moved (`NDTR` 64 → 61); the software disable **leaves `TCIF0` clear** (`LISR` 0); the re-arm on `0x24000100` fires the DMA guard's **`TVI-DMA-OFFSET`** (`unmodeled`), and the stock model writes the next bytes at the **old offset in the new buffer** (`0x44` at `0x24000103`).
- **So an unpadded frame cannot reach ArduPilot through a USART guard alone.** `IDLE` (this guard) raises the interrupt and the stream is disabled, but no `TCIF` follows, so `rxbuff_full_irq` never runs; and the re-arm refuses in the DMA guard. Both are the DMA guard's (§5 Q6). The armed run on a fully guarded twin also meets main's DMA guard first: Sentinel-03 routes `usart2` RX (43), `usart3` RX (45) and TX (46) through DMAMUX1 (32,585 `C2`/`C6` writes in 13 s), and `RaisedToMemory`/`RaisedFromMemory` list none of them (`TVI-DMA-REQUEST`, `unmodeled`).
- **Every `TDR` write in ArduPilot's runs is a byte** (`WriteByte`, the DMA's `PSIZE` = byte: 2,687 on `usart1` in the armed run), where RM0433 says "The peripheral registers have to be accessed by words (32 bits)" (p2067). 1302/1303 refuse sub-word accesses `unmodeled`; here that would refuse every ArduPilot transmission (§5 Q7).

## 5. The stop (WO §1.1, §7), and the questions

**Stops.** §1.1's stop, with the measurement. Two findings bear on §7 and the acceptance:
- **The armed run would refuse under the WO's own rule** (Q2 (c)): ChibiOS's `usart_init`, run on an enabled port, writes `BRR`, `M0`/`PCE` and `RXINV` with `UE` = 1, first at 0.2018 s (`usart2`). RM0433 says those writes are not taken (they "can only be written when the USART is disabled"), so this is not §7's "a path RM0433 says is valid". But the WO's rule and its acceptance ("the armed run show[s] no USART `would-refuse` outside the acknowledged table") cannot both hold, and `RUL-17`'s acknowledgement does not cover a `predicted-fault`.
- **The receive acceptance runs through the DMA guard** (Q6): an unpadded frame needs `TCIF` on a software disable and a clean re-arm, both DMA-side, outside this fence (§2.6).

Nothing in the generated boots or ArduPilot's fixture boot would refuse under the rules proposed below; no consumer observer changes from the rules, and two change from the form (§2.4).

**Q1. The form.** `STM32F7_USART` is sealed (§2.3).
- **Proposal:** `RUL-07`'s wrapper, `GuardedSTM32F7_USART`, owning an unregistered stock instance; forwarding every access, `IRQ`, `ReceiveDmaRequest`, the `IUART` surface and reset; a nested `Registers` enum so the access log keeps its names. The stock instances cancelled and restated on `tviburiRcc` with their alias, `frequency` and `IRQ -> exti@N` (`model/stm32h7-usart-restate.repl`, generated from the pinned file, as 1302/1303). The consumer's untyped `usartN:` blocks move onto the guard's variable (`RUL-09` (b)), as the replay does.
- **Which instances:** `usart1`, `usart2`, `usart3` (the RCC query covers their clocks; every committed board uses only these), or all eight USART/UARTs (`usart6`, `uart4/5/7/8` would answer every start `unmodeled`, after the RCC guard already refuses their `*EN`). **I propose all eight**, so that every port is guarded by one rule set; the memory cost of eight was nil (§4). `lpuart1` stays stock (the WO's NOT: LPUART1; it is declared without `lowPowerMode`, a `stock-wrong` row).
- **Prices, named for the consumer:** the `peripherals` kind (`STM32F7_USART` → `GuardedSTM32F7_USART`, the class map), the guard's INFO witness and end records on the port's own source line, which Aethelgard's serial access parser rejects as not an access (§2.4: a new consumer step), and the owned instance's WARNING lines without a source name.
- **Ask:** this form, and three or eight instances?

**Q2. Fields protected while `UE` = 1** (`predicted-fault`, per field, cited by page).
- **(a) The fields:** `CR1` `FIFOEN` (p2067), `M1`, `DEAT`, `DEDT`, `OVER8` (p2068), `M0`, `WAKE`, `PCE`, `PS` (p2069); `CR2` `MSBFIRST`, `DATAINV`, `TXINV`, `RXINV` (p2075), `SWAP`, `LINEN`, `STOP`, `CLKEN`, `CPOL` (p2076), `CPHA`, `LBCL`, `LBDL`, `ADDM7` (p2077), `ADD` with `RE` = 1 (p2074), `ABRMOD` with `ABREN` = 1 (p2075); `CR3` `WUS`, `SCARCNT` to a non-zero value, `DEP` (p2079), `DEM`, `DDRE`, `OVRDIS`, `ONEBIT`, `CTSE` (p2080), `RTSE`, `SCEN`, `NACK`, `HDSEL`, `IRLP`, `IREN` (p2081); `BRR` (p2082), `GTPR` (p2083), `PRESC` (p2099). The same value passes (`RUL-18`). The field keeps its old value (what recorded mode passes on). `UE` set together with a protected change, from `UE` = 0, is a configuration and passes (the generated firmware's `0x2000000C` → `0x2000000D` is the same word plus `UE`).
- **(b) A write that clears `UE`.** ChibiOS's `sdStop` writes `CR1` = 0 from `UE` = 1, clearing `FIFOEN` (and `M0`/`PCE` after an SBUS step) in the same write (26 times on `usart2`). RM0433 does not say whether such a write takes its other fields. **Recommendation:** judge it as written with `UE` = 0 (it passes), naming the reading in the guard; the alternative refuses every `sdStop` on a FIFO port, which RM0433 does not show to be wrong.
- **(c) `usart_init` on an enabled port** (§2.1): 16 changes on `usart2`, 2 on `usart3`, in the armed run only. Options:
  - **(i) `predicted-fault`, as the WO says,** with a bench row (`RUL-18`): on the Nucleo, USART3 at 115,200 baud, `BRR` written for 9,600 with `UE` = 1, then a byte sent and its bit time measured, expecting 115,200 (from the manual; no result). The armed run then refuses at 0.2018 s, and the acceptance is amended to say so: a finding for ArduPilot/ChibiOS, not the twin.
  - **(ii) `unmodeled` until the bench row has a result** ("the twin cannot say"): the armed run still refuses, attributed to the twin.
  - **(iii) Acknowledge by name,** with the stock behaviour (the write taken at once): the armed run passes; a row.
  - **No recommendation on silicon's behaviour:** the manual is clear, and I hold no bench result. Recorded mode keeps the old value (as on silicon under (i)), so in a recorded armed run the RC port stays at its first baud, which changes nothing the twin can see (nothing is injected on `usart2`).
- **Ask:** the field list, (b), and (i)/(ii)/(iii).

**Q3. A start with no kernel clock** (WO (2)).
- **Proposal:** at a start (`UE` rising with `TE` or `RE` set, or `TE`/`RE` rising with `UE` = 1), ask the RCC guard's existing `QueryKernelClock` with the port's own name, as `1300a` wired SPI1's: not running is `predicted-fault` (p371; p2019 `usart_ker_ck`; the query's own citation); not modeled is `unmodeled`. A test shows the clock-off case covered by the bus lock with no record (U0), and the query's answer at each start; no RCC code is added.
- **Not covered:** a kernel clock lost while a USART runs (the RCC guard's `TVI-RCC-CLOCK` watches SPI guards only). A row for the RCC guard.
- **Neither firmware starts a USART without its clock** (§2.5).

**Q4. Reads.** `CR1`, `CR2`, `CR3`, `GTPR`, `PRESC` from the guard's copies (`RUL-17`), so ArduPilot's read-modify-write keeps `FIFOEN` and `IDLEIE` as on silicon (§2.1); `BRR`, `RTOR`, `RDR`, `TDR` from the stock model; `ISR` from the stock model with the guard's `IDLE` (Q5). The boot block's masks exclude every copy-served bit (§2.4). **Ask:** accept.

**Q5. `IDLE`, modeled** (p2090, p2021).
- **Proposal:** after each byte the port receives, one idle frame later (1 start bit, the data bits by `M`, the stop bits by `STOP`, at the stock model's baud, `frequency / BRR` and `OVER8`; baud accuracy is Aethelgard's), if no newer byte arrived and `UE` and `RE` are set, the guard sets `IDLE`; with `IDLEIE` = 1 it raises the port's interrupt (the guard ORs its own source into `IRQ`). `ICR.IDLECF` clears it. It is not set again until a byte is received (p2090). Virtual time, by `ScheduleAction`, as the stock model's receiver timeout.
- **At a start with nothing received:** p2040 says "If the USART is activated while the line is IDLE, the idle state is detected after the duration of one IDLE frame", in the mute-mode section. **Recommendation:** not modeled at a start (`IDLE` only after a received byte), a row naming p2040; or the architect rules it applies generally.
- **Mute mode** (`MME` = 1) refuses `unmodeled` (Q8), so p2090's `RWU` condition does not arise.

**Q6. The receive DMA request, and the DMA side.**
- **The request, proposed** (p2048, p2070): the guard drives `ReceiveDmaRequest` itself: asserted while `DMAR` = 1, `UE` = 1 and a byte waits; re-asserted after each `RDR` read that leaves a byte, at `DMAR` set with a byte waiting (U12), and once per frame time while a byte waits (U11: the stream may be enabled after the byte arrived, and the guard cannot see the DMA's `EN`; silicon's request is a level, from memory); reset at `UE` = 0 (p2070). `DMAR` with `RE` = 0 raises nothing (no byte is received).
- **What blocks the WO's receive acceptance (outside this fence):**
  - `TCIF` on a software disable (p654): the stock DMA does not set it (§2.6), so ArduPilot never takes the frame;
  - the re-arm on the other bounce buffer: main's DMA guard refuses it `TVI-DMA-OFFSET`, and the stock model writes at the old offset;
  - withdrawing 1303's acknowledgement of request 41 means editing `RaisedToMemory`'s row in `GuardedSTM32DMA.cs`, and the armed run's 43, 45 (RX) and 46 (TX) are not in its lists at all.
- **Ask:** (a) a DMA work order (a `1303a`) for the three, with this one building the USART side and testing the request and `IDLE` at the USART's own boundary (a planted firmware that receives by DMA with the stream armed first, U10, and `IDLE` read by polling); or (b) widen this fence to `GuardedSTM32DMA.cs` for those three items.

**Q7. Access width.** "The peripheral registers have to be accessed by words (32 bits)" (p2067), yet ArduPilot's DMA writes `TDR` by bytes (§2.6), as does any byte DMA to a UART. **Proposal:** a byte or half-word access to `TDR` or `RDR` passes (the data register's low byte is the datum; a byte DMA to it is the normal use, from memory); any other sub-word access `unmodeled` (1302/1303's precedent). **Ask:** accept, or refuse all sub-word accesses.

**Q8. The allow-list (`RUL-17`), proposed classes.**
- **Modeled by the guard:** `CR1.UE`, `TE`, `RE` (the start, Q3); every Q2 field's protection; `IDLEIE`, `ISR.IDLE`, `ICR.IDLECF` (Q5); `CR3.DMAR` (Q6); the copies served (Q4).
- **Inherited, probed:** `BRR`; `CR1` `RXFNEIE`, `TCIE`, `TXFNFIE`, `PEIE` (stored; the stock interrupts); `M0`/`M1` = 00 (8 bits), `OVER8`, `PCE` = 0; `CR2.STOP`; `CR3.DMAT`; `TDR`, `RDR`; `ICR.TCCF`; `RQR.RXFRQ` (U15).
- **Acknowledged by name** (a firmware writes them; the twin delivers bytes whole):
  - `FIFOEN` = 1 (every port of both firmwares): the receive queue is unbounded and `ORE` never sets (U8); the transmit side never fills. A firmware that falls 16 bytes behind passes;
  - `M0` = 1 with `PCE` = 1 (ArduPilot's SBUS probe, 8E): parity is neither generated nor checked;
  - `CR2.RXINV` (the SBUS probe) and `STOP` = 10: the twin's line has no polarity or framing; `STOP` enters only `IDLE`'s frame time;
  - `CR2.LBDIE` and `CR3.EIE` (ChibiOS sets both on every port): no LIN break, framing, noise or overrun error is ever raised, so neither interrupt fires;
  - `ICR` written all ones (ChibiOS, every init): `PECF`, `FECF`, `NCF`, `ORECF`, `IDLECF`, `TCCF` and the rest clear flags the twin never sets (or sets, for `IDLE`/`TC`); its reserved bits are ignored (write-only, from memory).
- **`unmodeled` at any value but reset:** LIN (`LINEN`, `LBDL`, `LBCL`), smartcard (`SCEN`, `NACK`, `SCARCNT`, `GTPR`, `EOBIE`, `TCBGTIE`), IrDA (`IREN`, `IRLP`), synchronous and SPI slave (`CLKEN`, `CPOL`, `CPHA`, `SLVEN`, `DIS_NSS`), auto baud (`ABREN`, `ABRMOD`, `RQR.ABRRQ`), modem and RS-485 (`RTSE`, `CTSE`, `CTSIE`, `DEM`, `DEP`, `DEAT`, `DEDT`), mute mode and address match (`MME`, `WAKE`, `ADD`, `ADDM7`, `CMIE`, `RQR.MMRQ`), wake-up from Stop (`UESM`, `WUS`, `WUFIE`), `HDSEL`, `SWAP`, `TXINV`, `DATAINV`, `MSBFIRST`, `ONEBIT`, `OVRDIS`, `DDRE`, `RTOEN`, `RTOIE` and a non-zero `RTOR` (the stock receiver timeout ignores `RTOEN` and runs 8× long), the FIFO interrupts and thresholds (`RXFFIE`, `TXFEIE`, `RXFTIE`, `TXFTIE`, `RXFTCFG`, `TXFTCFG` ≠ 000), `M` = 01/10/11 (7 or 9 data bits), `OVER8` = 1 (stock stored; to probe if a firmware needs it), `PRESC` ≠ 0, `RQR.SBKRQ`, `TXFRQ`, and any write to a reserved field or offset.
- **Predicted:** neither firmware's boot nor the armed run writes a field this list refuses, apart from Q2 (c).

**Q9. Fences and harness.**
- **(a) The guard-name lists** add the instances (Q1) to every list: `scripts/tviburi-spike`, `tests/1300/rcc.sh`, `tests/1301/gpio.sh`, `tests/1302/timer.sh`, `tests/1303/dma.sh`, `tests/spike.sh` (six), and `tests/mutants.tsv` G17. The WO's fence names them.
- **(b) The replay** (`tests/1301/replay-1301.sh`) appends the USART restatement and moves the consumer's untyped `usartN:`/`uartN:` blocks onto it, as for the timers (inside `tests/`).
- **(c) The class row:** `class UART.STM32F7_USART Tviburi.GuardedSTM32F7_USART <instances>`.
- **(d) Where the armed-run check lives:** CI has only the fixture ELF (`a799dfb6`, `sentinel-02-own`, 5 s, built in CI). The Sentinel-03 ELF (`dbf598d8`) is neither committed nor built by CI. **Proposal:** the armed run measured on Furnace in the build, from the 4400h capture, recorded, and reported in FEEDBACK; CI runs the fixture boot recorded (no USART `would-refuse`) and the planted firmwares.

**Rows, outside this WO:**
- **The DMA side of reception** (Q6): `TCIF` on a software disable (p654), the re-arm offset (p666), and requests 43, 45, 46 in the DMA guard's lists. Until then, a foreign firmware that receives by DMA with the IDLE path hears nothing unpadded.
- **`lpuart1`** is declared as a full USART (no `lowPowerMode`); LPUART1 is out of this WO.
- **The receive line has no timing:** injected bytes arrive at once (`WriteChar`), so a firmware that reads too slowly is never overrun.
- **A kernel clock lost under a running USART** (Q3), for the RCC guard.
- **For Aethelgard (findings, not changes):** its serial access parser will reject a guarded port's records (§2.4); ChibiOS's `usart_init` on an enabled port writes `BRR` and framing with `UE` = 1, so ArduPilot's RC-protocol probing on an H7 may not switch baud on silicon (the manual's statement; no bench result).
