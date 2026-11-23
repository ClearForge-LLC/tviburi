# TVI-WO-1301 section 1.1: read back, by reflection, what the GPIO restatement loaded. Run by the Renode monitor
# (`include @checks.py`) after LoadPlatformDescription. Lines start TVICHK. From spike/1150/retype/checks.py.
import clr
from System.Reflection import BindingFlags
F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
m = monitor.Machine

def name_of(p):
    ok, n = m.TryGetAnyName(p)
    return n if ok else "?"

def member(obj, nm):
    t = obj.GetType()
    while t is not None:
        f = t.GetField(nm, F)
        if f is not None:
            return f.GetValue(obj)
        pr = t.GetProperty(nm, F)
        if pr is not None:
            return pr.GetValue(obj, None)
        t = t.BaseType
    return None

def endpoint(e):
    # A CombinedInput (Renode's OR of several sources into one input) is followed to its destinations, so two platforms
    # that reach the same inputs through different combiners read the same.
    r = e.Receiver
    if r.GetType().Name == "CombinedInput":
        return "|".join(sorted(endpoint(x) for x in r.OutputLine.Endpoints))
    owner = member(r, "port")
    if owner is not None:
        return "%s#%s@%d" % (name_of(owner), member(r, "pin"), e.Number)
    owner = member(r, "parent")
    n = name_of(r)
    if n == "?":
        n = r.GetType().Name + ("(" + name_of(owner) + ")" if owner is not None else "")
    return "%s@%d" % (n, e.Number)

from Antmicro.Renode.Peripherals.GPIOPort import STM32_GPIOPort
print("TVICHK sealed: STM32_GPIOPort=%s" % clr.GetClrType(STM32_GPIOPort).IsSealed)

ports = []
for k in range(11):
    addr = 0x58020000 + 0x400 * k
    p = m.SystemBus.WhatPeripheralIsAt(addr)
    print("TVICHK at 0x%X: %s %s" % (addr, name_of(p) if p is not None else "-", p.GetType().FullName if p is not None else "-"))
    if p is not None:
        ports.append(p)

stock = [x for x in m.GetPeripheralsOfType[STM32_GPIOPort]() if x.GetType().FullName == "Antmicro.Renode.Peripherals.GPIOPort.STM32_GPIOPort"]
print("TVICHK stock STM32_GPIOPort instances in the machine: %d %s" % (len(stock), ",".join(name_of(x) for x in stock)))
print("TVICHK GPIO ports in the machine: %d" % len(list(m.GetPeripheralsOfType[STM32_GPIOPort]())))

for p in ports:
    nm = name_of(p)
    outs = []
    for kv in member(p, "Connections"):
        ends = sorted(set("|".join(endpoint(e) for e in kv.Value.Endpoints).split("|")) - {""})
        if ends:
            outs.append("%d->%s" % (kv.Key, "|".join(ends)))
    print("TVICHK %s out: %s" % (nm, " ".join(outs)))
    try:
        kids = [name_of(c) for c in m.GetChildrenPeripherals(p)]
    except Exception as ex:
        kids = ["ERR " + str(ex)]
    print("TVICHK %s children: %s" % (nm, ",".join(kids) or "-"))

for t in ("timer1", "timer2", "timer3", "timer4", "timer5", "timer8", "timer12", "timer13", "timer14", "timer15", "timer16", "timer17"):
    tp = None
    for e in m.SystemBus.GetRegisteredPeripherals():
        if name_of(e.Peripheral) == "sysbus." + t:
            tp = e.Peripheral
    if tp is None:
        print("TVICHK %s: not in the machine" % t)
        continue
    outs = []
    for kv in member(tp, "Connections"):
        ends = [endpoint(e) for e in kv.Value.Endpoints]
        if ends:
            outs.append("%d->%s" % (kv.Key, "|".join(ends)))
    print("TVICHK %s out: %s" % (t, " ".join(outs)))
print("TVICHK-END")
