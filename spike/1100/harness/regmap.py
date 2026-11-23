# TVI-WO-1100: the register map of every bus peripheral, read by reflection from the objects that executed.
# Run by the Renode monitor (`include @regmap.py`) after the run. Lines:
#   TVIBASE <peripheral> <class> <start> <size>
#   TVIMAP  <peripheral> <class> <offset> <cond#> <kind> <name> <pos> <width> <mode | silent=..,reset=..>
#     kind: field (a RegisterField: stored, its FieldMode) | tag (a Tag: the written value is dropped and the
#     tag reads its reset value)
#   TVIMAP  <peripheral> <class> none          no *RegisterCollection field: classify by source
#   TVIMAP-ERR <peripheral> <class> <error>    the reflection failed for this peripheral
import clr
from System import Array, Object, Int32
from System.Reflection import BindingFlags
F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public

def fields_of(t):
    out = []
    while t is not None:
        out.extend(t.GetFields(F))
        t = t.BaseType
    return out

def get(obj, name):
    t = obj.GetType()
    while t is not None:
        f = t.GetField(name, F)
        if f is not None:
            return f.GetValue(obj)
        p = t.GetProperty(name, F)
        if p is not None:
            return p.GetValue(obj, None)
        t = t.BaseType
    raise Exception("no member %s on %s" % (name, obj.GetType().FullName))

def collections(p):
    cols = []
    for f in fields_of(p.GetType()):
        if f.FieldType.Name.endswith("RegisterCollection"):
            v = f.GetValue(p)
            if v is not None and not any(c is v for c in cols):
                cols.append(v)
    return cols

def registers_of(sel):
    # A RegisterSelector holds conditional registers; a plain register is its own.
    if sel.GetType().Name.startswith("RegisterSelector"):
        STAGE[0] += " / conditionalRegisters"
        conds = get(sel, "conditionalRegisters")
        out = []
        lt = conds.GetType()
        count = lt.GetProperty("Count").GetValue(conds, None)
        for i in range(count):
            c = lt.GetProperty("Item").GetValue(conds, Array[Object]([Int32(i)]))
            STAGE[0] += " / Register of " + c.GetType().FullName
            out.append(c.GetType().GetProperty("Register", F).GetValue(c, None))
        return out
    return [sel]

STAGE = ["-"]

def dump_one(p, name, cls):
    STAGE[0] = "collections"
    cols = collections(p)
    if not cols:
        print("TVIMAP\t%s\t%s\tnone" % (name, cls))
        return
    for col in cols:
        STAGE[0] = "registers of " + col.GetType().FullName
        regs = get(col, "registers")
        for key in list(regs.Keys):
            STAGE[0] = "selector at 0x%X" % key
            sel = regs.GetType().GetProperty("Item").GetValue(regs, Array[Object]([key]))
            STAGE[0] += " (%s)" % (sel.GetType().FullName if sel is not None else "null")
            for ci, reg in enumerate(registers_of(sel)):
                STAGE[0] = "register at 0x%X (%s)" % (key, reg.GetType().FullName)
                kv = type("KV", (), {})()
                kv.Key = key
                for rf in get(reg, "registerFields"):
                    print("TVIMAP\t%s\t%s\t0x%X\t%d\tfield\t%s\t%d\t%d\t%s" % (name, cls, kv.Key, ci, get(rf, "Name"), get(rf, "Position"), get(rf, "Width"), get(rf, "FieldMode")))
                for tg in get(reg, "tags"):
                    print("TVIMAP\t%s\t%s\t0x%X\t%d\ttag\t%s\t%d\t%d\tsilent=%s,reset=0x%X" % (name, cls, kv.Key, ci, get(tg, "Name"), get(tg, "Position"), get(tg, "Width"), get(tg, "IsSilent"), get(tg, "ResetValue")))

m = monitor.Machine
for entry in m.SystemBus.GetRegisteredPeripherals():
    p = entry.Peripheral
    ok, name = m.TryGetAnyName(p)
    cls = p.GetType().FullName
    try:
        print("TVIBASE\t%s\t%s\t0x%X\t0x%X" % (name, cls, entry.RegistrationPoint.Range.StartAddress, entry.RegistrationPoint.Range.Size))
    except Exception as ex:
        print("TVIBASE\t%s\t%s\t?\t%s" % (name, cls, str(ex).replace("\n", " ")))
    try:
        dump_one(p, name, cls)
    except Exception as ex:
        print("TVIMAP-ERR\t%s\t%s\t%s: %s" % (name, cls, STAGE[0], str(ex).replace("\n", " ")))
print("TVIMAP-END")
