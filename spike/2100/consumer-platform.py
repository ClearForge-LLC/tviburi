"""spike/2100/consumer-platform.py MANIFEST STOCKDIR PLATFORM [--package P] -- TVI-WO-2100 rehearsal, the consumer's side.

Written as the consumer (standing in for Aethelgard) from the release tarball's MANIFEST, the pinned stock chain copied
out of the consumer's tools image, and the consumer's own captured platform-booted.repl. It reads nothing else of
Tviburi's. It applies the class map as the ruling RUL-09 states it:
  - each `restate` instance: cancel it (`<name>: @none`) and declare the guard at the stock registration, `as "<name>"`,
    restating the stock entry's registration, properties and connections (RUL-09 (b));
  - each `retype` instance: change the consumer's own entry's type string;
  - every `property` row for each guard: required rows get a value (stock entry, consumer fact, or the row's meaning),
    optional rows are decided from the meaning text; a stock property with no row is not a constructor parameter;
  - every reference to a cancelled variable is repointed to the guard's variable (registrations on `rcc`, connection
    destinations, property values), on live stock entries as well as on the consumer's;
  - the stock file's and the consumer's untyped blocks for a cancelled instance are re-emitted under the guard's
    variable, never as an update of the cancelled one (RUL-09 (b)'s trap).
Every guess is a `GUESS` line in the emitted header and on stderr. The guarded platform goes to stdout.
After emission a static audit fails the run if any cancelled name is left in a reference position.
"""
import hashlib
import os
import re
import sys

GUESSES = []


def guess(text):
    GUESSES.append(text)


def die(text):
    sys.stderr.write("consumer-platform: " + text + "\n")
    sys.exit(1)


# ---------------------------------------------------------------- MANIFEST
def read_manifest(path):
    m = {"class": {}, "restate": [], "retype": [], "property": {}, "stock": {}, "build": {}}
    with open(path, encoding="utf-8") as f:
        raw = f.read()
    for line in raw.split("\n"):
        w = line.split(" ")
        if w[0] == "class":
            m["class"][w[1]] = w[2]
        elif w[0] == "restate":
            m["restate"].append((w[1], w[2], w[3]))
        elif w[0] == "retype":
            m["retype"].append((w[1], w[2]))
        elif w[0] == "property":
            cls, name, req = w[1], w[2], w[3]
            meaning = line.split(" ", 4)[4]
            m["property"].setdefault(cls, []).append((name, req, meaning))
        elif w[0] == "stock":
            m["stock"][w[2]] = w[1]
        elif w[0] == "build":
            m["build"][w[1]] = w[2]
    return m, "sha256:" + hashlib.sha256(raw.encode("utf-8")).hexdigest()


# ---------------------------------------------------------------- .repl blocks
# A block is one top-level entry: its header line (column 0, `name:`) and every line after it up to the next header,
# with ''' strings kept whole. Comments and blank lines between blocks are kept as their own `text` blocks.
HEADER = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*):(.*)$")


def parse_repl(text):
    blocks, cur, in_str = [], None, False
    for line in text.split("\n"):
        if in_str:
            cur["lines"].append(line)
            if line.count("'''") % 2 == 1:
                in_str = False
            continue
        h = HEADER.match(line)
        if h and not line.startswith(" "):
            cur = {"name": h.group(1), "lines": [line]}
            blocks.append(cur)
        elif line.startswith((" ", "\t")) and cur is not None and line.strip():
            cur["lines"].append(line)
        else:
            cur = None
            blocks.append({"name": None, "lines": [line]})
            continue
        if line.count("'''") % 2 == 1:
            in_str = True
    return blocks


def header_parts(block):
    """(name, type or None, registration text or None, rest-of-block lines) for an entry block."""
    first = block["lines"][0]
    rest = first.split(":", 1)[1].strip()
    lines = block["lines"][1:]
    typ, reg = None, None
    if "@" in rest:
        typ, reg = rest.split("@", 1)
        typ = typ.strip() or None
        reg = "@" + reg
        if reg.rstrip().endswith("{"):
            depth = 1
            while depth:
                ln = lines.pop(0)
                reg += "\n" + ln
                depth += ln.count("{") - ln.count("}")
    elif rest:
        typ = rest
    return block["name"], typ, reg, lines


