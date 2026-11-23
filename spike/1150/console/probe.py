# TVI-WO-1150 (ii): log, under the guarded peripheral's own name, a multi-line message and three identical
# messages in a row, to see how the console and the logFile frame them. Monitor-side only: no firmware can
# run this. Included by the Renode monitor after the run.
from Antmicro.Renode.Logging import Logger, LogLevel
m = monitor.Machine
spi1 = [e.Peripheral for e in m.SystemBus.GetRegisteredPeripherals() if m.TryGetAnyName(e.Peripheral)[1] == "sysbus.spi1"][0]
Logger.Log(spi1, LogLevel.Warning, 'TVI1150-MULTI first line\n{"tviburi":"refusal","seq":99,"rule":"TVI1150-CONTINUATION"}\n  indented third line')
for i in range(3):
    Logger.Log(spi1, LogLevel.Warning, 'TVI1150-REPEAT identical message')
Logger.Log(spi1, LogLevel.Warning, 'TVI1150-REPEAT-END')
