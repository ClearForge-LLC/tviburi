// Tviburi's guard over Renode's STM32_Timer for the STM32H753's advanced-control timers TIM1 and TIM8 (TVI-WO-1302).
// The measurement, every page, and the rulings: docs/work-orders/TVI-WO-1302.measure.md (RM0433 Rev 9, chapter 38).
//
// Insertion point (RUL-07): STM32_Timer is not sealed (measured, TVI-WO-1302.measure.md §2.3), so this guard is a
// subclass that re-implements the three bus access interfaces and INumberedGPIOOutput. The platform's routes
// (`timer1: 0 -> tviburiGpioA#08@01 | ...`) bind to the guard's own four outputs; the stock model's channel lines feed
// only the guard's taps. The stock platform declares both timers, so the board description cancels each and declares
// this guard at its address `as "timer1"` / `as "timer8"`, restating its registration, its constructor arguments and
// interrupts (model/stm32h7-timer-restate.repl), while the GPIO restatement names the guard's variable in every
// connection (variant B, ruled 2026-11-24T06:10Z Q3). Which timer it is comes from its own bus registration, never
// from a name. The consumer's untyped `timer1: frequency:` update binds to the cancelled entry and never reaches the
// guard (measured): the consumer gives the frequency to `tviburiTimer1` (RUL-09 (b)).
//
// What a channel output carries (CH1-CH4; RM0433 Rev 9 Table 337 p1608):
//   OCxREF is the stock model's channel line (PWM mode 1: high from the update event until the compare, p1558, with
//   the stock model's conventions for the period and the first edge, kept by ruling Q1). The guard keeps that line
//   OCxREF (ruling Q2): CCRx is 0 at reset (p1610) in the stock model too, a CCRx of 0 in PWM mode 1 lowers it
//   (p1558), and the stock model sees every CCxE set, so it never clears OCxREF for an output being disabled.
//   MOE = 1, CCxE = 1: OCx = OCxREF xor CCxP (polarity applied here, once: the stock model never applies it).
//   MOE = 1, CCxE = 0: not driven by the timer (Hi-Z).
//   MOE = 0, OSSI = 0, or CCxE = CCxNE = 0: not driven (Hi-Z).
//   MOE = 0, OSSI = 1, CCxE = 1: the idle level OISx (OIS1 p1591, MOE p1614, OSSI p1615: "forced to their idle level
//   after the deadtime"; no dead time is modeled, DTG = 0).
//   A Renode connection has two levels, so "not driven" is driven LOW: a stated assumption, which is silicon's level
//   on every committed motor pin, each with a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j; ruling Q4). A
//   pin whose board leaves it floating, or pulls it up, is not modeled (the twin cannot see fitment).
// The complementary outputs (CHxN) are not this guard's: the stock platform drives a CHxN pin through the GPIO port's
// invertedAFPins, as NOT CHx. CCxNE = 1 and CCxNP = 1 refuse (ruling Q5 (a)).
// The gate is not a refusal: an output silicon holds disabled stays low, and the consumer's observers see that level.
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD         (Records) the guard did not load: every access refuses.
//   TVI-TIM-CONFIG   the platform gives this guard what it cannot model: a bus address that is not TIM1's or TIM8's,
//                    more than one registration, or an initialLimit other than 0xFFFF (16 bits). unmodeled.
//   TVI-TIM-WIDTH    a byte access, or a misaligned half-word (p1588: half-words or words). unmodeled.
//   TVI-TIM-READ     a read of an offset with no register (0x50; past 0x68), or of DMAR. unmodeled.
//   TVI-TIM-DMAR     any write to DMAR (0x4C): a DMA burst access to the register DCR.DBA names (p1616), not modeled.
//   TVI-TIM-LOCK     a write that would change a bit its LOCK level protects (p1615, the fields' own pages):
//                    silicon keeps the old value. predicted-fault. The same value passes (RUL-18, amended).
//   TVI-TIM-FIELD    the allow-list (`Fields`): a field given a value the model does not implement, or a write to an
//                    offset with no register. unmodeled.
// Enforcing and recorded mode (RUL-19) are model/Refusal.cs's contract.

