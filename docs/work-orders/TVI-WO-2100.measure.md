# TVI-WO-2100 — measurement and evidence

**Builder:** Claude (Opus 5.5), Furnace, 2026-11-26. **Branch:** `wo/tvi-2100-first-release`, from `main` `cd235c7`.

## 3. The pins against Aethelgard's tools image (done first, WO §1.1)

Image: `ghcr.io/clearforge-llc/aethelgard-tools@sha256:1e0ab105ec42…` — the digest in Aethelgard `main` `20971352` `container/IMAGE`, and the same as Tviburi `tests/spike.sh`'s `IMAGE`. Read with `~/bin/capped-run --network none`.

| Pin (`tools/tviburi-release/pins.tsv`) | In the image | Aethelgard `main` `20971352` | |
|---|---|---|---|
| `build renode 1.17.0+20260907gitf1dd1b4af.aet1605` | `/opt/renode/Renode.dll` informational version `1.17.0+20260907gitf1dd1b4af.aet1605`; `renode --version`: `build: 1.17.0+20260907gitf1dd1b4af.aet1605`; sha256 `5734eb0c…f633` | `crates/aethelgard-twin/src/platform.rs:33` `RENODE_BUILD`; `container/smoke.sh:50`; `container/Dockerfile:121` `5734eb0c…f633` | match |
| `build infrastructure 1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2` | `/opt/renode/Infrastructure.dll` (the rebuilt one, AET-WO-1605) informational version `1.0.0+cd4b002aac2398994c4a61a34a8b31b5700facf2`; sha256 `ddb19d7b…24b5` | `container/Dockerfile:28` source `cd4b002a…`, `:115` built with `SourceRevisionId=cd4b002a…`, `:120` sha256 `ddb19d7b…24b5` | match |
| `stock 13c5571e… platforms/cpus/stm32h753.repl` | `13c5571e…d10ce` | `platform.rs:40` | match |
| `stock 86b2c2c5… platforms/cpus/stm32h743.repl` | `86b2c2c5…231c` | `platform.rs:44` | match |
| `stock b85cb4af… platforms/cpus/stm32h7.repl` | `b85cb4af…251e` | `platform.rs:48` | match |

**Finding (not a mismatch):** the rebuilt `Infrastructure.dll`'s build string is `1.0.0+cd4b002a…`, the same as an unpatched build of that commit, because the rebuild sets `SourceRevisionId` to the same commit (Dockerfile:115). So the string the MANIFEST names cannot distinguish AET-WO-1605's patched assembly from Antmicro's; only its sha256 (`ddb19d7b…`, pinned by Aethelgard's Dockerfile) does. `Renode.dll`'s string does carry the patch (`.aet1605`). A row: the MANIFEST could name each assembly's sha256 beside its build string.
