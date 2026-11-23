import clr
from System.Reflection import BindingFlags
F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
m = monitor.Machine
def member(obj, nm):
    t = obj.GetType()
    while t is not None:
        f = t.GetField(nm, F)
        if f is not None: return f.GetValue(obj)
        pr = t.GetProperty(nm, F)
        if pr is not None: return pr.GetValue(obj, None)
        t = t.BaseType
    return None
def name_of(p):
    ok, n = m.TryGetAnyName(p)
    return n if ok else "?"
rcc = m.SystemBus.WhatPeripheralIsAt(0x58024400)
inner = member(rcc, "inner")
reg = member(inner, "registeredPeripherals") or member(inner, "RegisteredPeripherals")
print("TVICHK rcc %s inner %s registry %s" % (rcc.GetType().Name, inner.GetType().Name if inner is not None else "-", reg.GetType().Name if reg is not None else "-"))
if reg is not None:
    for kv in reg:
        k = str(kv.Key)
        if "GPIO" in k: print("TVICHK rcc %s -> %s %s" % (k, name_of(kv.Value), kv.Value.GetType().Name))
    print("TVICHK rcc registry size %d" % reg.Count)
