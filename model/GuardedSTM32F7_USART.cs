// Tviburi's guard over Renode's STM32F7_USART for the STM32H753's USART1/2/3/6 and UART4/5/7/8 (TVI-WO-1305): a serial port
// receives, idles and takes its configuration only as silicon does, so the drone's command link is judged. The measurement,
// every page, and the rulings (2026-11-24T18:55Z, Q1-Q9): docs/work-orders/TVI-WO-1305.measure.md (RM0433 Rev 9 §48).
//
// Insertion point (RUL-07 as amended): STM32F7_USART is sealed (measure.md §2.3), so this guard is a wrapper that owns an
// unregistered stock instance and forwards every access it passes, its IUART surface (WriteChar, CharReceived: the
// consumer's injection and its file backend bind to the registered peripheral), and every reset. It drives its own IRQ (the
// stock interrupt ORed with its IDLE) and its own ReceiveDmaRequest; the stock instance's request line is left unconnected.
// The stock platform declares each port, so the board description cancels it and declares this guard at its address
// `as "usartN"` on tviburiRcc, with the stock entry's frequency and interrupt line (model/stm32h7-usart-restate.repl);
// lpuart1 stays stock. The nested `Registers` enum keeps the access log's register names (Renode's RegisterMapper reads the
// registered peripheral's type; measure.md §2.3).
//
// The bus clock (WO (2)): with USARTxEN = 0 the RCC guard locks this peripheral on the bus, so an access is dropped there (a
// read returns 0) with no record, which is silicon's ignored write (p373). It is covered, not refused (measure.md §2.5).
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-USART-CONFIG  the platform gives this guard what it cannot model: more than one bus registration, or
//                     lowPowerMode (LPUART1 is outside TVI-WO-1305). unmodeled.
//   TVI-USART-WIDTH   a byte or half-word access other than to RDR or TDR, or a misaligned word: "The peripheral registers
//                     have to be accessed by words (32 bits)" (p2067). unmodeled. RDR and TDR pass by byte and half-word:
//                     ArduPilot's DMA moves them by bytes, field evidence, not a page (ruled Q7).
//   TVI-USART-READ    a read of an offset with no register (0x30-0x3FC). unmodeled.
//   (ISR, RDR)        read-only: a write is ignored and changes nothing, with no record (RUL-18 as amended).
//   TVI-USART-UE      a write, while UE = 1, that changes a field RM0433 says "can only be written when the USART is disabled
//                     (UE = 0)" (CR1 pp. 2067-2069; CR2 pp. 2074-2077, ADD with RE = 1, ABRMOD with ABREN = 1; CR3
//                     pp. 2079-2081, SCARCNT to a non-zero value; BRR p2082; GTPR p2083; PRESC p2099). unmodeled: the manual
//                     does not say what silicon does with such a write (ruled Q2). The same value passes (RUL-18). A CR1
//                     write that clears UE is judged as with UE = 0: disabling is allowed (ruled Q2). A change confined to
//                     BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port, acknowledged
//                     by name with no record, and taken as the stock model takes it (ruled Q2 (c); bench row U1).
//   TVI-USART-FIELD   the allow-list (`Fields`): a field given a value the model does not implement, or a write to an offset
//                     with no register; and CR1.M0 = 1 with PCE = 0 (9 data bits). unmodeled.
//   TVI-USART-KERNEL  a start (UE with TE or RE, from a state that had none) with no running kernel clock, by the RCC guard's
//                     existing query (QueryKernelClock, TVI-WO-1300): "The usart_ker_ck is the USART clock source" (p2019),
//                     and "no clock will be provided to the peripheral" (p371). predicted-fault; unmodeled where the query
//                     has no rule (usart6, uart4/5/7/8, whose enables the RCC guard already refuses) or no RCC guard is found.
//   TVI-USART-BRR     "USARTDIV must be greater than or equal to 16" (§48.5.7 p2035; OVER8 = 0 only, so USARTDIV = BRR, the same page):
//                     a start with BRR < 16, BRR rewritten below 16 on a running port, or a byte moving while a held start
//                     waits. unmodeled. ArduPilot's measured start is held and acknowledged by name (RefusedBrr; the BRR
//                     field row; bench row U2).
//   TVI-USART-IDLE    IDLE's frame time needs the kernel clock's frequency (ruled at TVI-WO-1305's merge, Q4): a receiver
//                     start (UE and RE), or a byte needing the frame time, with a kernel clock this guard has no rule for.
//                     unmodeled; never a default (invariant 1).
//   TVI-USART-OVERRUN a byte arriving with FIFOEN = 0 while the last one waits unread: silicon loses it (§48.5.6 p2030), and
//                     the twin's queue keeps every byte. unmodeled (ruled at TVI-WO-1305's merge, Q3). FIFO mode is unchanged.
//   A byte's arrival is not a bus access: its records carry the CPU's PC if one is running, else "none", and the exact
//   virtual time of the injection.
// Enforcing: the first rule that fires refuses the access. Recorded mode (RUL-19): every rule is evaluated and records what
// it finds, and the access goes on as if none had fired (model/Refusal.cs).
//
// Modeled, cited:
//   IDLE (p2090, p2021): one idle frame (a start bit, the data bits by M, the stop bits by STOP, at the kernel clock over BRR,
//     p2034; `KernelHz`) after a received byte with no newer one, and with UE and RE never cleared in between, the guard sets ISR.IDLE; with IDLEIE it raises the port's
//     interrupt. ICR.IDLECF clears it. It is set only after a received byte, never at a start with nothing received (ruled
//     Q5: p2040's sentence is in the mute-mode section).
//   The receive DMA request (p2048, "after each RXNE (RXFNE in case FIFO mode is enabled) event"): asserted while DMAR = 1,
//     UE = 1 and a received byte waits; raised again after each RDR read that leaves a byte, and once per frame while a byte
//     waits (the stock model raises it only when its buffer leaves empty, so a byte that arrived before the stream was armed
//     never moved, measure.md U11-U12). Reset at UE = 0 (p2070).
//   UE cleared (p2070): "all the USART_ISR status flags are reset", and the DMA requests: the guard flushes the receive
//     queue and clears IDLE, so the request drops; TC is left to the stock model, which sets it as TE clears (its reset
//     value, p2085, p2091).
//   No clock (p470, p2019, p371): a byte arriving while the RCC guard's query says the port's clock does not run is not
//     received (no RXFNE, IDLE or request).
//
// Reads: CR1, CR2, CR3, GTPR and PRESC from the guard's copies (RUL-17), so a read-modify-write keeps FIFOEN, IDLEIE, M0,
// RXINV, LBDIE and EIE as silicon does (measure.md §2.1: ChibiOS reads CR1 back as 0x12D where silicon holds 0x2000013D); ISR
// from the stock model with the guard's IDLE; the rest from the stock model.

