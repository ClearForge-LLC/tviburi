# Console fixtures for the reference check (RUL-11 (a); TVI-WO-1150 (ii))

Copied byte for byte from `TVI-WO-1150`'s commit `ef87d9b` (branch `wo/tvi-1150-seam-spike`), `spike/1150/fixtures/aet-7dec1d7b/`, whose `SOURCE.md` says how they were made: Aethelgard `7dec1d7b`'s own Renode configuration and invocation, in the pinned image, with Tviburi `main` `dc55f98`'s model (`0100a`'s records: no `mode`, no `kind`). They go stale when Aethelgard's Renode configuration changes; this directory's name names the commit. Once `1150` is on `main`, the copies can be replaced by that path (FEEDBACK).

| File | sha256 | Framing |
|---|---|---|
| `modeled.modeldir.console.txt` | `70088dbf78eb72c364d5c7d94bbc6a17ea96645225ad518ee93090f181d35791` | the console: virtual stamp, one space; a CR may precede a line |
| `modf.modeldir.console.txt` | `f10a65d009b180c58a482e88e31256171640f8f8a99a5e482016c1214a474285` | the console: virtual stamp, one space; a CR may precede a line |
| `forge.modeldir.analyzer.console.txt` | `ec32984372bddac09ef5e36bdb004bbc88581aa2143db053a157fddd68ea3912` | the console: virtual stamp, one space; a CR may precede a line |
| `modeled.empty.console.txt` | `0a21681264af49b8b49e0996403959de9a8b8ce0732b6fd05cdffe8611b6a790` | the console: virtual stamp, one space; a CR may precede a line |
| `modf.modeldir.both.logfile.txt` | `673b254562547892ee322bfd4c67da864f97babf3430078881e5a05e6bc4715a` | the logFile backend under that configuration: virtual stamp, two spaces |