using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32_Timer : STM32_Timer, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, INumberedGPIOOutput, IDisposable
    {
        // The stock entry's constructor arguments, passed through; `mode` (RUL-19) is this guard's.
        public GuardedSTM32_Timer(IMachine machine, ulong frequency, uint initialLimit, string mode = Records.Enforcing)
            : base(machine, frequency, initialLimit)
        {
            this.machine = machine;
            limit = initialLimit;
            for(var i = 0; i < Channels; i++)
            {
                outs[i] = new GPIO();
                base.Connections[i].Connect(new Tap(this, i), 0);
            }
            connections = Enumerable.Range(0, Channels).ToDictionary(i => i, i => (IGPIO)outs[i]);
            records = new Records(machine, this, nameof(GuardedSTM32_Timer), mode);
            if(machine is Machine m)
            {
                m.StateChanged += CollectAtStart;
            }
        }

        // The number of full collections this instance has forced: 1 once the machine has started, never more
        // (tests/1302/timer.sh `gconce`).
        public int Collections { get; private set; }

        // A deliberate, load-time-only side effect (ruled 2026-11-24T10:30Z (A); at the first start, 10:55Z): one full,
        // blocking, compacting collection when the machine first starts, after the overlay's compile and the platform's
        // load and before any guest instruction. It reclaims their garbage, which otherwise stays in gen2 through the
        // consumer's boot and counts in its peak (measured: +22 MB of managed heap after `include` and `mach create`,
        // +0.4 MB after a full GC; ArduPilot's boot +35.6 MiB at the median over main, +11.6 with one full GC on both
        // sides, +27.7 with the collection in the constructor, which runs during the platform's load;
        // TVI-WO-1302.FEEDBACK.md §8). Each instance collects once and then unsubscribes, so TIM1 and TIM8 make two,
        // tens of milliseconds at the start; a pause and resume or a reset does not collect again. It changes no virtual
        // time, no access and no record.
        private void CollectAtStart(IMachine machine, MachineStateChangedEventArgs args)
        {
            if(args.CurrentState != MachineStateChangedEventArgs.State.Started)
            {
                return;
            }
            ((Machine)machine).StateChanged -= CollectAtStart;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            Collections++;
        }

        // The platform's routes bind here (measured: the probe of TVI-WO-1302.measure.md §2.3), not to the stock lines.
        public new IReadOnlyDictionary<int, IGPIO> Connections => connections;
        IReadOnlyDictionary<int, IGPIO> INumberedGPIOOutput.Connections => connections;

        // The copies first, so the stock lines' own reset edges are judged by silicon's reset state (no pulse); then
        // CCRx = 0, silicon's reset (p1610), into the stock model, whose reset is its initialLimit (0xFFFF), so that it
        // never raises an unwritten channel in PWM mode 1 (CCRx above ARR would hold OCxREF at 1, p1558).
        public override void Reset()
        {
            cr2 = ccmr1 = ccmr2 = ccer = bdtr = ccmr3 = 0;
            bdtrWritten = false;
            base.Reset();
            for(var i = 0; i < Channels; i++)
            {
                base.WriteDoubleWord(Ccr1Offset + 4 * i, 0);
            }
            DriveAll();
        }

        public void Dispose()
        {
            records.End();
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
            if(Unloaded(offset, 0, width) || Narrow(offset, 0, width, "read"))
            {
                return records.Recorded ? Served(b) >> (int)(8 * (offset & 3)) : 0;
            }
            if(b == DmarOffset || !Fields.Any(f => f.Offset == b))
            {
                records.Refuse("TVI-TIM-READ", Unmodeled, Register(b), b == DmarOffset ? "DMAR" : "no register at offset 0x" + offset.ToString("X"),
                    b == DmarOffset ? "read of DMAR: a DMA burst access to the register DCR.DBA names, which the twin does not model"
                        : "read of a TIM1/TIM8 offset where RM0433 defines no register: the twin cannot say what silicon returns",
                    b == DmarOffset ? "RM0433 Rev 9 §38.4.22 p1616" : "RM0433 Rev 9 §38.4.32 Table 338 p1628",
                    State(), offset, 0, width);
                if(!records.Recorded)
                {
                    return 0;
                }
            }
            return Served(b) >> (int)(8 * (offset & 3));
        }

        // A half-word write is merged into the word it is part of, as the register holds it, and judged as that word.
        private void Write(long offset, uint value, int width)
        {
            var b = offset & ~3L;
            var mask = width == 32 ? 0xFFFFFFFFu : width == 16 ? 0xFFFFu : 0xFFu;
            var shift = (int)(8 * (offset & 3));
            var word = width == 32 ? value : (Served(b) & ~(mask << shift)) | ((value & mask) << shift);
            if(Unloaded(offset, value, width) || Narrow(offset, value, width, "write"))
            {
                if(!records.Recorded)
                {
                    return;
                }
            }
            else if(records.Recorded)
            {
                // Recorded mode (RUL-19) measures every path: each rule is evaluated and records what it finds.
                RefusedDmar(offset, b, word, width);
                RefusedLock(offset, b, word, width);
                RefusedField(offset, b, word, width);
            }
            else if(RefusedDmar(offset, b, word, width) || RefusedLock(offset, b, word, width) || RefusedField(offset, b, word, width))
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
                records.Refuse("TVI-TIM-CONFIG", Unmodeled, Register(offset & ~3L), "none",
                    "the platform gives this timer guard what it cannot model: " + c + "; every access refuses",
                    "RM0433 Rev 9 §38.4.32-38.4.33 Table 338-339 p1628-1630 (TIM1 at 0x40010000, TIM8 at 0x40010400, 16-bit counters); Table 8 memory map",
                    "none", offset, value, width);
                return true;
            }
            return false;
        }

        private bool Narrow(long offset, ulong value, int width, string what)
        {
            if(width != 8 && (offset & (width / 8 - 1)) == 0)
            {
                return false;
            }
            records.Refuse("TVI-TIM-WIDTH", Unmodeled, Register(offset & ~3L), (width == 8 ? "" : "misaligned ") + width + "-bit " + what,
                "a " + (width == 8 ? "" : "misaligned ") + width + "-bit TIM1/TIM8 " + what + " at offset 0x" + offset.ToString("X")
                    + ": the registers are accessed by half-words or words, and the twin cannot say what silicon does with this one",
                "RM0433 Rev 9 §38.4 p1588", State(), offset, value, width);
            return true;
        }

        // What a read returns: the registers the guard keeps, from its copies (CR2, CCMR1-3, CCER, CCR1-4, BDTR, and
        // the registers the stock model lacks, which hold their reset values: the allow-list admits no other), the rest
        // from the stock model.
        private uint Served(long b)
        {
            switch(b)
            {
            case Cr2Offset: return cr2;
            case Ccmr1Offset: return ccmr1;
            case Ccmr2Offset: return ccmr2;
            case CcerOffset: return ccer;
            case BdtrOffset: return bdtr;
            case Ccmr3Offset: return ccmr3;
            case Af1Offset: case Af2Offset: return 1;   // BKINE, BK2INE reset 1 (p1619, p1621)
            }
            return b <= BdtrOffset ? base.ReadDoubleWord(b) : 0;
        }

        private void Pass(long b, uint word)
        {
            switch(b)
            {
            case Cr2Offset: cr2 = word; break;
            case Ccmr1Offset: ccmr1 = word; break;
            case Ccmr2Offset: ccmr2 = word; break;
            case CcerOffset: ccer = word; break;
            case BdtrOffset:
                if(bdtrWritten)
                {
                    word = (word & ~LockBits) | (bdtr & LockBits);   // frozen (p1615): kept even when recorded mode passes a change
                }
                bdtrWritten = true;   // the first write after reset freezes LOCK (p1615)
                bdtr = word;
                break;
            case Ccmr3Offset: ccmr3 = word; break;
            }
            if(b <= BdtrOffset)
            {
                // The registers past BDTR are the guard's alone (the stock model has none). The stock model sees every
                // CCxE set, so its line stays OCxREF whatever CCxE says (silicon computes OCxREF regardless, p1607; the
                // stock model would clear its line at CCxE = 0, STM32_Timer.cs L695-704): the guard alone gates.
                base.WriteDoubleWord(b, b == CcerOffset ? word | AllOutputs : word);
            }
            DriveAll();
        }

        // ---- the outputs ---------------------------------------------------------------------------------------

        // In PWM mode 1, a CCRx of 0 holds OCxREF at 0 (p1558); the stock model leaves its line where it was when CCRx
        // becomes 0 (STM32_Timer.cs L398-402), so the guard lowers it, and the stock model then never raises it.
        private void DriveAll()
        {
            for(var i = 0; i < Channels; i++)
            {
                var ccmr = i < 2 ? ccmr1 : ccmr2;
                if((ccmr >> (8 * (i % 2) + 4) & 7) == PwmMode1 && base.ReadDoubleWord(Ccr1Offset + 4 * i) == 0 && base.Connections[i].IsSet)
                {
                    base.Connections[i].Unset();
                }
                outs[i]?.Set(Level(i));
            }
        }

        // Silicon's level at channel i's output (Table 337 p1608), "not driven" as low (ruling Q4). OCxREF is the stock
        // model's line (its period and first edge, ruling Q1). The idle level with MOE = 0 and OSSI = 1 is OISx (OIS1
        // p1591, MOE p1614, OSSI p1615: Table 337 p1608 states it only after a break).
        private bool Level(int i)
        {
            var e = (ccer >> (4 * i) & 1) == 1;
            var p = (ccer >> (4 * i + 1) & 1) == 1;
            if((bdtr & MoeBit) != 0)
            {
                return e && (base.Connections[i].IsSet ^ p);
            }
            return e && (bdtr & OssiBit) != 0 && (cr2 >> (8 + 2 * i) & 1) == 1;
        }

        private sealed class Tap : IGPIOReceiver
        {
            public Tap(GuardedSTM32_Timer parent, int channel)
            {
                this.parent = parent;
                this.channel = channel;
            }

            public void OnGPIO(int number, bool value)
            {
                parent.outs[channel]?.Set(parent.Level(channel));
            }

            public void Reset()
            {
            }

            private readonly GuardedSTM32_Timer parent;
            private readonly int channel;
        }

        // ---- rules ---------------------------------------------------------------------------------------------

        private bool RefusedDmar(long offset, long b, uint word, int width)
        {
            if(b != DmarOffset)
            {
                return false;
            }
            records.Refuse("TVI-TIM-DMAR", Unmodeled, "TIMx_DMAR", "DMAB[31:0] = 0x" + word.ToString("X"),
                "write to DMAR: a DMA burst access to the register DCR.DBA names, which the twin does not model (DMA is the next 13NN)",
                "RM0433 Rev 9 §38.4.22 p1616", State(), offset, word, width);
            return true;
        }

        // The bits LOCK level n protects, by register (p1615 LOCK; each field's own page): level 1 DTG, BKE, BKP, AOE
        // (BDTR) and OIS1-OIS4, OIS1N-OIS3N (CR2); level 2 also OSSR, OSSI (BDTR) and CCxP, CCxNP (CCER, every channel
        // an output: CCxS = 00 is the allow-list's); level 3 also OCxM and OCxPE (CCMR1, CCMR2), and the LOCK bits
        // themselves once frozen. BKF, BK2F, BK2E and BK2P are left to the allow-list (refused unless 0): their own
        // pages lock them at level 1 and p1615's list does not, so the manual does not say which (measure.md §5). CCMR3
        // is not locked: p1615's level 3 names "TIMx_CCMRx" and CCMR3's own page (p1617) has no LOCK note; OC5/OC6
        // drive nothing modeled, so its copy only reads back.
        private uint Locked(long b)
        {
            switch(b)
            {
            case BdtrOffset:
                return (bdtrWritten ? 0x300u : 0) | (lockLevel >= 1 ? 0x70FFu : 0) | (lockLevel >= 2 ? 0x0C00u : 0);
            case Cr2Offset:
                return lockLevel >= 1 ? 0x7F00u : 0;
            case CcerOffset:
                return lockLevel >= 2 ? 0xAAAAu : 0;
            case Ccmr1Offset:
            case Ccmr2Offset:
                return lockLevel >= 3 ? 0x01017878u : 0;
            }
            return 0;
        }

        private bool RefusedLock(long offset, long b, uint word, int width)
        {
            var changed = (word ^ Served(b)) & Locked(b);
            if(changed == 0)
            {
                return false;
            }
            var reg = Register(b);
            records.Refuse("TVI-TIM-LOCK", PredictedFault, reg, reg + " bits 0x" + changed.ToString("X8") + " under LOCK level " + lockLevel,
                reg + " written with a change to bits its LOCK configuration protects: silicon keeps the old value, so the timer does not do what the firmware wrote"
                    + (b == BdtrOffset && (changed & 0x300) != 0 ? " (the LOCK bits are frozen by the first BDTR write after reset)" : ""),
                "RM0433 Rev 9 §38.4.20 p1615 (LOCK[1:0]); " + (b == CcerOffset ? "§38.4.11 p1607" : b == Cr2Offset ? "§38.4.2 p1590" : b == BdtrOffset ? "§38.4.20 p1613-1615" : "§38.4.8 p1602-1603"),
                State(), offset, word, width);
            return true;
        }

        private bool RefusedField(long offset, long b, uint word, int width)
        {
            if(b == DmarOffset)
            {
                return false;   // DMAR is a register with no modeled value: TVI-TIM-DMAR answers for it
            }
            var rows = Fields.Where(f => f.Offset == b).ToArray();
            if(rows.Length == 0)
            {
                records.Refuse("TVI-TIM-FIELD", Unmodeled, Register(b), "no register at offset 0x" + offset.ToString("X"),
                    "write to a TIM1/TIM8 offset where RM0433 defines no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §38.4.32 Table 338 p1628", State(), offset, word, width);
                return true;
            }
            foreach(var f in rows)
            {
                var v = f.Of(word);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    var reg = Register(b);
                    records.Refuse("TVI-TIM-FIELD", Unmodeled, reg, reg + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        reg + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-1302.measure.md §5)",
                        State(), offset, word, width);
                    return true;
                }
            }
            return false;
        }

        private string Config()
        {
            if(config == null)
            {
                config = Check();
            }
            return config.Length == 0 ? null : config;
        }

        private string Check()
        {
            if(limit != 0xFFFF)
            {
                return "initialLimit 0x" + limit.ToString("X") + ", not 0xFFFF: TIM1 and TIM8 have 16-bit counters";
            }
            ulong start;
            try
            {
                var points = machine.SystemBus.GetRegistrationPoints(this).ToArray();
                if(points.Length != 1)
                {
                    return points.Length + " bus registrations, not one";
                }
                start = points[0].Range.StartAddress;
            }
            catch(Exception e)
            {
                return "no bus registration (" + e.GetType().Name + ")";
            }
            return start == Tim1Base || start == Tim8Base ? "" : "bus address 0x" + start.ToString("X") + ", which is neither TIM1's nor TIM8's";
        }

        private string State()
        {
            return "CCER=0x" + ccer.ToString("X") + " BDTR=0x" + bdtr.ToString("X") + " CR2=0x" + cr2.ToString("X")
                + " CCMR1=0x" + ccmr1.ToString("X") + " CCMR2=0x" + ccmr2.ToString("X") + " LOCK=" + lockLevel
                + " outputs=" + string.Concat(Enumerable.Range(0, Channels).Select(i => Level(i) ? "1" : "0"));
        }

        private static string Register(long b)
        {
            var r = Fields.FirstOrDefault(f => f.Offset == b);
            return r != null ? r.Register : b == DmarOffset ? "TIMx_DMAR" : "TIMx+0x" + b.ToString("X");
        }

        private readonly IMachine machine;
        private readonly Records records;
        private readonly uint limit;
        private readonly GPIO[] outs = new GPIO[Channels];
        private readonly Dictionary<int, IGPIO> connections;
        private uint cr2, ccmr1, ccmr2, ccer, bdtr, ccmr3;
        private int lockLevel => (int)(bdtr >> 8 & 3);
        private bool bdtrWritten;
        private string config;

        private const int Channels = 4;
        private const uint PwmMode1 = 6;
        private const uint MoeBit = 1u << 15;
        private const uint OssiBit = 1u << 10;
        private const uint LockBits = 3u << 8;
        private const uint AllOutputs = 0x1111;
        private const ulong Tim1Base = 0x40010000;
        private const ulong Tim8Base = 0x40010400;
        private const long Cr2Offset = 0x04;
        private const long Ccmr1Offset = 0x18;
        private const long Ccmr2Offset = 0x1C;
        private const long CcerOffset = 0x20;
        private const long Ccr1Offset = 0x34;
        private const long BdtrOffset = 0x44;
        private const long DmarOffset = 0x4C;
        private const long Ccmr3Offset = 0x54;
        private const long Af1Offset = 0x60;
        private const long Af2Offset = 0x64;

        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";

        // ---- the allow-list (RUL-16, RUL-17): one row per field, every bit 31:0 of every register ---------------

        private sealed class Row
        {
            public Row(string register, long offset, string section, string name, int msb, int lsb, uint[] allowed,
                string behavesAs, int page)
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

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        // Generated from RM0433 Rev 9 §38.4 (TIM1/TIM8 registers) with the classes ruled 2026-11-24T06:10Z; DMAR (0x4C)
        // has no row, because no value of it is modeled (TVI-TIM-DMAR).
        private static readonly Row[] Fields =
        {
            R("TIMx_CR1", 0x00, "§38.4.1", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "Res15_12", 15, 12, Zero, "reserved", "reserved: dropped, reads 0", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "UIFREMAP", 11, 11, Zero, "undefined in the stock model", "0 only: the stock model reserves the bit", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "Res10_10", 10, 10, Zero, "reserved", "reserved: dropped, reads 0", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "CKD[1:0]", 9, 8, Zero, "tag", "00: the clock division is a tag; it sets the dead-time and filter clock, neither modeled", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "ARPE", 7, 7, Any, "stock, wrong (with ARPE = 1 the stock model loads a new ARR only at overflow, not at UG (p1531, p1533); unaffected: every ARR change is written with CR1 = 0 (ARPE = 0, counter stopped) and followed by UG: ArduPilot's two configurations (0.104 s, 7.77 s) and every generated board's (spike/1100/data/writes.tsv); later ARR writes repeat the value in force)", "the stock model: ARR preload (STM32_Timer.cs L164, L365); not probed (both firmwares write 1); read for 0x0,0x1 (TVI-WO-1206; p1531, p1533, p1588; STM32_Timer.cs L164; STM32_Timer.cs L363-368)", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "CMS[1:0]", 6, 5, Zero, "acknowledged by name", "00 only: centre-aligned modes are not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1588; STM32_Timer.cs L163; STM32_Timer.cs L157)", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "DIR", 4, 4, Zero, "acknowledged by name", "0 only: downcounting is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1589; STM32_Timer.cs L156-162; STM32_Timer.cs L26)", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "OPM", 3, 3, Zero, "inherited, unaudited (acknowledged by name)", "0 only: one-pulse mode is not audited", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "URS", 2, 2, Any, "stock, wrong (with URS = 0 a UG sets UIF on silicon (p1597); the stock model sets it only with UIE = 1; unaffected: no firmware reads TIMx_SR: 0 TIM1 reads in the armed run, no SR read in the generated boots or the fixture (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); with DIER = 0 no interrupt or DMA request follows on either side)", "the stock model: stored, gates the update interrupt of UG (STM32_Timer.cs L283); not probed (ArduPilot writes 1); read for 0x0,0x1 (TVI-WO-1206; p1533, p1589, p1597; STM32_Timer.cs L154; STM32_Timer.cs L283-287)", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "UDIS", 1, 1, Zero, "acknowledged by name", "0 only: the stock model's update-disable path is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1531, p1589; STM32_Timer.cs L153; STM32_Timer.cs L52-55)", 1588),
            R("TIMx_CR1", 0x00, "§38.4.1", "CEN", 0, 0, Any, "stock, wrong (the first PWM-mode-1 rise comes one period after CEN, where silicon's OCxREF is high from CEN while CNT < CCRx (p1558); kept as the stock model's, coordinated with Aethelgard's motor witness, which predicts this convention (aethelgard-twin/src/timer.rs L6-24; ruled 2026-11-24T06:10Z Q1))", "the stock model: starts and stops the counter (probed, TVI-WO-1302.measure.md T1)", 1588),
            R("TIMx_CR2", 0x04, "§38.4.2", "Res31_24", 31, 24, Zero, "reserved", "reserved: dropped, reads 0", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "MMS2[3:0]", 23, 20, Zero, "undefined in the stock model", "0 only: the stock model reserves the bits; OC5/OC6 reach no pin and no trigger output is modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "Res19_19", 19, 19, Zero, "reserved", "reserved: dropped, reads 0", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS6", 18, 18, Zero, "undefined in the stock model", "0 only: the stock model reserves the bits; OC5/OC6 reach no pin and no trigger output is modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "Res17_17", 17, 17, Zero, "reserved", "reserved: dropped, reads 0", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS5", 16, 16, Zero, "undefined in the stock model", "0 only: the stock model reserves the bits; OC5/OC6 reach no pin and no trigger output is modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "Res15_15", 15, 15, Zero, "reserved", "reserved: dropped, reads 0", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS4", 14, 14, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag): the idle level of the output while MOE = 0 with OSSI = 1 (OIS1 p1591, OSSI p1615); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS3N", 13, 13, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag); drives nothing: no complementary output is modeled (ruled 2026-11-24T06:10Z Q5 (a)); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS3", 12, 12, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag): the idle level of the output while MOE = 0 with OSSI = 1 (OIS1 p1591, OSSI p1615); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS2N", 11, 11, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag); drives nothing: no complementary output is modeled (ruled 2026-11-24T06:10Z Q5 (a)); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS2", 10, 10, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag): the idle level of the output while MOE = 0 with OSSI = 1 (OIS1 p1591, OSSI p1615); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS1N", 9, 9, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag); drives nothing: no complementary output is modeled (ruled 2026-11-24T06:10Z Q5 (a)); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "OIS1", 8, 8, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag): the idle level of the output while MOE = 0 with OSSI = 1 (OIS1 p1591, OSSI p1615); locked from LOCK level 1 (p1615)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "TI1S", 7, 7, Zero, "acknowledged by name", "0 only: the input XOR is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1591; STM32_Timer.cs L175; STM32_Timer.cs L505)", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "MMS[2:0]", 6, 4, Zero, "tag", "0 only: a stock tag (MMS); preloaded control, DMA selection and the trigger output are not modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "CCDS", 3, 3, Zero, "tag", "0 only: a stock tag (CCDS); preloaded control, DMA selection and the trigger output are not modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "CCUS", 2, 2, Zero, "tag", "0 only: a stock tag (CCUS); preloaded control, DMA selection and the trigger output are not modeled", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "Res1_1", 1, 1, Zero, "reserved", "reserved: dropped, reads 0", 1589),
            R("TIMx_CR2", 0x04, "§38.4.2", "CCPC", 0, 0, Zero, "tag", "0 only: a stock tag (CCPC); preloaded control, DMA selection and the trigger output are not modeled", 1589),
            R("TIMx_SMCR", 0x08, "§38.4.3", "Res31_22", 31, 22, Zero, "reserved", "reserved: dropped, reads 0", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "TS[4:3]", 21, 20, Zero, "undefined in the stock model", "0 only: the stock model reserves the bits", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "Res19_17", 19, 17, Zero, "reserved", "reserved: dropped, reads 0", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "SMS[3]", 16, 16, Zero, "undefined in the stock model", "0 only: the stock model reserves the bits", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "ETP", 15, 15, Zero, "tag", "0 only: a stock tag (ETP); the external trigger is not modeled", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "ECE", 14, 14, Zero, "tag", "0 only: a stock tag (ECE); the external trigger is not modeled", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "ETPS[1:0]", 13, 12, Zero, "tag", "0 only: a stock tag (ETPS); the external trigger is not modeled", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "ETF[3:0]", 11, 8, Zero, "tag", "0 only: a stock tag (ETF); the external trigger is not modeled", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "MSM", 7, 7, Zero, "tag", "0 only: a stock tag (MSM); the external trigger is not modeled", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "TS[2:0]", 6, 4, Zero, "inherited, unaudited (acknowledged by name)", "0 only: slave modes and trigger selection are not audited", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "Res3_3", 3, 3, Zero, "reserved", "reserved: dropped, reads 0", 1592),
            R("TIMx_SMCR", 0x08, "§38.4.3", "SMS[2:0]", 2, 0, Zero, "inherited, unaudited (acknowledged by name)", "0 only: slave modes and trigger selection are not audited", 1592),
            R("TIMx_DIER", 0x0C, "§38.4.4", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "Res15_15", 15, 15, Zero, "reserved", "reserved: dropped, reads 0", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "TDE", 14, 14, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "COMDE", 13, 13, Zero, "undefined in the stock model", "0 only: the stock model reserves the bit; COM and break are not modeled", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC4DE", 12, 12, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC3DE", 11, 11, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC2DE", 10, 10, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC1DE", 9, 9, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "UDE", 8, 8, Zero, "tag", "0 only: a DMA request enable, a stock tag; DMA is the next 13NN", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "BIE", 7, 7, Zero, "undefined in the stock model", "0 only: the stock model reserves the bit; COM and break are not modeled", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "TIE", 6, 6, Any, "acknowledged by name", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1595, p1597; STM32_Timer.cs L222; STM32_Timer.cs L656)", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "COMIE", 5, 5, Zero, "undefined in the stock model", "0 only: the stock model reserves the bit; COM and break are not modeled", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC4IE", 4, 4, Any, "stock, wrong (with CC4IE = 0 silicon still sets CC4IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L220; STM32_Timer.cs L132-134)", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC3IE", 3, 3, Any, "stock, wrong (with CC3IE = 0 silicon still sets CC3IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L219; STM32_Timer.cs L132-134)", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC2IE", 2, 2, Any, "stock, wrong (with CC2IE = 0 silicon still sets CC2IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L218; STM32_Timer.cs L132-134)", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "CC1IE", 1, 1, Any, "stock, wrong (with CC1IE = 0 silicon still sets CC1IF at a compare match (p1597); the stock model sets it only with the enable; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L217; STM32_Timer.cs L132-134)", 1594),
            R("TIMx_DIER", 0x0C, "§38.4.4", "UIE", 0, 0, Any, "stock, wrong (with UIE = 0 silicon still sets UIF at an update event (p1597); the stock model does not at UG; unaffected: no firmware reads TIMx_SR (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)); the interrupt is off on both sides)", "the stock model: interrupt enables (STM32_Timer.cs L215-233); with CCxE = 0 a running channel no longer reaches the pin (the guard gates it); read for 0x0 (TVI-WO-1206; p1595, p1597; STM32_Timer.cs L216; STM32_Timer.cs L86)", 1594),
            R("TIMx_SR", 0x10, "§38.4.5", "Res31_18", 31, 18, Zero, "reserved", "reserved: dropped, reads 0", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC6IF", 17, 17, Any, "stock, wrong (the twin reads CC6IF 0: the stock model has no channels 5 and 6. Silicon sets it at a compare match (p1596, \"Refer to CC1IF\"; p1597); ArduPilot writes CCMR3 = 0x01016868 (channels 5 and 6 outputs) and never writes CCR5/CCR6, which reset to 0, so on silicon it would set at each counter match with 0 (inferred from p1597, not run). ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "reads 0 and is never set (channels 5-6 are refused or unrouted); a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC5IF", 16, 16, Any, "stock, wrong (the twin reads CC5IF 0: the stock model has no channels 5 and 6. Silicon sets it at a compare match (p1596, \"Refer to CC1IF\"; p1597); ArduPilot writes CCMR3 = 0x01016868 (channels 5 and 6 outputs) and never writes CCR5/CCR6, which reset to 0, so on silicon it would set at each counter match with 0 (inferred from p1597, not run). ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "reads 0 and is never set (channels 5-6 are refused or unrouted); a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "Res15_14", 15, 14, Zero, "reserved", "reserved: dropped, reads 0", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "SBIF", 13, 13, Any, "stock, wrong (the twin reads SBIF 0 and never sets it: break, break 2, system break and COM are not modeled, and silicon sets SBIF at a system break (p1596), whose sources are the core lockup, the PVD, an SRAM, TCM or flash double ECC error and a CSS clock failure (p1566), none of them modeled. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z); and it sets none of them on silicon either (BDTR = 0x8000: MOE only, no BKE or BK2E; EGR = UG only, no BG, B2G or COMG; CR2 = 0; SMCR never written))", "reads 0 and is never set; a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC4OF", 12, 12, Any, "acknowledged by name", "the stock model, as silicon: set only in input capture mode when a capture finds CCxIF already set; reading CCRx clears CCxIF, and writing 0 clears it, a 1 changes nothing (rc_w0) (A10; p1596)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC3OF", 11, 11, Any, "acknowledged by name", "the stock model, as silicon: set only in input capture mode when a capture finds CCxIF already set; reading CCRx clears CCxIF, and writing 0 clears it, a 1 changes nothing (rc_w0) (A10; p1596)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC2OF", 10, 10, Any, "acknowledged by name", "the stock model, as silicon: set only in input capture mode when a capture finds CCxIF already set; reading CCRx clears CCxIF, and writing 0 clears it, a 1 changes nothing (rc_w0) (A10; p1596)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC1OF", 9, 9, Any, "acknowledged by name", "the stock model, as silicon: set only in input capture mode when a capture finds CCxIF already set; reading CCRx clears CCxIF, and writing 0 clears it, a 1 changes nothing (rc_w0) (A10; p1596)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "B2IF", 8, 8, Any, "stock, wrong (the twin reads B2IF 0 and never sets it: break, break 2, system break and COM are not modeled, and silicon sets B2IF when the break 2 input goes active with BK2E = 1, or at a software break, B2G = 1, whatever BK2E (p1596; p1567). ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z); and it sets none of them on silicon either (BDTR = 0x8000: MOE only, no BKE or BK2E; EGR = UG only, no BG, B2G or COMG; CR2 = 0; SMCR never written))", "reads 0 and is never set; a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "BIF", 7, 7, Any, "stock, wrong (the twin reads BIF 0 and never sets it: break, break 2, system break and COM are not modeled, and silicon sets BIF when the break input goes active with BKE = 1, or at a software break, BG = 1, whatever BKE (p1597; p1567). ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z); and it sets none of them on silicon either (BDTR = 0x8000: MOE only, no BKE or BK2E; EGR = UG only, no BG, B2G or COMG; CR2 = 0; SMCR never written))", "reads 0 and is never set; a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "TIF", 6, 6, Any, "stock, wrong (the stock model sets TIF at a trigger only with TIE = 1 (STM32_Timer.cs L656-658); silicon sets it at a TRG event (an active TRGI edge with the slave mode controller enabled in all modes but gated, or a start or stop in gated mode, p1597) and at EGR.TG = 1 (p1598), so a firmware that polls it reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z); it never writes SMCR and never sets TG, so no trigger event occurs on silicon either)", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "COMIF", 5, 5, Any, "stock, wrong (the twin reads COMIF 0 and never sets it: break, break 2, system break and COM are not modeled, and silicon sets COMIF at a COM event (p1597): COMG = 1 (p1598) or, with CCPC = 1, a TRGI edge (p1592). ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z); and it sets none of them on silicon either (BDTR = 0x8000: MOE only, no BKE or BK2E; EGR = UG only, no BG, B2G or COMG; CR2 = 0; SMCR never written))", "reads 0 and is never set; a write of 0 or 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC4IF", 4, 4, Any, "stock, wrong (the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC3IF", 3, 3, Any, "stock, wrong (the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC2IF", 2, 2, Any, "stock, wrong (the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "CC1IF", 1, 1, Any, "stock, wrong (the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_SR", 0x10, "§38.4.5", "UIF", 0, 0, Any, "stock, wrong (the stock model sets UIF at UG only with UIE = 1, and CCxIF at a compare only with CCxIE = 1 (STM32_Timer.cs L283, L132-134); silicon sets them regardless (p1597), so a firmware that polls them reads 0 in the twin. ArduPilot's path is not affected: its armed run writes SR = 0, never reads SR and enables no TIM1 interrupt or DMA request (DIER = 0) (TVI-WO-1205, ruled 2026-11-26T17:02Z))", "the stock model: cleared by writing 0, a 1 changes nothing (rc_w0)", 1596),
            R("TIMx_EGR", 0x14, "§38.4.6", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "Res15_9", 15, 9, Zero, "reserved", "reserved: dropped, reads 0", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "B2G", 8, 8, Zero, "undefined in the stock model", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "BG", 7, 7, Zero, "undefined in the stock model", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "TG", 6, 6, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "COMG", 5, 5, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "CC4G", 4, 4, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "CC3G", 3, 3, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "CC2G", 2, 2, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "CC1G", 1, 1, Zero, "tag", "0 only: a capture, COM, trigger or break event is not modeled", 1598),
            R("TIMx_EGR", 0x14, "§38.4.6", "UG", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model: reinitialises the counter (STM32_Timer.cs L266-295); probed (T1, the control)", 1598),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "Res31_25", 31, 25, Zero, "reserved", "reserved: dropped, reads 0", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC2M[3]", 24, 24, Zero, "undefined in the stock model", "0 only: the stock model reserves bits 16 and 24, so the modes 1000-1111 are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "Res23_17", 23, 17, Zero, "reserved", "reserved: dropped, reads 0", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC1M[3]", 16, 16, Zero, "undefined in the stock model", "0 only: the stock model reserves bits 16 and 24, so the modes 1000-1111 are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC2CE", 15, 15, Zero, "tag", "0 only: a stock tag (OC2CE); fast enable and OCxREF clear are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC2M[2:0]", 14, 12, new uint[] { 0, 4, 5, 6 }, "modeled by the guard", "the stock model's compare modes frozen, force inactive, force active and PWM mode 1; in PWM mode 1 a CCRx of 0 holds OCxREF at 0 (p1558), the guard lowering the stock line; locked at LOCK level 3 (p1602)", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC2PE", 11, 11, Any, "stock, wrong (OCxPE is a stock tag: no preload, so a CCRx write takes effect at once where silicon waits for the update event (p1603); acknowledged by name, ruled 2026-11-24T06:10Z Q7; Aethelgard lists it unobservable)", "kept by the guard and served on read; locked at LOCK level 3 (p1603)", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC2FE", 10, 10, Zero, "tag", "0 only: a stock tag (OC2FE); fast enable and OCxREF clear are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "CC2S[1:0]", 9, 8, Zero, "acknowledged by name", "00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1601; STM32_Timer.cs L429; STM32_Timer.cs L720)", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC1CE", 7, 7, Zero, "tag", "0 only: a stock tag (OC1CE); fast enable and OCxREF clear are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC1M[2:0]", 6, 4, new uint[] { 0, 4, 5, 6 }, "modeled by the guard", "the stock model's compare modes frozen, force inactive, force active and PWM mode 1; in PWM mode 1 a CCRx of 0 holds OCxREF at 0 (p1558), the guard lowering the stock line; locked at LOCK level 3 (p1602)", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC1PE", 3, 3, Any, "stock, wrong (OCxPE is a stock tag: no preload, so a CCRx write takes effect at once where silicon waits for the update event (p1603); acknowledged by name, ruled 2026-11-24T06:10Z Q7; Aethelgard lists it unobservable)", "kept by the guard and served on read; locked at LOCK level 3 (p1603)", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "OC1FE", 2, 2, Zero, "tag", "0 only: a stock tag (OC1FE); fast enable and OCxREF clear are not modeled", 1601),
            R("TIMx_CCMR1", 0x18, "§38.4.8", "CC1S[1:0]", 1, 0, Zero, "acknowledged by name", "00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1603; STM32_Timer.cs L429; STM32_Timer.cs L720)", 1601),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "Res31_25", 31, 25, Zero, "reserved", "reserved: dropped, reads 0", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC4M[3]", 24, 24, Zero, "undefined in the stock model", "0 only: the stock model reserves bits 16 and 24, so the modes 1000-1111 are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "Res23_17", 23, 17, Zero, "reserved", "reserved: dropped, reads 0", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC3M[3]", 16, 16, Zero, "undefined in the stock model", "0 only: the stock model reserves bits 16 and 24, so the modes 1000-1111 are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC4CE", 15, 15, Zero, "tag", "0 only: a stock tag (OC4CE); fast enable and OCxREF clear are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC4M[2:0]", 14, 12, new uint[] { 0, 4, 5, 6 }, "modeled by the guard", "the stock model's compare modes frozen, force inactive, force active and PWM mode 1; in PWM mode 1 a CCRx of 0 holds OCxREF at 0 (p1558), the guard lowering the stock line; locked at LOCK level 3 (p1602)", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC4PE", 11, 11, Any, "stock, wrong (OCxPE is a stock tag: no preload, so a CCRx write takes effect at once where silicon waits for the update event (p1603); acknowledged by name, ruled 2026-11-24T06:10Z Q7; Aethelgard lists it unobservable)", "kept by the guard and served on read; locked at LOCK level 3 (p1603)", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC4FE", 10, 10, Zero, "tag", "0 only: a stock tag (OC4FE); fast enable and OCxREF clear are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "CC4S[1:0]", 9, 8, Zero, "acknowledged by name", "00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1604; STM32_Timer.cs L429; STM32_Timer.cs L720)", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC3CE", 7, 7, Zero, "tag", "0 only: a stock tag (OC3CE); fast enable and OCxREF clear are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC3M[2:0]", 6, 4, new uint[] { 0, 4, 5, 6 }, "modeled by the guard", "the stock model's compare modes frozen, force inactive, force active and PWM mode 1; in PWM mode 1 a CCRx of 0 holds OCxREF at 0 (p1558), the guard lowering the stock line; locked at LOCK level 3 (p1602)", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC3PE", 3, 3, Any, "stock, wrong (OCxPE is a stock tag: no preload, so a CCRx write takes effect at once where silicon waits for the update event (p1603); acknowledged by name, ruled 2026-11-24T06:10Z Q7; Aethelgard lists it unobservable)", "kept by the guard and served on read; locked at LOCK level 3 (p1603)", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "OC3FE", 2, 2, Zero, "tag", "0 only: a stock tag (OC3FE); fast enable and OCxREF clear are not modeled", 1604),
            R("TIMx_CCMR2", 0x1C, "§38.4.10", "CC3S[1:0]", 1, 0, Zero, "acknowledged by name", "00 only: the channel is an output; input capture is not audited; read for 0x0: written 0 only, the mode the timer runs in on both sides: outputs (CCxS = 00), edge-aligned upcounting, updates enabled, no input XOR, no trigger interrupt (the guard holds SMCR.SMS at 0 and refuses EGR.TG) (TVI-WO-1206 A7; p1604; STM32_Timer.cs L429; STM32_Timer.cs L720)", 1604),
            R("TIMx_CCER", 0x20, "§38.4.11", "Res31_22", 31, 22, Zero, "reserved", "reserved: dropped, reads 0", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC6P", 21, 21, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled (the stock model reserves bits 16-31)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC6E", 20, 20, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled (the stock model reserves bits 16-31)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "Res19_18", 19, 18, Zero, "reserved", "reserved: dropped, reads 0", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC5P", 17, 17, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled (the stock model reserves bits 16-31)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC5E", 16, 16, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled (the stock model reserves bits 16-31)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC4NP", 15, 15, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "Res14_14", 14, 14, Zero, "reserved", "reserved: dropped, reads 0", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC4P", 13, 13, Any, "modeled by the guard", "kept by the guard and applied once to the driven level, OCx = OCxREF xor CCxP (Table 337 p1608); the stock model never applies it to an output; locked at LOCK level 2 (p1607)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC4E", 12, 12, Any, "modeled by the guard", "gates the channel output with MOE (Table 337 p1608), in the guard alone (the stock model sees every CCxE set, so its line stays OCxREF): disabled, the pin is not driven by the timer (Hi-Z) and the guard drives it low. a channel output silicon does not drive (Hi-Z, Table 337 p1608) is driven low: every committed motor pin has a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j), so low is silicon's level there; an open link would float, which the twin cannot see (ruled 2026-11-24T06:10Z Q4)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC3NP", 11, 11, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC3NE", 10, 10, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC3P", 9, 9, Any, "modeled by the guard", "kept by the guard and applied once to the driven level, OCx = OCxREF xor CCxP (Table 337 p1608); the stock model never applies it to an output; locked at LOCK level 2 (p1607)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC3E", 8, 8, Any, "modeled by the guard", "gates the channel output with MOE (Table 337 p1608), in the guard alone (the stock model sees every CCxE set, so its line stays OCxREF): disabled, the pin is not driven by the timer (Hi-Z) and the guard drives it low. a channel output silicon does not drive (Hi-Z, Table 337 p1608) is driven low: every committed motor pin has a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j), so low is silicon's level there; an open link would float, which the twin cannot see (ruled 2026-11-24T06:10Z Q4)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC2NP", 7, 7, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC2NE", 6, 6, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC2P", 5, 5, Any, "modeled by the guard", "kept by the guard and applied once to the driven level, OCx = OCxREF xor CCxP (Table 337 p1608); the stock model never applies it to an output; locked at LOCK level 2 (p1607)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC2E", 4, 4, Any, "modeled by the guard", "gates the channel output with MOE (Table 337 p1608), in the guard alone (the stock model sees every CCxE set, so its line stays OCxREF): disabled, the pin is not driven by the timer (Hi-Z) and the guard drives it low. a channel output silicon does not drive (Hi-Z, Table 337 p1608) is driven low: every committed motor pin has a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j), so low is silicon's level there; an open link would float, which the twin cannot see (ruled 2026-11-24T06:10Z Q4)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC1NP", 3, 3, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC1NE", 2, 2, Zero, "stock, wrong (a CHxN pin in AF mode shows NOT CHx through the GPIO port's invertedAFPins, whatever CCxNE says, where silicon leaves it Hi-Z with CCxNE = 0 (Table 337 p1608): a GPIO-guard row, ruled 2026-11-24T06:10Z Q5 (a))", "0 only: the guard has no complementary output to gate", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC1P", 1, 1, Any, "modeled by the guard", "kept by the guard and applied once to the driven level, OCx = OCxREF xor CCxP (Table 337 p1608); the stock model never applies it to an output; locked at LOCK level 2 (p1607)", 1605),
            R("TIMx_CCER", 0x20, "§38.4.11", "CC1E", 0, 0, Any, "modeled by the guard", "gates the channel output with MOE (Table 337 p1608), in the guard alone (the stock model sees every CCxE set, so its line stays OCxREF): disabled, the pin is not driven by the timer (Hi-Z) and the guard drives it low. a channel output silicon does not drive (Hi-Z, Table 337 p1608) is driven low: every committed motor pin has a fitted 442 kOhm pull-down (Aethelgard 65c74788, 4300j), so low is silicon's level there; an open link would float, which the twin cannot see (ruled 2026-11-24T06:10Z Q4)", 1605),
            R("TIMx_CNT", 0x24, "§38.4.12", "UIFCPY", 31, 31, Zero, "read-only", "ignored (read-only); reads 0 (UIFREMAP = 0)", 1609),
            R("TIMx_CNT", 0x24, "§38.4.12", "Res30_16", 30, 16, Zero, "reserved", "reserved: dropped, reads 0", 1609),
            R("TIMx_CNT", 0x24, "§38.4.12", "CNT[15:0]", 15, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model: the counter value; not written by either firmware", 1609),
            R("TIMx_PSC", 0x28, "§38.4.13", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1609),
            R("TIMx_PSC", 0x28, "§38.4.13", "PSC[15:0]", 15, 0, Any, "stock, wrong (the stock model applies a new PSC at the write, where silicon loads it at the next update event (p1531, p1609); unaffected: every PSC write is made with the counter stopped and followed by UG, which loads it on silicon: ArduPilot's two configurations and every generated board's (spike/1100/data/writes.tsv))", "the stock model: CK_CNT = f / (PSC + 1) (p1609); probed (T1, the control); read for 0x0,0x3f,0xc7 (TVI-WO-1206; p1531, p1533, p1609; STM32_Timer.cs L349; STM32_Timer.cs L351-356)", 1609),
            R("TIMx_ARR", 0x2C, "§38.4.14", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1609),
            R("TIMx_ARR", 0x2C, "§38.4.14", "ARR[15:0]", 15, 0, Any, "stock, wrong (the period is ARR ticks where silicon counts ARR + 1 (Figure 379 p1558); kept as the stock model's, coordinated with Aethelgard's motor witness (aethelgard-twin/src/timer.rs L6-24; ruled 2026-11-24T06:10Z Q1))", "the stock model: the auto-reload value; probed (T1)", 1609),
            R("TIMx_RCR", 0x30, "§38.4.15", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1610),
            R("TIMx_RCR", 0x30, "§38.4.15", "REP[15:0]", 15, 0, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the repetition counter is not audited", 1610),
            R("TIMx_CCR1", 0x34, "§38.4.16", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1610),
            R("TIMx_CCR1", 0x34, "§38.4.16", "CCR1[15:0]", 15, 0, Any, "modeled by the guard", "the stock model, set to 0 at reset by the guard (p1610; the stock reset is its initialLimit, 0xFFFF); in PWM mode 1 a value of 0 holds OCxREF at 0 (p1558): the guard lowers the stock line, which the stock model leaves at its last level; a value above ARR holds it at 1 (p1558), as the stock model does", 1610),
            R("TIMx_CCR2", 0x38, "§38.4.17", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1611),
            R("TIMx_CCR2", 0x38, "§38.4.17", "CCR2[15:0]", 15, 0, Any, "modeled by the guard", "the stock model, set to 0 at reset by the guard (p1610; the stock reset is its initialLimit, 0xFFFF); in PWM mode 1 a value of 0 holds OCxREF at 0 (p1558): the guard lowers the stock line, which the stock model leaves at its last level; a value above ARR holds it at 1 (p1558), as the stock model does", 1611),
            R("TIMx_CCR3", 0x3C, "§38.4.18", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1611),
            R("TIMx_CCR3", 0x3C, "§38.4.18", "CCR3[15:0]", 15, 0, Any, "modeled by the guard", "the stock model, set to 0 at reset by the guard (p1610; the stock reset is its initialLimit, 0xFFFF); in PWM mode 1 a value of 0 holds OCxREF at 0 (p1558): the guard lowers the stock line, which the stock model leaves at its last level; a value above ARR holds it at 1 (p1558), as the stock model does", 1611),
            R("TIMx_CCR4", 0x40, "§38.4.19", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1612),
            R("TIMx_CCR4", 0x40, "§38.4.19", "CCR4[15:0]", 15, 0, Any, "modeled by the guard", "the stock model, set to 0 at reset by the guard (p1610; the stock reset is its initialLimit, 0xFFFF); in PWM mode 1 a value of 0 holds OCxREF at 0 (p1558): the guard lowers the stock line, which the stock model leaves at its last level; a value above ARR holds it at 1 (p1558), as the stock model does", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "Res31_26", 31, 26, Zero, "reserved", "reserved: dropped, reads 0", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BK2P", 25, 25, Zero, "undefined in the stock model", "0 only: the break inputs and their filters are not modeled (the stock model reserves bits 16-31)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BK2E", 24, 24, Zero, "undefined in the stock model", "0 only: the break inputs and their filters are not modeled (the stock model reserves bits 16-31)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BK2F[3:0]", 23, 20, Zero, "undefined in the stock model", "0 only: the break inputs and their filters are not modeled (the stock model reserves bits 16-31)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BKF[3:0]", 19, 16, Zero, "undefined in the stock model", "0 only: the break inputs and their filters are not modeled (the stock model reserves bits 16-31)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "MOE", 15, 15, Any, "modeled by the guard", "kept by the guard: with MOE = 0 the outputs are disabled (OSSI = 0, Hi-Z, driven low) or at their idle level OISx (OSSI = 1) (Table 337 p1608, p1614); set only by software, since AOE and the break inputs are refused", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "AOE", 14, 14, Zero, "tag", "0 only: MOE is never set by the update event", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BKP", 13, 13, Zero, "tag", "0 only: no break input is modeled", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "BKE", 12, 12, Zero, "tag", "0 only: no break input is modeled", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "OSSR", 11, 11, Any, "modeled by the guard", "kept by the guard: it acts only with CCxNE = 1 (Table 337 p1608), which is refused, so it changes no output; locked at LOCK level 2 (p1614)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "OSSI", 10, 10, Any, "modeled by the guard", "kept by the guard: with MOE = 0, an enabled output goes to its idle level OISx (1) or is released (0, Hi-Z, driven low) (p1615, Table 337 p1608); locked at LOCK level 2", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "LOCK[1:0]", 9, 8, Any, "modeled by the guard", "kept by the guard: frozen by the first BDTR write after reset (p1615); level 1 locks DTG, BKE, BKP, AOE and OISx/OISxN, level 2 also CCxP, CCxNP, OSSR and OSSI, level 3 also OCxM and OCxPE (p1615); a write that would change a locked bit is a predicted-fault (silicon keeps the old value)", 1612),
            R("TIMx_BDTR", 0x44, "§38.4.20", "DTG[7:0]", 7, 0, Zero, "tag", "0 only: no dead time is modeled", 1612),
            R("TIMx_DCR", 0x48, "§38.4.21", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1615),
            R("TIMx_DCR", 0x48, "§38.4.21", "Res15_13", 15, 13, Zero, "reserved", "reserved: dropped, reads 0", 1615),
            R("TIMx_DCR", 0x48, "§38.4.21", "DBL[4:0]", 12, 8, Zero, "undefined in the stock model", "0 only: DMA bursts are not modeled (DMA is the next 13NN)", 1615),
            R("TIMx_DCR", 0x48, "§38.4.21", "Res7_5", 7, 5, Zero, "reserved", "reserved: dropped, reads 0", 1615),
            R("TIMx_DCR", 0x48, "§38.4.21", "DBA[4:0]", 4, 0, Zero, "undefined in the stock model", "0 only: DMA bursts are not modeled (DMA is the next 13NN)", 1615),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "Res31_25", 31, 25, Zero, "reserved", "reserved: dropped, reads 0", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC6M[3]", 24, 24, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "Res23_17", 23, 17, Zero, "reserved", "reserved: dropped, reads 0", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC5M[3]", 16, 16, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC6CE", 15, 15, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC6M[2:0]", 14, 12, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC6PE", 11, 11, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC6FE", 10, 10, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "Res9_8", 9, 8, Zero, "reserved", "reserved: dropped, reads 0", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC5CE", 7, 7, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC5M[2:0]", 6, 4, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC5PE", 3, 3, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "OC5FE", 2, 2, Any, "stock, wrong (the stock model has no CCMR3 and drops a write; OC5REF and OC6REF reach no pin and nothing modeled while CC5E = CC6E = 0 and GC5Cx = 0, so the value is kept by the guard and served on read (ruled 2026-11-24T06:10Z Q7; ArduPilot writes 0x1016868))", "kept by the guard and served on read; drives nothing", 1617),
            R("TIMx_CCMR3", 0x54, "§38.4.23", "Res1_0", 1, 0, Zero, "reserved", "reserved: dropped, reads 0", 1617),
            R("TIMx_CCR5", 0x58, "§38.4.24", "GC5C3", 31, 31, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3", 1618),
            R("TIMx_CCR5", 0x58, "§38.4.24", "GC5C2", 30, 30, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3", 1618),
            R("TIMx_CCR5", 0x58, "§38.4.24", "GC5C1", 29, 29, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3", 1618),
            R("TIMx_CCR5", 0x58, "§38.4.24", "Res28_16", 28, 16, Zero, "reserved", "reserved: dropped, reads 0", 1618),
            R("TIMx_CCR5", 0x58, "§38.4.24", "CCR5[15:0]", 15, 0, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3", 1618),
            R("TIMx_CCR6", 0x5C, "§38.4.25", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads 0", 1619),
            R("TIMx_CCR6", 0x5C, "§38.4.25", "CCR6[15:0]", 15, 0, Zero, "undefined in the stock model", "0 only: channels 5 and 6 are not modeled; GC5Cx would combine OC5REF into OC1-3", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "Res31_18", 31, 18, Zero, "reserved", "reserved: dropped, reads 0", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "ETRSEL[3:2]", 17, 16, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "ETRSEL[1:0]", 15, 14, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "Res13_12", 13, 12, Zero, "reserved", "reserved: dropped, reads 0", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKCMP2P", 11, 11, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKCMP1P", 10, 10, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKINP", 9, 9, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKDF1BK0E", 8, 8, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "Res7_3", 7, 3, Zero, "reserved", "reserved: dropped, reads 0", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKCMP2E", 2, 2, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKCMP1E", 1, 1, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF1", 0x60, "§38.4.26, §38.4.28", "BKINE", 0, 0, new uint[] { 1 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1619),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "Res31_12", 31, 12, Zero, "reserved", "reserved: dropped, reads 0", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2CMP2P", 11, 11, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2CMP1P", 10, 10, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2INP", 9, 9, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2DF1BK1E", 8, 8, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "Res7_3", 7, 3, Zero, "reserved", "reserved: dropped, reads 0", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2CMP2E", 2, 2, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2CMP1E", 1, 1, new uint[] { 0 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_AF2", 0x64, "§38.4.27, §38.4.29", "BK2INE", 0, 0, new uint[] { 1 }, "undefined in the stock model", "its reset value only: the break inputs and the ETR selection are not modeled", 1621),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "Res31_28", 31, 28, Zero, "reserved", "reserved: dropped, reads 0", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "TI4SEL[3:0]", 27, 24, Zero, "undefined in the stock model", "0 only: the input selection is not audited", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "Res23_20", 23, 20, Zero, "reserved", "reserved: dropped, reads 0", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "TI3SEL[3:0]", 19, 16, Zero, "undefined in the stock model", "0 only: the input selection is not audited", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "Res15_12", 15, 12, Zero, "reserved", "reserved: dropped, reads 0", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "TI2SEL[3:0]", 11, 8, Zero, "undefined in the stock model", "0 only: the input selection is not audited", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "Res7_4", 7, 4, Zero, "reserved", "reserved: dropped, reads 0", 1626),
            R("TIMx_TISEL", 0x68, "§38.4.30, §38.4.31", "TI1SEL[3:0]", 3, 0, Zero, "undefined in the stock model", "0 only: the input selection is not audited", 1626),
        };
    }
}
