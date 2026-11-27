// Tviburi's guard over Renode's STM32H7_RCC (TVI-WO-1300). Every citation is to ST RM0433 Rev 9, chapter 8.
// The field table and every page cited here: docs/work-orders/TVI-WO-1300.rm0433.md.
//
// Insertion point (RUL-07, as amended): STM32H7_RCC is `sealed`, so this guard is a wrapper. It owns an unregistered
// stock STM32H7_RCC, built with the stock entry's constructor arguments, and forwards every passed access, every
// registration, unregistration and reset to it. It implements all three bus widths itself. The board description
// cancels the stock `rcc`, declares this guard at 0x58024400 `as "rcc"`, and restates the 58 stock registrations
// onto it (RUL-09 (b); model/stm32h7-rcc-restate.repl). The owned stock RCC is unregistered, so its own log lines
// carry no source name, and none can carry this guard's (TVI-WO-1150 (vii)).
//
// What this guard models (the query, below): whether a peripheral's kernel clock is running now, for spi1, spi2, i2c1,
// i2c2, usart1, usart2, usart3 and rng (TVI-WO-1300 (4); usart2/3 ruled 14:35Z). It keeps copies of
// every field the query reads that the stock model drops (PLLCKSELR, PLLCFGR's DIVxyEN, RGE, VCOSEL and FRACEN, the kernel clock
// selectors, the *ENR bits as written), serves those fields on read from the copies (RUL-17), and models each kernel clock
// mux's current input (p371-372): a mux switches to a new selector value only while a clock is present on both its
// current and its new input; until then no clock reaches the peripheral, and a system reset changes the selector
// back to its reset value but not the mux's input.
//
// The kernel clock's frequency, in one place (TVI-WO-1300c, ruled 2026-11-26T04:03Z): KernelHz (a peripheral's kernel clock)
// and BusHz (sys_ck, rcc_hclk) are the overlay's only PLL and sys_ck rule; the USART, ADC, I2C and flash guards ask them and keep
// no copy. PllHz states what it allows, by page; a configuration RM0433 does not allow is "no rule", never a number.
//
// Enables at reset (TVI-WO-1300a (1)): the *ENR copies start at 0, as silicon, and are written into the owned stock RCC
// after each registration and at reset. Its write callbacks lock every registered peripheral whose enable is 0 on the bus:
// a write is dropped and a read returns 0, as p373 says for the write ("A write access will be ignored"). The stock model
// starts every peripheral enabled, and the bus's lock survives a reset; neither stands (measured, TVI-WO-1300a.measure.md
// §1.1). A guarded peripheral registered here (an SPI guard) stays unlocked on the bus: its own guard asks this guard for
// its enable at every access and refuses by name (TVI-SPI-ENABLE).
//
// RTCEN (TVI-WO-1300b (5); ruled 2026-11-25T03:03Z Q4): served from the guard's copy, 0 at construction and kept across a
// reset (p427); the stock model, which locks `rtc` while its RTCEN is 0, is always written RTCEN = 1, since the RTC's
// registers follow RTCAPBEN (p471). Why that is safe: the RCC_BDCR.RTCEN row.
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD        (Records) the model version could not be computed: every access refuses.
//   TVI-RCC-WIDTH   a misaligned access; and a half-word or byte access to any register but RCC_BDCR, RCC_CSR and RCC_RSR,
//                   for which alone §8.7 states "word, half-word and byte access" (p427, p429, p450). Those three take
//                   sub-word accesses, each checked as the 32-bit word it makes. Both firmwares use only 32-bit (§4).
//   TVI-RCC-READ    a read of an offset with no register (Table 84, p496-505).
//   TVI-RCC-HSE     (TVI-WO-1300b (4)) a CR write that sets HSEON on a platform that declares no hseFrequency: the twin
//                   cannot say what the HSE runs at (p384, HSEON; §8.5.2 p339, the HSE is an external crystal or clock).
//                   unmodeled. Every HSE-derived clock needs HSEON: SW = HSE is TVI-RCC-SW's, a PLL from the HSE is
//                   TVI-RCC-PLL-REF's, and per_ck on hse_ck does not run in the query.
//   TVI-RCC-PLL     a write silicon ignores because a PLL is on: PLLSRC with any PLL on (p399), DIVMx with PLLx on
//                   (p398-399), DIVPx/DIVQx/DIVRxEN with PLLx on (p400-401), RCC_PLLxDIVR changed with PLLx on (p403-410;
//                   TVI-WO-1300a). predicted-fault. Silicon blocks these writes, so no clock is lost by them.
//   TVI-RCC-PLL-REF a CR write after which an enabled PLL has no reference clock: PLLxON set while PLLSRC = 11 ("No clock
//                   send to DIVMx divider and PLLs", p399), its source off, or DIVMx = 0 ("prescaler disabled",
//                   p398-399); or the source of an enabled PLL cleared. The stock model reports PLLxRDY = PLLxON
//                   whatever the reference; silicon cannot lock. For PLL1's source, RM0433 contradicts itself: the
//                   register descriptions say it "cannot be cleared if ... selected as reference clock for PLL1 with
//                   PLL1 enabled" (p384-386), §8.5.2 adds "and selected to provide the system clock" (p340-342), and the
//                   stock model follows §8.5.2. Either way the twin cannot say, so it is unmodeled (ruled 14:35Z), judged
//                   on the RCC_CR value the stock model keeps (it reverts a clear of the system clock's source).
//   TVI-RCC-SW      a CFGR write that switches SW to a source that is not running: silicon switches "only if the target
//                   clock source is ready" (p350; SW p393), while the stock model switches at once (and, for PLL1 with
//                   DIVM1 = 0, divides by zero). unmodeled.
//   TVI-RCC-FIELD   the allow-list (`Fields`): a write that gives any field a value the model does not implement,
//                   or a write to an offset with no register. unmodeled.
//   TVI-RCC-DBP     (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10) an RCC_BDCR write that changes it while the PWR guard's
//                   PWR_CR1.DBP = 0: "After a system reset, the RCC_BDCR register is write-protected" (p427), and RM0433 never
//                   says what a protected write does. unmodeled. With no PWR guard on the platform, the stock pwr stub reads DBP = 1.
//   TVI-RCC-VOS     (TVI-WO-1308, ruled Q9; 11:06Z Q20 (A)) a write (RCC_CR, CFGR, D1-D3CFGR, PLLCKSELR, PLLCFGR, PLL1DIVR, or an
//                   APB enable) after which the CPU, AHB or an APB clock is above DS12117 Rev 10 Table 111 p210's limit for the
//                   scale the PWR guard holds ("the voltage scaling shall be changed before increasing the system frequency",
//                   p310; "otherwise the system might be unreliable", p286). predicted-fault; unmodeled where only VOS0 (VOS1 with
//                   SYSCFG_PWRCR.ODEN, p280-281, which the twin cannot read) would hold the clocks and the LDO is on (VOS0 "is
//                   available only when the LDO regulator is ON", DS12117 p210 note 4). Also unmodeled: a write that changes sys_ck
//                   while the PWR guard's ACTVOSRDY = 0 (CR3 "shall be written before changing ... ck_sys clock frequency", p308;
//                   Run*, p262). Judged only on a platform with a PWR guard: with none, not judged at all.
//   TVI-RCC-CLOCK   (TVI-WO-1300a (3); 1300b (2)) a write after which a guarded peripheral that runs (an SPI with SPE = 1, or a
//                   USART guard with UE = 1, ruled 2026-11-25T03:03Z Q2) loses the
//                   kernel clock it had, by the query on the state the write leaves: a source oscillator or PLLxON cleared,
//                   a selector or CKPERSEL changed so that its mux waits (p371-372), or the peripheral's own PERxEN cleared
//                   (Table 62, p376). predicted-fault (p371: "no clock will be provided to the peripheral"). A clear silicon
//                   blocks (an oscillator or PLL1ON on the system clock's path, p383-386) is modeled as the block: the stock
//                   model keeps the bit, and nothing is lost. PLL1's source cleared with PLL1 on is TVI-RCC-PLL-REF's (above).
//   TVI-RCC-KERNEL  (RequireKernelClock) a peripheral started with no kernel clock: predicted-fault, naming the selector,
//                   its source and the pages; unmodeled for a peripheral the query does not cover.
// Recorded mode (RUL-19) is model/Refusal.cs's contract: every rule is evaluated and records, and the access goes on to the
// stock model and the copies as if no rule had fired.
// Its one exception here: in recorded mode with no crystal declared, the stock RCC runs on a placeholder 8 MHz so a
// run can continue past TVI-RCC-HSE; no guard reads it (the constructor).
// Enabling a bus clock before its kernel source runs is valid and never refused (architect, 2026-11-23). Whether
// a peripheral's start finds its kernel clock is that peripheral's guard's rule, through the query (TVI-SPI-KERNEL);
// RequireKernelClock asks the same question from the monitor, recorded under rcc, for a peripheral with no guard yet.

