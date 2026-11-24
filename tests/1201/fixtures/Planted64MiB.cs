// tests/1201/fixtures/Planted64MiB.cs — TVI-WO-1201's planted allocation: the memory budget's own test, never part
// of a release. tests/1201/test-budget.sh copies main's tree to scratch, drops this file into that copy's model/, and
// the copy's release concatenates it into the one file like any model (tools/tviburi-release: every model/*.cs).
// The budget must refuse that copy by name, with a median difference near 64 MiB.
// It runs: a module initializer runs before the first use of any type in the assembly, which is the first guard
// Renode creates; it prints TVIBURI-PLANTED to Renode's console, which the test counts once per boot. It stays
// resident: every page is written, and a static field holds the array.
using System.Runtime.CompilerServices;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    internal static class TviburiPlanted64MiB
    {
        internal static byte[] Held;

        [ModuleInitializer]
        internal static void Allocate()
        {
            Held = new byte[64 << 20];
            for (var i = 0; i < Held.Length; i += 4096)
            {
                Held[i] = 1;
            }
            System.Console.WriteLine("TVIBURI-PLANTED " + Held.Length + " bytes written and held");
        }
    }
}
