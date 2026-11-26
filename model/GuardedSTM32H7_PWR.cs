// Tviburi's PWR guard for the STM32H753 (TVI-WO-1308 (3)). Every citation is to ST RM0433 Rev 9, chapter 6 (§6.8, pp304-313),
// and DS12117 Rev 10 for the voltage scales' frequency limits. The measurement and the rulings (2026-11-26T09:31Z Q5-Q14,
// 10:08Z Q17, 11:06Z Q19-Q21): docs/work-orders/TVI-WO-1308.measure.md §3, §6.
//
// Insertion point (ruled Q13): the stock `pwr` is a Python.PythonPeripheral script (stm32h7.repl L728-746) that answers "ready"
// to everything and drops every write, so there is no stock model to subclass or wrap: this guard is a Tviburi-authored model.
// The stock entry is cancelled and this guard declared at 0x58024800 `as "pwr"` (model/stm32h7-pwr-restate.repl). The board
// keeps the stock stub (ruled Q17 (a)); a platform that wants the guard appends the restatement and sets `revision`.
//
// What it models:
//   The supply configuration (PWR_CR3's lower byte: BYPASS, LDOEN, SCUEN): "written once after POR" (p308). The first write of
//   the lower byte is kept; a later write that leaves it as it is passes (RUL-18 as amended); one that changes it is ignored, as
//   silicon ignores it, and refused (predicted-fault) in both modes: in recorded mode the record is a would-refuse and the lower
//   byte is still ignored, because ignoring it is the model (ruled Q6). An invalid LDOEN/BYPASS pair (00 or 11, Table 34 p261) is
//   ignored and locks the register until a power cycle, with ACTVOSRDY left 0 (p308): predicted-fault. That state is reset only
//   by POR ("This register is reset only by POR. It is not reset by wakeup from Standby mode and by the RESET pad", p307), so it
//   survives `machine Reset` (ruled Q6: configure-once until a power-on reset).
//   The voltage scale: PWR_D3CR.VOS kept and served (p310); VOS 00 is "Reserved (Scale 3 selected)" and is Scale 3. `Scale` is
//   what the flash and RCC guards ask (ruled Q9, Q21). A VOS change before ACTVOSRDY ("VOS shall not be changed", p262), and a
//   VOS that lowers the scale under the clocks then running (p310's second sentence; ruled Q9), refuse.
//   The ready flags (ruled Q5): ACTVOSRDY rises at the valid supply configuration write, VOSRDY reads 1 from reset ("Following
//   reset VOSRDY will be read 1", p310) and at once at a VOS write, USB33RDY follows USB33DEN at once: "no figure in RM0433 Rev 9
//   or DS12117 Rev 10", acknowledged by name (bench rows P1, P3).
//   DBP (PWR_CR1 bit 8, p305): kept and served; it write-protects PWR_CR2 here (p306) and RCC_BDCR in the RCC guard (p427; `Dbp`).
//
// Rules, in the order they are evaluated. A refused access changes nothing.
//   TVI-LOAD         (Records) the guard did not load: every access refuses.
//   TVI-PWR-CONFIG   the platform gives this guard what it cannot model: no `revision` ("V" or "Y"; ruled Q7, never defaulted,
//                    refused at load by name), or a bus address that is not PWR's (0x58024800, RM0433 Rev 9 Table 8 p132).
//                    unmodeled; every access refuses.
//   TVI-PWR-WIDTH    a misaligned access: "The PWR registers can be accessed in word, half-word and byte format" (p304), and
//                    nothing says what a misaligned one does. unmodeled. An aligned byte or half-word write is merged into the
//                    register's word and judged as that word.
//   TVI-PWR-READ     a read of an offset with no register (Table 49 p313: 0x14, 0x1C, 0x2C and up). unmodeled.
//   TVI-PWR-DBP      a PWR_CR2 write that would change it while DBP = 0: "After reset, PWR_CR2 register is write-protected"
//                    (p306), and RM0433 never says what a protected write does. unmodeled. ArduPilot's measured BREN = 1 with
//                    DBP = 0 (measure.md §3.1) is acknowledged by name and not taken (ruled Q12).
//   TVI-PWR-FIELD    the allow-list (`Fields`): a field given a value the model does not implement, or a write to an offset with
//                    no register; and rev Y's CR3 bit 6 written 1 (its reset value is 0x06, p307). unmodeled.
//   TVI-PWR-SUPPLY   PWR_CR3: a write that changes the lower byte after its first write, a write of an invalid LDOEN/BYPASS pair,
//                    or any write that changes the register after an invalid pair locked it (p308; Table 34 p261).
//                    predicted-fault; ignored in both modes.
//   TVI-PWR-VOS      PWR_D3CR: VOS changed while ACTVOSRDY = 0 (p262; unmodeled: no page says what silicon does), or VOS
//                    lowered below the scale the running CPU, AHB or APB clocks need (p310; DS12117 Rev 10 Table 111 p210;
//                    predicted-fault; unmodeled where VOS0 could hold them, or the clocks cannot be read).
// Recorded mode (RUL-19): every rule is evaluated and records what it finds, and the access goes on as if none had fired, except
// that TVI-PWR-SUPPLY's write is still ignored (ruled Q6) and ArduPilot's acknowledged BREN write is still not taken.

