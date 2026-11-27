"""spike/2100/consumer-argv.py IN_ARGV OUT_ARGV OVERLAY [INJECT] -- TVI-WO-2100 rehearsal: the captured Renode argv, as
the consumer runs it guarded. IN_ARGV and OUT_ARGV are NUL-separated.
  - Aethelgard's reaper prefix is dropped: what follows `--` is kept, from the Renode path on;
  - `include @OVERLAY;` goes before `mach create` (RUL-09 (a));
  - with INJECT (the armed run): `include @INJECT;` goes before the `emulation RunFor`, whose value becomes
    "0.000000001" (the injection runs its own windows).
Nothing else in the argv changes. The config's `compiler-cache-enabled = False` is checked by the harness.
"""
import re
import sys


def main():
    a = sys.argv[1:]
    if len(a) not in (3, 4):
        sys.exit("usage: consumer-argv.py IN_ARGV OUT_ARGV OVERLAY [INJECT]")
    with open(a[0], "rb") as f:
        argv = f.read().split(b"\0")
    if argv and argv[-1] == b"":
        argv.pop()
    argv = [x.decode("utf-8") for x in argv]
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    if not argv[0].endswith("/renode"):
        sys.exit("argv[0] is %r, not a renode path" % argv[0])
    e = [i for i, x in enumerate(argv) if x == "-e"]
    if len(e) != 1:
        sys.exit("expected one -e")
    s = argv[e[0] + 1]
    if s.count("; mach create;") != 1:
        sys.exit("expected one `mach create`")
    s = s.replace("; mach create;", "; include @%s; mach create;" % a[2])
    if len(a) == 4:
        runs = re.findall(r'; emulation RunFor "[0-9.]+";', s)
        if len(runs) != 1:
            sys.exit("expected one emulation RunFor, found %d" % len(runs))
        s = s.replace(runs[0], '; include @%s; emulation RunFor "0.000000001";' % a[3])
    argv[e[0] + 1] = s
    with open(a[1], "wb") as f:
        f.write(b"\0".join(x.encode("utf-8") for x in argv) + b"\0")


if __name__ == "__main__":
    main()