using System;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.UART;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32F7_USART : IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IUART, IDisposable
    {
        // `frequency`, `lowPowerMode`: the stock entry's (the restatement copies them). `mode` (RUL-19) is this guard's.
        public GuardedSTM32F7_USART(IMachine machine, uint frequency, bool lowPowerMode = false, string mode = Records.Enforcing)
        {
            host = machine;
            this.lowPowerMode = lowPowerMode;
            inner = new STM32F7_USART(machine, frequency, lowPowerMode);
            records = new Records(machine, this, nameof(GuardedSTM32F7_USART), mode);
            inner.IRQ.AddStateChangedHook(v => { innerIrq = v; UpdateIrq(); });
            machine.PeripheralsChanged += FindRcc;
            if(machine is Machine m)
            {
                // PLL2DIVR returns to its reset value (p406, narrow) with the RCC, at a machine reset; never at this port's own
                // reset, which the RCC's USARTxRST runs (ArduPilot resets usart1 after setting PLL2 up).
                m.MachineReset += _ => pll2Wide = false;
            }
        }

        // The RCC guard's existing event `Written` (TVI-WO-1304), subscribed when the RCC guard appears at platform load, before
        // any monitor or guest write. The stock model narrows PLL2DIVR (DIVN2 to 8 bits, DIVQ2 to 6; p407 has 9 and 7), so its
        // read-back is PLL2's value only while no wider field was ever written: once one was, a read-modify-write writes the
        // narrowed bits back, and no copy here could say what silicon holds. `pll2Wide` marks it, until the machine resets.
        private void FindRcc(IMachine m, PeripheralsChangedEventArgs args)
        {
            if(rccSeen == null && args.Peripheral is GuardedSTM32H7_RCC r)
            {
                rccSeen = r;
                r.Written += (offset, value) => pll2Wide |= offset == Pll2divrOffset && (value & 0x00400100) != 0;
            }
        }

        public long Size => 0x400;

        // The port's interrupt: the stock model's, ORed with the guard's IDLE (`IRQ -> exti@N` binds here).
        public GPIO IRQ { get; } = new GPIO();

        // The receive DMA request (`ReceiveDmaRequest -> dmamux1@41` binds here, where a platform wires it).
        public GPIO ReceiveDmaRequest { get; } = new GPIO();

        public void Reset()
        {
            lock(sync)
            {
                inner.Reset();
                cr1 = cr2 = cr3 = gtpr = presc = 0;
                idle = brrHeld = false;
                idleGen++;
                ReceiveDmaRequest.Unset();
                UpdateIrq();
            }
        }

        public void Dispose()
        {
            records.End();
        }

        // ---- IUART ---------------------------------------------------------------------------------------------

        public event Action<byte> CharReceived
        {
            add { inner.CharReceived += value; }
            remove { inner.CharReceived -= value; }
        }

        // A byte arriving on the line (the consumer's injection, an analyzer). The stock model drops it unless UE and RE are
        // set (U5); a byte it takes starts the idle-frame timer and asserts the receive request.
        public void WriteChar(byte value)
        {
            lock(sync)
            {
                if(!Clocked())
                {
                    return;   // no clock reaches the port (p470 USARTxEN, p2019 usart_ker_ck, p371): nothing is received
                }
                var taken = (cr1 & UeRe) == UeRe;
                TimeInterval? frame = null;
                if(taken)
                {
                    var refused = RefusedHeld(RdrOffset, value, 8, "a byte received");
                    if(!refused || records.Recorded)
                    {
                        refused |= RefusedOverrun(value);
                    }
                    frame = !refused || records.Recorded ? FrameTime("a byte received", value, 8) : null;
                    if((refused || frame == null) && !records.Recorded)
                    {
                        return;
                    }
                }
                inner.WriteChar(value);
                if(taken)
                {
                    ScheduleIdle(frame);
                    Request();
                }
            }
        }

        public uint BaudRate => inner.BaudRate;
        public Bits StopBits => inner.StopBits;
        public Parity ParityBit => inner.ParityBit;

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
                if(Unloaded(offset, 0, width) || Narrow(offset, 0, width, "read"))
                {
                    return 0;
                }
                if(!Defined(offset))
                {
                    records.Refuse("TVI-USART-READ", Unmodeled, "none", "read of offset 0x" + offset.ToString("X"),
                        "read of a USART offset where RM0433 defines no register: the twin cannot say what silicon returns",
                        "RM0433 Rev 9 §48.8.15 Table 411 p2100", State(), offset, 0, width);
                    return records.Recorded ? inner.ReadDoubleWord(offset) : 0;
                }
                if(offset == RdrOffset && RefusedHeld(offset, 0, width, "an RDR read") && !records.Recorded)
                {
                    return 0;
                }
                return Served(offset);
            }
        }

        private void Write(long offset, uint value, int width)
        {
            lock(sync)
            {
                if(Unloaded(offset, value, width) || Narrow(offset, value, width, "write"))
                {
                    return;
                }
                if(!Defined(offset))
                {
                    records.Refuse("TVI-USART-FIELD", Unmodeled, "none", "write to offset 0x" + offset.ToString("X"),
                        "write to a USART offset where RM0433 defines no register: the twin cannot say what silicon does",
                        "RM0433 Rev 9 §48.8.15 Table 411 p2100", State(), offset, value, width);
                    return;
                }
                if(offset == IsrOffset || offset == RdrOffset)
                {
                    return;   // read-only: silicon ignores the write, and it changes nothing (RUL-18 as amended)
                }
                var refused = RefusedUe(offset, value, width);
                if(!refused || records.Recorded)
                {
                    refused |= RefusedField(offset, value, width);
                }
                if(offset == Cr1Offset && !Running(cr1) && Running(value) && (!refused || records.Recorded))
                {
                    refused |= RefusedKernel(value);
                }
                if(!refused || records.Recorded)
                {
                    refused |= RefusedBrr(offset, value, width);
                }
                if(offset == Cr1Offset && (cr1 & UeRe) != UeRe && (value & UeRe) == UeRe && (!refused || records.Recorded))
                {
                    refused |= KernelHz(out var why) == null && RefuseIdle("the receiver started (CR1.UE and RE)", why, Cr1Offset, value, 32);
                }
                if(refused && !records.Recorded)
                {
                    return;
                }
                Pass(offset, value);
            }
        }

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, RegisterAt(offset), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c != null)
            {
                records.Refuse("TVI-USART-CONFIG", Unmodeled, RegisterAt(offset), "none",
                    "the platform gives this USART guard what it cannot model: " + c + "; every access refuses",
                    "TVI-WO-1305.measure.md §2.3 (the form); ruled 2026-11-24T18:55Z Q1", "none", offset, value, width);
                return true;
            }
            return false;
        }

        // Words only, but for RDR and TDR, which pass by byte and half-word at their own offset (ruled Q7).
        private bool Narrow(long offset, ulong value, int width, string what)
        {
            if(width == 32 ? (offset & 3) == 0 : offset == RdrOffset || offset == TdrOffset)
            {
                return false;
            }
            records.Refuse("TVI-USART-WIDTH", Unmodeled, RegisterAt(offset & ~3L), width + "-bit " + what,
                "a " + width + "-bit USART " + what + " at offset 0x" + offset.ToString("X")
                    + ": the USART registers are accessed by words, and the twin cannot say what silicon does with this one",
                "RM0433 Rev 9 §48.8 p2067; ruled 2026-11-24T18:55Z Q7 (RDR and TDR by bytes)", State(), offset, value, width);
            return true;
        }

        private uint Served(long offset)
        {
            switch(offset)
            {
            case Cr1Offset: return cr1;
            case Cr2Offset: return cr2;
            case Cr3Offset: return cr3;
            case GtprOffset: return gtpr;
            case PrescOffset: return presc;
            case IsrOffset: return (inner.ReadDoubleWord(IsrOffset) & ~IdleBit) | (idle ? IdleBit : 0);
            case RdrOffset:
                var v = inner.ReadDoubleWord(RdrOffset);
                Request();
                return v;
            default: return inner.ReadDoubleWord(offset);
            }
        }

        private void Pass(long offset, uint word)
        {
            switch(offset)
            {
            case Cr1Offset:
                var before = cr1;
                cr1 = word;
                inner.WriteDoubleWord(offset, word);
                if((before & UeBit) != 0 && (word & UeBit) == 0)
                {
                    // p2070: UE = 0 resets every ISR flag and the DMA requests. RXFNE and IDLE to 0; TC and TXFNF are left to the
                    // stock model, whose TE clear sets TC: their reset value is 1 (ISR 0x0X8000C0, p2085, p2091).
                    inner.WriteDoubleWord(RqrOffset, RxfrqBit);
                    idle = false;
                }
                if((before & (UeBit | ReBit)) == (UeBit | ReBit) && (word & (UeBit | ReBit)) != (UeBit | ReBit))
                {
                    idleGen++;   // the receiver stops: a pending idle frame is never detected (p2070 RE, UE)
                }
                UpdateIrq();
                Request();
                return;
            case Cr2Offset:
                cr2 = word;
                inner.WriteDoubleWord(offset, word);
                return;
            case Cr3Offset:
                cr3 = word;
                inner.WriteDoubleWord(offset, word);
                Request();
                return;
            case GtprOffset:
                gtpr = word;   // the stock model has no register 0x10 (U1): kept here, never forwarded
                return;
            case PrescOffset:
                presc = word;  // nor 0x2C
                return;
            case BrrOffset:
                inner.WriteDoubleWord(offset, word);
                brrHeld &= (word & 0xFFFF) < 16;   // the held start acknowledged: BRR >= 16 before any byte moved
                return;
            case IcrOffset:
                inner.WriteDoubleWord(offset, word);
                if((word & IdleBit) != 0)
                {
                    idle = false;
                    UpdateIrq();
                }
                return;
            case RqrOffset:
                inner.WriteDoubleWord(offset, word);
                Request();
                return;
            default:
                inner.WriteDoubleWord(offset, word);
                return;
            }
        }

        // ---- the rules -----------------------------------------------------------------------------------------

        // TVI-USART-UE (ruled Q2): a protected field changed while UE = 1; the ChibiOS re-init pattern acknowledged.
        private bool RefusedUe(long offset, uint word, int width)
        {
            if((cr1 & UeBit) == 0 || (offset == Cr1Offset && (word & UeBit) == 0))
            {
                return false;
            }
            var mask = Protected(offset);
            if(mask == 0)
            {
                return false;
            }
            var changed = (Served(offset) ^ word) & mask;
            changed &= ~Acknowledged(offset);   // the re-init pattern's own fields are never the offence (ruled Q2 (c))
            if(changed == 0)
            {
                return false;
            }
            var rows = Fields.Where(f => f.Offset == offset && (changed & f.Mask) != 0).ToArray();
            records.Refuse("TVI-USART-UE", Unmodeled, RegisterAt(offset), string.Join(", ", rows.Select(f => f.Name + " (" + f.Bits + ")")) + " changed with UE = 1",
                RegisterAt(offset) + " written with UE = 1, changing " + string.Join(", ", rows.Select(f => f.Name + " " + f.Format(f.Of(Served(offset))) + " -> " + f.Format(f.Of(word))))
                    + ": RM0433 says it \"can only be written when the USART is disabled (UE = 0)\" and does not say what silicon does with such a write, so the twin cannot say",
                "RM0433 Rev 9 " + string.Join(", ", rows.Select(f => f.Section + " p" + f.Page).Distinct()) + "; ruled 2026-11-24T18:55Z Q2",
                State(), offset, word, width);
            return true;
        }

        // TVI-USART-FIELD: the first field, in the table's order, whose written value is not allowed; then CR1's 9-bit word.
        private bool RefusedField(long offset, uint word, int width)
        {
            foreach(var f in Fields)
            {
                if(f.Offset != offset || f.Allowed == null || f.Allowed.Contains(f.Of(word)))
                {
                    continue;
                }
                records.Refuse("TVI-USART-FIELD", Unmodeled, f.Register, f.Name + " (" + f.Bits + ") = " + f.Format(f.Of(word)),
                    f.Register + "." + f.Name + " written " + f.Format(f.Of(word)) + ": the model implements only " + string.Join(", ", f.Allowed.Select(f.Format))
                        + " (" + f.BehavesAs + ")",
                    "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; the allow-list (RUL-17), ruled 2026-11-24T18:55Z Q8", State(), offset, word, width);
                return true;
            }
            if(offset == Cr1Offset && (word & M0Bit) != 0 && (word & PceBit) == 0)
            {
                records.Refuse("TVI-USART-FIELD", Unmodeled, "USART_CR1", "M0 (bit 12) = 1 with PCE = 0",
                    "USART_CR1 written with M[1:0] = 01 and no parity: 9 data bits, which the twin does not carry (it delivers bytes)",
                    "RM0433 Rev 9 §48.8.1 p2068-2069 (M1, M0); ruled 2026-11-24T18:55Z Q8", State(), offset, word, width);
                return true;
            }
            return false;
        }

        // TVI-USART-KERNEL (ruled Q3): a start with no running kernel clock, by the RCC guard's existing query.
        private bool RefusedKernel(uint word)
        {
            string name = null;
            GuardedSTM32H7_RCC rcc = null;
            var k = host.TryGetLocalName(this, out name) && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out rcc) ? rcc.QueryKernelClock(name) : null;
            if(k != null && k.Modeled && k.Running)
            {
                return false;
            }
            var start = "CR1.UE with " + ((word & TeBit) != 0 ? "TE" : "") + ((word & TeBit) != 0 && (word & ReBit) != 0 ? " and " : "") + ((word & ReBit) != 0 ? "RE" : "");
            if(k == null || !k.Modeled)
            {
                records.Refuse("TVI-USART-KERNEL", Unmodeled, "USART_CR1", start,
                    (name ?? "a USART") + " started with no kernel-clock rule: " + (k == null ? "no RCC guard at " + RccName : k.Condition),
                    k == null ? "TVI-WO-1300 (4): the RCC guard's QueryKernelClock; ruled 2026-11-24T18:55Z Q3" : k.Citation,
                    State(), Cr1Offset, word, 32);
                return true;
            }
            records.Refuse("TVI-USART-KERNEL", PredictedFault, "USART_CR1", k.Selector,
                name + " started (" + start + ") with no kernel clock: " + k.Condition + "; \"The usart_ker_ck is the USART clock source\"",
                k.Citation + "; §48.4 p2019 (usart_ker_ck); p2031 (\"The clock source frequency is usart_ker_ck\")", State(), Cr1Offset, word, 32);
            return true;
        }

        // TVI-USART-BRR (TVI-WO-1305a (1)): USARTDIV below 16 at a start or on a running port (p2035), but ArduPilot's measured
        // start with BRR = 0 and no DMA request, held until BRR >= 16 (cleared in Pass) while no byte moves (RefusedHeld).
        private bool RefusedBrr(long offset, uint word, int width)
        {
            var brr = Brr;
            var w16 = word & 0xFFFF;
            if(offset == Cr1Offset && !Running(cr1) && Running(word) && brr < 16)
            {
                if(brr == 0 && (cr3 & (DmarBit | DmatBit)) == 0)
                {
                    brrHeld = true;   // the measured pattern (TVI-WO-1305a.measure.md §1), acknowledged by name while nothing moves
                    return false;
                }
                return RefuseBrr(brr, "a start (CR1.UE with TE or RE) with BRR = 0x" + brr.ToString("X") + (brr == 0 ? " and CR3.DMAR or DMAT set" : ""), offset, word, width);
            }
            if(offset == BrrOffset && Running(cr1) && w16 < 16 && w16 != brr)
            {
                return RefuseBrr(w16, "BRR rewritten to 0x" + w16.ToString("X") + " on a running port", offset, word, width);
            }
            return offset == TdrOffset ? RefusedHeld(offset, word, width, "a TDR write")
                : offset == Cr3Offset && (word & (DmarBit | DmatBit)) != 0 && RefusedHeld(offset, word, width, "CR3.DMAR or DMAT set");
        }

        // A byte moving, or a DMA request enabled, while ArduPilot's held start with BRR = 0 waits for BRR >= 16.
        private bool RefusedHeld(long offset, uint value, int width, string what)
        {
            return brrHeld && RefuseBrr(Brr, what + " after a start with BRR = 0, before BRR >= 16 was written: the acknowledged start "
                + "moves no byte (TVI-WO-1305a.measure.md §1)", offset, value, width);
        }

        private bool RefuseBrr(uint brr, string what, long offset, uint value, int width)
        {
            records.Refuse("TVI-USART-BRR", Unmodeled, "USART_BRR", "BRR (15:0) = 0x" + brr.ToString("X") + " (USARTDIV < 16)",
                what + ": \"USARTDIV must be greater than or equal to 16\", and the twin cannot say what silicon does below it",
                "RM0433 Rev 9 §48.5.7 p2035 (and: USARTDIV = BRR with OVER8 = 0); ruled 2026-11-24 at TVI-WO-1305's merge (Q2): "
                + "ArduPilot's measured start acknowledged by name, any other unmodeled; bench row U2", State(), offset, value, width);
            return true;
        }

        // TVI-USART-OVERRUN (TVI-WO-1305a (2)): with FIFOEN = 0 "An overrun error occurs if RXNE flag is set when the next data is
        // received" and "any data received during overrun is lost" (p2030); the stock queue keeps it. FIFO mode is not judged here.
        private bool RefusedOverrun(byte value)
        {
            if((cr1 & FifoenBit) != 0 || (inner.ReadDoubleWord(IsrOffset) & RxfneBit) == 0)
            {
                return false;
            }
            records.Refuse("TVI-USART-OVERRUN", Unmodeled, "USART_ISR", "ORE (bit 3): a byte received with RXNE = 1 and FIFOEN = 0",
                "byte 0x" + value.ToString("X2") + " received while the last one waits unread, with the FIFO disabled: silicon sets ORE and loses "
                    + "it, and the twin's receive queue would keep it, so the twin cannot say what the firmware reads",
                "RM0433 Rev 9 §48.5.6 p2030 (overrun, FIFO mode disabled); ruled 2026-11-24 at TVI-WO-1305's merge (Q3)", State(), RdrOffset, value, 8);
            return true;
        }

        private bool RefuseIdle(string what, string why, long offset, uint value, int width)
        {
            records.Refuse("TVI-USART-IDLE", Unmodeled, "USART_ISR", "IDLE (bit 4): " + why,
                what + ", and IDLE's frame time needs the kernel clock's frequency: " + why + "; the twin uses no default",
                "RM0433 Rev 9 §48.5.7 p2034 (baud = usart_ker_ck_pres / USARTDIV), §8.7.21 p417; ruled 2026-11-24 at TVI-WO-1305's merge (Q4)",
                State(), offset, value, width);
            return true;
        }

        // The port's kernel clock in Hz, from the RCC guard's registers (as the flash guard reads them), PLL2DIVR's trusted only
        // while its `Written` event saw no wide field; null, with the reason, where this guard has no rule. Called only while the RCC guard's query says
        // the clock runs (the start's TVI-USART-KERNEL, and Clocked() for a byte), so a running PLL2 has DIVM2 > 0.
        // Rules, HSI-rooted only (TVI-WO-1305a.measure.md §2: what both firmwares select):
        //   hsi_ker_ck = 64 MHz >> HSIDIV (p341, p385);
        //   rcc_pclk2 (USART1/6) or rcc_pclk1 = sys_ck / D1CPRE / HPRE / D2PPRE2 or D2PPRE1 (pp. 394-396), with sys_ck the HSI (SWS, p393);
        //   pll2_q_ck = HSI / DIVM2 * (DIVN2 + 1) / (DIVQ2 + 1) (p398, p407), FRACN2 0 (the RCC guard's allow-list), from the
        //     read-back while no wide field was written (`pll2Wide`), ref2_ck within 1-16 MHz and the VCO within PLL2VCOSEL's range (p407).
        // No rule: pll3_q_ck, csi_ker_ck, lse_ck, and sys_ck or a PLL reference from CSI, HSE or PLL1.
        private ulong? KernelHz(out string why)
        {
            var rcc = rccSeen;
            if(rcc == null || !host.TryGetLocalName(this, out var name))
            {
                why = "no RCC guard on this platform";
                return null;
            }
            uint R(long o) => rcc.ReadDoubleWord(o);
            var usart16 = name == "usart1" || name == "usart6";
            var hsi = 64000000ul >> (int)(R(0x00) >> 3 & 3);
            var sel = R(0x54) >> (usart16 ? 3 : 0) & 7;
            why = "RCC_D2CCIP2R." + (usart16 ? "USART16SEL" : "USART234578SEL") + " = " + Convert.ToString(sel, 2).PadLeft(3, '0') + " (" + new[] { usart16 ? "rcc_pclk2" : "rcc_pclk1", "pll2_q_ck", "pll3_q_ck", "hsi_ker_ck", "csi_ker_ck", "lse_ck", "disabled", "disabled" }[sel] + ")";
            switch(sel)
            {
            case 0:
                var sws = R(0x10) >> 3 & 7;
                if(sws != 0)
                {
                    why += " with sys_ck not the HSI (RCC_CFGR.SWS = " + Convert.ToString(sws, 2).PadLeft(3, '0') + "): no rule";
                    return null;
                }
                var d1 = R(0x18);
                var d2 = R(0x1C);
                var ppre = d2 >> (usart16 ? 8 : 4) & 7;   // D2PPRE2 or D2PPRE1 (p396): 0xx /1; 100-111 /2-/16
                return hsi / Div(d1 >> 8 & 0xF) / Div(d1 & 0xF) / ((ppre & 4) == 0 ? 1ul : 2ul << (int)(ppre & 3));
            case 1:
                var ck = R(0x28);
                var divr = R(Pll2divrOffset);
                var divm = ck >> 12 & 0x3F;
                var refHz = divm == 0 ? 0 : hsi / divm;
                var vco = divm == 0 ? 0 : hsi * ((divr & 0x1FF) + 1) / divm;
                var wide = (R(0x2C) >> 5 & 1) == 0;   // PLL2VCOSEL (p402): 0 wide, 192-960 MHz; 1 medium, 150-420 MHz (p407)
                if((ck & 3) != 0 || divm == 0 || pll2Wide || refHz < 1000000 || refHz > 16000000
                    || vco < (wide ? 192000000ul : 150000000ul) || vco > (wide ? 960000000ul : 420000000ul))
                {
                    why += (ck & 3) != 0 || divm == 0 ? " with PLL2's reference not the HSI through DIVM2 (RCC_PLLCKSELR): no rule"
                        : pll2Wide ? " with RCC_PLL2DIVR once written with DIVN2 >= 256 or DIVQ2 >= 64, which the stock model narrows: no rule"
                        : " with PLL2 outside p407's ranges (ref2_ck " + refHz + " Hz, VCO " + vco + " Hz): no rule";
                    return null;
                }
                return vco / ((divr >> 16 & 0x7F) + 1);
            case 3:
                return hsi;
            default:
                why += ": no rule for its frequency";
                return null;
            }
        }

        // D1CPRE and HPRE (pp. 394-395): 0xxx /1; 1000-1011 /2-/16; 1100-1111 /64-/512.
        private static ulong Div(uint v)
        {
            return (v & 8) == 0 ? 1 : (v & 7) < 4 ? 2ul << (int)(v & 7) : 64ul << (int)((v & 7) - 4);
        }

        // Whether the port's bus and kernel clocks run now, by the RCC guard's query; true where the query has no rule (a port
        // whose enable the RCC guard already refuses) or no RCC guard is found, as the stock model would.
        private bool Clocked()
        {
            GuardedSTM32H7_RCC rcc = null;
            var k = host.TryGetLocalName(this, out var name) && host.TryGetByName<GuardedSTM32H7_RCC>(RccName, out rcc) ? rcc.QueryKernelClock(name) : null;
            return k == null || !k.Modeled || k.Running;
        }

        private uint Brr => inner.ReadDoubleWord(BrrOffset) & 0xFFFF;

        private static bool Running(uint cr)
        {
            return (cr & UeBit) != 0 && (cr & (TeBit | ReBit)) != 0;
        }

        private uint Protected(long offset)
        {
            switch(offset)
            {
            case Cr1Offset: return 0x33FF9E00;   // FIFOEN, M1, DEAT, DEDT, OVER8, M0, WAKE, PCE, PS (pp. 2067-2069)
            case Cr2Offset:                      // MSBFIRST..ADDM7 (pp. 2075-2077); ADD with RE = 1; ABRMOD with ABREN = 1
                return 0x000FFF30 | ((cr1 & ReBit) != 0 ? 0xFF000000 : 0) | ((cr2 & AbrenBit) != 0 ? 0x00600000u : 0);
            case Cr3Offset: return 0x003EFB3E;   // WUS, SCARCNT, DEP, DEM, DDRE, OVRDIS, ONEBIT, CTSE, RTSE, SCEN, NACK, HDSEL, IRLP, IREN
                                                 // (SCARCNT's write of 0 under UE, p2079, cannot arise: any other value refuses by the allow-list)
            case BrrOffset: return 0xFFFF;       // p2082
            case GtprOffset: return 0xFFFF;      // p2083
            case PrescOffset: return 0xF;        // p2099
            default: return 0;
            }
        }

        // The ChibiOS re-init pattern (ruled Q2 (c)): BRR, CR1.M0/PCE/FIFOEN, CR2.RXINV.
        private static uint Acknowledged(long offset)
        {
            return offset == BrrOffset ? 0xFFFFu : offset == Cr1Offset ? M0Bit | PceBit | FifoenBit : offset == Cr2Offset ? RxinvBit : 0;
        }

        // ---- IDLE and the receive request ----------------------------------------------------------------------

        // One idle frame after this byte, unless a newer one arrives or the port stops (p2090, p2021).
        private void ScheduleIdle(TimeInterval? t)
        {
            var g = ++idleGen;
            if(t == null)
            {
                return;
            }
            host.ScheduleAction(t.Value, _ =>
            {
                lock(sync)
                {
                    if(g == idleGen)
                    {
                        idle = true;
                        UpdateIrq();
                    }
                }
            }, "Tviburi USART IDLE");
        }

        // A start bit, the data bits by M (00 8, 01 9, 10 7; p2068-2069), the stop bits by STOP (00 1, 01 0.5, 10 2, 11 1.5;
        // p2076), at the kernel clock's baud over BRR (p2034), in the stock model's integer arithmetic (its BaudRate). With no
        // rule for the kernel clock, TVI-USART-IDLE and no frame (ruled Q4: never the stock `frequency`).
        private TimeInterval? FrameTime(string what, uint value, int width)
        {
            var hz = KernelHz(out var why);
            var baud = hz == null ? 0 : (uint)(hz.Value / Math.Max(1u, Brr));
            if(baud == 0)
            {
                RefuseIdle(what, hz == null ? why : why + " at " + hz + " Hz, below one baud at this BRR", RdrOffset, value, width);
                return null;
            }
            var m = ((cr1 >> 27) & 2) | ((cr1 >> 12) & 1);
            var data = m == 1 ? 9 : m == 2 ? 7 : 8;
            var halves = 2 * (1 + data) + new[] { 2, 1, 4, 3 }[(cr2 >> 12) & 3];
            return TimeInterval.FromNanoseconds((ulong)Math.Ceiling(halves * 1e9 / (2.0 * baud)));
        }

        private bool RequestLevel()
        {
            // UE = 0 flushes the queue (p2070), and the stock model takes no byte then, so a waiting byte implies UE = 1.
            return (cr3 & DmarBit) != 0 && (inner.ReadDoubleWord(IsrOffset) & RxfneBit) != 0;
        }

        // The request as a level (p2048, p2070): an edge for the DMA whenever it holds, again after each byte the DMA takes
        // (an RDR read inside the pulse), and once per frame while a byte waits for a stream that is not yet armed.
        private void Request()
        {
            if(pulsing)
            {
                pulseAgain = true;
                return;
            }
            pulsing = true;
            try
            {
                do
                {
                    pulseAgain = false;
                    ReceiveDmaRequest.Unset();
                    if(!RequestLevel())
                    {
                        break;
                    }
                    ReceiveDmaRequest.Set();
                }
                while(pulseAgain);
            }
            finally
            {
                pulsing = false;
            }
            if(RequestLevel() && !repulse && FrameTime("a received byte waits for its next request (no bus access)", 0, 0) is TimeInterval t)
            {
                repulse = true;
                host.ScheduleAction(t, _ =>
                {
                    lock(sync)
                    {
                        repulse = false;
                        Request();
                    }
                }, "Tviburi USART request");
            }
        }

        private void UpdateIrq()
        {
            IRQ.Set(innerIrq || (idle && (cr1 & IdleieBit) != 0));
        }

        // ---- names and state -----------------------------------------------------------------------------------

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
            if(lowPowerMode)
            {
                return "lowPowerMode (LPUART1 is not modeled by this guard)";
            }
            try
            {
                var points = host.SystemBus.GetRegistrationPoints(this).ToArray();
                return points.Length == 1 ? "" : points.Length + " bus registrations, not one";
            }
            catch(Exception e)
            {
                return "no bus registration (" + e.GetType().Name + ")";
            }
        }

        private static bool Defined(long offset)
        {
            return offset >= 0 && offset <= PrescOffset;   // aligned: Narrow ran first
        }

        private static string RegisterAt(long offset)
        {
            var f = Fields.FirstOrDefault(x => x.Offset == offset);
            return f != null ? f.Register : "none";
        }

        private string State()
        {
            return "CR1=0x" + cr1.ToString("X") + " CR2=0x" + cr2.ToString("X") + " CR3=0x" + cr3.ToString("X")
                + " BRR=0x" + inner.ReadDoubleWord(BrrOffset).ToString("X") + " ISR=0x" + Served(IsrOffset).ToString("X");
        }

        private readonly IMachine host;
        private readonly bool lowPowerMode;
        private readonly STM32F7_USART inner;
        private readonly Records records;
        private readonly object sync = new object();
        private uint cr1, cr2, cr3, gtpr, presc;
        private bool idle, innerIrq, pulsing, pulseAgain, repulse, brrHeld;
        private ulong idleGen;
        private string config;
        private GuardedSTM32H7_RCC rccSeen;
        private bool pll2Wide;

        private const long Pll2divrOffset = 0x38;      // RCC_PLL2DIVR, §8.7.15 p406
        private const uint UeRe = UeBit | ReBit;
        private const uint DmatBit = 1u << 7;

        private const string RccName = "sysbus.rcc";
        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";
        private const long Cr1Offset = 0x00;
        private const long Cr2Offset = 0x04;
        private const long Cr3Offset = 0x08;
        private const long BrrOffset = 0x0C;
        private const long GtprOffset = 0x10;
        private const long RqrOffset = 0x18;
        private const long IsrOffset = 0x1C;
        private const long IcrOffset = 0x20;
        private const long RdrOffset = 0x24;
        private const long TdrOffset = 0x28;
        private const long PrescOffset = 0x2C;
        private const uint UeBit = 1u << 0;
        private const uint ReBit = 1u << 2;
        private const uint TeBit = 1u << 3;
        private const uint IdleieBit = 1u << 4;
        private const uint PceBit = 1u << 10;
        private const uint M0Bit = 1u << 12;
        private const uint FifoenBit = 1u << 29;
        private const uint RxinvBit = 1u << 16;
        private const uint AbrenBit = 1u << 20;
        private const uint DmarBit = 1u << 6;
        private const uint IdleBit = 1u << 4;     // ISR.IDLE and ICR.IDLECF
        private const uint RxfneBit = 1u << 5;
        private const uint RxfrqBit = 1u << 3;

        // The stock model's register names, so the access log names them as it did (measure.md §2.3).
        private enum Registers
        {
            ControlRegister1 = 0x0,
            ControlRegister2 = 0x4,
            ControlRegister3 = 0x8,
            BaudRate = 0xC,
            ReceiverTimeout = 0x14,
            Request = 0x18,
            InterruptAndStatus = 0x1C,
            InterruptFlagClear = 0x20,
            ReceiveData = 0x24,
            TransmitData = 0x28,
        }

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

        // Generated by tests/1305/fields.py from RM0433 Rev 9 §48.8 (pp. 2067-2099; tests/1305/data), with the classes ruled
        // 2026-11-24T18:55Z.
        private static readonly Row[] Fields =
        {
            R("USART_CR1", 0x000, "§48.8.1", "RXFFIE", 31, 31, Zero, "undefined in the stock model", "0 only: the stock model has no bit 31 (reserved there); the RX FIFO never fills in the twin (U8)", 2067),
            R("USART_CR1", 0x000, "§48.8.1", "TXFEIE", 30, 30, Zero, "undefined in the stock model", "0 only: the stock model has no bit 30 (reserved there); the TX FIFO interrupts are not modeled", 2067),
            R("USART_CR1", 0x000, "§48.8.1", "FIFOEN", 29, 29, Any, "stock, wrong (FIFOEN is reserved in the stock model, which runs no FIFO: its receive queue is unbounded and ORE never sets, U8; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "kept by the guard and served on read (the stock model has no bit 29, U2), so a read-modify-write keeps it as silicon does; FIFO mode acknowledged by name (ruled 2026-11-24T18:55Z Q8): the receive queue is unbounded and ORE never sets (U8), the transmitter never fills; with FIFOEN = 0, a byte arriving while the last waits unread is TVI-USART-OVERRUN, unmodeled (p2030; ruled at TVI-WO-1305's merge, Q3); protected under UE (p2067): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled", 2067),
            R("USART_CR1", 0x000, "§48.8.1", "M1", 28, 28, Zero, "tag", "0 only: a stock tag; 7-bit words are not modeled; protected under UE (p2068): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "EOBIE", 27, 27, Zero, "tag", "0 only: a stock tag; smartcard mode is not modeled", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "RTOIE", 26, 26, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock receiver timeout runs on RTOIE alone, ignoring RTOEN, at 8 times RTO bit times (STM32F7_USART.cs L118-131; U9), so it is not modeled", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "DEAT", 25, 21, Zero, "tag", "0 only: a stock tag; driver enable is not modeled; protected under UE (p2068): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "DEDT", 20, 16, Zero, "tag", "0 only: a stock tag; driver enable is not modeled; protected under UE (p2068): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "OVER8", 15, 15, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model stores it and halves its baud divisor; oversampling by 8 is not audited (no firmware writes it, TVI-WO-1305.measure.md §2.1); protected under UE (p2068): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "CMIE", 14, 14, Zero, "tag", "0 only: a stock tag; character match is not modeled", 2068),
            R("USART_CR1", 0x000, "§48.8.1", "MME", 13, 13, Zero, "tag", "0 only: a stock tag; mute mode is not modeled", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "M0", 12, 12, Any, "stock, wrong (M0 is a stock tag: the word length is not modeled; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "kept by the guard and served on read (a stock tag, U2); 1 only with PCE = 1, 8 data bits and parity (ArduPilot's SBUS probe, TVI-WO-1305.measure.md §2.1), acknowledged by name (ruled 2026-11-24T18:55Z Q8): the twin's line has no polarity, word length, parity or framing: bytes are delivered whole; M0 = 1 with PCE = 0 (9 data bits) is TVI-USART-FIELD; it enters IDLE's frame time; protected under UE (p2069): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "WAKE", 11, 11, Zero, "tag", "0 only: a stock tag; mute mode is not modeled; protected under UE (p2069): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "PCE", 10, 10, Any, "stock, wrong (the stock model stores PCE but neither generates nor checks parity; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "the stock model stores it; parity is neither generated nor checked (ruled 2026-11-24T18:55Z Q8): the twin's line has no polarity, word length, parity or framing: bytes are delivered whole; protected under UE (p2069): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "PS", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model stores it (its ParityBit); parity is not checked; protected under UE (p2069): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "PEIE", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "the stock model stores it; no parity error is ever raised, so it never fires", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "TXFNFIE", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model's TXEIE: TXFNF always reads 1 (U14), so it fires while set", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "TCIE", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model: TC set at each TDR write and when TE is cleared (U14)", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "RXFNEIE", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model's RXNEIE: fires while a received byte waits", 2069),
            R("USART_CR1", 0x000, "§48.8.1", "IDLEIE", 4, 4, Any, "modeled by the guard", "kept by the guard and served on read (a stock tag); with it set, the guard's IDLE raises the port's interrupt (p2070, p2090; ruled 2026-11-24T18:55Z Q5)", 2070),
            R("USART_CR1", 0x000, "§48.8.1", "TE", 3, 3, Any, "modeled by the guard", "the stock model transmits; a start (UE with TE or RE) asks the RCC guard's kernel-clock query, TVI-USART-KERNEL (ruled 2026-11-24T18:55Z Q3)", 2070),
            R("USART_CR1", 0x000, "§48.8.1", "RE", 2, 2, Any, "modeled by the guard", "the stock model receives; a start (UE with TE or RE) asks the RCC guard's kernel-clock query, TVI-USART-KERNEL (ruled 2026-11-24T18:55Z Q3)", 2070),
            R("USART_CR1", 0x000, "§48.8.1", "UESM", 1, 1, Zero, "tag", "0 only: a stock tag; wake-up from Stop is not modeled", 2070),
            R("USART_CR1", 0x000, "§48.8.1", "UE", 0, 0, Any, "modeled by the guard", "the stock model; at a start the kernel-clock query (TVI-USART-KERNEL); while set, protected fields refuse (TVI-USART-UE); a write that clears it is judged as with UE = 0 (ruled 2026-11-24T18:55Z Q2); cleared, it resets every ISR flag and the DMA requests (p2070): the guard flushes the receive queue and clears IDLE and TC", 2070),
            R("USART_CR2", 0x004, "§48.8.3", "ADD", 31, 24, Zero, "tag", "0 only: a stock tag; address match is not modeled; protected under UE (p2074): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2074),
            R("USART_CR2", 0x004, "§48.8.3", "RTOEN", 23, 23, Zero, "tag", "0 only: a stock tag; the receiver timeout is not modeled", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "ABRMOD", 22, 21, Zero, "tag", "0 only: a stock tag; auto baud rate is not modeled; protected under UE (p2075): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "ABREN", 20, 20, Zero, "tag", "0 only: a stock tag; auto baud rate is not modeled", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "MSBFIRST", 19, 19, Zero, "tag", "0 only: a stock tag; MSB-first order is not modeled; protected under UE (p2075): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "DATAINV", 18, 18, Zero, "tag", "0 only: a stock tag; data inversion is not modeled; protected under UE (p2075): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "TXINV", 17, 17, Zero, "tag", "0 only: a stock tag; TX inversion is not modeled; protected under UE (p2075): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "RXINV", 16, 16, Any, "stock, wrong (RXINV is a stock tag: the line has no polarity; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "kept by the guard and served on read (a stock tag, U2); the twin's line has no polarity, word length, parity or framing: bytes are delivered whole (ArduPilot's SBUS probe, ruled 2026-11-24T18:55Z Q8); protected under UE (p2075): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled", 2075),
            R("USART_CR2", 0x004, "§48.8.3", "SWAP", 15, 15, Zero, "tag", "0 only: a stock tag; the pin swap is not modeled; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2076),
            R("USART_CR2", 0x004, "§48.8.3", "LINEN", 14, 14, Zero, "tag", "0 only: a stock tag; LIN mode is not modeled; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2076),
            R("USART_CR2", 0x004, "§48.8.3", "STOP", 13, 12, Any, "inherited, unaudited (acknowledged by name)", "the stock model stores it (its StopBits); it enters IDLE's frame time; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2076),
            R("USART_CR2", 0x004, "§48.8.3", "CLKEN", 11, 11, Zero, "tag", "0 only: a stock tag; synchronous mode is not modeled; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2076),
            R("USART_CR2", 0x004, "§48.8.3", "CPOL", 10, 10, Zero, "tag", "0 only: a stock tag; synchronous mode is not modeled; protected under UE (p2076): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2076),
            R("USART_CR2", 0x004, "§48.8.3", "CPHA", 9, 9, Zero, "tag", "0 only: a stock tag; synchronous mode is not modeled; protected under UE (p2077): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "LBCL", 8, 8, Zero, "tag", "0 only: a stock tag; synchronous mode is not modeled; protected under UE (p2077): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "Res7_7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "LBDIE", 6, 6, Any, "stock, wrong (LBDIE is a stock tag: no LIN break is raised; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "kept by the guard and served on read (a stock tag); ChibiOS sets it on every port: no LIN break is ever raised (LINEN 0 only), so it never fires (ruled 2026-11-24T18:55Z Q8)", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "LBDL", 5, 5, Zero, "tag", "0 only: a stock tag; LIN mode is not modeled; protected under UE (p2077): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "ADDM7", 4, 4, Zero, "tag", "0 only: a stock tag; address match is not modeled; protected under UE (p2077): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "DIS_NSS", 3, 3, Zero, "undefined in the stock model", "0 only: the stock model has no bits 3:0 (reserved there); SPI slave mode is not modeled", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "Res2_1", 2, 1, Zero, "reserved", "reserved: must be kept at reset value", 2077),
            R("USART_CR2", 0x004, "§48.8.3", "SLVEN", 0, 0, Zero, "undefined in the stock model", "0 only: the stock model has no bits 3:0 (reserved there); SPI slave mode is not modeled", 2078),
            R("USART_CR3", 0x008, "§48.8.4", "TXFTCFG", 31, 29, Zero, "undefined in the stock model", "0 only: the stock model has no bits 31:25 (reserved there); FIFO thresholds are not modeled", 2078),
            R("USART_CR3", 0x008, "§48.8.4", "RXFTIE", 28, 28, Zero, "undefined in the stock model", "0 only: the stock model has no bits 31:25 (reserved there); FIFO thresholds are not modeled", 2078),
            R("USART_CR3", 0x008, "§48.8.4", "RXFTCFG", 27, 25, Zero, "undefined in the stock model", "0 only: the stock model has no bits 31:25 (reserved there); FIFO thresholds are not modeled", 2078),
            R("USART_CR3", 0x008, "§48.8.4", "TCBGTIE", 24, 24, Zero, "tag", "0 only: a stock tag; smartcard mode is not modeled", 2078),
            R("USART_CR3", 0x008, "§48.8.4", "TXFTIE", 23, 23, Zero, "tag", "0 only: the stock model's tag at bit 23 (named UCESM there); FIFO thresholds are not modeled", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "WUFIE", 22, 22, Zero, "tag", "0 only: a stock tag; wake-up from Stop is not modeled", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "WUS", 21, 20, Zero, "tag", "0 only: a stock tag; wake-up from Stop is not modeled; protected under UE (p2079): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "SCARCNT", 19, 17, Zero, "tag", "0 only: a stock tag; smartcard mode is not modeled; protected under UE (p2079): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "Reserved", 16, 16, Zero, "reserved", "reserved: must be kept at reset value", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "DEP", 15, 15, Zero, "tag", "0 only: a stock tag; driver enable is not modeled; protected under UE (p2079): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2079),
            R("USART_CR3", 0x008, "§48.8.4", "DEM", 14, 14, Zero, "tag", "0 only: a stock tag; driver enable is not modeled; protected under UE (p2080): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "DDRE", 13, 13, Zero, "tag", "0 only: a stock tag; DMA disable on error is not modeled; protected under UE (p2080): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "OVRDIS", 12, 12, Zero, "tag", "0 only: a stock tag; overrun disable is not modeled; protected under UE (p2080): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "ONEBIT", 11, 11, Zero, "tag", "0 only: a stock tag; one-bit sampling is not modeled; protected under UE (p2080): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "CTSIE", 10, 10, Zero, "tag", "0 only: a stock tag; hardware flow control is not modeled", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "CTSE", 9, 9, Zero, "tag", "0 only: a stock tag; hardware flow control is not modeled; protected under UE (p2080): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2080),
            R("USART_CR3", 0x008, "§48.8.4", "RTSE", 8, 8, Zero, "tag", "0 only: a stock tag; hardware flow control is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "DMAT", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "the stock model stores it; a memory-to-peripheral stream moves its bytes at the DMA's EN, unpaced (TVI-WO-1303 Q5 (c))", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "DMAR", 6, 6, Any, "modeled by the guard", "the guard drives ReceiveDmaRequest: asserted while DMAR = 1, UE = 1 and a received byte waits, again after each RDR read that leaves one, and once per frame while one waits; reset at UE = 0 (p2048, p2070; ruled 2026-11-24T18:55Z Q6)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "SCEN", 5, 5, Zero, "tag", "0 only: a stock tag; smartcard mode is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "NACK", 4, 4, Zero, "tag", "0 only: a stock tag; smartcard mode is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "HDSEL", 3, 3, Zero, "tag", "0 only: a stock tag; half-duplex mode is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "IRLP", 2, 2, Zero, "tag", "0 only: a stock tag; IrDA is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "IREN", 1, 1, Zero, "tag", "0 only: a stock tag; IrDA is not modeled; protected under UE (p2081): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2081),
            R("USART_CR3", 0x008, "§48.8.4", "EIE", 0, 0, Any, "stock, wrong (EIE is a stock tag: no framing, noise or overrun error is raised; acknowledged by name, ruled 2026-11-24T18:55Z Q8)", "kept by the guard and served on read (a stock tag); ChibiOS sets it on every port: no framing, noise or overrun error is ever raised, so it never fires (ruled 2026-11-24T18:55Z Q8)", 2082),
            R("USART_BRR", 0x00C, "§48.8.5", "Reserved", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 2082),
            R("USART_BRR", 0x00C, "§48.8.5", "BRR", 15, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model stores it: its BaudRate, the frequency property over BRR (baud accuracy is Aethelgard's); IDLE's frame time is the kernel clock over BRR (p2034; TVI-USART-IDLE where the guard has no rule for the kernel clock); USARTDIV must be >= 16 (p2035): a start with BRR < 16, or BRR rewritten below 16 on a running port, is TVI-USART-BRR, unmodeled, but for ArduPilot's measured start with BRR = 0 and no DMA request, acknowledged by name until BRR >= 16 is written while no byte moves (ruled at TVI-WO-1305's merge, Q2; bench row U2); protected under UE (p2082): a change with UE = 1 confined to BRR, CR1.M0/PCE/FIFOEN and CR2.RXINV is ChibiOS re-running usart_init on an enabled port (sdStart without sdStop), acknowledged by name with no record: the stock model takes it at once; RM0433 does not say what silicon does, and thousands of H7 flight controllers run this ArduPilot RC-protocol probing and change baud, field evidence, not a page (ruled 2026-11-24T18:55Z Q2 (c); bench row U1); any other change with UE = 1 is TVI-USART-UE, unmodeled", 2082),
            R("USART_GTPR", 0x010, "§48.8.6", "Reserved", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 2082),
            R("USART_GTPR", 0x010, "§48.8.6", "GT", 15, 8, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x10 (U1); smartcard and IrDA are not modeled; the guard keeps 0 and serves it; protected under UE (p2083): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2083),
            R("USART_GTPR", 0x010, "§48.8.6", "PSC", 7, 0, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x10 (U1); smartcard and IrDA are not modeled; the guard keeps 0 and serves it; protected under UE (p2083): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2083),
            R("USART_RTOR", 0x014, "§48.8.7", "BLEN", 31, 24, Zero, "tag", "0 only: a stock tag; smartcard block length is not modeled", 2084),
            R("USART_RTOR", 0x014, "§48.8.7", "RTO", 23, 0, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock receiver timeout is not modeled (RTOIE)", 2084),
            R("USART_RQR", 0x018, "§48.8.8", "Reserved", 31, 5, Zero, "reserved", "reserved: must be kept at reset value", 2084),
            R("USART_RQR", 0x018, "§48.8.8", "TXFRQ", 4, 4, Zero, "tag", "0 only: a stock tag; the TX FIFO flush is not modeled", 2085),
            R("USART_RQR", 0x018, "§48.8.8", "RXFRQ", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "the stock model flushes the receive queue (U15)", 2085),
            R("USART_RQR", 0x018, "§48.8.8", "MMRQ", 2, 2, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model takes it with no effect; mute mode is not modeled", 2085),
            R("USART_RQR", 0x018, "§48.8.8", "SBKRQ", 1, 1, Zero, "inherited, unaudited (acknowledged by name)", "0 only: the stock model takes it with no effect; no break is sent", 2085),
            R("USART_RQR", 0x018, "§48.8.8", "ABRRQ", 0, 0, Zero, "tag", "0 only: a stock tag; auto baud rate is not modeled", 2085),
            R("USART_ISR", 0x01C, "§48.8.9", "Reserved", 31, 28, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "TXFT", 27, 27, Any, "read-only", "read-only (p2086): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "RXFT", 26, 26, Any, "read-only", "read-only (p2086): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "TCBGT", 25, 25, Any, "read-only", "read-only (p2086): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "RXFF", 24, 24, Any, "read-only", "read-only (p2086): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "TXFE", 23, 23, Any, "read-only", "read-only (p2086): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2086),
            R("USART_ISR", 0x01C, "§48.8.9", "REACK", 22, 22, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: RE", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "TEACK", 21, 21, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: TE", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "WUF", 20, 20, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "RWU", 19, 19, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "SBKF", 18, 18, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "CMF", 17, 17, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "BUSY", 16, 16, Any, "read-only", "read-only (p2087): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2087),
            R("USART_ISR", 0x01C, "§48.8.9", "ABRF", 15, 15, Any, "read-only", "read-only (p2088): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2088),
            R("USART_ISR", 0x01C, "§48.8.9", "ABRE", 14, 14, Any, "read-only", "read-only (p2088): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2088),
            R("USART_ISR", 0x01C, "§48.8.9", "UDR", 13, 13, Any, "read-only", "read-only (p2088): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2088),
            R("USART_ISR", 0x01C, "§48.8.9", "EOBF", 12, 12, Any, "read-only", "read-only (p2088): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2088),
            R("USART_ISR", 0x01C, "§48.8.9", "RTOF", 11, 11, Any, "read-only", "read-only (p2088): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: 0 while RTOIE = 0 (the only value allowed)", 2088),
            R("USART_ISR", 0x01C, "§48.8.9", "CTS", 10, 10, Any, "read-only", "read-only (p2089): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2089),
            R("USART_ISR", 0x01C, "§48.8.9", "CTSIF", 9, 9, Any, "read-only", "read-only (p2089): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2089),
            R("USART_ISR", 0x01C, "§48.8.9", "LBDF", 8, 8, Any, "read-only", "read-only (p2089): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2089),
            R("USART_ISR", 0x01C, "§48.8.9", "TXFNF", 7, 7, Any, "read-only", "read-only (p2089): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: always 1 (transmission is immediate)", 2089),
            R("USART_ISR", 0x01C, "§48.8.9", "TC", 6, 6, Any, "read-only", "read-only (p2089): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: set at each TDR write and when TE is cleared", 2089),
            R("USART_ISR", 0x01C, "§48.8.9", "RXFNE", 5, 5, Any, "read-only", "read-only (p2090): a write is ignored and changes nothing (no record, RUL-18 as amended); the stock model's: a received byte waits", 2090),
            R("USART_ISR", 0x01C, "§48.8.9", "IDLE", 4, 4, Any, "read-only", "read-only (p2090): a write is ignored and changes nothing (no record, RUL-18 as amended); the guard's: set one idle frame after the last received byte, never at a start (p2090, p2021, p2040; ruled 2026-11-24T18:55Z Q5), cleared by IDLECF and by UE = 0", 2090),
            R("USART_ISR", 0x01C, "§48.8.9", "ORE", 3, 3, Any, "read-only", "read-only (p2090): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2090),
            R("USART_ISR", 0x01C, "§48.8.9", "NE", 2, 2, Any, "read-only", "read-only (p2090): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2090),
            R("USART_ISR", 0x01C, "§48.8.9", "FE", 1, 1, Any, "read-only", "read-only (p2091): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2091),
            R("USART_ISR", 0x01C, "§48.8.9", "PE", 0, 0, Any, "read-only", "read-only (p2091): a write is ignored and changes nothing (no record, RUL-18 as amended); reads 0: never set in the twin", 2091),
            R("USART_ICR", 0x020, "§48.8.11", "Res31_21", 31, 21, Any, "inherited, unaudited (acknowledged by name)", "ChibiOS writes 1 here (ICR = 0xFFFFFFFF at every usart_init, TVI-WO-1305.measure.md §2.1); RM0433 says \"must be kept at reset value\"; a clear register: the write clears nothing (ruled 2026-11-24T18:55Z Q8)", 2096),
            R("USART_ICR", 0x020, "§48.8.11", "WUCF", 20, 20, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2096),
            R("USART_ICR", 0x020, "§48.8.11", "Res19_18", 19, 18, Any, "inherited, unaudited (acknowledged by name)", "ChibiOS writes 1 here (ICR = 0xFFFFFFFF at every usart_init, TVI-WO-1305.measure.md §2.1); RM0433 says \"must be kept at reset value\"; a clear register: the write clears nothing (ruled 2026-11-24T18:55Z Q8)", 2096),
            R("USART_ICR", 0x020, "§48.8.11", "CMCF", 17, 17, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2096),
            R("USART_ICR", 0x020, "§48.8.11", "Res16_14", 16, 14, Any, "inherited, unaudited (acknowledged by name)", "ChibiOS writes 1 here (ICR = 0xFFFFFFFF at every usart_init, TVI-WO-1305.measure.md §2.1); RM0433 says \"must be kept at reset value\"; a clear register: the write clears nothing (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "UDRCF", 13, 13, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "EOBCF", 12, 12, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "RTOCF", 11, 11, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears RTOF", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "Res10_10", 10, 10, Any, "inherited, unaudited (acknowledged by name)", "ChibiOS writes 1 here (ICR = 0xFFFFFFFF at every usart_init, TVI-WO-1305.measure.md §2.1); RM0433 says \"must be kept at reset value\"; a clear register: the write clears nothing (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "CTSCF", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "LBDCF", 8, 8, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "TCBGTCF", 7, 7, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "TCCF", 6, 6, Any, "inherited, unaudited (acknowledged by name)", "the stock model clears TC", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "TXFECF", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "IDLECF", 4, 4, Any, "modeled by the guard", "clears the guard's IDLE (p2097)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "ORECF", 3, 3, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "NECF", 2, 2, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "FECF", 1, 1, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_ICR", 0x020, "§48.8.11", "PECF", 0, 0, Any, "inherited, unaudited (acknowledged by name)", "clears a flag the twin never sets, so the write changes nothing; ChibiOS writes it at every init (ruled 2026-11-24T18:55Z Q8)", 2097),
            R("USART_RDR", 0x024, "§48.8.12", "Reserved", 31, 9, Any, "read-only", "reserved in a read-only register: a write is ignored and changes nothing (no record, RUL-18 as amended)", 2098),
            R("USART_RDR", 0x024, "§48.8.12", "RDR", 8, 0, Any, "read-only", "read-only (p2098): a write is ignored (no record); a read takes the stock model's next received byte; a byte or half-word read passes (ruled 2026-11-24T18:55Z Q7)", 2098),
            R("USART_TDR", 0x028, "§48.8.13", "Reserved", 31, 9, Zero, "reserved", "reserved: must be kept at reset value", 2098),
            R("USART_TDR", 0x028, "§48.8.13", "TDR", 8, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model sends its low byte (9-bit words refuse at CR1.M0); a byte or half-word write passes: ArduPilot's DMA writes it by bytes, though p2067 says \"by words\", field evidence (ruled 2026-11-24T18:55Z Q7)", 2098),
            R("USART_PRESC", 0x02C, "§48.8.14", "Reserved", 31, 4, Zero, "reserved", "reserved: must be kept at reset value", 2099),
            R("USART_PRESC", 0x02C, "§48.8.14", "PRESCALER", 3, 0, Zero, "undefined in the stock model", "0 only: the stock model has no register 0x2C (U1); the kernel clock is not prescaled; the guard keeps 0 and serves it; protected under UE (p2099): changed with UE = 1 it is TVI-USART-UE, unmodeled (ruled 2026-11-24T18:55Z Q2)", 2099),
        };
    }
}
