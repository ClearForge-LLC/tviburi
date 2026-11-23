# Visual transcription notes (DS12117 Rev 10, Table 9, pp. 89-100)

Pins per page (first, last, count):
89 PA0-PA11 (12); 90 PA12-PB9 (14); 91 PB10-PC5 (12); 92 PC6-PD4 (15); 93 PD5-PD15 (11);
94 PE0-PE15 (16); 95 PF0-PF15 (16); 96 PG0-PG11 (12); 97 PG12-PH9 (14); 98 PH10-PI8 (15);
99 PI9-PJ8 (16); 100 PJ9-PK7 (15). Total 168 rows (A-J 16 each, K 8). 2688 cells.

No row continues across a page break.
Cells marked ??: none.
Column-assignment doubts: none material; multi-line cells were placed by the vertical rules.
Wrap-joins that deserve a second look in the diff:
- PE15 AF13: printed over 4 lines, read as TIM1_BKIN_COMP12/COMP_TIM1_BKIN.
- PA7 AF11 ETH_MII_RX_DV/ETH_RMII_CRS_DV; PA1 AF11 ETH_MII_RX_CLK/ETH_RMII_REF_CLK.
- PD5 AF2 printed "HRTIM_EE"/"V3" -> HRTIM_EEV3; PD6/PD7 AF5 printed "SPI3_MOS"/"I/I2S3_"/"SDO" -> SPI3_MOSI/I2S3_SDO (PD7: SPI1_MOSI/I2S1_SDO).
- PE10 AF1 "TIM1_CH2"/"N" -> TIM1_CH2N; PE12 AF12 "FMC_D9/F"/"MC_DA9" -> FMC_D9/FMC_DA9; PE14 AF5 "SPI4_MOS"/"I" -> SPI4_MOSI.
- PG12/PG14 AF11 both read ETH_MII_TXD1/ETH_RMII_TXD1; PG13 TXD0.
- Port label "Port E" appears twice on p94 (PE15 in its own block) and "Port F" twice on p95 (PF15); label artefact only.
Non-cell content: running header "Table 9. Alternate functions", repeated AF0..AF15 header rows with peripheral group names, page footers (NN/357, DS12117 Rev 10). No footnotes or notes seen. Empty-looking cells all printed "-". AF0 "JTMS-SWDIO", "JTCK-SWCLK" printed with a hyphen (kept).
Method note: cells were entered by me per page by eye; no other sources consulted.