def body_items(lines):
    """Split body lines into items: ('prop', name, text), ('conn', text), ('init', [lines]), ('other', text)."""
    items, i = [], 0
    while i < len(lines):
        ln = lines[i]
        s = ln.strip()
        if s.startswith("//") or not s:
            items.append(("other", ln))
            i += 1
        elif re.match(r"^(init|init add|reset)\s*:\s*$", s):
            ind = len(ln) - len(ln.lstrip())
            grp = [ln]
            i += 1
            while i < len(lines) and (len(lines[i]) - len(lines[i].lstrip()) > ind or not lines[i].strip()):
                grp.append(lines[i])
                i += 1
            items.append(("init", grp))
        elif "->" in s:
            items.append(("conn", ln))
            i += 1
        else:
            pm = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:", s)
            grp = [ln]
            i += 1
            if "'''" in ln and ln.count("'''") % 2 == 1:
                while i < len(lines):
                    grp.append(lines[i])
                    i += 1
                    if grp[-1].count("'''") % 2 == 1:
                        break
            items.append(("prop", pm.group(1) if pm else None, "\n".join(grp)))
    return items


# ---------------------------------------------------------------- repointing
def repoint(text, ren, where):
    """Rewrite references to cancelled variables in a registration, a connection or a property value."""
    out = text
    if where == "reg":
        # `@ name ...` (a parent other than sysbus) and `name RegistrationPeripheralAlias.X` inside { }.
        out = re.sub(r"(@\s*)([A-Za-z_][A-Za-z0-9_]*)\b", lambda m: m.group(1) + ren.get(m.group(2), m.group(2)), out)
        out = re.sub(r"(^|[{;\n]\s*)([A-Za-z_][A-Za-z0-9_]*)(\s+RegistrationPeripheralAlias)",
                     lambda m: m.group(1) + ren.get(m.group(2), m.group(2)) + m.group(3), out)
    elif where == "conn":
        src, dst = out.split("->", 1)
        dst = re.sub(r"([A-Za-z_][A-Za-z0-9_]*)(?=\s*[#@])", lambda m: ren.get(m.group(1), m.group(1)), dst)
        out = src + "->" + dst
    elif where == "prop":
        m = re.match(r"^(\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*)([A-Za-z_][A-Za-z0-9_]*)\s*$", out)
        if m and m.group(2) in ren:
            out = m.group(1) + ren[m.group(2)]
    return out


def gvar(name):
    return "guarded" + name[0].upper() + name[1:]


# ---------------------------------------------------------------- consumer facts (the board's, not Tviburi's)
SILICON_REVISION = "V"                          # the consumer's fact: silicon revision V
ADC_LEVELS = {"adcM1S2": "10:1.40"}             # ArduPilot's battery pin: ADC1 channel 10, declared 1.40 V
GPIO_EXTERNAL = {"gpioPortB": "PB6:up PB7:up"}  # the board's I2C pull-ups
USART_RX_DMA = {"usart1": 41}                   # USART1's receive DMA request, DMAMUX1 input 41
# GPIOx reset values, RM0433 Rev 7 p541 (MODER) and p542 (OSPEEDR, PUPDR), FROM MEMORY. The MANIFEST's meaning text
# gives OSPEEDR and PUPDR for ports A and B (they agree with these); it gives no MODER value for any port.
GPIO_RESET = {
    "A": (0xABFFFFFF, 0x0C000000, 0x64000000),
    "B": (0xFFFFFEBF, 0x000000C0, 0x00000100),
}
GPIO_RESET_OTHER = (0xFFFFFFFF, 0x00000000, 0x00000000)


