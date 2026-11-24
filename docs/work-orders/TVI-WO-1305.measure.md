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