using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_PWR : IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IDisposable
    {
        // `revision` ("V" or "Y"; ruled Q7): CR3's reset value (0x46 rev V, 0x06 rev Y, p307) and the scales' limits depend on it.
        public GuardedSTM32H7_PWR(IMachine machine, string mode = Records.Enforcing, string revision = null)
        {
            this.machine = machine;
            Revision = revision;
            records = new Records(machine, this, nameof(GuardedSTM32H7_PWR), mode,
                new[] { new KeyValuePair<string, string>("revision", revision ?? "none") });
            config = revision == "V" || revision == "Y" ? null
                : "no revision declared (`revision: \"V\"` or `\"Y\"`): PWR_CR3's reset value (p307) and the voltage scales' limits (DS12117 Rev 10 p210, p129-130) depend on it"
                    + (revision == null ? "" : ", and \"" + revision + "\" is neither");
            cr3 = revision == "Y" ? 0x06u : 0x46u;
            machine.PeripheralsChanged += OnPeripheralsChanged;
            Reset();
        }

        public long Size => 0x400;

        public string Revision { get; }

        // ---- the other guards' read API (ruled Q9, Q10, Q21) ---------------------------------------------------

        // The voltage scale in force, 1-3 (VOS0 is VOS1 with SYSCFG_PWRCR.ODEN, p280-281, which this guard does not hold).
        public int Scale
        {
            get
            {
                lock(sync)
                {
                    return ScaleOf(vos);
                }
            }
        }

        public string ScaleCitation
        {
            get
            {
                lock(sync)
                {
                    return "PWR_D3CR.VOS = " + Bin(vos, 2) + " (Scale " + ScaleOf(vos) + "), held by the PWR guard (RM0433 Rev 9 §6.8.6 p310)";
                }
            }
        }

        // PWR_CR1.DBP (p305): 0 write-protects RCC_BDCR (p427), the RTC and PWR_CR2's BREN and MONEN.
        public bool Dbp
        {
            get
            {
                lock(sync)
                {
                    return (cr1 & DbpBit) != 0;
                }
            }
        }

        // DS12117 Rev 10 Table 111 p210 (rev V) and Table 39 p129-130 (rev Y), for `revision`, against bus clocks in Hz: CPU (rcc_c_ck), AHB
        // (rcc_hclk), APB1-APB4 (rcc_pclk1-4); `apbEnabled`, per APB bus, whether a peripheral on it is enabled (VOS3's APB limit
        // holds "when at least one peripheral is enabled", footnote 6, on VOS3's APB cell only, read on the rendered page). Null
        // when every clock is within `scale`'s limits; otherwise the first one above, with its kind: unmodeled when VOS0 (rev V,
        // scale 1 with ODEN, which the twin cannot read) would hold every clock, else predicted-fault. Changes nothing.
        public static string Exceeds(string revision, double[] clocks, bool[] apbEnabled, int scale, out string kind)
        {
            kind = null;
            var table = revision == "V" ? LimitsV : revision == "Y" ? LimitsY : null;
            if(table == null || clocks == null || scale < 1 || scale > 3)
            {
                return null;
            }
            for(var i = 0; i < BusNames.Length; i++)
            {
                var max = table[scale][i];
                if(max == 0 || clocks[i] <= max * 1e6 || (scale == 3 && i >= 2 && !apbEnabled[i - 2]))
                {
                    continue;
                }
                var vos0 = revision == "V" && scale == 1 && clocks.Select((c, j) => c <= LimitsV[0][j] * 1e6).All(x => x);
                kind = vos0 ? Unmodeled : PredictedFault;
                return BusNames[i] + " at " + clocks[i] + " Hz, above " + max + " MHz at VOS" + scale + " (revision " + revision + ")"
                    + (vos0 ? ": within VOS0's limits, which need SYSCFG_PWRCR.ODEN (RM0433 Rev 9 p280-281, p561), which the twin cannot read" : "");
            }
            return null;
        }

        public const string LimitsCitation = "DS12117 Rev 10 Table 111 p210 (rev V: CPU, AHB, APB per scale; APB at VOS3 \"when at least one peripheral is enabled\", footnote 6), Table 39 p129-130 (rev Y: CPU only)";

        public void Reset()
        {
            lock(sync)
            {
                // A system reset: CR1, CPUCR, D3CR and the wakeup registers at their reset values (p304, p309, p310: "After reset,
                // the system starts on the lowest Run mode voltage scaling (VOS3)", p280; p311-312). CR3 is reset only by POR (p307)
                // and CR2 only by VSW POR (p306): neither is touched here, so the supply configuration stays written once.
                cr1 = Cr1Reset;
                vos = 1;
                cpucr = 0;
                wkupepr = 0;
            }
        }

        public void Dispose()
        {
            records.End();
        }

        // ---- bus access ----------------------------------------------------------------------------------------

        public uint ReadDoubleWord(long offset)
        {
            return Read(offset, 32);
        }

        public void WriteDoubleWord(long offset, uint value)
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
                if(Unloaded(offset, 0, width))
                {
                    return 0;
                }
                var b = offset & ~3L;
                if(Misaligned(offset, 0, width, "read") && !records.Recorded)
                {
                    return 0;
                }
                if(!Fields.Any(f => f.Offset == b))
                {
                    records.Refuse("TVI-PWR-READ", Unmodeled, "none", "read of offset 0x" + b.ToString("X"),
                        "read of a PWR offset with no register: the twin cannot say what silicon returns",
                        "RM0433 Rev 9 §6.8.10 Table 49 p313", State(), offset, 0, width);
                    if(!records.Recorded)
                    {
                        return 0;
                    }
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
                var b = offset & ~3L;
                var shift = (int)(8 * (offset & 3));
                var mask = width == 32 ? 0xFFFFFFFFu : width == 16 ? 0xFFFFu : 0xFFu;
                var word = width == 32 && shift == 0 ? value : (uint)((Served(b) & ~((ulong)mask << shift)) | ((ulong)(value & mask) << shift));
                // The bytes of the register this access writes: CR3's lower byte is "written" only by an access that covers byte 0.
                var lowerByte = shift == 0;
                var refused = Misaligned(offset, value, width, "write");
                if(!refused || records.Recorded)
                {
                    if(b == Cr2Offset && RefusedDbp(word, width, ref refused))
                    {
                        return;   // ArduPilot's acknowledged write: not taken, no record (ruled Q12)
                    }
                }
                if(!refused || records.Recorded)
                {
                    refused |= RefusedField(b, word, width);
                }
                if(b == Cr3Offset)
                {
                    if(!refused || records.Recorded)
                    {
                        WriteCr3(word, width, lowerByte, refused);
                    }
                    return;
                }
                if(!refused || records.Recorded)
                {
                    refused |= b == D3crOffset && RefusedVos(word, width);
                }
                if(refused && !records.Recorded)
                {
                    return;
                }
                Pass(b, word);
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
            records.Refuse("TVI-PWR-CONFIG", Unmodeled, RegisterAt(offset & ~3L), "none",
                "the platform gives this PWR guard what it cannot model: " + c + "; every access refuses",
                "TVI-WO-1308.measure.md §3, §6; ruled 2026-11-26T09:31Z Q7, Q13", "none", offset, value, width);
            return true;
        }

        private string Config()
        {
            if(config != null)
            {
                return config;
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
                if(start != PwrBase)
                {
                    return "bus address 0x" + start.ToString("X") + ", which is not PWR's (0x58024800, RM0433 Rev 9 Table 8 p132)";
                }
                placed = true;
            }
            return null;
        }

        private bool Misaligned(long offset, ulong value, int width, string what)
        {
            if((offset & (width / 8 - 1)) == 0)
            {
                return false;
            }
            records.Refuse("TVI-PWR-WIDTH", Unmodeled, RegisterAt(offset & ~3L), "misaligned " + width + "-bit " + what,
                "a " + width + "-bit PWR " + what + " at offset 0x" + offset.ToString("X")
                    + ": RM0433 allows word, half-word and byte accesses and states nothing of a misaligned one, so the twin cannot say what silicon does",
                "RM0433 Rev 9 §6.8 p304 (\"The PWR registers can be accessed in word, half-word and byte format\")", State(), offset, value, width);
            return true;
        }

        // TVI-PWR-DBP (p306; ruled Q10, Q12). True when the write is ArduPilot's acknowledged one, which is not taken.
        private bool RefusedDbp(uint word, int width, ref bool refused)
        {
            if((cr1 & DbpBit) != 0 || (word & Cr2Writable) == (cr2 & Cr2Writable))
            {
                return false;   // DBP = 1, or a write that changes neither BREN nor MONEN (RUL-18 as amended)
            }
            if(word == BrenBit && cr2 == 0)
            {
                return true;   // ChibiOS hal_lld.c L111 before L61 (measure.md §6 Q12): acknowledged by name, BREN stays 0
            }
            records.Refuse("TVI-PWR-DBP", Unmodeled, "PWR_CR2", "BREN/MONEN with PWR_CR1.DBP = 0",
                "PWR_CR2 written 0x" + word.ToString("X8") + " while PWR_CR1.DBP = 0: \"After reset, PWR_CR2 register is write-protected\", and RM0433 never says what a protected write does, so the twin cannot say what silicon holds",
                "RM0433 Rev 9 §6.8.3 p306; §6.8.1 p305 (DBP); ruled 2026-11-26T09:31Z Q10, Q12", State(), Cr2Offset, word, width);
            refused = true;
            return false;
        }

        private bool RefusedField(long b, uint word, int width)
        {
            var rows = Fields.Where(f => f.Offset == b).ToArray();
            if(rows.Length == 0)
            {
                records.Refuse("TVI-PWR-FIELD", Unmodeled, "none", "write to offset 0x" + b.ToString("X"),
                    "write to a PWR offset with no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §6.8.10 Table 49 p313", State(), b, word, width);
                return true;
            }
            foreach(var f in rows)
            {
                var v = f.Of(word);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    records.Refuse("TVI-PWR-FIELD", Unmodeled, f.Register, f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        f.Register + "." + f.Name + " written " + f.Format(v) + ": the model implements only " + string.Join(", ", f.Allowed.Select(f.Format))
                            + " (" + f.BehavesAs + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; the allow-list (RUL-17)", State(), b, word, width);
                    return true;
                }
            }
            if(b == Cr3Offset && Revision == "Y" && (word & Cr3Bit6) != 0)
            {
                records.Refuse("TVI-PWR-FIELD", Unmodeled, "PWR_CR3", "Res6 (bit 6) = 1",
                    "PWR_CR3 bit 6 written 1 on revision Y, whose reset value is 0x00000006: bits 7:3 are \"Reserved, must be kept at reset value\"",
                    "RM0433 Rev 9 §6.8.4 p307-308; the allow-list (RUL-17)", State(), b, word, width);
                return true;
            }
            return false;
        }

        // TVI-PWR-SUPPLY (p308; Table 34 p261; ruled Q6). `refused`: an earlier rule refused (we are here only in recorded mode).
        private void WriteCr3(uint word, int width, bool lowerByte, bool refused)
        {
            var low = word & 0xFF;
            string why = null;
            if(locked)
            {
                if((word & Cr3Writable) != (cr3 & Cr3Writable))
                {
                    why = "PWR_CR3 written 0x" + word.ToString("X8") + " after an invalid LDOEN/BYPASS pair locked it: \"the written-once mechanism will lock the register and any further write access will be ignored ... The system shall be power cycled before writing a new value\"";
                }
            }
            else if(lowerByte && written && low != (cr3 & 0xFF))
            {
                why = "PWR_CR3's lower byte written 0x" + low.ToString("X2") + " after its first write (0x" + (cr3 & 0xFF).ToString("X2")
                    + "): \"The lower byte of this register is written once after POR\", so silicon keeps 0x" + (cr3 & 0xFF).ToString("X2") + " and ignores the write";
            }
            else if(lowerByte && !written && (low & LdoenBit) >> 1 == (low & BypassBit))
            {
                why = "PWR_CR3's supply configuration written with LDOEN = " + ((low & LdoenBit) >> 1) + ", BYPASS = " + (low & BypassBit)
                    + ", an illegal combination: \"data will not be written, the written-once mechanism will lock the register and any further write access will be ignored. The default supply configuration will be kept and the ACTVOSRDY bit ... will go on indicating invalid voltage levels\"";
                written = true;
                locked = true;   // the model: ignored and locked in both modes (p308)
            }
            if(why != null)
            {
                records.Refuse("TVI-PWR-SUPPLY", PredictedFault, "PWR_CR3", lowerByte && !locked ? "lower byte = 0x" + low.ToString("X2") : "PWR_CR3 = 0x" + word.ToString("X8"),
                    why + " (reset only by POR, p307: kept across a system reset)",
                    "RM0433 Rev 9 §6.8.4 p307-308; Table 34 p261; ruled 2026-11-26T09:31Z Q6", State(), Cr3Offset, word, width);
                if(!records.Recorded || locked)
                {
                    return;   // enforcing: nothing changes; a lock ignores the whole write in either mode
                }
                // recorded: the lower byte is still ignored (the model), the upper bytes go on as if no rule had fired
                cr3 = (cr3 & 0xFF) | (word & Cr3Writable & ~0xFFu);
                return;
            }
            if(refused && !records.Recorded)
            {
                return;
            }
            if(locked)
            {
                return;   // the same value to a locked register: ignored, and nothing changes
            }
            if(lowerByte && !written)
            {
                written = true;
                cr3 = word & Cr3Writable;
                return;
            }
            cr3 = (cr3 & 0xFF) | (word & Cr3Writable & ~0xFFu);   // the upper bytes: "No limitation applies to the upper bytes" (p308)
        }

        // TVI-PWR-VOS (p262, p310; ruled Q9).
        private bool RefusedVos(uint word, int width)
        {
            var next = word >> 14 & 3;
            if(next == vos || ScaleOf(next) == ScaleOf(vos) && ActvosRdy)
            {
                return false;   // no change of VOS (or 00 for 01, the same Scale 3, once VOS may change)
            }
            if(!ActvosRdy)
            {
                records.Refuse("TVI-PWR-VOS", Unmodeled, "PWR_D3CR", "VOS (15:14) = " + Bin(next, 2),
                    "PWR_D3CR.VOS changed from " + Bin(vos, 2) + " to " + Bin(next, 2) + " while PWR_CSR1.ACTVOSRDY = 0 (" + (written ? "the supply configuration locked by an invalid pair" : "no supply configuration written yet")
                        + "): \"As long as ACTVOSRDY indicates that voltage levels are invalid, the system is in Run* mode ... and VOS shall not be changed\"; no page says what silicon does with the write",
                    "RM0433 Rev 9 §6.4.1 p262; p265; §6.8.6 p310", State(), D3crOffset, word, width);
                return true;
            }
            if(ScaleOf(next) <= ScaleOf(vos))
            {
                return false;   // raising the scale (or keeping it): VOS before the frequency is p310's order
            }
            var rcc = machine.GetPeripheralsOfType<GuardedSTM32H7_RCC>().FirstOrDefault();
            string why;
            var kind = Unmodeled;
            if(rcc == null)
            {
                why = "no RCC guard on this platform: the clocks the scale must hold cannot be read";
            }
            else
            {
                var clocks = rcc.BusClocks(out var apb, out var none);
                why = clocks == null ? "the running clocks cannot be computed (" + none + ")" : Exceeds(Revision, clocks, apb, ScaleOf(next), out kind);
                if(why == null)
                {
                    return false;
                }
            }
            records.Refuse("TVI-PWR-VOS", kind, "PWR_D3CR", "VOS (15:14) = " + Bin(next, 2),
                "PWR_D3CR.VOS lowered from Scale " + ScaleOf(vos) + " to Scale " + ScaleOf(next) + " under the running clocks: " + why
                    + ": \"When decreasing performance, the system frequency shall first be decreased before changing the voltage scaling\"; \"otherwise the system might be unreliable\"",
                "RM0433 Rev 9 §6.8.6 p310; §6.6.4 p286; " + LimitsCitation + "; ruled 2026-11-26T09:31Z Q9", State(), D3crOffset, word, width);
            return true;
        }

        // ---- the registers -------------------------------------------------------------------------------------

        private bool ActvosRdy => written && !locked;   // ruled Q5: at once at a valid supply configuration (p306, p308)

        private uint Served(long b)
        {
            switch(b)
            {
            case Cr1Offset: return cr1;
            case Csr1Offset: return ActvosCode(vos) << 14 | (ActvosRdy ? ActvosRdyBit : 0);   // ACTVOS: "the last VOS value applied" (p306)
            case Cr2Offset: return cr2;
            case Cr3Offset: return cr3 | ((cr3 & Usb33denBit) != 0 ? Usb33rdyBit : 0);        // USB33RDY at once (ruled Q5)
            case CpucrOffset: return cpucr;
            case D3crOffset: return vos << 14 | VosrdyBit;                                     // VOSRDY 1 (p310; ruled Q5)
            case WkupeprOffset: return wkupepr;
            default: return 0;                                                                  // WKUPCR, WKUPFR (p311): read as 0
            }
        }

        private void Pass(long b, uint word)
        {
            switch(b)
            {
            case Cr1Offset: cr1 = word; break;
            case Cr2Offset: cr2 = word & Cr2Writable; break;
            case CpucrOffset: cpucr = word & CpucrWritable; break;   // CSSF "always read as 0" (p309)
            case D3crOffset: vos = word >> 14 & 3; break;
            case WkupeprOffset: wkupepr = word & WkupeprWritable; break;
            default: break;                                          // CSR1, WKUPFR read-only; WKUPCR always reads 0 (p311)
            }
        }

        private static int ScaleOf(uint v)
        {
            return v == 3 ? 1 : v == 2 ? 2 : 3;   // 11 Scale 1, 10 Scale 2, 01 Scale 3, 00 "Reserved (Scale 3 selected)" (p310)
        }

        private static uint ActvosCode(uint v)
        {
            return v == 0 ? 1u : v;
        }

        private static string RegisterAt(long b)
        {
            var f = Fields.FirstOrDefault(x => x.Offset == b);
            return f != null ? f.Register : "none";
        }

        private string State()
        {
            return "CR1=0x" + cr1.ToString("X8") + " CR2=0x" + cr2.ToString("X8") + " CR3=0x" + cr3.ToString("X8") + " (supply configuration "
                + (locked ? "locked by an invalid pair" : written ? "written" : "not written") + ") D3CR.VOS=" + Bin(vos, 2)
                + " ACTVOSRDY=" + (ActvosRdy ? 1 : 0) + " revision " + (Revision ?? "none");
        }

        private static string Bin(uint v, int width)
        {
            return Convert.ToString(v, 2).PadLeft(width, '0');
        }

        // Ruled Q7: a platform without a revision refuses at load, by name (after the witness, which Records writes first).
        private void OnPeripheralsChanged(IMachine m, PeripheralsChangedEventArgs args)
        {
            if(args.Peripheral == this && args.Operation == PeripheralsChangedEventArgs.PeripheralChangeType.NameChanged)
            {
                m.PeripheralsChanged -= OnPeripheralsChanged;
                if(config != null && !records.LoadFailed)
                {
                    records.Refuse("TVI-PWR-CONFIG", Unmodeled, "none", "revision", "pwr: " + config + "; every access refuses",
                        "TVI-WO-1308.measure.md §6 Q7; ruled 2026-11-26T09:31Z Q7", "none", 0, 0, 0);
                }
            }
        }

        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";
        private const ulong PwrBase = 0x58024800;
        private const long Cr1Offset = 0x00;
        private const long Csr1Offset = 0x04;
        private const long Cr2Offset = 0x08;
        private const long Cr3Offset = 0x0C;
        private const long CpucrOffset = 0x10;
        private const long D3crOffset = 0x18;
        private const long WkupeprOffset = 0x28;
        private const uint Cr1Reset = 0xF000C000;      // p304's printed reset value (the p313 map gives 0x0000C000; ruled Q11)
        private const uint DbpBit = 1u << 8;           // p305
        private const uint BrenBit = 1u << 0;          // p307
        private const uint Cr2Writable = 0x00000011;   // MONEN, BREN (p306-307)
        private const uint Cr3Writable = 0x0300037F;   // USBREGEN, USB33DEN, VBRS, VBE, bits 6:0 (p308)
        private const uint Cr3Bit6 = 1u << 6;
        private const uint LdoenBit = 1u << 1;
        private const uint BypassBit = 1u << 0;
        private const uint Usb33denBit = 1u << 24;
        private const uint Usb33rdyBit = 1u << 26;
        private const uint ActvosRdyBit = 1u << 13;    // p305-306
        private const uint VosrdyBit = 1u << 13;       // p310
        private const uint CpucrWritable = 0x00000807; // RUN_D3, PDDS_D3, PDDS_D2, PDDS_D1 (p309-310)
        private const uint WkupeprWritable = 0x0FFF3F3F;

        private static readonly string[] BusNames = { "CPU (rcc_c_ck)", "AHB (rcc_hclk)", "APB1 (rcc_pclk1)", "APB2 (rcc_pclk2)", "APB3 (rcc_pclk3)", "APB4 (rcc_pclk4)" };
        // MHz per scale, index 0 VOS0 .. 3 VOS3; columns CPU, AHB, APB1-APB4; 0 where the datasheet states no limit.
        private static readonly double[][] LimitsV =
        {
            new double[] { 480, 240, 120, 120, 120, 120 },
            new double[] { 400, 200, 100, 100, 100, 100 },
            new double[] { 300, 150, 75, 75, 75, 75 },
            new double[] { 200, 100, 50, 50, 50, 50 },
        };
        private static readonly double[][] LimitsY =
        {
            new double[] { 0, 0, 0, 0, 0, 0 },
            new double[] { 400, 0, 0, 0, 0, 0 },
            new double[] { 300, 0, 0, 0, 0, 0 },
            new double[] { 200, 0, 0, 0, 0, 0 },
        };

        private readonly IMachine machine;
        private readonly Records records;
        private readonly string config;
        private readonly object sync = new object();
        private bool placed;
        // Reset by a system reset (Reset).
        private uint cr1;
        private uint vos;
        private uint cpucr;
        private uint wkupepr;
        // Reset only by POR (CR3, p307) or VSW POR (CR2, p306): set at construction, never by Reset.
        private uint cr2;
        private uint cr3;
        private bool written;
        private bool locked;

        // ---- the allow-list (RUL-16, RUL-17): one row per field, every bit 31:0 of every register ---------------
        // Typed from RM0433 Rev 9 §6.8 (pp. 304-313).

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
        private static readonly uint[] NibbleQ11 = { 0, 15 };
        private static readonly uint[] SvosReset = { 3 };

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        private static readonly Row[] Fields =
        {
            R("PWR_CR1", 0x00, "§6.8.1", "Res31_28", 31, 28, NibbleQ11, "reserved", "reserved, served as written: 0000, or 1111, p304's printed reset value 0xF000C000, which the p313 map (0x0000C000) contradicts; ChibiOS hal_lld.c L110 writes 1111 on purpose (ruled 2026-11-26T09:31Z Q11)", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "Res27_19", 27, 19, Zero, "reserved", "reserved: must be kept at reset value", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "ALS", 18, 17, Zero, "tag", "its reset value only: the analog voltage detector is not modeled", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "AVDEN", 16, 16, Zero, "tag", "its reset value only: the analog voltage detector is not modeled", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "SVOS", 15, 14, SvosReset, "tag", "its reset value (11, SVOS3) only: Stop mode is not modeled", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "Res13_10", 13, 10, Zero, "reserved", "reserved: must be kept at reset value", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "FLPS", 9, 9, Zero, "tag", "its reset value only: flash low-power in Stop mode is not modeled", 304),
            R("PWR_CR1", 0x00, "§6.8.1", "DBP", 8, 8, Any, "modeled by the guard", "kept and served; 0 write-protects PWR_CR2 (TVI-PWR-DBP, p306) and RCC_BDCR (the RCC guard's TVI-RCC-DBP, p427); the RTC and backup SRAM it also protects are unguarded (ruled 2026-11-26T09:31Z Q10)", 305),
            R("PWR_CR1", 0x00, "§6.8.1", "PLS", 7, 5, Zero, "tag", "its reset value only: the programmable voltage detector is not modeled", 305),
            R("PWR_CR1", 0x00, "§6.8.1", "PVDE", 4, 4, Zero, "tag", "its reset value only: the programmable voltage detector is not modeled", 305),
            R("PWR_CR1", 0x00, "§6.8.1", "Res3_1", 3, 1, Zero, "reserved", "reserved: must be kept at reset value", 305),
            R("PWR_CR1", 0x00, "§6.8.1", "LPDS", 0, 0, Zero, "tag", "its reset value only: Stop mode is not modeled", 305),
            R("PWR_CSR1", 0x04, "§6.8.2", "Res31_17", 31, 17, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "AVDO", 16, 16, Any, "read-only", "read-only: a write is ignored; reads 0 (AVDEN is 0 only)", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "ACTVOS", 15, 14, Any, "read-only", "read-only: a write is ignored; reads the VOS in force (\"the last VOS value applied\", p306), 01 for VOS 00", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "ACTVOSRDY", 13, 13, Any, "read-only", "read-only: a write is ignored; 0 from POR (reset value 0x4000, p305), 1 at once at a valid supply configuration write: no figure in RM0433 Rev 9 or DS12117 Rev 10, acknowledged by name with bench row P1 (ruled 2026-11-26T09:31Z Q5); stays 0 after an invalid pair (p308)", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "Res12_5", 12, 5, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "PVDO", 4, 4, Any, "read-only", "read-only: a write is ignored; reads 0 (PVDE is 0 only)", 306),
            R("PWR_CSR1", 0x04, "§6.8.2", "Res3_0", 3, 0, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 306),
            R("PWR_CR2", 0x08, "§6.8.3", "Res31_24", 31, 24, Zero, "reserved", "reserved: must be kept at reset value", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "TEMPH", 23, 23, Any, "read-only", "read-only: a write is ignored; reads 0 (MONEN is 0 only)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "TEMPL", 22, 22, Any, "read-only", "read-only: a write is ignored; reads 0 (MONEN is 0 only)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "VBATH", 21, 21, Any, "read-only", "read-only: a write is ignored; reads 0 (MONEN is 0 only)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "VBATL", 20, 20, Any, "read-only", "read-only: a write is ignored; reads 0 (MONEN is 0 only)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "Res19_17", 19, 17, Zero, "reserved", "reserved: must be kept at reset value", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "BRRDY", 16, 16, Any, "read-only", "read-only: a write is ignored; reads 0 (BREN is 0 only)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "Res15_5", 15, 5, Zero, "reserved", "reserved: must be kept at reset value", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "MONEN", 4, 4, Zero, "tag", "its reset value only: VBAT and temperature monitoring are not modeled; while DBP = 0 a write that changes it is TVI-PWR-DBP (p306)", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "Res3_1", 3, 1, Zero, "reserved", "reserved: must be kept at reset value", 307),
            R("PWR_CR2", 0x08, "§6.8.3", "BREN", 0, 0, Zero, "tag", "its reset value only: the backup regulator is not modeled; while DBP = 0 a write that changes it is TVI-PWR-DBP (p306), except ArduPilot's measured BREN = 1 (CR2 = 0x00000001, ChibiOS hal_lld.c L111 before L61, its BRRDY wait at L131 commented out), acknowledged by name and not taken: BREN stays 0 (ruled 2026-11-26T09:31Z Q12); not reset by a system reset (VSW POR only, p306)", 307),
            R("PWR_CR3", 0x0C, "§6.8.4", "Res31_27", 31, 27, Zero, "reserved", "reserved: must be kept at reset value", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "USB33RDY", 26, 26, Any, "read-only", "read-only: a write is ignored; reads USB33DEN at once: no figure in RM0433 Rev 9 or DS12117 Rev 10, acknowledged by name with bench row P3 (ruled 2026-11-26T09:31Z Q5)", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "USBREGEN", 25, 25, Any, "modeled by the guard", "kept and served; an upper byte (\"No limitation applies to the upper bytes\", p308); the USB regulator itself is not modeled", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "USB33DEN", 24, 24, Any, "modeled by the guard", "kept and served; USB33RDY follows it at once (ruled 2026-11-26T09:31Z Q5)", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "Res23_10", 23, 10, Zero, "reserved", "reserved: must be kept at reset value", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "VBRS", 9, 9, Zero, "tag", "its reset value only: battery charging is not modeled", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "VBE", 8, 8, Zero, "tag", "its reset value only: battery charging is not modeled", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "Res6", 6, 6, Any, "reserved", "reserved, set by revision V's reset value 0x46 (p307): kept or cleared on rev V, ArduPilot's clear (0x02) acknowledged by name (ruled 2026-11-26T09:31Z Q7); on rev Y (reset 0x06) 1 refuses TVI-PWR-FIELD; part of the lower byte written once (TVI-PWR-SUPPLY)", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "Res5_3", 5, 3, Zero, "reserved", "reserved: must be kept at reset value", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "SCUEN", 2, 2, Any, "modeled by the guard", "kept and served; the lower byte is written once after POR (p308): a later write that changes it is TVI-PWR-SUPPLY, predicted-fault, ignored in both modes, kept across a system reset (p307; ruled 2026-11-26T09:31Z Q6, bench row P2)", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "LDOEN", 1, 1, Any, "modeled by the guard", "kept and served, written once (as SCUEN); LDOEN = BYPASS (00, 11) is illegal (Table 34 p261): ignored, locking the register until a power cycle with ACTVOSRDY 0 (p308), TVI-PWR-SUPPLY, predicted-fault", 308),
            R("PWR_CR3", 0x0C, "§6.8.4", "BYPASS", 0, 0, Any, "modeled by the guard", "kept and served, written once (as SCUEN); with LDOEN, Table 34 p261's pair (as LDOEN)", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "Res31_12", 31, 12, Zero, "reserved", "reserved: must be kept at reset value", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "RUN_D3", 11, 11, Zero, "tag", "its reset value only: the low-power modes are not modeled", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "Res10", 10, 10, Zero, "reserved", "reserved: must be kept at reset value", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "CSSF", 9, 9, Any, "modeled by the guard", "\"always read as 0\"; 1 clears STOPF, SBF, SBF_D1 and SBF_D2 (p309), which are 0 here (no low-power mode is modeled), so it changes nothing", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "SBF_D2", 8, 8, Any, "read-only", "read-only: a write is ignored; reads 0 (no DStandby is modeled)", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "SBF_D1", 7, 7, Any, "read-only", "read-only: a write is ignored; reads 0 (no DStandby is modeled)", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "SBF", 6, 6, Any, "read-only", "read-only: a write is ignored; reads 0 (no Standby is modeled)", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "STOPF", 5, 5, Any, "read-only", "read-only: a write is ignored; reads 0 (no Stop is modeled)", 309),
            R("PWR_CPUCR", 0x10, "§6.8.5", "Res4_3", 4, 3, Zero, "reserved", "reserved: must be kept at reset value", 310),
            R("PWR_CPUCR", 0x10, "§6.8.5", "PDDS_D3", 2, 2, Zero, "tag", "its reset value only: the low-power modes are not modeled", 310),
            R("PWR_CPUCR", 0x10, "§6.8.5", "PDDS_D2", 1, 1, Zero, "tag", "its reset value only: the low-power modes are not modeled", 310),
            R("PWR_CPUCR", 0x10, "§6.8.5", "PDDS_D1", 0, 0, Zero, "tag", "its reset value only: the low-power modes are not modeled", 310),
            R("PWR_D3CR", 0x18, "§6.8.6", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 310),
            R("PWR_D3CR", 0x18, "§6.8.6", "VOS", 15, 14, Any, "modeled by the guard", "kept and served, Scale 3 (01) after a system reset (p280); 00 \"Reserved (Scale 3 selected)\" is Scale 3; the scale the flash guard (TVI-FLASH-WAIT) and the RCC guard (TVI-RCC-VOS) ask for; changed while ACTVOSRDY = 0 it is TVI-PWR-VOS, unmodeled (p262); lowered under the running clocks it is TVI-PWR-VOS, predicted-fault (p310; DS12117 Rev 10 p210; ruled 2026-11-26T09:31Z Q9, bench row P4)", 310),
            R("PWR_D3CR", 0x18, "§6.8.6", "VOSRDY", 13, 13, Any, "read-only", "read-only: a write is ignored; reads 1 (\"Following reset VOSRDY will be read 1\", p310) and at once after a VOS write: no figure in RM0433 Rev 9 or DS12117 Rev 10, acknowledged by name with bench row P1 (ruled 2026-11-26T09:31Z Q5)", 310),
            R("PWR_D3CR", 0x18, "§6.8.6", "Res12_0", 12, 0, Zero, "reserved", "reserved: must be kept at reset value", 310),
            R("PWR_WKUPCR", 0x20, "§6.8.7", "Res31_6", 31, 6, Zero, "reserved", "reserved, always read as 0", 311),
            R("PWR_WKUPCR", 0x20, "§6.8.7", "WKUPC", 5, 0, Zero, "tag", "its reset value only: the wakeup pins are not modeled", 311),
            R("PWR_WKUPFR", 0x24, "§6.8.8", "Res31_6", 31, 6, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 311),
            R("PWR_WKUPFR", 0x24, "§6.8.8", "WKUPF", 5, 0, Any, "read-only", "read-only: a write is ignored; reads 0 (the wakeup pins are not modeled)", 311),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "Res31_28", 31, 28, Zero, "reserved", "reserved: must be kept at reset value", 312),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "WKUPPUPD", 27, 16, Zero, "tag", "its reset value only: the wakeup pins are not modeled", 312),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "Res15_14", 15, 14, Zero, "reserved", "reserved: must be kept at reset value", 312),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "WKUPP", 13, 8, Zero, "tag", "its reset value only: the wakeup pins are not modeled", 312),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "Res7_6", 7, 6, Zero, "reserved", "reserved: must be kept at reset value", 312),
            R("PWR_WKUPEPR", 0x28, "§6.8.9", "WKUPEN", 5, 0, Zero, "tag", "its reset value only: the wakeup pins are not modeled", 312),
        };
    }
}
