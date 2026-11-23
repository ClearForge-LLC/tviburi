# TVI-WO-1100 scratch reader: each pin's role from a generated board crate's src/board.rs (the copy `verify`
# generated into its scratch), from the doc line the generator writes above each pin type:
#   /// `PA5`, net `SPI_SCK`: `SPI1_SCK` on AF5.        /// `PA0`, net `FLASH_CS`: a GPIO output.
# Usage: gawk -v board=B -f pins.awk board.rs     Output: board pin port index net function peripheral
BEGIN { OFS = "\t" }
match($0, /^\/\/\/ `P([A-K])([0-9]+)`, net `([^`]+)`: (.*)\.$/, m) {
  fn = m[4]; per = "-"
  if (match(fn, /`([A-Z0-9]+)_[A-Z0-9_]+` on AF[0-9]+/, f)) per = f[1]
  gsub(/`/, "", fn)
  print board, "P" m[1] m[2], "GPIO" m[1], m[2], m[3], fn, per
}
