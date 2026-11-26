// tests/1204/TeardownHold.cs — test-only, never in a release (TVI-WO-1204 (2), architect 2026-11-26T15:48Z): a Renode logger
// backend that holds the asynchronous logging thread, so the teardown race's loss happens every time the flush is missing.
//
// The race (TVI-WO-1204's measure, §1): at a clean quit Renode disposes the old emulation's machines, whose guards log their
// end records, then that emulation's logger, whose Dispose sets `stopThread`, cancels the thread and joins it; whatever is still
// queued is never written. This backend's Log blocks until that logger's `stopThread` is true, so from the first entry it is
// given, the thread writes nothing more and everything after stays queued:
//   - without Records.End()'s Logger.Flush(), the queue is still full at Dispose, and every end record is lost, every time;
//   - with it, Flush sets `stopThread` (StopLoggingThread), this backend returns, and Flush writes the whole queue itself.
// `stopThread` is the pinned Renode build's private field (src/Emulator/Main/Logging/Logger.cs, ActualLogger), read by
// reflection; if it is not found, installing refuses, so the test fails rather than holding nothing. A hold longer than 90 s
// releases for good, so a mistake fails the run's check instead of hanging it.
//
// Installed from the monitor, after `include @/repo/tests/1204/TeardownHold.cs`: `emulation HoldLoggingUntilStopped`.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Antmicro.Renode.Core;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Tviburi.Test
{
    public static class TeardownHoldExtensions
    {
        public static void HoldLoggingUntilStopped(this Emulation emulation)
        {
            var logger = emulation.CurrentLogger;
            var stop = logger.GetType().GetField("stopThread", BindingFlags.NonPublic | BindingFlags.Instance);
            if(stop == null || stop.FieldType != typeof(bool))
            {
                throw new RecoverableException("TeardownHold: the logger has no bool stopThread field; this Renode build is not the pinned one");
            }
            Logger.AddBackend(new TeardownHold(logger, stop), "tviburi-teardown-hold", true);
        }
    }

    public sealed class TeardownHold : LoggerBackend
    {
        public TeardownHold(object logger, FieldInfo stop)
        {
            this.logger = logger;
            this.stop = stop;
        }

        public override void Log(LogEntry entry, Logger.TimestampType timestampType)
        {
            var held = Stopwatch.StartNew();
            while(!released && !(bool)stop.GetValue(logger))
            {
                if(held.ElapsedMilliseconds > 90000)
                {
                    released = true;
                    break;
                }
                Thread.Sleep(1);
            }
        }

        private volatile bool released;
        private readonly object logger;
        private readonly FieldInfo stop;
    }
}
