#!/bin/bash
# TVI-WO-1150 (iii), (iv): run each case of /cases/cases.txt the way Aethelgard runs Renode (its renode.config,
# its invocation, its empty environment, seed and quantum; see run-console.sh), with a case's own overlay files.
# Inside the pinned tools image, --network none. Mounts: /repo (ro), /aetcfg (ro), /cases (ro), /out (rw).
# cases.txt lines: NAME OVERLAY_DIR|- PLATFORM FIRMWARE [CONFIG_OVERRIDE]
#   OVERLAY_DIR (under /cases) holds TviburiRelease.cs, Records1150.cs, GuardedSpi1150.cs, included in that order;
#   "-" includes nothing (a stock run). PLATFORM is a path under /repo. FIRMWARE is a firmware/spi1.rs variant.
#   CONFIG_OVERRIDE "cache-on" sets compiler-cache-enabled = True; "runner" runs as Tviburi's own runner does
#   instead (no --config, Renode's defaults, and a logFile at /out/NAME.logfile.txt that tools/tviburi-check reads).
#   A case named *.again reuses the previous HOME.
set -u
export PATH=/opt/cargo/bin:$PATH RUSTUP_HOME=/opt/rustup CARGO_HOME=/out/cargo-home
variants="modeled modf cfg2first iolock forge spray rmw"
for v in $variants; do
  rustc --edition 2021 --target thumbv7em-none-eabihf -C opt-level=s -C panic=abort -C debuginfo=0 \
    --cfg "path=\"$v\"" --check-cfg "cfg(path, values(\"${variants// /\",\"}\"))" \
    -C link-arg=-T/repo/firmware/link.x -o /out/fw-$v.elf /repo/firmware/spi1.rs 2>/dev/null || { echo "build $v failed"; exit 1; }
done
while read -r name ov plat fw cfg; do
  [ -z "$name" ] || [ "${name#\#}" != "$name" ] && continue
  case $name in *.again) ;; *) rm -rf /out/work; mkdir -p /out/work ;; esac
  sed "s#^history-path = .*#history-path = /out/work/.config/renode/history#" /aetcfg > /out/$name.renode.config
  [ "${cfg:-}" = cache-on ] && sed -i 's/^compiler-cache-enabled = False/compiler-cache-enabled = True/' /out/$name.renode.config
  inc=""; cfgarg=(--config /out/$name.renode.config)
  [ "${cfg:-}" = runner ] && { inc="logFile @/out/$name.logfile.txt; "; cfgarg=(); }
  [ "$ov" != - ] && inc="${inc}include @/cases/$ov/TviburiRelease.cs; include @/cases/$ov/Records1150.cs; include @/cases/$ov/GuardedSpi1150.cs; "
  c="emulation SetSeed 2100; emulation SetGlobalQuantum \"0.000100000\"; ${inc}mach create; machine LoadPlatformDescription @/repo/$plat; sysbus LoadELF @/out/fw-$fw.elf; cpu VectorTableOffset 0x08000000; emulation RunFor \"0.001\"; echo \"TVI1150-AFTER-RUN\"; sysbus ReadDoubleWord 0x20000104; peripherals; quit"
  printf '%s\n' "$c" > /out/$name.commands.txt
  ( cd /out/work && env -i HOME=/out/work TMPDIR=/out/work DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 \
      /usr/local/bin/renode "${cfgarg[@]}" --disable-gui --console --plain -e "$c" < /dev/null ) > /out/$name.console.txt 2>&1
  echo "$name renode exit $?"
done < /cases/cases.txt
# The case lists run on 2026-11-23: harness/cases-main.txt and harness/cases-cache.txt. Their overlay directories
# (A, B, C, E, V2, RA, RC) were made on the host by release/make-release.sh, as results.md (iii) describes.
