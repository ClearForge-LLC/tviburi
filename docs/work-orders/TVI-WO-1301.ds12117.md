# TVI-WO-1301 (1) — DS12117 Rev 10's alternate-function table, transcribed, and compared with ST's open pin data

**Status:** §1.1, at the stop, 2026-11-23. The table is data, `model/ds12117-rev10-table9.tsv` (committed alone in `154d6cf`). This document says how it was made, what checks it, and every disagreement with ST's open pin data. Nothing here is resolved: each disagreement waits for the architect.

## 1. The source

| What | Identity |
|---|---|
| DS12117 Rev 10, "STM32H753xI" | `~/.cache/aet-held/ds12117-rev10.pdf`, sha256 `607b9eadaef1c54f93e02d7cea58c862a1552bda47c848d96f2de5c0ee84c032` |
| The table | **Table 9, "Alternate functions", pp. 89–100.** It is the only alternate-function table in Rev 10 (table of contents, text line 369). It has no footnotes; every printed character outside the cells is the title "Table 9. Alternate functions" or a page margin (measured: `tests/1301/ds12117-table9.py`'s grid holds every horizontal glyph on the 12 pages). |

**The part.** DS12117 is the STM32H753xI's datasheet. The WO says "for the STM32H743". The consumer's part is `STM32H753ZITx` (Aethelgard's `sentinel-02-own.json`, `mcu.part`), the stock platform is `stm32h753.repl`, and the Nucleo is an H753ZI, so DS12117 is the right document. That the H743's DS12110 has the same Table 9 is **from memory, not checked**.

## 2. How it was transcribed, independently

**Two independent transcriptions, compared cell by cell. They are byte-identical** (sha256 `a1d3f623…`, 2,689 lines: a header and 2,688 cells).

1. **Mechanical** (`tests/1301/ds12117-table9.py`, reproduced in the pinned tools image by `tests/1301/ds12117-extract.sh`).
   - Ghostscript writes each glyph with its coordinates (`txtwrite`) and renders each page at 144 dpi.
   - The page's ruled grid is read from the rendering: 17 vertical rules about 75 px apart give the 16 AF columns; each horizontal rule below the header closes a pin row.
   - Each glyph goes to the cell that holds it. A cell's text is its glyphs in reading order, with whitespace removed, so a cell printed over several lines (`TIM2_CH1/` over `TIM2_ETR`) is one string.
   - Each page's header is checked (`AF0`…`AF15` in their own columns), and each row's pin label must read `P[A-K]n`. A glyph whose box Ghostscript reports too wide (`PC0` on p91) is placed by its left edge.
2. **Visual**, by a separate agent. It read only the 12 page images (200 dpi) and typed each page; it was told to read no other file. Its tool calls were audited afterwards: only the 12 PNGs were read, and nothing under the mechanical run's directory (`tests/1301/visual/`, with its notes).

**What the table holds.** 168 pins: PA0–PJ15 (16 per port), PK0–PK7. 2,688 cells: 1,588 are the page's `-` (no function), and 1,100 name one or more signals separated by `/`.
- Every pin's AF15 is `EVENTOUT`.
- The only hyphens inside a name are `JTMS-SWDIO` and `JTCK-SWCLK` (PA13, PA14 AF0).

**A blank cell is data.** The page prints `-` in it, and the file keeps `-`. AF0 is not a default: 139 of 168 pins print `-` at AF0.

**Never read while transcribing:** Aethelgard's pin data, ArduPilot's, Renode's. **Disclosures on the order** (times from the files' mtimes):
- `model/ds12117-rev10-table9.tsv` was written at **16:35:40Z**.
- After it, two things were displayed to me before its hash was frozen at 16:36:57Z:
  - `spike/1150/data/queue-ap.tsv`'s GPIO rows, which are ArduPilot's AFSEL and MODER values by field;
  - the stock `stm32h7.repl`'s timer-to-pin wiring (Renode's pin data).
  The visual transcription was finished at 16:37:13Z by an agent that saw neither.
- The comparison with ST's data (§3) **ran at 16:38:53Z, before the table was committed** (`154d6cf`, 16:48:33Z). The table was committed byte-identical to what was compared, and no edit followed from the comparison.

## 3. The comparison with ST's open pin data

**ST's data, as Aethelgard holds it.**
- **Where:** Aethelgard `main` `1c32c8ea0069fba5ba1dc7a3ed7e126b20ac540c`, `crates/aethelgard-svd-hal/data/st-open-pin-data/`. Its `SOURCE.md` names STMicroelectronics/STM32_open_pin_data at `7d1f1514ed5583ec5007ad91236b4e1d377295b1`, the files unmodified.
- **The GPIO IP file:** `GPIO-STM32H747_gpio_v1_0_Modes.xml` (sha256 `49f51ae2…`). `STM32H753ZITx.xml` (`d167c088…`) names this IP version itself (line 112, `Version="STM32H747_gpio_v1_0"`).
- **Read-only:** nothing in Aethelgard was changed.

**Method** (first `tests/1301/compare-st.py`, whose full output is `TVI-WO-1301.st-compare.tsv`; since the 17:52Z ruling, `tools/tviburi-afcheck`, the same classes in Rust, run in CI against the ruled list `tests/1301/st-ruled.tsv`).
- For each pin and AF, DS's set is the cell split at `/`. ST's set is every `PinSignal` of that pin whose `GPIO_AF` value is `GPIO_AF<n>_*`.
- A pair of sets that differ is a disagreement, printed with both sources. It is classed, never resolved:
  - **af-number:** a signal named by both sources on this pin, at different AF numbers;
  - **presence:** one source has no function at this AF, and the other has one;
  - **alias:** every difference is explained by an explicit name pair in `tests/1301/st-aliases.tsv` (94 pairs, each with its rule);
  - **name:** whatever is left.

