# tests/1204/board-guards.awk — every instance one platform file runs under a Tviburi guard, one per line, in file order
# (TVI-WO-1204 (6)): run by tests/spike.sh guards over model/tviburi-nucleo-h753zi.repl and each `using "./…"` file it
# includes, to hold scripts/guards' declared list to the board. An entry is a top-level `<var>: Tviburi.<Type> @ …` line, read
# with its `//` comment removed, named by its `as "<name>"` (on that line or on the `}` line that closes it), else by <var>;
# a later top-level `<var>: @none` cancels it.
{ sub(/\/\/.*$/, ""); sub(/[ \t\r]+$/, "") }
/^[A-Za-z0-9_]+: @none$/ { v = $0; sub(/: @none$/, "", v); for (i = 1; i <= n; i++) if (var[i] == v) var[i] = ""; open = 0; next }
/^[A-Za-z0-9_]+: Tviburi\.[A-Za-z0-9_]+ @/ {
  n++; var[n] = $0; sub(/:.*$/, "", var[n]); name[n] = var[n]; open = 1
  if (match($0, / as "[A-Za-z0-9_]+"$/)) { name[n] = substr($0, RSTART + 5, RLENGTH - 6); open = 0 }
  next
}
/^[^ \t}]/ { open = 0 }
open && /^[ \t]*\} as "[A-Za-z0-9_]+"$/ { match($0, /"[A-Za-z0-9_]+"/); name[n] = substr($0, RSTART + 1, RLENGTH - 2); open = 0 }
END { for (i = 1; i <= n; i++) if (var[i] != "") print name[i] }
