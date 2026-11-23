# TVI-WO-1150 (i): read back, by reflection from the objects that loaded, what a re-typing form produced.
# Run by the Renode monitor (`include @checks.py`) after LoadPlatformDescription. Lines start TVICHK.
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

def by_name(nm):
    # Registered peripherals only: a scan of the bus, matched by the machine's name for each.
    for e in m.SystemBus.GetRegisteredPeripherals():
        if name_of(e.Peripheral) == "sysbus." + nm:
            return e.Peripheral
    return None

for addr in (0x40010000, 0x58024400, 0x40017000, 0x58027000):
    p = m.SystemBus.WhatPeripheralIsAt(addr)
    print("TVICHK at 0x%X: %s %s" % (addr, name_of(p) if p is not None else "-", p.GetType().FullName if p is not None else "-"))

for nm in ("timer1", "rcc", "spiketimer", "spikercc", "tim1g", "rccg", "rccguard"):
    p = by_name(nm)
    if p is None:
        print("TVICHK %s: not in the machine" % nm)
        continue
    print("TVICHK %s: class %s" % (nm, p.GetType().FullName))
    try:
        parents = [name_of(x) for x in m.GetParentPeripherals(p)]
        print("TVICHK %s: parents %s" % (nm, ",".join(parents)))
    except Exception as ex:
        print("TVICHK %s: parents ERR %s" % (nm, str(ex).replace("\n", " ")))
    for prop in ("Frequency", "Limit"):
        v = member(p, prop)
        if v is not None:
            print("TVICHK %s: %s %s" % (nm, prop, v))
    for irq in ("BreakInterrupt", "UpdateInterrupt", "TriggerInterrupt", "CommutationInterrupt", "CaptureCompareInterrupt"):
        g = member(p, irq)
        if g is None:
            continue
        ends = []
        try:
            for e in g.Endpoints:
                ends.append("%s@%d" % (name_of(e.Receiver), e.Number))
        except Exception as ex:
            ends.append("ERR " + str(ex))
        print("TVICHK %s: %s connected=%s -> %s" % (nm, irq, g.IsConnected, ",".join(ends) or "-"))
    inner = member(p, "Inner")
    reg = member(inner if inner is not None else p, "RegisteredPeripherals")
    if reg is not None:
        print("TVICHK %s: RCC registered %d: %s" % (nm, reg.Count, ",".join(sorted("%s=%s" % (k, name_of(v)) for k, v in [(kv.Key, kv.Value) for kv in reg]))))

from Antmicro.Renode.Peripherals.Miscellaneous import STM32H7_RCC
from Antmicro.Renode.Peripherals.Timers import STM32_Timer
print("TVICHK sealed: STM32H7_RCC=%s STM32_Timer=%s" % (clr.GetClrType(STM32H7_RCC).IsSealed, clr.GetClrType(STM32_Timer).IsSealed))
print("TVICHK-END")
