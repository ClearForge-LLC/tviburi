"""TVI-WO-1206 §5: the fields re-read independently, 20 % of the work list chosen at random with a committed seed.

Usage: python3 -I spike/1206/reread.py spike/1206/data/items.tsv > spike/1206/data/reread.txt
The seed is 1206 and the draw is over the sorted field names, so the choice is fixed before any reading."""
import math, random, sys
names = sorted(l.split('\t')[0] for l in open(sys.argv[1]) if not l.startswith('#'))
k = math.ceil(len(names) * 0.2)
print('# seed 1206, %d of %d fields' % (k, len(names)))
print('\n'.join(sorted(random.Random(1206).sample(names, k))))