def main():
    a = sys.argv[1:]
    package = None
    if "--package" in a:
        i = a.index("--package")
        package = a[i + 1]
        del a[i:i + 2]
    if len(a) != 3:
        die("usage: consumer-platform.py MANIFEST STOCKDIR PLATFORM [--package P]")
    man, release = read_manifest(a[0])
    stockdir, platform = a[1], a[2]

    # The pinned stock chain: every file the MANIFEST pins, by sha256.
    stock_text = {}
    for rel, sha in man["stock"].items():
        p = os.path.join(stockdir, os.path.basename(rel))
        with open(p, "rb") as f:
            b = f.read()
        if hashlib.sha256(b).hexdigest() != sha:
            die("stock file %s is not the MANIFEST's %s" % (rel, sha))
        stock_text[rel] = b.decode("utf-8")

    with open(platform, encoding="utf-8") as f:
        consumer = f.read()
    using = re.findall(r'^using "([^"]+)"', consumer, re.M)
    if using != ["platforms/cpus/stm32h753.repl"]:
        die("the consumer's platform uses %r, not the one stock chain this emitter knows" % using)

    # The stock chain's blocks, in the order Renode merges them (an included file's entries before the includer's).
    chain = ["platforms/cpus/stm32h7.repl", "platforms/cpus/stm32h743.repl", "platforms/cpus/stm32h753.repl"]
    stock_blocks = []
    for rel in chain:
        for b in parse_repl(stock_text[rel]):
            if b["name"] and b["name"] != "using":
                b["file"] = rel
                stock_blocks.append(b)
    cblocks = parse_repl(consumer)

    restate = {inst: (cls, f) for cls, inst, f in man["restate"]}
    retype = {inst: cls for cls, inst in man["retype"]}
    ren = {inst: gvar(inst) for inst in restate}
    params = {g: {p[0]: (p[1], p[2]) for p in rows} for g, rows in man["property"].items()}

    # Every cancelled instance must be typed, once, in the file the MANIFEST names, with the class the MANIFEST names.
    typed = {}
    for b in stock_blocks:
        n, t, r, _ = header_parts(b)
        if t:
            typed.setdefault(n, []).append((t, b["file"], b))
    for inst, (cls, f) in restate.items():
        if [(t, fl) for t, fl, _ in typed.get(inst, [])] != [(cls, f)]:
            die("restate %s %s: the stock chain types it %r" % (cls, inst, [(t, fl) for t, fl, _ in typed.get(inst, [])]))

    # Classes in the map that the consumer declares itself, but under a name the MANIFEST does not retype.
    for b in cblocks:
        if b["name"]:
            n, t, r, _ = header_parts(b)
            if t in man["class"] and n not in retype and n not in restate:
                die("the consumer declares %s %s, which the class map names but no retype line does" % (t, n))

    guess("GPIO modeResetValue: required, and the row says \"the stock entry's\", but no stock entry declares one and the"
          " MANIFEST gives no MODER value; RM0433 Rev 7 p541 FROM MEMORY: A 0xABFFFFFF, B 0xFFFFFEBF, C-K 0xFFFFFFFF."
          " OSPEEDR/PUPDR: A and B from the row's meaning text, C-K the row's 0")
    if package:
        guess("GPIO package %s: not a consumer fact given; from the sentinel-02 hwdef's `STM32H753ZITx` (ZI = LQFP144)"
              ", assumed for any board this is run on" % package)
    guess("ADC levels: the board's \"ADC1 channel 10\" is declared on adcM1S2 (the stock ADC12 pair) as \"10:1.40\";"
          " the row's form names no ADC of a pair")
    guess("usart1 ReceiveDmaRequest -> dmamux1@41: a consumer fact, with no MANIFEST row (the MANIFEST lists no"
          " connections); wired on the guard")
    guess("timer and USART frequency: the rows say \"the stock entry's\"; the consumer's own blocks (200 MHz, 72 MHz)"
          " are merged onto the guards and override it, as they did the stock instances")

    out = []
    w = out.append
    w("// Guarded by Tviburi release %s: emitted by spike/2100/consumer-platform.py from the release's MANIFEST," % release)
    w("// the stock chain it pins, and the consumer's platform below (RUL-09 (b): cancel and restate; retype)." )
    w("// GUESSES-PLACEHOLDER")
    w("")
    audit = {"rcc_registrations": 0, "conn_repointed": 0, "prop_repointed": 0, "renamed_blocks": 0}

    def count_rcc(reg_text):
        return len(re.findall(r"\b%s\s+RegistrationPeripheralAlias" % re.escape(ren.get("rcc", "rcc")), reg_text))

    def emit_items(items, guard_cls, inst, lines_out):
        """Body items, repointed; for a guard, properties filtered to its MANIFEST rows."""
        for it in items:
            if it[0] == "conn":
                new = repoint(it[1], ren, "conn")
                audit["conn_repointed"] += new != it[1]
                lines_out.append(new)
            elif it[0] == "prop":
                name, text = it[1], it[2]
                if guard_cls and name not in params[guard_cls]:
                    guess("%s: the stock or consumer property `%s` has no property row for %s, so it is not a constructor"
                          " parameter of the guard; dropped" % (inst, name, guard_cls))
                    continue
                new = repoint(text, ren, "prop")
                audit["prop_repointed"] += new != text
                lines_out.append(new)
            elif it[0] == "init":
                lines_out.extend(it[1])
            else:
                lines_out.append(it[1])

    # 1. Cancel each restated instance.
    w("// RUL-09 (b): cancel every instance the MANIFEST restates.")
    for inst in sorted(restate):
        w("%s: @none" % inst)
    w("")

    # 2. Declare each guard at its stock registration, as "<instance>", restating the stock entry.
    #    Collect every later stock block for the instance too (stock order), and the consumer's (consumer order).
    consumer_by_name = {}
    for b in cblocks:
        if b["name"]:
            consumer_by_name.setdefault(b["name"], []).append(b)
    for inst in [i for _, i, _ in man["restate"]]:
        cls, _ = restate[inst]
        g = man["class"][cls]
        rows = params[g]
        _, _, sb = typed[inst][0]
        n, t, reg, body = header_parts(sb)
        reg2 = repoint(reg, ren, "reg")
        audit["rcc_registrations"] += count_rcc(reg2)
        lines = ["%s: %s %s as \"%s\"" % (ren[inst], g, reg2, inst)]
        items = body_items(body)
        have = {it[1] for it in items if it[0] == "prop"}
        emit_items(items, g, inst, lines)
        # Later untyped blocks for this instance, stock first then consumer's, merged under the guard's variable.
        later = [b for b in stock_blocks if b["name"] == inst and b is not sb]
        cons = consumer_by_name.get(inst, [])
        later_items = []
        for b in later + cons:
            _, t2, r2, body2 = header_parts(b)
            if t2 or r2:
                die("a later block for %s carries a type or registration: %r" % (inst, b["lines"][0]))
            later_items.append((b, body_items(body2)))
            audit["renamed_blocks"] += 1
        for b, its in later_items:
            have |= {it[1] for it in its if it[0] == "prop"}
        # Consumer-supplied properties the stock entry lacks.
        add = []
        if "mode" in rows:
            add.append('    mode: "enforcing"')
        if "revision" in rows:
            add.append('    revision: "%s"' % SILICON_REVISION)
        if g == "Tviburi.GuardedSTM32H7_ADC" and inst in ADC_LEVELS:
            add.append('    levels: "%s"' % ADC_LEVELS[inst])
        if g == "Tviburi.GuardedSTM32_GPIOPort":
            port = inst[-1]
            mo, sp, pu = GPIO_RESET.get(port, GPIO_RESET_OTHER)
            add.append("    modeResetValue: 0x%08X" % mo)
            add.append("    outputSpeedResetValue: 0x%08X" % sp)
            add.append("    pullUpPullDownResetValue: 0x%08X" % pu)
            if inst in GPIO_EXTERNAL:
                add.append('    external: "%s"' % GPIO_EXTERNAL[inst])
            if package:
                add.append('    package: "%s"' % package)
        if g == "Tviburi.GuardedSTM32DMA":
            add.append("    dmamux: %s" % ren["dmamux1"])
        if g == "Tviburi.GuardedSTM32F7_USART" and inst in USART_RX_DMA:
            add.append("    ReceiveDmaRequest -> %s@%d" % (ren["dmamux1"], USART_RX_DMA[inst]))
        if g == "Tviburi.GuardedSTM32H7_RCC":
            # The board's HSE: the consumer's own rcc: block, if it declares one (it is merged below).
            if "hseFrequency" not in have:
                guess("rcc: the consumer's platform declares no HSE (no `rcc:` block), so hseFrequency is left out:"
                      " the row says that refuses TVI-RCC-HSE only at the HSE's start")
        lines.extend(add)
        # Required rows: every one must now be present.
        present = {re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:", l).group(1)
                   for l in lines[1:] + [l2 for _, its in later_items for it in its if it[0] == "prop" for l2 in [it[2]]]
                   if re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:", l) and "->" not in l}
        for pname, (req, meaning) in rows.items():
            if req == "required" and pname not in present:
                die("%s (%s): required property %s has no value: %s" % (inst, g, pname, meaning))
        out.extend(lines)
        w("")
        for b, its in later_items:
            bl = ["%s:" % ren[inst]]
            kept = []
            for it in its:
                if it[0] == "init" and g == "Tviburi.GuardedSTM32H7_Flash" and re.match(r"^\s*init add\s*:", it[1][0]):
                    # The consumer's factory step programs FLASH_OPTSR_PRG then OPTSTART: the row says the
                    # OPTSR_CUR word moves into optionBytes.
                    prg = [re.match(r"^\s*WriteDoubleWord\s+0x20\s+(0x[0-9A-Fa-f]+)\s*$", l) for l in it[1]]
                    prg = [m.group(1) for m in prg if m]
                    if len(prg) != 1:
                        die("flashController's init add: cannot read one FLASH_OPTSR_PRG write")
                    guess("flashController: the consumer's `init add` (option keys, OPTSR_PRG %s, OPTSTART, relock) is"
                          " replaced by optionBytes: %s, the OPTSR_CUR word it leaves; the stock entry has no init"
                          % (prg[0], prg[0]))
                    kept.append(("prop", "optionBytes", "    optionBytes: %s" % prg[0]))
                    continue
                kept.append(it)
            emit_items(kept, g, inst, bl)
            if b in cons:
                w("// the consumer's own block for %s, merged onto the guard (RUL-09 (b): never an update of the"
                  " cancelled variable)" % inst)
            out.extend(bl)
            w("")

    # 3. Live stock entries that reference a cancelled variable: restate their registration or connection.
    w("// Live stock entries: every registration on a cancelled variable, and every connection into one,")
    w("// restated onto the guard's variable.")
    for b in stock_blocks:
        if b["name"] in restate:
            continue
        n, t, reg, body = header_parts(b)
        lines = []
        if reg and reg != repoint(reg, ren, "reg"):
            reg2 = repoint(reg, ren, "reg")
            audit["rcc_registrations"] += count_rcc(reg2)
            lines.append("%s: %s" % (n, reg2))
        conns = [it[1] for it in body_items(body) if it[0] == "conn" and repoint(it[1], ren, "conn") != it[1]]
        props = [it for it in body_items(body) if it[0] == "prop" and repoint(it[2], ren, "prop") != it[2]]
        if props:
            die("live stock entry %s has a property naming a cancelled variable: %r" % (n, props))
        if conns:
            if not lines:
                lines.append("%s:" % n)
            for c in conns:
                lines.append(repoint(c, ren, "conn"))
                audit["conn_repointed"] += 1
        if lines:
            out.extend(lines)
            w("")

    # 4. The consumer's own platform, minus its blocks for cancelled instances (merged above), retyped and repointed.
    w("// The consumer's platform, retyped and repointed.")
    for b in cblocks:
        if not b["name"]:
            w(b["lines"][0])
            continue
        if b["name"] in restate:
            continue
        n, t, reg, body = header_parts(b)
        if n == "using":
            out.extend(b["lines"])
            continue
        if n in retype:
            cls = retype[n]
            if t != cls:
                die("retype %s %s: the consumer types it %r" % (cls, n, t))
            t = man["class"][cls]
        head = n + ":"
        if t:
            head += " " + t
        if reg:
            reg2 = repoint(reg, ren, "reg")
            audit["rcc_registrations"] += count_rcc(reg2)
            head += " " + reg2
        lines = [head]
        guard = t if t in params else None
        emit_items(body_items(body), guard, n, lines)
        if n in retype and "mode" in params[t]:
            lines.append('    mode: "enforcing"')
        out.extend(lines)

    text = "\n".join(out).rstrip("\n") + "\n"
    if "using" not in text.split("\n", 6)[-1] and not re.search(r'^using "', text, re.M):
        die("lost the using line")
    # Move the using line to the top (Renode requires `using` before entries).
    ulines = [l for l in text.split("\n") if l.startswith("using ")]
    rest = [l for l in text.split("\n") if not l.startswith("using ")]
    gl = "\n".join("// GUESS: " + g for g in GUESSES) or "// (no guesses)"
    text = "\n".join(rest[:1] + rest[1:2] + gl.split("\n") + ulines + [l for l in rest[2:] if l != "// GUESSES-PLACEHOLDER"])

    # Static audit: no cancelled name left in a reference position outside the cancel lines and `as "..."`.
    bad = []
    for b in parse_repl(text):
        if not b["name"]:
            continue
        n, t, reg, body = header_parts(b)
        if n in restate and not (len(b["lines"]) == 1 and b["lines"][0].strip() == "%s: @none" % n):
            bad.append("an update of cancelled %s: %r" % (n, b["lines"][0]))
        if reg:
            r = re.sub(r'as\s+"[^"]*"', "", reg)
            for inst in restate:
                if re.search(r"(@\s*|[{;\n]\s*)%s\b" % inst, r):
                    bad.append("%s's registration names %s" % (n, inst))
        for it in body_items(body):
            if it[0] == "conn":
                for inst in restate:
                    if re.search(r"\b%s(?=\s*[#@])" % inst, it[1].split("->", 1)[1]):
                        bad.append("%s's connection %r names %s" % (n, it[1].strip(), inst))
            if it[0] == "prop":
                m = re.match(r"^\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*([A-Za-z_][A-Za-z0-9_]*)\s*$", it[2])
                if m and m.group(1) in restate:
                    bad.append("%s's property %r names %s" % (n, it[2].strip(), m.group(1)))
    # The stock chain's own lines still name cancelled variables; each live one must be restated above.
    for b in stock_blocks:
        if b["name"] in restate:
            continue
        n, t, reg, body = header_parts(b)
        needs = (reg and reg != repoint(reg, ren, "reg")) or any(
            it[0] == "conn" and repoint(it[1], ren, "conn") != it[1] for it in body_items(body))
        if needs and not re.search(r"^%s:" % re.escape(n), text, re.M):
            bad.append("live stock entry %s references a cancelled variable and is not restated" % n)
    if bad:
        die("audit: " + "; ".join(bad))
    sys.stderr.write("consumer-platform: release %s; %d restated, %d retyped; %d registrations on %s; %d connections "
                     "and %d property values repointed; %d later blocks merged onto guards; audit clean\n"
                     % (release, len(restate), len(retype), audit["rcc_registrations"], ren.get("rcc"),
                        audit["conn_repointed"], audit["prop_repointed"], audit["renamed_blocks"]))
    for g in GUESSES:
        sys.stderr.write("GUESS: " + g + "\n")
    sys.stdout.write(text)


if __name__ == "__main__":
    main()
