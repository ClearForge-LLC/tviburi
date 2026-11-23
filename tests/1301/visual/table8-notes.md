# LQFP144 transcription notes (DS12117 Rev 10, Table 8)

Table 8 runs p64-p88 (p64 is first; p88 holds only PI7, three VSS rows and the footnotes).
Total I/O rows: 169 (113 with a LQFP144 number, 56 with "-").

I/O rows per page: p64=7 p65=13 p66=9 p67=4 p68=5 p69=6 p70=5 p71=14 p72=7 p73=6 p74=7 p75=5 p76=8 p77=7 p78=7 p79=4 p80=6 p81=8 p82=6 p83=10 p84=5 p85=9 p86=4 p87=6 p88=1

Non-I/O rows with a name that is not a plain supply (skipped, listed here):
- PC2_C (p67, type ANA, LQFP144 = 28(4)); PC3_C (p67, ANA, 29(4)); PA0_C (p68, ANA, -); PA1_C (p68, ANA, -)
- BOOT0 (p86, type I, LQFP144 = 138); PDR_ON (p87, type I, LQFP144 = 143)
- NC rows (p65 E1(2), p77 N17/M16/M17/L16/L17/K16/K17(2)): type shown S or "-", LQFP144 "-"
- All other skipped rows are type S (VSS, VDD, VBAT, VSSA, VREF-, VREF+, VDDA, VCAP, VDDLDO(6), VDD50USB(8), VDD33USB).

Names with a footnote marker as printed: PC14-OSC32_IN(OSC32_IN)(1), PC15-OSC32_OUT(OSC32_OUT)(1), PB12(7), PB13(7).
Names with a parenthesised function after reset (part of the printed name): PH0-OSC_IN(PH0), PH1-OSC_OUT(PH1), PA13(JTMS/SWDIO), PA14(JTCK/SWCLK), PA15(JTDI), PB3(JTDO/TRACESWO), PB4(NJTRST).

Uncertain cells: none. All LQFP144 numbers increase monotonically down the table with no duplicates.
Note: PC2 and PC3 (I/O, p67) have "-" in LQFP144; the LQFP144 pins 28 and 29 belong to PC2_C and PC3_C (ANA, footnote 4).

## Footnotes printed below the table (p88), quoted
1. When this pin/ball was previously configured as an oscillator, the oscillator function is kept during and after a reset. This is valid for all resets except for power-on reset.
2. This ball should remain floating.
3. Pxy_C and Pxy pins/balls are two separate pads (analog switch open). The analog switch is configured through a SYSCFG register. Refer to the product reference manual for a detailed description of the switch configuration bits.
4. There is a direct path between Pxy_C and Pxy pins/balls, through an analog switch. Pxy alternate functions are available on Pxy_C when the analog switch is closed. The analog switch is configured through a SYSCFG register. Refer to the product reference manual for a detailed description of the switch configuration bits.
5. VREF+ pin, and consequently the internal voltage reference, are not available on the TFBGA100 package. On this package, this pin is double-bonded to VDDA which can be connected to an external reference. The internal voltage reference buffer is not available and must be kept disabled
6. When it is not available on a package, the VDDLDO pin is internally tied to VDD.
7. When the pin is used in USB configuration (OTG_HS_ID/OTG_HS_VBUS), the I/O is supplied by VDD33USB, otherwise it is supplied by VDD.
8. When it is not available on a package, the VDD50USB pin is internally tied to VDD33USB.
No footnotes are printed under the table on any page other than p88.
