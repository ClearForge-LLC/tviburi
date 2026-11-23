#!/bin/bash
# TVI-WO-1150 (i): load each form in retype/forms/ under Aethelgard's Renode configuration, list the
# peripherals, read the result back by reflection, then make a monitor write and the poke firmware's CPU
# writes to TIM1 and RCC. Inside the pinned tools image, --network none.
# Mounts: /repo = this worktree (ro), /aetcfg = Aethelgard's renode.config as captured (ro), /out (rw).
set -u
export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
S=/repo/spike/1150
rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 \
  -C link-arg=-T/repo/firmware/link.x -o /out/pokes.elf $S/fw/pokes.rs || { echo "firmware build failed"; exit 1; }
sed "s#^history-path = .*#history-path = /out/home/.config/renode/history#" /aetcfg > /out/renode.config
mkdir -p /out/home
for f in $S/retype/forms/*.repl; do
  n=$(basename $f .repl)
  cat > /out/$n.resc <<EOR
include @$S/retype/SpikeTimer.cs
include @$S/retype/SpikeRcc.cs
mach create
machine LoadPlatformDescription @$f
peripherals
include @$S/retype/checks.py
sysbus LoadELF @/out/pokes.elf
cpu VectorTableOffset 0x08000000
sysbus WriteDoubleWord 0x40010030 0x5
sysbus WriteDoubleWord 0x58024454 0x0
emulation RunFor "0.0005"
echo "TVI1150-AFTER"
sysbus ReadDoubleWord 0x20000104
echo "TVI1150-ISPR0"
sysbus ReadDoubleWord 0xE000E200
echo "TVI1150-PSC-BEFORE-RESET"
sysbus WriteDoubleWord 0x40010028 0x5
sysbus ReadDoubleWord 0x40010028
sysbus WriteDoubleWord 0x58024498 0x1
sysbus WriteDoubleWord 0x58024498 0x0
echo "TVI1150-PSC-AFTER-TIM1RST"
sysbus ReadDoubleWord 0x40010028
quit
EOR
  ( cd /out/home && env -i HOME=/out/home TMPDIR=/out/home DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
    /usr/local/bin/renode --config /out/renode.config --disable-gui --console --plain -e "include @/out/$n.resc" ) > /out/$n.log 2>&1
  echo "$n renode exit $?"
done
