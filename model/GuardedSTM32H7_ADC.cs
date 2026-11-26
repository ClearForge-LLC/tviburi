// Tviburi's guard over Renode's STM32H7_ADC for the STM32H753's ADC1 (the ADC1/ADC2 pair's entry, adcM1S2) and ADC3
// (TVI-WO-1307): a converter that starts, paces and stops as silicon does, so the armed run's analog stream is judged, not
// inherited. The measurement, every page, and the rulings (2026-11-25T21:17Z, Q1-Q14): docs/work-orders/TVI-WO-1307.measure.md
// (RM0433 Rev 9 §25; DS12117 Rev 10 Table 76 p166-167, rev Y, and Table 173 p272-274, rev V).
//
// Insertion point (RUL-07; ruled Q1): STM32H7_ADC is not sealed, but a typed redeclaration of `adc3` or `adcM1S2` is refused
// (E02: the stock entries come from the `using` file; measure.md §2.3, F), so each entry is cancelled and restated
// (model/stm32h7-adc-restate.repl) as this subclass, which re-implements the bus interfaces. The stock register map is the
// generic STM32 one, not H7's (measure.md §2.3), so this guard owns every register (its copies) and the regular conversion
// (pacing, EOC/EOS/OVR, DR, the DMA request, the interrupt); the stock part keeps only its channel sources, so Renode's
// `SetVoltage(microvolts, channel)` still declares a level (Q10). The stock sampling thread is never started. The base's
// nested `Registers` enum keeps the access log's register names (measured identical, F).
//
// Rules, in the order they are evaluated. A refused access changes nothing.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-ADC-CONFIG    the platform gives this guard what it cannot model: no `revision` ("V" or "Y"; ruled Q2: never defaulted,
//                     and refused at load, by name), a `levels` it cannot read, a name other than adcM1S2 or adc3, or no RCC
//                     guard holding it under its alias (adc12, adc3). unmodeled; every access refuses.
//   TVI-ADC-WIDTH     a byte or half-word access, or a misaligned one: "Word (32-bit) accesses" (p926). unmodeled. Acknowledged by
//                     name (ruled Q14): ArduPilot's pattern only, a half-word read of ADC1's DR (adcM1S2 0x40) with DMNGT = 11,
//                     its DMA's 16-bit peripheral reads; it returns DR's low half and counts as the read (it clears EOC).
//   TVI-ADC-READ      a read of an offset with no register, ADC2's window (0x100-0x1FC: the slave is not modeled), or the
//                     dual-mode data registers (CDR, CDR2). unmodeled.
//   TVI-ADC-FIELD     the allow-list (`Fields`): a field given a value the model does not implement. unmodeled.
//   TVI-ADC-STATE     a write RM0433 allows "only when" a state that does not hold (p934 and each bit's own row; ruled Q5):
//                     "There is no hardware protection to prevent these forbidden write accesses and ADC behavior may become in
//                     an unknown state" (p934). unmodeled: the text gives no outcome. A value written that changes nothing passes
//                     (RUL-18 as amended); a set-only bit written 1 while it is 1 is no command. Acknowledged by name (ruled Q6):
//                     ArduPilot's pattern only, ADC3's CCR changing VREFEN, TSEN or VBATEN alone with ADEN = 1 and ADSTART = 0
//                     (p1038).
//   TVI-ADC-KERNEL    ADCAL, ADEN or ADSTART with CKMODE = 00 and no running kernel clock, by the RCC guard's query (adc12, adc3;
//                     ADCSEL p419, granted Q3): predicted-fault, naming the ADC, the selector and p920; unmodeled where the query
//                     has no rule. With CKMODE != 00 the bus clock runs, which the RCC guard's bus lock already guarantees.
//   TVI-ADC-RATE      ADCAL, ADEN or ADSTART when f_ADC cannot be computed from declared values (the clock's source has no rule
//                     here), RES a code the declared revision reserves (p1016), f_ADC above the revision's maximum (DS12117), or
//                     BOOST not the one p1009 (rev V) / p1005 (rev Y) gives for f_ADC. unmodeled. Acknowledged by name (ruled
//                     Q7): ArduPilot's pattern only, rev V, BOOST = 01 where f_ADC asks for 10 (ChibiOS's hal_adc_lld.h #if order).
//   TVI-ADC-PCSEL     ADSTART with a sequenced channel whose PCSEL bit is 0: "the ADC conversion result with this channel shows
//                     wrong result" (p1021, p935). unmodeled.
//   TVI-ADC-DMNGT     ADSTART with DMNGT = 10 (DFSDM: no DFSDM model), or 01 (one-shot) with no stream of a DMA guard selecting
//                     this ADC's request (the twin cannot see the DMA's last transfer, p964). unmodeled.
//   TVI-ADC-LEVEL     DR read (by the CPU or a DMA) holding a conversion of a channel no `levels` entry and no SetVoltage declared
//                     (ruled Q10): never 0 V by default. unmodeled. A conversion nobody reads makes no record.
// Enforcing and recorded mode (RUL-19) are model/Refusal.cs's contract, but a recorded start whose rate cannot be computed converts nothing.
//
// Modeled, cited:
//   The clock (p920, p1039; ruled Q2, Q4): CKMODE = 00, the kernel clock ADCSEL selects (pll2_p_ck, pll3_r_ck, per_ck, p419) /
//     PRESC; CKMODE 01/10/11, rev Y adc_hclk /1/2/4, rev V adc_sclk (sys_ck, or sys_ck/2 with HPRE != 0, p920) /1/2/4; rev V
//     divides by 2 again ("Whatever CKMODE[1:0] settings, an additional divider factor of 2", p1039).
//   The conversion (p1019-1020, Table 214 p955): SMPx + the SAR time of RES, per channel through SQRx; with CONT the sequence
//     restarts at once (p937), without it ADSTART clears at EOS (p1007). EOC per channel, EOS per sequence (p955); EOSMP is set
//     with EOC (not the SAR time earlier: EOSMPIE is 0 only). The code: round(V / VREF+ x (2^N - 1)) (p996), ties up, VREF+ the
//     entry's referenceVoltage, right-aligned (CFGR2 0 only).
//   DMNGT (p964-965, p1016; ruled Q8): 00 DR only; 01 and 11 a DMA request per conversion; 01 stops at the DMA's last transfer
//     (seen through the serving stream's StreamState, read-only), DR frozen and the sequence stopped.
//   Overrun (ruled Q9): OVR when a conversion ends with EOC still set (p999); OVRMOD 0 keeps DR, 1 overwrites it (p1015); DMA
//     requests blocked until OVR is cleared (p964). The eight-stage FIFO (p962) is acknowledged by name as not modeled.
//   The enable sequence (ruled Q5; DS12117 p167, p273): DEEPPWD resets to 1 (p1003); LDORDY (rev V) 10 us after ADVREGEN
//     (t_ADCVREG_STUP max), and the regulator ready for ADCAL and ADEN then on both revisions; ADCAL stays 1 for t_CAL = 165,010
//     (ADCALLIN = 1) or t_OFF_CAL = 1,280 (ADCALLIN = 0) ADC clock cycles; LINCALRDYW1-6 set by a calibration with ADCALLIN
//     (p931); ADRDY t_STAB after ADEN, one conversion cycle, read as 1.5 + the SAR time (inferred: "1 conversion cycle").
//   ADCx_CCR is per entry (adcM1S2 holds ADC1/ADC2's, adc3 its own); CSR reads the master's flags.
//   ADSTART = 1 with CKMODE = 00 is this ADC running, for the RCC guard's TVI-RCC-CLOCK (IRccWatched; granted Q3).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Analog;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.DMA;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities.RESD;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_ADC : STM32H7_ADC, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IRccWatched, IDisposable
    {
        // The stock entry's constructor arguments, passed through; `mode` (RUL-19), `revision` ("V" or "Y", ruled Q2) and `levels`
        // (ruled Q10: "<channel>:<volts at the pin> ...", e.g. "10:1.40") are this guard's.
        public GuardedSTM32H7_ADC(IMachine machine, double referenceVoltage, uint externalEventFrequency, int dmaChannel = 0,
            IDMA dmaPeripheral = null, string mode = Records.Enforcing, string revision = null, string levels = null)
            : base(machine, referenceVoltage, externalEventFrequency, dmaChannel, dmaPeripheral)
        {
            host = machine;
            vref = referenceVoltage;
            dma = dmaPeripheral;
            request = dmaChannel;
            Revision = revision;
            records = new Records(machine, this, nameof(GuardedSTM32H7_ADC), mode,
                new[] { new KeyValuePair<string, string>("revision", revision ?? "none") });
            config = revision == "V" || revision == "Y" ? null
                : "no revision declared (`revision: \"V\"` or `\"Y\"`): the clock, BOOST, LDORDY and RES depend on it (p920, p1039, p1005, p1009, p1016)"
                    + (revision == null ? "" : ", and " + Quote(revision) + " is neither");
            config = config ?? ParseLevels(levels);
            machine.PeripheralsChanged += OnPeripheralsChanged;
            ResetCopies();
        }

        public string Revision { get; }

        // IRccWatched (granted Q3): a conversion running on the kernel clock ADCSEL selects.
        public bool IsRunning => (cr & AdstartBit) != 0 && Ckmode == 0;
        public string RunningName => "ADSTART";
        public string KernelCitation => "§25.4.3 p920 (\"adc_ker_ck_input ... To select this scheme, CKMODE[1:0] bits of the ADCx_CCR register must be reset\")";

        public new void Reset()
        {
            lock(sync)
            {
                base.Reset();
                ResetCopies();
                IRQ.Set(false);
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

        public new ushort ReadWord(long offset)
        {
            return (ushort)Read(offset, 16);
        }

        public new void WriteWord(long offset, ushort value)
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
                if(Unloaded(offset, 0, width))
                {
                    return 0;
                }
                var halfDr = width == 16 && offset == DrOffset && Name == "adcM1S2" && Dmngt == 3;   // ruled Q14, ArduPilot's DMA
                if(!halfDr && Narrow(offset, 0, width, "read") && !records.Recorded)
                {
                    return 0;
                }
                var b = offset & ~3L;
                if(!Readable(b))
                {
                    records.Refuse("TVI-ADC-READ", Unmodeled, RegisterAt(b), "read of offset 0x" + b.ToString("X"),
                        "read of an ADC offset " + Unread(b) + ": the twin cannot say what silicon returns",
                        "RM0433 Rev 9 §25.6.31 Table 224 p1043-1046, §25.7 Table 225 p1046", State(), b, 0, width);
                    if(!records.Recorded)
                    {
                        return 0;
                    }
                }
                if(b == DrOffset)
                {
                    return ReadDr(width) >> (int)(8 * (offset & 3));
                }
                return Served(b) >> (int)(8 * (offset & 3));
            }
        }

        private void Write(long offset, uint value, int width)
        {
            lock(sync)
            {
                if(Unloaded(offset, value, width))
                {
                    return;
                }
                var refused = Narrow(offset, value, width, "write");
                var b = offset & ~3L;
                var word = width == 32 ? value : (Served(b) & ~(Mask(width) << (int)(8 * (offset & 3)))) | ((value & Mask(width)) << (int)(8 * (offset & 3)));
                if(!refused || records.Recorded)
                {
                    if(RegisterAt(b) == "none")
                    {
                        records.Refuse("TVI-ADC-FIELD", Unmodeled, RegisterAt(b), "write to offset 0x" + b.ToString("X"),
                            "write to an ADC offset " + Unread(b) + ": the twin cannot say what silicon does",
                            "RM0433 Rev 9 §25.6.31 Table 224 p1043-1046, §25.7 Table 225 p1046", State(), b, word, width);
                        refused = true;
                    }
                    else
                    {
                        refused |= RefusedField(b, word, width);
                    }
                }
                if(!refused || records.Recorded)
                {
                    refused |= RefusedState(b, word, width);
                }
                var commands = b == CrOffset ? Commands(word) : 0;
                if((commands & (AdcalBit | AdenBit | AdstartBit)) != 0 && (!refused || records.Recorded))
                {
                    refused |= RefusedClock(word, commands);
                }
                if((commands & AdstartBit) != 0 && (!refused || records.Recorded))
                {
                    refused |= RefusedStart(word);
                }
                if(refused && !records.Recorded)
                {
                    return;
                }
                Pass(b, word);
            }
        }

        // ---- the registers -------------------------------------------------------------------------------------

        private uint Served(long b)
        {
            switch(b)
            {
            case IsrOffset: return isr | (Revision == "V" && ldoReady ? LdordyBit : 0);
            case CrOffset: return cr;
            case CsrOffset: return isr & 0x7FF;   // the master's flags (p1036); the slave's are 0: ADC2 is not modeled
            default: return regs.TryGetValue(b, out var v) ? v : 0;
            }
        }

        private void Pass(long b, uint word)
        {
            switch(b)
            {
            case IsrOffset:
                isr &= ~(word & IsrClearable);   // rc_w1 (p998-1000); LDORDY is read-only (rev V) or reserved (rev Y)
                UpdateIrq();
                return;
            case CrOffset:
                PassCr(word);
                return;
            case DrOffset:
            case CsrOffset:
            case CdrOffset:
            case Cdr2Offset:
                return;   // read-only: a write is ignored and changes nothing (RUL-18 as amended)
            default:
                if(regs.ContainsKey(b))
                {
                    regs[b] = word & Defined(b);
                    if(b == IerOffset)
                    {
                        UpdateIrq();
                    }
                }
                return;
            }
        }

        private void PassCr(uint word)
        {
            var before = cr;
            var commands = Commands(word);
            // The stored bits: DEEPPWD, ADVREGEN, ADCALDIF, ADCALLIN, BOOST, LINCALRDYW as written (a toggle refused or recorded).
            cr = (cr & CrCommands) | (word & CrStored);
            if((before & AdvregenBit) != 0 && (cr & AdvregenBit) == 0 || (cr & DeeppwdBit) != 0)
            {
                ldoReady = false;
                ldoEpoch++;
            }
            if((before & AdvregenBit) == 0 && (cr & AdvregenBit) != 0 && (cr & DeeppwdBit) == 0)
            {
                var e = ++ldoEpoch;
                host.ScheduleAction(TimeInterval.FromMicroseconds(VregStartupMicros), _ => { lock(sync) { if(e == ldoEpoch) ldoReady = true; } });
            }
            if((commands & AdcalBit) != 0)
            {
                cr |= AdcalBit;
                var f = AdcHz(out var _);
                if(f != null)
                {
                    var lin = (cr & AdcallinBit) != 0;
                    var e = ++calEpoch;
                    Schedule((lin ? CalCycles : OffCalCycles) / f.Value, () =>
                    {
                        if(e != calEpoch)
                        {
                            return;
                        }
                        cr &= ~AdcalBit;
                        if(lin)
                        {
                            cr |= LincalMask;   // "set when the calibration is complete" (p931)
                        }
                    });
                }
            }
            if((commands & AdenBit) != 0)
            {
                cr |= AdenBit;
                var f = AdcHz(out var _);
                if(f != null)
                {
                    var e = ++rdyEpoch;
                    Schedule((1.5 + Sar(Res)) / f.Value, () => { if(e == rdyEpoch) { isr |= AdrdyBit; UpdateIrq(); } });
                }
            }
            if((commands & AddisBit) != 0)
            {
                cr &= ~(AdenBit | AdstartBit);   // "cleared by hardware once the ADC is effectively disabled" (p1006)
                rdyEpoch++;
                StopConversions();
            }
            if((commands & AdstpBit) != 0)
            {
                cr &= ~AdstartBit;   // ADSTP and ADSTART cleared together (p939); the sequence aborted, DR not updated
                StopConversions();
            }
            if((commands & AdstartBit) != 0)
            {
                cr |= AdstartBit;
                StartConversions();
            }
        }

        // The command bits a CR write sets: a set-only bit written 1 while it is 0 (written 1 while 1, or 0, it is no command).
        private uint Commands(uint word)
        {
            return word & ~cr & CrCommands;
        }

        // ---- the conversion engine -----------------------------------------------------------------------------

        private void StartConversions()
        {
            StopConversions();
            oneShotDone = false;
            index = 0;
            var f = AdcHz(out var _);
            if(f == null || RejectedRes(Res))
            {
                return;   // refused (TVI-ADC-RATE) or, recorded, a start that cannot be paced: nothing converts
            }
            hz = f.Value;
            rateStale = false;
            var e = convEpoch;
            ScheduleNext(e);
        }

        private void StopConversions()
        {
            convEpoch++;
        }

        private void ScheduleNext(long e)
        {
            if(rateStale)
            {
                // WO §5: a conversion on a clock silicon would not give. An RCC write under running conversions may change f_ADC (a
                // divider, HPRE for adc_sclk): the next conversion takes the clock as it now is, or the twin says it cannot.
                rateStale = false;
                var f = AdcHz(out var why);
                var problem = f == null ? "a clock the twin cannot compute from declared values: " + why : RateProblem(f.Value, cr, true, out var _);
                if(problem != null)
                {
                    records.Refuse("TVI-ADC-RATE", Unmodeled, "ADC_CR", "ADSTART = 1",
                        Name + ": its clock changed under running conversions (an RCC write) to " + problem,
                        "RM0433 Rev 9 §25.4.3 p920-922, §25.7.2 p1039; ruled 2026-11-25T21:17Z Q4, Q7", State(), CrOffset, cr, 0);
                    StopConversions();
                    return;
                }
                hz = f.Value;
            }
            var ch = Channel(index);
            Schedule((Smp(ch) + Sar(Res)) / hz, () => Converted(e));
        }

        private void Converted(long e)
        {
            if(e != convEpoch || (cr & AdstartBit) == 0 || oneShotDone)
            {
                return;
            }
            var ch = Channel(index);
            var bits = ResBits(Res);
            var declared = Level(ch, out var microvolts);
            var top = (1UL << bits) - 1;
            var code = (uint)Math.Min(top, (2UL * microvolts * top + (ulong)Math.Round(vref * 1e6)) / (2UL * (ulong)Math.Round(vref * 1e6)));
            var overrun = (isr & EocBit) != 0;
            if(overrun)
            {
                isr |= OvrBit;   // "a new conversion has completed while the EOC flag was already set" (p999)
            }
            if(!overrun || (regs[CfgrOffset] & OvrmodBit) != 0)
            {
                dr = code;
                drChannel = ch;
                drDeclared = declared;
            }
            isr |= EocBit | EosmpBit;
            var last = index >= SequenceLength - 1;
            if(last)
            {
                isr |= EosBit;
            }
            UpdateIrq();
            var dmngt = Dmngt;
            if((dmngt == 1 || dmngt == 3) && (isr & OvrBit) == 0)
            {
                var before = dmngt == 1 ? ServingState() : -1;
                dma?.RequestTransfer(request);
                if(dmngt == 1 && before == RunningStream && ServingState() != RunningStream)
                {
                    oneShotDone = true;   // the DMA's last transfer (p964-965): no new request; DR frozen; the sequence stopped
                    StopConversions();
                    return;
                }
            }
            index = last ? 0 : index + 1;
            if(last && (regs[CfgrOffset] & ContBit) == 0)
            {
                cr &= ~AdstartBit;   // single mode, software trigger: ADSTART cleared at EOS (p1007)
                return;
            }
            ScheduleNext(e);
        }

        private uint ReadDr(int width)
        {
            if(drChannel >= 0 && !drDeclared)
            {
                records.Refuse("TVI-ADC-LEVEL", Unmodeled, "ADC_DR", "channel " + drChannel,
                    Name + " DR read holding a conversion of channel " + drChannel + (Name == "adc3" && drChannel >= 17 ? ", an internal channel converted with its enable (VBATEN, TSEN, VREFEN: p1038) 0 or" : ",") + " whose level no `levels` entry and no SetVoltage declared: the twin will not invent one (never 0 V by default)",
                    "TVI-WO-1307 Q10 (ruled 2026-11-25T21:17Z); RM0433 Rev 9 §25.4.30 p996", State(), DrOffset, 0, width);
                if(!records.Recorded)
                {
                    return 0;
                }
            }
            isr &= ~EocBit;   // "cleared ... by reading the ADC_DR register" (p1000)
            UpdateIrq();
            return dr;
        }

        private void UpdateIrq()
        {
            var ier = regs[IerOffset];
            IRQ.Set((isr & ier & (AdrdyBit | EocBit | EosBit | OvrBit)) != 0);
        }

        private void Schedule(double seconds, Action action)
        {
            host.ScheduleAction(TimeInterval.FromNanoseconds((ulong)Math.Ceiling(seconds * 1e9)), _ => { lock(sync) { action(); } });
        }

        // The serving stream's state (GuardedSTM32DMA.StreamState: 0 off, 1 running, 2 completed), by the DMAMUX1 channel whose
        // DMAREQ_ID is this ADC's request (p696: DMA1's streams are channels 0-7, DMA2's 8-15); -1 when no DMA guard serves it.
        private int ServingState()
        {
            return Serving(out var g, out var s) ? g.StreamState(s) : -1;
        }

        // The serving stream's SxCR.CIRC (bit 8, p664), read through the DMA guard's bus interface: read-only (granted Q8).
        private bool ServingCircular()
        {
            return Serving(out var g, out var s) && (((IDoubleWordPeripheral)g).ReadDoubleWord(0x10 + 0x18 * s) & (1u << 8)) != 0;
        }

        private bool Serving(out GuardedSTM32DMA g, out int s)
        {
            g = null;
            s = -1;
            if(!(dma is IDoubleWordPeripheral mux))
            {
                return false;
            }
            for(var c = 0; c < 16; c++)
            {
                if((mux.ReadDoubleWord(4 * c) & 0x7F) != request)
                {
                    continue;
                }
                g = host.GetPeripheralsOfType<GuardedSTM32DMA>().FirstOrDefault(d => d.ChannelBase == c - c % 8);
                s = c % 8;
                return g != null;
            }
            return false;
        }

        private bool Level(int ch, out ulong microvolts)
        {
            // WO §5: a sample the twin invents. ADC3's internal channels convert only with their source enabled (VBAT/4 17 with VBATEN,
            // VSENSE 18 with TSEN, VREFINT 19 with VREFEN; p920, p1038): with it off, no declared level stands for what silicon converts.
            if(Name == "adc3" && ch >= 17 && (regs[CcrOffset] & (1u << (41 - ch))) == 0)
            {
                microvolts = 0;
                return false;
            }
            if(levels.TryGetValue(ch, out var v))
            {
                microvolts = v;
                return true;
            }
            microvolts = 0;
            if(ch >= ADCChannelCount || !ADCContainer.TryGetByAddress(ch, out var source))
            {
                return false;
            }
            microvolts = source.Sample.Voltage;
            // Declared by SetVoltage (the consumer's surface, d1-drv8876): a default source whose sample is no longer the one it was
            // created with, or a source the platform registered in its place.
            return !(source is ADCDefaultChannelSource d) || !initial.TryGetValue(ch, out var s0) || !ReferenceEquals(d.DefaultSample, s0);
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
            records.Refuse("TVI-ADC-CONFIG", Unmodeled, RegisterAt(offset & ~3L), "none",
                "the platform gives this ADC guard what it cannot model: " + c + "; every access refuses",
                "TVI-WO-1307.measure.md §2.3 (the form), §2.2 (the revisions); ruled 2026-11-25T21:17Z Q1, Q2, Q10", "none", offset, value, width);
            return true;
        }

        private string Config()
        {
            if(config != null)
            {
                return config;
            }
            if(Alias == null)
            {
                return "the name " + (Name == null ? "(none)" : Quote(Name)) + ", where this guard models adcM1S2 (ADC1, with ADC2 behind it) and adc3";
            }
            if(!(host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc) && rcc.Holds(Alias, this)))
            {
                return Name + " is not registered on an RCC guard at " + RccName + " under " + Alias + ", so its enable and its kernel clock are not watched (TVI-WO-1300b (2))";
            }
            return null;
        }

        // Words only (p926), but ruled Q14's half-word DR read.
        private bool Narrow(long offset, ulong value, int width, string what)
        {
            if(width == 32 && (offset & 3) == 0)
            {
                return false;
            }
            records.Refuse("TVI-ADC-WIDTH", Unmodeled, RegisterAt(offset & ~3L), ((offset & (width / 8 - 1)) != 0 ? "misaligned " : "") + width + "-bit " + what,
                "a " + width + "-bit ADC " + what + " at offset 0x" + offset.ToString("X")
                    + ": RM0433 states word accesses only, and the twin cannot say what silicon does with this one",
                "RM0433 Rev 9 §25.4.5 p926 (\"Word (32-bit) accesses\"); ruled 2026-11-25T21:17Z Q14", State(), offset, value, width);
            return true;
        }

        private bool RefusedField(long b, uint word, int width)
        {
            foreach(var f in Fields)
            {
                if(f.Offset != b || f.Allowed == null || f.Allowed.Contains(f.Of(word)))
                {
                    continue;
                }
                records.Refuse("TVI-ADC-FIELD", Unmodeled, f.Register, f.Name + " (" + f.Bits + ") = " + f.Format(f.Of(word)),
                    f.Register + "." + f.Name + " written " + f.Format(f.Of(word)) + ": the model implements only " + string.Join(", ", f.Allowed.Select(f.Format))
                        + " (" + f.BehavesAs + ")",
                    "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; the allow-list (RUL-17), ruled 2026-11-25T21:17Z Q12", State(), b, word, width);
                return true;
            }
            // Rev Y has BOOST at bit 8 alone; bit 9 is reserved (p1005).
            if(b == CrOffset && Revision == "Y" && (word & (1u << 9)) != 0)
            {
                records.Refuse("TVI-ADC-FIELD", Unmodeled, "ADC_CR", "bit 9 = 1 (rev Y)",
                    "ADC_CR bit 9 written 1 on revision Y, where BOOST is bit 8 alone and bit 9 is reserved: must be kept at reset value",
                    "RM0433 Rev 9 §25.6.3 p1005 (rev Y); ruled 2026-11-25T21:17Z Q12", State(), b, word, width);
                return true;
            }
            return false;
        }

        // TVI-ADC-STATE (ruled Q5): "only when" (p934; each bit's row). A field whose written value differs from the served one.
        private bool RefusedState(long b, uint word, int width)
        {
            var old = Served(b);
            var changed = (old ^ word) & Defined(b);
            var adstart = (cr & AdstartBit) != 0;
            var disabled = (cr & (AdenBit | AdcalBit | AdstartBit | AdstpBit | AddisBit | JadstartBit)) == 0;
            string why = null, page = null, bit = null;
            switch(b)
            {
            case CrOffset:
                var commands = Commands(word);
                var after = (cr & CrCommands) | (word & CrStored);
                if((changed & (DeeppwdBit | AdvregenBit | AdcaldifBit | AdcallinBit)) != 0 && !disabled)
                {
                    bit = Names(changed & (DeeppwdBit | AdvregenBit | AdcaldifBit | AdcallinBit), CrNames);
                    why = "written while the ADC is not disabled; allowed only when ADCAL=0, JADSTART=0, ADSTART=0, ADSTP=0, ADDIS=0 and ADEN=0";
                    page = "p1003, p1005, p1009";
                }
                else if((changed & AdvregenBit) != 0 && (word & AdvregenBit) != 0 && (after & DeeppwdBit) != 0)
                {
                    bit = "ADVREGEN (bit 28) = 1";
                    why = "the voltage regulator enabled with DEEPPWD = 1: \"Once DEEPPWD=0 and ADVREGEN=1, the ADC can be enabled\"";
                    page = "p926, p933";
                }
                else if((changed & BoostMask) != 0 && (adstart || (cr & JadstartBit) != 0))
                {
                    bit = "BOOST (9:8)";
                    why = "written with a conversion ongoing; allowed only when ADSTART=0 and JADSTART=0";
                    page = Revision == "Y" ? "p1005" : "p1009";
                }
                else if((changed & LincalMask) != 0)
                {
                    bit = Names(changed & LincalMask, CrNames);
                    why = "a LINCALRDYWx toggle (the linearity factor's word access): not modeled; \"The software is allowed to toggle a single LINCALRDYWx bit at once\" and only when ADEN=1, ADSTART=0 and JADSTART=0";
                    page = "p931-932, p1004";
                }
                else if((commands & AdcalBit) != 0 && ((cr & AdenBit) != 0 || !Regulated(after)))
                {
                    bit = "ADCAL (bit 31) = 1";
                    why = (cr & AdenBit) != 0 ? "a calibration launched with ADEN = 1; allowed only when ADEN=0" : RegulatorWhy(after);
                    page = "p1003, p928";
                }
                else if((commands & AdenBit) != 0 && ((cr & (AdcalBit | AdstartBit | AdstpBit | AddisBit | JadstartBit)) != 0 || (commands & ~AdenBit) != 0 || !Regulated(after)))
                {
                    bit = "ADEN (bit 0) = 1";
                    why = !Regulated(after) ? RegulatorWhy(after) : "set while another command bit is 1; allowed only when ADCAL=0, JADSTART=0, ADSTART=0, ADSTP=0, ADDIS=0 and ADEN=0, ADVREGEN=1";
                    page = Revision == "Y" ? "p1007, p933" : "p1011, p933";
                }
                else if((commands & AddisBit) != 0 && ((cr & AdenBit) == 0 || adstart || (cr & JadstartBit) != 0))
                {
                    bit = "ADDIS (bit 1) = 1";
                    why = "allowed only when ADEN=1 and both ADSTART=0 and JADSTART=0";
                    page = "p1006";
                }
                else if((commands & AdstartBit) != 0 && ((cr & AdenBit) == 0 || (cr & AddisBit) != 0 || (commands & AddisBit) != 0))
                {
                    bit = "ADSTART (bit 2) = 1";
                    why = "allowed only when ADEN=1 and ADDIS=0";
                    page = "p1007";
                }
                else if((commands & AdstpBit) != 0 && (!adstart || (cr & AddisBit) != 0))
                {
                    bit = "ADSTP (bit 4) = 1";
                    why = "allowed only when ADSTART=1 and ADDIS=0";
                    page = "p1006";
                }
                break;
            case CcrOffset:
                if(changed == 0 || (cr & (AdenBit | AdcalBit | AdstartBit | AdstpBit | AddisBit | JadstartBit)) == 0)
                {
                    break;
                }
                if(Name == "adc3" && (changed & ~InternalEnables) == 0 && !adstart && (cr & JadstartBit) == 0)
                {
                    break;   // acknowledged by name (ruled Q6): ArduPilot's internal-channel enables after ADEN (p1038, p934)
                }
                bit = Names(changed, CcrNames);
                why = "written while the ADC is not disabled; allowed only when the ADCs are disabled (ADCAL=0, JADSTART=0, ADSTART=0, ADSTP=0, ADDIS=0 and ADEN=0)";
                page = "p1038-1039";
                break;
            case DifselOffset:
                if(changed != 0 && (cr & AdenBit) != 0)
                {
                    bit = "DIFSEL";
                    why = "written with ADEN = 1; allowed only when the ADC is disabled";
                    page = "p1034";
                }
                break;
            case CfgrOffset:
            case Cfgr2Offset:
            case Smpr1Offset:
            case Smpr2Offset:
            case PcselOffset:
            case IerOffset:
            case Ltr1Offset:
            case Htr1Offset:
            case Sqr1Offset:
            case Sqr2Offset:
            case Sqr3Offset:
            case Sqr4Offset:
                if(changed != 0 && adstart)
                {
                    bit = RegisterAt(b);
                    why = "a regular conversion's configuration written while ADSTART = 1; allowed only when no regular conversion is ongoing (ADSTART must be equal to 0)";
                    page = "p934";
                }
                break;
            }
            if(why == null)
            {
                return false;
            }
            records.Refuse("TVI-ADC-STATE", Unmodeled, RegisterAt(b), bit,
                Name + ": " + why + "; \"There is no hardware protection to prevent these forbidden write accesses and ADC behavior may become in an unknown state\"",
                "RM0433 Rev 9 §25.4.10 p934; " + page + "; ruled 2026-11-25T21:17Z Q5", State(), b, word, width);
            return true;
        }

        private bool Regulated(uint after)
        {
            return (after & DeeppwdBit) == 0 && (after & AdvregenBit) != 0 && ldoReady;
        }

        private string RegulatorWhy(uint after)
        {
            return (after & DeeppwdBit) != 0 ? "issued with DEEPPWD = 1 (the ADC in deep-power-down)"
                : (after & AdvregenBit) == 0 ? "issued with ADVREGEN = 0 (the voltage regulator off)"
                : "issued before the voltage regulator's start-up time (t_ADCVREG_STUP, 10 us max, DS12117) has elapsed since ADVREGEN: \"the software must wait for the startup time of the ADC voltage regulator (TADCVREG_STUP) before launching a calibration or enabling the ADC\"";
        }

        // TVI-ADC-KERNEL and TVI-ADC-RATE at ADCAL, ADEN and ADSTART.
        private bool RefusedClock(uint word, uint commands)
        {
            var cmd = (commands & AdstartBit) != 0 ? "ADSTART" : (commands & AdenBit) != 0 ? "ADEN" : "ADCAL";
            if(Ckmode == 0)
            {
                var k = host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc) ? rcc.QueryKernelClock(Alias) : null;
                if(k == null || !k.Modeled)
                {
                    records.Refuse("TVI-ADC-KERNEL", Unmodeled, "ADC_CR", cmd + " = 1",
                        Name + ": " + cmd + " with CKMODE = 00 and no kernel-clock rule: " + (k == null ? "no RCC guard at " + RccName : k.Condition),
                        (k == null ? "TVI-WO-1300 (4): the RCC guard's QueryKernelClock" : k.Citation) + "; ruled 2026-11-25T21:17Z Q3", State(), CrOffset, word, 32);
                    return true;
                }
                if(!k.Running)
                {
                    records.Refuse("TVI-ADC-KERNEL", PredictedFault, "ADC_CR", k.Selector,
                        Name + " " + cmd + " with CKMODE = 00 and no kernel clock: " + k.Condition + "; the ADC's clock is adc_ker_ck_input when CKMODE = 00",
                        k.Citation + "; " + KernelCitation + "; ruled 2026-11-25T21:17Z Q3", State(), CrOffset, word, 32);
                    return true;
                }
            }
            var f = AdcHz(out var why);
            if(f == null)
            {
                return RefuseRate(cmd + " = 1", Name + ": " + cmd + " with an ADC clock the twin cannot compute from declared values: " + why,
                    "RM0433 Rev 9 §25.4.3 p920, §25.7.2 p1039; ruled 2026-11-25T21:17Z Q4", word);
            }
            var problem = RateProblem(f.Value, (cr & CrCommands) | (word & CrStored), cmd == "ADSTART", out var bit);
            if(problem != null)
            {
                return RefuseRate(bit, Name + ": " + cmd + " with " + problem, "RM0433 Rev 9 §25.4.3 p922; §25.6.3 " + (Revision == "V" ? "p1009" : "p1005")
                    + "; DS12117 Rev 10 " + (Revision == "V" ? "Table 173 p272" : "Table 76 p166") + "; ruled 2026-11-25T21:17Z Q4, Q7", word);
            }
            if(cmd == "ADSTART" && RejectedRes(Res))
            {
                return RefuseRate("RES (4:2) = " + Convert.ToString(Res, 2).PadLeft(3, '0'),
                    Name + ": ADSTART with RES = " + Convert.ToString(Res, 2).PadLeft(3, '0') + ", a code revision " + Revision + " reserves (\"Others: Reserved, must not be used\")",
                    "RM0433 Rev 9 §25.6.4 p1016; ruled 2026-11-25T21:17Z Q2", word);
            }
            return false;
        }

        // f_ADC against the revision's maximum (DS12117) and, when `boosted`, the BOOST p1009 (rev V) / p1005 (rev Y) gives for it (the
        // conversions' own check: at ADSTART and after a clock change), judged on `crBits`, CR as the write leaves it; null when it holds.
        private string RateProblem(double f, uint crBits, bool boosted, out string bit)
        {
            var boost = (crBits >> 8) & 3;
            bit = "BOOST (" + (Revision == "V" ? "9:8" : "bit 8") + ") = " + boost;
            var max = Revision == "V" ? 50e6 : (boost & 1) != 0 ? 36e6 : 20e6;
            if(f > max)
            {
                return "f_ADC " + Mhz(f) + ", above the maximum " + Mhz(max) + " for revision " + Revision + " with this BOOST";
            }
            if(!boosted)
            {
                return null;
            }
            var want = Revision == "V" ? (f <= 6.25e6 ? 0u : f <= 12.5e6 ? 1u : f <= 25e6 ? 2u : 3u) : f > 20e6 ? 1u : boost & 1;
            if(boost == want || (Revision == "V" && boost == 1 && want == 2))
            {
                return null;   // the second: acknowledged by name (ruled Q7), ArduPilot's BOOST = 01 where f_ADC asks for 10 (ChibiOS's #if order)
            }
            return "BOOST = " + boost + " at f_ADC " + Mhz(f) + ", where revision " + Revision + " asks for " + want + ": \"This bit must be set according to the ADC clock setting\"";
        }

        private bool RefuseRate(string bit, string condition, string citation, uint word)
        {
            records.Refuse("TVI-ADC-RATE", Unmodeled, "ADC_CR", bit, condition, citation, State(), CrOffset, word, 32);
            return true;
        }

        // TVI-ADC-PCSEL and TVI-ADC-DMNGT at ADSTART.
        private bool RefusedStart(uint word)
        {
            var pcsel = regs[PcselOffset];
            for(var i = 0; i < SequenceLength; i++)
            {
                var ch = Channel(i);
                if((pcsel & (1u << ch)) == 0)
                {
                    records.Refuse("TVI-ADC-PCSEL", Unmodeled, "ADC_PCSEL", "PCSEL" + ch + " = 0",
                        Name + ": ADSTART with channel " + ch + " sequenced (SQ" + (i + 1) + ") and not preselected: \"the ADC conversion result with this channel shows wrong result\"",
                        "RM0433 Rev 9 §25.4.12 p935, §25.6.8 p1021", State(), CrOffset, word, 32);
                    return true;
                }
            }
            var dmngt = Dmngt;
            if(dmngt == 1 && ServingCircular())
            {
                records.Refuse("TVI-ADC-DMNGT", Unmodeled, "ADC_CFGR", "DMNGT (1:0) = 01",
                    Name + ": ADSTART with DMNGT = 01 (one-shot) on a circular DMA stream, whose transfers never end: the twin cannot see the DMA's last transfer (WO §5: a stream that never ends)",
                    "RM0433 Rev 9 §25.4.27 p964-965, §25.6.4 p1016; ruled 2026-11-25T21:17Z Q8", State(), CrOffset, word, 32);
                return true;
            }
            if(dmngt == 2 || (dmngt == 1 && ServingState() < 0))
            {
                records.Refuse("TVI-ADC-DMNGT", Unmodeled, "ADC_CFGR", "DMNGT (1:0) = " + Convert.ToString(dmngt, 2).PadLeft(2, '0'),
                    Name + ": ADSTART with " + (dmngt == 2 ? "DMNGT = 10 (DFSDM mode): the platform has no DFSDM model" : "DMNGT = 01 (one-shot) and no stream of a DMA guard selecting request " + request + ": the twin cannot see the DMA's last transfer"),
                    "RM0433 Rev 9 §25.4.27 p964-965, §25.6.4 p1016; ruled 2026-11-25T21:17Z Q8", State(), CrOffset, word, 32);
                return true;
            }
            return false;
        }

        // ---- the clock (ruled Q2, Q4) ------------------------------------------------------------------------------

        // f_ADC in Hz: the clock the analog block converts on, or null with the reason. Its source's frequency is the RCC guard's, the
        // overlay's one frequency rule (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q5): KernelHz for adc_ker_ck_input (CKMODE = 00, ADCSEL,
        // p419), BusHz for the bus clock (CKMODE != 00); then this ADC's own PRESC or CKMODE divider and rev V's /2 (p920, p1039).
        private double? AdcHz(out string why)
        {
            why = null;
            if(!host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out var rcc))
            {
                why = "no RCC guard at " + RccName;
                return null;
            }
            double? input;
            var ccr = regs[CcrOffset];
            var ckmode = (ccr >> 16) & 3;
            if(ckmode == 0)
            {
                input = Alias == null ? null : rcc.KernelHz(Alias, out why);
                why = why ?? (Name ?? "an ADC") + ": no kernel-clock query names it";
                var presc = (ccr >> 18) & 0xF;
                input = presc > 11 ? null : input / Prescale[presc];
            }
            else
            {
                double? bus;
                if(Revision == "V")
                {
                    var sys = rcc.BusHz("sys_ck", out why);
                    bus = (rcc.ReadDoubleWord(0x18) & 8) == 0 ? sys : sys / 2;   // adc_sclk: "sys_ck when HPRE is set to 0, otherwise ... sys_ck/2" (p920)
                }
                else
                {
                    bus = rcc.BusHz("rcc_hclk", out why);   // adc_hclk: rcc_hclk1/rcc_hclk4, sys_ck / D1CPRE / HPRE (p394-395)
                }
                input = bus / (ckmode == 1 ? 1 : ckmode == 2 ? 2 : 4);
            }
            return input == null ? null : Revision == "V" ? input / 2 : input;   // rev V: "an additional divider factor of 2" (p1039)
        }

        // ---- the platform --------------------------------------------------------------------------------------

        // Each default channel source's first sample, kept once the stock part has made them (at the name), so that a SetVoltage can be
        // told from an untouched source; and every RCC write, which marks a running conversion's rate stale (its `Written` event).
        private void OnPeripheralsChanged(IMachine m, PeripheralsChangedEventArgs args)
        {
            if(args.Peripheral == this && args.Operation == PeripheralsChangedEventArgs.PeripheralChangeType.NameChanged)
            {
                for(var i = 0; i < ADCChannelCount; i++)
                {
                    if(ADCContainer.TryGetByAddress(i, out var s) && s is ADCDefaultChannelSource d)
                    {
                        initial[i] = d.DefaultSample;
                    }
                }
                if(config != null && config.StartsWith("no revision", StringComparison.Ordinal))
                {
                    // Ruled Q2: a platform without a revision refuses at load, by name (after the witness, which Records writes first).
                    records.Refuse("TVI-ADC-CONFIG", Unmodeled, "none", "revision", (Name ?? "an ADC") + ": " + config + "; every access refuses",
                        "TVI-WO-1307.measure.md §2.2; ruled 2026-11-25T21:17Z Q2", "none", 0, 0, 0);
                }
            }
            if(rccSeen == null && args.Peripheral is GuardedSTM32H7_RCC r)
            {
                rccSeen = r;
                r.Written += (offset, value) => rateStale |= (cr & AdstartBit) != 0;
            }
        }

        private string ParseLevels(string text)
        {
            if(string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            foreach(var item in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = item.Split(':');
                if(p.Length != 2 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ch) || ch < 0 || ch > 19
                    || !decimal.TryParse(p[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var volts) || levels.ContainsKey(ch))
                {
                    return "levels " + Quote(text) + ": each entry must be <channel 0-19>:<volts>, once per channel (" + Quote(item) + ")";
                }
                levels[ch] = (ulong)Math.Round(volts * 1000000m);
            }
            return null;
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        private void ResetCopies()
        {
            cr = CrReset;
            isr = 0;
            dr = 0;
            drChannel = -1;
            drDeclared = false;
            ldoReady = false;
            oneShotDone = false;
            index = 0;
            ldoEpoch++;
            calEpoch++;
            rdyEpoch++;
            convEpoch++;
            regs.Clear();
            foreach(var o in Fields.Select(f => f.Offset).Distinct())
            {
                if(o != IsrOffset && o != CrOffset && o != DrOffset)
                {
                    regs[o] = o == CfgrOffset ? CfgrReset : o == Htr1Offset || o == Htr2Offset || o == Htr3Offset ? HtrReset : 0;
                }
            }
        }

        private int SequenceLength => (int)(regs[Sqr1Offset] & 0xF) + 1;

        private int Channel(int i)
        {
            var n = i + 1;   // SQn: SQR1 holds SQ1-4 from bit 6, SQR2 SQ5-9, SQR3 SQ10-14, SQR4 SQ15-16 (p1023-1026)
            var (o, slot) = n <= 4 ? (Sqr1Offset, n) : n <= 9 ? (Sqr2Offset, n - 5) : n <= 14 ? (Sqr3Offset, n - 10) : (Sqr4Offset, n - 15);
            return (int)(regs[o] >> (6 * slot) & 0x1F);
        }

        private double Smp(int ch)
        {
            var code = (regs[ch < 10 ? Smpr1Offset : Smpr2Offset] >> (3 * (ch % 10))) & 7;
            return SampleCycles[code];
        }

        private uint Res => (regs[CfgrOffset] >> 2) & 7;
        private uint Dmngt => regs[CfgrOffset] & 3;
        private uint Ckmode => (regs[CcrOffset] >> 16) & 3;

        private bool RejectedRes(uint res)
        {
            return Revision == "V" ? res == 4 : res > 4;   // p1016: rev V 100 not listed; rev Y 101-111 not listed
        }

        private static int ResBits(uint res)
        {
            return new[] { 16, 14, 12, 10, 8, 14, 12, 8 }[res];
        }

        private static double Sar(uint res)
        {
            return ResBits(res) / 2.0 + 0.5;   // Table 214 p955: 8.5, 7.5, 6.5, 5.5, 4.5 cycles
        }

        private string Name => host.TryGetLocalName(this, out var n) ? n : null;
        private string Alias => Name == "adcM1S2" ? "adc12" : Name == "adc3" ? "adc3" : null;

        private static bool Readable(long b)
        {
            return RegisterAt(b) != "none" && b != CdrOffset && b != Cdr2Offset;
        }

        private static string Unread(long b)
        {
            return b >= 0x100 && b < 0x200 ? "in ADC2's window (the slave of ADC1 is not modeled)"
                : b == CdrOffset || b == Cdr2Offset ? "(" + RegisterAt(b) + ": dual-mode data; dual mode is not modeled)"
                : "where RM0433 defines no register";
        }

        private static uint Defined(long b)
        {
            return Fields.Where(f => f.Offset == b && !Reserved(f.Name)).Aggregate(0u, (m, f) => m | f.Mask);
        }

        // A reserved row's name: "Reserved", or "Res31_24" where a register has several (tests/1307/fields.py).
        private static bool Reserved(string name)
        {
            return name == "Reserved" || (name.StartsWith("Res", StringComparison.Ordinal) && name.Length > 3 && char.IsDigit(name[3]));
        }

        private static uint Mask(int width)
        {
            return width == 8 ? 0xFFu : 0xFFFFu;
        }

        private static string Names(uint bits, string[] names)
        {
            return string.Join(", ", Enumerable.Range(0, 32).Where(i => (bits >> i & 1) != 0).Select(i => names[i] ?? "bit " + i));
        }

        private static string Mhz(double hz)
        {
            return (hz / 1e6).ToString("0.###", CultureInfo.InvariantCulture) + " MHz";
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        private static string RegisterAt(long offset)
        {
            var f = Fields.FirstOrDefault(x => x.Offset == offset);
            return f != null ? f.Register : "none";
        }

        private string State()
        {
            var f = AdcHz(out var _);
            return "CR=0x" + cr.ToString("X8") + " ISR=0x" + Served(IsrOffset).ToString("X") + " CFGR=0x" + regs[CfgrOffset].ToString("X")
                + " CCR=0x" + regs[CcrOffset].ToString("X") + " revision " + (Revision ?? "none") + " f_ADC " + (f == null ? "none" : Mhz(f.Value));
        }

        private readonly IMachine host;
        private readonly IDMA dma;
        private readonly int request;
        private readonly double vref;
        private readonly Records records;
        private readonly object sync = new object();
        private readonly string config;
        private readonly Dictionary<long, uint> regs = new Dictionary<long, uint>();
        private readonly Dictionary<int, ulong> levels = new Dictionary<int, ulong>();
        private readonly Dictionary<int, VoltageSample> initial = new Dictionary<int, VoltageSample>();
        private GuardedSTM32H7_RCC rccSeen;
        private uint cr, isr, dr;
        private int drChannel, index;
        private bool drDeclared, ldoReady, oneShotDone;
        private volatile bool rateStale;
        private double hz;
        private long ldoEpoch, calEpoch, rdyEpoch, convEpoch;

        private static readonly double[] SampleCycles = { 1.5, 2.5, 8.5, 16.5, 32.5, 64.5, 387.5, 810.5 };   // p1019-1020
        private static readonly double[] Prescale = { 1, 2, 4, 6, 8, 10, 12, 16, 32, 64, 128, 256 };       // PRESC (p1039)
        private static readonly string[] CrNames = NameTable(new Dictionary<int, string>
        {
            [0] = "ADEN", [1] = "ADDIS", [2] = "ADSTART", [3] = "JADSTART", [4] = "ADSTP", [5] = "JADSTP", [8] = "BOOST", [9] = "BOOST",
            [16] = "ADCALLIN", [22] = "LINCALRDYW1", [23] = "LINCALRDYW2", [24] = "LINCALRDYW3", [25] = "LINCALRDYW4", [26] = "LINCALRDYW5",
            [27] = "LINCALRDYW6", [28] = "ADVREGEN", [29] = "DEEPPWD", [30] = "ADCALDIF", [31] = "ADCAL",
        });
        private static readonly string[] CcrNames = NameTable(new Dictionary<int, string>
        {
            [16] = "CKMODE", [17] = "CKMODE", [18] = "PRESC", [19] = "PRESC", [20] = "PRESC", [21] = "PRESC", [22] = "VREFEN", [23] = "TSEN", [24] = "VBATEN",
        });

        private static string[] NameTable(Dictionary<int, string> names)
        {
            return Enumerable.Range(0, 32).Select(i => names.TryGetValue(i, out var n) ? n + " (bit " + i + ")" : null).ToArray();
        }

        private const string RccName = "sysbus.rcc";
        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";
        private const double CalCycles = 165010;     // t_CAL, offset and linearity (DS12117 p167, p273)
        private const double OffCalCycles = 1280;    // t_OFF_CAL, offset only (DS12117 p167, p273)
        private const int VregStartupMicros = 10;    // t_ADCVREG_STUP max (DS12117 p167, p273)
        private const int RunningStream = 1;
        private const long IsrOffset = 0x00;
        private const long IerOffset = 0x04;
        private const long CrOffset = 0x08;
        private const long CfgrOffset = 0x0C;
        private const long Cfgr2Offset = 0x10;
        private const long Smpr1Offset = 0x14;
        private const long Smpr2Offset = 0x18;
        private const long PcselOffset = 0x1C;
        private const long Ltr1Offset = 0x20;
        private const long Htr1Offset = 0x24;
        private const long Sqr1Offset = 0x30;
        private const long Sqr2Offset = 0x34;
        private const long Sqr3Offset = 0x38;
        private const long Sqr4Offset = 0x3C;
        private const long DrOffset = 0x40;
        private const long Htr2Offset = 0xB4;
        private const long Htr3Offset = 0xBC;
        private const long DifselOffset = 0xC0;
        private const long CsrOffset = 0x300;
        private const long CcrOffset = 0x308;
        private const long CdrOffset = 0x30C;
        private const long Cdr2Offset = 0x310;
        private const uint CrReset = 0x20000000;     // DEEPPWD = 1 (p1003)
        private const uint CfgrReset = 0x80000000;   // JQDIS = 1 (p1012)
        private const uint HtrReset = 0x03FFFFFF;    // p1022, p1033
        private const uint AdenBit = 1u << 0;
        private const uint AddisBit = 1u << 1;
        private const uint AdstartBit = 1u << 2;
        private const uint JadstartBit = 1u << 3;
        private const uint AdstpBit = 1u << 4;
        private const uint JadstpBit = 1u << 5;
        private const uint BoostMask = 3u << 8;
        private const uint AdcallinBit = 1u << 16;
        private const uint LincalMask = 0x3Fu << 22;
        private const uint AdvregenBit = 1u << 28;
        private const uint DeeppwdBit = 1u << 29;
        private const uint AdcaldifBit = 1u << 30;
        private const uint AdcalBit = 1u << 31;
        private const uint CrCommands = AdenBit | AddisBit | AdstartBit | JadstartBit | AdstpBit | JadstpBit | AdcalBit;
        private const uint CrStored = DeeppwdBit | AdvregenBit | AdcaldifBit | AdcallinBit | BoostMask | LincalMask;
        private const uint AdrdyBit = 1u << 0;
        private const uint EosmpBit = 1u << 1;
        private const uint EocBit = 1u << 2;
        private const uint EosBit = 1u << 3;
        private const uint OvrBit = 1u << 4;
        private const uint LdordyBit = 1u << 12;
        private const uint IsrClearable = 0x7FF;      // ADRDY..JQOVF, rc_w1 (p998-1000)
        private const uint OvrmodBit = 1u << 12;
        private const uint ContBit = 1u << 13;
        private const uint InternalEnables = 7u << 22;   // VREFEN, TSEN, VBATEN (p1038)

        // ---- the allow-list (RUL-16, RUL-17): one row per field, every bit 31:0 of every register ---------------
        // Generated by tests/1307/fields.py from tests/1307/data/rm0433-rev9-adc-fields.tsv; tests/1307/adc.sh `regen` checks it.

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
        private static readonly uint[] Channels = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };   // SQx (p1023)
        private static readonly uint[] Prescalers = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };   // PRESC 0000-1011 (p1039)

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        private static readonly Row[] Fields =
        {
            R("ADC_ISR", 0x000, "§25.6.1", "Res31_13", 31, 13, Zero, "reserved", "reserved: must be kept at reset value", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "LDORDY", 12, 12, Any, "read-only", "rev V: set 10 us after ADVREGEN (t_ADCVREG_STUP max, DS12117); read-only, a write is ignored; reserved and 0 on rev Y (p998; ruled 2026-11-25T21:17Z Q5)", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "Res11", 11, 11, Zero, "reserved", "reserved: must be kept at reset value", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "JQOVF", 10, 10, Any, "modeled by the guard", "never set: the injected group is not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "AWD3", 9, 9, Any, "modeled by the guard", "never set: the analog watchdogs are not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "AWD2", 8, 8, Any, "modeled by the guard", "never set: the analog watchdogs are not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 998),
            R("ADC_ISR", 0x000, "§25.6.1", "AWD1", 7, 7, Any, "modeled by the guard", "never set: the analog watchdogs are not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 999),
            R("ADC_ISR", 0x000, "§25.6.1", "JEOS", 6, 6, Any, "modeled by the guard", "never set: the injected group is not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 999),
            R("ADC_ISR", 0x000, "§25.6.1", "JEOC", 5, 5, Any, "modeled by the guard", "never set: the injected group is not modeled; writing 1 changes nothing (ruled 2026-11-25T21:17Z Q12)", 999),
            R("ADC_ISR", 0x000, "§25.6.1", "OVR", 4, 4, Any, "modeled by the guard", "set when a conversion ends with EOC still set (p999); DMA requests blocked until it is cleared (p964; ruled 2026-11-25T21:17Z Q9)", 999),
            R("ADC_ISR", 0x000, "§25.6.1", "EOS", 3, 3, Any, "modeled by the guard", "set by the guard's conversion; cleared by writing 1 (p998-1000); set at the sequence's last conversion (p955)", 999),
            R("ADC_ISR", 0x000, "§25.6.1", "EOC", 2, 2, Any, "modeled by the guard", "set by the guard's conversion; cleared by writing 1 (p998-1000); set at each conversion, also cleared by reading DR (p1000)", 1000),
            R("ADC_ISR", 0x000, "§25.6.1", "EOSMP", 1, 1, Any, "modeled by the guard", "set by the guard's conversion; cleared by writing 1 (p998-1000); set with EOC, not the SAR time earlier (EOSMPIE is 0 only; ruled 2026-11-25T21:17Z Q12)", 1000),
            R("ADC_ISR", 0x000, "§25.6.1", "ADRDY", 0, 0, Any, "modeled by the guard", "set by the guard's conversion; cleared by writing 1 (p998-1000); set t_STAB after ADEN (DS12117)", 1000),
            R("ADC_IER", 0x004, "§25.6.2", "Reserved", 31, 11, Zero, "reserved", "reserved: must be kept at reset value", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "JQOVFIE", 10, 10, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "AWD3IE", 9, 9, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "AWD2IE", 8, 8, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "AWD1IE", 7, 7, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "JEOSIE", 6, 6, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1001),
            R("ADC_IER", 0x004, "§25.6.2", "JEOCIE", 5, 5, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1002),
            R("ADC_IER", 0x004, "§25.6.2", "OVRIE", 4, 4, Any, "modeled by the guard", "the interrupt line (the entry's nvic or exti input) = OR of ADRDY, EOC, EOS, OVR and their enables (p1001-1002)", 1002),
            R("ADC_IER", 0x004, "§25.6.2", "EOSIE", 3, 3, Any, "modeled by the guard", "the interrupt line (the entry's nvic or exti input) = OR of ADRDY, EOC, EOS, OVR and their enables (p1001-1002)", 1002),
            R("ADC_IER", 0x004, "§25.6.2", "EOCIE", 2, 2, Any, "modeled by the guard", "the interrupt line (the entry's nvic or exti input) = OR of ADRDY, EOC, EOS, OVR and their enables (p1001-1002)", 1002),
            R("ADC_IER", 0x004, "§25.6.2", "EOSMPIE", 1, 1, Zero, "modeled by the guard", "0 only: its flag is never set, or set without its timing (EOSMP) (ruled 2026-11-25T21:17Z Q12)", 1002),
            R("ADC_IER", 0x004, "§25.6.2", "ADRDYIE", 0, 0, Any, "modeled by the guard", "the interrupt line (the entry's nvic or exti input) = OR of ADRDY, EOC, EOS, OVR and their enables (p1001-1002)", 1002),
            R("ADC_CR", 0x008, "§25.6.3", "ADCAL", 31, 31, Any, "modeled by the guard", "calibration: stays 1 for t_CAL = 165,010 (ADCALLIN = 1) or t_OFF_CAL = 1,280 (ADCALLIN = 0) ADC clock cycles (DS12117 p167, p273); set only with ADEN = 0, DEEPPWD = 0, ADVREGEN = 1 and the regulator started, else TVI-ADC-STATE (p1003, p928; ruled 2026-11-25T21:17Z Q5)", 1008),
            R("ADC_CR", 0x008, "§25.6.3", "ADCALDIF", 30, 30, Zero, "modeled by the guard", "0 only: differential calibration is not modeled (ruled 2026-11-25T21:17Z Q12)", 1008),
            R("ADC_CR", 0x008, "§25.6.3", "DEEPPWD", 29, 29, Any, "modeled by the guard", "stored and served on read; resets to 1 (p1003); changed only with the ADC disabled, else TVI-ADC-STATE (ruled 2026-11-25T21:17Z Q5)", 1008),
            R("ADC_CR", 0x008, "§25.6.3", "ADVREGEN", 28, 28, Any, "modeled by the guard", "stored and served on read; the regulator ready (LDORDY on rev V) 10 us after it is set with DEEPPWD = 0 (t_ADCVREG_STUP max, DS12117); changed only with the ADC disabled (p1003; ruled 2026-11-25T21:17Z Q5)", 1008),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW6", 27, 27, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1008),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW5", 26, 26, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW4", 25, 25, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW3", 24, 24, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW2", 23, 23, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "LINCALRDYW1", 22, 22, Any, "modeled by the guard", "stored and served on read; 0 from reset, set by a calibration with ADCALLIN = 1 (p931); a toggle (the linearity factor's word access) is TVI-ADC-STATE, unmodeled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "Res21_17", 21, 17, Zero, "reserved", "reserved: must be kept at reset value", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "ADCALLIN", 16, 16, Any, "modeled by the guard", "stored and served on read; chooses the calibration time and sets LINCALRDYW1-6 at its end (p931); changed only with the ADC disabled (ruled 2026-11-25T21:17Z Q5)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "Res15_10", 15, 10, Zero, "reserved", "reserved: must be kept at reset value", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "BOOST", 9, 8, Any, "modeled by the guard", "stored and served on read; rev V BOOST[1:0] (p1009), rev Y BOOST bit 8 with bit 9 reserved (p1005); checked against f_ADC at ADSTART (TVI-ADC-RATE), ArduPilot's 01 where rev V asks 10 acknowledged by name (ruled 2026-11-25T21:17Z Q7)", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "Res7_6", 7, 6, Zero, "reserved", "reserved: must be kept at reset value", 1009),
            R("ADC_CR", 0x008, "§25.6.3", "JADSTP", 5, 5, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1010),
            R("ADC_CR", 0x008, "§25.6.3", "ADSTP", 4, 4, Any, "modeled by the guard", "stops the regular conversions; set only with ADSTART = 1 and ADDIS = 0 (p1006; ruled 2026-11-25T21:17Z Q5)", 1010),
            R("ADC_CR", 0x008, "§25.6.3", "JADSTART", 3, 3, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1010),
            R("ADC_CR", 0x008, "§25.6.3", "ADSTART", 2, 2, Any, "modeled by the guard", "starts the regular conversions, paced at f_ADC (ruled Q4); set only with ADEN = 1 and ADDIS = 0 (p1007); TVI-ADC-KERNEL, -RATE, -PCSEL and -DMNGT judge it (ruled 2026-11-25T21:17Z Q3-Q8)", 1011),
            R("ADC_CR", 0x008, "§25.6.3", "ADDIS", 1, 1, Any, "modeled by the guard", "clears ADEN and ADSTART at once; set only with ADEN = 1 and ADSTART = 0 (p1006; ruled 2026-11-25T21:17Z Q5)", 1011),
            R("ADC_CR", 0x008, "§25.6.3", "ADEN", 0, 0, Any, "modeled by the guard", "ADRDY one conversion cycle later (t_STAB, DS12117); set only with every command bit 0, ADVREGEN = 1 and the regulator started (p1007, p1011, p933; ruled 2026-11-25T21:17Z Q5)", 1011),
            R("ADC_CFGR", 0x00C, "§25.6.4", "JQDIS", 31, 31, Any, "modeled by the guard", "stored and served on read; used by no modeled path: the injected group is 0 only (ruled 2026-11-25T21:17Z Q12)", 1012),
            R("ADC_CFGR", 0x00C, "§25.6.4", "AWD1CH", 30, 26, Channels, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1012),
            R("ADC_CFGR", 0x00C, "§25.6.4", "JAUTO", 25, 25, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1012),
            R("ADC_CFGR", 0x00C, "§25.6.4", "JAWD1EN", 24, 24, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1012),
            R("ADC_CFGR", 0x00C, "§25.6.4", "AWD1EN", 23, 23, Zero, "modeled by the guard", "0 only: the analog watchdogs are not modeled (ruled 2026-11-25T21:17Z Q12)", 1013),
            R("ADC_CFGR", 0x00C, "§25.6.4", "AWD1SGL", 22, 22, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1013),
            R("ADC_CFGR", 0x00C, "§25.6.4", "JQM", 21, 21, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1013),
            R("ADC_CFGR", 0x00C, "§25.6.4", "JDISCEN", 20, 20, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1013),
            R("ADC_CFGR", 0x00C, "§25.6.4", "DISCNUM", 19, 17, Any, "modeled by the guard", "stored and served on read; used by no modeled path while DISCEN = 0 (ruled 2026-11-25T21:17Z Q12)", 1014),
            R("ADC_CFGR", 0x00C, "§25.6.4", "DISCEN", 16, 16, Zero, "modeled by the guard", "0 only: discontinuous mode is not modeled (ruled 2026-11-25T21:17Z Q12)", 1014),
            R("ADC_CFGR", 0x00C, "§25.6.4", "Reserved", 15, 15, Zero, "reserved", "reserved: must be kept at reset value", 1014),
            R("ADC_CFGR", 0x00C, "§25.6.4", "AUTDLY", 14, 14, Zero, "modeled by the guard", "0 only: the delayed conversion mode is not modeled (ruled 2026-11-25T21:17Z Q12)", 1014),
            R("ADC_CFGR", 0x00C, "§25.6.4", "CONT", 13, 13, Any, "modeled by the guard", "continuous: the sequence restarts at EOS (p937); single: ADSTART cleared at EOS (p1007); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1014),
            R("ADC_CFGR", 0x00C, "§25.6.4", "OVRMOD", 12, 12, Any, "modeled by the guard", "OVR when a conversion ends with EOC set (p999): 0 keeps DR, 1 overwrites it (p1015); the eight-stage FIFO (p962) is not modeled, acknowledged by name (ruled 2026-11-25T21:17Z Q9); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1015),
            R("ADC_CFGR", 0x00C, "§25.6.4", "EXTEN", 11, 10, Zero, "modeled by the guard", "0 only: hardware triggers are not modeled: no trigger line reaches the ADC (ruled 2026-11-25T21:17Z Q12)", 1015),
            R("ADC_CFGR", 0x00C, "§25.6.4", "EXTSEL", 9, 5, Any, "modeled by the guard", "stored and served on read; used by no modeled path while EXTEN = 0 (ruled 2026-11-25T21:17Z Q12)", 1016),
            R("ADC_CFGR", 0x00C, "§25.6.4", "RES", 4, 2, Any, "modeled by the guard", "the resolution and the SAR time (Table 214 p955), by the declared revision; a code it reserves is TVI-ADC-RATE at ADSTART (p1016; ruled 2026-11-25T21:17Z Q2); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1016),
            R("ADC_CFGR", 0x00C, "§25.6.4", "DMNGT", 1, 0, Any, "modeled by the guard", "00 DR only; 01 and 11 a DMA request per conversion, 01 stopping at the DMA's last transfer (p964-965); 10 (DFSDM) is TVI-ADC-DMNGT at ADSTART (p1016; ruled 2026-11-25T21:17Z Q8); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1016),
            R("ADC_CFGR2", 0x010, "§25.6.5", "LSHIFT", 31, 28, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1017),
            R("ADC_CFGR2", 0x010, "§25.6.5", "Res27_26", 27, 26, Zero, "reserved", "reserved: must be kept at reset value", 1017),
            R("ADC_CFGR2", 0x010, "§25.6.5", "OSVR", 25, 16, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1017),
            R("ADC_CFGR2", 0x010, "§25.6.5", "Res15", 15, 15, Zero, "reserved", "reserved: must be kept at reset value", 1017),
            R("ADC_CFGR2", 0x010, "§25.6.5", "RSHIFT4", 14, 14, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1017),
            R("ADC_CFGR2", 0x010, "§25.6.5", "RSHIFT3", 13, 13, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "RSHIFT2", 12, 12, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "RSHIFT1", 11, 11, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "ROVSM", 10, 10, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "TROVS", 9, 9, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "OVSS", 8, 5, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1018),
            R("ADC_CFGR2", 0x010, "§25.6.5", "Res4_2", 4, 2, Zero, "reserved", "reserved: must be kept at reset value", 1019),
            R("ADC_CFGR2", 0x010, "§25.6.5", "JOVSE", 1, 1, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1019),
            R("ADC_CFGR2", 0x010, "§25.6.5", "ROVSE", 0, 0, Zero, "modeled by the guard", "0 only: oversampling and the data shift are not modeled; data are right-aligned (ruled 2026-11-25T21:17Z Q12)", 1019),
            R("ADC_SMPR1", 0x014, "§25.6.6", "Reserved", 31, 30, Zero, "reserved", "reserved: must be kept at reset value", 1019),
            R("ADC_SMPR1", 0x014, "§25.6.6", "SMPx", 29, 0, Any, "modeled by the guard", "the sampling time per channel: 1.5-810.5 ADC clock cycles (p1019-1020); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1019),
            R("ADC_SMPR2", 0x018, "§25.6.7", "Reserved", 31, 30, Zero, "reserved", "reserved: must be kept at reset value", 1020),
            R("ADC_SMPR2", 0x018, "§25.6.7", "SMPx", 29, 0, Any, "modeled by the guard", "the sampling time per channel: 1.5-810.5 ADC clock cycles (p1019-1020); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1020),
            R("ADC_PCSEL", 0x01C, "§25.6.8", "Reserved", 31, 20, Zero, "reserved", "reserved: must be kept at reset value", 1021),
            R("ADC_PCSEL", 0x01C, "§25.6.8", "PCSEL", 19, 0, Any, "modeled by the guard", "stored and served on read; a sequenced channel not preselected is TVI-ADC-PCSEL at ADSTART (p935, p1021); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1021),
            R("ADC_LTR1", 0x020, "§25.6.9", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1021),
            R("ADC_LTR1", 0x020, "§25.6.9", "LTR1", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1021),
            R("ADC_HTR1", 0x024, "§25.6.10", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1022),
            R("ADC_HTR1", 0x024, "§25.6.10", "HTR1", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1022),
            R("ADC_SQR1", 0x030, "§25.6.11", "Res31_29", 31, 29, Zero, "reserved", "reserved: must be kept at reset value", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "SQ4", 28, 24, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "SQ3", 22, 18, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "SQ2", 16, 12, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "Res11", 11, 11, Zero, "reserved", "reserved: must be kept at reset value", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "SQ1", 10, 6, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "Res5_4", 5, 4, Zero, "reserved", "reserved: must be kept at reset value", 1023),
            R("ADC_SQR1", 0x030, "§25.6.11", "L", 3, 0, Any, "modeled by the guard", "the sequence length (p1023); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1023),
            R("ADC_SQR2", 0x034, "§25.6.12", "Res31_29", 31, 29, Zero, "reserved", "reserved: must be kept at reset value", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "SQ9", 28, 24, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "SQ8", 22, 18, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "SQ7", 16, 12, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "Res11", 11, 11, Zero, "reserved", "reserved: must be kept at reset value", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "SQ6", 10, 6, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "Res5", 5, 5, Zero, "reserved", "reserved: must be kept at reset value", 1024),
            R("ADC_SQR2", 0x034, "§25.6.12", "SQ5", 4, 0, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1024),
            R("ADC_SQR3", 0x038, "§25.6.13", "Res31_29", 31, 29, Zero, "reserved", "reserved: must be kept at reset value", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "SQ14", 28, 24, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "SQ13", 22, 18, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "SQ12", 16, 12, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "Res11", 11, 11, Zero, "reserved", "reserved: must be kept at reset value", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "SQ11", 10, 6, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "Res5", 5, 5, Zero, "reserved", "reserved: must be kept at reset value", 1025),
            R("ADC_SQR3", 0x038, "§25.6.13", "SQ10", 4, 0, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1025),
            R("ADC_SQR4", 0x03C, "§25.6.14", "Res31_11", 31, 11, Zero, "reserved", "reserved: must be kept at reset value", 1026),
            R("ADC_SQR4", 0x03C, "§25.6.14", "SQ16", 10, 6, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1026),
            R("ADC_SQR4", 0x03C, "§25.6.14", "Res5", 5, 5, Zero, "reserved", "reserved: must be kept at reset value", 1026),
            R("ADC_SQR4", 0x03C, "§25.6.14", "SQ15", 4, 0, Channels, "modeled by the guard", "the sequence: a channel 0-19 (p1023-1026); written with a change while ADSTART = 1 it is TVI-ADC-STATE, unmodeled (p934; ruled 2026-11-25T21:17Z Q5)", 1026),
            R("ADC_DR", 0x040, "§25.6.15", "RDATA", 31, 0, Any, "read-only", "the last conversion, right-aligned (p1027); reading it clears EOC; holding an undeclared channel's conversion the read is TVI-ADC-LEVEL (ruled 2026-11-25T21:17Z Q10); a half-word read by ArduPilot's DMA (DMNGT = 11) acknowledged by name (p926; ruled 2026-11-25T21:17Z Q14)", 1027),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JSQ4", 31, 27, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "Res26", 26, 26, Zero, "reserved", "reserved: must be kept at reset value", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JSQ3", 25, 21, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "Res20", 20, 20, Zero, "reserved", "reserved: must be kept at reset value", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JSQ2", 19, 15, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "Res14", 14, 14, Zero, "reserved", "reserved: must be kept at reset value", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JSQ1", 13, 9, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1028),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JEXTEN", 8, 7, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1029),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JEXTSEL", 6, 2, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1029),
            R("ADC_JSQR", 0x04C, "§25.6.16", "JL", 1, 0, Zero, "modeled by the guard", "0 only: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1029),
            R("ADC_OFR1", 0x060, "§25.6.17", "SSATE", 31, 31, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR1", 0x060, "§25.6.17", "OFFSET_CH", 30, 26, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR1", 0x060, "§25.6.17", "OFFSET", 25, 0, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR2", 0x064, "§25.6.17", "SSATE", 31, 31, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR2", 0x064, "§25.6.17", "OFFSET_CH", 30, 26, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR2", 0x064, "§25.6.17", "OFFSET", 25, 0, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR3", 0x068, "§25.6.17", "SSATE", 31, 31, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR3", 0x068, "§25.6.17", "OFFSET_CH", 30, 26, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR3", 0x068, "§25.6.17", "OFFSET", 25, 0, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR4", 0x06C, "§25.6.17", "SSATE", 31, 31, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR4", 0x06C, "§25.6.17", "OFFSET_CH", 30, 26, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_OFR4", 0x06C, "§25.6.17", "OFFSET", 25, 0, Zero, "modeled by the guard", "0 only: the offsets are not modeled (ruled 2026-11-25T21:17Z Q12)", 1030),
            R("ADC_JDR1", 0x080, "§25.6.18", "JDATA", 31, 0, Any, "read-only", "reads 0: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1031),
            R("ADC_JDR2", 0x084, "§25.6.18", "JDATA", 31, 0, Any, "read-only", "reads 0: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1031),
            R("ADC_JDR3", 0x088, "§25.6.18", "JDATA", 31, 0, Any, "read-only", "reads 0: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1031),
            R("ADC_JDR4", 0x08C, "§25.6.18", "JDATA", 31, 0, Any, "read-only", "reads 0: the injected group is not modeled (ruled 2026-11-25T21:17Z Q12)", 1031),
            R("ADC_AWD2CR", 0x0A0, "§25.6.19", "Reserved", 31, 20, Zero, "reserved", "reserved: must be kept at reset value", 1031),
            R("ADC_AWD2CR", 0x0A0, "§25.6.19", "AWD2CH", 19, 0, Zero, "modeled by the guard", "0 only: the analog watchdogs are not modeled (ruled 2026-11-25T21:17Z Q12)", 1031),
            R("ADC_AWD3CR", 0x0A4, "§25.6.20", "Reserved", 31, 20, Zero, "reserved", "reserved: must be kept at reset value", 1032),
            R("ADC_AWD3CR", 0x0A4, "§25.6.20", "AWD3CH", 19, 0, Zero, "modeled by the guard", "0 only: the analog watchdogs are not modeled (ruled 2026-11-25T21:17Z Q12)", 1032),
            R("ADC_LTR2", 0x0B0, "§25.6.21", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1032),
            R("ADC_LTR2", 0x0B0, "§25.6.21", "LTR2", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1032),
            R("ADC_HTR2", 0x0B4, "§25.6.22", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1033),
            R("ADC_HTR2", 0x0B4, "§25.6.22", "HTR2", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1033),
            R("ADC_LTR3", 0x0B8, "§25.6.23", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1033),
            R("ADC_LTR3", 0x0B8, "§25.6.23", "LTR3", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1033),
            R("ADC_HTR3", 0x0BC, "§25.6.24", "Reserved", 31, 26, Zero, "reserved", "reserved: must be kept at reset value", 1034),
            R("ADC_HTR3", 0x0BC, "§25.6.24", "HTR3", 25, 0, Any, "modeled by the guard", "stored and served on read; used by no modeled path while every watchdog enable is 0 (ruled 2026-11-25T21:17Z Q12)", 1034),
            R("ADC_DIFSEL", 0x0C0, "§25.6.25", "Reserved", 31, 20, Zero, "reserved", "reserved: must be kept at reset value", 1034),
            R("ADC_DIFSEL", 0x0C0, "§25.6.25", "DIFSEL", 19, 0, Zero, "modeled by the guard", "0 only: differential inputs are not modeled (ruled 2026-11-25T21:17Z Q12)", 1034),
            R("ADC_CALFACT", 0x0C4, "§25.6.26", "Res31_27", 31, 27, Zero, "reserved", "reserved: must be kept at reset value", 1035),
            R("ADC_CALFACT", 0x0C4, "§25.6.26", "CALFACT_D", 26, 16, Zero, "modeled by the guard", "0 only: reads 0: the calibration factors are not modeled; a calibration only takes its time (ruled 2026-11-25T21:17Z Q5, Q12)", 1035),
            R("ADC_CALFACT", 0x0C4, "§25.6.26", "Res15_11", 15, 11, Zero, "reserved", "reserved: must be kept at reset value", 1035),
            R("ADC_CALFACT", 0x0C4, "§25.6.26", "CALFACT_S", 10, 0, Zero, "modeled by the guard", "0 only: reads 0: the calibration factors are not modeled; a calibration only takes its time (ruled 2026-11-25T21:17Z Q5, Q12)", 1035),
            R("ADC_CALFACT2", 0x0C8, "§25.6.27", "Reserved", 31, 30, Zero, "reserved", "reserved: must be kept at reset value", 1036),
            R("ADC_CALFACT2", 0x0C8, "§25.6.27", "LINCALFACT", 29, 0, Zero, "modeled by the guard", "0 only: reads 0: the calibration factors are not modeled; a calibration only takes its time (ruled 2026-11-25T21:17Z Q5, Q12)", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "Res31_27", 31, 27, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "JQOVF_SLV", 26, 26, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD3_SLV", 25, 25, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD2_SLV", 24, 24, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD1_SLV", 23, 23, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1036),
            R("ADCx_CSR", 0x300, "§25.7.1", "JEOS_SLV", 22, 22, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "JEOC_SLV", 21, 21, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "OVR_SLV", 20, 20, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOS_SLV", 19, 19, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOC_SLV", 18, 18, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOSMP_SLV", 17, 17, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "ADRDY_SLV", 16, 16, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "Res15_11", 15, 11, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "JQOVF_MST", 10, 10, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD3_MST", 9, 9, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD2_MST", 8, 8, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "AWD1_MST", 7, 7, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "JEOS_MST", 6, 6, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "JEOC_MST", 5, 5, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "OVR_MST", 4, 4, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOS_MST", 3, 3, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOC_MST", 2, 2, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "EOSMP_MST", 1, 1, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CSR", 0x300, "§25.7.1", "ADRDY_MST", 0, 0, Any, "read-only", "the master's ISR flags (p1036); the slave's read 0: ADC2 is not modeled", 1037),
            R("ADCx_CCR", 0x308, "§25.7.2", "Res31_25", 31, 25, Zero, "reserved", "reserved: must be kept at reset value", 1038),
            R("ADCx_CCR", 0x308, "§25.7.2", "VBATEN", 24, 24, Any, "modeled by the guard", "stored and served on read; the internal channels' enable (ADC3: VBAT/4 17, VSENSE 18, VREFINT 19, p920); written only with the ADCs disabled (p1038), but ArduPilot's ADC3 pattern (these bits with ADEN = 1, ADSTART = 0) acknowledged by name (ruled 2026-11-25T21:17Z Q6)", 1038),
            R("ADCx_CCR", 0x308, "§25.7.2", "TSEN", 23, 23, Any, "modeled by the guard", "stored and served on read; the internal channels' enable (ADC3: VBAT/4 17, VSENSE 18, VREFINT 19, p920); written only with the ADCs disabled (p1038), but ArduPilot's ADC3 pattern (these bits with ADEN = 1, ADSTART = 0) acknowledged by name (ruled 2026-11-25T21:17Z Q6)", 1038),
            R("ADCx_CCR", 0x308, "§25.7.2", "VREFEN", 22, 22, Any, "modeled by the guard", "stored and served on read; the internal channels' enable (ADC3: VBAT/4 17, VSENSE 18, VREFINT 19, p920); written only with the ADCs disabled (p1038), but ArduPilot's ADC3 pattern (these bits with ADEN = 1, ADSTART = 0) acknowledged by name (ruled 2026-11-25T21:17Z Q6)", 1038),
            R("ADCx_CCR", 0x308, "§25.7.2", "PRESC", 21, 18, Prescalers, "modeled by the guard", "divides the kernel clock when CKMODE = 00 (p1039); 1100-1111 reserved; written only with the ADCs disabled (ruled 2026-11-25T21:17Z Q5)", 1039),
            R("ADCx_CCR", 0x308, "§25.7.2", "CKMODE", 17, 16, Any, "modeled by the guard", "00: the kernel clock ADCSEL selects (p419) / PRESC; 01/10/11: rev Y adc_hclk, rev V adc_sclk, /1/2/4; rev V /2 again (p920, p1039); written only with the ADCs disabled (ruled 2026-11-25T21:17Z Q2-Q5)", 1039),
            R("ADCx_CCR", 0x308, "§25.7.2", "DAMDF", 15, 14, Zero, "modeled by the guard", "0 only: dual mode is not modeled: ADC2 is never started (ruled 2026-11-25T21:17Z Q1, Q12)", 1040),
            R("ADCx_CCR", 0x308, "§25.7.2", "Res13_12", 13, 12, Zero, "reserved", "reserved: must be kept at reset value", 1040),
            R("ADCx_CCR", 0x308, "§25.7.2", "DELAY", 11, 8, Zero, "modeled by the guard", "0 only: dual mode is not modeled: ADC2 is never started (ruled 2026-11-25T21:17Z Q1, Q12)", 1040),
            R("ADCx_CCR", 0x308, "§25.7.2", "Res7_5", 7, 5, Zero, "reserved", "reserved: must be kept at reset value", 1040),
            R("ADCx_CCR", 0x308, "§25.7.2", "DUAL", 4, 0, Zero, "modeled by the guard", "0 only: dual mode is not modeled: ADC2 is never started (ruled 2026-11-25T21:17Z Q1, Q12)", 1040),
            R("ADCx_CDR", 0x30C, "§25.7.3", "RDATA_SLV", 31, 16, Any, "read-only", "dual-mode data: a read is TVI-ADC-READ, unmodeled (ruled 2026-11-25T21:17Z Q12)", 1042),
            R("ADCx_CDR", 0x30C, "§25.7.3", "RDATA_MST", 15, 0, Any, "read-only", "dual-mode data: a read is TVI-ADC-READ, unmodeled (ruled 2026-11-25T21:17Z Q12)", 1042),
            R("ADCx_CDR2", 0x310, "§25.7.4", "RDATA_ALT", 31, 0, Any, "read-only", "dual-mode data: a read is TVI-ADC-READ, unmodeled (ruled 2026-11-25T21:17Z Q12)", 1043),
        };
    }
}