| Class | Cells |
|---|---|
| agree | 2,314 |
| **af-number** | **0** |
| presence | 177 |
| alias | 189 |
| name | 8 |

ST also has `PA0_C`, `PA1_C`, `PC2_C` and `PC3_C` (the analog-switch pads), which DS12117's Table 9 does not list, and which have no AF in ST's file either.

### 3.1 Every disagreement, by kind (for the architect to rule)

**D1. AF15 `EVENTOUT` on all 168 pins: in DS, absent from ST** (168 presence rows).
- ST lists `EVENTOUT` as an I/O mode (`IOModes="…,EVENTOUT,…"` on all 114 I/O pins of `STM32H753ZITx.xml`), not as a pin's `GPIO_AF15_*` value. ST's GPIO file has `GPIO_AF15_EVENTOUT` once, in its `EVENTOUT` mode definition (line 781), for every pin, and no pin lists it.
- **For the guard:** with DS, AF15 always has a function, so AF15 is never `predicted-fault`.

**D2. Six AF0 system functions in DS, absent from ST** (6 presence rows): PA4 `D1PWREN`, PA5 `D2PWREN`, PC2 `C1DSLEEP`, PC3 `C1SLEEP`, PC4 `C2DSLEEP`, PC5 `C2SLEEP` (pp. 89, 91).
- `C2*` names a second core, which the H753 does not have (from memory).
- **For the guard:** DS gives these cells a function, so AF0 on them is not `predicted-fault`.

**D3. `DSIHOST_TE` at AF13 on PA15, PB11 and PJ2: in ST, blank in DS** (3 presence rows; pp. 90, 91, 99).
- `STM32H753ZITx.xml` names no `DSIHOST` signal at all (0 occurrences). The rows come from the GPIO IP file that ST shares with the STM32H747, which has a DSI host (from memory).
- **For the guard:** with DS, AF13 on these pins is `predicted-fault`; with ST's GPIO file, it would pass.

**D4. 189 cells that differ only by name, each explained by the alias map** (`st-aliases.tsv`). The rules:
- `LCD_*` is ST's `LTDC_*`;
- `OTG_*` is `USB_OTG_*`;
- the debug signals carry `DEBUG_` in ST;
- `JTDO/TRACESWO` is ST's one `DEBUG_JTDO-SWO`;
- `NJTRST` is `SYS_JTRST`;
- `MCOn` is `RCC_MCO_n`;
- `SWPMI_*` is `SWPMI1_*`;
- the ETH MII and RMII names are merged in ST (`ETH_MII_TXD1/ETH_RMII_TXD1` is `ETH_TXD1`);
- `RTC_OUT` is ST's `RTC_OUT_ALARM/RTC_OUT_CALIB`;
- `TIMx_BKIN[2]_COMP12` is ST's two signals `…_COMP1` and `…_COMP2`.
None changes an AF number.

**D5. `SPDIFRX1_INn` off by one: DS's `IN1`…`IN4` are ST's `IN0`…`IN3`** (8 name rows; not put in the alias map, by decision). The cells:
- PD7 AF9 and PG11 AF8: `IN1`/`IN0`;
- PD8 AF9 and PG12 AF8: `IN2`/`IN1`;
- PC4 AF9 and PG8 AF8: `IN3`/`IN2`;
- PC5 AF9 and PG9 AF8: `IN4`/`IN3`.

The AF numbers agree. Which numbering RM0433's SPDIFRX chapter uses was not read.

### 3.2 A third source, read after the commit: Renode's own routing

The stock `stm32h7.repl` routes 102 timer outputs to pins by AF number (`timer1: 0 -> gpioPortA#07@01`, and on). **All 102 agree with DS12117's Table 9.** The channel `k+1` of `TIMn` is in the cell, with `N` on the complementary pins, which the stock file inverts by `invertedAFPins`.

So the WO's premise that "stock Renode routes nothing by AF" holds for SPI, USART and I2C, whose models take no pins. It does not hold for the timers.

## 4. `GPIOx_AFRH`: what the documents say

The WO asks whether Rev 10 carries "the `GPIOx_AFRH` offset correction ST's errata names". What I found:
- **DS12117 Rev 10** has no text on `GPIOx_AFRH`, or on any GPIO register (0 matches for `AFRH` in the text).
- **ES0392 Rev 16** (`~/.cache/aet-ds/es0392-rev16.txt`, the STM32H742/743/753/750 errata) names no AFRH item. Its only GPIO item is 2.2.19, "GPIO assigned to DAC cannot be used in output mode…".
- **RM0433 Rev 9:**
  - §11.4.10 (p546) gives `GPIOx_AFRH` at offset `0x24` and heads it "(x = A to J)", as the table of contents does.
  - Table 95, the register map (p548), gives `0x24` "(where x = A to K)".
  - The revision history (p3281, Rev 2) records "changed index to A to K for GPIOx_AFRH" in the register map.
  - No document held here gives any other offset.
**I could not find the errata the WO refers to:** which document is meant? Port K has only PK0–PK7 (Table 9), so AFRH on port K selects for no pin either way.

## 5. What the guard would read

`model/ds12117-rev10-table9.tsv` is keyed by port, pin and AF. Each row gives the page, and the table is always Table 9 (the file name says so).

If the architect wants the table named in every row, a `table` column can be added mechanically, with a test that the two files agree.

The rule `predicted-fault` fires for a `-` cell. The query "which signal does pin P carry" answers from the cell.
