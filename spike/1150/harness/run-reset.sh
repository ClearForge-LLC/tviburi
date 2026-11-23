#!/bin/bash
# TVI-WO-1150 (vi): the records across `machine Reset`, rendered as Aethelgard renders a brown-out
# (crates/aethelgard-cli/src/renode.rs L697-725 at 7dec1d7b, `assert_reset`: machine Reset; the cold RCC writes;
# cpu VectorTableOffset; cpu IsHalted true; re-armed; released later), and a DMA write to a guarded register.
# The overlay is the one-file release (/ovl/Overlay1150.cs, enforcing) on the Nucleo spike platform. Aethelgard's
# configuration, invocation, empty environment, seed and quantum. Inside the pinned image, --network none.
# Mounts: /repo (ro), /aetcfg (ro), /ovl (ro), /out (rw).
set -u
export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
variants="modeled modf cfg2first iolock forge spray rmw"
for v in modeled modf iolock; do
  rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 \
    --cfg "path=\"$v\"" --check-cfg "cfg(path, values(\"${variants// /\",\"}\"))" \
    -C link-arg=-T/repo/firmware/link.x -o /out/fw-$v.elf /repo/firmware/spi1.rs 2>/dev/null || { echo "build $v failed"; exit 1; }
done
rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 \
  -C link-arg=-T/repo/firmware/link.x -o /out/fw-dma.elf /repo/spike/1150/fw/dma.rs || { echo "build dma failed"; exit 1; }
mkdir -p /out/work
sed "s#^history-path = .*#history-path = /out/work/.config/renode/history#" /aetcfg > /out/renode.config
# The cold writes: the RCC enable registers at their reset values, exactly as the captured foreign boot gives them.
cold="sysbus WriteDoubleWord 0x580244d4 0x0; sysbus WriteDoubleWord 0x580244d8 0x0; sysbus WriteDoubleWord 0x580244dc 0x0; sysbus WriteDoubleWord 0x580244e0 0x0; sysbus WriteDoubleWord 0x580244e4 0x0; sysbus WriteDoubleWord 0x580244e8 0x0; sysbus WriteDoubleWord 0x580244ec 0x0; sysbus WriteDoubleWord 0x580244f0 0x0; sysbus WriteDoubleWord 0x580244f4 0x10000; "
reset="echo \"TVI1150-RESET\"; machine Reset; ${cold}cpu VectorTableOffset 0x08000000; cpu IsHalted true; "
run() { # NAME FIRMWARE MID (commands after the reset, core held) [LOGDMA]
  local name=$1 fw=$2 mid=$3 dma=${4:-}
  local c="emulation SetSeed 2100; emulation SetGlobalQuantum \"0.000100000\"; include @/ovl/Overlay1150.cs; mach create; machine LoadPlatformDescription @/repo/spike/1150/overlay/nucleo-default.repl; sysbus LoadELF @/out/fw-$fw.elf; ${cold}cpu VectorTableOffset 0x08000000; ${dma}emulation RunFor \"0.001\"; echo \"TVI1150-MARK\"; sysbus ReadDoubleWord 0x20000104; ${reset}${mid}echo \"TVI1150-RELEASE\"; cpu IsHalted false; emulation RunFor \"0.001\"; echo \"TVI1150-MARK2\"; sysbus ReadDoubleWord 0x20000104; peripherals; quit"
  printf '%s\n' "$c" > /out/$name.commands.txt
  ( cd /out/work && env -i HOME=/out/work TMPDIR=/out/work DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
      /usr/local/bin/renode --config /out/renode.config --disable-gui --console --plain -e "$c" < /dev/null ) > /out/$name.console.txt 2>&1
  echo "$name renode exit $?"
}
# modeled: then a monitor write while the core is held, CR1.SPE with CFG2 at reset (slave): a monitor-caused refusal.
run reset-modeled modeled "echo \"TVI1150-MONITOR-WRITE\"; sysbus WriteDoubleWord 0x40013000 0x1; "
# The same two probes with SPI1EN set first by the monitor: after the cold writes the bus locks the unclocked spi1
# ("Tried to write a locked peripheral"), so the probes above never reach the guard (measured).
run reset-modeled-clocked modeled "sysbus WriteDoubleWord 0x580244f0 0x1000; echo \"TVI1150-MONITOR-WRITE\"; sysbus WriteDoubleWord 0x40013000 0x1; "
run reset-modf modf ""
# iolock: is the guard's IOLOCK latch kept across the reset? A monitor CFG2 write while the core is held.
run reset-iolock iolock "echo \"TVI1150-MONITOR-CFG2\"; sysbus WriteDoubleWord 0x4001300c 0x4400000; "
run reset-iolock-clocked iolock "sysbus WriteDoubleWord 0x580244f0 0x1000; echo \"TVI1150-MONITOR-CFG2\"; sysbus WriteDoubleWord 0x4001300c 0x4400000; "
# dma: the DMA's write to CFG2, with dma1's and spi1's accesses logged to see the CPU store that enabled the stream.
run dma dma "" "sysbus LogPeripheralAccess sysbus.dma1 true; sysbus LogPeripheralAccess sysbus.spi1 true; "
