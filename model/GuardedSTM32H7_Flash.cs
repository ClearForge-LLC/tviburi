// Tviburi's guard over Renode's STM32H7_FlashController (TVI-WO-1304). Every citation is to ST RM0433 Rev 9, chapter 4.
// The measurement, every page, and the rulings (2026-11-24T15:50Z, Q1-Q7): docs/work-orders/TVI-WO-1304.measure.md.
//
// Insertion point (RUL-07): STM32H7_FlashController is not sealed (measured, TVI-WO-1304.measure.md §2.4), so this guard is
// a subclass that re-implements the three bus access interfaces. The stock platform declares `flashController`, so the
// board description cancels it and declares this guard at its address `as "flashController"` with the stock entry's
// banks and interrupt (model/stm32h7-flash-restate.repl; RUL-09 (b)). The consumer's untyped `flashController:` block,
// left on the cancelled entry, fails the load (E29); its option-byte word goes on `tviburiFlash` as `optionBytes`.
//
// What this guard models:
//   Wait states against the clock (Table 17 p160; p159-161): FLASH_ACR's LATENCY against the AXI clock (rcc_aclk =
//   sys_ck / D1CPRE / HPRE, p395; Table 17's F_ACLK) at the voltage scale in force, at every ACR write and after every
//   write the RCC guard passes that changes the clock (its one event, `Written`: ruled Q6). The clock is the RCC guard's
//   BusHz("rcc_hclk"), the overlay's one frequency rule (TVI-WO-1300c, ruled 2026-11-26T04:03Z Q9), which logs nothing; the
//   voltage scale from the pwr peripheral's PWR_D3CR.VOS (p310), read from the object at 0x58024818, which logs
//   nothing (TVI-WO-1304.measure.md §2.3). A D3CR that reads VOS 00 (the stock stub drops every write) is "VOS unknown": decided at
//   VOS1, the most permissive scale that can be known, so a mismatch only at VOS2/VOS3 passes, acknowledged in the
//   ledger (ruled Q1). VOS0's SYSCFG_PWRCR.ODEN cannot be read, so an AXI clock above 225 MHz is unmodeled.
//   The programming delay (p164, Table 17): WRHIGHFREQ below Table 17's value at a program or erase start. One-sided:
//   11 passes, read as at least as lax as 10, inferred, with no RM page (ruled Q2).
//   Bank 2 as silicon locks it: the keys (p182, p163), a CR write while locked (p182; pp. 202-206, 226-229), PSIZE
//   served as written (p228; Table 18 p164: no value is an error), SR read-only (p207, p230), OPTSR_CUR from the
//   declared image (ruled Q3).
// Not seen: a store into a flash bank (the stock model hooks the CPU only while PG is set), so a store with PG = 0 and a
// program over a programmed word are rows; a VOS write (a pwr write); the erase and program time (BSY, QW read 0).
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-FLASH-CONFIG  the platform gives this guard what it cannot model: a bus address other than 0x52002000, more than
//                     one registration, or an optionBytes outside 32 bits. unmodeled.
//   TVI-FLASH-READ    a read of an offset the guard does not serve: a write-only register (KEYR, CCR), the option-byte
//                     registers, the protection, boot, CRC and ECC registers, or no register. unmodeled.
//   TVI-FLASH-OPTSR   a read of OPTSR_CUR with no optionBytes declared: the twin does not invent the chip's option bytes
//                     (ruled Q3). unmodeled.
//   TVI-FLASH-REG     a write to a register the guard does not model: option-byte programming (OPTKEYR, OPTCR,
//                     OPTSR_PRG, OPTCCR), the protection, boot, CRC and ECC registers, or no register. unmodeled.
//   TVI-FLASH-KEY     a wrong key sequence: a wrong key, a key while FLASH_CRx is unlocked, a key after a wrong sequence,
//                     another register of the bank written between KEY1 and KEY2, or a key of less than 32 bits. Silicon
//                     locks FLASH_CRx until reset and raises a bus error (p182; p163). predicted-fault.
//   TVI-FLASH-LOCK    a FLASH_CRx write that changes a field while LOCKx = 1 (p182: "not allowed after reset"; every field
//                     "can be programmed only when LOCKx is cleared", pp. 202-206, 226-229: read as ignored, bench row
//                     F5), or one that sets LOCKx and changes another bit ("When LOCKx changes from 0 to 1, the other
//                     bits ... do not change", p206, p229). predicted-fault. The same value passes (RUL-18, amended).
//   TVI-FLASH-FIELD   the allow-list (`Fields`): a field given a value the model does not implement. unmodeled.
//   TVI-FLASH-WAIT    the AXI clock above what LATENCY covers at the voltage scale (Table 17 p160), at the ACR or RCC
//                     write that makes it so. predicted-fault; unmodeled when the clock cannot be computed or is above
//                     225 MHz.
//   TVI-FLASH-PROG    WRHIGHFREQ below Table 17's value for the clock at a program or erase start (PG set, START or FW
//                     with SER or BER), or at an ACR or RCC write while PG is set ("too tight", p164). predicted-fault;
//                     unmodeled as TVI-FLASH-WAIT.
// Enforcing and recorded mode (RUL-19) are model/Refusal.cs's contract. Recorded records after the first rank
// paths, they do not prove them: the guard's own state (the key sequence, a mismatch already recorded) follows the
// firmware's writes, the stock model's follows the stock model's rules.

