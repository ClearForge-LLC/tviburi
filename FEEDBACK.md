# TVI-WO-0100 feedback

Producing agent: Build.
Base: `663e2b32f9569099188dfb81d9c6f931d7dc4533` (live main, the WO merge). The header's `04cdc510` was not used.
Host: Renode 1.17.0, installed outside this repo. The v1.17.0 infrastructure submodule is `066a7f13c052215632d469c995c89aea37c573b1`. That tree is not checked in.

## Spike answers

### 1. Can we get an STM32H7 platform without pulling in the Renode tree?

Yes. `renode/nucleo_h753zi_tviburi.repl` is an overlay. It `using`s the host file `platforms/boards/nucleo_h753zi.repl` and registers one peripheral. The refusal shim is `renode/Tviburi_STM32H7_SPI1.cs`, loaded with Renode's `include` and compiled in-process. The Renode source tree is not in this repo.

### 2. Where is the narrowest refusal insertion?

At SPI1's address, `0x40013000`, size `0x400`, in the overlay repl:

```
spi1: SPI.Tviburi_STM32H7_SPI1 @ sysbus 0x40013000
```

On the 1.17.0 NUCLEO-H753ZI, SPI1 is not an `STM32H7_SPI`. `platforms/cpus/stm32h7.repl` only tags `<0x40013000, 0x400133FF>` as `SPI1_/_I2S1`. The bus handles a registered peripheral before that tag, so the tag's silent success stops as soon as this type occupies the address. The CPU, the bus, and `STM32H7_SPI` are untouched. `DefineRegisters` is private, and `WithTaggedFlag` is not a virtual hook, so a subclass cannot replace MODF without copying the register map. That would be a rewrite, which this spike does not do.

### 3. Which path, and what does the refusal say?

The path is a 32-bit write of `0x1` to SPI1 `SPI_CR1` (offset `0x00`), which sets `SPE`. On the H753 that raises `SPI_SR.MODF` (bit 9) and clears MASTER. This model does not do that.

H7 positions, measured in `STM32H7_SPI.cs` from the 1.17.0 pin: `SPE` is `SPI_CR1` bit 0, `MODF` is `SPI_SR` bit 9. MASTER is `SPI_CFG2` bit 22. The older SPI's MODF bit 5 is not this peripheral.

The invocation exits 1. The refusal line is:

```
Tviburi refusal: peripheral=SPI1 register=SPI_SR.MODF bit9 condition=SPI_CR1.SPE set; silicon raises SPI_SR.MODF and clears MASTER; this model does not model_version=tvi-wo-0100+renode-1.17.0 model_hash=e21ebf69a95a9d37e89c4a1f485126653df0315978ce03283e8ad80b16f8cc8f
```

The four fields are:

| Field | Value |
|---|---|
| Peripheral | `SPI1` |
| Register or bit | `SPI_SR.MODF bit9` |
| Condition | `SPI_CR1.SPE set; silicon raises SPI_SR.MODF and clears MASTER; this model does not` |
| Model version / hash | `model_version=tvi-wo-0100+renode-1.17.0`, `model_hash=e21ebf69a95a9d37e89c4a1f485126653df0315978ce03283e8ad80b16f8cc8f` |

`model_hash` is the SHA-256 of `renode/Tviburi_STM32H7_SPI1.cs`. `renode/run-proof.sh` recomputes it and requires the refusal text to contain it. The resc sets `TVI_MODEL_SOURCE` to `$ORIGIN/Tviburi_STM32H7_SPI1.cs` before the platform loads, because Renode's process directory is the host install, not the repo. If that variable is unset or the file is missing, construction refuses and the process exits 1.

Every other SPI1 access refuses as well. SPI1 has no modeled register, so a read that returned 0 would be silent success. The named proof is the SPE write. A read of `SPI_TXDR` at `0x40013020` is the check that a non-SPE access also refuses.

### 4. Does the log still record the refused access?

Yes. `sysbus LogPeripheralAccess spi1` runs first, then the shim logs and flushes, then the process exits. The log contains both:

```
spi1: WriteUInt32 to 0x0 (unknown), value 0x1
Tviburi access: peripheral=SPI1 offset=0x0 kind=write value=0x1 width=4
```

`logFile` is opened with flush-after-write. The trace format is Renode's existing log. It was not changed.

## What Renode did that the architecture did not describe

- SPI1 on this board is a bus tag, not a partial SPI model. The tag warns and completes the access (read returns 0, write is dropped). SPI4 is the instance that is actually `SPI.STM32H7_SPI`.
- `WithTaggedFlag("MODF", 9)` on that real model logs an unhandled write at warning level and the access still succeeds. There is no built-in refusal that carries peripheral, register, condition, and model hash.
- `UnhandledAccessBehaviour.ThrowException` throws a generic bus exception and is not the default.
- A `RecoverableException` aborts the rest of the script and is then swallowed. With `--console` and stdin closed, the process exits 0. The shim calls `Environment.Exit(1)` after the log flush so the invocation itself is a failure.
- `--disable-gui` with no port flag listens on telnet port 1234 and waits. The proof passes `-P -1` and uses `--console`.
- The H7 MODF bit is 9, not the older SPI's bit 5.

## OQ-02

Yes. A ruling is needed before the next work order. This spike did not need a vendor drop or a submodule.

The layout that worked is an external Renode 1.17.0 host plus an overlay repl and one runtime-compiled file. The next orders (timers, DMA, BOR) target flags that live in private `DefineRegisters` methods. An out-of-tree replacement peripheral can still refuse without a fork. Editing the upstream model in place cannot, not from `include`.

Recommendation: do not vendor. Keep the host pinned at Renode 1.17.0. If a later order has to patch infrastructure sources, submodule `renode` at v1.17.0 and `renode-infrastructure` at `066a7f13c052215632d469c995c89aea37c573b1` instead of copying the tree. OQ-01 (ledger format) is untouched. No ledger file was added.

## Adversarial checks

- SPI4 `SPI_CR1` write of `0x1` then read returns `0x1` (`Control1`). Process exit 0. The log records `WriteUInt32 to 0x0 (Control1), value 0x1` and contains no `Tviburi refusal`.
- SPI1 read at `0x40013020` exits 1 with a named refusal. It does not return 0.
- `--hide-log` still exits 1. The log file still has the four fields and the access line. The shim has no switch that turns a refusal into success.
- With the `include` of the shim removed, platform load prints `Could not resolve type: 'SPI.Tviburi_STM32H7_SPI1'`, the machine is disposed, and the SPE write does not run. Renode's process exit code in that case is 0, because a stopped script plus stdin EOF is a clean monitor exit. The proof harness requires the type-resolution error and rejects a success sentinel.

## Network and credentials

The shim does not open a socket and does not read a credential. The same refusal, exit 1, same text, completes inside `unshare -n`.

A strace of the host shows no `AF_INET` or `AF_INET6` socket. With a display set, the Renode process connects to the local X11 socket and the session AT-SPI bus while it starts. Those are UNIX sockets from the host GUI stack. The shim does not open them. Unsetting `DISPLAY` makes GTK probe a local broadway socket (`ECONNREFUSED`) and the refusal still fires. The proof does not clear `DISPLAY`, so the host's usual local GUI probe still happens. No credential file is read.

## Flag-and-stop

None fired.

- The SPE write exits 1 with the named refusal.
- Renode is not vendored. A platform file and a shim were enough.
- `docs/northstar.md` is unchanged.
- The shim makes no network call and uses no credential.

Upstream gaps outside this path still warn and succeed. That includes SPI4's tagged MODF, the other bus tags, and the timer, DMA, and BOR families. Those are later work orders. This spike stops here.

## Proof command

From a clean checkout, with Renode 1.17.0 on the side:

```
RENODE=/path/to/renode ./renode/run-proof.sh
```

The script exits 2 unless `renode --version` prints `Renode v1.17.0`. It does not download and it does not read credentials.
