#!/bin/bash
# TVI-WO-1301 (1): reproduce model/ds12117-rev10-table9.tsv from the held DS12117 Rev 10 (sha256 607b9ead...).
# Inside the pinned tools image (Ghostscript 10.05.1), --network none. Ghostscript cannot read or write mounted paths
# here, so it works in the container's /tmp.
# Mounts: /in/ds.pdf (ro), /h = tests/1301 (ro), /out (rw). Writes /out/table9.tsv, /out/t9.xml, /out/g<page>.pgm.
set -eu
cp /in/ds.pdf /tmp/ds.pdf
cd /tmp
gs -q -dNOPAUSE -dBATCH -dFirstPage=89 -dLastPage=100 -sDEVICE=txtwrite -dTextFormat=0 -sOutputFile=/tmp/t9.xml ds.pdf
gs -q -dNOPAUSE -dBATCH -dFirstPage=89 -dLastPage=100 -sDEVICE=pgmraw -r144 -sOutputFile=/tmp/g%03d.pgm ds.pdf
for i in $(seq 1 12); do mv /tmp/g$(printf %03d $i).pgm /tmp/g$((88 + i)).pgm; done
python3 -I /h/ds12117-table9.py /tmp > /tmp/table9.tsv
cp /tmp/t9.xml /tmp/g*.pgm /tmp/table9.tsv /out/
