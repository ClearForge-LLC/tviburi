# ST's open pin data, as Aethelgard holds it

The fixture for `tools/tviburi-afcheck` (TVI-WO-1301 (1)): `model/ds12117-rev10-table9.tsv` compared with ST's
alternate-function data. These files are copied, unmodified, from Aethelgard `main` at
`1c32c8ea0069fba5ba1dc7a3ed7e126b20ac540c`, `crates/aethelgard-svd-hal/data/st-open-pin-data/`. Aethelgard's own
`SOURCE.md` there names the origin:

- Repository: https://github.com/STMicroelectronics/STM32_open_pin_data
- Commit: `7d1f1514ed5583ec5007ad91236b4e1d377295b1`
- Licence: BSD-3-Clause, © STMicroelectronics (`LICENSE`, verbatim)

| File here | Original path | sha256 |
|---|---|---|
| `STM32H753ZITx.xml` | `mcu/STM32H753ZITx.xml` | `d167c0884383dd22adc1f25b712c4b12ed1cdf35d898af7edba34ce0e7f98a05` |
| `GPIO-STM32H747_gpio_v1_0_Modes.xml` | `mcu/IP/GPIO-STM32H747_gpio_v1_0_Modes.xml` | `49f51ae2508a11df6b385b86ed21172ea43478c4d314314db465e0f8d278c246` |
| `LICENSE` | `LICENSE` | `2e80479026d27db007f8bdd190860e4b0452ca30d622ef4c04084b064418a096` |

`STM32H753ZITx.xml` names its GPIO IP version `STM32H747_gpio_v1_0` (line 112), which is why that modes file is the one
compared. When Aethelgard's copy changes, this fixture is refreshed from a named Aethelgard commit, never edited.
