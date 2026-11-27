// Tviburi's guard over Renode's STM32_GPIOPort, for each of the STM32H753's GPIO ports A-K (TVI-WO-1301).
// The field table, every page, and the rulings: docs/work-orders/TVI-WO-1301.rm0433.md (RM0433 Rev 9, chapter 11) and
// TVI-WO-1301.ds12117.md (DS12117 Rev 10).
//
// Insertion point (RUL-07): STM32_GPIOPort is not sealed (measured by reflection in the pinned build), so this guard is
// a subclass that re-implements the three bus access interfaces. The stock platform declares every port, so the board
// description cancels each stock port and declares this guard at its address `as "gpioPortX"`, restating its
// registration, its constructor arguments and every connection that names it (RUL-09 (b);
// model/stm32h7-gpio-restate.repl, generated from the pinned stm32h7.repl). The stock port's AF routing, connections,
// local AF receivers and children are inherited unchanged.
//
// Which port it is comes from its own bus registration (0x58020000 + 0x400 * k, ports A-K), never from a name.
//
// What it models, beyond the stock port:
//   - silicon's reset values (RM0433 Rev 9 §11.4.1 p541 MODER: 0xABFFFFFF port A, 0xFFFFFEBF port B, 0xFFFFFFFF
//     others; §11.4.3-11.4.4 p542 OSPEEDR and PUPDR). They reach the stock port as its constructor arguments, from
//     the restatement, and this guard checks them against its port at the first access (TVI-WO-1301 A5);
//   - OTYPER, which the stock port drops (a tag): kept here and served on read (RUL-17; TVI-WO-1301 A1);
//   - BSRR's priority: "If both BSx and BRx are set, BSx has priority" (§11.4.7 p544), in the guard's ODR copy (the
//     stock port applies BR last);
//   - the query "which signal does pin P carry now", from MODER, AFSEL and DS12117 Rev 10 Table 9 (WhichSignal);
//   - what drives a pin (TVI-WO-1301a; RM0433 Rev 9 §11.3.9-11.3.13 p537-539). The stock port drives every pin's
//     connections (LEDs, EXTI, timer inputs, devices) and IDR from ODR in every mode. Here ODR is latched by the guard
//     and read back as written (p537), and a pin carries: in output mode, ODR (p537); in AF mode, the peripheral
//     output the platform routes to its AFSEL (only the timers have routes), taken at the switch and followed through
//     the stock port's AF receiver (p538); in input and analog mode, nothing from the port (the output buffer is
//     disabled, p537, p539). A pin the port does not drive takes the level from outside the port: an external
//     sender's, else the board's declared level (`external`), else PUPDR's pull (not in analog mode, p539); with none,
//     an input or analog pin is low, not driven (ruled 2026-11-24T01:05Z), and an AF pin no Renode model drives holds
//     its level (ruled 2026-11-23T23:52Z (3)). IDR reads by mode (Idr). The consumer declares its board's levels.
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock port.
//   TVI-LOAD          (Records) the model version could not be computed: every access refuses.
//   TVI-GPIO-CONFIG   the platform gives this guard what it cannot model: a bus address that is no port, numberOfAFs
//                     other than 16, a reset value other than RM0433's for its port, or a package with no bonding
//                     data. Every access refuses. unmodeled.
//   TVI-GPIO-WIDTH    a misaligned access. Word, half-word and byte accesses are valid (§11.4 p541; §11.3 p531).
//   TVI-GPIO-READ     a read of an offset with no register (Table 95, p547-548: the map ends at 0x24). unmodeled.
//   TVI-GPIO-FIELD    the allow-list (`Fields`): a write that gives any field a value the model does not implement, or
//                     a write to an offset with no register (the stock port's 0x28 BRR is RM0090's, not the H7's).
//                     unmodeled.
//   TVI-GPIO-AF       a write after which a pin is in alternate-function mode (MODER = 10) selecting an AF with no
//                     function on that pin in DS12117 Rev 10 Table 9 (a blank cell), or a pin the table does not list
//                     (PK8-PK15: port K has PK0-PK7, p100). predicted-fault, naming the pin, the AF and the page.
//   TVI-GPIO-PACKAGE  with a package declared: a write after which a pin the package does not bond (or that DS12117
//                     lists on no package, PK8-PK15) is in output or AF mode (MODER 01 or 10). predicted-fault, naming the pin, the package and the page. PC2 and PC3
//                     on LQFP144 are unmodeled: their alternate functions reach PC2_C and PC3_C when the SYSCFG analog
//                     switch is closed (DS12117 Table 8 footnote 4, p88), which the twin does not model. Input and
//                     analog writes pass, so a firmware's whole-port initialisation does (ruled 2026-11-23T17:52Z).
//                     With no package declared the rule does not run, and the witness says "package": "none", so a
//                     consumer sees a check that did not run (ruled 17:52Z; the witness field granted 19:08Z).
// TVI-GPIO-AF and TVI-GPIO-PACKAGE record each pin that enters the faulting state, once, not on every later write.
// Enforcing and recorded mode (RUL-19) are model/Refusal.cs's contract.
// A port whose RCC clock is off is locked by the RCC guard's sync (TVI-WO-1300a), before this guard sees the access;
// it adds no rule of its own for it (ruled 2026-11-23T17:52Z).

