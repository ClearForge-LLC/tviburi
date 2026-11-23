/* NUCLEO-H753ZI: flash bank 1 at 0x08000000, DTCM at 0x20000000. */
MEMORY
{
  FLASH (rx)  : ORIGIN = 0x08000000, LENGTH = 1M
  DTCM  (rwx) : ORIGIN = 0x20000000, LENGTH = 128K
}
ENTRY(reset);
SECTIONS
{
  .vectors ORIGIN(FLASH) : { LONG(ORIGIN(DTCM) + LENGTH(DTCM)); LONG(reset | 1); } > FLASH
  .text : { *(.text .text.*); *(.rodata .rodata.*); } > FLASH
  /DISCARD/ : { *(.ARM.exidx .ARM.exidx.*); }
}
