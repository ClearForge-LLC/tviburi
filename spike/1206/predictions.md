# TVI-WO-1206: predictions, committed before any field is read (WO §1.1)

**Written:** 2026-11-26, by the builder, before reading any page or stock line for these fields. Made from the work list's
values (`spike/1206/data/items.tsv`) and each row's present text only.

**The work list:** the 129 items above the cut (105 rows, 24 registers) rest on **121 distinct ledger fields**. One reading per
field covers every instance bound to that guard type; the adversarial pass checks whether siblings' stock paths differ.

| Family | Fields | `acknowledged` | `stock-wrong` | Stops | Why I expect that |
|---|---|---|---|---|---|
| RCC | 74 | 66 | 8 | 0 | Mostly enables at the value the firmware wants, and dividers inside the stock widths. Stock-wrong where an enable of 0 leaves an unclocked peripheral still answering, or a divider is held at the wrong width |
| USART | 21 | 17 | 4 | 0 | `ICR`'s clear bits clear flags the twin never sets. Stock-wrong for a flag silicon can set and the twin cannot (noise and framing), and for `TCIE` |
| TIM1 | 17 | 16 | 1 | 0 | Zero-only modes and disabled interrupts. `URS` is the likeliest difference |
| I2C | 7 | 4 | 2 | 1 | `ERRIE`: no error is ever raised (stock-wrong). The reserved `ICR` bits are stock-wrong. **`AUTOEND` = 0 is the likeliest stop:** the stock model sends a STOP after `NBYTES` where silicon, in software-end mode, sets `TC` and waits |
| GPIO | 1 | 0 | 1 | 0 | `OSPEEDR` sets slew on silicon and does nothing in the stock port |
| SPI | 1 | 1 | 0 | 0 | `TXDR`, one frame per access |
| **All** | **121** | **104** | **16** | **1** | |

**How this is scored:** at the addendum, each family's counts are set beside these. A family read to exactly its prediction is
no evidence by itself; one that differs is explained in FEEDBACK.
