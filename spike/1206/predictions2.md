# TVI-WO-1206, widened: predictions for the second work list, committed before any field of it is read (ruled 2026-11-27T00:43Z)

**Written:** 2026-11-27, by the builder, before reading any page or stock line for these fields, from `spike/1206/data/items2.tsv`
(174 distinct ledger fields at 60 registers, open in the per-field measure) and each row's present text only.

| Family | Fields | `acknowledged` | `stock-wrong` | Held for a ruling | Why I expect that |
|---|---|---|---|---|---|
| RCC reset bits (`*RSTR`) | 88 | 80 | 8 | 0 | A reset pulse (1 then 0) resets a registered peripheral in both; stock-wrong where stock resets at the 1 but holds nothing in reset, or a bit's stock name is another's |
| RCC enables (`AHB4ENR`, `APB1LENR`) | 29 | 27 | 2 | 0 | As A1/A2 |
| RCC `CR`, `CFGR` | 11 | 8 | 3 | 0 | The oscillators and PLL enables are modeled by stock with ready flags; `SW` and `TIMPRE` are the likeliest differences |
| DMA | 13 | 11 | 2 | 0 | Address increments and sizes are what stock moves by; `DMDIS` (direct mode) and the transfer-complete clears are the likeliest differences |
| I2C | 11 | 8 | 3 | 0 | Interrupt enables, addressing and counts; `TCIE` (TC left set after a STOP), `RXDMAEN` and `Res6` (A12's kind) the likeliest |
| SPI | 12 | 8 | 4 | 0 | `SPE`, `CSTART`, `TSIZE` and the interrupt enables; suspend and the end-of-transfer flags the likeliest differences |
| USART | 8 | 3 | 1 | 4 | `DMAT` and three clear bits; the four reserved ranges ChibiOS writes 1 into are A12's kind (the manual silent) |
| TIM1 | 2 | 1 | 1 | 0 | `OPM` = 0; `EGR.UG`, whose shadow-register loads stock does only in part |
| **All** | **174** | **146** | **24** | **4** | |

Scored at the addendum against the readings, as `predictions.md` was.