using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Miscellaneous;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    // TVI-WO-1306 (ruled 2026-11-25T05:06Z Q4): a guard this RCC guard watches and never unlocks (TVI-WO-1300b (2)): the USART
    // guards and the I2C guards. Registered on it, it is held (Holds) and watched for a kernel clock lost while it runs
    // (TVI-RCC-CLOCK); not registered, it refuses every access by its own rule.
    public interface IRccWatched : IPeripheral
    {
        bool IsRunning { get; }          // UE = 1 (a USART), PE = 1 (an I2C port)
        string RunningName { get; }      // "UE", "PE": named in TVI-RCC-CLOCK's record
        string KernelCitation { get; }   // its own chapter's kernel-clock page, appended to TVI-RCC-CLOCK's citation
    }

    public class GuardedSTM32H7_RCC : IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IDisposable,
        IRegisterablePeripheral<IPeripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias>>
    {
        // The stock entry's constructor arguments (stm32h7.repl: `systick: nvic`); the defaults are the stock model's.
        // Every property the stock entry takes is the stock model's, passed through: a consumer sets hseFrequency (or
        // csiFrequency) on this entry, never on the cancelled `rcc` (RUL-09 (b); TVI-WO-1300.rm0433.md §6.4).
        // hseFrequency has no default (TVI-WO-1300b (4)): HseFrequency stays null, and every guard rule and query reads it. The
        // stock RCC gets 0 in enforcing mode, where TVI-RCC-HSE stops the run first, and the header's placeholder in recorded mode:
        // given 0 there, the forwarded SW = HSE write threw "Frequency must be greater than 0" (measured, TVI-WO-1204 (3) P2).
        public GuardedSTM32H7_RCC(IMachine machine, IHasDivisibleFrequency systick = null, ulong? hseFrequency = null,
            ulong csiFrequency = 4000000, string mode = Records.Enforcing)
        {
            HseFrequency = hseFrequency == 0 ? null : hseFrequency;   // 0 Hz is no frequency: declared 0 is none (1300b adversarial)
            CsiFrequency = csiFrequency;
            inner = new STM32H7_RCC(machine, systick, HseFrequency ?? (mode == Records.RecordedMode ? RecordedHsePlaceholder : 0UL), csiFrequency);
            host = machine;
            records = new Records(machine, this, nameof(GuardedSTM32H7_RCC), mode);
            muxes = new[] { PerMux, SpiMux, I2cMux, UsartMux, Usart2Mux, RngMux, AdcMux }.Select(m => new MuxState(m)).ToArray();
            ResetCopies();
        }

        public long Size => 0x400;

        // The platform's constructor arguments, so a test can show which reached the guard. HseFrequency is null when the
        // platform declares none: a reader of it (the flash and USART guards) has no HSE frequency then.
        public ulong? HseFrequency { get; }

        // The header's placeholder: the stock model's own default (STM32H7_RCC.DefaultHseFrequency, private there).
        private const ulong RecordedHsePlaceholder = 8000000;
        public ulong CsiFrequency { get; }

        public void Reset()
        {
            inner.Reset();
            ResetCopies();
            SyncEnables();
        }

        public void Dispose()
        {
            records.End();
        }

        public void Register(IPeripheral peripheral, EnumRegistrationPoint<STM32H7_RCC.RegistrationPeripheralAlias> registrationPoint)
        {
            inner.Register(peripheral, registrationPoint);
            if(peripheral is GuardedSTM32H7_SPI spi)
            {
                guarded[registrationPoint.Name.ToString().ToLowerInvariant()] = spi;
            }
            if(peripheral is IRccWatched w)
            {
                watched[registrationPoint.Name.ToString().ToLowerInvariant()] = w;   // watched, never unlocked (1300b (2))
            }
            SyncEnables();
        }

        public void Unregister(IPeripheral peripheral)
        {
            inner.Unregister(peripheral);
            foreach(var key in guarded.Where(g => g.Value == peripheral).Select(g => g.Key).ToArray())
            {
                guarded.Remove(key);
                host.SystemBus.SetPeripheralEnabled(peripheral, true);
            }
            foreach(var key in watched.Where(w => w.Value == peripheral).Select(w => w.Key).ToArray())
            {
                watched.Remove(key);
                host.SystemBus.SetPeripheralEnabled(peripheral, true);   // reachable, so its own guard refuses it (not held)
            }
        }

        // Whether this guard holds `p` (an SPI guard, or a watched USART or I2C guard) registered under `name`: only then does it watch its clock
        // (TVI-RCC-CLOCK), and keep an SPI unlocked, so a guard that is not held refuses rather than run unwatched.
        public bool Holds(string name, IPeripheral p)
        {
            return name != null && ((guarded.TryGetValue(name, out var g) && g == p) || (watched.TryGetValue(name, out var u) && u == p));
        }

        // Whether P's bus-clock enable is set in the guard's copy, for P's own guard (TVI-SPI-ENABLE). False when the
        // query has no rule for P. Allocates nothing: it is asked at every access to a guarded peripheral.
        public bool TryGetEnable(string peripheral, out bool enabled, out string enableName, out string enablePage)
        {
            if(peripheral == null || !Clocked.TryGetValue(peripheral, out var p))
            {
                enabled = false;
                enableName = enablePage = null;
                return false;
            }
            enabled = Enabled(p);
            enableName = p.EnableName;
            enablePage = p.EnablePage;
            return true;
        }

        // TVI-WO-1304: raised after every write this guard passes, with its offset and value, so that the flash guard judges
        // the AXI clock the write leaves against FLASH_ACR (RM0433 Rev 9 Table 17 p160). Its one call into the flash guard
        // (ruled 2026-11-24T15:50Z Q6); the flash guard reads the clock back from this guard's registers.
        public event Action<long, uint> Written;

        // ---- the query -----------------------------------------------------------------------------------------

        // "Is peripheral P's kernel clock running now", by the guard's tracked state. Peripheral names are the
        // stock instances' (spi1, spi2, i2c1, i2c2, usart1, rng). Unknown names answer Modeled = false.
        public KernelClock QueryKernelClock(string peripheral)
        {
            if(!Clocked.TryGetValue(peripheral, out var p))
            {
                return new KernelClock(peripheral, false, false, "no kernel clock rule for " + peripheral, "none",
                    "TVI-WO-1300 (4): the query covers spi1, spi2, i2c1, i2c2, usart1, usart2, usart3, rng, adc12 and adc3");
            }
            var mux = muxes.Single(m => m.Def == p.Mux);
            var enabled = Enabled(p);
            var selector = p.Mux.Register + "." + p.Mux.Field + " = " + Bin(mux.Selector, p.Mux.Width);
            var citation = "RM0433 Rev 9 " + p.EnablePage + " (" + p.EnableName + "); p" + p.Mux.Page + " (" + p.Mux.Field + "); "
                + "p371-372 (a kernel clock switch needs a clock on both inputs); p376-377 Table 62 (PERxEN gates the kernel clock)";
            if(!enabled)
            {
                return new KernelClock(peripheral, true, false, p.EnableName + " = 0: no clock is provided to the peripheral",
                    selector, citation);
            }
            if(mux.Current != mux.Selector)
            {
                return new KernelClock(peripheral, true, false, "the mux has not switched from " + p.Mux.Sources[mux.Current]
                    + " (" + Condition(p.Mux.Sources[mux.Current]) + ") to " + p.Mux.Sources[mux.Selector] + " ("
                    + Condition(p.Mux.Sources[mux.Selector]) + "): no clock is provided to the peripheral", selector, citation);
            }
            var source = p.Mux.Sources[mux.Current];
            var running = Running(source);
            return new KernelClock(peripheral, true, running, source + ": " + Condition(source), selector,
                citation + "; " + SourceCitation(source) + (source == "per_ck" ? "; " + SourceCitation(PerMux.Sources[muxes[0].Current]) : ""));
        }

        // The kernel clock's frequency, in one place (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q1-Q10; first granted to TVI-WO-1306a,
        // 2026-11-26T00:22Z Q2): the only PLL and sys_ck rule in the overlay. The USART, ADC, I2C and flash guards ask it, read-only.
        // P's kernel-clock frequency in Hz, from the selector's settled input (as QueryKernelClock), RCC_PLLCKSELR and RCC_PLLCFGR
        // from this guard's copies, RCC_PLLxDIVR as last written (`divr`, silicon's value; ruled Q2), and the stock-stored RCC_CR
        // (HSIDIV), RCC_CFGR (SWS), RCC_D1CFGR and RCC_D2CFGR. Null, with the reason, where the query says the clock does not run or
        // has no rule, or where this guard has no rule for its frequency (lse_ck, I2S_CKIN, an HSE with no hseFrequency, pll3_q_ck on
        // a USART (the WO's NOT list; ruled Q10), a PLL outside what PllHz allows). It changes nothing.
        public double? KernelHz(string peripheral, out string why)
        {
            var k = QueryKernelClock(peripheral);
            if(!k.Modeled || !k.Running)
            {
                why = k.Condition;
                return null;
            }
            var p = Clocked[peripheral];
            var source = p.Mux.Sources[muxes.Single(m => m.Def == p.Mux).Current];
            why = k.Selector + " (" + source + ")";
            if(source == "pll3_q_ck" && (p.Mux == UsartMux || p.Mux == Usart2Mux))
            {
                why += ": no rule for its frequency on a USART (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q10)";
                return null;
            }
            return SourceHz(source, ref why);
        }

        // A bus clock's frequency in Hz, with no query (ruled 2026-11-26T04:03Z Q5): "sys_ck" (SWS, p393) or "rcc_hclk" (sys_ck /
        // D1CPRE / HPRE, pp. 394-395; the AXI clock rcc_aclk and the ADC's adc_hclk). Null, with the reason, as KernelHz. It changes nothing.
        public double? BusHz(string clock, out string why)
        {
            why = clock;
            if(clock != "sys_ck" && clock != "rcc_hclk")
            {
                why += ": no rule for its frequency";
                return null;
            }
            var sys = SysHz(ref why);
            var d1 = inner.ReadDoubleWord(0x018);
            return clock == "sys_ck" ? sys : sys / Prescaler(d1 >> 8 & 0xF) / Prescaler(d1 & 0xF);
        }

        private double Hsi()
        {
            return 64e6 / (1 << (int)(Cr() >> 3 & 3));   // hsi_ck = 64 MHz / HSIDIV (p385)
        }

        // sys_ck by SWS (p393): the HSI, the CSI, the HSE or PLL1's P output.
        private double? SysHz(ref string why)
        {
            return SysHz(inner.ReadDoubleWord(CfgrOffset) >> 3 & 7, ref why);
        }

        // sys_ck with a given SWS: the stock model's, or during TVI-RCC-VOS's trial the SW a CFGR write switches to.
        private double? SysHz(uint sws, ref string why)
        {
            var sys = sws == 0 ? Hsi() : sws == 1 ? CsiFrequency : sws == 2 ? (double?)HseFrequency : sws == 3 ? PllHz(1, 'p', ref why) : null;
            if(sys == null)
            {
                why += sws > 3 ? ": RCC_CFGR.SWS = " + Bin(sws, 3) + ", no rule for its frequency" : sws == 2 ? ": sys_ck on the HSE with no hseFrequency" : "";
            }
            return sys;
        }

        private double? SourceHz(string source, ref string why)
        {
            switch(source)
            {
            case "rcc_pclk1":
            case "rcc_pclk2":
                // rcc_hclk (BusHz) / D2PPRE1 or D2PPRE2 (p396)
                var hclk = BusHz("rcc_hclk", out var w);
                if(hclk == null)
                {
                    why += w.Substring("rcc_hclk".Length);
                    return null;
                }
                var ppre = inner.ReadDoubleWord(0x01C) >> (source == "rcc_pclk2" ? 8 : 4) & 7;   // 0xx /1; 100-111 /2-/16
                return hclk / ((ppre & 4) == 0 ? 1 : 2 << (int)(ppre & 3));
            case "hsi_ker_ck":
                return Hsi();
            case "csi_ker_ck":
                return CsiFrequency;
            case "hse_ck":
                if(HseFrequency == null)
                {
                    why += ": no hseFrequency";
                }
                return HseFrequency;
            case "per_ck":
                return SourceHz(PerMux.Sources[muxes[0].Current], ref why);
            }
            if(source.StartsWith("pll", StringComparison.Ordinal))
            {
                return PllHz(source[3] - '0', source[5], ref why);
            }
            why += ": no rule for its frequency";
            return null;
        }

        // pllN_x_ck = vcoN_ck / (DIVxN + 1), vcoN_ck = refN_ck x (DIVNN + 1), refN_ck = the PLLSRC source / DIVMN (pp. 398-410), with
        // FRACN 0 (this guard's allow-list). No rule (null, with the page) unless every one of these holds (ruled 2026-11-26T04:03Z):
        //   refN_ck within 1-16 MHz and the VCO within its PLLNVCOSEL's range, 192-960 MHz or 150-420 MHz (p404, p407, p410);
        //   RCC_PLLNDIVR known as silicon holds it (Q2): not written after a write the stock model did not keep, nor while PLLN ran;
        //   nor DIVMN, PLLSRC, PLLNRGE or PLLNVCOSEL written while PLLN ran (p347; this WO's adversarial pass);
        //   PLLNRGE's range holds refN_ck, its edges inclusive (pp. 401-402, p347; Q3);
        //   the VCO choice: VCOH takes 2-16 MHz (p346; p347); VCOL takes 1-2 MHz, and VCOL above 2 MHz with its VCO in VCOL's range
        //     is acknowledged by name: ChibiOS chooses VCOL for any VCO at most 420 MHz, whatever its reference (hal_lld_type1.h
        //     L1834-1837), as ArduPilot sets PLL2 and PLL3 (Q1; bench row RC1);
        //   for pll1_p_ck, DIVP1 an even divisor: "odd division factors are not allowed" (p404) and "DIVPx = 2,4,6,...128 when x = 1"
        //     (Figure 47 p346), so neither DIVP1 = 0 (/1) nor an even DIVP1 (Q7).
        private double? PllHz(int n, char output, ref string why)
        {
            var sel = Copy(PllckselrOffset);
            var cfg = Copy(PllcfgrOffset);
            var divm = sel >> DivmShift[n] & 0x3F;
            var input = (sel & 3) == 0 ? Hsi() : (sel & 3) == 1 ? CsiFrequency : (sel & 3) == 2 ? (double?)HseFrequency : null;
            var d = divr[Pll1divrOffset + 8 * (n - 1)];
            var wide = (cfg >> (1 + 4 * (n - 1)) & 1) == 0;
            var refHz = input == null || divm == 0 ? 0 : input.Value / divm;
            var vco = refHz * ((d & 0x1FF) + 1);
            if(refHz < 1e6 || refHz > 16e6 || vco < (wide ? 192e6 : 150e6) || vco > (wide ? 960e6 : 420e6))
            {
                why += ": PLL" + n + " outside the ranges p404 gives, or with no reference (ref " + refHz + " Hz, VCO " + vco + " Hz): no rule for its frequency";
                return null;
            }
            if(cfgUnknown[n])
            {
                why += ": PLL" + n + "'s DIVM" + n + ", PLLSRC, PLL" + n + "RGE or PLL" + n + "VCOSEL written while it ran, which silicon does not take (p347, p398-399): no rule for its frequency";
                return null;
            }
            if(divrUnknown[n])
            {
                why += ": RCC_PLL" + n + "DIVR written after a write the stock model did not keep, or while PLL" + n + " ran: silicon's value cannot be known (TVI-WO-1300c Q2): no rule for its frequency";
                return null;
            }
            var rge = cfg >> (2 + 4 * (n - 1)) & 3;
            if(refHz < 1e6 * (1 << (int)rge) || refHz > 2e6 * (1 << (int)rge))
            {
                why += ": PLL" + n + "RGE = " + Bin(rge, 2) + " (" + (1 << (int)rge) + " to " + (2 << (int)rge) + " MHz) with ref" + n + "_ck at " + refHz + " Hz: \"must be set according to the reference input frequency\" (p347; pp. 401-402): no rule for its frequency";
                return null;
            }
            if(wide && refHz < 2e6)
            {
                why += ": PLL" + n + "VCOSEL = 0 (VCOH, 2 to 16 MHz, p346) with ref" + n + "_ck at " + refHz + " Hz: \"if the frequency of the reference clock is lower or equal to 2 MHz, then VCOL must be selected\" (p347): no rule for its frequency";
                return null;
            }
            var div = d >> (output == 'p' ? 9 : output == 'q' ? 16 : 24) & 0x7F;
            if(n == 1 && output == 'p' && (div & 1) == 0)
            {
                why += ": DIVP1 = " + div + " (/" + (div + 1) + "): \"odd division factors are not allowed\" (p404), \"DIVPx = 2,4,6,...128 when x = 1\" (Figure 47 p346): no rule for its frequency";
                return null;
            }
            return vco / (div + 1);
        }

        // TVI-WO-1308 (ruled 2026-11-26T09:31Z Q9): the bus clocks now, in Hz, for the PWR guard's VOS rule: CPU (rcc_c_ck = sys_ck /
        // D1CPRE, p394), AHB (rcc_hclk = rcc_c_ck / HPRE, p395), APB1-APB4 (rcc_pclk1/2 by D2PPRE1/2, p396; rcc_pclk3 by D1PPRE,
        // p395; rcc_pclk4 by D3PPRE, p397); `apbEnabled`, per APB bus, whether one of its peripherals is enabled (RCC_APB1LENR or
        // APB1HENR, APB2ENR, APB3ENR, APB4ENR, p462-471). Null, with the reason, where sys_ck has no frequency rule. It changes nothing.
        public double[] BusClocks(out bool[] apbEnabled, out string why)
        {
            return Clocks(-1, 0, out apbEnabled, out why, out var _);
        }

        // The bus clocks as BusClocks gives them, after a write of `value` at `b` (none for b = -1): the write's copy, RCC_PLLxDIVR
        // and RCC_CR (as the stock model keeps it) taken for the trial and restored, as TVI-RCC-CLOCK's trial does; CFGR's SW as the
        // new SWS (TVI-RCC-SW has passed: its target runs); D1CFGR, D2CFGR and D3CFGR as written.
        private double[] Clocks(long b, uint value, out bool[] apbEnabled, out string why, out double? sysHz)
        {
            var savedCopy = b >= 0 && copies.TryGetValue(b, out var c) ? c : (uint?)null;
            var savedDivr = b >= 0 && divr.TryGetValue(b, out var d) ? d : (uint?)null;
            try
            {
                if(savedCopy != null)
                {
                    copies[b] = value;
                }
                if(savedDivr != null)
                {
                    divr[b] = value;
                }
                crAfter = b == CrOffset ? KeptCr(value) : (uint?)null;
                apbEnabled = new[] { Copy(0x0E8) != 0 || Copy(0x0EC) != 0, Copy(0x0F0) != 0, Copy(0x0E4) != 0, Copy(0x0F4) != 0 };
                why = "sys_ck";
                var sys = SysHz(b == CfgrOffset ? value & 7 : inner.ReadDoubleWord(CfgrOffset) >> 3 & 7, ref why);
                sysHz = sys;
                if(sys == null)
                {
                    return null;
                }
                var d1 = b == 0x018 ? value : inner.ReadDoubleWord(0x018);
                var d2 = b == 0x01C ? value : inner.ReadDoubleWord(0x01C);
                var d3 = b == 0x020 ? value : inner.ReadDoubleWord(0x020);
                var cpu = sys.Value / Prescaler(d1 >> 8 & 0xF);
                var ahb = cpu / Prescaler(d1 & 0xF);
                Func<uint, double> apb = x => ahb / ((x & 4) == 0 ? 1 : 2 << (int)(x & 3));   // 0xx /1; 100-111 /2-/16
                return new[] { cpu, ahb, apb(d2 >> 4 & 7), apb(d2 >> 8 & 7), apb(d1 >> 4 & 7), apb(d3 >> 4 & 7) };
            }
            finally
            {
                if(savedCopy is uint sc)
                {
                    copies[b] = sc;
                }
                if(savedDivr is uint sd)
                {
                    divr[b] = sd;
                }
                crAfter = default(uint?);
            }
        }

        // D1CPRE and HPRE (pp. 394-395): 0xxx /1; 1000-1011 /2-/16; 1100-1111 /64-/512.
        private static double Prescaler(uint v)
        {
            return (v & 8) == 0 ? 1 : (v & 7) < 4 ? 2 << (int)(v & 7) : 64 << (int)((v & 7) - 4);
        }

        // Monitor- and test-facing: refuse (predicted-fault) unless P's kernel clock runs. The start that needs it is
        // described by the caller. This is what a peripheral's guard will call at its own start (TVI-WO-1300a). True when
        // the clock runs; false when a refusal (or, in recorded mode, a would-refuse) was emitted.
        public bool RequireKernelClock(string peripheral, string start)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, "none", peripheral + " kernel clock", "the guard did not load: no query is answered",
                    Records.LoadCitation, "none", 0, 0, 0);
                return false;
            }
            var k = QueryKernelClock(peripheral);
            if(!k.Modeled)
            {
                records.Refuse("TVI-RCC-KERNEL", Unmodeled, "none", peripheral + " kernel clock", k.Condition, k.Citation,
                    State(), 0, 0, 0);
                return false;
            }
            if(!k.Running)
            {
                records.Refuse("TVI-RCC-KERNEL", PredictedFault, k.Selector.Split('.')[0], k.Selector,
                    peripheral + " started (" + start + ") with no kernel clock: " + k.Condition, k.Citation, State(), 0, 0, 0);
                return false;
            }
            return true;
        }

        public sealed class KernelClock
        {
            public KernelClock(string peripheral, bool modeled, bool running, string condition, string selector, string citation)
            {
                Peripheral = peripheral;
                Modeled = modeled;
                Running = running;
                Condition = condition;
                Selector = selector;
                Citation = citation;
            }

            public override string ToString()
            {
                return Peripheral + ": " + (Modeled ? (Running ? "running" : "NOT running") : "not modeled") + "; " + Condition
                    + "; " + Selector + "; " + Citation;
            }

            public string Peripheral { get; }
            public bool Modeled { get; }
            public bool Running { get; }
            public string Condition { get; }
            public string Selector { get; }
            public string Citation { get; }
        }

        // ---- bus access ----------------------------------------------------------------------------------------

        public uint ReadDoubleWord(long offset)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, State(), offset, 0, 32);
                return 0;
            }
            if((offset & 3) != 0)
            {
                Width(offset, 0, 32, "read");
                return records.Recorded ? Served(Base(offset & ~3L)) : 0;
            }
            var b = Base(offset);
            if(!Fields.Any(f => f.Offset == b))
            {
                records.Refuse("TVI-RCC-READ", Unmodeled, RegisterAt(offset), "no register at offset 0x" + offset.ToString("X"),
                    "read of an RCC offset where RM0433 defines no register: the twin cannot say what silicon returns",
                    "RM0433 Rev 9 §8.8 Table 84 p496-505", State(), offset, 0, 32);
                if(!records.Recorded)
                {
                    return 0;
                }
            }
            return Served(b);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(Refused(offset, value) && !records.Recorded)
            {
                return;
            }
            Pass(Base(offset), value);
        }

        public ushort ReadWord(long offset)
        {
            return (ushort)SubRead(offset, 16);
        }

        public void WriteWord(long offset, ushort value)
        {
            SubWrite(offset, value, 16);
        }

        public byte ReadByte(long offset)
        {
            return (byte)SubRead(offset, 8);
        }

        public void WriteByte(long offset, byte value)
        {
            SubWrite(offset, value, 8);
        }

        // RCC_BDCR, RCC_CSR and RCC_RSR take half-word and byte accesses (p427, p429, p450); no other register states it.
        private static bool SubWordAllowed(long b)
        {
            return b == BdcrOffset || b == CsrOffset || b == RsrOffset;
        }

        private uint SubRead(long offset, int width)
        {
            var b = Base(offset & ~3L);
            if(records.LoadFailed || (offset & (width / 8 - 1)) != 0 || !SubWordAllowed(b))
            {
                Width(offset, 0, width, "read");
                if(!records.Recorded)
                {
                    return 0;
                }
            }
            return Served(b) >> (int)(8 * (offset & 3));
        }

        private void SubWrite(long offset, uint value, int width)
        {
            var b = Base(offset & ~3L);
            var mask = width == 16 ? 0xFFFFu : 0xFFu;
            var shift = (int)(8 * (offset & 3));
            var word = (Served(b) & ~(mask << shift)) | ((value & mask) << shift);
            if(records.LoadFailed || (offset & (width / 8 - 1)) != 0 || !SubWordAllowed(b))
            {
                Width(offset, value, width, "write");
                if(!records.Recorded)
                {
                    return;
                }
            }
            else
            {
                var fired = RefusedField(b, b, word);
                fired |= records.Evaluates(fired) && RefusedDbp(offset, b, word, width);   // every rule in recorded mode (model/Refusal.cs)
                if(fired && !records.Recorded)
                {
                    return;
                }
            }
            Pass(b, word);
        }

        private void Width(long offset, ulong value, int width, string what)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset & ~3L), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, State(), offset, value, width);
                return;
            }
            records.Refuse("TVI-RCC-WIDTH", Unmodeled, RegisterAt(offset & ~3L), ((offset & (width / 8 - 1)) != 0 ? "misaligned " : "") + width + "-bit " + what,
                "a " + width + "-bit RCC " + what + " at offset 0x" + offset.ToString("X") + ": §8.7 states half-word and byte access only for RCC_BDCR, RCC_CSR and RCC_RSR, and an aligned access for every register, so the twin cannot say what silicon does with it",
                "RM0433 Rev 9 §8.7 p382-495; p427 (RCC_BDCR), p429 (RCC_CSR), p450 (RCC_RSR): \"word, half-word and byte access\"", State(), offset, value, width);
        }

        // What a read returns: the copy's bits for a copied register, the stock model's for the rest.
        private uint Served(long b)
        {
            if(b == BdcrOffset)
            {
                return (inner.ReadDoubleWord(b) & ~BdcrRtcen) | rtcen;
            }
            if(copies.TryGetValue(b, out var c))
            {
                var mask = CopyMask(b);
                return (c & mask) | (inner.ReadDoubleWord(b) & ~mask);
            }
            return inner.ReadDoubleWord(b);
        }

        // The rules for a 32-bit write, in the header's order: enforcing, the first that fires; recorded, every one (model/Refusal.cs).
        private bool Refused(long offset, uint value)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, State(), offset, value, 32);
                return true;
            }
            if((offset & 3) != 0)
            {
                Width(offset, value, 32, "write");
                return true;
            }
            var b = Base(offset);
            var fired = RefusedHse(b, value);
            fired |= records.Evaluates(fired) && RefusedPll(b, value);
            fired |= records.Evaluates(fired) && RefusedPllReference(b, value);
            fired |= records.Evaluates(fired) && RefusedSwitch(b, value);
            fired |= records.Evaluates(fired) && RefusedField(offset, b, value);
            fired |= records.Evaluates(fired) && RefusedDbp(offset, b, value, 32);
            fired |= records.Evaluates(fired) && RefusedVos(offset, b, value);
            fired |= records.Evaluates(fired) && RefusedClockLoss(offset, b, value);
            return fired;
        }

        private void Pass(long b, uint value)
        {
            if(b == BdcrOffset)
            {
                rtcen = value & BdcrRtcen;   // the copy; the stock model's RTCEN is kept at 1, so `rtc` stays unlocked (p471)
                value |= BdcrRtcen;
            }
            if(b == PllckselrOffset || b == PllcfgrOffset)
            {
                // TVI-WO-1300c adversarial pass: "The following PLL parameters cannot be changed once the PLL is enabled: DIVNx, PLLxRGE,
                // PLLxVCOSEL, ..." (p347), nor DIVMx and PLLSRC (p398-399). A write that changes one under a running PLL passes TVI-RCC-PLL
                // for RGE and VCOSEL (and reaches here after it, recorded, for DIVM and PLLSRC); silicon keeps the old value, so that
                // PLL's frequency is no longer known (as Q2's PLL-on write), until a machine reset.
                var old = Copy(b);
                var src = b == PllckselrOffset && ((old ^ value) & 0x3) != 0 && AnyPllOn();   // PLLSRC, "only when all PLLs are disabled" (p399)
                for(var n = 1; n <= 3; n++)
                {
                    var mask = b == PllckselrOffset ? 0x3Fu << DivmShift[n] : 0xEu << 4 * (n - 1);   // DIVMn; PLLnRGE, PLLnVCOSEL
                    cfgUnknown[n] |= src || (PllOn(n) && ((old ^ value) & mask) != 0);
                }
            }
            if(copies.ContainsKey(b))
            {
                copies[b] = value;
            }
            var pll = divr.ContainsKey(b) ? (int)(b - Pll1divrOffset) / 8 + 1 : 0;
            if(pll != 0)
            {
                // Ruled 2026-11-26T04:03Z Q2: the write is silicon's value, unless it follows a write the stock model did not keep (a
                // read-modify-write may carry the stock's narrowed bits back) or changes the register while the PLL runs (silicon
                // ignores it, p403-410; this write reaches here only in recorded mode, after TVI-RCC-PLL). Until a machine reset.
                divrUnknown[pll] |= divrUnkept[pll] || (value != divr[b] && PllOn(pll));
                divr[b] = value;
            }
            inner.WriteDoubleWord(b, value);
            if(pll != 0)
            {
                divrUnkept[pll] |= inner.ReadDoubleWord(b) != value;
            }
            if(EnableOffsets.Contains(b))
            {
                UnlockGuarded();
            }
            Settle();
            Written?.Invoke(b, value);
        }

        // ---- enables at reset (TVI-WO-1300a (1)) ---------------------------------------------------------------

        // The copies of the nine *ENR into the owned stock RCC, whose write callbacks lock every registered peripheral whose
        // enable is 0 on the bus, as silicon ignores it (p373; measured, TVI-WO-1300a.measure.md §1.1): after a registration and
        // at reset. The stock model's own *ENR start at 1 and the bus's lock survives a reset, so neither may stand.
        private void SyncEnables()
        {
            foreach(var off in EnableOffsets)
            {
                inner.WriteDoubleWord(off, Copy(off));
            }
            UnlockGuarded();
        }

        // A guarded peripheral stays unlocked on the bus: a lock would hide the access from its own guard, which refuses it by
        // name instead (TVI-SPI-ENABLE; ruled 17:16Z).
        private void UnlockGuarded()
        {
            foreach(var g in guarded.Values)
            {
                host.SystemBus.SetPeripheralEnabled(g, true);
            }
        }

        // ---- rules ---------------------------------------------------------------------------------------------

        // TVI-RCC-HSE (TVI-WO-1300b (4); ruled 2026-11-25T03:03Z Q3): the HSE started on a platform that declares no frequency.
        private bool RefusedHse(long b, uint value)
        {
            if(b != CrOffset || HseFrequency != null || (value & CrHseon) == 0 || (inner.ReadDoubleWord(CrOffset) & CrHseon) != 0)
            {
                return false;   // the start only: in recorded mode a later CR write that keeps HSEON is judged by the other rules
            }
            records.Refuse("TVI-RCC-HSE", Unmodeled, "RCC_CR", "RCC_CR.HSEON (bit 16) = 1",
                "the HSE started on a platform that declares no hseFrequency: the twin cannot say what the HSE runs at, so no clock derived from it is modeled (no default is taken)",
                "RM0433 Rev 9 §8.7.2 p384 (HSEON); §8.5.2 p339 (the HSE: an external crystal or a user external clock); TVI-WO-1300b (4), ruled 2026-11-25T03:03Z Q3",
                State(), b, value, 32);
            return true;
        }

        private bool RefusedPll(long b, uint value)
        {
            if(b == PllckselrOffset)
            {
                var old = Copy(b);
                if((old & 0x3) != (value & 0x3) && AnyPllOn())
                {
                    return RefusePll(b, value, "PLLSRC", "RCC_PLLCKSELR.PLLSRC (1:0) changed while a PLL is on: \"These bits can be written only when all PLLs are disabled\"",
                        "RM0433 Rev 9 §8.7.11 p399 (PLLSRC); §8.5.5 p347");
                }
                for(var n = 1; n <= 3; n++)
                {
                    var shift = DivmShift[n];
                    if(((old >> shift) & 0x3F) != ((value >> shift) & 0x3F) && PllOn(n))
                    {
                        return RefusePll(b, value, "DIVM" + n, "RCC_PLLCKSELR.DIVM" + n + " changed while PLL" + n + " is on: \"The hardware does not allow any modification of this prescaler when PLLx is enabled\"",
                            "RM0433 Rev 9 §8.7.11 p398-399 (DIVM" + n + "); §8.5.5 p347");
                    }
                }
            }
            if(divr.TryGetValue(b, out var oldDivr) && value != oldDivr && value != inner.ReadDoubleWord(b))
            {
                var n = (int)(b - Pll1divrOffset) / 8 + 1;
                if(PllOn(n))
                {
                    return RefusePll(b, value, "DIVN" + n + "/DIVP" + n + "/DIVQ" + n + "/DIVR" + n, "RCC_PLL" + n + "DIVR changed while PLL" + n + " is on: \"These bits can be written only when the PLL" + n + " is disabled\"",
                        "RM0433 Rev 9 " + (n == 1 ? "§8.7.13 p403-404" : n == 2 ? "§8.7.15 p406-407" : "§8.7.17 p409-410") + " (DIVN" + n + ", DIVP" + n + ", DIVQ" + n + ", DIVR" + n + "); §8.5.5 p347");
                }
            }
            if(b == PllcfgrOffset)
            {
                var old = Copy(b);
                for(var n = 1; n <= 3; n++)
                {
                    var mask = 0x7u << (16 + 3 * (n - 1));
                    if((old & mask) != (value & mask) && PllOn(n))
                    {
                        return RefusePll(b, value, "DIVP" + n + "EN/DIVQ" + n + "EN/DIVR" + n + "EN", "RCC_PLLCFGR's PLL" + n + " output enables changed while PLL" + n + " is on: \"This bit can be written only when the PLL" + n + " is disabled\"",
                            "RM0433 Rev 9 §8.7.12 p400-401 (DIVxyEN); §8.5.5 p347");
                    }
                }
            }
            return false;
        }

        private bool RefusedPllReference(long b, uint value)
        {
            if(b != CrOffset)
            {
                return false;
            }
            var cr = KeptCr(value);
            for(var n = 1; n <= 3; n++)
            {
                var on = 1u << (24 + 2 * (n - 1));
                if((cr & on) != 0 && !PllReference(n, cr))
                {
                    records.Refuse("TVI-RCC-PLL-REF", Unmodeled, "RCC_CR", "PLL" + n + "ON (bit " + (24 + 2 * (n - 1)) + ") = 1",
                        "PLL" + n + " enabled with no reference clock after this write (RCC_CR would be 0x" + cr.ToString("X8") + "; PLLSRC = "
                            + Bin(Copy(PllckselrOffset) & 0x3, 2) + ", DIVM" + n + " = " + ((Copy(PllckselrOffset) >> DivmShift[n]) & 0x3F)
                            + "): the stock model reports PLL" + n + "RDY = PLL" + n + "ON, while silicon has nothing to lock to; the twin cannot say what silicon does",
                        "RM0433 Rev 9 §8.7.2 p383-386 (PLLxON, PLLxRDY; the sources' clear rules, which §8.5.2 p340-342 words differently for PLL1); §8.7.11 p398-399 (PLLSRC, DIVMx); §8.5.5 p347-349",
                        State(), b, value, 32);
                    return true;
                }
            }
            return false;
        }

        private bool RefusedSwitch(long b, uint value)
        {
            if(b != CfgrOffset)
            {
                return false;
            }
            var sw = value & 0x7;
            if(sw == (inner.ReadDoubleWord(CfgrOffset) & 0x7) || sw > 3)
            {
                return false;   // no switch; or a reserved value, which the allow-list's SW row decides
            }
            var cr = inner.ReadDoubleWord(CrOffset);
            var ready = sw == 0 ? (cr & CrHsion) != 0 : sw == 1 ? (cr & CrCsion) != 0 : sw == 2 ? (cr & CrHseon) != 0
                : PllOn(1) && PllReference(1, cr) && (Copy(PllcfgrOffset) >> 16 & 1) != 0;
            if(ready)
            {
                return false;
            }
            var target = new[] { "hsi_ck (HSION = 0)", "csi_ck (CSION = 0)", "hse_ck (HSEON = 0)", "pll1_p_ck (" + Condition("pll1_p_ck") + ")" }[sw];
            records.Refuse("TVI-RCC-SW", Unmodeled, "RCC_CFGR", "SW (2:0) = " + Bin(sw, 3),
                "the system clock switched to " + target + ", which is not running: silicon switches \"only if the target clock source is ready\" and keeps the old sys_ck until then, while the stock model switches at once",
                "RM0433 Rev 9 §8.5.6 p350 (system clock switch); §8.7.7 p393 (SW, SWS)", State(), b, value, 32);
            return true;
        }

        // The RCC_CR value the stock model keeps after a write: it reverts a clear of HSION, CSION or HSEON while that
        // oscillator is the system clock or PLL1's source with PLL1 the system clock, and of PLL1ON while PLL1 is the
        // system clock (STM32H7_RCC.cs, the RCC_CR write callbacks).
        private uint KeptCr(uint value)
        {
            var old = inner.ReadDoubleWord(CrOffset);
            var sw = inner.ReadDoubleWord(CfgrOffset) & 0x7;
            var src = Copy(PllckselrOffset) & 0x3;
            var kept = value;
            foreach(var (bit, sel) in new[] { (CrHsion, 0u), (CrCsion, 1u), (CrHseon, 2u) })
            {
                if((old & bit) != 0 && (value & bit) == 0 && (sw == sel || (sw == 3 && src == sel)))
                {
                    kept |= bit;
                }
            }
            if((old & (1u << 24)) != 0 && sw == 3)
            {
                kept |= 1u << 24;
            }
            return kept;
        }

        private bool RefusePll(long b, uint value, string field, string condition, string citation)
        {
            records.Refuse("TVI-RCC-PLL", PredictedFault, RegisterAt(b), field, condition + "; silicon keeps the old value, while the guard's copy would take the write",
                citation, State(), b, value, 32);
            return true;
        }

        private bool RefusedField(long offset, long b, uint value)
        {
            var rows = Fields.Where(f => f.Offset == b).ToArray();
            if(rows.Length == 0)
            {
                records.Refuse("TVI-RCC-FIELD", Unmodeled, RegisterAt(offset), "no register at offset 0x" + offset.ToString("X"),
                    "write to an RCC offset where RM0433 defines no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §8.8 Table 84 p496-505", State(), offset, value, 32);
                return true;
            }
            foreach(var f in rows)
            {
                var v = f.Of(value);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    records.Refuse("TVI-RCC-FIELD", Unmodeled, f.Register, f.Register + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        f.Register + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-1300.rm0433.md)",
                        State(), offset, value, 32);
                    return true;
                }
            }
            return false;
        }

        // TVI-RCC-DBP (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10): an RCC_BDCR write that changes it while the PWR guard's DBP = 0.
        private bool RefusedDbp(long offset, long b, uint value, int width)
        {
            var p = b == BdcrOffset ? Pwr() : null;
            if(p == null || p.Dbp || value == Served(b))
            {
                return false;   // no PWR guard (the stock stub reads DBP = 1), DBP = 1, or a write that changes nothing (RUL-18 as amended)
            }
            records.Refuse("TVI-RCC-DBP", Unmodeled, RegisterAt(offset), "RCC_BDCR with PWR_CR1.DBP = 0",
                "RCC_BDCR written 0x" + value.ToString("X8") + " (it holds 0x" + Served(b).ToString("X8") + ") while PWR_CR1.DBP = 0: \"After a system reset, the RCC_BDCR register is write-protected\", and RM0433 never says what a protected write does, so the twin cannot say what silicon holds",
                "RM0433 Rev 9 §8.7.26 p427; §6.8.1 p305 (DBP); TVI-WO-1308, ruled 2026-11-26T09:31Z Q10", State(), offset, value, width);
            return true;
        }

        // TVI-RCC-VOS (TVI-WO-1308, ruled 2026-11-26T09:31Z Q9, 11:06Z Q20 (A)): a write after which the CPU, AHB or an APB clock is
        // above DS12117 Rev 10 p210's limit for the scale the PWR guard holds. Judged only on a platform with a PWR guard; with none,
        // not judged at all. At a write that changes the verdict (a new excess, or the same with other clocks); a clock with no
        // frequency rule (sys_ck's source gives none) is not judged here.
        private bool RefusedVos(long offset, long b, uint value)
        {
            if(!VosOffsets.Contains(b))
            {
                return false;
            }
            var p = Pwr();
            if(p == null)
            {
                return false;   // ruled 2026-11-26T11:06Z Q20 (A): not judged without a PWR guard
            }
            var revision = p.Revision;
            var scale = p.Scale;
            var held = p.ScaleCitation;
            var c0 = Clocks(-1, 0, out var apb0, out var _, out var sys0);
            var c1 = Clocks(b, value, out var apb1, out var _, out var sys1);
            if(!p.ActvosReady && sys1 != sys0)
            {
                // TVI-WO-1308 adversarial pass: sys_ck changed in Run* mode, before a valid supply configuration (p308, p262).
                records.Refuse("TVI-RCC-VOS", Unmodeled, RegisterAt(offset), "sys_ck before the supply configuration",
                    "sys_ck changed from " + (sys0?.ToString() ?? "no known frequency") + " to " + (sys1?.ToString() ?? "no known frequency") + " Hz while PWR_CSR1.ACTVOSRDY = 0 (no valid supply configuration written): "
                        + "PWR_CR3's lower byte \"shall be written before changing VOS level or ck_sys clock frequency\" (p308), and until ACTVOSRDY the system is in Run* mode (p262); no page says what silicon does with the change",
                    "RM0433 Rev 9 §6.8.4 p308; §6.4.1 p262; TVI-WO-1308 adversarial pass", State(), offset, value, 32);
                return true;
            }
            var before = GuardedSTM32H7_PWR.Exceeds(revision, c0, apb0, scale, p.LdoOn, out var _);
            var after = GuardedSTM32H7_PWR.Exceeds(revision, c1, apb1, scale, p.LdoOn, out var kind);
            if(after == null || after == before)
            {
                return false;
            }
            records.Refuse("TVI-RCC-VOS", kind, RegisterAt(offset), after.Split(new[] { " at " }, StringSplitOptions.None)[0] + " above VOS" + scale,
                "after this write, " + after + "; the scale: " + held
                    + ": \"When increasing the performance, the voltage scaling shall be changed before increasing the system frequency\" (p310), \"otherwise the system might be unreliable\" (p286)",
                "RM0433 Rev 9 §6.8.6 p310; §6.6.4 p286; " + GuardedSTM32H7_PWR.LimitsCitation + "; TVI-WO-1308, ruled 2026-11-26T09:31Z Q9",
                State(), offset, value, 32);
            return true;
        }

        private GuardedSTM32H7_PWR Pwr()
        {
            return pwr ?? (pwr = host.GetPeripheralsOfType<GuardedSTM32H7_PWR>().FirstOrDefault());
        }

        // TVI-RCC-CLOCK (TVI-WO-1300a (3); ruled 17:16Z): a write after which a guarded peripheral that runs (an SPI with SPE = 1)
        // loses the kernel clock it had. Evaluated on the state the write leaves: the copies take it, RCC_CR keeps what the
        // stock model keeps (the oscillator and PLL1ON clears silicon blocks, p383-386, which therefore lose nothing), and the
        // muxes settle (p371). A transition only: a peripheral that runs without a clock was refused at its start.
        private bool RefusedClockLoss(long offset, long b, uint value)
        {
            // The query reads RCC_CR and the copies only: a write to neither, or one that leaves its copy as it is, loses no clock.
            if(b != CrOffset && (!copies.TryGetValue(b, out var held) || held == value))
            {
                return false;
            }
            var running = guarded.Where(g => g.Value.Started).Select(g => g.Key).Concat(watched.Where(w => w.Value.IsRunning).Select(w => w.Key))
                .Where(p => QueryKernelClock(p).Running).ToArray();
            if(running.Length == 0)
            {
                return false;
            }
            string lost = null;
            KernelClock after = null;
            var savedCopy = copies.TryGetValue(b, out var c) ? c : (uint?)null;
            var savedMux = muxes.Select(m => m.Current).ToArray();
            try
            {
                if(savedCopy != null)
                {
                    copies[b] = value;
                }
                if(b == CrOffset)
                {
                    crAfter = KeptCr(value);
                }
                Settle();
                foreach(var p in running)
                {
                    var k = QueryKernelClock(p);
                    if(!k.Running)
                    {
                        lost = p;
                        after = k;
                        break;
                    }
                }
            }
            finally
            {
                if(savedCopy != null)
                {
                    copies[b] = savedCopy.Value;
                }
                crAfter = null;
                for(var i = 0; i < muxes.Length; i++)
                {
                    muxes[i].Current = savedMux[i];
                }
                Settle();
            }
            if(lost == null)
            {
                return false;
            }
            watched.TryGetValue(lost, out var watch);
            var old = b == CrOffset ? inner.ReadDoubleWord(CrOffset) : Served(b);
            var written = b == CrOffset ? KeptCr(value) : value;
            var changed = Fields.Where(f => f.Offset == b && f.Class != "read-only" && f.Class != "reserved" && f.Of(old) != f.Of(written)).Select(f => f.Register + "." + f.Name + " (" + f.Bits + ") = " + f.Format(f.Of(written)));
            records.Refuse("TVI-RCC-CLOCK", PredictedFault, RegisterAt(offset), string.Join(", ", changed),
                lost + " is enabled (" + (watch != null ? watch.RunningName : "SPE") + " = 1) and this write stops its kernel clock: " + after.Condition,
                (after.Condition.Contains("has not switched") ? "RM0433 Rev 9 §8.5.10 p371 (\"no clock will be provided to the peripheral\"); " : "RM0433 Rev 9 ")
                    + after.Citation.Substring("RM0433 Rev 9 ".Length)
                    + (watch != null ? "; " + watch.KernelCitation : ""),
                State(), offset, value, 32);
            return true;
        }

        // ---- the clock tree ------------------------------------------------------------------------------------

        // After every passed write and every reset: each mux whose selector differs from its input switches when a
        // clock is present on both (p371). per_ck feeds SPI123SEL, so it settles first (the array's order).
        private void Settle()
        {
            foreach(var m in muxes)
            {
                m.Selector = (Copy(m.Def.Offset) >> m.Def.Lsb) & ((1u << m.Def.Width) - 1);
                if(m.Current != m.Selector && Running(m.Def.Sources[m.Current]) && Running(m.Def.Sources[m.Selector]))
                {
                    m.Current = m.Selector;
                }
            }
        }

        private bool Running(string source)
        {
            var cr = Cr();
            switch(source)
            {
            case "rcc_pclk1":
            case "rcc_pclk2":
                return true;
            case "hsi_ker_ck":
                return (cr & CrHsion) != 0;
            case "csi_ker_ck":
                return (cr & CrCsion) != 0;
            case "hse_ck":
                return (cr & CrHseon) != 0;
            case "hsi48_ck":
                return (cr & CrHsi48on) != 0;
            case "per_ck":
                var per = muxes[0];
                return per.Current == per.Selector && Running(PerMux.Sources[per.Current]);
            }
            if(source.StartsWith("pll", StringComparison.Ordinal))
            {
                var n = source[3] - '0';
                var output = source[5];
                var en = (Copy(PllcfgrOffset) >> (16 + 3 * (n - 1) + (output == 'p' ? 0 : output == 'q' ? 1 : 2)) & 1) != 0;
                return PllOn(n) && en && PllReference(n, cr);
            }
            return false;   // "disabled", and every source outside the allow-list
        }

        private string Condition(string source)
        {
            var cr = Cr();
            switch(source)
            {
            case "rcc_pclk1":
            case "rcc_pclk2":
                return "the bus clock, running while the peripheral is enabled";
            case "hsi_ker_ck":
                return "HSION = " + ((cr & CrHsion) != 0 ? 1 : 0);
            case "csi_ker_ck":
                return "CSION = " + ((cr & CrCsion) != 0 ? 1 : 0);
            case "hse_ck":
                return "HSEON = " + ((cr & CrHseon) != 0 ? 1 : 0);
            case "hsi48_ck":
                return "HSI48ON = " + ((cr & CrHsi48on) != 0 ? 1 : 0);
            case "per_ck":
                var per = muxes[0];
                return per.Current == per.Selector ? "CKPERSEL = " + Bin(per.Selector, 2) + ", " + PerMux.Sources[per.Current] + ": " + Condition(PerMux.Sources[per.Current])
                    : "per_ck's mux has not switched from " + PerMux.Sources[per.Current] + " to " + PerMux.Sources[per.Selector];
            case "disabled":
                return "a reserved selector value: \"the kernel clock is disabled\"";
            }
            if(source.StartsWith("pll", StringComparison.Ordinal))
            {
                var n = source[3] - '0';
                var output = char.ToUpperInvariant(source[5]);
                var en = (Copy(PllcfgrOffset) >> (16 + 3 * (n - 1) + (output == 'P' ? 0 : output == 'Q' ? 1 : 2)) & 1) != 0;
                var src = Copy(PllckselrOffset) & 0x3;
                return "PLL" + n + "ON = " + (PllOn(n) ? 1 : 0) + ", DIV" + output + n + "EN = " + (en ? 1 : 0) + ", PLLSRC = " + Bin(src, 2)
                    + " (" + PllSourceName[src] + (src == 3 ? "" : ", " + (PllReference(n, cr) ? "running" : "not running")) + "), DIVM" + n + " = " + ((Copy(PllckselrOffset) >> DivmShift[n]) & 0x3F);
            }
            return "not modeled";
        }

        private static string SourceCitation(string source)
        {
            if(source.StartsWith("pll", StringComparison.Ordinal))
            {
                return "p383 (PLLxON, PLLxRDY), p400-401 (DIVxyEN: \"0: output is disabled\"), p398-399 (PLLSRC, DIVMx: 0 is \"prescaler disabled\")";
            }
            switch(source)
            {
            case "hsi_ker_ck": return "p386 (HSION); p338 Figure 45 and p353 (hsi_ker_ck from the HSI; inferred: present in Run mode while HSION = 1)";
            case "csi_ker_ck": return "p385 (CSION); p338 Figure 45 and p353 (inferred, as hsi_ker_ck)";
            case "hse_ck": return "p384 (HSEON)";
            case "hsi48_ck": return "p384 (HSI48ON)";
            case "per_ck": return "p412 (CKPERSEL)";
            default: return "p376-377 Table 62 (the bus clock, gated by PERxEN)";
            }
        }

        private bool PllOn(int n)
        {
            return (Cr() >> (24 + 2 * (n - 1)) & 1) != 0;
        }

        // RCC_CR as the clock tree reads it: the stock model's, or during TVI-RCC-CLOCK's trial the value a write leaves.
        private uint Cr()
        {
            return crAfter ?? inner.ReadDoubleWord(CrOffset);
        }

        private bool AnyPllOn()
        {
            return PllOn(1) || PllOn(2) || PllOn(3);
        }

        // The PLL's reference runs, by the copies and the given RCC_CR value.
        private bool PllReference(int n, uint cr)
        {
            var sel = Copy(PllckselrOffset);
            if(((sel >> DivmShift[n]) & 0x3F) == 0)
            {
                return false;   // "000000: prescaler disabled" (p398-399)
            }
            switch(sel & 0x3)
            {
            case 0: return (cr & CrHsion) != 0;
            case 1: return (cr & CrCsion) != 0;
            case 2: return (cr & CrHseon) != 0;
            default: return false;   // "11: No clock send to DIVMx divider and PLLs"
            }
        }

        // ---- copies --------------------------------------------------------------------------------------------

        private void ResetCopies()
        {
            if(copies == null)
            {
                copies = new Dictionary<long, uint>();
            }
            foreach(var off in CopiedOffsets)
            {
                copies[off] = ResetValue(off);
            }
            divrUnkept = new bool[4];
            divrUnknown = new bool[4];
            cfgUnknown = new bool[4];
            foreach(var off in divr.Keys.ToArray())
            {
                divr[off] = DivrReset;
            }
            Settle();
        }

        private uint Copy(long offset)
        {
            return copies[offset];
        }

        private bool Enabled(ClockedPeripheral p)
        {
            return (Copy(p.EnableOffset) >> p.EnableBit & 1) != 0;
        }

        private static uint ResetValue(long offset)
        {
            switch(offset)
            {
            case PllckselrOffset: return 0x02020200;   // p398
            case PllcfgrOffset: return 0x01FF0000;     // p400
            case 0x0F4: return 0x00010000;             // RCC_APB4ENR: RTCAPBEN resets to 1 (p471)
            default: return 0;                         // the selectors (p412-418) and the other *ENR (p452-468)
            }
        }

        // Bits served from the copy on read: the whole register, except PLLCFGR's reserved bits, which the stock model keeps:
        // DIVxyEN, and since TVI-WO-1300c (4) PLLxRGE, PLLxVCOSEL and PLLxFRACEN, which the frequency rule reads from the copy and the
        // stock model drops but PLL2VCOSEL (tags reading 0, p400-402).
        private static uint CopyMask(long offset)
        {
            return offset == PllcfgrOffset ? 0x01FF0FFFu : 0xFFFFFFFFu;
        }

        // RCC_C1_* (0x130-0x17C) are the same physical registers as 0x0D0-0x11C (p382, §8.7.1, Figure 64).
        private static long Base(long offset)
        {
            return offset >= 0x130 && offset <= 0x17C ? offset - 0x60 : offset;
        }

        private string RegisterAt(long offset)
        {
            var b = Base(offset);
            var f = Fields.FirstOrDefault(r => r.Offset == b);
            return f == null ? "none" : (b != offset ? f.Register.Replace("RCC_", "RCC_C1_") : f.Register);
        }

        private string State()
        {
            var cr = inner.ReadDoubleWord(CrOffset);
            return "CR=0x" + cr.ToString("X8") + " PLLCKSELR=0x" + Copy(PllckselrOffset).ToString("X8") + " PLLCFGR.DIVxyEN=0x"
                + (Copy(PllcfgrOffset) & 0x01FF0000).ToString("X8") + " " + string.Join(" ", muxes.Select(m => m.Def.Field + "=" + Bin(m.Selector, m.Def.Width)
                + (m.Current != m.Selector ? "(input " + Bin(m.Current, m.Def.Width) + ")" : "")));
        }

        private static string Bin(uint v, int width)
        {
            return Convert.ToString(v, 2).PadLeft(width, '0');
        }

        // ---- the clock tree's data (RM0433 Rev 9) --------------------------------------------------------------

        private sealed class MuxDef
        {
            public MuxDef(string register, long offset, string field, int lsb, int width, int page, params string[] sources)
            {
                Register = register;
                Offset = offset;
                Field = field;
                Lsb = lsb;
                Width = width;
                Page = page;
                Sources = sources;
            }

            public string Register { get; }
            public long Offset { get; }
            public string Field { get; }
            public int Lsb { get; }
            public int Width { get; }
            public int Page { get; }
            public string[] Sources { get; }
        }

        private sealed class MuxState
        {
            public MuxState(MuxDef def)
            {
                Def = def;
            }

            public MuxDef Def { get; }
            public uint Selector { get; set; }
            public uint Current { get; set; }   // the input the mux has switched to; 0 at power-on
        }

        private sealed class ClockedPeripheral
        {
            public ClockedPeripheral(MuxDef mux, long enableOffset, int enableBit, string enableName, string enablePage)
            {
                Mux = mux;
                EnableOffset = enableOffset;
                EnableBit = enableBit;
                EnableName = enableName;
                EnablePage = enablePage;
            }

            public MuxDef Mux { get; }
            public long EnableOffset { get; }
            public int EnableBit { get; }
            public string EnableName { get; }
            public string EnablePage { get; }
        }

        // Source tables, quoted: CKPERSEL p412; SPI123SEL p414; I2C123SEL p416; USART16SEL, USART234578SEL p417; RNGSEL p417, whose
        // "hsi8_ck" for 00 is a misprint for hsi48_ck (Figure 58 p364). I2S_CKIN, lse_ck and lsi_ck are named but not
        // modeled: the allow-list refuses the selector values that name them.
        private static readonly MuxDef PerMux = new MuxDef("RCC_D1CCIPR", 0x04C, "CKPERSEL", 28, 2, 412,
            "hsi_ker_ck", "csi_ker_ck", "hse_ck", "disabled");
        private static readonly MuxDef SpiMux = new MuxDef("RCC_D2CCIP1R", 0x050, "SPI123SEL", 12, 3, 414,
            "pll1_q_ck", "pll2_p_ck", "pll3_p_ck", "I2S_CKIN", "per_ck", "disabled", "disabled", "disabled");
        private static readonly MuxDef I2cMux = new MuxDef("RCC_D2CCIP2R", 0x054, "I2C123SEL", 12, 2, 416,
            "rcc_pclk1", "pll3_r_ck", "hsi_ker_ck", "csi_ker_ck");
        private static readonly MuxDef UsartMux = new MuxDef("RCC_D2CCIP2R", 0x054, "USART16SEL", 3, 3, 417,
            "rcc_pclk2", "pll2_q_ck", "pll3_q_ck", "hsi_ker_ck", "csi_ker_ck", "lse_ck", "disabled", "disabled");
        private static readonly MuxDef Usart2Mux = new MuxDef("RCC_D2CCIP2R", 0x054, "USART234578SEL", 0, 3, 417,
            "rcc_pclk1", "pll2_q_ck", "pll3_q_ck", "hsi_ker_ck", "csi_ker_ck", "lse_ck", "disabled", "disabled");
        private static readonly MuxDef RngMux = new MuxDef("RCC_D2CCIP2R", 0x054, "RNGSEL", 8, 2, 417,
            "hsi48_ck", "pll1_q_ck", "lse_ck", "lsi_ck");
        // TVI-WO-1307 (ruled 2026-11-25T21:17Z Q3): ADCSEL, the kernel clock of ADC1, ADC2 and ADC3 (p419: "others: reserved, the
        // kernel clock is disabled"). Its peripherals use it only with ADCx_CCR.CKMODE = 00 (p920); the ADC guard asks then.
        private static readonly MuxDef AdcMux = new MuxDef("RCC_D3CCIPR", 0x058, "ADCSEL", 16, 2, 419,
            "pll2_p_ck", "pll3_r_ck", "per_ck", "disabled");

        // Enables: SPI1EN APB2ENR bit 12 (p469); SPI2EN APB1LENR bit 14 (p464); I2C1EN, I2C2EN APB1LENR bits 21, 22
        // (p463); USART1EN APB2ENR bit 4 (p470); RNGEN AHB2ENR bit 6 (p457); ADC12EN AHB1ENR bit 5 (p455); ADC3EN AHB4ENR bit 24
        // (p458), named by the ADCs' registration aliases (ADC12, ADC3; TVI-WO-1307 Q3).
        private static readonly Dictionary<string, ClockedPeripheral> Clocked = new Dictionary<string, ClockedPeripheral>
        {
            ["spi1"] = new ClockedPeripheral(SpiMux, 0x0F0, 12, "RCC_APB2ENR.SPI1EN", "§8.7.47 p469"),
            ["spi2"] = new ClockedPeripheral(SpiMux, 0x0E8, 14, "RCC_APB1LENR.SPI2EN", "§8.7.45 p464"),
            ["i2c1"] = new ClockedPeripheral(I2cMux, 0x0E8, 21, "RCC_APB1LENR.I2C1EN", "§8.7.45 p463"),
            ["i2c2"] = new ClockedPeripheral(I2cMux, 0x0E8, 22, "RCC_APB1LENR.I2C2EN", "§8.7.45 p463"),
            ["usart1"] = new ClockedPeripheral(UsartMux, 0x0F0, 4, "RCC_APB2ENR.USART1EN", "§8.7.47 p470"),
            ["usart2"] = new ClockedPeripheral(Usart2Mux, 0x0E8, 17, "RCC_APB1LENR.USART2EN", "§8.7.45 p463"),
            ["usart3"] = new ClockedPeripheral(Usart2Mux, 0x0E8, 18, "RCC_APB1LENR.USART3EN", "§8.7.45 p463"),
            ["rng"] = new ClockedPeripheral(RngMux, 0x0DC, 6, "RCC_AHB2ENR.RNGEN", "§8.7.42 p457"),
            ["adc12"] = new ClockedPeripheral(AdcMux, 0x0D8, 5, "RCC_AHB1ENR.ADC12EN", "§8.7.41 p455"),
            ["adc3"] = new ClockedPeripheral(AdcMux, 0x0E0, 24, "RCC_AHB4ENR.ADC3EN", "§8.7.43 p458"),
        };

        private static readonly int[] DivmShift = { 0, 4, 12, 20 };
        private static readonly string[] PllSourceName = { "hsi_ck", "csi_ck", "hse_ck", "no clock" };
        private static readonly long[] EnableOffsets = { 0x0D4, 0x0D8, 0x0DC, 0x0E0, 0x0E4, 0x0E8, 0x0EC, 0x0F0, 0x0F4 };
        private static readonly long[] CopiedOffsets = new[] { PllckselrOffset, PllcfgrOffset, 0x04C, 0x050, 0x054, 0x058 }.Concat(EnableOffsets).ToArray();

        private const long CrOffset = 0x000;
        private const long CfgrOffset = 0x010;
        private const long BdcrOffset = 0x070;
        private const long CsrOffset = 0x074;
        private const long RsrOffset = 0x0D0;
        private const long PllckselrOffset = 0x028;
        private const long PllcfgrOffset = 0x02C;
        private const long Pll1divrOffset = 0x030;
        private const uint DivrReset = 0x01010280;
        private const uint CrHsion = 1u << 0;
        private const uint CrCsion = 1u << 7;
        private const uint CrHsi48on = 1u << 12;
        private const uint CrHseon = 1u << 16;
        private const uint BdcrRtcen = 1u << 15;
        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";

        private readonly STM32H7_RCC inner;
        private readonly IMachine host;
        // The guarded peripherals registered here, by their alias's name (spi1): kept unlocked; asked whether they run.
        private readonly Dictionary<string, GuardedSTM32H7_SPI> guarded = new Dictionary<string, GuardedSTM32H7_SPI>();
        // The USART guards registered here, by their alias's name (usart1): asked whether UE = 1, never unlocked (they rely on
        // the bus lock for their enable; TVI-WO-1300b (2)).
        private readonly Dictionary<string, IRccWatched> watched = new Dictionary<string, IRccWatched>();
        // RCC_BDCR.RTCEN as silicon holds it: 0 at construction (a backup domain reset), never reset by Reset() (p427).
        private uint rtcen;
        // RCC_PLLxDIVR as last written, at the reset value p403-410 give: TVI-RCC-PLL refuses a write that differs from it and
        // from what the stock model reads back, which drops the top bit of each PLL2/3 field (a read-modify-write is no change).
        private readonly Dictionary<long, uint> divr = new Dictionary<long, uint> { [Pll1divrOffset] = DivrReset, [Pll1divrOffset + 8] = DivrReset, [Pll1divrOffset + 16] = DivrReset };
        // Per PLL (1-3): a write to RCC_PLLnDIVR the stock model did not keep; and divr no longer silicon's value (ruled Q2).
        private bool[] divrUnkept = new bool[4];
        private bool[] divrUnknown = new bool[4];
        // Per PLL (1-3): DIVMn, PLLSRC, PLLnRGE or PLLnVCOSEL written while it ran (p347): its frequency no longer known.
        private bool[] cfgUnknown = new bool[4];
        private uint? crAfter;
        private GuardedSTM32H7_PWR pwr;
        // TVI-RCC-VOS: the writes that can change a bus clock or an APB bus's enables (RCC_CR, CFGR, D1-D3CFGR, PLLCKSELR, PLLCFGR,
        // PLL1DIVR, APB3ENR, APB1LENR, APB1HENR, APB2ENR, APB4ENR).
        private static readonly long[] VosOffsets = { CrOffset, CfgrOffset, 0x018, 0x01C, 0x020, PllckselrOffset, PllcfgrOffset, Pll1divrOffset, 0x0E4, 0x0E8, 0x0EC, 0x0F0, 0x0F4 };
        private readonly Records records;
        private readonly MuxState[] muxes;
        private Dictionary<long, uint> copies;

        // ---- the allow-list (RUL-17): every RM0433 field of every RCC register, keyed by its names (RUL-16) ---------
        // Generated by tests/1300/allowlist.py from RM0433 Rev 9's field list and the stock model's kinds, with the guard's
        // own fields overriding, and the architect's acknowledgements (TVI-WO-1300 A1)-(A6) of TVI-WO-1300.rm0433.md §6.1 and §8 (14:35Z).
        // One row of the allow-list: an RM0433 field, the values the model implements (null: any value),
        // its class (guard, inherited and unaudited, acknowledged by name, tag, reserved), and what the
        // model behaves as. Rows are the measured table of TVI-WO-0100b.rm0433.md §2 as ruled (§4).
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
                Class = cls;
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
            public string Class { get; }
            public string BehavesAs { get; }
            public int Page { get; }
        }

        private static readonly uint[] Any = null;
        private static readonly uint[] Zero = { 0 };

        // The access log's register names (TVI-WO-1203 (5)): Renode's RegisterMapper names an access by any nested enum of the
        // registered peripheral's type whose name contains "register", and this wrapper is the registered type, so without it
        // every access logged `unknown`. This is the stock STM32H7_RCC's own `Registers` enum, copied by reflection from the pinned build,
        // and CSICalibration, a register the guard covers that the stock enum does not name.
        private enum Registers
        {
            ClockControl = 0x0,
            InternalClockSourceCalibration = 0x4,
            ClockRecoveryRC = 0x8,
            // RCC_CSICFGR (p390): not in the stock enum, which leaves the register undefined; named here.
            CSICalibration = 0xC,
            ClockConfiguration = 0x10,
            Domain1ClockConfiguration = 0x18,
            Domain2ClockConfiguration = 0x1C,
            Domain3ClockConfiguration = 0x20,
            PLLClockSourceSelect = 0x28,
            PLLConfiguration = 0x2C,
            PLL1DividersConfiguration = 0x30,
            PLL1FractionalDivider = 0x34,
            PLL2DividersConfiguration = 0x38,
            PLL2FractionalDivider = 0x3C,
            PLL3DividersConfiguration = 0x40,
            PLL3FractionalDivider = 0x44,
            Domain1KernelClockConfiguration = 0x4C,
            Domain2KernelClockConfigurationLow = 0x50,
            Domain2KernelClockConfigurationHigh = 0x54,
            Domain3KernelClockConfiguration = 0x58,
            ClockSourceInterruptEnable = 0x60,
            ClockSourceInterruptFlag = 0x64,
            ClockSourceInterruptClear = 0x68,
            BackupDomainControl = 0x70,
            ClockControlAndStatus = 0x74,
            AHB3Reset = 0x7C,
            AHB1Reset = 0x80,
            AHB2Reset = 0x84,
            AHB4Reset = 0x88,
            APB3Reset = 0x8C,
            APB1ResetLow = 0x90,
            APB1ResetHigh = 0x94,
            APB2Reset = 0x98,
            APB4Reset = 0x9C,
            GlobalControl = 0xA0,
            D3AutonomousMode = 0xA8,
            ResetStatus = 0xD0,
            AHB3Clock = 0xD4,
            AHB1Clock = 0xD8,
            AHB2Clock = 0xDC,
            AHB4Clock = 0xE0,
            APB3Clock = 0xE4,
            APB1ClockLow = 0xE8,
            APB1ClockHigh = 0xEC,
            APB2Clock = 0xF0,
            APB4Clock = 0xF4,
            AHB3SleepClock = 0xFC,
            AHB1SleepClock = 0x100,
            AHB2SleepClock = 0x104,
            AHB4SleepClock = 0x108,
            APB3SleepClock = 0x10C,
            APB1LowSleepClock = 0x110,
            APB1HighSleepClock = 0x114,
            APB2SleepClock = 0x118,
            APB4SleepClock = 0x11C,
            C1ResetStatus = 0x130,
            C1AHB3Clock = 0x134,
            C1AHB1Clock = 0x138,
            C1AHB2Clock = 0x13C,
            C1AHB4Clock = 0x140,
            C1APB3Clock = 0x144,
            C1APB1ClockLow = 0x148,
            C1APB1ClockHigh = 0x14C,
            C1APB2Clock = 0x150,
            C1APB4Clock = 0x154,
            C1AHB3SleepClock = 0x15C,
            C1AHB1SleepClock = 0x160,
            C1AHB2SleepClock = 0x164,
            C1AHB4SleepClock = 0x168,
            C1APB3SleepClock = 0x16C,
            C1APB1LowSleepClock = 0x170,
            C1APB1HighSleepClock = 0x174,
            C1APB2SleepClock = 0x178,
            C1APB4SleepClock = 0x17C,
        }

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, cls, behaves, page);
        }

        private static readonly Row[] Fields =
        {
            R("RCC_CR", 0x000, "§8.7.2", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL3RDY", 29, 29, Any, "read-only", "ignored (read-only; stock computed-read)", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL3ON", 28, 28, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored): stored; only gates PLLSRC/HSIDIV changes", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL2RDY", 27, 27, Any, "read-only", "ignored (read-only; stock computed-read)", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL2ON", 26, 26, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored): stored; only gates PLLSRC/HSIDIV changes (guard inputs); no frequency use", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL1RDY", 25, 25, Any, "read-only", "ignored (read-only; stock computed-read)", 383),
            R("RCC_CR", 0x000, "§8.7.2", "PLL1ON", 24, 24, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored; write 0 reverted to 1 (Warning) while SW==PLL", 383),
            R("RCC_CR", 0x000, "§8.7.2", "Res23_20", 23, 20, Zero, "reserved", "reserved: dropped, reads its reset value", 383),
            R("RCC_CR", 0x000, "§8.7.2", "HSECSSON", 19, 19, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored): FieldMode Read|Set: write 1 sets, write 0 cannot clear; no CSS behaviour", 384),
            R("RCC_CR", 0x000, "§8.7.2", "HSEBYP", 18, 18, Any, "acknowledged by name", "the twin has no oscillator: bypass and crystal behave alike (TVI-WO-1300 A3)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "HSERDY", 17, 17, Any, "read-only", "ignored (read-only; stock computed-read)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "HSEON", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored; write 0 reverted if SW==HSE or PLL1 selected with PLLSRC==HSE; in recorded mode with no crystal declared, the stock RCC runs on a placeholder 8 MHz so a run can continue past TVI-RCC-HSE; no guard reads it (TVI-WO-1204 (3) P2, ruled 2026-11-26T12:51Z)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "D2CKRDY", 15, 15, Any, "read-only", "ignored (read-only; stock tag)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "D1CKRDY", 14, 14, Any, "read-only", "ignored (read-only; stock tag)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "HSI48RDY", 13, 13, Any, "read-only", "ignored (read-only; stock computed-read)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "HSI48ON", 12, 12, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored): stored (local var, no callback)", 384),
            R("RCC_CR", 0x000, "§8.7.2", "Res11_10", 11, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 384),
            R("RCC_CR", 0x000, "§8.7.2", "CSIKERON", 9, 9, Zero, "tag", "its reset value (stock tag)", 385),
            R("RCC_CR", 0x000, "§8.7.2", "CSIRDY", 8, 8, Any, "read-only", "ignored (read-only; stock computed-read)", 385),
            R("RCC_CR", 0x000, "§8.7.2", "CSION", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored; write 0 reverted if SW==CSI or PLL1 selected with PLLSRC==CSI", 385),
            R("RCC_CR", 0x000, "§8.7.2", "Res6", 6, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 385),
            R("RCC_CR", 0x000, "§8.7.2", "HSIDIVF", 5, 5, Any, "read-only", "ignored (read-only; stock read-only-constant)", 385),
            R("RCC_CR", 0x000, "§8.7.2", "HSIDIV", 4, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored; a change reverted while PLLSRC = HSI with a PLL on; hsi_ck = 64 MHz / HSIDIV (p385), for sys_ck, hsi_ker_ck and the PLL reference in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 385),
            R("RCC_CR", 0x000, "§8.7.2", "HSIRDY", 2, 2, Any, "read-only", "ignored (read-only; stock computed-read)", 386),
            R("RCC_CR", 0x000, "§8.7.2", "HSIKERON", 1, 1, Zero, "tag", "its reset value (stock tag)", 386),
            R("RCC_CR", 0x000, "§8.7.2", "HSION", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored; write 0 reverted (previous restored, Warning) if SW==HSI or PLL1 selected with PLLSRC==HSI (writeCallback)", 386),
            R("RCC_HSICFGR", 0x004, "§8.7.4", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 388),
            R("RCC_HSICFGR", 0x004, "§8.7.4", "HSITRIM", 30, 24, new uint[] { 64 }, "tag", "its reset value (stock tag)", 388),
            R("RCC_HSICFGR", 0x004, "§8.7.4", "Res23_12", 23, 12, Zero, "reserved", "reserved: dropped, reads its reset value", 388),
            R("RCC_HSICFGR", 0x004, "§8.7.4", "HSICAL", 11, 0, Any, "read-only", "ignored (read-only; stock tag)", 388),
            R("RCC_CRRCR", 0x008, "§8.7.5", "Res31_10", 31, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 389),
            R("RCC_CRRCR", 0x008, "§8.7.5", "HSI48CAL", 9, 0, Any, "read-only", "ignored (read-only; stock tag)", 389),
            R("RCC_CSICFGR", 0x00C, "§8.7.6", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 390),
            R("RCC_CSICFGR", 0x00C, "§8.7.6", "CSITRIM", 29, 24, new uint[] { 32 }, "undefined in the stock model", "its reset value (stock undefined)", 390),
            R("RCC_CSICFGR", 0x00C, "§8.7.6", "Res23_10", 23, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 390),
            R("RCC_CSICFGR", 0x00C, "§8.7.6", "CSICAL", 9, 0, Any, "read-only", "ignored (read-only; stock undefined)", 390),
            R("RCC_CFGR", 0x010, "§8.7.7", "MCO2", 31, 29, Zero, "tag", "its reset value (stock tag)", 391),
            R("RCC_CFGR", 0x010, "§8.7.7", "MCO2PRE", 28, 25, Any, "acknowledged by name", "no MCO pin and no RTC clock are modeled; with MCOx at reset no pin is driven (TVI-WO-1300 A2)", 391),
            R("RCC_CFGR", 0x010, "§8.7.7", "MCO1", 24, 22, Zero, "tag", "its reset value (stock tag)", 391),
            R("RCC_CFGR", 0x010, "§8.7.7", "MCO1PRE", 21, 18, Any, "acknowledged by name", "no MCO pin and no RTC clock are modeled; with MCOx at reset no pin is driven (TVI-WO-1300 A2)", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "Res17_16", 17, 16, Zero, "reserved", "reserved: dropped, reads its reset value", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "TIMPRE", 15, 15, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored): stored; selects timer kernel clock divisor (LookupHclkDivisorForTimersKernelClock)", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "HRTIMSEL", 14, 14, Zero, "tag", "its reset value (stock tag)", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "RTCPRE", 13, 8, Any, "acknowledged by name", "no MCO pin and no RTC clock are modeled; with MCOx at reset no pin is driven (TVI-WO-1300 A2)", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "STOPKERWUCK", 7, 7, Zero, "tag", "its reset value (stock tag)", 392),
            R("RCC_CFGR", 0x010, "§8.7.7", "STOPWUCK", 6, 6, Zero, "tag", "its reset value (stock tag)", 393),
            R("RCC_CFGR", 0x010, "§8.7.7", "SWS", 5, 3, Any, "read-only", "ignored (read-only; stock computed-read); it selects sys_ck (the HSI, CSI, HSE or pll1_p_ck, p393) for the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 393),
            R("RCC_CFGR", 0x010, "§8.7.7", "SW", 2, 0, new uint[] { 0, 1, 2, 3 }, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback); 100-111 are reserved (p393), and the stock model keeps the old value", 393),
            R("RCC_D1CFGR", 0x018, "§8.7.8", "Res31_12", 31, 12, Zero, "reserved", "reserved: dropped, reads its reset value", 394),
            R("RCC_D1CFGR", 0x018, "§8.7.8", "D1CPRE", 11, 8, Any, "acknowledged by name", "the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x0: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p394; STM32H7_RCC.cs L245; STM32H7_RCC.cs L247-250)", 394),
            R("RCC_D1CFGR", 0x018, "§8.7.8", "Res7", 7, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 395),
            R("RCC_D1CFGR", 0x018, "§8.7.8", "D1PPRE", 6, 4, Any, "acknowledged by name", "the stock model (stored+callback): stored; changeCallback derives d1PpreDivider (APB3 divider); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p395; STM32H7_RCC.cs L243; STM32H7_RCC.cs L247-250)", 395),
            R("RCC_D1CFGR", 0x018, "§8.7.8", "HPRE", 3, 0, Any, "acknowledged by name", "the stock model (stored+callback): stored; rcc_hclk = sys_ck / D1CPRE / HPRE (pp. 394-395) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x8: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p395; STM32H7_RCC.cs L242; STM32H7_RCC.cs L247-250)", 395),
            R("RCC_D2CFGR", 0x01C, "§8.7.9", "Res31_11", 31, 11, Zero, "reserved", "reserved: dropped, reads its reset value", 396),
            R("RCC_D2CFGR", 0x01C, "§8.7.9", "D2PPRE2", 10, 8, Any, "acknowledged by name", "the stock model (stored+callback): stored; rcc_pclk2 = rcc_hclk / D2PPRE2 (p396) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p352, p396; STM32H7_RCC.cs L256; STM32H7_RCC.cs L258-261)", 396),
            R("RCC_D2CFGR", 0x01C, "§8.7.9", "Res7", 7, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 396),
            R("RCC_D2CFGR", 0x01C, "§8.7.9", "D2PPRE1", 6, 4, Any, "acknowledged by name", "the stock model (stored+callback): stored; rcc_pclk1 = rcc_hclk / D2PPRE1 (p396) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z); read for 0x4: for the value written the twin's bus frequency equals silicon's (the divider's table, pp394-396); the guard's KernelHz and BusHz decode every value as the manual does (TVI-WO-1206 A5; p351, p352, p396; STM32H7_RCC.cs L254; STM32H7_RCC.cs L258-261)", 396),
            R("RCC_D2CFGR", 0x01C, "§8.7.9", "Res3_0", 3, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 396),
            R("RCC_D3CFGR", 0x020, "§8.7.10", "Res31_7", 31, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 397),
            R("RCC_D3CFGR", 0x020, "§8.7.10", "D3PPRE", 6, 4, Any, "stock, wrong (the stock model applies a new APB4 divider to its APB4 peripherals only at the next CR, CFGR, D1CFGR or D2CFGR write (no update on D3CFGR); unaffected: the armed run's CFGR write 3.98 us later applies it; no APB4 timer runs (lptimer2-5: no access in any run, spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture); the guard's own APB4 frequency is computed at the write)", "the stock model (stored+callback): stored; derives d3PpreDivider (APB4); NO register-level PropagateClock here; read for 0x4 (TVI-WO-1206; p397; STM32H7_RCC.cs L262-265; STM32H7_RCC.cs L1438-1441)", 397),
            R("RCC_D3CFGR", 0x020, "§8.7.10", "Res3_0", 3, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 397),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "Res31_26", 31, 26, Zero, "reserved", "reserved: dropped, reads its reset value", 398),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "DIVM3", 25, 20, Any, "modeled by the guard", "the guard's copy (the kernel clock query); ref3_ck = the PLLSRC source / DIVM3, 0 \"prescaler disabled\" (pp. 398-399), in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 398),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "Res19_18", 19, 18, Zero, "reserved", "reserved: dropped, reads its reset value", 398),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "DIVM2", 17, 12, Any, "modeled by the guard", "the guard's copy (the kernel clock query); ref2_ck = the PLLSRC source / DIVM2, 0 \"prescaler disabled\" (pp. 398-399), in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 398),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "Res11_10", 11, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 398),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "DIVM1", 9, 4, Any, "modeled by the guard", "the guard's copy (the kernel clock query); ref1_ck = the PLLSRC source / DIVM1, 0 \"prescaler disabled\" (pp. 398-399), in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 399),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "Res3_2", 3, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 399),
            R("RCC_PLLCKSELR", 0x028, "§8.7.11", "PLLSRC", 1, 0, Any, "modeled by the guard", "the guard's copy (the kernel clock query); the PLLs' reference, the HSI, CSI or HSE, 11 none (p399), in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 399),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "Res31_25", 31, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVR3EN", 24, 24, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVQ3EN", 23, 23, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVP3EN", 22, 22, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVR2EN", 21, 21, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVQ2EN", 20, 20, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 400),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVP2EN", 19, 19, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVR1EN", 18, 18, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVQ1EN", 17, 17, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "DIVP1EN", 16, 16, Any, "modeled by the guard", "the guard's copy (the kernel clock query)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "Res15_12", 15, 12, Zero, "reserved", "reserved: dropped, reads its reset value", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL3RGE", 11, 10, Any, "modeled by the guard", "the guard's copy, served on read (the stock model drops it): ref3_ck must lie in the range it names, 1-2, 2-4, 4-8 or 8-16 MHz, edges inclusive, else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency (\"must be set according to the reference input frequency\", p347; pp. 401-402; Q3)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL3VCOSEL", 9, 9, Any, "modeled by the guard", "the guard's copy, served on read: the VCO's output range, 192-960 MHz or 150-420 MHz (p404, p407, p410), and its input: VCOH takes 2-16 MHz, VCOL 1-2 MHz (p346, p347), else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency; VCOL above 2 MHz with its VCO in VCOL's range acknowledged by name, as ChibiOS chooses it (hal_lld_type1.h L1834-1837; bench row R11; Q1)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL3FRACEN", 8, 8, Any, "modeled by the guard", "the guard's copy, served on read; FRACN3 is 0 (its allow-list), so the PLL runs in integer mode whatever this bit says (p349, p405) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 401),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL2RGE", 7, 6, Any, "modeled by the guard", "the guard's copy, served on read (the stock model drops it): ref2_ck must lie in the range it names, 1-2, 2-4, 4-8 or 8-16 MHz, edges inclusive, else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency (\"must be set according to the reference input frequency\", p347; pp. 401-402; Q3)", 402),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL2VCOSEL", 5, 5, Any, "modeled by the guard", "the guard's copy, served on read: the VCO's output range, 192-960 MHz or 150-420 MHz (p404, p407, p410), and its input: VCOH takes 2-16 MHz, VCOL 1-2 MHz (p346, p347), else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency; VCOL above 2 MHz with its VCO in VCOL's range acknowledged by name, as ChibiOS chooses it (hal_lld_type1.h L1834-1837; bench row R11; Q1)", 402),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL2FRACEN", 4, 4, Any, "modeled by the guard", "the guard's copy, served on read; FRACN2 is 0 (its allow-list), so the PLL runs in integer mode whatever this bit says (p349, p405) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 402),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL1RGE", 3, 2, Any, "modeled by the guard", "the guard's copy, served on read (the stock model drops it): ref1_ck must lie in the range it names, 1-2, 2-4, 4-8 or 8-16 MHz, edges inclusive, else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency (\"must be set according to the reference input frequency\", p347; pp. 401-402; Q3)", 402),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL1VCOSEL", 1, 1, Any, "modeled by the guard", "the guard's copy, served on read: the VCO's output range, 192-960 MHz or 150-420 MHz (p404, p407, p410), and its input: VCOH takes 2-16 MHz, VCOL 1-2 MHz (p346, p347), else the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) has no frequency; VCOL above 2 MHz with its VCO in VCOL's range acknowledged by name, as ChibiOS chooses it (hal_lld_type1.h L1834-1837; bench row R11; Q1)", 402),
            R("RCC_PLLCFGR", 0x02C, "§8.7.12", "PLL1FRACEN", 0, 0, Any, "modeled by the guard", "the guard's copy, served on read; FRACN1 is 0 (its allow-list), so the PLL runs in integer mode whatever this bit says (p349, p405) in the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z)", 402),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 403),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "DIVR1", 30, 24, Any, "acknowledged by name", "the stock model (stored+callback); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x1: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p403; STM32H7_RCC.cs L359-366)", 403),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "Res23", 23, 23, Zero, "reserved", "reserved: dropped, reads its reset value", 403),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "DIVQ1", 22, 16, Any, "acknowledged by name", "the stock model (stored+callback); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x3,0x9: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L350-357)", 404),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "DIVP1", 15, 9, Any, "acknowledged by name", "the stock model (computed-read); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); DIVP1 = 0 or an odd divisor gives none (p404; Figure 47 p346; Q7); read for 0x1,0x3: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L329-334; STM32H7_RCC.cs L335-343)", 404),
            R("RCC_PLL1DIVR", 0x030, "§8.7.13", "DIVN1", 8, 0, Any, "acknowledged by name", "the stock model (computed-read); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 403-404): none after a write the stock model did not keep, or one made while PLL1 ran (Q2); read for 0x31,0x4f,0x9f: for the value written the twin's PLL1 frequency equals the manual's formula (p347: VCO = ref x (DIVN + 1), out = VCO / (DIVy + 1)), inside p346's input ranges: the armed run's VCOH on 16 MHz (HSI / 4), the generated boards' VCOH on 3.2 MHz (hse-crystal-16: HSE 16 / 5) and VCOL on 1.6 MHz (hse-external-8: 8 / 5), each VCO in its range (p404) (TVI-WO-1206 A6; p347, p404; STM32H7_RCC.cs L312-319; STM32H7_RCC.cs L320-328)", 404),
            R("RCC_PLL1FRACR", 0x034, "§8.7.14", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads its reset value", 405),
            R("RCC_PLL1FRACR", 0x034, "§8.7.14", "FRACN1", 15, 3, Zero, "tag", "its reset value (stock tag)", 405),
            R("RCC_PLL1FRACR", 0x034, "§8.7.14", "Res2_0", 2, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 405),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 406),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "DIVR2", 30, 24, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x7: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p406; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)", 406),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "Res23", 23, 23, Zero, "reserved", "reserved: dropped, reads its reset value", 406),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "DIVQ2", 22, 16, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x4: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)", 407),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "DIVP2", 15, 9, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x1: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)", 407),
            R("RCC_PLL2DIVR", 0x038, "§8.7.15", "DIVN2", 8, 0, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 406-407): none after a write the stock model did not keep, or one made while PLL2 ran (Q2); read for 0x2c: by TVI-WO-1300c's ruling Q1 (2026-11-26T04:03Z), on field evidence: PLL2 runs VCOL (PLLCFGR 0x1BF0FFD, PLL2VCOSEL = 1) on an 8 MHz reference (PLLCKSELR 0x408040: HSI 64 MHz / DIVM2 8), where p346 gives VCOL 1 to 2 MHz and p347 says VCOL must be chosen only at or below 2 MHz; RM0433 then guarantees no frequency. The twin serves the formula's (pll2_q_ck 72 MHz, every USART's kernel clock), as the PLL2VCOSEL row acknowledges by name (as ChibiOS chooses it; bench row R11). PLL2's stock fields are narrower than silicon's, and every value written fits them (TVI-WO-1206 A13; p346, p347, p407; STM32H7_RCC.cs L372-378; PeripheralRegister.cs L595-602)", 407),
            R("RCC_PLL2FRACR", 0x03C, "§8.7.16", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads its reset value", 408),
            R("RCC_PLL2FRACR", 0x03C, "§8.7.16", "FRACN2", 15, 3, Zero, "tag", "its reset value (stock tag)", 408),
            R("RCC_PLL2FRACR", 0x03C, "§8.7.16", "Res2_0", 2, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 408),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 409),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "DIVR3", 30, 24, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x7: by the architect's ruling, 2026-11-27T00:10Z, on field evidence: p347 says \"To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to '0'. If the above rules are not respected, the PLL output frequency is not guaranteed.\" The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346's 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3's outputs in this configuration. The twin serves the formula's 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p409; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)", 409),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "Res23", 23, 23, Zero, "reserved", "reserved: dropped, reads its reset value", 409),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "DIVQ3", 22, 16, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x4: by the architect's ruling, 2026-11-27T00:10Z, on field evidence: p347 says \"To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to '0'. If the above rules are not respected, the PLL output frequency is not guaranteed.\" The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346's 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3's outputs in this configuration. The twin serves the formula's 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)", 410),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "DIVP3", 15, 9, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0x7: by the architect's ruling, 2026-11-27T00:10Z, on field evidence: p347 says \"To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to '0'. If the above rules are not respected, the PLL output frequency is not guaranteed.\" The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346's 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3's outputs in this configuration. The twin serves the formula's 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)", 410),
            R("RCC_PLL3DIVR", 0x040, "§8.7.17", "DIVN3", 8, 0, Any, "acknowledged by name", "the stock model (stored, DIVN and DIVQ narrowed to 8 and 6 bits where silicon has 9 and 7, a row); the guard's copy of the write it passed, silicon's value, feeds the guard's KernelHz and BusHz (the overlay's one frequency rule; TVI-WO-1300c, ruled 2026-11-26T04:03Z) (pp. 409-410): none after a write the stock model did not keep, or one made while PLL3 ran (Q2); read for 0xe: by the architect's ruling, 2026-11-27T00:10Z, on field evidence: p347 says \"To insure an optimal behavior of the PLL when one of the post-divider (DIVP, DIVQ or DIVR) is not used, the application shall set the enable bit (DIVyEN) as well as the corresponding post-divider bits (DIVP, DIVQ or DIVR) to '0'. If the above rules are not respected, the PLL output frequency is not guaranteed.\" The armed run writes PLLCFGR 0x1FF0000, PLL3DIVR 0x7040E0E, PLLCFGR 0x1BF0FFD (DIVP3EN = 0, DIVP3 = 7), then CR 0x15003025 (PLL1-3 on). PLL3 also runs VCOL on a 16 MHz reference (PLL3VCOSEL = 1, DIVM3 4), outside p346's 1 to 2 MHz (the PLL3VCOSEL row, Q1, R11). RM0433 guarantees no frequency for PLL3's outputs in this configuration. The twin serves the formula's 30 MHz on pll3_r_ck to I2C1/2 and the ADC. ArduPilot flies this configuration on silicon, which is field evidence, not the manual. Bench row BR1 (TVI-WO-1206 FEEDBACK §5): measure pll3_r_ck on the Nucleo with DIVP3EN = 0 and DIVP3 = 7, by I2C SCL timing (TVI-WO-1206 A11; p346, p347, p410; STM32H7_RCC.cs L383-389; PeripheralRegister.cs L595-602)", 410),
            R("RCC_PLL3FRACR", 0x044, "§8.7.18", "Res31_16", 31, 16, Zero, "reserved", "reserved: dropped, reads its reset value", 411),
            R("RCC_PLL3FRACR", 0x044, "§8.7.18", "FRACN3", 15, 3, Zero, "tag", "its reset value (stock tag)", 411),
            R("RCC_PLL3FRACR", 0x044, "§8.7.18", "Res2_0", 2, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 411),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "CKPERSEL", 29, 28, new uint[] { 0, 1, 2, 3 }, "modeled by the guard", "the guard's copy and its mux (p371)", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "Res27_17", 27, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "SDMMCSEL", 16, 16, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "Res15_6", 15, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "QSPISEL", 5, 4, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "Res3_2", 3, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 412),
            R("RCC_D1CCIPR", 0x04C, "§8.7.19", "FMCSEL", 1, 0, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 412),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SWPSEL", 31, 31, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res30", 30, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "FDCANSEL", 29, 28, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res27_25", 27, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "DFSDM1SEL", 24, 24, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res23_22", 23, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SPDIFSEL", 21, 20, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res19", 19, 19, Zero, "reserved", "reserved: dropped, reads its reset value", 413),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SPI45SEL", 18, 16, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 414),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res15", 15, 15, Zero, "reserved", "reserved: dropped, reads its reset value", 414),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SPI123SEL", 14, 12, new uint[] { 0, 1, 2, 4 }, "modeled by the guard", "the guard's copy and its mux (p371)", 414),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res11_9", 11, 9, Zero, "reserved", "reserved: dropped, reads its reset value", 414),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SAI23SEL", 8, 6, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 415),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "Res5_3", 5, 3, Zero, "reserved", "reserved: dropped, reads its reset value", 415),
            R("RCC_D2CCIP1R", 0x050, "§8.7.20", "SAI1SEL", 2, 0, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 415),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "LPTIM1SEL", 30, 28, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "Res27_24", 27, 24, Zero, "reserved", "reserved: dropped, reads its reset value", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "CECSEL", 23, 22, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "USBSEL", 21, 20, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "Res19_14", 19, 14, Zero, "reserved", "reserved: dropped, reads its reset value", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "I2C123SEL", 13, 12, new uint[] { 0, 1, 2, 3 }, "modeled by the guard", "the guard's copy and its mux (p371)", 416),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "Res11_10", 11, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 417),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "RNGSEL", 9, 8, new uint[] { 0, 1 }, "modeled by the guard", "the guard's copy and its mux (p371)", 417),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "Res7_6", 7, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 417),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "USART16SEL", 5, 3, new uint[] { 0, 1, 2, 3, 4, 6, 7 }, "modeled by the guard", "the guard's copy and its mux (p371)", 417),
            R("RCC_D2CCIP2R", 0x054, "§8.7.21", "USART234578SEL", 2, 0, new uint[] { 0, 1, 2, 3, 4, 6, 7 }, "modeled by the guard", "the guard's copy and its mux (p371)", 417),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 418),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "SPI6SEL", 30, 28, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 418),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "Res27", 27, 27, Zero, "reserved", "reserved: dropped, reads its reset value", 418),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "SAI4BSEL", 26, 24, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 418),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "SAI4ASEL", 23, 21, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 419),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "Res20_18", 20, 18, Zero, "reserved", "reserved: dropped, reads its reset value", 419),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "ADCSEL", 17, 16, Any, "modeled by the guard", "the guard's copy, served on read; the ADCs' kernel clock mux, answered by the query for adc12 and adc3 (TVI-WO-1307 Q3)", 419),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "LPTIM345SEL", 15, 13, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 419),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "LPTIM2SEL", 12, 10, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 419),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "I2C4SEL", 9, 8, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 420),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "Res7_3", 7, 3, Zero, "reserved", "reserved: dropped, reads its reset value", 420),
            R("RCC_D3CCIPR", 0x058, "§8.7.22", "LPUART1SEL", 2, 0, Any, "modeled by the guard", "the guard's copy, served on read; this mux's peripherals are not modeled, and their enable refuses", 420),
            R("RCC_CIER", 0x060, "§8.7.23", "Res31_10", 31, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "LSECSSIE", 9, 9, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "PLL3RDYIE", 8, 8, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "PLL2RDYIE", 7, 7, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "PLL1RDYIE", 6, 6, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "HSI48RDYIE", 5, 5, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "CSIRDYIE", 4, 4, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "HSERDYIE", 3, 3, Zero, "tag", "its reset value (stock tag)", 421),
            R("RCC_CIER", 0x060, "§8.7.23", "HSIRDYIE", 2, 2, Zero, "tag", "its reset value (stock tag)", 422),
            R("RCC_CIER", 0x060, "§8.7.23", "LSERDYIE", 1, 1, Zero, "tag", "its reset value (stock tag)", 422),
            R("RCC_CIER", 0x060, "§8.7.23", "LSIRDYIE", 0, 0, Zero, "tag", "its reset value (stock tag)", 422),
            R("RCC_CIFR", 0x064, "§8.7.24", "Res31_11", 31, 11, Zero, "reserved", "reserved: dropped, reads its reset value", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "HSECSSF", 10, 10, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "LSECSSF", 9, 9, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "PLL3RDYF", 8, 8, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "PLL2RDYF", 7, 7, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "PLL1RDYF", 6, 6, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "HSI48RDYF", 5, 5, Any, "read-only", "ignored (read-only; stock tag)", 423),
            R("RCC_CIFR", 0x064, "§8.7.24", "CSIRDYF", 4, 4, Any, "read-only", "ignored (read-only; stock tag)", 424),
            R("RCC_CIFR", 0x064, "§8.7.24", "HSERDYF", 3, 3, Any, "read-only", "ignored (read-only; stock tag)", 424),
            R("RCC_CIFR", 0x064, "§8.7.24", "HSIRDYF", 2, 2, Any, "read-only", "ignored (read-only; stock tag)", 424),
            R("RCC_CIFR", 0x064, "§8.7.24", "LSERDYF", 1, 1, Any, "read-only", "ignored (read-only; stock tag)", 424),
            R("RCC_CIFR", 0x064, "§8.7.24", "LSIRDYF", 0, 0, Any, "read-only", "ignored (read-only; stock tag)", 424),
            R("RCC_CICR", 0x068, "§8.7.25", "Res31_11", 31, 11, Zero, "reserved", "reserved: dropped, reads its reset value", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "HSECSSC", 10, 10, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "LSECSSC", 9, 9, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "PLL3RDYC", 8, 8, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "PLL2RDYC", 7, 7, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "PLL1RDYC", 6, 6, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "HSI48RDYC", 5, 5, Zero, "tag", "its reset value (stock tag)", 425),
            R("RCC_CICR", 0x068, "§8.7.25", "CSIRDYC", 4, 4, Zero, "tag", "its reset value (stock tag)", 426),
            R("RCC_CICR", 0x068, "§8.7.25", "HSERDYC", 3, 3, Zero, "tag", "its reset value (stock tag)", 426),
            R("RCC_CICR", 0x068, "§8.7.25", "HSIRDYC", 2, 2, Zero, "tag", "its reset value (stock tag)", 426),
            R("RCC_CICR", 0x068, "§8.7.25", "LSERDYC", 1, 1, Zero, "tag", "its reset value (stock tag)", 426),
            R("RCC_CICR", 0x068, "§8.7.25", "LSIRDYC", 0, 0, Zero, "tag", "its reset value (stock tag)", 426),
            R("RCC_BDCR", 0x070, "§8.7.26", "Res31_17", 31, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "BDRST", 16, 16, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427; STM32H7_RCC.cs L496; STM32H7_RCC.cs L483)", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "RTCEN", 15, 15, new uint[] { 0, 1 }, "acknowledged by name", "the guard's copy, served on read: 0 from construction (a backup domain reset, p427, p334), kept across a machine reset (p427); it gates rtc_ck only (p366), which has no source while RTCSEL = 00 (\"No clock\", p427), and the RTC's registers follow RCC_APB4ENR.RTCAPBEN (p471), so `rtc` is never locked by it; RCC_BDCR's write protection (PWR_CR1.DBP, p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1 and the write is taken (acknowledged by name) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q4); read for 0x0,0x1: written 0, then 1 at 4.93 s (BDCR 0x8000), and read back 1 every 100 ms (90 reads): silicon's register holds it and the guard serves its copy (the guard keeps rtc on the bus itself); with RTCSEL = 00 rtc_ck has no source on either side (p427), and the firmware uses only the RTC's backup registers, which follow RCC_APB4ENR.RTCAPBEN (p366): no time register is read (TVI-WO-1206 A3; p305, p366, p427, p471; STM32H7_RCC.cs L493-495; STM32H7_RCC.cs L1364)", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "Res14_10", 14, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "RTCSEL", 9, 8, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427; STM32H7_RCC.cs L491; STM32F4_RTC.cs L32)", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "Res7", 7, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 427),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSECSSD", 6, 6, Any, "read-only", "ignored (read-only; stock tag)", 428),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSECSSON", 5, 5, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L488)", 428),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSEDRV", 4, 3, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L487)", 428),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSEBYP", 2, 2, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L486)", 428),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSERDY", 1, 1, Any, "read-only", "ignored (read-only; stock computed-read)", 428),
            R("RCC_BDCR", 0x070, "§8.7.26", "LSEON", 0, 0, Zero, "acknowledged by name", "its reset value only: the RTC's kernel clock (RTCSEL) is not modeled; RCC_BDCR is write-protected after a system reset until PWR_CR1.DBP = 1 (p427): with a PWR guard on the platform, a write that changes RCC_BDCR while its DBP = 0 refuses TVI-RCC-DBP, unmodeled (TVI-WO-1308, ruled 2026-11-26T09:31Z Q10); with none, the stock pwr stub reads DBP = 1; read for 0x0: its reset value, written 0: stored and read back alike; with RTCSEL = 00 the RTC has no clock on either side (p427) (TVI-WO-1206 A4; p305, p427, p428; STM32H7_RCC.cs L484-485)", 428),
            R("RCC_CSR", 0x074, "§8.7.27", "Res31_2", 31, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 429),
            R("RCC_CSR", 0x074, "§8.7.27", "LSIRDY", 1, 1, Any, "read-only", "ignored (read-only; stock computed-read)", 429),
            R("RCC_CSR", 0x074, "§8.7.27", "LSION", 0, 0, Any, "stock, wrong (the twin's LSIRDY follows LSION alone, where silicon also runs the LSI for a started IWDG (p344, p429; the armed run starts it at 4.77 s); unaffected: no firmware reads RCC_CSR: no read in the armed run, the generated boots or the fixture (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", "the stock model (stored): stored (local var); read for 0x0 (TVI-WO-1206; p344, p429; STM32H7_RCC.cs L498-501)", 429),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "Res31_17", 31, 17, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "SDMMC1RST", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SDMMC1].Reset() if registe", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "Res15", 15, 15, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "QSPIRST", 14, 14, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[QSPI].Reset() if registere", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "Res13", 13, 13, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "FMCRST", 12, 12, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[FMC].Reset() if registered", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "Res11_6", 11, 6, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "JPGDECRST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[JPGDEC].Reset() if registe", 430),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "DMA2DRST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DMA2D].Reset() if register", 431),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "Res3_1", 3, 1, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 431),
            R("RCC_AHB3RSTR", 0x07C, "§8.7.28", "MDMARST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[MDMA].Reset() if registere", 431),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "Res31_28", 31, 28, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "USB2OTGRST", 27, 27, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USB2OTGHS].Reset() if regi", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "Res26", 26, 26, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "USB1OTGRST", 25, 25, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USB2OTGHS].Reset() if regi", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "Res24_16", 24, 16, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "ETH1MACRST", 15, 15, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[ETH1MAC].Reset() if regist", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "Res14_6", 14, 6, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "ADC12RST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[ADC12].Reset() if register", 432),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "Res4_2", 4, 2, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 433),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "DMA2RST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DMA2].Reset() if registere", 433),
            R("RCC_AHB1RSTR", 0x080, "§8.7.29", "DMA1RST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DMA1].Reset() if registere", 433),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "Res31_10", 31, 10, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "SDMMC2RST", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SDMMC2].Reset() if registe", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "Res8_7", 8, 7, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "RNGRST", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[RNG].Reset() if registered", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "HASHRST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[HASH].Reset() if registere", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "CRYPTRST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[CRYPT].Reset() if register", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "Res3_1", 3, 1, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 434),
            R("RCC_AHB2RSTR", 0x084, "§8.7.30", "DCMIRST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DCMI].Reset() if registere", 434),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "Res31_26", 31, 26, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "HSEMRST", 25, 25, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[HSEM].Reset() if registere", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "ADC3RST", 24, 24, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[ADC3].Reset() if registere", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "Res23_22", 23, 22, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "BDMARST", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[BDMA].Reset() if registere", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "Res20", 20, 20, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "CRCRST", 19, 19, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[CRC].Reset() if registered", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "Res18_11", 18, 11, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOKRST", 10, 10, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOK].Reset() if register", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOJRST", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOJ].Reset() if register", 435),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOIRST", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOI].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOHRST", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOH].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOGRST", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOG].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOFRST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOF].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOERST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOE].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIODRST", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOD].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOCRST", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOC].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOBRST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOB].Reset() if register", 436),
            R("RCC_AHB4RSTR", 0x088, "§8.7.31", "GPIOARST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[GPIOA].Reset() if register", 436),
            R("RCC_APB3RSTR", 0x08C, "§8.7.32", "Res31_4", 31, 4, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 437),
            R("RCC_APB3RSTR", 0x08C, "§8.7.32", "LTDCRST", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LTDC].Reset() if registere", 437),
            R("RCC_APB3RSTR", 0x08C, "§8.7.32", "Res2_0", 2, 0, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 437),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "UART8RST", 31, 31, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[UART8].Reset() if register", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "UART7RST", 30, 30, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[UART7].Reset() if register", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "DAC12RST", 29, 29, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DAC12].Reset() if register", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "Res28", 28, 28, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "CECRST", 27, 27, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[CEC].Reset() if registered", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "Res26_24", 26, 24, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "I2C3RST", 23, 23, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[I2C3].Reset() if registere", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "I2C2RST", 22, 22, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[I2C2].Reset() if registere", 438),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "I2C1RST", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[I2C1].Reset() if registere", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "UART5RST", 20, 20, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[UART5].Reset() if register", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "UART4RST", 19, 19, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[UART4].Reset() if register", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "USART3RST", 18, 18, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USART3].Reset() if registe", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "USART2RST", 17, 17, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USART2].Reset() if registe", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "SPDIFRXRST", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPDIFRX].Reset() if regist", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "SPI3RST", 15, 15, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI3].Reset() if registere", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "SPI2RST", 14, 14, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI2].Reset() if registere", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "Res13_10", 13, 10, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "LPTIM1RST", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPTIM1].Reset() if registe", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM14RST", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM14].Reset() if register", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM13RST", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM13].Reset() if register", 439),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM12RST", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM12].Reset() if register", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM7RST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM7].Reset() if registere", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM6RST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM6].Reset() if registere", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM5RST", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM5].Reset() if registere", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM4RST", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM4].Reset() if registere", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM3RST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM3].Reset() if registere", 440),
            R("RCC_APB1LRSTR", 0x090, "§8.7.33", "TIM2RST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM2].Reset() if registere", 440),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "Res31_9", 31, 9, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "FDCANRST", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[FDCAN].Reset() if register", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "Res7_6", 7, 6, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "MDIOSRST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[MDIOS].Reset() if register", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "OPAMPRST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[OPAMP].Reset() if register", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "Res3", 3, 3, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "SWPRST", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SWP].Reset() if registered", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "CRSRST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[CRS].Reset() if registered", 441),
            R("RCC_APB1HRSTR", 0x094, "§8.7.34", "Res0", 0, 0, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 441),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res31_30", 31, 30, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "HRTIMRST", 29, 29, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[HRTIM].Reset() if register", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "DFSDM1RST", 28, 28, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[DFSDM1].Reset() if registe", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res27_25", 27, 25, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SAI3RST", 24, 24, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SAI3].Reset() if registere", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SAI2RST", 23, 23, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SAI2].Reset() if registere", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SAI1RST", 22, 22, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SAI1].Reset() if registere", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res21", 21, 21, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SPI5RST", 20, 20, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI5].Reset() if registere", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res19", 19, 19, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 442),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "TIM17RST", 18, 18, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM17].Reset() if register", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "TIM16RST", 17, 17, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM16].Reset() if register", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "TIM15RST", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM15].Reset() if register", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res15_14", 15, 14, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SPI4RST", 13, 13, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI4].Reset() if registere", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "SPI1RST", 12, 12, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI1].Reset() if registere", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res11_6", 11, 6, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "USART6RST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USART6].Reset() if registe", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "USART1RST", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[USART1].Reset() if registe", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "Res3_2", 3, 2, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "TIM8RST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM8].Reset() if registere", 443),
            R("RCC_APB2RSTR", 0x098, "§8.7.35", "TIM1RST", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[TIM1].Reset() if registere", 443),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res31_26", 31, 26, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res25_22", 25, 22, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "SAI4RST", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SAI4].Reset() if registere", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res20_16", 20, 16, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "VREFRST", 15, 15, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[VREF].Reset() if registere", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "COMP12RST", 14, 14, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[COMP12].Reset() if registe", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res13", 13, 13, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "LPTIM5RST", 12, 12, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPTIM5].Reset() if registe", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "LPTIM4RST", 11, 11, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPTIM4].Reset() if registe", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "LPTIM3RST", 10, 10, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPTIM3].Reset() if registe", 444),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "LPTIM2RST", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPTIM2].Reset() if registe", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res8", 8, 8, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "I2C4RST", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[I2C4].Reset() if registere", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res6", 6, 6, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "SPI6RST", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SPI6].Reset() if registere", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res4", 4, 4, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "LPUART1RST", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[LPUART1].Reset() if regist", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res2", 2, 2, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "SYSCFGRST", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model (stored+callback): stored (reads back as written, never self-clears); on any write of 1 -> RegisteredPeripherals[SYSCFG].Reset() if registe", 445),
            R("RCC_APB4RSTR", 0x09C, "§8.7.36", "Res0", 0, 0, Any, "acknowledged by name", "reserved: no flop on either side; written 1 by all-ones resets (TVI-WO-1300 A4)", 445),
            R("RCC_GCR", 0x0A0, "§8.7.37", "Res31_1", 31, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 446),
            R("RCC_GCR", 0x0A0, "§8.7.37", "WW1RSC", 0, 0, Zero, "tag", "its reset value (stock tag)", 446),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "SRAM4AMEN", 29, 29, Zero, "tag", "its reset value (stock tag)", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "BKPRAMAMEN", 28, 28, Zero, "tag", "its reset value (stock tag)", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res27", 27, 27, Zero, "reserved", "reserved: dropped, reads its reset value", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res26_25", 26, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "ADC3AMEN", 24, 24, Zero, "tag", "its reset value (stock tag)", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res23_22", 23, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "SAI4AMEN", 21, 21, Zero, "tag", "its reset value (stock tag)", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res20", 20, 20, Zero, "reserved", "reserved: dropped, reads its reset value", 447),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "CRCAMEN", 19, 19, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res18_17", 18, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "RTCAMEN", 16, 16, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "VREFAMEN", 15, 15, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "COMP12AMEN", 14, 14, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res13", 13, 13, Zero, "reserved", "reserved: dropped, reads its reset value", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "LPTIM5AMEN", 12, 12, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "LPTIM4AMEN", 11, 11, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "LPTIM3AMEN", 10, 10, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "LPTIM2AMEN", 9, 9, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res8", 8, 8, Zero, "reserved", "reserved: dropped, reads its reset value", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "I2C4AMEN", 7, 7, Zero, "tag", "its reset value (stock tag)", 448),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res6", 6, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 449),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "SPI6AMEN", 5, 5, Zero, "tag", "its reset value (stock tag)", 449),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res4", 4, 4, Zero, "reserved", "reserved: dropped, reads its reset value", 449),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "LPUART1AMEN", 3, 3, Zero, "tag", "its reset value (stock tag)", 449),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "Res2_1", 2, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 449),
            R("RCC_D3AMR", 0x0A8, "§8.7.38", "BDMAAMEN", 0, 0, Zero, "tag", "its reset value (stock tag)", 449),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res31", 31, 31, Zero, "reserved", "reserved: dropped, reads its reset value", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "LPWRRSTF", 30, 30, Any, "read-only", "ignored (read-only; stock tag)", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res29", 29, 29, Zero, "reserved", "reserved: dropped, reads its reset value", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "WWDG1RSTF", 28, 28, Any, "read-only", "ignored (read-only; stock tag)", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res27", 27, 27, Zero, "reserved", "reserved: dropped, reads its reset value", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "IWDG1RSTF", 26, 26, Any, "read-only", "ignored (read-only; stock tag)", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res25", 25, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 450),
            R("RCC_RSR", 0x0D0, "§8.7.39", "SFTRSTF", 24, 24, Any, "read-only", "ignored (read-only; stock tag)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "PORRSTF", 23, 23, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "PINRSTF", 22, 22, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "BORRSTF", 21, 21, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "D2RSTF", 20, 20, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "D1RSTF", 19, 19, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res18", 18, 18, new uint[] { 0, 1 }, "acknowledged by name", "reserved, reset 1 by the register's 0x00FE0000: written 0 with RMVF, ignored by both (TVI-WO-1300 A5)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "CPURSTF", 17, 17, Any, "read-only", "ignored (read-only; stock stored)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "RMVF", 16, 16, Any, "stock, wrong (RMVF reads back 0 (write-one-to-clear) where silicon's is set and reset by software (p451); the flags it clears read 0 after on both sides; unaffected: RCC_RSR is read once, before the write, by the armed run and by the fixture, and never after (spike/1206/data: the armed run (main-8e15435/reads.tsv), the 14 generated boots and the fixture (gen-reads.tsv)))", "the stock model (write-only-action): FieldMode.WriteOneToClear: always reads 0; write 1 clears C1RSTF,C2RSTF,D1RSTF,D2RSTF,BORRSTF,PINRSTF,PORRSTF (not the t; read for 0x1 (TVI-WO-1206; p333, p450, p451; STM32H7_RCC.cs L672-679; STM32H7_RCC.cs L680-684)", 451),
            R("RCC_RSR", 0x0D0, "§8.7.39", "Res15_0", 15, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 451),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "Res31_17", 31, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "SDMMC1EN", 16, 16, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "Res15", 15, 15, Zero, "reserved", "reserved: dropped, reads its reset value", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "QSPIEN", 14, 14, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "Res13", 13, 13, Zero, "reserved", "reserved: dropped, reads its reset value", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "FMCEN", 12, 12, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "Res11_6", 11, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "JPGDECEN", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 452),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "DMA2DEN", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 453),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "Res3_1", 3, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 453),
            R("RCC_AHB3ENR", 0x0D4, "§8.7.40", "MDMAEN", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 453),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "Res31_29", 31, 29, Zero, "reserved", "reserved: dropped, reads its reset value", 454),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "USB2OTGHSULPIEN", 28, 28, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p454; STM32H7_RCC.cs L759; STM32H7_RCC.cs L760)", 454),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "USB2OTGHSEN", 27, 27, Zero, "stock, wrong (a 0 leaves the twin's OTG core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses usb2: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0 (TVI-WO-1206; p373, p454; STM32H7_RCC.cs L756; STM32H7_RCC.cs L757)", 454),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "USB1OTGHSULPIEN", 26, 26, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p454; STM32H7_RCC.cs L753; STM32H7_RCC.cs L754)", 454),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "USB1OTGHSEN", 25, 25, Zero, "stock, wrong (a 0 leaves the twin's OTG_HS core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses usb1: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0 (TVI-WO-1206; p373, p454; STM32H7_RCC.cs L750; STM32H7_RCC.cs L751)", 454),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "Res24_18", 24, 18, Zero, "reserved", "reserved: dropped, reads its reset value", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "ETH1RXEN", 17, 17, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p455; STM32H7_RCC.cs L746; STM32H7_RCC.cs L747)", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "ETH1TXEN", 16, 16, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p455; STM32H7_RCC.cs L743; STM32H7_RCC.cs L744)", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "ETH1MACEN", 15, 15, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L740; STM32H7_RCC.cs L741)", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "Res14_6", 14, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "ADC12EN", 5, 5, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read): the bus lock only. With ADCx_CCR.CKMODE = 00 the ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L736; STM32H7_RCC.cs L737)", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "Res4_2", 4, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "DMA2EN", 1, 1, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L732; STM32H7_RCC.cs L733)", 455),
            R("RCC_AHB1ENR", 0x0D8, "§8.7.41", "DMA1EN", 0, 0, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p455; STM32H7_RCC.cs L729; STM32H7_RCC.cs L730)", 455),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "SRAM3EN", 31, 31, Any, "stock, wrong (the twin never gates SRAM3, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: as SRAM2EN: 0 only from reset to its enable; that window is not measured (a row))", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L788; STM32H7_RCC.cs L789)", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "SRAM2EN", 30, 30, Any, "stock, wrong (the twin never gates SRAM2, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: every AHB2ENR write only sets bits (0x20000000, 0x60000000, 0xE0000000, 0xE0000040, the first at 4.06 us), so the bit is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row))", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L785; STM32H7_RCC.cs L786)", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "SRAM1EN", 29, 29, Any, "stock, wrong (the twin never gates SRAM1, where silicon needs the bit set before the CPU uses it (p369, p456); unaffected: AHB2ENR's first write (0x20000000, at 4.06 us) sets it, and every later one only sets bits, so it is 0 only from reset to its enable; the access log covers peripherals, not memory, so that window is not measured (a row))", "the guard's copy; the stock bus enable (computed-read); read for 0x1 (TVI-WO-1206; p373, p456; STM32H7_RCC.cs L782; STM32H7_RCC.cs L783)", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "Res28_10", 28, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "SDMMC2EN", 9, 9, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p456; STM32H7_RCC.cs L778; STM32H7_RCC.cs L779)", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "Res8_7", 8, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 456),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "RNGEN", 6, 6, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 457),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "HASHEN", 5, 5, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p457; STM32H7_RCC.cs L771; STM32H7_RCC.cs L772)", 457),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "CRYPTEN", 4, 4, Any, "stock, wrong (a 0 leaves the twin's CRYP core reachable, where silicon ignores its writes and reads invalid data (p373); unaffected: no firmware accesses crypto: no read or write in any run (spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture))", "the guard's copy; the stock bus enable (computed-read); read for 0x0 (TVI-WO-1206; p373, p457; STM32H7_RCC.cs L768; STM32H7_RCC.cs L769)", 457),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "Res3_1", 3, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 457),
            R("RCC_AHB2ENR", 0x0DC, "§8.7.42", "DCMIEN", 0, 0, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p457; STM32H7_RCC.cs L764; STM32H7_RCC.cs L765)", 457),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "Res31_29", 31, 29, Zero, "reserved", "reserved: dropped, reads its reset value", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "BKPRAMEN", 28, 28, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "Res27_26", 27, 26, Zero, "reserved", "reserved: dropped, reads its reset value", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "HSEMEN", 25, 25, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "ADC3EN", 24, 24, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read): the bus lock only. With ADCx_CCR.CKMODE = 00 the ADC's kernel clock is the one ADCSEL selects (p920, p419), answered by the query; the ADC guard asks it at ADSTART and registers as watched (TVI-WO-1307 Q3) (TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p458; STM32H7_RCC.cs L834; STM32H7_RCC.cs L835)", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "Res23_22", 23, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "BDMAEN", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "Res20", 20, 20, Zero, "reserved", "reserved: dropped, reads its reset value", 458),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "CRCEN", 19, 19, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "Res18_11", 18, 11, Zero, "reserved", "reserved: dropped, reads its reset value", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOKEN", 10, 10, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOJEN", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOIEN", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOHEN", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOGEN", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOFEN", 5, 5, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L807; STM32H7_RCC.cs L808)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOEEN", 4, 4, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L804; STM32H7_RCC.cs L805)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIODEN", 3, 3, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p459; STM32H7_RCC.cs L801; STM32H7_RCC.cs L802)", 459),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOCEN", 2, 2, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p460; STM32H7_RCC.cs L798; STM32H7_RCC.cs L799)", 460),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOBEN", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 460),
            R("RCC_AHB4ENR", 0x0E0, "§8.7.43", "GPIOAEN", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 460),
            R("RCC_APB3ENR", 0x0E4, "§8.7.44", "Res31_7", 31, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 461),
            R("RCC_APB3ENR", 0x0E4, "§8.7.44", "WWDG1EN", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model (computed-read): write: if WWDG1 registered in RCC -> sysbus.SetPeripheralEnabled(peri,value) (lock/unlock on bus), else no-op (Noisy log", 461),
            R("RCC_APB3ENR", 0x0E4, "§8.7.44", "Res5_4", 5, 4, Zero, "reserved", "reserved: dropped, reads its reset value", 461),
            R("RCC_APB3ENR", 0x0E4, "§8.7.44", "LTDCEN", 3, 3, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 461),
            R("RCC_APB3ENR", 0x0E4, "§8.7.44", "Res2_0", 2, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 461),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "UART8EN", 31, 31, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "UART7EN", 30, 30, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "DAC12EN", 29, 29, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "Res28", 28, 28, Zero, "reserved", "reserved: dropped, reads its reset value", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "CECEN", 27, 27, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "Res26_24", 26, 24, Zero, "reserved", "reserved: dropped, reads its reset value", 462),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "I2C3EN", 23, 23, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "I2C2EN", 22, 22, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "I2C1EN", 21, 21, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "UART5EN", 20, 20, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "UART4EN", 19, 19, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "USART3EN", 18, 18, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "USART2EN", 17, 17, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "SPDIFRXEN", 16, 16, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 463),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "SPI3EN", 15, 15, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "SPI2EN", 14, 14, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "Res13_10", 13, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "LPTIM1EN", 9, 9, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM14EN", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM13EN", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM12EN", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM7EN", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM6EN", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM5EN", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 464),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM4EN", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 465),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM3EN", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 465),
            R("RCC_APB1LENR", 0x0E8, "§8.7.45", "TIM2EN", 0, 0, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p465; STM32H7_RCC.cs L856; STM32H7_RCC.cs L857)", 465),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "Res31_9", 31, 9, Zero, "reserved", "reserved: dropped, reads its reset value", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "FDCANEN", 8, 8, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "Res7_6", 7, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "MDIOSEN", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "OPAMPEN", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "Res3", 3, 3, Zero, "reserved", "reserved: dropped, reads its reset value", 466),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "SWPEN", 2, 2, Zero, "inherited, unaudited (acknowledged by name)", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4))", 467),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "CRSEN", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the guard's copy; the stock bus enable (computed-read)", 467),
            R("RCC_APB1HENR", 0x0EC, "§8.7.46", "Res0", 0, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 467),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "HRTIMEN", 29, 29, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L1000; STM32H7_RCC.cs L1001)", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "DFSDM1EN", 28, 28, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L997; STM32H7_RCC.cs L998)", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res27_25", 27, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SAI3EN", 24, 24, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L993; STM32H7_RCC.cs L994)", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SAI2EN", 23, 23, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p468; STM32H7_RCC.cs L990; STM32H7_RCC.cs L991)", 468),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SAI1EN", 22, 22, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p469; STM32H7_RCC.cs L987; STM32H7_RCC.cs L988)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res21", 21, 21, Zero, "reserved", "reserved: dropped, reads its reset value", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SPI5EN", 20, 20, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p469; STM32H7_RCC.cs L983; STM32H7_RCC.cs L984)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res19", 19, 19, Zero, "reserved", "reserved: dropped, reads its reset value", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "TIM17EN", 18, 18, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L979; STM32H7_RCC.cs L980)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "TIM16EN", 17, 17, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L976; STM32H7_RCC.cs L977)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "TIM15EN", 16, 16, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L973; STM32H7_RCC.cs L974)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res15_14", 15, 14, Zero, "reserved", "reserved: dropped, reads its reset value", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SPI4EN", 13, 13, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L969; STM32H7_RCC.cs L970)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "SPI1EN", 12, 12, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res11_6", 11, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "USART6EN", 5, 5, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p469; STM32H7_RCC.cs L962; STM32H7_RCC.cs L963)", 469),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "USART1EN", 4, 4, Any, "modeled by the guard", "the guard's copy and the stock bus enable; the kernel clock query", 470),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "Res3_2", 3, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 470),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "TIM8EN", 1, 1, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p470; STM32H7_RCC.cs L955; STM32H7_RCC.cs L956)", 470),
            R("RCC_APB2ENR", 0x0F0, "§8.7.47", "TIM1EN", 0, 0, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0,0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p470; STM32H7_RCC.cs L952; STM32H7_RCC.cs L953)", 470),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res31_26", 31, 26, Zero, "reserved", "reserved: dropped, reads its reset value", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res25_22", 25, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "SAI4EN", 21, 21, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1043; STM32H7_RCC.cs L1044)", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res20_17", 20, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "RTCAPBEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "VREFEN", 15, 15, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1038; STM32H7_RCC.cs L1039)", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "COMP12EN", 14, 14, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p471; STM32H7_RCC.cs L1035; STM32H7_RCC.cs L1036)", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res13", 13, 13, Zero, "reserved", "reserved: dropped, reads its reset value", 471),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "LPTIM5EN", 12, 12, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1031; STM32H7_RCC.cs L1032)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "LPTIM4EN", 11, 11, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1028; STM32H7_RCC.cs L1029)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "LPTIM3EN", 10, 10, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1025; STM32H7_RCC.cs L1026)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "LPTIM2EN", 9, 9, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1022; STM32H7_RCC.cs L1023)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res8", 8, 8, Zero, "reserved", "reserved: dropped, reads its reset value", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "I2C4EN", 7, 7, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1018; STM32H7_RCC.cs L1019)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res6", 6, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "SPI6EN", 5, 5, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: written 0 only: the bit is stored and reads back from the guard's copy as silicon's does, and the stock RCC registers no peripheral at it, so it gates nothing; no firmware accesses a peripheral behind it (ethernet, the one present, has no access in any run: spike/1206/data/main-8e15435/instance-accesses.tsv: the armed run, the 14 generated boots and the fixture) (TVI-WO-1206 A2; p373, p472; STM32H7_RCC.cs L1014; STM32H7_RCC.cs L1015)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res4", 4, 4, Zero, "reserved", "reserved: dropped, reads its reset value", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "LPUART1EN", 3, 3, Zero, "acknowledged by name", "disabled: a kernel-clocked peripheral the query does not cover (TVI-WO-1300 (4)); read for 0x0: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p472; STM32H7_RCC.cs L1010-1012; STM32H7_RCC.cs L1355-1364)", 472),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res2", 2, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 473),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "SYSCFGEN", 1, 1, Any, "acknowledged by name", "the guard's copy; the stock bus enable (computed-read); read for 0x1: a 0 gates the peripheral's clock and its registers then ignore writes and read invalid data (p373), a 1 clocks it; the stock RCC sets the peripheral's bus enable from this bit, so a 0 drops its accesses and reads 0, and the bit reads back from the guard's copy (TVI-WO-1206 A1; p373, p473; STM32H7_RCC.cs L1006; STM32H7_RCC.cs L1007)", 473),
            R("RCC_APB4ENR", 0x0F4, "§8.7.48", "Res0", 0, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 473),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "AXISRAMLPEN", 31, 31, new uint[] { 1 }, "tag", "its reset value (stock tag)", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "ITCMLPEN", 30, 30, new uint[] { 1 }, "tag", "its reset value (stock tag)", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "DTCM2LPEN", 29, 29, new uint[] { 1 }, "tag", "its reset value (stock tag)", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "D1DTCM1LPEN", 28, 28, new uint[] { 1 }, "tag", "its reset value (stock tag)", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res27_17", 27, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "SDMMC1LPEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res15", 15, 15, Zero, "reserved", "reserved: dropped, reads its reset value", 474),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "QSPILPEN", 14, 14, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res13", 13, 13, Zero, "reserved", "reserved: dropped, reads its reset value", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "FMCLPEN", 12, 12, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res11_9", 11, 9, Zero, "reserved", "reserved: dropped, reads its reset value", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "FLASHLPEN", 8, 8, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res7_6", 7, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "JPGDECLPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "DMA2DLPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "Res3_1", 3, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 475),
            R("RCC_AHB3LPENR", 0x0FC, "§8.7.49", "MDMALPEN", 0, 0, new uint[] { 1 }, "tag", "its reset value (stock tag)", 475),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "Res31_29", 31, 29, Zero, "reserved", "reserved: dropped, reads its reset value", 476),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "USB2OTGHSULPILPEN", 28, 28, new uint[] { 1 }, "tag", "its reset value (stock tag)", 476),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "USB2OTGHSLPEN", 27, 27, new uint[] { 1 }, "tag", "its reset value (stock tag)", 476),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "USB1OTGHSULPILPEN", 26, 26, new uint[] { 1 }, "tag", "its reset value (stock tag)", 476),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "USB1OTGHSLPEN", 25, 25, new uint[] { 1 }, "tag", "its reset value (stock tag)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "Res24_18", 24, 18, Zero, "reserved", "reserved: dropped, reads its reset value", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "ETH1RXLPEN", 17, 17, new uint[] { 1 }, "tag", "its reset value (stock tag)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "ETH1TXLPEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "ETH1MACLPEN", 15, 15, new uint[] { 1 }, "tag", "its reset value (stock tag)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "Res14_6", 14, 6, new uint[] { 256 }, "reserved", "reserved: dropped, reads its reset value", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "ADC12LPEN", 5, 5, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "Res4_2", 4, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "DMA2LPEN", 1, 1, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6)", 477),
            R("RCC_AHB1LPENR", 0x100, "§8.7.50", "DMA1LPEN", 0, 0, new uint[] { 1 }, "tag", "its reset value (stock tag)", 477),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "SRAM3LPEN", 31, 31, new uint[] { 1 }, "tag", "its reset value (stock tag)", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "SRAM2LPEN", 30, 30, new uint[] { 1 }, "tag", "its reset value (stock tag)", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "SRAM1LPEN", 29, 29, new uint[] { 1 }, "tag", "its reset value (stock tag)", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "Res28_10", 28, 10, Zero, "reserved", "reserved: dropped, reads its reset value", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "SDMMC2LPEN", 9, 9, new uint[] { 1 }, "tag", "its reset value (stock tag)", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "Res8_7", 8, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "RNGLPEN", 6, 6, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6)", 478),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "HASHLPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 479),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "CRYPTLPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 479),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "Res3_1", 3, 1, Zero, "reserved", "reserved: dropped, reads its reset value", 479),
            R("RCC_AHB2LPENR", 0x104, "§8.7.51", "DCMILPEN", 0, 0, new uint[] { 1 }, "tag", "its reset value (stock tag)", 479),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "SRAM4LPEN", 29, 29, new uint[] { 1 }, "tag", "its reset value (stock tag)", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "BKPRAMLPEN", 28, 28, new uint[] { 1 }, "tag", "its reset value (stock tag)", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "Res27_25", 27, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "ADC3LPEN", 24, 24, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "Res23_22", 23, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "BDMALPEN", 21, 21, new uint[] { 1 }, "tag", "its reset value (stock tag)", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "Res20", 20, 20, Zero, "reserved", "reserved: dropped, reads its reset value", 480),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "CRCLPEN", 19, 19, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "Res18_11", 18, 11, Zero, "reserved", "reserved: dropped, reads its reset value", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOKLPEN", 10, 10, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOJLPEN", 9, 9, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOILPEN", 8, 8, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOHLPEN", 7, 7, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOGLPEN", 6, 6, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOFLPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOELPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIODLPEN", 3, 3, new uint[] { 1 }, "tag", "its reset value (stock tag)", 481),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOCLPEN", 2, 2, new uint[] { 1 }, "tag", "its reset value (stock tag)", 482),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOBLPEN", 1, 1, new uint[] { 1 }, "tag", "its reset value (stock tag)", 482),
            R("RCC_AHB4LPENR", 0x108, "§8.7.52", "GPIOALPEN", 0, 0, new uint[] { 1 }, "tag", "its reset value (stock tag)", 482),
            R("RCC_APB3LPENR", 0x10C, "§8.7.53", "Res31_7", 31, 7, Zero, "reserved", "reserved: dropped, reads its reset value", 483),
            R("RCC_APB3LPENR", 0x10C, "§8.7.53", "WWDG1LPEN", 6, 6, new uint[] { 1 }, "tag", "its reset value (stock tag)", 483),
            R("RCC_APB3LPENR", 0x10C, "§8.7.53", "Res5_4", 5, 4, new uint[] { 1 }, "reserved", "reserved: dropped, reads its reset value", 483),
            R("RCC_APB3LPENR", 0x10C, "§8.7.53", "LTDCLPEN", 3, 3, new uint[] { 1 }, "tag", "its reset value (stock tag)", 483),
            R("RCC_APB3LPENR", 0x10C, "§8.7.53", "Res2_0", 2, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 483),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "UART8LPEN", 31, 31, new uint[] { 1 }, "tag", "its reset value (stock tag)", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "UART7LPEN", 30, 30, new uint[] { 1 }, "tag", "its reset value (stock tag)", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "DAC12LPEN", 29, 29, new uint[] { 1 }, "tag", "its reset value (stock tag)", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "Res28", 28, 28, Zero, "reserved", "reserved: dropped, reads its reset value", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "CECLPEN", 27, 27, new uint[] { 1 }, "tag", "its reset value (stock tag)", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "Res26_24", 26, 24, Zero, "reserved", "reserved: dropped, reads its reset value", 484),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "I2C3LPEN", 23, 23, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "I2C2LPEN", 22, 22, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "I2C1LPEN", 21, 21, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "UART5LPEN", 20, 20, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "UART4LPEN", 19, 19, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "USART3LPEN", 18, 18, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "USART2LPEN", 17, 17, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "SPDIFRXLPEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 485),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "SPI3LPEN", 15, 15, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "SPI2LPEN", 14, 14, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "Res13_10", 13, 10, new uint[] { 2 }, "reserved", "reserved: dropped, reads its reset value", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "LPTIM1LPEN", 9, 9, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM14LPEN", 8, 8, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM13LPEN", 7, 7, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM12LPEN", 6, 6, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM7LPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM6LPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM5LPEN", 3, 3, new uint[] { 1 }, "tag", "its reset value (stock tag)", 486),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM4LPEN", 2, 2, new uint[] { 1 }, "tag", "its reset value (stock tag)", 487),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM3LPEN", 1, 1, new uint[] { 1 }, "tag", "its reset value (stock tag)", 487),
            R("RCC_APB1LLPENR", 0x110, "§8.7.54", "TIM2LPEN", 0, 0, new uint[] { 1 }, "tag", "its reset value (stock tag)", 487),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "Res31_9", 31, 9, Zero, "reserved", "reserved: dropped, reads its reset value", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "FDCANLPEN", 8, 8, new uint[] { 1 }, "tag", "its reset value (stock tag)", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "Res7_6", 7, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "MDIOSLPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "OPAMPLPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "Res3", 3, 3, Zero, "reserved", "reserved: dropped, reads its reset value", 488),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "SWPLPEN", 2, 2, new uint[] { 1 }, "tag", "its reset value (stock tag)", 489),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "CRSLPEN", 1, 1, new uint[] { 1 }, "tag", "its reset value (stock tag)", 489),
            R("RCC_APB1HLPENR", 0x114, "§8.7.55", "Res0", 0, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 489),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res31_30", 31, 30, Zero, "reserved", "reserved: dropped, reads its reset value", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "HRTIMLPEN", 29, 29, new uint[] { 1 }, "tag", "its reset value (stock tag)", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "DFSDM1LPEN", 28, 28, new uint[] { 1 }, "tag", "its reset value (stock tag)", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res27_25", 27, 25, Zero, "reserved", "reserved: dropped, reads its reset value", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SAI3LPEN", 24, 24, new uint[] { 1 }, "tag", "its reset value (stock tag)", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SAI2LPEN", 23, 23, new uint[] { 1 }, "tag", "its reset value (stock tag)", 490),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SAI1LPEN", 22, 22, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res21", 21, 21, Zero, "reserved", "reserved: dropped, reads its reset value", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SPI5LPEN", 20, 20, new uint[] { 1 }, "tag", "its reset value (stock gap)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res19", 19, 19, Zero, "reserved", "reserved: dropped, reads its reset value", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "TIM17LPEN", 18, 18, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "TIM16LPEN", 17, 17, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "TIM15LPEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res15_14", 15, 14, Zero, "reserved", "reserved: dropped, reads its reset value", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SPI4LPEN", 13, 13, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "SPI1LPEN", 12, 12, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res11_6", 11, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "USART6LPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 491),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "USART1LPEN", 4, 4, new uint[] { 1 }, "tag", "its reset value (stock tag)", 492),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "Res3_2", 3, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 492),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "TIM8LPEN", 1, 1, new uint[] { 1 }, "tag", "its reset value (stock tag)", 492),
            R("RCC_APB2LPENR", 0x118, "§8.7.56", "TIM1LPEN", 0, 0, new uint[] { 0, 1 }, "acknowledged by name", "the twin does not model CSleep: the clock runs while the CPU sleeps, whatever this bit says (backlog) (TVI-WO-1300 A6; extended to this bit by TVI-WO-1300b, ruled 2026-11-25T03:03Z Q1)", 492),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res31_26", 31, 26, new uint[] { 1 }, "reserved", "reserved: dropped, reads its reset value", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res25_22", 25, 22, Zero, "reserved", "reserved: dropped, reads its reset value", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "SAI4LPEN", 21, 21, new uint[] { 1 }, "tag", "its reset value (stock tag)", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res20_17", 20, 17, Zero, "reserved", "reserved: dropped, reads its reset value", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "RTCAPBLPEN", 16, 16, new uint[] { 1 }, "tag", "its reset value (stock tag)", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "VREFLPEN", 15, 15, new uint[] { 1 }, "tag", "its reset value (stock tag)", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "COMP12LPEN", 14, 14, new uint[] { 1 }, "tag", "its reset value (stock tag)", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res13", 13, 13, Zero, "reserved", "reserved: dropped, reads its reset value", 493),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "LPTIM5LPEN", 12, 12, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "LPTIM4LPEN", 11, 11, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "LPTIM3LPEN", 10, 10, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "LPTIM2LPEN", 9, 9, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res8", 8, 8, Zero, "reserved", "reserved: dropped, reads its reset value", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "I2C4LPEN", 7, 7, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res6", 6, 6, Zero, "reserved", "reserved: dropped, reads its reset value", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "SPI6LPEN", 5, 5, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res4", 4, 4, Zero, "reserved", "reserved: dropped, reads its reset value", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "LPUART1LPEN", 3, 3, new uint[] { 1 }, "tag", "its reset value (stock tag)", 494),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res2", 2, 2, Zero, "reserved", "reserved: dropped, reads its reset value", 495),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "SYSCFGLPEN", 1, 1, new uint[] { 1 }, "tag", "its reset value (stock tag)", 495),
            R("RCC_APB4LPENR", 0x11C, "§8.7.57", "Res0", 0, 0, Zero, "reserved", "reserved: dropped, reads its reset value", 495),
        };
    }
}
