# Console fixtures for `RUL-11`: Aethelgard `7dec1d7b`

These are Renode's console captures, byte for byte, made under the configuration Aethelgard writes. Every file here comes from that configuration. **When Aethelgard's Renode configuration or invocation changes, these go stale.** This directory's name names the commit.

- **Aethelgard:** `main` at `7dec1d7b486b92bc896e3cee34b33175ad0825a8`. Its configuration text is `crates/aethelgard-cli/src/renode.rs` `config()` L73–94.
- **`renode.config`:** the file that commit's CLI wrote, as captured from its own `simulate --foreign` boot (sha256 `ef1111aa…` as written). Only `history-path` was moved to the container's scratch (`renode.config.diff`).
- **Image:** `ghcr.io/clearforge-llc/aethelgard-tools@sha256:e5fc7ddbe36c050e78763ad1d203ec8d176408a3e534bed5563c424d8d9a1b58`, Renode `1.17.0+20260907gitf1dd1b4af.aet1605`. Run with `--network none`.
- **Invocation:** as Aethelgard's CLI runs Renode (`main.rs` L1954, L2069): `renode --config renode.config --disable-gui --console --plain -e "<commands>"`. The environment is only `HOME` and `TMPDIR` (the work directory), `DOTNET_EnableDiagnostics=0` and `DOTNET_EnableWriteXorExecute=0` (`main.rs` `renode_env`). The commands use its seed and quantum. Each run's exact commands are in `<name>.commands.txt`.
- **Model and firmware:** Tviburi `main` `dc55f98`'s `model/` (`0100a`, unchanged) on `model/tviburi-nucleo-h753zi.repl`. The firmware is `firmware/spi1.rs`, built per variant (`firmware.sha256`).
- **Paths:** `/repo` is this repository and `/out` the output directory, as mounted in the container.
- **Made by:** `spike/1150/harness/run-console.sh`, 2026-11-23.

| File | What |
|---|---|
| `{modeled,modf,forge}.empty.console.txt` | **The fixture.** The environment exactly as Aethelgard's. `0100a`'s guard cannot compute its version there, so the witness is a `TVI-LOAD` refusal and every access refuses (review A2) |
| `{modeled,modf,forge}.modeldir.console.txt` | The same, with `TVIBURI_MODEL_DIR=/repo/model` added, so that `0100a`'s records are the ones it emits when loaded. Aethelgard never sets this variable |
| `*.modeldir.both.{console,logfile}.txt` | One run with `logFile` added before the machine: both framings of the same records |
| `forge.modeldir.analyzer.*` | Adversarial: `showAnalyzer usart3` added, as Tviburi's own runner has it. Aethelgard attaches no analyzer |
| `spray.modeldir.both.*` | Adversarial: record bytes written into `spi1`'s unguarded registers |
| `modeled.modeldir.probe.*` | Framing probe: after the run, the monitor logs under `spi1`'s name a three-line message and three identical messages (`spike/1150/console/probe.py`). Monitor-side only |

What each file shows is in `spike/1150/results.md` (ii).
