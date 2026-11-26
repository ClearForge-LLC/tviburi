#!/bin/bash
# TVI-WO-1301: replay one captured `aethelgard simulate` Renode invocation inside the pinned tools image, --network
# none, in the CLI's environment (HOME and TMPDIR = the tool directory, the two DOTNET variables), without the CLI's
# sandbox and reaper, on the guard built from /repo (WO §2). From tests/1300/replay-1300.sh's `accept` mode. Used by
# tests/1301/ci-memory.sh, TVI-WO-1201's memory budget. (§1.1's other modes, which measured the prototype, are in this
# file's git history before the 19:47Z ruling deleted them.)
# Mounts: /cap = capture directory (ro), /repo = this repository (ro), /out (rw). Usage: replay-1301.sh N accept
#   accept  the release built from /repo, its one file included before `mach create`; spi1/spi2 re-typed to the
#           guarded SPI and recorded; the RCC restated and recorded (its properties moved onto `tviburiRcc`); the
#           committed model/stm32h7-gpio-restate.repl appended, each port on LQFP144 in ACCEPT_MODE (default recorded;
#           spi1, spi2 and the RCC stay recorded); the consumer's own references to a port moved onto the guard's
#           variable (its untyped `gpioPortX:` blocks re-appended after the restatement; `@ gpioPortX` and
#           `-> gpioPortX` repointed); a tree without the GPIO restate file runs with its ports stock. Writes
#           /out/N-accept.{argv.txt,platform-booted.repl,console.txt,cost.txt,peak-proc.txt}; peak-proc.txt is the
#           Renode process's /proc status and smaps_rollup at its peak sample.
#           ACCEPT_EXTERNAL ("PB6:up PB7:up"): the board's declared levels, each on its port's guard as `external`
#           (TVI-WO-1301a, ruled 2026-11-23T23:52Z), for a tree whose guard has that property. It stands in for the
#           consumer, whose platforms declare none yet.
set -u
n=$1
[ "${2:-}" = accept ] || { echo "replay-1301: usage: replay-1301.sh N accept" >&2; exit 2; }
mode=accept
rm -rf /var/lib/aethelgard; cp -r /cap/$n/varlib /var/lib/aethelgard
mapfile -d '' args < /cap/$n/argv.bin
args=("${args[@]:1}")
# Aethelgard >= 65c74788 runs Renode under its reaper (`... -- /usr/local/bin/renode ...`): keep only what follows `--` and
# the renode path (TVI-WO-1302 FEEDBACK §7 row, taken by TVI-WO-1303 for its recaptured boots). An argv without it is unchanged.
for i in "${!args[@]}"; do [ "${args[$i]}" = "--" ] && { args=("${args[@]:$((i + 2))}"); break; }; done
d=""; cfg=""
for i in "${!args[@]}"; do [ "${args[$i]}" = "--config" ] && { cfg=${args[$((i + 1))]}; d=$(dirname "$cfg"); }; done
plat=$d/platform-booted.repl
for i in "${!args[@]}"; do
  [ "${args[$i]}" = "-e" ] || continue
  c=${args[$((i + 1))]}
  [ -s /tmp/release/Tviburi.cs ] || /repo/scripts/build-release /repo /tmp/release > /out/$n-$mode.release.txt 2>&1 || { echo "replay-1301: release build failed" >&2; exit 3; }
  sed -i 's/^\(spi[0-9]*\): SPI\.STM32H7_SPI @ {/\1: Tviburi.GuardedSTM32H7_SPI @ {/' "$plat"
  sed -i '/RegistrationPeripheralAlias\.SPI[0-9]*$/{n;s/^    }$/    }\n    mode: "recorded"/}' "$plat"
  sed -i 's/^\( *\)rcc RegistrationPeripheralAlias/\1tviburiRcc RegistrationPeripheralAlias/' "$plat"
  props=$(awk '/^rcc:$/{f=1; next} f && /^ /{print; next} {f=0}' "$plat")
  awk '/^rcc:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
  { cat /repo/model/stm32h7-rcc-restate.repl; printf '\ntviburiRcc:\n    mode: "recorded"\n'; [ -n "$props" ] && printf '%s\n' "$props"; } >> "$plat"
  # A tree without a guard's restate file (main before 1301) replays without that guard: one harness wires both
  # trees of TVI-WO-1201's budget (ruled 2026-11-23T23:12Z).
  if [ -f /repo/model/stm32h7-gpio-restate.repl ]; then
    awk '/^gpioPort[A-K]:$/{f=1; sub(/gpioPort/, "tviburiGpio"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-gpio.txt
    awk '/^gpioPort[A-K]:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    grep -n '@ gpioPort[A-K]\|-> gpioPort[A-K]' "$plat" > /out/$n-$mode.repointed-gpio.txt
    sed -i 's/@ gpioPort\([A-K]\)/@ tviburiGpio\1/; s/-> gpioPort\([A-K]\)/-> tviburiGpio\1/g' "$plat"
    { echo; cat /repo/model/stm32h7-gpio-restate.repl; echo
      ext=; grep -q 'string external = null' /repo/model/GuardedSTM32_GPIOPort.cs && ext=${ACCEPT_EXTERNAL:-}
      for x in A B C D E F G H I J K; do
        printf 'tviburiGpio%s:\n    mode: "%s"\n    package: "LQFP144"\n' $x "${ACCEPT_MODE:-recorded}"
        e=$(printf '%s\n' $ext | grep "^P$x" | tr '\n' ' ' | sed 's/ $//')
        [ -z "$e" ] || printf '    external: "%s"\n' "$e"
        echo
      done
      cat /out/$n-$mode.moved-gpio.txt; } >> "$plat"
  fi
  # TVI-WO-1302: a tree with the timer restatement (TIM1 and TIM8 guarded) gets it after the GPIO one, which names the
  # guards' variables. The consumer's untyped `timer1:`/`timer8:` blocks (its frequency) move onto `tviburiTimer1`/`8`
  # (an untyped update binds to the cancelled entry: measured, TVI-WO-1302.measure.md §2.3), and its `timerN@` inputs
  # are repointed. A tree without it replays as before.
  if [ -f /repo/model/stm32h7-timer-restate.repl ]; then
    awk '/^timer[18]:$/{f=1; sub(/timer/, "tviburiTimer"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-timer.txt
    awk '/^timer[18]:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    sed -i 's/\btimer\([18]\)@/tviburiTimer\1@/g' "$plat"
    { echo; cat /repo/model/stm32h7-timer-restate.repl; echo
      printf 'tviburiTimer1:\n    mode: "%s"\n\ntviburiTimer8:\n    mode: "%s"\n\n' "${ACCEPT_MODE:-recorded}" "${ACCEPT_MODE:-recorded}"
      cat /out/$n-$mode.moved-timer.txt; } >> "$plat"
  fi
  # TVI-WO-1303: a tree with the DMA restatement (DMA1 and DMA2 guarded) gets it last, with each controller's mode.
  # The consumer's platforms declare no `dma1:`/`dma2:` of their own (measured on the 16 captured platforms); their
  # references to `dmamux1` stay bound, since DMAMUX1 is not cancelled (ruled 2026-11-24T14:10Z Q1).
  if [ -f /repo/model/stm32h7-dma-restate.repl ]; then
    { echo; cat /repo/model/stm32h7-dma-restate.repl; echo
      printf 'tviburiDma1:\n    mode: "%s"\n\ntviburiDma2:\n    mode: "%s"\n\n' "${ACCEPT_MODE:-recorded}" "${ACCEPT_MODE:-recorded}"; } >> "$plat"
  fi
  # TVI-WO-1304: a tree with the flash restatement gets it after the timers'. The consumer's untyped `flashController:`
  # block (its option-byte init sequence, Aethelgard AET-WO-4230 §1.6) cannot stay on the cancelled entry (E29: the load
  # fails, measured, TVI-WO-1304.measure.md §2.4): the OPTSR_PRG word it writes becomes the guard's `optionBytes` (ruled
  # 2026-11-24T15:50Z Q3), written to /out/N-accept.moved-flash.txt, and the block is dropped. A tree without it replays
  # as before.
  if [ -f /repo/model/stm32h7-flash-restate.repl ]; then
    awk '/^flashController:$/{f=1; print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-flash.txt
    ob=$(awk '$1 == "WriteDoubleWord" && $2 == "0x20" {print $3}' /out/$n-$mode.moved-flash.txt | tail -n 1)
    awk '/^flashController:$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    { echo; cat /repo/model/stm32h7-flash-restate.repl; echo
      printf 'tviburiFlash:\n    mode: "%s"\n' "${ACCEPT_MODE:-recorded}"
      [ -z "$ob" ] || printf '    optionBytes: %s\n' "$ob"
      echo; } >> "$plat"
  fi
  # TVI-WO-1305: a tree with the USART restatement gets it last. The consumer's untyped `usartN:`/`uartN:` blocks (its
  # frequency, and a receive request line where it wires one) move onto the guard's variable (an untyped update binds to
  # the cancelled entry, RUL-09 (b); measured as a form, TVI-WO-1305.measure.md §2.3); lpuart1 stays stock. ACCEPT_USART_DMA
  # ("usart1:41 ..."), for a tree whose guard raises the request, adds `ReceiveDmaRequest -> dmamux1@N` to those ports,
  # standing in for the consumer (Aethelgard AET-WO-4400i). A tree without it replays as before.
  if [ -f /repo/model/stm32h7-usart-restate.repl ]; then
    awk '/^(usart[1236]|uart[4578]):$/{f=1; sub(/^u/, "tviburiU"); print; next} f && /^ /{print; next} {f=0}' "$plat" > /out/$n-$mode.moved-usart.txt
    awk '/^(usart[1236]|uart[4578]):$/{f=1; next} f && /^ /{next} {f=0; print}' "$plat" > "$plat.new" && cat "$plat.new" > "$plat" && rm "$plat.new"
    { echo; cat /repo/model/stm32h7-usart-restate.repl; echo
      for u in usart1 usart2 usart3 uart4 uart5 usart6 uart7 uart8; do
        v=tviburiU${u#u}
        printf '%s:\n    mode: "%s"\n' "$v" "${ACCEPT_MODE:-recorded}"
        for w in ${ACCEPT_USART_DMA:-}; do [ "${w%%:*}" = "$u" ] && printf '    ReceiveDmaRequest -> dmamux1@%s\n' "${w#*:}"; done
        echo
      done
      cat /out/$n-$mode.moved-usart.txt; } >> "$plat"
  fi
  # TVI-WO-1306: a tree with the I2C restatement gets it last, with each port's mode. The consumer's sensors (`@ i2cN`) are
  # repointed onto the guard's variable (an `@ i2cN` binds to the cancelled entry and the load fails, E39; measured,
  # TVI-WO-1306.measure.md §2.4), and listed in /out/N-accept.repointed-i2c.txt. A tree without it replays as before.
  if [ -f /repo/model/stm32h7-i2c-restate.repl ]; then
    grep -n '@ i2c[123]\b' "$plat" > /out/$n-$mode.repointed-i2c.txt || true
    sed -i 's/@ i2c\([123]\)\b/@ tviburiI2c\1/' "$plat"
    { echo; cat /repo/model/stm32h7-i2c-restate.repl; echo
      for k in 1 2 3; do printf 'tviburiI2c%s:\n    mode: "%s"\n\n' $k "${ACCEPT_MODE:-recorded}"; done; } >> "$plat"
  fi
  # TVI-WO-1307: a tree with the ADC restatement gets it last, each ADC with its mode and the revision "V" (ruled 2026-11-25T21:17Z
  # Q2: required; declared by this replay, not read: no held document says which revision a board carries, and both measured
  # firmwares assume rev V, TVI-WO-1307.measure.md §2.4). ACCEPT_ADC_LEVELS ("adcM1S2:10:1.40 ..."), a channel's level at the pin
  # in volts, stands in for the consumer, whose foreign platforms declare none (Q10; a test value, cited as such where used).
  # The consumer's own `adc3 SetVoltage` lines bind to the guard, which keeps the name. A tree without it replays as before.
  if [ -f /repo/model/stm32h7-adc-restate.repl ]; then
    { echo; cat /repo/model/stm32h7-adc-restate.repl; echo
      for a in adcM1S2:tviburiAdc12 adc3:tviburiAdc3; do
        printf '%s:\n    mode: "%s"\n    revision: "V"\n' "${a#*:}" "${ACCEPT_MODE:-recorded}"
        lv=$(printf '%s\n' ${ACCEPT_ADC_LEVELS:-} | sed -n "s/^${a%%:*}://p" | tr '\n' ' ' | sed 's/ $//')
        [ -z "$lv" ] || printf '    levels: "%s"\n' "$lv"
        echo
      done; } >> "$plat"
  fi
  # TVI-WO-1308: a tree with the DMAMUX1 restatement gets it last (after the DMA and ADC restatements, whose guards it
  # updates), with its mode. DMAMUX1 is cancelled and restated (ruled 2026-11-26T09:31Z Q1), so the consumer's own
  # `-> dmamux1@N` lines (spi1/spi2's DMARecieve) and the USART block's ReceiveDmaRequest lines above, which would bind to the
  # cancelled entry with no error, are repointed onto the guard's variable. A tree without it replays as before.
  if [ -f /repo/model/stm32h7-dmamux-restate.repl ]; then
    sed -i 's/-> dmamux1@/-> tviburiDmamux1@/g' "$plat"
    { echo; cat /repo/model/stm32h7-dmamux-restate.repl; echo
      printf 'tviburiDmamux1:\n    mode: "%s"\n\n' "${ACCEPT_MODE:-recorded}"; } >> "$plat"
  fi
  args[$((i + 1))]=${c/'mach create; '/'include @/tmp/release/Tviburi.cs; mach create; '}
done
printf '%s\n' "${args[@]}" > /out/$n-$mode.argv.txt
cp "$plat" /out/$n-$mode.platform-booted.repl
cd "$d"
start=$(date +%s.%N)
env -i HOME="$d" TMPDIR="$d" DOTNET_EnableDiagnostics=0 DOTNET_EnableWriteXorExecute=0 /opt/renode/renode "${args[@]}" < /dev/null > /out/$n-$mode.console.txt 2>&1 &
sleep 0.05
peak=0; hwm=0
while kill -0 $! 2>/dev/null; do
  s=0; k=0; top=0; topr=0
  for p in $(pgrep -f 'enode'); do r=$(awk '/^VmRSS:/{print $2}' /proc/$p/status 2>/dev/null); s=$((s + ${r:-0})); k=$((k + 1)); [ "${r:-0}" -gt $topr ] && { topr=$r; top=$p; }; h=$(awk '/^VmHWM:/{print $2}' /proc/$p/status 2>/dev/null); [ "${h:-0}" -gt $hwm ] && hwm=$h; done
  # At each new peak, the breakdown of the largest matched process, Renode (TVI-WO-1201 (4)): its status and
  # smaps_rollup. Kept only if it has RssAnon: a peak sampled as Renode exits finds its memory already gone (no Rss
  # lines, 2 threads; seen in CI run 45286469273 and on Furnace), so the last whole snapshot stays, with its own sum.
  if [ $s -gt $peak ]; then
    peak=$s
    { echo "pid $top of $k matched; rss_sum_kib $s"; cat /proc/$top/status /proc/$top/smaps_rollup 2>/dev/null; } > /tmp/peak-proc.new
    grep -q '^RssAnon:' /tmp/peak-proc.new && mv /tmp/peak-proc.new /tmp/peak-proc.txt
  fi
  sleep 0.1
done
wait $!
cp /tmp/peak-proc.txt /out/$n-$mode.peak-proc.txt 2>/dev/null
echo "mode=$mode wall_s=$(awk -v a=$start -v b=$(date +%s.%N) 'BEGIN{printf "%.2f", b-a}') peak_tree_vmrss_kib=$peak vmhwm_kib=$hwm load=$(cut -d' ' -f1-3 /proc/loadavg)" | tee /out/$n-$mode.cost.txt
