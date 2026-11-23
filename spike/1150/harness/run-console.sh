#!/bin/bash
# TVI-WO-1150 (ii): 0100a's firmware and unchanged guard (model/), run the way Aethelgard runs Renode: its
# renode.config as `--config`, `--disable-gui --console --plain -e "<commands>"`, the environment its CLI
# gives Renode (HOME and TMPDIR = the work directory, the two DOTNET variables, nothing else), its seed and
# quantum. Inside the pinned tools image, --network none.
# Mounts: /repo = this worktree (ro), /aetcfg = Aethelgard's renode.config as captured (ro), /out (rw).
# Per firmware F and environment E (empty = Aethelgard's; modeldir = plus TVIBURI_MODEL_DIR=/repo/model):
#   F.E.console.txt  - the console, byte for byte (stdout and stderr), as Aethelgard reads it
#   F.E.both.console.txt and F.E.both.logfile.txt - one run with `logFile` added, for the two framings
set -u
export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
variants="modeled modf cfg2first iolock forge spray rmw"
for v in modeled modf forge spray; do
  rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 \
    --cfg "path=\"$v\"" --check-cfg "cfg(path, values(\"${variants// /\",\"}\"))" \
    -C link-arg=-T/repo/firmware/link.x -o /out/fw-$v.elf /repo/firmware/spi1.rs || { echo "build $v failed"; exit 1; }
done
sha256sum /out/fw-*.elf > /out/firmware.sha256
mkdir -p /out/work
sed "s#^history-path = .*#history-path = /out/work/.config/renode/history#" /aetcfg > /out/renode.config
run() { # run NAME ENV BEFORE_INCLUDES AFTER_LOAD FIRMWARE [AFTER_RUN]
  local name=$1 env=$2 before=$3 after=$4 fw=$5 post=${6:-}
  local c="emulation SetSeed 2100; emulation SetGlobalQuantum \"0.000100000\"; ${before}include @/repo/model/Refusal.cs; include @/repo/model/GuardedSTM32H7_SPI.cs; mach create; machine LoadPlatformDescription @/repo/model/tviburi-nucleo-h753zi.repl; sysbus LoadELF @/out/fw-$fw.elf; cpu VectorTableOffset 0x08000000; ${after}emulation RunFor \"0.001\"; echo \"TVI1150-AFTER-RUN\"; sysbus ReadDoubleWord 0x20000104; ${post}peripherals; quit"
  local e=(HOME=/out/work TMPDIR=/out/work DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0)
  [ "$env" = modeldir ] && e+=(TVIBURI_MODEL_DIR=/repo/model)
  printf '%s\n' "$c" > /out/$name.commands.txt
  ( cd /out/work && env -i "${e[@]}" /usr/local/bin/renode --config /out/renode.config --disable-gui --console --plain -e "$c" ) > /out/$name.console.txt 2>&1
  echo "$name renode exit $?"
}
for env in empty modeldir; do
  for fw in modeled modf forge; do
    run $fw.$env $env "" "" $fw
    run $fw.$env.both $env "logFile @/out/$fw.$env.both.logfile.txt; " "" $fw
  done
done
# Adversarial and framing probes (not the fixture): the UART on the console, as Tviburi's own runner shows it;
# record bytes sprayed into spi1's unguarded registers; a multi-line and a repeated message under spi1's name.
run forge.modeldir.analyzer modeldir "logFile @/out/forge.modeldir.analyzer.logfile.txt; " "showAnalyzer usart3; " forge
run spray.modeldir.both modeldir "logFile @/out/spray.modeldir.both.logfile.txt; " "" spray
run modeled.modeldir.probe modeldir "logFile @/out/modeled.modeldir.probe.logfile.txt; " "" modeled "include @/repo/spike/1150/console/probe.py; "
