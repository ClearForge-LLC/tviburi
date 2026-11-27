"""TVI-WO-1206 §5: the fields re-read independently, 20 % of the work list chosen at random with a committed seed.

Usage: python3 -I spike/1206/reread.py ITEMS [SEED] > REREAD   (items.tsv with seed 1206 -> reread.txt; items2.tsv with 12062 -> reread2.txt)
The draw is over the sorted field names, so the choice is fixed before any reading."""
import math, random, sys
seed = int(sys.argv[2]) if len(sys.argv) > 2 else 1206
names = sorted(l.split('\t')[0] for l in open(sys.argv[1]) if not l.startswith('#'))
k = math.ceil(len(names) * 0.2)
print('# seed %d, %d of %d fields' % (seed, k, len(names)))
print('\n'.join(sorted(random.Random(seed).sample(names, k))))
