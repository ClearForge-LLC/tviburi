// Tviburi's guard over Renode's STM32_DMAMUX for the STM32H753's DMAMUX1 (TVI-WO-1308): each DMA stream's request route is
// served from the guard's copy and judged against the stream the DMA guard starts. The measurement, every page, and the
// rulings (2026-11-26T09:31Z, Q1-Q4): docs/work-orders/TVI-WO-1308.measure.md §2, §4, §6 (RM0433 Rev 9 §17).
//
// Insertion point (RUL-07; ruled Q1): STM32_DMAMUX is public and not sealed (measure.md §1, R), so this guard is a subclass
// that re-implements the bus interfaces and calls the base for a modeled access. No typed redeclaration exists (RUL-09 (b)),
// so the stock `dmamux1` is cancelled and this guard declared at 0x40020800 `as "dmamux1"`, with the stock entry's properties
// and routes and every other reference to the variable restated (model/stm32h7-dmamux-restate.repl). The nested `Registers`
// enum, a copy of the stock one, keeps the access log's register names. The bus methods hide the base's (`new`), so a C#
// caller holding the stock type (the DMA guard's `dmamux`) reads the stock registers, which hold the same route as this
// guard's copy: every write the guard passes reaches both.
//
// The clock (ruled Q4): DMA1EN or DMA2EN clocks DMAMUX1 ("DMA1 and DMAMUX1 clock enable", "DMA2 and DMAMUX1 Clock Enable",
// RM0433 Rev 9 §8.7.41 p455). With both 0 in the RCC guard's RCC_AHB1ENR (read through its object at each access), a write is
// dropped and a read returns 0, with no record, as the RCC guard does for every unclocked peripheral ("A write access will be
// ignored", §8.5.11 p373). Covered, not refused. DMA1RST or DMA2RST written 1 ("DMA1 and DMAMUX1 reset", §8.7.29 p433) resets
// this guard and its stock model, through the RCC guard's `Written` event (the flash guard's, TVI-WO-1304), as the RCC resets
// the DMA guards. A machine reset resets both too (Reset). This guard keeps no copy of the RCC's registers.
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-DMAMUX-CONFIG the platform gives this guard what it cannot model: no RCC guard on the machine (the clock and the reset
//                     above are the RCC guard's: without it the twin cannot say whether a write takes), a bus address that is
//                     not DMAMUX1's (0x40020800, RM0433 Rev 9 Table 8 p134), or other than 16 channels and 8 generators
//                     (Table 123 p696). unmodeled; every access refuses.
//   (the clock)       both DMA1EN and DMA2EN 0: the access is dropped as above, no record.
//   TVI-DMAMUX-WIDTH  a misaligned access: "DMAMUX registers may be accessed per byte (8-bit), half-word (16-bit), or word
//                     (32-bit). The address must be aligned with the data size" (§17.6 p708), and nothing says what a
//                     misaligned one does. unmodeled. An aligned byte or half-word access is modeled (ruled Q2): a write is
//                     merged into the served word, judged as that word, and passed to the stock model as a word (the stock
//                     model drops a sub-word write, measure.md §2.1 S); a read is served from the word.
//   TVI-DMAMUX-READ   a read of an offset with no register (Table 133 p716-717). unmodeled.
//   TVI-DMAMUX-FIELD  the allow-list (`Fields`): a field given a value the model does not implement (the synchronisation,
//                     event and generator fields, 0 only), or a write to an offset with no register. unmodeled. DMAREQ_ID takes
//                     any id, a reserved one included (ruled 2026-11-26T10:43Z Q18 (a)): a route nothing starts moves nothing,
//                     and a stream started on a reserved id is the DMA guard's TVI-DMA-ROUTE, predicted-fault (p697-698).
//   TVI-DMAMUX-LIVE   (ruled Q3) a CxCR write that gives DMAREQ_ID another value while the DMA stream its channel serves is
//                     enabled (running, or completed by the twin with EN still set; the DMA guard's StreamState): RM0433 sets
//                     the route before the stream's EN (§17.4.2 p703) and states nothing of a change while it is active.
//                     unmodeled. The same id passes (ArduPilot rewrites it 77,402 times, measure.md §2.1).
//   TVI-DMAMUX-PAIR   (ruled Q3, narrowed by Q18 (b); the DMA guard's one call, RefusedPair, at its route check on EN) the
//                     stream's PAR is in the peripherals' region (0x40000000-0x5FFFFFFF, Table 7 p130, which Table 8 p132-135
//                     divides into the peripherals' register spaces) and is not the data register of the request its channel
//                     routes (`Pairs`, below: each request the DMA guard raises or
//                     acknowledges, with its peripheral's base, Table 8 p132-135, and the register's offset by its page).
//                     Silicon paces the stream by that request and moves to or from PAR as written, which no page calls a
//                     fault; the twin pairs each request with its own peripheral and was never checked on another pairing.
//                     unmodeled. A PAR in memory, paced by the request, passes: silicon moves it, and the stock model moves it
//                     exactly. A PAR in a gap Table 8 reserves inside that region is judged too: it is no data register.
//                     A route the platform cannot raise stays the DMA guard's TVI-DMA-REQUEST/ROUTE.
// Enforcing: the first rule that fires refuses the access. Recorded mode (RUL-19): every rule is evaluated and records what it
// finds, and the access goes on to the stock model as if none had fired (the clock still drops it: that is the model).
//
// Served: CxCR's DMAREQ_ID from the guard's copy, the rest of CxCR 0; CSR and RGSR 0 (no overrun flag can be set while the
// synchronisation, event and generator fields are 0 only); CFR and RGCFR 0 (write-only: "Reading this bit returns the reset
// value", §1.2 p101); RGxCR 0 (its fields are 0 only).

using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Miscellaneous;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_DMAMUX : STM32_DMAMUX, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IDisposable
    {
        // The stock entry's constructor arguments, passed through; `mode` (RUL-19) is this guard's.
        public GuardedSTM32H7_DMAMUX(Machine machine, int numberOfInputRequestChannels, int numberOfOutputRequestChannels,
            int numberOfRequestGeneratorChannels, string mode = Records.Enforcing)
            : base(machine, numberOfInputRequestChannels, numberOfOutputRequestChannels, numberOfRequestGeneratorChannels)
        {
            this.machine = machine;
            records = new Records(machine, this, nameof(GuardedSTM32H7_DMAMUX), mode);
            if(numberOfOutputRequestChannels != Channels || numberOfRequestGeneratorChannels != Generators)
            {
                config = numberOfOutputRequestChannels + " channels and " + numberOfRequestGeneratorChannels
                    + " request generators, where DMAMUX1 has 16 and 8 (RM0433 Rev 9 §17.3.1 Table 123 p696)";
            }
            machine.StateChanged += FindRccAtStart;
        }

        public override void Reset()
        {
            lock(sync)
            {
                base.Reset();
                Array.Clear(ids, 0, ids.Length);
            }
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
            lock(sync)
            {
                if(Unloaded(offset, 0, width) || !Clocked())
                {
                    return 0;
                }
                var b = offset & ~3L;
                var shift = (int)(8 * (offset & 3));
                if(Misaligned(offset, 0, width, "read") && !records.Recorded)
                {
                    return 0;
                }
                if(RowOffset(b) < 0)
                {
                    records.Refuse("TVI-DMAMUX-READ", Unmodeled, RegisterAt(b), "read of offset 0x" + b.ToString("X"),
                        "read of a DMAMUX1 offset with no register: the twin cannot say what silicon returns",
                        "RM0433 Rev 9 §17.6.13 Table 133 p716-717", State(), offset, 0, width);
                    if(!records.Recorded)
                    {
                        return 0;
                    }
                }
                return Served(b) >> shift;
            }
        }

        private void Write(long offset, uint value, int width)
        {
            lock(sync)
            {
                if(Unloaded(offset, value, width) || !Clocked())
                {
                    return;
                }
                var b = offset & ~3L;
                var shift = (int)(8 * (offset & 3));
                var mask = width == 32 ? 0xFFFFFFFFu : width == 16 ? 0xFFFFu : 0xFFu;
                var word = width == 32 && shift == 0 ? value : (uint)((Served(b) & ~((ulong)mask << shift)) | ((ulong)(value & mask) << shift));
                var refused = Misaligned(offset, value, width, "write");
                if(!refused || records.Recorded)
                {
                    refused |= RefusedField(b, word, width);
                }
                if(!refused || records.Recorded)
                {
                    refused |= RefusedLive(b, word, width);
                }
                if(refused && !records.Recorded)
                {
                    return;
                }
                Pass(b, word);
            }
        }

        // ---- the DMA guard's one call (ruled Q3) ---------------------------------------------------------------

        // TVI-DMAMUX-PAIR: asked by the DMA guard at a stream's EN, for a stream whose request it raises or acknowledges, with
        // the stream's PAR and its direction, and the access being judged: the DMA_SxCR write that sets EN (its offset in the DMA
        // controller and the value written), which the record locates. True when it refused, so that the DMA guard does not start the
        // stream; false after a would-refuse (recorded mode, RUL-19: the access goes on as if no rule had fired).
        public bool RefusedPair(int channel, uint par, bool toMemory, long crOffset, uint crValue)
        {
            lock(sync)
            {
                if(par < PeripheralsStart || par > PeripheralsEnd)
                {
                    return false;   // memory, paced by the request: legal on silicon, moved exactly by the stock model (Q18 (b))
                }
                var id = ids[channel];
                var p = Pairs.FirstOrDefault(x => x.Id == id && x.ToMemory == toMemory);
                if(p != null && p.Address == par)
                {
                    return false;
                }
                var stream = "DMA" + (channel / 8 + 1) + " stream " + channel % 8;
                var dir = toMemory ? "peripheral-to-memory" : "memory-to-peripheral";
                var condition = p == null
                    ? stream + " enabled " + dir + " through DMAMUX1 channel " + channel + " on request " + id
                        + ", for which this guard holds no data register: the twin cannot say what the stream moves"
                    : stream + " enabled " + dir + " through DMAMUX1 channel " + channel + ", which routes request " + id + " (" + p.Request
                        + "), whose data register is " + p.Register + " at 0x" + p.Address.ToString("X8") + ", with PAR = 0x" + par.ToString("X8")
                        + ": silicon paces the stream by that request and moves " + (toMemory ? "from" : "to") + " PAR as written, which no page calls a fault, "
                        + "but the twin pairs each request only with its own peripheral's data register and cannot say what this stream moves when";
                condition += " (at DMA" + (channel / 8 + 1) + "_S" + channel % 8 + "CR's EN write)";
                records.Refuse("TVI-DMAMUX-PAIR", Unmodeled, "DMAMUX1_C" + channel + "CR",
                    "DMAREQ_ID = " + id + (p == null ? "" : " (" + p.Request + ")") + " with DMA" + (channel / 8 + 1) + "_S" + channel % 8 + "PAR = 0x" + par.ToString("X8"),
                    condition,
                    "RM0433 Rev 9 §17.3.2 Table 124 p697-698" + (p == null ? "" : "; " + p.Citation) + "; Table 7 p130 (the peripherals' region); ruled 2026-11-26T09:31Z Q3, 10:43Z Q18 (b)",
                    State(), crOffset, crValue, 32);
                return !records.Recorded;
            }
        }

        // ---- the rules -----------------------------------------------------------------------------------------

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset & ~3L), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c == null)
            {
                return false;
            }
            records.Refuse("TVI-DMAMUX-CONFIG", Unmodeled, RegisterAt(offset & ~3L), "none",
                "the platform gives this DMAMUX1 guard what it cannot model: " + c + "; every access refuses",
                "TVI-WO-1308.measure.md §2.2, §4; ruled 2026-11-26T09:31Z Q1, Q4", "none", offset, value, width);
            return true;
        }

        private string Config()
        {
            if(config != null)
            {
                return config;
            }
            if(Rcc() == null)
            {
                return "no RCC guard on this machine: DMAMUX1's clock (DMA1EN, DMA2EN, RM0433 Rev 9 p455) and reset (DMA1RST, DMA2RST, p433) are "
                    + "the RCC guard's, so the twin cannot say whether a write takes";
            }
            if(!placed)
            {
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
                if(start != Dmamux1Base)
                {
                    return "bus address 0x" + start.ToString("X") + ", which is not DMAMUX1's (0x40020800, RM0433 Rev 9 Table 8 p134)";
                }
                placed = true;
            }
            return null;
        }

        // DMA1EN or DMA2EN (p455), read from the RCC guard's RCC_AHB1ENR at each access, never kept.
        private bool Clocked()
        {
            return (Rcc().ReadDoubleWord(Ahb1enrOffset) & (Dma1En | Dma2En)) != 0;
        }

        private bool Misaligned(long offset, ulong value, int width, string what)
        {
            if((offset & (width / 8 - 1)) == 0)
            {
                return false;
            }
            records.Refuse("TVI-DMAMUX-WIDTH", Unmodeled, RegisterAt(offset & ~3L), "misaligned " + width + "-bit " + what,
                "a " + width + "-bit DMAMUX1 " + what + " at offset 0x" + offset.ToString("X")
                    + ": RM0433 allows byte, half-word and word accesses with the address aligned with the data size, and states nothing of a misaligned one, so the twin cannot say what silicon does",
                "RM0433 Rev 9 §17.6 p708 (\"The address must be aligned with the data size\")", State(), offset, value, width);
            return true;
        }

        private bool RefusedField(long b, uint word, int width)
        {
            var r = RowOffset(b);
            if(r < 0)
            {
                records.Refuse("TVI-DMAMUX-FIELD", Unmodeled, RegisterAt(b), "write to offset 0x" + b.ToString("X"),
                    "write to a DMAMUX1 offset with no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §17.6.13 Table 133 p716-717", State(), b, word, width);
                return true;
            }
            foreach(var f in Fields)
            {
                if(f.Offset != r || f.Allowed == null || f.Allowed.Contains(f.Of(word)))
                {
                    continue;
                }
                records.Refuse("TVI-DMAMUX-FIELD", Unmodeled, RegisterAt(b), f.Name + " (" + f.Bits + ") = " + f.Format(f.Of(word)),
                    RegisterAt(b) + "." + f.Name + " written " + f.Format(f.Of(word)) + ": the model implements only "
                        + string.Join(", ", f.Allowed.Select(f.Format))
                        + " (" + f.BehavesAs + ")",
                    "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; the allow-list (RUL-17), ruled 2026-11-26T09:31Z Q1-Q4", State(), b, word, width);
                return true;
            }
            return false;
        }

        // TVI-DMAMUX-LIVE (ruled Q3): a route changed under an enabled stream.
        private bool RefusedLive(long b, uint word, int width)
        {
            if(b >= CxcrEnd)
            {
                return false;
            }
            var ch = (int)(b / 4);
            var id = word & IdMask;
            if(id == ids[ch])
            {
                return false;
            }
            var state = StreamState(ch);
            if(state == 0)
            {
                return false;
            }
            var stream = "DMA" + (ch / 8 + 1) + " stream " + ch % 8;
            records.Refuse("TVI-DMAMUX-LIVE", Unmodeled, RegisterAt(b), "DMAREQ_ID " + ids[ch] + " -> " + id + " (" + stream + " " + (state == 1 ? "running" : "completed, EN still set") + ")",
                RegisterAt(b) + ".DMAREQ_ID rewritten from " + ids[ch] + " to " + id + " while " + stream + ", which channel " + ch + " serves, "
                    + (state == 1 ? "is running" : "has been completed by the twin with EN still set")
                    + ": RM0433 configures the route before the stream's EN is set and states nothing of a change while the stream is active, so the twin cannot say which request paces it",
                "RM0433 Rev 9 §17.4.2 p703 (the channel's configuration order); §17.6.1 p708-709 (DMAREQ_ID); ruled 2026-11-26T09:31Z Q3",
                State(), b, word, width);
            return true;
        }

        // ---- the registers -------------------------------------------------------------------------------------

        private uint Served(long b)
        {
            return b < CxcrEnd ? ids[b / 4] : 0;
        }

        private void Pass(long b, uint word)
        {
            if(b == CsrOffset || b == RgsrOffset)
            {
                return;   // read-only: a write is ignored and changes nothing (RUL-18 as amended)
            }
            if(b < CxcrEnd)
            {
                ids[b / 4] = word & IdMask;
            }
            base.WriteDoubleWord(b, word);
        }

        // The offset of the Fields rows a register's word is judged by: every CxCR by C0's (0x000), every RGxCR by RG0's (0x100);
        // -1 for an offset with no register.
        private static long RowOffset(long b)
        {
            if(b >= 0 && b < CxcrEnd)
            {
                return 0;
            }
            if(b >= RgcrOffset && b < RgcrEnd)
            {
                return RgcrOffset;
            }
            return b == CsrOffset || b == CfrOffset || b == RgsrOffset || b == RgcfrOffset ? b : -1;
        }

        private static string RegisterAt(long b)
        {
            if(b >= 0 && b < CxcrEnd)
            {
                return "DMAMUX1_C" + b / 4 + "CR";
            }
            if(b >= RgcrOffset && b < RgcrEnd)
            {
                return "DMAMUX1_RG" + (b - RgcrOffset) / 4 + "CR";
            }
            var f = Fields.FirstOrDefault(x => x.Offset == b);
            return f != null ? f.Register : "none";
        }

        private string State()
        {
            var en = rcc == null ? "unknown" : (rcc.ReadDoubleWord(Ahb1enrOffset) & 1) + "/" + (rcc.ReadDoubleWord(Ahb1enrOffset) >> 1 & 1);
            return "DMAREQ_ID C0-C15 = " + string.Join(",", ids) + "; RCC_AHB1ENR.DMA1EN/DMA2EN = " + en;
        }

        // ---- the platform --------------------------------------------------------------------------------------

        // The state of the DMA stream DMAMUX1 channel c serves (0 off, 1 running, 2 completed with EN set): DMA1's streams are
        // channels 0-7, DMA2's 8-15 (§17.3.2 p696), from the DMA guard at that controller; off when it has none, or has not yet
        // been reached (its ChannelBase is set at its first access: a stream never written is off).
        private int StreamState(int c)
        {
            var k = c / 8;
            if(dma[k] == null)
            {
                dma[k] = machine.GetPeripheralsOfType<GuardedSTM32DMA>().FirstOrDefault(g => g.ChannelBase == 8 * k);
            }
            return dma[k] == null ? 0 : dma[k].StreamState(c % 8);
        }

        // The RCC guard, and its one event (`Written`, TVI-WO-1304): subscribed when the machine first starts, or at this guard's
        // first access, whichever comes first.
        private GuardedSTM32H7_RCC Rcc()
        {
            if(rcc == null)
            {
                rcc = machine.GetPeripheralsOfType<GuardedSTM32H7_RCC>().FirstOrDefault();
                if(rcc != null)
                {
                    rcc.Written += OnRccWritten;
                }
            }
            return rcc;
        }

        private void FindRccAtStart(IMachine m, MachineStateChangedEventArgs args)
        {
            if(args.CurrentState == MachineStateChangedEventArgs.State.Started)
            {
                ((Machine)m).StateChanged -= FindRccAtStart;
                lock(sync)
                {
                    Rcc();
                }
            }
        }

        // DMA1RST or DMA2RST written 1 resets DMAMUX1 (p433), as the stock RCC resets a registered peripheral at each write of 1.
        private void OnRccWritten(long offset, uint value)
        {
            if(offset == Ahb1rstrOffset && (value & (Dma1Rst | Dma2Rst)) != 0)
            {
                Reset();
            }
        }

        private const string Unmodeled = "unmodeled";
        private const int Channels = 16;
        private const int Generators = 8;
        private const ulong Dmamux1Base = 0x40020800;
        private const uint PeripheralsStart = 0x40000000;   // Table 7 p130: "Peripherals (refer to Table 8)"
        private const uint PeripheralsEnd = 0x5FFFFFFF;
        private const long CxcrEnd = 0x040;
        private const long CsrOffset = 0x080;
        private const long CfrOffset = 0x084;
        private const long RgcrOffset = 0x100;
        private const long RgcrEnd = 0x120;
        private const long RgsrOffset = 0x140;
        private const long RgcfrOffset = 0x144;
        private const uint IdMask = 0x7F;
        private const long Ahb1rstrOffset = 0x080;   // RCC_AHB1RSTR (§8.7.29 p432)
        private const long Ahb1enrOffset = 0x0D8;    // RCC_AHB1ENR (§8.7.41 p454)
        private const uint Dma1En = 1u << 0;         // p455
        private const uint Dma2En = 1u << 1;         // p455
        private const uint Dma1Rst = 1u << 0;        // p433
        private const uint Dma2Rst = 1u << 1;        // p433

        private readonly Machine machine;
        private readonly Records records;
        private readonly string config;
        private readonly object sync = new object();
        // DMAREQ_ID per channel, as last written and passed (the stock model holds the same).
        private readonly uint[] ids = new uint[Channels];
        private readonly GuardedSTM32DMA[] dma = new GuardedSTM32DMA[2];
        private GuardedSTM32H7_RCC rcc;
        private bool placed;

        // ---- the pairs (ruled Q3): each request the DMA guard raises or acknowledges (its RaisedToMemory, RaisedFromMemory), the
        // data register its peripheral moves through. tests/1308/dmamux.sh `pairs` holds the two lists equal.

        private sealed class Pair
        {
            public Pair(uint id, string request, bool toMemory, uint address, string register, string citation)
            {
                Id = id;
                Request = request;
                ToMemory = toMemory;
                Address = address;
                Register = register;
                Citation = citation;
            }

            public uint Id { get; }
            public string Request { get; }
            public bool ToMemory { get; }
            public uint Address { get; }
            public string Register { get; }
            public string Citation { get; }
        }

        private static readonly Pair[] Pairs =
        {
            new Pair(9, "adc1_dma", true, 0x40022040, "ADC1_DR", "Table 124 p697; Table 8 p134 (ADC1-ADC2 0x40022000), Table 223 p1043 (ADC1 at 0x000); §25.6.15 p1027 (ADC_DR 0x40)"),
            new Pair(37, "spi1_rx_dma", true, 0x40013030, "SPI1_RXDR", "Table 124 p698; Table 8 p134 (SPI1 0x40013000); §50.11.9 p2228 (SPI_RXDR 0x30)"),
            new Pair(38, "spi1_tx_dma", false, 0x40013020, "SPI1_TXDR", "Table 124 p698; Table 8 p134 (SPI1 0x40013000); §50.11.8 p2227 (SPI_TXDR 0x20)"),
            new Pair(39, "spi2_rx_dma", true, 0x40003830, "SPI2_RXDR", "Table 124 p698; Table 8 p135 (SPI2 0x40003800); §50.11.9 p2228 (SPI_RXDR 0x30)"),
            new Pair(40, "spi2_tx_dma", false, 0x40003820, "SPI2_TXDR", "Table 124 p698; Table 8 p135 (SPI2 0x40003800); §50.11.8 p2227 (SPI_TXDR 0x20)"),
            new Pair(41, "usart1_rx_dma", true, 0x40011024, "USART1_RDR", "Table 124 p698; Table 8 p134 (USART1 0x40011000); §48.8.12 p2098 (USART_RDR 0x24)"),
            new Pair(42, "usart1_tx_dma", false, 0x40011028, "USART1_TDR", "Table 124 p698; Table 8 p134 (USART1 0x40011000); §48.8.13 p2098 (USART_TDR 0x28)"),
            new Pair(43, "usart2_rx_dma", true, 0x40004424, "USART2_RDR", "Table 124 p698; Table 8 p135 (USART2 0x40004400); §48.8.12 p2098 (USART_RDR 0x24)"),
            new Pair(45, "usart3_rx_dma", true, 0x40004824, "USART3_RDR", "Table 124 p697; Table 8 p135 (USART3 0x40004800); §48.8.12 p2098 (USART_RDR 0x24)"),
            new Pair(46, "usart3_tx_dma", false, 0x40004828, "USART3_TDR", "Table 124 p697; Table 8 p135 (USART3 0x40004800); §48.8.13 p2098 (USART_TDR 0x28)"),
            new Pair(77, "cryp_out_dma", true, 0x4802100C, "CRYP_DOUT", "Table 124 p697; Table 8 p134 (CRYPTO 0x48021000); §35.7.4 p1319 (CRYP_DOUT 0x0C)"),
            new Pair(83, "spi4_rx_dma", true, 0x40013430, "SPI4_RXDR", "Table 124 p698; Table 8 p134 (SPI4 0x40013400); §50.11.9 p2228 (SPI_RXDR 0x30)"),
            new Pair(115, "adc3_dma", true, 0x58026040, "ADC3_DR", "Table 124 p697; Table 8 p132 (ADC3 0x58026000); §25.6.15 p1027 (ADC_DR 0x40)"),
        };

        // The stock model's register enum, copied so the access log keeps its names (measure.md §4, F).
        private enum Registers
        {
            RequestLineMultiplexerChannelConfiguration      = 0x0,      // CxCR
            RequestLineMultiplexerInterruptStatus           = 0x80,     // CSR
            RequestLineMultiplexerInterruptClear            = 0x84,     // CFR
            RequestGeneratorChannelConfiguration            = 0x100,    // RGxCR
            RequestGeneratorInterruptStatus                 = 0x140,    // RGSR
            RequestGeneratorInterruptClear                  = 0x144,    // RGCFR
        }

        // ---- the allow-list (RUL-16, RUL-17): one row per field, every bit 31:0 of every register ---------------
        // Typed from RM0433 Rev 9 §17.6 (pp. 708-714). CxCR at C0's offset and RGxCR at RG0's: the guard applies each row to all
        // sixteen channels and all eight generators (RowOffset).

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
                Mask = Msb - Lsb == 31 ? uint.MaxValue : ((1u << (Msb - Lsb + 1)) - 1) << Lsb;
            }

            public uint Of(uint value)
            {
                return (value & Mask) >> Lsb;
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
            public uint Mask { get; }
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

        private static readonly Row[] Fields =
        {
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "Res31_27", 31, 27, Zero, "reserved", "reserved: must be kept at reset value", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "SYNC_ID", 26, 24, Zero, "tag", "0 only: synchronisation is not modeled (the stock model's tag is bits 25:24, narrower than RM0433's 26:24; moot while 0 only) (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "NBREQ", 23, 19, Zero, "tag", "0 only: synchronisation and event generation are not modeled, so no request count (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "SPOL", 18, 17, Zero, "tag", "0 only: 00 is no event, no synchronisation (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "SE", 16, 16, Zero, "tag", "0 only: synchronisation is not modeled (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "Res15_10", 15, 10, Zero, "reserved", "reserved: must be kept at reset value", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "EGE", 9, 9, Zero, "tag", "0 only: event generation is not modeled (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "SOIE", 8, 8, Zero, "tag", "0 only: no synchronisation overrun can occur while SE is 0 (ruled 2026-11-26T09:31Z)", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 708),
            R("DMAMUX1_CxCR", 0x000, "§17.6.1", "DMAREQ_ID", 6, 0, Any, "modeled by the guard", "the request routed to channel x: kept in the guard's copy, served on read and passed to the stock model, which raises the channel's line for that input; judged at the stream's EN against its PAR (TVI-DMAMUX-PAIR) and while it runs (TVI-DMAMUX-LIVE); a reserved id (54, 116-127, Table 124 p697-698) is taken, and a stream started on it is the DMA guard's TVI-DMA-ROUTE (ruled 2026-11-26T09:31Z Q3, 10:43Z Q18 (a))", 709),
            R("DMAMUX1_CSR", 0x080, "§17.6.3", "Res31_16", 31, 16, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 710),
            R("DMAMUX1_CSR", 0x080, "§17.6.3", "SOF", 15, 0, Any, "read-only", "read-only: a write is ignored; reads 0: no synchronisation overrun can occur while SE and EGE are 0 only", 710),
            R("DMAMUX1_CFR", 0x084, "§17.6.5", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 711),
            R("DMAMUX1_CFR", 0x084, "§17.6.5", "CSOF", 15, 0, Any, "modeled by the guard", "write 1 clears SOFx (p711), passed to the stock model; no SOF can be set while SE and EGE are 0 only, so it changes nothing; reads 0 (write-only, p101)", 711),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "Res31_24", 31, 24, Zero, "reserved", "reserved: must be kept at reset value", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "GNBREQ", 23, 19, Zero, "tag", "0 only: the request generators are not modeled (ruled 2026-11-26T09:31Z)", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "GPOL", 18, 17, Zero, "tag", "0 only: the request generators are not modeled (ruled 2026-11-26T09:31Z)", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "GE", 16, 16, Zero, "tag", "0 only: the request generators are not modeled (ruled 2026-11-26T09:31Z)", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "Res15_9", 15, 9, Zero, "reserved", "reserved: must be kept at reset value", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "OIE", 8, 8, Zero, "tag", "0 only: the request generators are not modeled (ruled 2026-11-26T09:31Z)", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "Res7_3", 7, 3, Zero, "reserved", "reserved: must be kept at reset value", 712),
            R("DMAMUX1_RGxCR", 0x100, "§17.6.7", "SIG_ID", 2, 0, Zero, "tag", "0 only: the request generators are not modeled (ruled 2026-11-26T09:31Z)", 712),
            R("DMAMUX1_RGSR", 0x140, "§17.6.9", "Res31_16", 31, 16, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 714),
            R("DMAMUX1_RGSR", 0x140, "§17.6.9", "OF", 15, 0, Any, "read-only", "read-only: a write is ignored; reads 0: no trigger overrun can occur while every generator's fields are 0 only", 714),
            R("DMAMUX1_RGCFR", 0x144, "§17.6.11", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 714),
            R("DMAMUX1_RGCFR", 0x144, "§17.6.11", "COF", 15, 0, Any, "modeled by the guard", "write 1 clears OFx (p714), passed to the stock model; no OF can be set while every generator's fields are 0 only, so it changes nothing; reads 0 (write-only, p101)", 714),
        };
    }
}
