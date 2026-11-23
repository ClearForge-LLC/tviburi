# TVI-WO-1150 (vii): from the stock stm32h7.repl, every entry whose registration block names
# `rcc RegistrationPeripheralAlias.X`, restated as an untyped update whose block names PARENT (-v parent=…) instead.
/^[A-Za-z0-9_]+: .* @ \{$/ { name = $1; sub(/:$/, "", name); inblk = 1; blk = ""; hasrcc = 0; next }
inblk && /^ *\}/ { inblk = 0; if (hasrcc) { print name ": @ {"; printf "%s", blk; print "    }"; print "" }; next }
inblk { line = $0; if (line ~ /^ *rcc RegistrationPeripheralAlias\./) { hasrcc = 1; sub(/rcc /, parent " ", line) }; blk = blk line "\n" }