using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Memory;
using Antmicro.Renode.Peripherals.MTD;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32H7_Flash : STM32H7_FlashController, IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IDisposable
    {
        // The stock entry's constructor arguments, passed through; `optionBytes` (the whole FLASH_OPTSR_CUR word the
        // consumer's part holds, -1 for none) and `mode` (RUL-19) are this guard's.
        public GuardedSTM32H7_Flash(IMachine machine, MappedMemory flash1, MappedMemory flash2, long optionBytes = -1,
            string mode = Records.Enforcing) : base(machine, flash1, flash2)
        {
            this.machine = machine;
            this.optionBytes = optionBytes;
            records = new Records(machine, this, nameof(GuardedSTM32H7_Flash), mode);
            if(machine is Machine m)
            {
                m.StateChanged += FindRccAtStart;
            }
        }

        // The RCC guard's one event (ruled Q6), subscribed when the machine first starts, before any guest instruction.
        private void FindRccAtStart(IMachine m, MachineStateChangedEventArgs args)
        {
            if(args.CurrentState == MachineStateChangedEventArgs.State.Started)
            {
                ((Machine)m).StateChanged -= FindRccAtStart;
                Rcc();
            }
        }

        public override void Reset()
        {
            base.Reset();
            psize[0] = psize[1] = 3;
            key[0] = key[1] = 0;
            keyLost[0] = keyLost[1] = false;
            waitWhy = progWhy = judgedClock = null;
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

        // The AHB configuration port takes 8-, 16- and 32-bit accesses (p151): a sub-word access is the word it is part of.
        private uint Read(long offset, int width)
        {
            var b = Canon(offset & ~3L);
            var shift = (int)(8 * (offset & 3));
            if(Unloaded(offset, 0, width))
            {
                return records.Recorded ? base.ReadDoubleWord(offset & ~3L) >> shift : 0;
            }
            if(!Readable(b))
            {
                records.Refuse("TVI-FLASH-READ", Unmodeled, Name(b), "read of " + Name(b),
                    "read of a FLASH offset the guard does not serve (" + What(b) + "): the twin cannot say what silicon returns",
                    "RM0433 Rev 9 §4.9.38 Table 28 p241-245", State(), offset, 0, width);
                return records.Recorded ? base.ReadDoubleWord(offset & ~3L) >> shift : 0;
            }
            if(b == OptsrCur && optionBytes < 0)
            {
                records.Refuse("TVI-FLASH-OPTSR", Unmodeled, "FLASH_OPTSR_CUR", "no optionBytes declared",
                    "read of FLASH_OPTSR_CUR with no option-byte image declared: silicon returns the option bytes the part holds, which the platform has not declared (the twin does not invent them; ruled 2026-11-24T15:50Z Q3)",
                    "RM0433 Rev 9 §4.9.8 p212-214; Table 21 p177", State(), offset, 0, width);
                return records.Recorded ? base.ReadDoubleWord(OptsrCur) >> shift : 0;
            }
            return Served(b) >> shift;
        }

        private void Write(long offset, uint value, int width)
        {
            var raw = offset & ~3L;
            var b = Canon(raw);
            var mask = width == 32 ? 0xFFFFFFFFu : width == 16 ? 0xFFFFu : 0xFFu;
            var shift = (int)(8 * (offset & 3));
            var word = width == 32 ? value : (Served(b) & ~(mask << shift)) | ((value & mask) << shift);
            if(Unloaded(offset, value, width))
            {
                if(!records.Recorded)
                {
                    return;
                }
            }
            else
            {
                // In the header's order. Enforcing: the first rule that fires refuses. Recorded mode (RUL-19) measures every
                // path: each rule is evaluated and records what it finds.
                foreach(var rule in new Func<long, long, uint, int, bool>[] { RefusedRegister, RefusedKey, RefusedLock, RefusedField, RefusedClock })
                {
                    if(rule(offset, b, word, width) && !records.Recorded)
                    {
                        return;
                    }
                }
            }
            Pass(raw, b, word);
        }

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, Name(Canon(offset & ~3L)), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c != null)
            {
                records.Refuse("TVI-FLASH-CONFIG", Unmodeled, Name(Canon(offset & ~3L)), "none",
                    "the platform gives this flash guard what it cannot model: " + c + "; every access refuses",
                    "RM0433 Rev 9 §2.3.2 Table 8 p133 (the FLASH registers at 0x52002000); §4.9.8 p212", "none", offset, value, width);
                return true;
            }
            return false;
        }

        // What a read returns: ACR, CRx with PSIZEx as written, SRx as the stock model holds them, OPTSR_CUR from the image.
        private uint Served(long b)
        {
            switch(b)
            {
            case Cr1:
            case Cr2:
                return (base.ReadDoubleWord(b) & ~PsizeMask) | (psize[Bank(b)] << 4);
            case OptsrCur:
                return (uint)optionBytes;
            }
            return Modeled(b) ? base.ReadDoubleWord(b) : 0;
        }

        private void Pass(long raw, long b, uint word)
        {
            switch(b)
            {
            case Sr1:
            case Sr2:
            case OptsrCur:
                return;   // read-only (p207, p230, p212): ignored, changes nothing; the stock model's EOP would be set (F5)
            case Cr1:
            case Cr2:
                psize[Bank(b)] = (word & PsizeMask) >> 4;
                break;
            }
            base.WriteDoubleWord(Modeled(b) ? b : raw, word);
        }

        // ---- rules ---------------------------------------------------------------------------------------------

        private bool RefusedRegister(long offset, long b, uint word, int width)
        {
            if(Modeled(b))
            {
                return false;
            }
            records.Refuse("TVI-FLASH-REG", Unmodeled, Name(b), "write to " + Name(b),
                "write to a FLASH register the guard does not model (" + What(b) + "): the twin cannot say what silicon does",
                Options.Contains(b) ? "RM0433 Rev 9 §4.4 p172-181 (option bytes); ruled 2026-11-24T15:50Z Q3" : "RM0433 Rev 9 §4.9.38 Table 28 p241-245",
                State(), offset, word, width);
            return true;
        }

        private bool RefusedKey(long offset, long b, uint word, int width)
        {
            var k = Bank(b);
            string why = null;
            if(b == Keyr1 || b == Keyr2)
            {
                if(width != 32)
                {
                    why = "a key written in less than 32 bits (" + width + ")";
                }
                else if(keyLost[k])
                {
                    why = "a key after a wrong sequence: FLASH_CR" + (k + 1) + " stays locked until reset (p182; whether this key also raises a bus error, RM0433 does not say)";
                }
                else if(!LockedCr(k))
                {
                    why = "a key written while FLASH_CR" + (k + 1) + " is unlocked (p163: it then remains locked until reset)";
                }
                else if(key[k] == 0 && word != Key1)
                {
                    why = "a first key other than KEY1 (0x45670123)";
                }
                else if(key[k] == 1 && word != Key2)
                {
                    why = "a second key other than KEY2 (0xCDEF89AB)";
                }
                if(why == null)
                {
                    key[k] = key[k] == 0 ? 1 : 0;
                    return false;
                }
            }
            else if(key[k] == 1 && !Aliases.Contains(b + 0x100))
            {
                why = "a write to " + Name(b) + ", another bank " + (k + 1) + " register, between KEY1 and KEY2";
            }
            if(why == null)
            {
                return false;
            }
            key[k] = 0;
            keyLost[k] = true;
            records.Refuse("TVI-FLASH-KEY", PredictedFault, Name(b), why,
                "a wrong FLASH_CR" + (k + 1) + " unlock sequence: " + why + "; silicon locks FLASH_CR" + (k + 1) + " until the next system reset and raises a bus error",
                "RM0433 Rev 9 §4.5.1 p182; §4.3.9 p163; §4.9." + (k == 0 ? "2 p201" : "24 p225"), State(), offset, word, width);
            return true;
        }

        private bool RefusedLock(long offset, long b, uint word, int width)
        {
            if(b != Cr1 && b != Cr2)
            {
                return false;
            }
            var locked = LockedCr(Bank(b));
            var locking = !locked && (word & LockBit) != 0;
            var changed = (word ^ Served(b)) & ~LockBit;
            if((!locked && !locking) || changed == 0)
            {
                return false;
            }
            var reg = Name(b);
            var n = Bank(b) + 1;
            records.Refuse("TVI-FLASH-LOCK", PredictedFault, reg, reg + " bits 0x" + changed.ToString("X8") + (locked ? " while LOCK" + n + " = 1" : " with LOCK" + n + " set"),
                locked ? reg + " written with a change while it is locked: every field \"can be programmed only when LOCK" + n
                    + " is cleared\", and FLASH_CR" + n + " writes \"are not allowed after reset\" until it is unlocked (p182), so the write is read as ignored (inferred from those pages; bench row F5), and the flash does not do what the firmware wrote"
                    : reg + " written setting LOCK" + n + " with other bits changed: \"When LOCK" + n + " changes from 0 to 1, the other bits of FLASH_CR" + n
                    + " register do not change\", so the flash does not do what the firmware wrote",
                "RM0433 Rev 9 §4.5.1 p182; §4.9." + (b == Cr1 ? "4 p202-206" : "25 p226-229"), State(), offset, word, width);
            return true;
        }

        private bool RefusedField(long offset, long b, uint word, int width)
        {
            foreach(var f in Fields.Where(r => r.Offset == b))
            {
                var v = f.Of(word);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    records.Refuse("TVI-FLASH-FIELD", Unmodeled, f.Register, f.Register + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        f.Register + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-1304.measure.md §5)",
                        State(), offset, word, width);
                    return true;
                }
            }
            return false;
        }

        // At a write to ACR (the latency it leaves) or a program or erase start on CRx: judged before the write passes.
        private bool RefusedClock(long offset, long b, uint word, int width)
        {
            if(b == Acr)
            {
                return Judge(word, offset, word, width);
            }
            if(b == Cr1 || b == Cr2)
            {
                var before = Served(b);
                var starts = ((word & PgBit) != 0 && (before & PgBit) == 0) || ((word & (StartBit | FwBit)) != 0 && (word & (SerBit | BerBit)) != 0);
                return starts && Program(base.ReadDoubleWord(Acr), offset, word, width, Name(b) + " = 0x" + word.ToString("X8") + " starts a program or erase", true);
            }
            return false;
        }

        // ---- the clock (Table 17 p160) -------------------------------------------------------------------------

        // After every write the RCC guard passes (its event `Written`, ruled Q6): the clock that write leaves, against ACR, when the
        // write changed it (its frequency or how it is derived, as Clock reports it from the RCC guard's BusHz; TVI-WO-1300c Q9).
        // ArduPilot's other RCC writes, about 26,000 enables and resets in 5 s (TVI-WO-1300 §4), are not judged.
        private void OnRccWritten(long offset, uint value)
        {
            if(records.LoadFailed || Config() != null)
            {
                return;   // a guard that did not load, or cannot model the platform, refuses its own accesses instead
            }
            var c = Clock();
            var now = c.Hz + " " + c.Note;
            if(now == judgedClock)
            {
                return;
            }
            judgedClock = now;
            Judge(base.ReadDoubleWord(Acr), RccBase + offset, value, 32);
        }

        // The wait states (TVI-FLASH-WAIT) and, while a bank is programming, the programming delay (TVI-FLASH-PROG), for the
        // ACR word `acr` against the clock now, at the write being judged (an ACR write, an RCC write at its absolute address). A mismatch
        // refuses at the write that makes it, and again only at a write that changes it (waitWhy, progWhy: its description,
        // clock and latency included); a later write that leaves it as it was refuses nothing.
        private bool Judge(uint acr, long offset, uint value, int width)
        {
            var fired = false;
            var clock = Clock();
            var latency = (int)(acr & 0xF);
            string why = null;
            var kind = PredictedFault;
            if(clock.Hz == null || clock.Hz > MaxAclk)
            {
                kind = Unmodeled;
                why = clock.Hz == null ? "the AXI clock cannot be computed (" + clock.Note + ")"
                    : "the AXI clock is " + clock.Hz + " Hz, above 225 MHz: Table 17 covers it only at VOS0 (VOS1 with SYSCFG_PWRCR.ODEN), which the twin cannot read; " + clock.Scale;
            }
            else
            {
                var need = Need(Latencies17, clock);
                if(latency < need)
                {
                    why = "LATENCY " + latency + " wait states with the AXI clock at " + clock.Hz + " Hz at " + clock.Scale + ": Table 17 needs " + need + " (" + clock.Note + ")";
                }
            }
            if(why != null && why != waitWhy)
            {
                fired = true;
                records.Refuse("TVI-FLASH-WAIT", kind, "FLASH_ACR", "LATENCY = " + latency, why + (offset >= RccBase ? ", at the RCC write to 0x" + offset.ToString("X8") : ", at the FLASH_ACR write")
                    + ": too few wait states for the clock fetch the wrong instructions or data (from memory; RM0433 says LATENCY \"must be correctly programmed\")",
                    "RM0433 Rev 9 §4.3.8 Table 17 p160; p159-161 (the order: raise LATENCY before the clock, lower the clock before LATENCY)", State(), offset, value, width);
            }
            waitWhy = why;
            var programming = (base.ReadDoubleWord(Cr1) & PgBit) != 0 || (base.ReadDoubleWord(Cr2) & PgBit) != 0;
            if(programming)
            {
                fired |= Program(acr, offset, value, width, "while PG is set", false);
            }
            else
            {
                progWhy = null;
            }
            return fired;
        }

        // TVI-FLASH-PROG: WRHIGHFREQ against Table 17 for the clock. `start`: a program or erase start, judged each time;
        // otherwise as Judge's (progWhy).
        private bool Program(uint acr, long offset, uint value, int width, string when, bool start)
        {
            var clock = Clock();
            var wrh = (int)(acr >> 4 & 3);
            string why = null;
            var kind = PredictedFault;
            if(clock.Hz == null || clock.Hz > MaxAclk)
            {
                kind = Unmodeled;
                why = clock.Hz == null ? "the AXI clock cannot be computed (" + clock.Note + ")" : "the AXI clock is " + clock.Hz + " Hz, above 225 MHz (VOS0 cannot be read)";
            }
            else
            {
                var need = Need(Delays17, clock);
                if(wrh < need)
                {
                    why = "WRHIGHFREQ " + Convert.ToString(wrh, 2).PadLeft(2, '0') + " with the AXI clock at " + clock.Hz + " Hz at " + clock.Scale
                        + ": Table 17's programming delay is " + Convert.ToString(need, 2).PadLeft(2, '0') + " (" + clock.Note + ")";
                }
            }
            var fire = why != null && (start || why != progWhy);
            if(!start)
            {
                progWhy = why;
            }
            if(!fire)
            {
                return false;
            }
            records.Refuse("TVI-FLASH-PROG", kind, "FLASH_ACR", "WRHIGHFREQ = " + Convert.ToString(wrh, 2).PadLeft(2, '0'),
                why + ", " + when + ": a programming delay too tight for the clock, \"the non-volatile memory will not operate correctly\"",
                "RM0433 Rev 9 §4.3.9 p164; §4.3.8 Table 17 p160; §4.9.1 p201", State(), offset, value, width);
            return true;
        }

        // The first row of `table` whose upper bound (MHz) at the scale covers the clock; VOS unknown is decided at VOS1.
        private static int Need(int[][] table, ClockNow c)
        {
            var row = table[c.Vos == 0 ? 1 : c.Vos];
            for(var i = 0; i < row.Length; i++)
            {
                if(c.Hz <= (ulong)row[i] * 1000000)
                {
                    return i;
                }
            }
            return row.Length;
        }

        // The AXI clock (rcc_aclk = sys_ck / D1CPRE / HPRE, p395) and the voltage scale, now.
        private ClockNow Clock()
        {
            var c = new ClockNow();
            c.Vos = Vos();
            c.Scale = c.Vos == 0 ? "VOS unknown on this platform (PWR_D3CR reads VOS 00), decided at VOS1" : "VOS" + c.Vos;
            var rcc = Rcc();
            if(rcc == null)
            {
                c.Note = "no RCC guard on this platform";
                return c;
            }
            // rcc_aclk is rcc_hclk (sys_ck / D1CPRE / HPRE, p395): the RCC guard's BusHz, the overlay's one frequency rule (TVI-WO-1300c,
            // ruled 2026-11-26T04:03Z Q9).
            var sys = rcc.BusHz("sys_ck", out var _);
            var hz = rcc.BusHz("rcc_hclk", out var why);
            var d1 = rcc.ReadDoubleWord(0x18);
            if(hz == null)
            {
                c.Note = "sys_ck's source gives no frequency the twin can compute (" + why + ")";
                return c;
            }
            c.Hz = (ulong)hz.Value;
            c.Note = "sys_ck " + sys + " Hz, D1CPRE = " + Convert.ToString(d1 >> 8 & 0xF, 2).PadLeft(4, '0') + ", HPRE = " + Convert.ToString(d1 & 0xF, 2).PadLeft(4, '0')
                + " (rcc_aclk " + c.Hz + " Hz); " + c.Scale;
            return c;
        }

        // PWR_D3CR.VOS (p310), as the scale's number: 11 Scale 1 is 1, 10 Scale 2 is 2, 01 Scale 3 is 3; 00 is 0, "unknown"
        // (the stock stub reads 00 whatever was written).
        private int Vos()
        {
            var at = machine.SystemBus.WhatIsAt(PwrD3cr);
            if(!(at?.Peripheral is IDoubleWordPeripheral pwr))
            {
                return 0;
            }
            var scale = (int)(pwr.ReadDoubleWord((long)(PwrD3cr - at.RegistrationPoint.Range.StartAddress)) >> 14 & 3);
            return scale == 0 ? 0 : 4 - scale;
        }

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

        private sealed class ClockNow
        {
            public ulong? Hz;
            public int Vos;   // the scale, VOS1-VOS3 as 1-3; 0 unknown
            public string Scale;
            public string Note;
        }

        // Table 17 (p160): per scale (index 1 VOS1, 2 VOS2, 3 VOS3), the top of each band in MHz. LATENCY n covers the n-th
        // band; WRHIGHFREQ 00, 01, 10 the three delay bands. Read from the rendered page (TVI-WO-1304.measure.md §2.2).
        private static readonly int[][] Latencies17 = { null, new[] { 70, 140, 210, 225 }, new[] { 55, 110, 165, 225 }, new[] { 45, 90, 135, 180, 225 } };
        private static readonly int[][] Delays17 = { null, new[] { 70, 185, 225 }, new[] { 55, 165, 225 }, new[] { 45, 135, 225 } };

        // ---- the registers -------------------------------------------------------------------------------------

        // A bank 2 alias of a common register (Table 20 p172; ACR p201, OPTSR_CUR p212) is that register. The registers with
        // an alias are the common ones: a write to one between KEY1 and KEY2 is not "a different register belonging to the
        // same bank" (p182).
        private static long Canon(long b)
        {
            return Aliases.Contains(b) ? b - 0x100 : b;
        }

        private static int Bank(long b)
        {
            return b >= 0x100 ? 1 : 0;
        }

        private bool LockedCr(int k)
        {
            return (base.ReadDoubleWord(k == 0 ? Cr1 : Cr2) & LockBit) != 0;
        }

        private static bool Modeled(long b)
        {
            return Fields.Any(f => f.Offset == b);
        }

        private static bool Readable(long b)
        {
            return b == Acr || b == Cr1 || b == Cr2 || b == Sr1 || b == Sr2 || b == OptsrCur;
        }

        private static string Name(long b)
        {
            return Names.TryGetValue(b, out var n) ? n : "FLASH+0x" + b.ToString("X");
        }

        private static string What(long b)
        {
            if(Options.Contains(b))
            {
                return "option-byte programming, not modeled: ruled 2026-11-24T15:50Z Q3";
            }
            if(b == Keyr1 || b == Keyr2 || b == Ccr1 || b == Ccr2)
            {
                return Name(b) + " is write-only";
            }
            return Names.ContainsKey(b) ? Name(b) + ": the protection areas, boot address, CRC and ECC registers are not modeled (TVI-WO-1304 (2))" : "no register at offset 0x" + b.ToString("X");
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
            if(optionBytes < -1 || optionBytes > 0xFFFFFFFFL)
            {
                return "optionBytes 0x" + optionBytes.ToString("X") + ", not a 32-bit FLASH_OPTSR_CUR word";
            }
            try
            {
                var points = machine.SystemBus.GetRegistrationPoints(this).ToArray();
                if(points.Length != 1)
                {
                    return points.Length + " bus registrations, not one";
                }
                var start = points[0].Range.StartAddress;
                return start == FlashBase ? "" : "bus address 0x" + start.ToString("X") + ", not the FLASH registers' 0x52002000";
            }
            catch(Exception e)
            {
                return "no bus registration (" + e.GetType().Name + ")";
            }
        }

        private string State()
        {
            return "ACR=0x" + base.ReadDoubleWord(Acr).ToString("X") + " CR1=0x" + Served(Cr1).ToString("X") + " CR2=0x" + Served(Cr2).ToString("X")
                + " SR2=0x" + base.ReadDoubleWord(Sr2).ToString("X") + " keys=" + key[0] + key[1] + (keyLost[0] || keyLost[1] ? " lost" : "")
                + " optionBytes=" + (optionBytes < 0 ? "none" : "0x" + optionBytes.ToString("X8"));
        }

        private readonly IMachine machine;
        private readonly Records records;
        private readonly long optionBytes;
        private readonly uint[] psize = { 3, 3 };
        private readonly int[] key = { 0, 0 };
        private readonly bool[] keyLost = { false, false };
        private GuardedSTM32H7_RCC rcc;
        private string waitWhy, progWhy, judgedClock;   // judgedClock: the AXI clock at the last RCC write judged (OnRccWritten)
        private string config;

        private const ulong FlashBase = 0x52002000;
        private const long RccBase = 0x58024400;
        private const ulong PwrD3cr = 0x58024818;
        private const ulong MaxAclk = 225000000;
        private const uint Key1 = 0x45670123;
        private const uint Key2 = 0xCDEF89AB;
        private const uint LockBit = 1u << 0;
        private const uint PgBit = 1u << 1;
        private const uint SerBit = 1u << 2;
        private const uint BerBit = 1u << 3;
        private const uint PsizeMask = 3u << 4;
        private const uint FwBit = 1u << 6;
        private const uint StartBit = 1u << 7;
        private const long Acr = 0x000;
        private const long Keyr1 = 0x004;
        private const long Cr1 = 0x00C;
        private const long Sr1 = 0x010;
        private const long Ccr1 = 0x014;
        private const long OptsrCur = 0x01C;
        private const long Keyr2 = 0x104;
        private const long Cr2 = 0x10C;
        private const long Sr2 = 0x110;
        private const long Ccr2 = 0x114;

        private static readonly long[] Aliases = { 0x100, 0x108, 0x118, 0x11C, 0x120, 0x124, 0x140, 0x144, 0x15C };
        private static readonly long[] Options = { 0x008, 0x018, 0x020, 0x024 };
        private static readonly Dictionary<long, string> Names = new Dictionary<long, string>
        {
            { 0x000, "FLASH_ACR" }, { 0x004, "FLASH_KEYR1" }, { 0x008, "FLASH_OPTKEYR" }, { 0x00C, "FLASH_CR1" }, { 0x010, "FLASH_SR1" },
            { 0x014, "FLASH_CCR1" }, { 0x018, "FLASH_OPTCR" }, { 0x01C, "FLASH_OPTSR_CUR" }, { 0x020, "FLASH_OPTSR_PRG" },
            { 0x024, "FLASH_OPTCCR" }, { 0x028, "FLASH_PRAR_CUR1" }, { 0x02C, "FLASH_PRAR_PRG1" }, { 0x030, "FLASH_SCAR_CUR1" },
            { 0x034, "FLASH_SCAR_PRG1" }, { 0x038, "FLASH_WPSN_CUR1R" }, { 0x03C, "FLASH_WPSN_PRG1R" }, { 0x040, "FLASH_BOOT_CURR" },
            { 0x044, "FLASH_BOOT_PRGR" }, { 0x050, "FLASH_CRCCR1" }, { 0x054, "FLASH_CRCSADD1R" }, { 0x058, "FLASH_CRCEADD1R" },
            { 0x05C, "FLASH_CRCDATAR" }, { 0x060, "FLASH_ECC_FA1R" }, { 0x104, "FLASH_KEYR2" }, { 0x10C, "FLASH_CR2" },
            { 0x110, "FLASH_SR2" }, { 0x114, "FLASH_CCR2" }, { 0x128, "FLASH_PRAR_CUR2" }, { 0x12C, "FLASH_PRAR_PRG2" },
            { 0x130, "FLASH_SCAR_CUR2" }, { 0x134, "FLASH_SCAR_PRG2" }, { 0x138, "FLASH_WPSN_CUR2R" }, { 0x13C, "FLASH_WPSN_PRG2R" },
            { 0x150, "FLASH_CRCCR2" }, { 0x154, "FLASH_CRCSADD2R" }, { 0x158, "FLASH_CRCEADD2R" }, { 0x160, "FLASH_ECC_FA2R" },
        };

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
        private static readonly uint[] Latencies = { 0, 1, 2, 3, 4, 5, 6, 7 };

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        // Generated by tests/1304/fields.py from tests/1304/data/rm0433-rev9-flash-fields.tsv (RM0433 Rev 9 §4.9) with the
        // classes ruled 2026-11-24T15:50Z; tests/1304/flash.sh `regen` checks it byte for byte.
        private static readonly Row[] Fields =
        {
            R("FLASH_ACR", 0x000, "§4.9.1", "Reserved", 31, 6, Zero, "reserved", "reserved: dropped, reads 0", 201),
            R("FLASH_ACR", 0x000, "§4.9.1", "WRHIGHFREQ", 5, 4, Any, "modeled by the guard", "kept and served, also at 0x100 (p201); TVI-FLASH-PROG: at a program or erase start, a value below Table 17's for the AXI clock at the voltage scale is a predicted-fault (\"too tight\", p164; p160); 11 read as at least as lax as 10 is inferred, no RM page: it is the reset value (p201), and every ArduPilot H7 programs flash at it, field evidence, not a page (ruled 2026-11-24T15:50Z Q2)", 201),
            R("FLASH_ACR", 0x000, "§4.9.1", "LATENCY", 3, 0, Latencies, "modeled by the guard", "kept and served, also at its bank 2 alias 0x100 (p201); TVI-FLASH-WAIT: wait states below Table 17's for the AXI clock at the voltage scale in force are a predicted-fault, at the write (ACR or RCC) that makes them so (p159-161); where PWR_D3CR reads VOS 00, decided at VOS1, the most permissive scale that can be known, so a mismatch only at VOS2/VOS3 passes, acknowledged (ruled 2026-11-24T15:50Z Q1): that is the stock stub, which drops every write, and also a firmware that writes VOS 00 itself on a pwr that stores it (\"Reserved (Scale 3 selected)\", p310), which the guard cannot tell from the stub without a name; a VOS write is not seen, so a mismatch it makes is judged at the next ACR or RCC clock write, or never; an AXI clock above 225 MHz is unmodeled (VOS0's ODEN cannot be read); 8-15 not modeled (p201 lists 0-7)", 201),
            R("FLASH_KEYR1", 0x004, "§4.9.2", "KEY1R", 31, 0, Any, "modeled by the guard", "the unlock keys (p182): KEY1 then KEY2 unlock FLASH_CR1; a wrong key, a key while unlocked, a third key, another bank 1 register written between the two, or a key of less than 32 bits is TVI-FLASH-KEY, a predicted-fault (locked until reset with a bus error, p182, p163); write-only: a read is TVI-FLASH-READ", 202),
            R("FLASH_CR1", 0x00C, "§4.9.4", "Res31_29", 31, 29, Zero, "reserved", "reserved: dropped, reads 0", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "CRCRDERRIE1", 28, 28, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "CRCENDIE1", 27, 27, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "DBECCERRIE1", 26, 26, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "SNECCERRIE1", 25, 25, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "RDSERRIE1", 24, 24, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "RDPERRIE1", 23, 23, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "OPERRIE1", 22, 22, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "INCERRIE1", 21, 21, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 203),
            R("FLASH_CR1", 0x00C, "§4.9.4", "Res20_20", 20, 20, Zero, "reserved", "reserved: dropped, reads 0", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "STRBERRIE1", 19, 19, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "PGSERRIE1", 18, 18, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "WRPERRIE1", 17, 17, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "EOPIE1", 16, 16, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "CRC_EN", 15, 15, Zero, "tag", "0: a stock tag; CRC computation is not modeled (TVI-WO-1304 (2), outside the allow-list)", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "Res14_14", 14, 14, Zero, "reserved", "reserved: dropped, reads 0", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "Res13_11", 13, 11, Zero, "reserved", "reserved: dropped, reads 0", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "SNB1", 10, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model: stored; used only by a bank 1 sector erase, which is not modeled (SER1 0 only)", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "START1", 7, 7, Zero, "inherited, unaudited (acknowledged by name)", "0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)", 204),
            R("FLASH_CR1", 0x00C, "§4.9.4", "FW1", 6, 6, Zero, "inherited, unaudited (acknowledged by name)", "0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)", 205),
            R("FLASH_CR1", 0x00C, "§4.9.4", "PSIZE1", 5, 4, Any, "modeled by the guard", "kept and served as written (p205; the stock model's tag reads 11, TVI-WO-1304.measure.md F7); no value is an error: \"There is no hardware limitation on programming parallelism\" (Table 18 p164; ruled 2026-11-24T15:50Z Q4, Q5)", 205),
            R("FLASH_CR1", 0x00C, "§4.9.4", "BER1", 3, 3, Zero, "inherited, unaudited (acknowledged by name)", "0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)", 205),
            R("FLASH_CR1", 0x00C, "§4.9.4", "SER1", 2, 2, Zero, "inherited, unaudited (acknowledged by name)", "0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)", 206),
            R("FLASH_CR1", 0x00C, "§4.9.4", "PG1", 1, 1, Zero, "inherited, unaudited (acknowledged by name)", "0 only: bank 1 programming and erase are not modeled (TVI-WO-1304 (2), outside the allow-list); a store into bank 1, which silicon rejects with PGSERR1 while PG1 = 0 (p164), is not seen by the guard (a row)", 206),
            R("FLASH_CR1", 0x00C, "§4.9.4", "LOCK1", 0, 0, Any, "modeled by the guard", "the stock model's lock (p206); a CR1 write that changes another field while locked is TVI-FLASH-LOCK, a predicted-fault (\"can be programmed only when LOCK1 is cleared\", p182: not allowed after reset); silicon ignores it", 206),
            R("FLASH_SR1", 0x010, "§4.9.5", "Res31_28", 31, 28, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "CRCEND1", 27, 27, Any, "read-only", "read-only (p207): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "DBECCERR1", 26, 26, Any, "read-only", "read-only (p207): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "SNECCERR1", 25, 25, Any, "read-only", "read-only (p207): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "RDSERR1", 24, 24, Any, "read-only", "read-only (p207): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "RDPERR1", 23, 23, Any, "read-only", "read-only (p207): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 207),
            R("FLASH_SR1", 0x010, "§4.9.5", "OPERR1", 22, 22, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "INCERR1", 21, 21, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "Res20_20", 20, 20, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "STRBERR1", 19, 19, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "PGSERR1", 18, 18, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "WRPERR1", 17, 17, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "EOP1", 16, 16, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set at the end of an erase, a program or an option byte change (p200), cleared by CLR_EOP", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "Res15_4", 15, 4, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "CRC_BUSY1", 3, 3, Any, "read-only", "read-only (p208): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: CRC computation is not modeled", 208),
            R("FLASH_SR1", 0x010, "§4.9.5", "QW1", 2, 2, Any, "read-only", "read-only (p209): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: erase and program time are not modeled (ruled 2026-11-24T15:50Z Q4)", 209),
            R("FLASH_SR1", 0x010, "§4.9.5", "WBNE1", 1, 1, Any, "read-only", "read-only (p209): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set while a flash word is partly written with PG set", 209),
            R("FLASH_SR1", 0x010, "§4.9.5", "BSY1", 0, 0, Any, "read-only", "read-only (p209): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: erase and program time are not modeled (ruled 2026-11-24T15:50Z Q4)", 209),
            R("FLASH_CCR1", 0x014, "§4.9.6", "Res31_28", 31, 28, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_CRCEND1", 27, 27, Any, "modeled by the guard", "CRCEND1 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_DBECCERR1", 26, 26, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears DBECCERR1 (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_SNECCERR1", 25, 25, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears SNECCERR1 (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_RDSERR1", 24, 24, Any, "modeled by the guard", "RDSERR1 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_RDPERR1", 23, 23, Any, "modeled by the guard", "RDPERR1 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_OPERR1", 22, 22, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears OPERR1 (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_INCERR1", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears INCERR1 (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "Res20_20", 20, 20, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_STRBERR1", 19, 19, Any, "modeled by the guard", "STRBERR1 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_PGSERR1", 18, 18, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears PGSERR1 (p210)", 210),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_WRPERR1", 17, 17, Any, "modeled by the guard", "WRPERR1 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p211)", 211),
            R("FLASH_CCR1", 0x014, "§4.9.6", "CLR_EOP1", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears EOP1 (p211)", 211),
            R("FLASH_CCR1", 0x014, "§4.9.6", "Res15_0", 15, 0, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 211),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "SWAP_BANK_OPT", 31, 31, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "OPTCHANGEERR", 30, 30, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "IO_HSLV", 29, 29, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res28_26", 28, 26, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res25_24", 25, 24, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res23_22", 23, 22, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "SECURITY", 21, 21, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "ST_RAM_SIZE", 20, 19, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "IWDG_FZ_SDBY", 18, 18, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "IWDG_FZ_STOP", 17, 17, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res16_16", 16, 16, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 213),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "RDP", 15, 8, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "NRST_STDY_D1", 7, 7, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "NRST_STOP_D1", 6, 6, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res5_5", 5, 5, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "IWDG1_SW", 4, 4, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "BOR_LEV", 3, 2, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "Res1_1", 1, 1, Any, "read-only", "reserved: served as the declared optionBytes image has it (the board's 0x1BC6AAF0 and the consumer's 0x0406AAFC set reserved bits), never invented; a write is ignored (read-only, p212-214)", 214),
            R("FLASH_OPTSR_CUR", 0x01C, "§4.9.8", "OPT_BUSY", 0, 0, Any, "read-only", "served from the declared optionBytes image, also at 0x11C, never the stock model's reset value (ruled 2026-11-24T15:50Z Q3); an undeclared read is TVI-FLASH-OPTSR; read-only: a write is ignored (p212-214)", 214),
            R("FLASH_KEYR2", 0x104, "§4.9.24", "KEY2R", 31, 0, Any, "modeled by the guard", "the unlock keys (p182): KEY1 then KEY2 unlock FLASH_CR2; a wrong key, a key while unlocked, a third key, another bank 2 register written between the two, or a key of less than 32 bits is TVI-FLASH-KEY, a predicted-fault (locked until reset with a bus error, p182, p163); write-only: a read is TVI-FLASH-READ", 225),
            R("FLASH_CR2", 0x10C, "§4.9.25", "Res31_29", 31, 29, Zero, "reserved", "reserved: dropped, reads 0", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "CRCRDERRIE2", 28, 28, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "CRCENDIE2", 27, 27, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "DBECCERRIE2", 26, 26, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "SNECCERRIE2", 25, 25, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "RDSERRIE2", 24, 24, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 226),
            R("FLASH_CR2", 0x10C, "§4.9.25", "RDPERRIE2", 23, 23, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "OPERRIE2", 22, 22, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "INCERRIE2", 21, 21, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "Res20_20", 20, 20, Zero, "reserved", "reserved: dropped, reads 0", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "STRBERRIE2", 19, 19, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "PGSERRIE2", 18, 18, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "WRPERRIE2", 17, 17, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "EOPIE2", 16, 16, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the flash interrupts are not audited", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "CRC_EN", 15, 15, Zero, "tag", "0: a stock tag; CRC computation is not modeled (TVI-WO-1304 (2), outside the allow-list)", 227),
            R("FLASH_CR2", 0x10C, "§4.9.25", "SPSS2", 14, 14, Zero, "undefined in the stock model", "0 only: the special sector selection is not modeled (the stock model has no bit 14)", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "Res13_11", 13, 11, Zero, "reserved", "reserved: dropped, reads 0", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "SNB2", 10, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model: the sector a sector erase sets to 0xFF (128 KiB)", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "START2", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model: the erase runs at once and sets EOP2 (TVI-WO-1304.measure.md F2, F8: no erase time, so BSY2 and QW2 are never 1; a row, ruled 2026-11-24T15:50Z Q4); TVI-FLASH-PROG at it", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "FW2", 6, 6, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the force-write is not audited", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "PSIZE2", 5, 4, Any, "modeled by the guard", "kept and served as written (p228; the stock model's tag reads 11, TVI-WO-1304.measure.md F7); no value is an error: \"There is no hardware limitation on programming parallelism\" (Table 18 p164; ruled 2026-11-24T15:50Z Q4, Q5)", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "BER2", 3, 3, Zero, "inherited, unaudited (acknowledged by name)", "0 only: a bank erase is not audited", 228),
            R("FLASH_CR2", 0x10C, "§4.9.25", "SER2", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "the stock model: a sector erase at START2", 229),
            R("FLASH_CR2", 0x10C, "§4.9.25", "PG2", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "the stock model: its program-write hook (EOP2 after a 32-byte flash word, TVI-WO-1304.measure.md F3); TVI-FLASH-PROG at its set; set with SER2 still set passes, RM0433 stating nothing (ArduPilot does it; ruled 2026-11-24T15:50Z Q4); a store into bank 2 with PG2 = 0, which silicon rejects with PGSERR2 (p164), is not seen by the guard (a row, ruled 2026-11-24T15:50Z Q4)", 229),
            R("FLASH_CR2", 0x10C, "§4.9.25", "LOCK2", 0, 0, Any, "modeled by the guard", "the stock model's lock (p229); a CR2 write that changes another field while locked is TVI-FLASH-LOCK, a predicted-fault (\"can be programmed only when LOCK2 is cleared\", p182: not allowed after reset); silicon ignores it", 229),
            R("FLASH_SR2", 0x110, "§4.9.26", "Res31_28", 31, 28, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "CRCEND2", 27, 27, Any, "read-only", "read-only (p230): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "DBECCERR2", 26, 26, Any, "read-only", "read-only (p230): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "SNECCERR2", 25, 25, Any, "read-only", "read-only (p230): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "RDSERR2", 24, 24, Any, "read-only", "read-only (p230): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "RDPERR2", 23, 23, Any, "read-only", "read-only (p230): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 230),
            R("FLASH_SR2", 0x110, "§4.9.26", "OPERR2", 22, 22, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "INCERR2", 21, 21, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "Res20_20", 20, 20, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "STRBERR2", 19, 19, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "PGSERR2", 18, 18, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "WRPERR2", 17, 17, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set only by the stock model's own checks", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "EOP2", 16, 16, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set at the end of an erase, a program or an option byte change (p200), cleared by CLR_EOP", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "Res15_4", 15, 4, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "CRC_BUSY2", 3, 3, Any, "read-only", "read-only (p231): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: CRC computation is not modeled", 231),
            R("FLASH_SR2", 0x110, "§4.9.26", "QW2", 2, 2, Any, "read-only", "read-only (p232): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: erase and program time are not modeled (ruled 2026-11-24T15:50Z Q4)", 232),
            R("FLASH_SR2", 0x110, "§4.9.26", "WBNE2", 1, 1, Any, "read-only", "read-only (p232): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads the stock model's: set while a flash word is partly written with PG set", 232),
            R("FLASH_SR2", 0x110, "§4.9.26", "BSY2", 0, 0, Any, "read-only", "read-only (p232): a write is ignored and changes nothing (no record, RUL-18 as amended; the stock model's EOP is writable, TVI-WO-1304.measure.md F5); reads 0: erase and program time are not modeled (ruled 2026-11-24T15:50Z Q4)", 232),
            R("FLASH_CCR2", 0x114, "§4.9.27", "Res31_28", 31, 28, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_CRCEND2", 27, 27, Any, "modeled by the guard", "CRCEND2 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_DBECCERR2", 26, 26, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears DBECCERR2 (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_SNECCERR2", 25, 25, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears SNECCERR2 (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_RDSERR2", 24, 24, Any, "modeled by the guard", "RDSERR2 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_RDPERR2", 23, 23, Any, "modeled by the guard", "RDPERR2 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_OPERR2", 22, 22, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears OPERR2 (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_INCERR2", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears INCERR2 (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "Res20_20", 20, 20, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_STRBERR2", 19, 19, Any, "modeled by the guard", "STRBERR2 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_PGSERR2", 18, 18, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears PGSERR2 (p233)", 233),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_WRPERR2", 17, 17, Any, "modeled by the guard", "WRPERR2 is a stock tag reading 0, never set in the twin, so its clear changes nothing (p234)", 234),
            R("FLASH_CCR2", 0x114, "§4.9.27", "CLR_EOP2", 16, 16, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears EOP2 (p234)", 234),
            R("FLASH_CCR2", 0x114, "§4.9.27", "Res15_0", 15, 0, Any, "inherited, unaudited (acknowledged by name)", "ArduPilot writes 1 here (CCRx = 0xFFFFFFFF, TVI-WO-1304.measure.md §2.1); RM0433 says \"must be kept at reset value\"; ignored", 234),
        };
    }
}