using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.GPIOPort;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32_GPIOPort : STM32_GPIOPort, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IDisposable
    {
        // The stock entry's constructor arguments, passed through; `mode` (RUL-19), `package` and `external` are this
        // guard's. `package` names the package the platform's part is in ("LQFP144"); null: none declared, the package
        // rule off. `external` declares the board's levels on this port's pins, which the twin cannot know: "PB6:up
        // PB7:up" (up or down, a pull or a driven level alike); null: none (TVI-WO-1301a, ruled 2026-11-23T23:52Z).
        public GuardedSTM32_GPIOPort(IMachine machine, uint modeResetValue = 0, uint outputSpeedResetValue = 0,
            uint pullUpPullDownResetValue = 0, uint numberOfAFs = 16, List<List<int>> invertedAFPins = null,
            string mode = Records.Enforcing, string package = null, string external = null)
            : base(machine, modeResetValue, outputSpeedResetValue, pullUpPullDownResetValue, numberOfAFs, invertedAFPins)
        {
            this.machine = machine;
            resets = new[] { modeResetValue, outputSpeedResetValue, pullUpPullDownResetValue };
            afs = numberOfAFs;
            Package = package;
            External = external;
            foreach(var l in invertedAFPins ?? new List<List<int>>())
            {
                foreach(var af in l.Skip(1))
                {
                    inverted.Add(l[0] * 16 + af);
                }
            }
            records = new Records(machine, this, nameof(GuardedSTM32_GPIOPort), mode, new[]
            {
                new KeyValuePair<string, string>("external", external ?? "none"),
                new KeyValuePair<string, string>("package", package ?? "none"),
            });
        }

        public string Package { get; }
        public string External { get; }

        public override void Reset()
        {
            base.Reset();
            otyper = 0;
            afFaulted = 0;
            packageFaulted = 0;
            odr = 0;   // a sender's level is outside the port, so a reset keeps it
            if(config == "")
            {
                Drive(0xFFFF);   // the declared levels and the reset pulls, on the pins nobody drives (silicon's reset: analog)
            }
        }

        // An external sender into the port (a button, a device's output: `-> gpioPortX@pin`) sets the pin's level from
        // outside. The pin follows it unless the port drives the pin itself (output, or AF with a peripheral behind it).
        public override void OnGPIO(int number, bool value)
        {
            if(number < 0 || number > 15)
            {
                base.OnGPIO(number, value);   // the stock port's own error
                return;
            }
            senderKnown |= 1u << number;
            sender = value ? sender | 1u << number : sender & ~(1u << number);
            if(config == "")
            {
                Drive(1u << number);
            }
            else
            {
                SetPin(number, value);   // before the first access, or with a configuration it refuses, the port drives no pin
            }
        }

        public void Dispose()
        {
            records.End();
        }

        // ---- the query ----------------------------------------------------------------------------------------

        // Which signal pin `pin` of this port carries now: "input", "output" or "analog" from MODER; in AF mode, the
        // DS12117 Rev 10 Table 9 cell of its AFSEL, "-" where the cell is blank (no function). Null if the guard
        // cannot say (not loaded, or no such pin).
        public string WhichSignal(int pin)
        {
            if(pin < 0 || pin > 15 || records.LoadFailed || Config() != null)
            {
                return null;
            }
            var mode = (base.ReadDoubleWord(ModerOffset) >> (2 * pin)) & 3;
            if(mode != 2)
            {
                return mode == 0 ? "input" : mode == 1 ? "output" : "analog";
            }
            return CellAt(pin, Af(base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset), pin))?.Text ?? "-";
        }

        // The citation for WhichSignal's answer on `pin`: the table and page, or RM0433's MODER.
        public string WhichSignalCitation(int pin)
        {
            var s = WhichSignal(pin);
            if(s == null)
            {
                return null;
            }
            var mode = (base.ReadDoubleWord(ModerOffset) >> (2 * pin)) & 3;
            var cell = mode == 2 ? CellAt(pin, Af(base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset), pin)) : null;
            return mode != 2 ? "RM0433 Rev 9 §11.4.1 p541"
                : (cell == null ? "DS12117 Rev 10 Table 9 p100 (port K: PK0-PK7 only)" : "DS12117 Rev 10 Table 9 p" + cell.Page)
                    + "; RM0433 Rev 9 §11.4.9-11.4.10 p545-546";
        }

        // ---- bus access ----------------------------------------------------------------------------------------

        public new uint ReadDoubleWord(long offset)
        {
            return Read(offset, 32);
        }

        public new void WriteDoubleWord(long offset, uint value)
        {
            Write(offset, value, 32);
        }

        public ushort ReadWord(long offset)
        {
            return (ushort)Read(offset, 16);
        }

        public void WriteWord(long offset, ushort value)
        {
            Write(offset, value, 16);
        }

        public byte ReadByte(long offset)
        {
            return (byte)Read(offset, 8);
        }

        public void WriteByte(long offset, byte value)
        {
            Write(offset, value, 8);
        }

        private uint Read(long offset, int width)
        {
            var b = offset & ~3L;
            if(Unloaded(offset, 0, width) || Misaligned(offset, 0, width, "read"))
            {
                return records.Recorded ? Served(b) >> (int)(8 * (offset & 3)) : 0;
            }
            if(b > AfrhOffset)
            {
                records.Refuse("TVI-GPIO-READ", Unmodeled, Register(b), "no register at offset 0x" + offset.ToString("X"),
                    "read of a GPIO offset where RM0433 defines no register: the twin cannot say what silicon returns (the stock port answers 0x28 as RM0090's BRR)",
                    "RM0433 Rev 9 §11.4.11 Table 95 p547-548", State(), offset, 0, width);
                if(!records.Recorded)
                {
                    return 0;
                }
            }
            return Served(b) >> (int)(8 * (offset & 3));
        }

        // A half-word or byte write is merged into the word it is part of, as the register holds it (BSRR, write-only,
        // reads 0: the half written is the whole write), and judged as that word.
        private void Write(long offset, uint value, int width)
        {
            var b = offset & ~3L;
            var mask = width == 32 ? 0xFFFFFFFFu : width == 16 ? 0xFFFFu : 0xFFu;
            var shift = (int)(8 * (offset & 3));
            var word = width == 32 ? value : (Served(b) & ~(mask << shift)) | ((value & mask) << shift);
            if(Unloaded(offset, value, width) || Misaligned(offset, value, width, "write"))
            {
                if(!records.Recorded)
                {
                    return;
                }
            }
            else if(records.Recorded)
            {
                // Recorded mode (RUL-19) measures every path: each rule is evaluated and records what it finds.
                RefusedField(offset, b, word, width);
                RefusedAf(offset, b, word, width);
                RefusedPackage(offset, b, word, width);
            }
            else if(RefusedField(offset, b, word, width) || RefusedAf(offset, b, word, width) || RefusedPackage(offset, b, word, width))
            {
                return;
            }
            Pass(b, word);
        }

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, Register(offset & ~3L), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c != null)
            {
                records.Refuse("TVI-GPIO-CONFIG", Unmodeled, Register(offset & ~3L), "none",
                    "the platform gives this GPIO guard what it cannot model: " + c + "; every access refuses",
                    "RM0433 Rev 9 §11.4.1 p541, §11.4.3-11.4.4 p542 (reset values); DS12117 Rev 10 Table 8 p64-88 (packages)",
                    "none", offset, value, width);
                return true;
            }
            return false;
        }

        private bool Misaligned(long offset, ulong value, int width, string what)
        {
            if((offset & (width / 8 - 1)) == 0)
            {
                return false;
            }
            records.Refuse("TVI-GPIO-WIDTH", Unmodeled, Register(offset & ~3L), "misaligned " + width + "-bit " + what,
                "a misaligned " + width + "-bit GPIO " + what + " at offset 0x" + offset.ToString("X") + ": the twin cannot say what silicon does with it",
                "RM0433 Rev 9 §11.4 p541 (word, half word or byte); §11.3 p531", State(), offset, value, width);
            return true;
        }

        // What a read returns: OTYPER and ODR from the guard's copies (the stock port drops OTYPER, and reads ODR back
        // from the pins), IDR by mode, the rest from the stock port.
        private uint Served(long b)
        {
            return b == OtyperOffset ? otyper : b == OdrOffset ? odr : b == IdrOffset ? Idr() : base.ReadDoubleWord(b);
        }

        // IDR, pin by pin (RM0433 Rev 9 §11.3.9-11.3.13 p537-539): output, the pin as ODR drives it (push-pull; no
        // open-drain behaviour, TVI-WO-1301 A1); AF and input, the pin's level (Drive keeps it: the peripheral's signal,
        // the level from outside the port, or for input 0 where there is none, a floating pin whose level silicon does
        // not define: the ledger's IDR row); analog, 0 (p539).
        private uint Idr()
        {
            var moder = base.ReadDoubleWord(ModerOffset);
            uint v = 0;
            for(var pin = 0; pin < 16; pin++)
            {
                var m = (moder >> (2 * pin)) & 3;
                var level = m == 1 ? ((odr >> pin) & 1) == 1 : m != 3 && Connections[pin].IsSet;
                v |= level ? 1u << pin : 0;
            }
            return v;
        }

        private void Pass(long b, uint value)
        {
            if(b == OdrOffset || b == BsrrOffset || b == BrrOffset)
            {
                // ODR is the guard's: the stock port would drive every pin from it, in every mode (TVI-WO-1301 F1).
                // BRR (0x28) reaches here only in recorded mode, where it does what the stock port does with it.
                var old = odr;
                odr = b == OdrOffset ? value & 0xFFFFu
                    : b == BrrOffset ? odr & ~value & 0xFFFFu
                    : (odr & ~(value >> 16) | value) & 0xFFFFu;   // BSRR: BSx has priority over BRx (p544)
                Drive(old ^ odr);
                return;
            }
            if(b == OtyperOffset)
            {
                otyper = value & 0xFFFFu;
            }
            if(b == ModerOffset || b == OspeedrOffset || b == PupdrOffset)
            {
                // The stock port's register caches the last value written and calls a field's change handler only
                // where a write differs from it. Reset leaves the cache at 0 while the port's modes, speeds and pulls
                // take the reset values (silicon's, non-zero), so a write of 00 after a reset would be dropped. A read
                // refreshes the cache from the port's state (Renode's PeripheralRegister.WriteInner, ReadInner).
                base.ReadDoubleWord(b);
            }
            var before = Selection();
            base.WriteDoubleWord(b, value);
            var after = Selection();
            // A pin leaves a fault's latch when it leaves the faulting state; only a recorded pin enters it.
            var moder = base.ReadDoubleWord(ModerOffset);
            afFaulted &= AfFaults(moder, base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset));
            packageFaulted &= PackageFaults(moder);
            // A pin whose mode, AF or pull changed is driven again: switching to output drives the latched ODR, entering
            // AF the peripheral's level now, leaving both the level from outside the port or the one held.
            uint changed = 0;
            for(var pin = 0; pin < 16; pin++)
            {
                if(before[pin] != after[pin])
                {
                    changed |= 1u << pin;
                }
            }
            Drive(changed);
        }

        // ---- the pins: what drives each one (RM0433 Rev 9 §11.3.9-11.3.13 p537-539) ----------------------------------

        // Per pin, what decides its level: its mode, its AF in AF mode, its pull.
        private uint[] Selection()
        {
            var moder = base.ReadDoubleWord(ModerOffset);
            var afrl = base.ReadDoubleWord(AfrlOffset);
            var afrh = base.ReadDoubleWord(AfrhOffset);
            var pupdr = base.ReadDoubleWord(PupdrOffset);
            var s = new uint[16];
            for(var pin = 0; pin < 16; pin++)
            {
                s[pin] = (moder >> (2 * pin) & 3) | (uint)Af(afrl, afrh, pin) << 2 | (pupdr >> (2 * pin) & 3) << 6;
            }
            return s;
        }

        // Each pin in `pins` set to what drives it. An input or analog pin with no level from outside goes low (not
        // driven, p537, p539; ruled 2026-11-24T01:05Z); an AF pin with no Renode source and none from outside holds.
        private void Drive(uint pins)
        {
            if(pins == 0)
            {
                return;
            }
            var moder = base.ReadDoubleWord(ModerOffset);
            for(var pin = 0; pin < 16; pin++)
            {
                if((pins >> pin & 1) == 0)
                {
                    continue;
                }
                var m = (moder >> (2 * pin)) & 3;
                var level = m == 1 ? ((odr >> pin) & 1) == 1                       // output: ODR (p537)
                    : m == 2 ? Peripheral(pin) ?? Outside(pin, m)                     // AF: the peripheral's signal (p538)
                    : Outside(pin, m) ?? false;                                        // input, analog: output buffer off (p537, p539)
                if(level.HasValue)
                {
                    SetPin(pin, level.Value);
                }
            }
        }

        private void SetPin(int pin, bool level)
        {
            base.State[pin] = level;
            Connections[pin].Set(level);
        }

        // The level from outside the port on a pin the port does not drive: a sender's, else the board's declared level,
        // else PUPDR's pull (none in analog mode: "disabled by hardware", p539); null: none.
        private bool? Outside(int pin, uint mode)
        {
            if((senderKnown >> pin & 1) == 1)
            {
                return (sender >> pin & 1) == 1;
            }
            if((declaredMask >> pin & 1) == 1)
            {
                return (declaredHigh >> pin & 1) == 1;
            }
            var pull = mode == 3 ? 0 : base.ReadDoubleWord(PupdrOffset) >> (2 * pin) & 3;
            return pull == 1 ? true : pull == 2 ? (bool?)false : null;
        }

        // In AF mode, the level of the peripheral output routed to the pin's selected AF now (the stock port's AF
        // receiver carries its later edges); null: no Renode model drives that AF (only the timers do, through the
        // platform's routes).
        private bool? Peripheral(int pin)
        {
            var af = Af(base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset), pin);
            var sources = Sources()[pin * 16 + af];
            if(sources == null)
            {
                return null;
            }
            return sources.Any(g => g.IsSet) ^ inverted.Contains(pin * 16 + af);
        }

        // Every peripheral output wired to one of this port's AF receivers (`timer1: 0 -> gpioPortA#08@01`), by pin and
        // AF, found once in the loaded machine.
        private List<IGPIO>[] Sources()
        {
            if(sources == null)
            {
                var receivers = Enumerable.Range(0, 16).Select(GetLocalReceiver).ToArray();
                sources = new List<IGPIO>[256];
                foreach(var p in machine.GetPeripheralsOfType<INumberedGPIOOutput>())
                {
                    foreach(var g in p.Connections.Values)
                    {
                        foreach(var e in g.Endpoints)
                        {
                            var pin = Array.IndexOf(receivers, e.Receiver);
                            if(pin >= 0 && e.Number >= 0 && e.Number < 16)
                            {
                                (sources[pin * 16 + e.Number] = sources[pin * 16 + e.Number] ?? new List<IGPIO>()).Add(g);
                            }
                        }
                    }
                }
            }
            return sources;
        }

        // ---- rules ---------------------------------------------------------------------------------------------

        private bool RefusedField(long offset, long b, uint word, int width)
        {
            var rows = Fields.Where(f => f.Offset == b).ToArray();
            if(rows.Length == 0)
            {
                records.Refuse("TVI-GPIO-FIELD", Unmodeled, Register(b), "no register at offset 0x" + offset.ToString("X"),
                    "write to a GPIO offset where RM0433 defines no register: the twin cannot say what silicon does (the stock port takes 0x28 as RM0090's BRR and clears ODR bits)",
                    "RM0433 Rev 9 §11.4.11 Table 95 p547-548", State(), offset, word, width);
                return true;
            }
            foreach(var f in rows)
            {
                var v = f.Of(word);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    var reg = Register(b);
                    records.Refuse("TVI-GPIO-FIELD", Unmodeled, reg, reg + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        reg + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-1301.rm0433.md)",
                        State(), offset, word, width);
                    return true;
                }
            }
            return false;
        }

        private bool RefusedAf(long offset, long b, uint word, int width)
        {
            if(b != ModerOffset && b != AfrlOffset && b != AfrhOffset)
            {
                return false;
            }
            var moder = b == ModerOffset ? word : base.ReadDoubleWord(ModerOffset);
            var afrl = b == AfrlOffset ? word : base.ReadDoubleWord(AfrlOffset);
            var afrh = b == AfrhOffset ? word : base.ReadDoubleWord(AfrhOffset);
            var entering = AfFaults(moder, afrl, afrh) & ~afFaulted;
            if(entering == 0)
            {
                return false;
            }
            for(var pin = 0; pin < 16; pin++)
            {
                if((entering & (1 << pin)) != 0)
                {
                    RefuseAf(offset, b, word, width, moder, afrl, afrh, pin);
                }
            }
            afFaulted |= entering;
            return true;
        }

        private void RefuseAf(long offset, long b, uint word, int width, uint moder, uint afrl, uint afrh, int pin)
        {
            var af = Af(afrl, afrh, pin);
            var cell = CellAt(pin, af);
            records.Refuse("TVI-GPIO-AF", PredictedFault, Register(b), "P" + port + pin + " AF" + af,
                "pin P" + port + pin + " in alternate-function mode (MODER = 10) selects AF" + af + (cell == null
                    ? ", but DS12117's alternate-function table lists no pin P" + port + pin + " (port K has PK0-PK7): on silicon no pin carries the signal"
                    : ", which has no function on that pin in DS12117's alternate-function table: on silicon the pin carries no peripheral's signal"),
                (cell == null ? "DS12117 Rev 10 Table 9 p100 (port K: PK0-PK7 only)" : "DS12117 Rev 10 Table 9 p" + cell.Page + " (P" + port + pin + " AF" + af + ": -)")
                    + "; RM0433 Rev 9 §11.4.1 p541 (MODER), §11.4.9-11.4.10 p545-546 (AFSEL)",
                State(moder, afrl, afrh), offset, word, width);
        }

        private bool RefusedPackage(long offset, long b, uint word, int width)
        {
            if(b != ModerOffset || Package == null)
            {
                return false;
            }
            var entering = PackageFaults(word) & ~packageFaulted;
            if(entering == 0)
            {
                return false;
            }
            for(var pin = 0; pin < 16; pin++)
            {
                if((entering & (1 << pin)) != 0)
                {
                    RefusePackage(offset, b, word, width, pin);
                }
            }
            packageFaulted |= entering;
            return true;
        }

        private void RefusePackage(long offset, long b, uint word, int width, int pin)
        {
            var name = "P" + port + pin;
            Bonding(Package).TryGetValue(name, out var b8);   // null: a pin DS12117 lists on no package (PK8-PK15)
            var mode = ((word >> (2 * pin)) & 3) == 1 ? "output" : "alternate-function";
            if(name == "PC2" || name == "PC3")
            {
                records.Refuse("TVI-GPIO-PACKAGE", Unmodeled, Register(b), name + " MODER = " + (mode == "output" ? "01" : "10"),
                    name + " set to " + mode + " mode on " + Package + ", which does not bond " + name + "; its alternate functions reach " + name + "_C when the SYSCFG analog switch is closed, which the twin does not model",
                    "DS12117 Rev 10 Table 8 p" + b8.Page + " (" + Package + ": -) and footnote 4 p88", State(), offset, word, width);
                return;
            }
            records.Refuse("TVI-GPIO-PACKAGE", PredictedFault, Register(b), name + " MODER = " + (mode == "output" ? "01" : "10"),
                name + " set to " + mode + " mode, but the declared package " + Package + (b8 == null ? " bonds no " + name + ", which DS12117 lists on no package (port K has PK0-PK7)" : " does not bond " + name) + ": on silicon the signal reaches no pin",
                (b8 == null ? "DS12117 Rev 10 Table 8 p64-88 (no " + name + " row), Table 9 p100" : "DS12117 Rev 10 Table 8 p" + b8.Page + " (" + Package + " column: -)") + ", Figure 6 p57; RM0433 Rev 9 §11.4.1 p541",
                State(word, base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset)), offset, word, width);
        }

        // The pins in AF mode on a blank cell.
        private int AfFaults(uint moder, uint afrl, uint afrh)
        {
            var m = 0;
            for(var pin = 0; pin < 16; pin++)
            {
                if(((moder >> (2 * pin)) & 3) != 2)
                {
                    continue;
                }
                var cell = CellAt(pin, Af(afrl, afrh, pin));
                if(cell == null || cell.Text == "-")
                {
                    m |= 1 << pin;
                }
            }
            return m;
        }

        // The pins in output or AF mode that the declared package does not bond (none with no package declared).
        private int PackageFaults(uint moder)
        {
            if(Package == null)
            {
                return 0;
            }
            var bonding = Bonding(Package);
            var m = 0;
            for(var pin = 0; pin < 16; pin++)
            {
                var mode = (moder >> (2 * pin)) & 3;
                if((mode == 1 || mode == 2) && (!bonding.TryGetValue("P" + port + pin, out var b) || b.Pin == null))
                {
                    m |= 1 << pin;
                }
            }
            return m;
        }

        private static int Af(uint afrl, uint afrh, int pin)
        {
            return (int)(((pin < 8 ? afrl : afrh) >> (4 * (pin & 7))) & 0xF);
        }

        // ---- the port, its configuration, its data -------------------------------------------------------------

        // Null when this guard can model what the platform gave it; otherwise why not. Computed at the first access,
        // from the guard's own bus registration (never a name).
        private string Config()
        {
            if(config == null)
            {
                config = Check();
                if(config.Length == 0)
                {
                    Drive(0xFFFF);   // the declared levels and the pulls, on the pins nobody drives
                }
            }
            return config.Length == 0 ? null : config;
        }

        private string Check()
        {
            ulong start;
            try
            {
                var points = machine.SystemBus.GetRegistrationPoints(this).ToArray();
                if(points.Length != 1)
                {
                    return points.Length + " bus registrations, not one: a second address would reach the stock port's registers";
                }
                start = points[0].Range.StartAddress;
            }
            catch(Exception e)
            {
                return "no bus registration (" + e.GetType().Name + ")";
            }
            if(start < PortsBase || (start - PortsBase) % 0x400 != 0 || (start - PortsBase) / 0x400 > 10)
            {
                return "a bus address, 0x" + start.ToString("X") + ", that is no GPIO port (0x58020000 + 0x400 * k, ports A-K, RM0433 Rev 9 §2.3 Table 8 p132)";
            }
            port = (char)('A' + (int)((start - PortsBase) / 0x400));
            if(afs != 16)
            {
                return "numberOfAFs = " + afs + ", not the 16 of DS12117 Table 9";
            }
            var want = port == 'A' ? new[] { 0xABFFFFFFu, 0x0C000000u, 0x64000000u } : port == 'B' ? new[] { 0xFFFFFEBFu, 0x000000C0u, 0x00000100u } : new[] { 0xFFFFFFFFu, 0u, 0u };
            var names = new[] { "modeResetValue (MODER, p541)", "outputSpeedResetValue (OSPEEDR, p542)", "pullUpPullDownResetValue (PUPDR, p542)" };
            for(var i = 0; i < 3; i++)
            {
                if(resets[i] != want[i])
                {
                    return names[i] + " = 0x" + resets[i].ToString("X8") + " for port " + port + ", not RM0433's 0x" + want[i].ToString("X8");
                }
            }
            if(Package != null && Bonding(Package) == null)
            {
                return "package \"" + Package + "\", for which the guard holds no bonding (it holds LQFP144's, DS12117 Rev 10 Table 8)";
            }
            foreach(var d in (External ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = d.Split(':');
                var pin = -1;
                var ok = f.Length == 2 && f[0].Length >= 3 && f[0][0] == 'P' && f[0][1] == port && int.TryParse(f[0].Substring(2), out pin)
                    && pin >= 0 && pin <= 15 && f[0].Substring(2) == pin.ToString() && (declaredMask >> pin & 1) == 0 && (f[1] == "up" || f[1] == "down");
                if(!ok)
                {
                    return "external = \"" + External + "\": \"" + d + "\" is not P" + port + "<pin>:up or :down, once per pin of port " + port;
                }
                declaredMask |= 1u << pin;
                declaredHigh |= f[1] == "up" ? 1u << pin : 0;
            }
            return "";
        }

        private Cell CellAt(int pin, int af)
        {
            return Table().TryGetValue("P" + port + pin, out var cells) ? cells[af] : null;
        }

        private static Dictionary<string, Cell[]> table;

        private static Dictionary<string, Cell[]> Table()
        {
            if(table == null)
            {
                var t = new Dictionary<string, Cell[]>();
                foreach(var row in AfTable.Split(';'))
                {
                    var f = row.Split(':');
                    var page = int.Parse(f[1]);
                    t[f[0]] = f[2].Split(',').Select(c => new Cell(c, page)).ToArray();
                }
                table = t;
            }
            return table;
        }

        private static Dictionary<string, Bond> lqfp144;

        // A package's bonding by pin name; null for a package the guard holds no data for.
        private static Dictionary<string, Bond> Bonding(string package)
        {
            if(package != "LQFP144")
            {
                return null;
            }
            if(lqfp144 == null)
            {
                lqfp144 = Lqfp144.Split(';').Select(r => r.Split(':')).ToDictionary(f => f[0], f => new Bond(f[1] == "-" ? (int?)null : int.Parse(f[1]), int.Parse(f[2])));
            }
            return lqfp144;
        }

        private string Register(long b)
        {
            var reg = Fields.FirstOrDefault(f => f.Offset == b)?.Register;
            return reg == null ? "GPIO" + (port == '\0' ? 'x' : port) + " offset 0x" + b.ToString("X") : reg.Replace("GPIOx", "GPIO" + (port == '\0' ? 'x' : port));
        }

        private string State()
        {
            return State(base.ReadDoubleWord(ModerOffset), base.ReadDoubleWord(AfrlOffset), base.ReadDoubleWord(AfrhOffset));
        }

        private string State(uint moder, uint afrl, uint afrh)
        {
            return "port " + port + " MODER=0x" + moder.ToString("X8") + " AFRL=0x" + afrl.ToString("X8") + " AFRH=0x" + afrh.ToString("X8")
                + " package=" + (Package ?? "none");
        }

        private sealed class Cell
        {
            public Cell(string text, int page)
            {
                Text = text;
                Page = page;
            }

            public string Text { get; }
            public int Page { get; }
        }

        private sealed class Bond
        {
            public Bond(int? pin, int page)
            {
                Pin = pin;
                Page = page;
            }

            public int? Pin { get; }
            public int Page { get; }
        }

        private const long ModerOffset = 0x00;
        private const long OtyperOffset = 0x04;
        private const long OspeedrOffset = 0x08;
        private const long PupdrOffset = 0x0C;
        private const long IdrOffset = 0x10;
        private const long OdrOffset = 0x14;
        private const long BsrrOffset = 0x18;
        private const long BrrOffset = 0x28;
        private const long AfrlOffset = 0x20;
        private const long AfrhOffset = 0x24;
        private const ulong PortsBase = 0x58020000;
        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";

        private readonly IMachine machine;
        private readonly Records records;
        private readonly uint[] resets;
        private readonly uint afs;
        private string config;
        private char port;
        private uint otyper;
        private int afFaulted;
        private int packageFaulted;
        private uint odr;                       // ODR as written (p543), latched by the guard
        private uint sender, senderKnown;       // the last level an external sender put on each pin, and which pins have one
        private uint declaredHigh, declaredMask; // the board's declared levels (`external`)
        private List<IGPIO>[] sources;          // per pin * 16 + AF, the peripheral outputs routed to it (Sources)
        private readonly HashSet<int> inverted = new HashSet<int>();   // pin * 16 + AF, from invertedAFPins

        // ---- the tables (generated: tools/tviburi-afcheck csharp; its unit tests check them against the files) --
        // DS12117 Rev 10 Table 9 (model/ds12117-rev10-table9.tsv): per pin PIN:PAGE:AF0,...,AF15.
        // The LQFP144 bonding (model/ds12117-rev10-lqfp144.tsv): per pin PIN:PACKAGEPIN:TABLE8PAGE, "-" not bonded.
        private const string AfTable = "PA0:89:-,TIM2_CH1/TIM2_ETR,TIM5_CH1,TIM8_ETR,TIM15_BKIN,-,-,USART2_CTS/USART2_NSS,UART4_TX,SDMMC2_CMD,SAI2_SD_B,ETH_MII_CRS,-,-,-,EVENTOUT;PA1:89:-,TIM2_CH2,TIM5_CH2,LPTIM3_OUT,TIM15_CH1N,-,-,USART2_RTS/USART2_DE,UART4_RX,QUADSPI_BK1_IO3,SAI2_MCLK_B,ETH_MII_RX_CLK/ETH_RMII_REF_CLK,-,-,LCD_R2,EVENTOUT;PA2:89:-,TIM2_CH3,TIM5_CH3,LPTIM4_OUT,TIM15_CH1,-,-,USART2_TX,SAI2_SCK_B,-,-,ETH_MDIO,MDIOS_MDIO,-,LCD_R1,EVENTOUT;PA3:89:-,TIM2_CH4,TIM5_CH4,LPTIM5_OUT,TIM15_CH2,-,-,USART2_RX,-,LCD_B2,OTG_HS_ULPI_D0,ETH_MII_COL,-,-,LCD_B5,EVENTOUT;PA4:89:D1PWREN,-,TIM5_ETR,-,-,SPI1_NSS/I2S1_WS,SPI3_NSS/I2S3_WS,USART2_CK,SPI6_NSS,-,-,-,OTG_HS_SOF,DCMI_HSYNC,LCD_VSYNC,EVENTOUT;PA5:89:D2PWREN,TIM2_CH1/TIM2_ETR,-,TIM8_CH1N,-,SPI1_SCK/I2S1_CK,-,-,SPI6_SCK,-,OTG_HS_ULPI_CK,-,-,-,LCD_R4,EVENTOUT;PA6:89:-,TIM1_BKIN,TIM3_CH1,TIM8_BKIN,-,SPI1_MISO/I2S1_SDI,-,-,SPI6_MISO,TIM13_CH1,TIM8_BKIN_COMP12,MDIOS_MDC,TIM1_BKIN_COMP12,DCMI_PIXCLK,LCD_G2,EVENTOUT;PA7:89:-,TIM1_CH1N,TIM3_CH2,TIM8_CH1N,-,SPI1_MOSI/I2S1_SDO,-,-,SPI6_MOSI,TIM14_CH1,-,ETH_MII_RX_DV/ETH_RMII_CRS_DV,FMC_SDNWE,-,-,EVENTOUT;PA8:89:MCO1,TIM1_CH1,HRTIM_CHB2,TIM8_BKIN2,I2C3_SCL,-,-,USART1_CK,-,-,OTG_FS_SOF,UART7_RX,TIM8_BKIN2_COMP12,LCD_B3,LCD_R6,EVENTOUT;PA9:89:-,TIM1_CH2,HRTIM_CHC1,LPUART1_TX,I2C3_SMBA,SPI2_SCK/I2S2_CK,-,USART1_TX,-,-,-,ETH_TX_ER,-,DCMI_D0,LCD_R5,EVENTOUT;PA10:89:-,TIM1_CH3,HRTIM_CHC2,LPUART1_RX,-,-,-,USART1_RX,-,-,OTG_FS_ID,MDIOS_MDIO,LCD_B4,DCMI_D1,LCD_B1,EVENTOUT;PA11:89:-,TIM1_CH4,HRTIM_CHD1,LPUART1_CTS,-,SPI2_NSS/I2S2_WS,UART4_RX,USART1_CTS/USART1_NSS,-,FDCAN1_RX,OTG_FS_DM,-,-,-,LCD_R4,EVENTOUT;PA12:90:-,TIM1_ETR,HRTIM_CHD2,LPUART1_RTS/LPUART1_DE,-,SPI2_SCK/I2S2_CK,UART4_TX,USART1_RTS/USART1_DE,SAI2_FS_B,FDCAN1_TX,OTG_FS_DP,-,-,-,LCD_R5,EVENTOUT;PA13:90:JTMS-SWDIO,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PA14:90:JTCK-SWCLK,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PA15:90:JTDI,TIM2_CH1/TIM2_ETR,HRTIM_FLT1,-,CEC,SPI1_NSS/I2S1_WS,SPI3_NSS/I2S3_WS,SPI6_NSS,UART4_RTS/UART4_DE,-,-,UART7_TX,-,-,-,EVENTOUT;PB0:90:-,TIM1_CH2N,TIM3_CH3,TIM8_CH2N,-,-,DFSDM1_CKOUT,-,UART4_CTS,LCD_R3,OTG_HS_ULPI_D1,ETH_MII_RXD2,-,-,LCD_G1,EVENTOUT;PB1:90:-,TIM1_CH3N,TIM3_CH4,TIM8_CH3N,-,-,DFSDM1_DATIN1,-,-,LCD_R6,OTG_HS_ULPI_D2,ETH_MII_RXD3,-,-,LCD_G0,EVENTOUT;PB2:90:RTC_OUT,-,SAI1_D1,-,DFSDM1_CKIN1,-,SAI1_SD_A,SPI3_MOSI/I2S3_SDO,SAI4_SD_A,QUADSPI_CLK,SAI4_D1,ETH_TX_ER,-,-,-,EVENTOUT;PB3:90:JTDO/TRACESWO,TIM2_CH2,HRTIM_FLT4,-,-,SPI1_SCK/I2S1_CK,SPI3_SCK/I2S3_CK,-,SPI6_SCK,SDMMC2_D2,CRS_SYNC,UART7_RX,-,-,-,EVENTOUT;PB4:90:NJTRST,TIM16_BKIN,TIM3_CH1,HRTIM_EEV6,-,SPI1_MISO/I2S1_SDI,SPI3_MISO/I2S3_SDI,SPI2_NSS/I2S2_WS,SPI6_MISO,SDMMC2_D3,-,UART7_TX,-,-,-,EVENTOUT;PB5:90:-,TIM17_BKIN,TIM3_CH2,HRTIM_EEV7,I2C1_SMBA,SPI1_MOSI/I2S1_SDO,I2C4_SMBA,SPI3_MOSI/I2S3_SDO,SPI6_MOSI,FDCAN2_RX,OTG_HS_ULPI_D7,ETH_PPS_OUT,FMC_SDCKE1,DCMI_D10,UART5_RX,EVENTOUT;PB6:90:-,TIM16_CH1N,TIM4_CH1,HRTIM_EEV8,I2C1_SCL,CEC,I2C4_SCL,USART1_TX,LPUART1_TX,FDCAN2_TX,QUADSPI_BK1_NCS,DFSDM1_DATIN5,FMC_SDNE1,DCMI_D5,UART5_TX,EVENTOUT;PB7:90:-,TIM17_CH1N,TIM4_CH2,HRTIM_EEV9,I2C1_SDA,-,I2C4_SDA,USART1_RX,LPUART1_RX,-,-,DFSDM1_CKIN5,FMC_NL,DCMI_VSYNC,-,EVENTOUT;PB8:90:-,TIM16_CH1,TIM4_CH3,DFSDM1_CKIN7,I2C1_SCL,-,I2C4_SCL,SDMMC1_CKIN,UART4_RX,FDCAN1_RX,SDMMC2_D4,ETH_MII_TXD3,SDMMC1_D4,DCMI_D6,LCD_B6,EVENTOUT;PB9:90:-,TIM17_CH1,TIM4_CH4,DFSDM1_DATIN7,I2C1_SDA,SPI2_NSS/I2S2_WS,I2C4_SDA,SDMMC1_CDIR,UART4_TX,FDCAN1_TX,SDMMC2_D5,I2C4_SMBA,SDMMC1_D5,DCMI_D7,LCD_B7,EVENTOUT;PB10:91:-,TIM2_CH3,HRTIM_SCOUT,LPTIM2_IN1,I2C2_SCL,SPI2_SCK/I2S2_CK,DFSDM1_DATIN7,USART3_TX,-,QUADSPI_BK1_NCS,OTG_HS_ULPI_D3,ETH_MII_RX_ER,-,-,LCD_G4,EVENTOUT;PB11:91:-,TIM2_CH4,HRTIM_SCIN,LPTIM2_ETR,I2C2_SDA,-,DFSDM1_CKIN7,USART3_RX,-,-,OTG_HS_ULPI_D4,ETH_MII_TX_EN/ETH_RMII_TX_EN,-,-,LCD_G5,EVENTOUT;PB12:91:-,TIM1_BKIN,-,-,I2C2_SMBA,SPI2_NSS/I2S2_WS,DFSDM1_DATIN1,USART3_CK,-,FDCAN2_RX,OTG_HS_ULPI_D5,ETH_MII_TXD0/ETH_RMII_TXD0,OTG_HS_ID,TIM1_BKIN_COMP12,UART5_RX,EVENTOUT;PB13:91:-,TIM1_CH1N,-,LPTIM2_OUT,-,SPI2_SCK/I2S2_CK,DFSDM1_CKIN1,USART3_CTS/USART3_NSS,-,FDCAN2_TX,OTG_HS_ULPI_D6,ETH_MII_TXD1/ETH_RMII_TXD1,-,-,UART5_TX,EVENTOUT;PB14:91:-,TIM1_CH2N,TIM12_CH1,TIM8_CH2N,USART1_TX,SPI2_MISO/I2S2_SDI,DFSDM1_DATIN2,USART3_RTS/USART3_DE,UART4_RTS/UART4_DE,SDMMC2_D0,-,-,OTG_HS_DM,-,-,EVENTOUT;PB15:91:RTC_REFIN,TIM1_CH3N,TIM12_CH2,TIM8_CH3N,USART1_RX,SPI2_MOSI/I2S2_SDO,DFSDM1_CKIN2,-,UART4_CTS,SDMMC2_D1,-,-,OTG_HS_DP,-,-,EVENTOUT;PC0:91:-,-,-,DFSDM1_CKIN0,-,-,DFSDM1_DATIN4,-,SAI2_FS_B,-,OTG_HS_ULPI_STP,-,FMC_SDNWE,-,LCD_R5,EVENTOUT;PC1:91:TRACED0,-,SAI1_D1,DFSDM1_DATIN0,DFSDM1_CKIN4,SPI2_MOSI/I2S2_SDO,SAI1_SD_A,-,SAI4_SD_A,SDMMC2_CK,SAI4_D1,ETH_MDC,MDIOS_MDC,-,-,EVENTOUT;PC2:91:C1DSLEEP,-,-,DFSDM1_CKIN1,-,SPI2_MISO/I2S2_SDI,DFSDM1_CKOUT,-,-,-,OTG_HS_ULPI_DIR,ETH_MII_TXD2,FMC_SDNE0,-,-,EVENTOUT;PC3:91:C1SLEEP,-,-,DFSDM1_DATIN1,-,SPI2_MOSI/I2S2_SDO,-,-,-,-,OTG_HS_ULPI_NXT,ETH_MII_TX_CLK,FMC_SDCKE0,-,-,EVENTOUT;PC4:91:C2DSLEEP,-,-,DFSDM1_CKIN2,-,I2S1_MCK,-,-,-,SPDIFRX1_IN3,-,ETH_MII_RXD0/ETH_RMII_RXD0,FMC_SDNE0,-,-,EVENTOUT;PC5:91:C2SLEEP,-,SAI1_D3,DFSDM1_DATIN2,-,-,-,-,-,SPDIFRX1_IN4,SAI4_D3,ETH_MII_RXD1/ETH_RMII_RXD1,FMC_SDCKE0,COMP1_OUT,-,EVENTOUT;PC6:92:-,HRTIM_CHA1,TIM3_CH1,TIM8_CH1,DFSDM1_CKIN3,I2S2_MCK,-,USART6_TX,SDMMC1_D0DIR,FMC_NWAIT,SDMMC2_D6,-,SDMMC1_D6,DCMI_D0,LCD_HSYNC,EVENTOUT;PC7:92:TRGIO,HRTIM_CHA2,TIM3_CH2,TIM8_CH2,DFSDM1_DATIN3,-,I2S3_MCK,USART6_RX,SDMMC1_D123DIR,FMC_NE1,SDMMC2_D7,SWPMI_TX,SDMMC1_D7,DCMI_D1,LCD_G6,EVENTOUT;PC8:92:TRACED1,HRTIM_CHB1,TIM3_CH3,TIM8_CH3,-,-,-,USART6_CK,UART5_RTS/UART5_DE,FMC_NE2/FMC_NCE,-,SWPMI_RX,SDMMC1_D0,DCMI_D2,-,EVENTOUT;PC9:92:MCO2,-,TIM3_CH4,TIM8_CH4,I2C3_SDA,I2S_CKIN,-,-,UART5_CTS,QUADSPI_BK1_IO0,LCD_G3,SWPMI_SUSPEND,SDMMC1_D1,DCMI_D3,LCD_B2,EVENTOUT;PC10:92:-,-,HRTIM_EEV1,DFSDM1_CKIN5,-,-,SPI3_SCK/I2S3_CK,USART3_TX,UART4_TX,QUADSPI_BK1_IO1,-,-,SDMMC1_D2,DCMI_D8,LCD_R2,EVENTOUT;PC11:92:-,-,HRTIM_FLT2,DFSDM1_DATIN5,-,-,SPI3_MISO/I2S3_SDI,USART3_RX,UART4_RX,QUADSPI_BK2_NCS,-,-,SDMMC1_D3,DCMI_D4,-,EVENTOUT;PC12:92:TRACED3,-,HRTIM_EEV2,-,-,-,SPI3_MOSI/I2S3_SDO,USART3_CK,UART5_TX,-,-,-,SDMMC1_CK,DCMI_D9,-,EVENTOUT;PC13:92:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PC14:92:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PC15:92:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PD0:92:-,-,-,DFSDM1_CKIN6,-,-,SAI3_SCK_A,-,UART4_RX,FDCAN1_RX,-,-,FMC_D2/FMC_DA2,-,-,EVENTOUT;PD1:92:-,-,-,DFSDM1_DATIN6,-,-,SAI3_SD_A,-,UART4_TX,FDCAN1_TX,-,-,FMC_D3/FMC_DA3,-,-,EVENTOUT;PD2:92:TRACED2,-,TIM3_ETR,-,-,-,-,-,UART5_RX,-,-,-,SDMMC1_CMD,DCMI_D11,-,EVENTOUT;PD3:92:-,-,-,DFSDM1_CKOUT,-,SPI2_SCK/I2S2_CK,-,USART2_CTS/USART2_NSS,-,-,-,-,FMC_CLK,DCMI_D5,LCD_G7,EVENTOUT;PD4:92:-,-,HRTIM_FLT3,-,-,-,SAI3_FS_A,USART2_RTS/USART2_DE,-,-,-,-,FMC_NOE,-,-,EVENTOUT;PD5:93:-,-,HRTIM_EEV3,-,-,-,-,USART2_TX,-,-,-,-,FMC_NWE,-,-,EVENTOUT;PD6:93:-,-,SAI1_D1,DFSDM1_CKIN4,DFSDM1_DATIN1,SPI3_MOSI/I2S3_SDO,SAI1_SD_A,USART2_RX,SAI4_SD_A,-,SAI4_D1,SDMMC2_CK,FMC_NWAIT,DCMI_D10,LCD_B2,EVENTOUT;PD7:93:-,-,-,DFSDM1_DATIN4,-,SPI1_MOSI/I2S1_SDO,DFSDM1_CKIN1,USART2_CK,-,SPDIFRX1_IN1,-,SDMMC2_CMD,FMC_NE1,-,-,EVENTOUT;PD8:93:-,-,-,DFSDM1_CKIN3,-,-,SAI3_SCK_B,USART3_TX,-,SPDIFRX1_IN2,-,-,FMC_D13/FMC_DA13,-,-,EVENTOUT;PD9:93:-,-,-,DFSDM1_DATIN3,-,-,SAI3_SD_B,USART3_RX,-,-,-,-,FMC_D14/FMC_DA14,-,-,EVENTOUT;PD10:93:-,-,-,DFSDM1_CKOUT,-,-,SAI3_FS_B,USART3_CK,-,-,-,-,FMC_D15/FMC_DA15,-,LCD_B3,EVENTOUT;PD11:93:-,-,-,LPTIM2_IN2,I2C4_SMBA,-,-,USART3_CTS/USART3_NSS,-,QUADSPI_BK1_IO0,SAI2_SD_A,-,FMC_A16,-,-,EVENTOUT;PD12:93:-,LPTIM1_IN1,TIM4_CH1,LPTIM2_IN1,I2C4_SCL,-,-,USART3_RTS/USART3_DE,-,QUADSPI_BK1_IO1,SAI2_FS_A,-,FMC_A17,-,-,EVENTOUT;PD13:93:-,LPTIM1_OUT,TIM4_CH2,-,I2C4_SDA,-,-,-,-,QUADSPI_BK1_IO3,SAI2_SCK_A,-,FMC_A18,-,-,EVENTOUT;PD14:93:-,-,TIM4_CH3,-,-,-,SAI3_MCLK_B,-,UART8_CTS,-,-,-,FMC_D0/FMC_DA0,-,-,EVENTOUT;PD15:93:-,-,TIM4_CH4,-,-,-,SAI3_MCLK_A,-,UART8_RTS/UART8_DE,-,-,-,FMC_D1/FMC_DA1,-,-,EVENTOUT;PE0:94:-,LPTIM1_ETR,TIM4_ETR,HRTIM_SCIN,LPTIM2_ETR,-,-,-,UART8_RX,-,SAI2_MCLK_A,-,FMC_NBL0,DCMI_D2,-,EVENTOUT;PE1:94:-,LPTIM1_IN2,-,HRTIM_SCOUT,-,-,-,-,UART8_TX,-,-,-,FMC_NBL1,DCMI_D3,-,EVENTOUT;PE2:94:TRACECLK,-,SAI1_CK1,-,-,SPI4_SCK,SAI1_MCLK_A,-,SAI4_MCLK_A,QUADSPI_BK1_IO2,SAI4_CK1,ETH_MII_TXD3,FMC_A23,-,-,EVENTOUT;PE3:94:TRACED0,-,-,-,TIM15_BKIN,-,SAI1_SD_B,-,SAI4_SD_B,-,-,-,FMC_A19,-,-,EVENTOUT;PE4:94:TRACED1,-,SAI1_D2,DFSDM1_DATIN3,TIM15_CH1N,SPI4_NSS,SAI1_FS_A,-,SAI4_FS_A,-,SAI4_D2,-,FMC_A20,DCMI_D4,LCD_B0,EVENTOUT;PE5:94:TRACED2,-,SAI1_CK2,DFSDM1_CKIN3,TIM15_CH1,SPI4_MISO,SAI1_SCK_A,-,SAI4_SCK_A,-,SAI4_CK2,-,FMC_A21,DCMI_D6,LCD_G0,EVENTOUT;PE6:94:TRACED3,TIM1_BKIN2,SAI1_D1,-,TIM15_CH2,SPI4_MOSI,SAI1_SD_A,-,SAI4_SD_A,SAI4_D1,SAI2_MCLK_B,TIM1_BKIN2_COMP12,FMC_A22,DCMI_D7,LCD_G1,EVENTOUT;PE7:94:-,TIM1_ETR,-,DFSDM1_DATIN2,-,-,-,UART7_RX,-,-,QUADSPI_BK2_IO0,-,FMC_D4/FMC_DA4,-,-,EVENTOUT;PE8:94:-,TIM1_CH1N,-,DFSDM1_CKIN2,-,-,-,UART7_TX,-,-,QUADSPI_BK2_IO1,-,FMC_D5/FMC_DA5,COMP2_OUT,-,EVENTOUT;PE9:94:-,TIM1_CH1,-,DFSDM1_CKOUT,-,-,-,UART7_RTS/UART7_DE,-,-,QUADSPI_BK2_IO2,-,FMC_D6/FMC_DA6,-,-,EVENTOUT;PE10:94:-,TIM1_CH2N,-,DFSDM1_DATIN4,-,-,-,UART7_CTS,-,-,QUADSPI_BK2_IO3,-,FMC_D7/FMC_DA7,-,-,EVENTOUT;PE11:94:-,TIM1_CH2,-,DFSDM1_CKIN4,-,SPI4_NSS,-,-,-,-,SAI2_SD_B,-,FMC_D8/FMC_DA8,-,LCD_G3,EVENTOUT;PE12:94:-,TIM1_CH3N,-,DFSDM1_DATIN5,-,SPI4_SCK,-,-,-,-,SAI2_SCK_B,-,FMC_D9/FMC_DA9,COMP1_OUT,LCD_B4,EVENTOUT;PE13:94:-,TIM1_CH3,-,DFSDM1_CKIN5,-,SPI4_MISO,-,-,-,-,SAI2_FS_B,-,FMC_D10/FMC_DA10,COMP2_OUT,LCD_DE,EVENTOUT;PE14:94:-,TIM1_CH4,-,-,-,SPI4_MOSI,-,-,-,-,SAI2_MCLK_B,-,FMC_D11/FMC_DA11,-,LCD_CLK,EVENTOUT;PE15:94:-,TIM1_BKIN,-,-,-,-,-,-,-,-,-,-,FMC_D12/FMC_DA12,TIM1_BKIN_COMP12/COMP_TIM1_BKIN,LCD_R7,EVENTOUT;PF0:95:-,-,-,-,I2C2_SDA,-,-,-,-,-,-,-,FMC_A0,-,-,EVENTOUT;PF1:95:-,-,-,-,I2C2_SCL,-,-,-,-,-,-,-,FMC_A1,-,-,EVENTOUT;PF2:95:-,-,-,-,I2C2_SMBA,-,-,-,-,-,-,-,FMC_A2,-,-,EVENTOUT;PF3:95:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A3,-,-,EVENTOUT;PF4:95:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A4,-,-,EVENTOUT;PF5:95:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A5,-,-,EVENTOUT;PF6:95:-,TIM16_CH1,-,-,-,SPI5_NSS,SAI1_SD_B,UART7_RX,SAI4_SD_B,QUADSPI_BK1_IO3,-,-,-,-,-,EVENTOUT;PF7:95:-,TIM17_CH1,-,-,-,SPI5_SCK,SAI1_MCLK_B,UART7_TX,SAI4_MCLK_B,QUADSPI_BK1_IO2,-,-,-,-,-,EVENTOUT;PF8:95:-,TIM16_CH1N,-,-,-,SPI5_MISO,SAI1_SCK_B,UART7_RTS/UART7_DE,SAI4_SCK_B,TIM13_CH1,QUADSPI_BK1_IO0,-,-,-,-,EVENTOUT;PF9:95:-,TIM17_CH1N,-,-,-,SPI5_MOSI,SAI1_FS_B,UART7_CTS,SAI4_FS_B,TIM14_CH1,QUADSPI_BK1_IO1,-,-,-,-,EVENTOUT;PF10:95:-,TIM16_BKIN,SAI1_D3,-,-,-,-,-,-,QUADSPI_CLK,SAI4_D3,-,-,DCMI_D11,LCD_DE,EVENTOUT;PF11:95:-,-,-,-,-,SPI5_MOSI,-,-,-,-,SAI2_SD_B,-,FMC_SDNRAS,DCMI_D12,-,EVENTOUT;PF12:95:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A6,-,-,EVENTOUT;PF13:95:-,-,-,DFSDM1_DATIN6,I2C4_SMBA,-,-,-,-,-,-,-,FMC_A7,-,-,EVENTOUT;PF14:95:-,-,-,DFSDM1_CKIN6,I2C4_SCL,-,-,-,-,-,-,-,FMC_A8,-,-,EVENTOUT;PF15:95:-,-,-,-,I2C4_SDA,-,-,-,-,-,-,-,FMC_A9,-,-,EVENTOUT;PG0:96:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A10,-,-,EVENTOUT;PG1:96:-,-,-,-,-,-,-,-,-,-,-,-,FMC_A11,-,-,EVENTOUT;PG2:96:-,-,-,TIM8_BKIN,-,-,-,-,-,-,-,TIM8_BKIN_COMP12,FMC_A12,-,-,EVENTOUT;PG3:96:-,-,-,TIM8_BKIN2,-,-,-,-,-,-,-,TIM8_BKIN2_COMP12,FMC_A13,-,-,EVENTOUT;PG4:96:-,TIM1_BKIN2,-,-,-,-,-,-,-,-,-,TIM1_BKIN2_COMP12,FMC_A14/FMC_BA0,-,-,EVENTOUT;PG5:96:-,TIM1_ETR,-,-,-,-,-,-,-,-,-,-,FMC_A15/FMC_BA1,-,-,EVENTOUT;PG6:96:-,TIM17_BKIN,HRTIM_CHE1,-,-,-,-,-,-,-,QUADSPI_BK1_NCS,-,FMC_NE3,DCMI_D12,LCD_R7,EVENTOUT;PG7:96:-,-,HRTIM_CHE2,-,-,-,SAI1_MCLK_A,USART6_CK,-,-,-,-,FMC_INT,DCMI_D13,LCD_CLK,EVENTOUT;PG8:96:-,-,-,TIM8_ETR,-,SPI6_NSS,-,USART6_RTS/USART6_DE,SPDIFRX1_IN3,-,-,ETH_PPS_OUT,FMC_SDCLK,-,LCD_G7,EVENTOUT;PG9:96:-,-,-,-,-,SPI1_MISO/I2S1_SDI,-,USART6_RX,SPDIFRX1_IN4,QUADSPI_BK2_IO2,SAI2_FS_B,-,FMC_NE2/FMC_NCE,DCMI_VSYNC,-,EVENTOUT;PG10:96:-,-,HRTIM_FLT5,-,-,SPI1_NSS/I2S1_WS,-,-,-,LCD_G3,SAI2_SD_B,-,FMC_NE3,DCMI_D2,LCD_B2,EVENTOUT;PG11:96:-,LPTIM1_IN2,HRTIM_EEV4,-,-,SPI1_SCK/I2S1_CK,-,-,SPDIFRX1_IN1,-,SDMMC2_D2,ETH_MII_TX_EN/ETH_RMII_TX_EN,-,DCMI_D3,LCD_B3,EVENTOUT;PG12:97:-,LPTIM1_IN1,HRTIM_EEV5,-,-,SPI6_MISO,-,USART6_RTS/USART6_DE,SPDIFRX1_IN2,LCD_B4,-,ETH_MII_TXD1/ETH_RMII_TXD1,FMC_NE4,-,LCD_B1,EVENTOUT;PG13:97:TRACED0,LPTIM1_OUT,HRTIM_EEV10,-,-,SPI6_SCK,-,USART6_CTS/USART6_NSS,-,-,-,ETH_MII_TXD0/ETH_RMII_TXD0,FMC_A24,-,LCD_R0,EVENTOUT;PG14:97:TRACED1,LPTIM1_ETR,-,-,-,SPI6_MOSI,-,USART6_TX,-,QUADSPI_BK2_IO3,-,ETH_MII_TXD1/ETH_RMII_TXD1,FMC_A25,-,LCD_B0,EVENTOUT;PG15:97:-,-,-,-,-,-,-,USART6_CTS/USART6_NSS,-,-,-,-,FMC_SDNCAS,DCMI_D13,-,EVENTOUT;PH0:97:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PH1:97:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PH2:97:-,LPTIM1_IN2,-,-,-,-,-,-,-,QUADSPI_BK2_IO0,SAI2_SCK_B,ETH_MII_CRS,FMC_SDCKE0,-,LCD_R0,EVENTOUT;PH3:97:-,-,-,-,-,-,-,-,-,QUADSPI_BK2_IO1,SAI2_MCLK_B,ETH_MII_COL,FMC_SDNE0,-,LCD_R1,EVENTOUT;PH4:97:-,-,-,-,I2C2_SCL,-,-,-,-,LCD_G5,OTG_HS_ULPI_NXT,-,-,-,LCD_G4,EVENTOUT;PH5:97:-,-,-,-,I2C2_SDA,SPI5_NSS,-,-,-,-,-,-,FMC_SDNWE,-,-,EVENTOUT;PH6:97:-,-,TIM12_CH1,-,I2C2_SMBA,SPI5_SCK,-,-,-,-,-,ETH_MII_RXD2,FMC_SDNE1,DCMI_D8,-,EVENTOUT;PH7:97:-,-,-,-,I2C3_SCL,SPI5_MISO,-,-,-,-,-,ETH_MII_RXD3,FMC_SDCKE1,DCMI_D9,-,EVENTOUT;PH8:97:-,-,TIM5_ETR,-,I2C3_SDA,-,-,-,-,-,-,-,FMC_D16,DCMI_HSYNC,LCD_R2,EVENTOUT;PH9:97:-,-,TIM12_CH2,-,I2C3_SMBA,-,-,-,-,-,-,-,FMC_D17,DCMI_D0,LCD_R3,EVENTOUT;PH10:98:-,-,TIM5_CH1,-,I2C4_SMBA,-,-,-,-,-,-,-,FMC_D18,DCMI_D1,LCD_R4,EVENTOUT;PH11:98:-,-,TIM5_CH2,-,I2C4_SCL,-,-,-,-,-,-,-,FMC_D19,DCMI_D2,LCD_R5,EVENTOUT;PH12:98:-,-,TIM5_CH3,-,I2C4_SDA,-,-,-,-,-,-,-,FMC_D20,DCMI_D3,LCD_R6,EVENTOUT;PH13:98:-,-,-,TIM8_CH1N,-,-,-,-,UART4_TX,FDCAN1_TX,-,-,FMC_D21,-,LCD_G2,EVENTOUT;PH14:98:-,-,-,TIM8_CH2N,-,-,-,-,UART4_RX,FDCAN1_RX,-,-,FMC_D22,DCMI_D4,LCD_G3,EVENTOUT;PH15:98:-,-,-,TIM8_CH3N,-,-,-,-,-,-,-,-,FMC_D23,DCMI_D11,LCD_G4,EVENTOUT;PI0:98:-,-,TIM5_CH4,-,-,SPI2_NSS/I2S2_WS,-,-,-,-,-,-,FMC_D24,DCMI_D13,LCD_G5,EVENTOUT;PI1:98:-,-,-,TIM8_BKIN2,-,SPI2_SCK/I2S2_CK,-,-,-,-,-,TIM8_BKIN2_COMP12,FMC_D25,DCMI_D8,LCD_G6,EVENTOUT;PI2:98:-,-,-,TIM8_CH4,-,SPI2_MISO/I2S2_SDI,-,-,-,-,-,-,FMC_D26,DCMI_D9,LCD_G7,EVENTOUT;PI3:98:-,-,-,TIM8_ETR,-,SPI2_MOSI/I2S2_SDO,-,-,-,-,-,-,FMC_D27,DCMI_D10,-,EVENTOUT;PI4:98:-,-,-,TIM8_BKIN,-,-,-,-,-,-,SAI2_MCLK_A,TIM8_BKIN_COMP12,FMC_NBL2,DCMI_D5,LCD_B4,EVENTOUT;PI5:98:-,-,-,TIM8_CH1,-,-,-,-,-,-,SAI2_SCK_A,-,FMC_NBL3,DCMI_VSYNC,LCD_B5,EVENTOUT;PI6:98:-,-,-,TIM8_CH2,-,-,-,-,-,-,SAI2_SD_A,-,FMC_D28,DCMI_D6,LCD_B6,EVENTOUT;PI7:98:-,-,-,TIM8_CH3,-,-,-,-,-,-,SAI2_FS_A,-,FMC_D29,DCMI_D7,LCD_B7,EVENTOUT;PI8:98:-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,EVENTOUT;PI9:99:-,-,-,-,-,-,-,-,UART4_RX,FDCAN1_RX,-,-,FMC_D30,-,LCD_VSYNC,EVENTOUT;PI10:99:-,-,-,-,-,-,-,-,-,-,-,ETH_MII_RX_ER,FMC_D31,-,LCD_HSYNC,EVENTOUT;PI11:99:-,-,-,-,-,-,-,-,-,LCD_G6,OTG_HS_ULPI_DIR,-,-,-,-,EVENTOUT;PI12:99:-,-,-,-,-,-,-,-,-,-,-,ETH_TX_ER,-,-,LCD_HSYNC,EVENTOUT;PI13:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_VSYNC,EVENTOUT;PI14:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_CLK,EVENTOUT;PI15:99:-,-,-,-,-,-,-,-,-,LCD_G2,-,-,-,-,LCD_R0,EVENTOUT;PJ0:99:-,-,-,-,-,-,-,-,-,LCD_R7,-,-,-,-,LCD_R1,EVENTOUT;PJ1:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_R2,EVENTOUT;PJ2:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_R3,EVENTOUT;PJ3:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_R4,EVENTOUT;PJ4:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_R5,EVENTOUT;PJ5:99:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_R6,EVENTOUT;PJ6:99:-,-,-,TIM8_CH2,-,-,-,-,-,-,-,-,-,-,LCD_R7,EVENTOUT;PJ7:99:TRGIN,-,-,TIM8_CH2N,-,-,-,-,-,-,-,-,-,-,LCD_G0,EVENTOUT;PJ8:99:-,TIM1_CH3N,-,TIM8_CH1,-,-,-,-,UART8_TX,-,-,-,-,-,LCD_G1,EVENTOUT;PJ9:100:-,TIM1_CH3,-,TIM8_CH1N,-,-,-,-,UART8_RX,-,-,-,-,-,LCD_G2,EVENTOUT;PJ10:100:-,TIM1_CH2N,-,TIM8_CH2,-,SPI5_MOSI,-,-,-,-,-,-,-,-,LCD_G3,EVENTOUT;PJ11:100:-,TIM1_CH2,-,TIM8_CH2N,-,SPI5_MISO,-,-,-,-,-,-,-,-,LCD_G4,EVENTOUT;PJ12:100:TRGOUT,-,-,-,-,-,-,-,-,LCD_G3,-,-,-,-,LCD_B0,EVENTOUT;PJ13:100:-,-,-,-,-,-,-,-,-,LCD_B4,-,-,-,-,LCD_B1,EVENTOUT;PJ14:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B2,EVENTOUT;PJ15:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B3,EVENTOUT;PK0:100:-,TIM1_CH1N,-,TIM8_CH3,-,SPI5_SCK,-,-,-,-,-,-,-,-,LCD_G5,EVENTOUT;PK1:100:-,TIM1_CH1,-,TIM8_CH3N,-,SPI5_NSS,-,-,-,-,-,-,-,-,LCD_G6,EVENTOUT;PK2:100:-,TIM1_BKIN,-,TIM8_BKIN,-,-,-,-,-,-,TIM8_BKIN_COMP12,TIM1_BKIN_COMP12,-,-,LCD_G7,EVENTOUT;PK3:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B4,EVENTOUT;PK4:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B5,EVENTOUT;PK5:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B6,EVENTOUT;PK6:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_B7,EVENTOUT;PK7:100:-,-,-,-,-,-,-,-,-,-,-,-,-,-,LCD_DE,EVENTOUT";
        private const string Lqfp144 = "PA0:34:68;PA1:35:68;PA2:36:68;PA3:37:69;PA4:40:69;PA5:41:69;PA6:42:69;PA7:43:70;PA8:100:80;PA9:101:80;PA10:102:80;PA11:103:80;PA12:104:80;PA13:105:80;PA14:109:81;PA15:110:82;PB0:46:70;PB1:47:70;PB2:48:71;PB3:133:85;PB4:134:85;PB5:135:86;PB6:136:86;PB7:137:86;PB8:139:86;PB9:140:87;PB10:69:73;PB11:70:73;PB12:73:74;PB13:74:75;PB14:75:75;PB15:76:75;PC0:26:67;PC1:27:67;PC2:-:67;PC3:-:67;PC4:44:70;PC5:45:70;PC6:96:79;PC7:97:79;PC8:98:79;PC9:99:79;PC10:111:82;PC11:112:82;PC12:113:82;PC13:7:64;PC14:8:65;PC15:9:65;PD0:114:82;PD1:115:82;PD2:116:83;PD3:117:83;PD4:118:83;PD5:119:83;PD6:122:83;PD7:123:83;PD8:77:75;PD9:78:75;PD10:79:76;PD11:80:76;PD12:81:76;PD13:82:76;PD14:85:76;PD15:86:76;PE0:141:87;PE1:142:87;PE2:1:64;PE3:2:64;PE4:3:64;PE5:4:64;PE6:5:64;PE7:58:72;PE8:59:72;PE9:60:72;PE10:63:72;PE11:64:72;PE12:65:72;PE13:66:72;PE14:67:73;PE15:68:73;PF0:10:65;PF1:11:65;PF2:12:65;PF3:13:65;PF4:14:65;PF5:15:66;PF6:18:66;PF7:19:66;PF8:20:66;PF9:21:66;PF10:22:66;PF11:49:71;PF12:50:71;PF13:53:71;PF14:54:71;PF15:55:71;PG0:56:71;PG1:57:71;PG2:87:78;PG3:88:78;PG4:89:78;PG5:90:78;PG6:91:78;PG7:92:78;PG8:93:78;PG9:124:84;PG10:125:84;PG11:126:84;PG12:127:84;PG13:128:84;PG14:129:85;PG15:132:85;PH0:23:66;PH1:24:66;PH2:-:68;PH3:-:68;PH4:-:69;PH5:-:69;PH6:-:73;PH7:-:74;PH8:-:74;PH9:-:74;PH10:-:74;PH11:-:74;PH12:-:74;PH13:-:81;PH14:-:81;PH15:-:81;PI0:-:81;PI1:-:81;PI2:-:81;PI3:-:81;PI4:-:87;PI5:-:87;PI6:-:87;PI7:-:88;PI8:-:64;PI9:-:65;PI10:-:65;PI11:-:65;PI12:-:65;PI13:-:65;PI14:-:65;PI15:-:71;PJ0:-:71;PJ1:-:71;PJ2:-:71;PJ3:-:71;PJ4:-:71;PJ5:-:73;PJ6:-:76;PJ7:-:76;PJ8:-:77;PJ9:-:77;PJ10:-:77;PJ11:-:77;PJ12:-:83;PJ13:-:83;PJ14:-:83;PJ15:-:83;PK0:-:77;PK1:-:77;PK2:-:77;PK3:-:85;PK4:-:85;PK5:-:85;PK6:-:85;PK7:-:85";

        // ---- the allow-list (RUL-17): every bit of every GPIO register, keyed by RM0433 Rev 9's names (RUL-16) ------
        // From the field table TVI-WO-1301.rm0433.md §2 and its classifications A1-A10 (§6.3), ruled 2026-11-23T17:52Z.
        // A register whose every value the model implements is one row; PUPDR, with a reserved value, is one row a field.
        private sealed class Row
        {
            public Row(string register, long offset, string section, string name, int msb, int lsb, uint[] allowed,
                string cls, string behavesAs, int page)
            {
                Register = register;
                Offset = offset;
                Section = section;
                Name = name;
                Msb = msb;
                Lsb = lsb;
                Allowed = allowed;
                BehavesAs = behavesAs;
                Page = page;
            }

            public uint Of(uint value)
            {
                var width = Msb - Lsb + 1;
                return width == 32 ? value : (value >> Lsb) & ((1u << width) - 1);
            }

            public string Format(uint v)
            {
                var width = Msb - Lsb + 1;
                return width > 1 && width <= 5 ? Convert.ToString(v, 2).PadLeft(width, '0') : width == 1 ? v.ToString() : "0x" + v.ToString("X");
            }

            public string Bits => Msb == Lsb ? "bit " + Lsb : Msb + ":" + Lsb;
            public string Register { get; }
            public long Offset { get; }
            public string Section { get; }
            public string Name { get; }
            public int Msb { get; }
            public int Lsb { get; }
            public uint[] Allowed { get; }
            public string BehavesAs { get; }
            public int Page { get; }
        }

        private static readonly uint[] Any = null;
        private static readonly uint[] Zero = { 0 };
        private static readonly uint[] NotReserved = { 0, 1, 2 };

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, cls, behaves, page);
        }

        private static readonly Row[] Fields =
        {
            R("GPIOx_MODER", 0x00, "§11.4.1", "MODER[15:0]", 31, 0, Any, "modeled by the guard", "stored by the stock port; the guard drives each pin by it (§11.3.9-11.3.13 p537-539): output from ODR, AF from the peripheral routed to the pin's AFSEL (a timer, through the platform's routes; its level taken at the switch), input and analog from nothing (the pin takes a sender's level, the board's declared level or PUPDR's pull, else it is low; an AF pin no Renode model drives holds); 10 is checked against DS12117 Table 9 and the declared package; 11 has nothing analog behind it (TVI-WO-1301 A4)", 541),
            R("GPIOx_OTYPER", 0x04, "§11.4.2", "OT[15:0]", 15, 0, Any, "modeled by the guard", "kept by the guard and served on read (the stock port drops it, a tag); no open-drain electrical behaviour (TVI-WO-1301 A1)", 541),
            R("GPIOx_OTYPER", 0x04, "§11.4.2", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 541),
            R("GPIOx_OSPEEDR", 0x08, "§11.4.3", "OSPEEDR[15:0]", 31, 0, Any, "stock, wrong (the speed sets the output driver's edge rate on silicon (p542, figures in DS12117); the stock port stores it with no effect; unaffected: the twin's pins carry logic levels only, which the speed does not change; the field reads back as written on both sides)", "the stock port: stored, with no effect on timing (TVI-WO-1301 A2); read for every value (11 distinct written) (TVI-WO-1206; p542; STM32_GPIOPort.cs L185-188; STM32_GPIOPort.cs L396-403)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR15", 31, 30, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR14", 29, 28, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR13", 27, 26, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR12", 25, 24, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR11", 23, 22, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR10", 21, 20, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR9", 19, 18, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR8", 17, 16, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR7", 15, 14, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR6", 13, 12, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR5", 11, 10, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR4", 9, 8, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR3", 7, 6, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR2", 5, 4, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR1", 3, 2, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_PUPDR", 0x0C, "§11.4.4", "PUPDR0", 1, 0, NotReserved, "modeled by the guard", "stored by the stock port; on an input or AF pin the port does not drive, its pull sets the pin and IDR, unless a sender or the board's declared level does (p537-538); none in analog mode (p539); 11 is reserved (TVI-WO-1301 A3)", 542),
            R("GPIOx_IDR", 0x10, "§11.4.5", "IDR[15:0]", 15, 0, Any, "read-only", "ignored (read-only); served by the guard by mode (p537-539): output, ODR; AF, the pin's level; analog, 0 (p539); input, a sender's level, else the board's declared level (`external`), else PUPDR's pull, else 0. That 0 is an assumption: silicon gives a floating pin no defined level, so a firmware that needs 0 from a floating pin passes in the twin and may not on silicon (ruled 2026-11-23T23:52Z)", 543),
            R("GPIOx_IDR", 0x10, "§11.4.5", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 543),
            R("GPIOx_ODR", 0x14, "§11.4.6", "ODR[15:0]", 15, 0, Any, "modeled by the guard", "latched by the guard and read back as written (p537); drives a pin only in output mode, at the write or when the pin switches to output (p537-539)", 543),
            R("GPIOx_ODR", 0x14, "§11.4.6", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 543),
            R("GPIOx_BSRR", 0x18, "§11.4.7", "BR[15:0]", 31, 16, Any, "modeled by the guard", "resets ODR bits, except where the BS bit is set too: BS has priority (p544; TVI-WO-1301 A7); ODR drives a pin only in output mode (p537-539)", 544),
            R("GPIOx_BSRR", 0x18, "§11.4.7", "BS[15:0]", 15, 0, Any, "modeled by the guard", "sets ODR bits (the guard's copy); ODR drives a pin only in output mode (p537-539)", 544),
            R("GPIOx_LCKR", 0x1C, "§11.4.8", "Res31_17", 31, 17, Zero, "reserved", "reserved: dropped, reads 0", 545),
            R("GPIOx_LCKR", 0x1C, "§11.4.8", "LCKK", 16, 16, Zero, "stock, wrong (the lock key sequence and the lock are not modeled; the stock port reads back the locked pins, not the LCK bits; TVI-WO-1301 A8)", "no lock: LCKK written 0, which starts no sequence", 545),
            R("GPIOx_LCKR", 0x1C, "§11.4.8", "LCK[15:0]", 15, 0, Zero, "stock, wrong (the lock key sequence and the lock are not modeled; the stock port reads back the locked pins, not the LCK bits; TVI-WO-1301 A8)", "no lock: LCK written 0", 545),
            R("GPIOx_AFRL", 0x20, "§11.4.9", "AFSEL[7:0]", 31, 0, Any, "modeled by the guard", "the stock port's AF selection for pins 0-7; the guard refuses AF mode on a pin whose AF has no function in DS12117 Rev 10 Table 9", 545),
            R("GPIOx_AFRH", 0x24, "§11.4.10", "AFSEL[15:8]", 31, 0, Any, "modeled by the guard", "the stock port's AF selection for pins 8-15; the guard refuses AF mode on a pin whose AF has no function in DS12117 Rev 10 Table 9", 546),
        };
    }
}
