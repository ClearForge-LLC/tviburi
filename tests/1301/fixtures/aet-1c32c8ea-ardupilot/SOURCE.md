# ArduPilot's full boot, as Aethelgard runs it: the fixture for TVI-WO-1301's memory check in CI

Ruled by the architect, 2026-11-23T18:30Z (the bound is judged in CI) and 19:08Z (Q3: build the ELF in CI from
Aethelgard's pinned ArduPilot image and check its sha256; commit no ArduPilot bytes). `tests/1301/ci-memory.sh`
uses it.

| Here | What | Source |
|---|---|---|
| `argv.bin` | the Renode argv of `aethelgard simulate --spec crates/aethelgard-cli/tests/fixtures/sentinel-02-own.json --foreign 5000ms`, captured from `/proc/<pid>/cmdline` | Aethelgard `main` `1c32c8ea0069fba5ba1dc7a3ed7e126b20ac540c`, captured by `spike/1150/harness/capture-foreign.sh` on 2026-11-23 (`TVI-WO-1301.rm0433.md` §1) |
| `varlib/…/tool/` | the CLI's scratch at that boot: `platform-booted.repl` (sha256 `3927f28a…`; its `platform.repl` is byte-identical and left out), `renode.config`, and Aethelgard's two models `models/BMP581.cs` and `models/ICM42688P.cs` (MIT OR Apache-2.0, their own headers) | the same capture. **`firmware.elf` is not here:** CI builds it (below) and puts it in place |
| `boards/sentinel-02.hwdef.dat` | the board definition the ELF is built from (sha256 `8da4cb01…`), Aethelgard's generated output | Aethelgard `0829fe83`, `crates/aethelgard-hwdef/tests/fixtures/sentinel-02.hwdef.dat`: the commit whose CI built `a799dfb6` (run 44148995663, `AET-WO-4400e2.FEEDBACK.md`). Aethelgard `main` has since dropped its `BARO` line (`465bd860`) |
| `container/ardupilot-build.sh` | Aethelgard's build script (sha256 `77cf09bd…`), MIT OR Apache-2.0 | Aethelgard `1c32c8ea`, `container/ardupilot-build.sh` |

**The ELF:** `ardupilot-build.sh boards sentinel-02`, `--network none`, in Aethelgard's pinned image
`ghcr.io/clearforge-llc/aethelgard-ardupilot@sha256:e1809b909b02c7fd01b593a65f32d191169a5027ef61ceeae8e74cc08117c2b6`
(`container/ARDUPILOT-IMAGE` at `1c32c8ea`), must give sha256
`a799dfb6633537e74a8c3b7d8b6a464f185c255e377a612b7f9fcf8378dab1ef` (2,502,044 bytes). Measured on Furnace,
2026-11-23: it does, byte for byte, in 52 s on 6 CPUs. If it does not, the check fails, naming both hashes.
ArduPilot is GPL-3.0; its corresponding source is that image's, as Aethelgard publishes it (its `/SOURCES.md`).
