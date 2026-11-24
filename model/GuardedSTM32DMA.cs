// Tviburi's guard over Renode's STM32DMA for the STM32H753's DMA1 and DMA2 (TVI-WO-1303): a stream moves data only where
// and when silicon would. The measurement, every page, and the rulings (2026-11-24T14:10Z, Q1-Q9):
// docs/work-orders/TVI-WO-1303.measure.md (RM0433 Rev 9 §15.5, §17, §2.1.6, Table 7).
//
// Insertion point (RUL-07 as amended): STM32DMA is sealed (measured, measure.md §2.3), so this guard is a wrapper that
// owns an unregistered stock instance and forwards every access it passes, every DMAMUX1 request line (IGPIOReceiver),
// its stream interrupt lines (INumberedGPIOOutput) and every reset. The stock platform declares both controllers, so the
// board description cancels each and declares this guard at its address `as "dma1"` / `as "dma2"` on tviburiRcc, with the
// stock entry's interrupt lines and an untyped `dmamux1:` update routing DMAMUX1's channels to it
// (model/stm32h7-dma-restate.repl). DMAMUX1 is not guarded (ruled Q1): the routing rules run at each stream's enable,
// reading the stream's channel from the stock `dmamux1`, which the platform hands this guard as `dmamux`. Which
// controller it is comes from its own bus registration (DMA1 0x40020000: DMAMUX1 channels 0-7; DMA2 0x40020400: 8-15,
// p696), never from a name.
//
// The clock (WO (2), ruled Q7): with DMA1EN/DMA2EN = 0 the RCC guard locks this peripheral on the bus, so an access is
// dropped there (a read returns 0) with no record, which is silicon's ignored write (p373). It is covered, not refused.
//
// A stream's state, for the rules on writes while it is enabled (ruled Q2, Q3 (c)):
//   off        EN = 0 (as the stock model holds it, which is as software wrote it: the stock model never clears EN at the
//              end of a transfer, stock-wrong, p665).
//   running    EN = 1 and the twin's transfer has items left (NDT > 0).
//   completed  EN = 1 and the twin's transfer has none left. The stock model completes a memory-to-peripheral or
//              memory-to-memory transfer inside the EN write itself (measure.md D2, D9), where silicon takes as long as its
//              requests do and then clears EN, so the twin cannot say whether silicon is still moving data.
//
// Rules, in the order they are evaluated. A refused access is not passed to the stock model.
//   TVI-LOAD          (Records) the guard did not load: every access refuses.
//   TVI-DMA-CONFIG    the platform gives this guard what it cannot model: a bus address that is not DMA1's or DMA2's,
//                     more than one registration, or no `dmamux`. unmodeled.
//   TVI-DMA-WIDTH     a byte or half-word access: "The DMA registers have to be accessed by words" (p659). unmodeled.
//   TVI-DMA-READ      a read of an offset with no register (0xD0-0x3FC, Table 117 p668-671). unmodeled.
//   TVI-DMA-EN        a write, while the stream is running, that changes a field RM0433 says can be written only with
//                     EN = 0 (CR: MBURST, PBURST, CT, DBM, PL, PINCOS, MSIZE, PSIZE, MINC, PINC, DIR, PFCTRL, pp. 662-664;
//                     NDTR p665; PAR p666; M0AR, M1AR p666; FCR.DMDIS and FTH p667): silicon ignores it, so the stream does not do
//                     what the firmware wrote. predicted-fault. The same value passes (RUL-18, amended). A write that also
//                     clears EN is judged against the state before it. The other CR fields (TCIE, HTIE, TEIE, DMEIE, CIRC,
//                     TRBUFF) and FCR's FEIE carry no such line and take the write (ruled Q2: p665's "forbidden to
//                     write these registers when the EN bit is read as 1" would refuse every disable, which RM0433 tells
//                     software to make). In recorded mode the protected fields keep their old values, as on silicon.
//   TVI-DMA-DONE      the same write once the twin has completed the transfer and EN is still set; and any CR write with
//                     EN = 1 then, changed or not: silicon cleared EN at the end of the transfer (p665), so on silicon that
//                     write starts a new transfer, reloading NDT, which the twin does not make. unmodeled (Q3 (c)).
//   TVI-DMA-FIELD     the allow-list (`Fields`): a field given a value the model does not implement, or a write to an
//                     offset with no register. unmodeled.
//   At the write that sets EN (0 -> 1), on the stream's configuration as the copies and the stock model then hold it, with
//   NDT items of PSIZE bytes on each side that increments (one item on a side that does not: "the number of bytes to be
//   transferred is equal to 2 x NDT" for half-words, p648; direct mode forces MSIZE to PSIZE, p663); NDT = 0 addresses
//   nothing (p665), and after a completed transfer silicon reloads the NDT last programmed (p665), so that is the span:
//   TVI-DMA-SIZE      PSIZE and MSIZE differ: silicon forces MSIZE to PSIZE in direct mode (p663), and memory-to-memory
//                     runs in FIFO mode only (§15.3.8 p645), while the stock model counts the transfer in MSIZE items and
//                     corrupts its count or throws (STM32DMA.cs L251-255, measured in review). unmodeled.
//   TVI-DMA-RELOAD    NDT = 0 after a non-zero NDT was programmed: silicon reloads the programmed value when the stream is
//                     enabled again (p665), and the stock model never reloads, so the twin would move nothing. unmodeled;
//                     the address rules below run first, on the reloaded span.
//   TVI-DMA-TCM       a span with a byte in ITCM (0x0000_0000-0x0000_FFFF) or DTCM (0x2000_0000-0x2001_FFFF): DMA1/DMA2's
//                     memory and peripheral buses reach every internal memory "except ITCM and DTCM" (§2.1.6 p108; Table 7
//                     p131). predicted-fault. The stock platform maps both TCMs as plain memory, so the twin would move it.
//   TVI-DMA-ADDRESS   a span that wraps past 0xFFFF_FFFF (judged before TVI-DMA-TCM), or with a byte in no region Table 7
//                     maps (p130-131): reserved or unmapped. unmodeled (Q4).
//   For a stream that uses a request (peripheral-to-memory, memory-to-peripheral), from its DMAMUX1 channel's CxCR:
//   TVI-DMA-ROUTE     DMAREQ_ID 0, "no DMA request line selected" (p703), or a reserved id (54, 116-127; Table 124
//                     p697-698, printed again identically as Table 125): silicon moves nothing, where the stock model moves
//                     a memory-to-peripheral transfer at once (measure.md D9). predicted-fault.
//   TVI-DMA-MUX       a request generator id (1-8): DMAMUX1's request generators are stock tags. unmodeled. (Its
//                     synchronisation and event fields, SE, EGE, SOIE, SPOL, NBREQ and SYNC_ID, are tags too, and the stock
//                     model drops them before this guard can read them: a row, TVI-WO-1303.FEEDBACK.md.)
//   TVI-DMA-SHARED    the same id on another channel whose stream is running ("cannot be programmed to different x and y
//                     DMAMUX request multiplexer channels ... except when ... not simultaneously active", p704):
//                     predicted-fault; on one whose stream the twin has completed with EN still set: unmodeled.
//   TVI-DMA-REQUEST   a request outside RaisedToMemory / RaisedFromMemory (below: those a model raises, and those
//                     acknowledged by name for ArduPilot's recorded boot, ruled Q5 (c)). unmodeled.
// Enforcing: the first rule that fires refuses the access. Recorded mode (RUL-19): every rule is evaluated and records what
// it finds, and the access goes on as if none had fired (model/Refusal.cs), with protected fields kept as silicon keeps them.
//
// Modeled (TVI-WO-1305, ruled 2026-11-24T18:55Z Q6): software clearing EN while the stream runs (NDT > 0) suspends it as
// silicon does: TCIF is set "to indicate the end of transfer due to the stream interruption", and the stream's next transfer
// starts at the addresses software programs (§15.3.16 p654; PAR, M0AR p666), where the stock model sets no flag and resumes
// from the offset its earlier transfer reached (STM32DMA.cs L160, L256, L262). The guard reaches the stock model's private
// `transferCompleteIrqStatus` and per-stream `dataOffset` by reflection; a build without them is TVI-DMA-CONFIG.
//   TVI-DMA-OFFSET (narrowed by TVI-WO-1305, which models the offset) a stream enabled again after such a suspension with no
//                     NDTR write since: the manual has software rewrite NDTR (p654), and says NDT reloads after a completed
//                     transfer (p665), so whether the remaining or the programmed count runs is not stated. unmodeled. ArduPilot's USART receive path ends a frame so
// (UARTDriver.cpp rx_irq_cb, rxbuff_full_irq; TVI-WO-1305.measure.md §2.6).
//
// Reads: CR from the guard's copy (every field but EN, which is the stock model's), so a read-modify-write keeps the
// fields the stock model drops (PL, TEIE, DMEIE, TRBUFF; measure.md §2.1); FCR from its copy, reset 0x21 (p667; the stock
// model resets it to 0x00). The rest from the stock model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.DMA;
using Antmicro.Renode.Peripherals.Miscellaneous;

namespace Antmicro.Renode.Peripherals.Tviburi
{
    public class GuardedSTM32DMA : IDoubleWordPeripheral, IWordPeripheral, IBytePeripheral, IKnownSize, IGPIOReceiver,
        INumberedGPIOOutput, IDisposable
    {
        // `dmamux`: the stock DMAMUX1, whose channels route this controller's requests (stm32h7.repl's `dmamux1`).
        // `mode` (RUL-19) is this guard's.
        public GuardedSTM32DMA(IMachine machine, STM32_DMAMUX dmamux = null, string mode = Records.Enforcing)
        {
            this.machine = machine;
            this.dmamux = dmamux;
            inner = new STM32DMA(machine);
            records = new Records(machine, this, nameof(GuardedSTM32DMA), mode);
            ResetCopies();
        }

        public long Size => 0x400;

        // The stock model's stream interrupt lines: the platform's `[0-7] -> nvic@[...]` bind here.
        public IReadOnlyDictionary<int, IGPIO> Connections => inner.Connections;

        // DMAMUX1's request lines for this controller's streams (`dmamux1: [0-7] -> tviburiDma1@[0-7]`).
        public void OnGPIO(int number, bool value)
        {
            inner.OnGPIO(number, value);
        }

        public void Reset()
        {
            inner.Reset();
            ResetCopies();
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

        // A sub-word access never reaches the stock model, which drops it (measure.md D14), in either mode.
        private uint Read(long offset, int width)
        {
            if(Unloaded(offset, 0, width) || Narrow(offset, 0, width, "read"))
            {
                return 0;
            }
            if(RegisterOffset(offset) < 0)
            {
                records.Refuse("TVI-DMA-READ", Unmodeled, "DMA+0x" + offset.ToString("X"), "no register at offset 0x" + offset.ToString("X"),
                    "read of a DMA offset where RM0433 defines no register: the twin cannot say what silicon returns",
                    "RM0433 Rev 9 §15.5.11 Table 117 p668-671", State(Stream(offset)), offset, 0, width);
                return records.Recorded ? inner.ReadDoubleWord(offset) : 0;
            }
            return Served(offset);
        }

        private void Write(long offset, uint value, int width)
        {
            if(Unloaded(offset, value, width) || Narrow(offset, value, width, "write"))
            {
                return;
            }
            var r = RegisterOffset(offset);
            var s = Stream(offset);
            var word = value;
            var refused = false;
            if(r < 0)
            {
                records.Refuse("TVI-DMA-FIELD", Unmodeled, "DMA+0x" + offset.ToString("X"), "no register at offset 0x" + offset.ToString("X"),
                    "write to a DMA offset where RM0433 defines no register: the twin cannot say what silicon does",
                    "RM0433 Rev 9 §15.5.11 Table 117 p668-671", State(s), offset, word, width);
                if(records.Recorded)
                {
                    inner.WriteDoubleWord(offset, word);
                }
                return;
            }
            if(s >= 0)
            {
                refused = RefusedEnabled(offset, s, r, ref word);
            }
            if(!refused || records.Recorded)
            {
                refused |= RefusedField(offset, r, word, width, s);
            }
            if(s >= 0 && r == CrOffset && (word & EnBit) != 0 && !Enabled(s) && (!refused || records.Recorded))
            {
                refused |= RefusedAtEnable(offset, s, word);
            }
            if(refused && !records.Recorded)
            {
                return;
            }
            Pass(offset, s, r, word);
        }

        private bool Unloaded(long offset, ulong value, int width)
        {
            if(records.LoadFailed)
            {
                records.Refuse("TVI-LOAD", Unmodeled, Name(RegisterOffset(offset)), "none", "the guard did not load: every access refuses",
                    Records.LoadCitation, "none", offset, value, width);
                return true;
            }
            var c = Config();
            if(c != null)
            {
                records.Refuse("TVI-DMA-CONFIG", Unmodeled, Name(RegisterOffset(offset)), "none",
                    "the platform gives this DMA guard what it cannot model: " + c + "; every access refuses",
                    "RM0433 Rev 9 Table 8 p134 (DMA1 at 0x40020000, DMA2 at 0x40020400); §17.3.2 p696 (DMAMUX1 channels 0-7 to DMA1, 8-15 to DMA2)",
                    "none", offset, value, width);
                return true;
            }
            return false;
        }

        private bool Narrow(long offset, ulong value, int width, string what)
        {
            if(width == 32 && (offset & 3) == 0)
            {
                return false;
            }
            records.Refuse("TVI-DMA-WIDTH", Unmodeled, Name(RegisterOffset(offset & ~3L)), width + "-bit " + what,
                "a " + width + "-bit DMA " + what + " at offset 0x" + offset.ToString("X")
                    + ": the DMA registers are accessed by words, and the twin cannot say what silicon does with this one",
                "RM0433 Rev 9 §15.5 p659", State(Stream(offset & ~3L)), offset, value, width);
            return true;
        }

        // What a read returns: CR (all but EN) and FCR from the copies, the rest from the stock model.
        private uint Served(long offset)
        {
            var r = RegisterOffset(offset);
            var s = Stream(offset);
            if(r == CrOffset)
            {
                return (cr[s] & ~EnBit) | (inner.ReadDoubleWord(offset) & EnBit);
            }
            if(r == FcrOffset)
            {
                return fcr[s];
            }
            return inner.ReadDoubleWord(offset);
        }

        private void Pass(long offset, int s, long r, uint word)
        {
            if(r == CrOffset)
            {
                cr[s] = word & CrBits;
                // Software clears EN while the stream runs: silicon's suspension (p654), after the stock model takes the write.
                if((word & EnBit) == 0 && Enabled(s) && Items(s) != 0)
                {
                    inner.WriteDoubleWord(offset, word);
                    Suspend(s);
                    return;
                }
            }
            else if(r == FcrOffset)
            {
                fcr[s] = (word & FcrWritable) | FcrEmpty;   // FS is read-only: 100, FIFO empty (p667)
            }
            else if(r == NdtrOffset && !Enabled(s))
            {
                programmed[s] = word & 0xFFFF;   // written only while disabled (p665); a refused write keeps nothing
                suspended[s] = false;
            }
            inner.WriteDoubleWord(offset, word);
        }

        // Software cleared EN while stream s ran (NDT > 0): silicon sets TCIF "to indicate the end of transfer due to the
        // stream interruption", and the next transfer starts at the addresses software programs (§15.3.16 p654; p666). The
        // stock model's own flag and offset, by reflection (checked at load, Check()).
        private void Suspend(int s)
        {
            ((IFlagRegisterField[])IrqStatusField.GetValue(inner))[s].Value = true;
            OffsetField.SetValue(((Array)StreamsField.GetValue(inner)).GetValue(s), 0ul);
            UpdateInterruptsMethod.Invoke(inner, null);
            suspended[s] = true;
        }

        private void ResetCopies()
        {
            for(var i = 0; i < Streams; i++)
            {
                cr[i] = 0;
                fcr[i] = FcrReset;
                programmed[i] = 0;
                suspended[i] = false;
            }
        }

        // ---- stream state --------------------------------------------------------------------------------------

        private bool Enabled(int s)
        {
            return (inner.ReadDoubleWord(StreamBase(s) + CrOffset) & EnBit) != 0;
        }

        private uint Items(int s)
        {
            return inner.ReadDoubleWord(StreamBase(s) + NdtrOffset) & 0xFFFF;
        }

        // 0 off, 1 running, 2 completed in the twin with EN still set.
        public int StreamState(int s)
        {
            return !Enabled(s) ? Off : Items(s) != 0 ? Running : Completed;
        }

        // The DMAMUX1 channel of stream s: DMA1's streams are channels 0-7, DMA2's 8-15 (p696).
        public int ChannelBase { get; private set; } = -1;

        // ---- rules ---------------------------------------------------------------------------------------------

        // TVI-DMA-EN / TVI-DMA-DONE: a protected field changed while the stream is enabled. In recorded mode the word is
        // passed on with the protected fields as silicon keeps them.
        private bool RefusedEnabled(long offset, int s, long r, ref uint word)
        {
            var state = StreamState(s);
            if(state == Off)
            {
                return false;
            }
            var mask = Protected(r);
            var old = Served(offset);
            var changed = (word ^ old) & mask;
            var restart = state == Completed && r == CrOffset && (word & EnBit) != 0 && programmed[s] != 0;
            if(changed == 0 && !restart)
            {
                return false;
            }
            var reg = Name(r);
            var running = state == Running;
            records.Refuse(running ? "TVI-DMA-EN" : "TVI-DMA-DONE", running ? PredictedFault : Unmodeled, reg,
                reg + " bits 0x" + changed.ToString("X8") + " of stream " + s + " (EN = 1, " + (running ? "running" : "completed in the twin") + ")",
                running
                    ? reg + " written with a change to bits that can be written only while the stream is disabled: silicon ignores the write, so the stream does not do what the firmware wrote"
                    : changed == 0
                        ? reg + " written with EN = 1 after the twin completed the transfer: silicon cleared EN at the end of the transfer, so on silicon this write starts a new transfer with NDT reloaded, which the twin does not make (or, as a read-modify-write of the twin's EN, means something else): the twin cannot say"
                        : reg + " written with a change to bits that can be written only while the stream is disabled, after the twin completed the transfer and with EN still set: silicon may still be transferring (the twin completes a memory-to-peripheral transfer at once) or done and disabled, and the twin cannot say which",
                "RM0433 Rev 9 " + Citation(r) + "; §15.5.5 p665 (EN)", State(s), offset, word, 32);
            word = (word & ~mask) | (old & mask);
            return true;
        }

        private static uint Protected(long r)
        {
            switch(r)
            {
            case CrOffset: return CrProtected;
            case NdtrOffset: case ParOffset: case M0arOffset: case M1arOffset: return 0xFFFFFFFF;
            case FcrOffset: return DmdisBit | FthBits;
            }
            return 0;
        }

        private static string Citation(long r)
        {
            switch(r)
            {
            case CrOffset: return "§15.5.5 p662-664";
            case NdtrOffset: return "§15.5.6 p665";
            case ParOffset: return "§15.5.7 p666";
            case M0arOffset: return "§15.5.8 p666";
            case M1arOffset: return "§15.5.9 p666";
            }
            return "§15.5.10 p667";
        }

        private bool RefusedField(long offset, long r, uint word, int width, int s)
        {
            foreach(var f in Fields.Where(x => x.Offset == r))
            {
                var v = f.Of(word);
                if(f.Allowed != null && !f.Allowed.Contains(v))
                {
                    var reg = Name(r);
                    records.Refuse("TVI-DMA-FIELD", Unmodeled, reg, reg + "." + f.Name + " (" + f.Bits + ") = " + f.Format(v),
                        reg + "." + f.Name + " written with a value the model does not implement: the model behaves as " + f.BehavesAs
                            + " (allowed: " + string.Join(", ", f.Allowed.Select(f.Format)) + ")",
                        "RM0433 Rev 9 " + f.Section + " p" + f.Page + "; allow-list row " + f.Register + "." + f.Name + " (TVI-WO-1303.measure.md §5 Q6)",
                        State(s), offset, word, width);
                    return true;
                }
            }
            return false;
        }

        // The checks at the write that sets EN, on the configuration the stream starts with.
        private bool RefusedAtEnable(long offset, int s, uint word)
        {
            var b = StreamBase(s);
            var dir = (word >> 6) & 3;
            var reload = Items(s) == 0 && programmed[s] != 0;
            var items = (ulong)(reload ? programmed[s] : Items(s));
            var size = 1ul << (int)((word >> 11) & 3);
            var refused = false;
            if(((word >> 11) & 3) != ((word >> 13) & 3))
            {
                refused = Refuse(offset, s, word, "TVI-DMA-SIZE", Unmodeled, "DMA_SxCR.PSIZE = " + ((word >> 11) & 3) + ", MSIZE = " + ((word >> 13) & 3),
                    "stream " + s + " enabled with PSIZE and MSIZE different: silicon forces MSIZE to PSIZE in direct mode and runs memory-to-memory in FIFO mode only, while the stock model counts the transfer in MSIZE items, so the twin cannot say what moves",
                    "RM0433 Rev 9 §15.5.5 p663 (MSIZE); §15.3.8 p645 (memory-to-memory: direct mode not allowed); p648 (NDT in PSIZE items)");
                if(!records.Recorded)
                {
                    return true;
                }
            }
            if(suspended[s])
            {
                suspended[s] = false;
                refused |= Refuse(offset, s, word, "TVI-DMA-OFFSET", Unmodeled, "stream " + s + " re-enabled after a suspension, NDTR not rewritten",
                    "stream " + s + " enabled again after software suspended it (EN cleared mid-transfer) with no NDTR write since: RM0433 has software rewrite NDTR before the restart (p654), and NDT reloads the programmed value at a re-enable after a completed transfer (p665), which TCIF declared this one, so the twin cannot say whether silicon moves the remaining items or the programmed count",
                    "RM0433 Rev 9 §15.3.16 p654; §15.5.6 p665 (NDT); TVI-WO-1305 (adversarial pass G2)");
                if(!records.Recorded)
                {
                    return true;
                }
            }
            if(items != 0)
            {
                var par = inner.ReadDoubleWord(b + ParOffset);
                var m0 = inner.ReadDoubleWord(b + M0arOffset);
                refused |= RefusedSpan(offset, s, word, "PAR", par, (word & PincBit) != 0 ? items * size : size);
                if(!refused || records.Recorded)
                {
                    refused |= RefusedSpan(offset, s, word, "M0AR", m0, (word & MincBit) != 0 ? items * size : size);
                }
            }
            if(reload && (!refused || records.Recorded))
            {
                refused |= Refuse(offset, s, word, "TVI-DMA-RELOAD", Unmodeled, "DMA_S" + s + "NDTR.NDT = 0 (programmed " + programmed[s] + ")",
                    "stream " + s + " enabled again with NDT = 0 after " + programmed[s] + " items were programmed: silicon reloads the programmed value when the stream is enabled again and transfers it, and the stock model never reloads, so the twin would move nothing",
                    "RM0433 Rev 9 §15.5.6 p665 (NDT)");
            }
            if(dir != MemoryToMemory && (!refused || records.Recorded))
            {
                refused |= RefusedRoute(offset, s, word, dir);
            }
            return refused;
        }

        private bool RefusedSpan(long offset, int s, uint word, string which, uint start, ulong length)
        {
            var end = start + length - 1;
            string rule = null, kind = null, condition = null, citation = null, region = null;
            if(end > 0xFFFFFFFFul)
            {
                rule = "TVI-DMA-ADDRESS"; kind = Unmodeled; region = "past 0xFFFFFFFF";
                condition = "stream " + s + " enabled with " + which + " = 0x" + start.ToString("X8") + " over " + length
                    + " bytes, which wraps past 0xFFFFFFFF: the twin cannot say what silicon addresses";
                citation = "RM0433 Rev 9 §2.3.2 Table 7 p130-131";
            }
            else if((region = Tcm(start, end)) != null)
            {
                rule = "TVI-DMA-TCM"; kind = PredictedFault;
                condition = "stream " + s + " enabled with " + which + " = 0x" + start.ToString("X8") + " over " + length + " bytes, in " + region
                    + ", which DMA1 and DMA2 cannot reach: their memory and peripheral buses access all internal memories except ITCM and DTCM, so silicon does not move this data";
                citation = "RM0433 Rev 9 §2.1.6 p108 (DMA1 and DMA2 controllers); §2.3.2 Table 7 p131 (ITCM, DTCM)";
            }
            else if((region = Unmapped(start, end)) != null)
            {
                rule = "TVI-DMA-ADDRESS"; kind = Unmodeled;
                condition = "stream " + s + " enabled with " + which + " = 0x" + start.ToString("X8") + " over " + length + " bytes, with a byte at "
                    + region + ", in no region the memory map maps: the twin cannot say what silicon does";
                citation = "RM0433 Rev 9 §2.3.2 Table 7 p130-131";
            }
            if(rule == null)
            {
                return false;
            }
            return Refuse(offset, s, word, rule, kind, "DMA_S" + s + which + " = 0x" + start.ToString("X8") + " (" + region + ")", condition, citation);
        }

        private static string Tcm(ulong start, ulong end)
        {
            if(start <= ItcmEnd)
            {
                return "ITCM (0x00000000-0x0000FFFF)";
            }
            if(start <= DtcmEnd && end >= DtcmStart)
            {
                return "DTCM (0x20000000-0x2001FFFF)";
            }
            return null;
        }

        // The first byte of [start, end] in no region Table 7 maps (p130-131), as hex; null when every byte is mapped.
        private static string Unmapped(ulong start, ulong end)
        {
            var a = start;
            while(a <= end)
            {
                var m = Mapped.FirstOrDefault(x => x.Item1 <= a && a <= x.Item2);
                if(m == null)
                {
                    return "0x" + a.ToString("X8");
                }
                a = m.Item2 + 1;
            }
            return null;
        }

        private bool RefusedRoute(long offset, int s, uint word, uint dir)
        {
            var ch = ChannelBase + s;
            var cxcr = dmamux.ReadDoubleWord(4 * ch);
            var id = cxcr & 0x7F;
            var where = "DMAMUX1_C" + ch + "CR.DMAREQ_ID = " + id;
            var p2m = dir == PeripheralToMemory;
            if(id == 0 || id == 54 || id >= 116)
            {
                return Refuse(offset, s, word, "TVI-DMA-ROUTE", PredictedFault, where + (id == 0 ? " (no request)" : " (reserved)"),
                    "stream " + s + " enabled " + (p2m ? "peripheral-to-memory" : "memory-to-peripheral") + " on DMAMUX1 channel " + ch + ", which "
                        + (id == 0 ? "selects no DMA request line" : "selects a reserved request") + ": no request reaches the stream on silicon, so it moves nothing"
                        + (p2m ? "" : ", where the twin moves the whole transfer at once"),
                    id == 0 ? "RM0433 Rev 9 §17.4.2 p703 (the null value selects no request line)" : "RM0433 Rev 9 §17.3.2 Table 124 p697-698 (Reserved)");
            }
            var refused = false;
            if(id <= 8)
            {
                refused = Refuse(offset, s, word, "TVI-DMA-MUX", Unmodeled, where + " (" + RequestName(id) + ")",
                    "stream " + s + " enabled on DMAMUX1 channel " + ch + " with request generator " + (id - 1)
                        + ": DMAMUX1's request generators are stock tags, so the twin cannot say when requests arrive",
                    "RM0433 Rev 9 §17.3.2 Table 124 p697; §17.6.7 p712 (DMAMUX1_RGxCR)");
                if(!records.Recorded)
                {
                    return true;
                }
            }
            for(var other = 0; other < 16; other++)
            {
                if(other == ch || (dmamux.ReadDoubleWord(4 * other) & 0x7F) != id)
                {
                    continue;
                }
                var state = OtherState(other);
                if(state == Off)
                {
                    continue;
                }
                var running = state == Running;
                refused |= Refuse(offset, s, word, "TVI-DMA-SHARED", running ? PredictedFault : Unmodeled, where + " on channels " + ch + " and " + other,
                    "stream " + s + " enabled on DMAMUX1 channel " + ch + " with the request channel " + other + " also selects, while that channel's stream "
                        + (running ? "is running: both streams answer one request line, which RM0433 forbids unless they are never active together"
                                   : "has been completed by the twin with EN still set: whether it is still active on silicon, the twin cannot say"),
                    "RM0433 Rev 9 §17.4.2 p704 (Caution)");
                if(refused && !records.Recorded)
                {
                    return true;
                }
            }
            var raised = p2m ? RaisedToMemory : RaisedFromMemory;
            if(!raised.Contains(id))
            {
                refused |= Refuse(offset, s, word, "TVI-DMA-REQUEST", Unmodeled, where + " (" + RequestName(id) + ")",
                    "stream " + s + " enabled " + (p2m ? "peripheral-to-memory" : "memory-to-peripheral") + " on request " + id + " (" + RequestName(id) + "), which no model "
                        + (p2m ? "raises, so the twin would move no data" : "is acknowledged for: the twin would move the whole transfer at once, unpaced by any request")
                        + "; the twin cannot say what silicon moves when",
                    "RM0433 Rev 9 §17.3.2 Table 124 p697-698; TVI-WO-1303.measure.md §2.2, §5 Q5 (c)");
            }
            return refused;
        }

        private bool Refuse(long offset, int s, uint word, string rule, string kind, string bit, string condition, string citation)
        {
            records.Refuse(rule, kind, "DMA_SxCR", bit, condition, citation, State(s), offset, word, 32);
            return true;
        }

        // The state of the stream DMAMUX1 channel c serves, from the guard of the controller it belongs to; off when that
        // controller has no guard on this machine.
        private int OtherState(int c)
        {
            if(c / Streams == ChannelBase / Streams)
            {
                return StreamState(c % Streams);
            }
            var other = (machine as Machine)?.GetPeripheralsOfType<GuardedSTM32DMA>().FirstOrDefault(g => g != this && g.ChannelBase == c - c % Streams);
            return other == null ? Off : other.StreamState(c % Streams);
        }

        private static string RequestName(uint id)
        {
            return id < RequestNames.Length ? RequestNames[id] : "reserved";
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
            if(dmamux == null)
            {
                return "no `dmamux` (the stock DMAMUX1), so no request route can be read";
            }
            if(StreamsField == null || IrqStatusField == null || UpdateInterruptsMethod == null || OffsetField == null)
            {
                return "this Renode build's STM32DMA lacks the private members a suspension is modeled through (streams, "
                    + "transferCompleteIrqStatus, UpdateInterrupts, Stream.dataOffset), so a software disable cannot be modeled";
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
            if(start != Dma1Base && start != Dma2Base)
            {
                return "bus address 0x" + start.ToString("X") + ", which is neither DMA1's nor DMA2's";
            }
            ChannelBase = start == Dma1Base ? 0 : Streams;
            return "";
        }

        private string State(int s)
        {
            if(s < 0)
            {
                return "streams " + string.Concat(Enumerable.Range(0, Streams).Select(i => "SOR"[StreamState(i)]));
            }
            var b = StreamBase(s);
            return "S" + s + "CR=0x" + Served(b + CrOffset).ToString("X") + " NDTR=0x" + Items(s).ToString("X")
                + " PAR=0x" + inner.ReadDoubleWord(b + ParOffset).ToString("X") + " M0AR=0x" + inner.ReadDoubleWord(b + M0arOffset).ToString("X")
                + " state=" + new[] { "off", "running", "completed" }[StreamState(s)]
                + (ChannelBase < 0 || dmamux == null ? "" : " DMAMUX1_C" + (ChannelBase + s) + "CR=0x" + dmamux.ReadDoubleWord(4 * (ChannelBase + s)).ToString("X"));
        }

        // The register an offset decodes to, as stream 0's offset (0x00-0x24), or -1 when there is none.
        private static long RegisterOffset(long offset)
        {
            if(offset < 0 || offset >= StreamEnd)
            {
                return -1;
            }
            return offset < CrOffset ? offset & ~3L : CrOffset + (offset - CrOffset) % StreamStep;
        }

        private static int Stream(long offset)
        {
            return offset < CrOffset || offset >= StreamEnd ? -1 : (int)((offset - CrOffset) / StreamStep);
        }

        private static long StreamBase(int s)
        {
            return s * StreamStep;
        }

        private static string Name(long r)
        {
            var f = Fields.FirstOrDefault(x => x.Offset == r);
            return f != null ? f.Register : "none";
        }

        private readonly IMachine machine;
        private readonly STM32_DMAMUX dmamux;
        private readonly STM32DMA inner;
        private readonly Records records;
        private readonly uint[] cr = new uint[Streams];
        private readonly uint[] fcr = new uint[Streams];
        // The NDT software last wrote to each stream, which silicon reloads at a re-enable (p665).
        private readonly uint[] programmed = new uint[Streams];
        // Suspended by software (Suspend) with no NDTR write since: TVI-DMA-OFFSET at the next enable.
        private readonly bool[] suspended = new bool[Streams];
        private string config;

        private const int Streams = 8;
        private const int Off = 0;
        private const int Running = 1;
        private const int Completed = 2;
        private const long StreamStep = 0x18;
        private const long StreamEnd = 0x10 + 8 * 0x18;
        private const long CrOffset = 0x10;
        private const long NdtrOffset = 0x14;
        private const long ParOffset = 0x18;
        private const long M0arOffset = 0x1C;
        private const long M1arOffset = 0x20;
        private const long FcrOffset = 0x24;
        private const uint EnBit = 1;
        private const uint PincBit = 1u << 9;
        private const uint MincBit = 1u << 10;
        private const uint DmdisBit = 1u << 2;
        private const uint FthBits = 3;
        private const uint CrBits = 0x01FFFFFF;
        // MBURST, PBURST, CT, DBM, PL, PINCOS, MSIZE, PSIZE, MINC, PINC, DIR, PFCTRL (pp. 662-664).
        private const uint CrProtected = 0x01EFFEE0;
        private const uint FcrWritable = 0x87;
        private const uint FcrEmpty = 0x20;
        private const uint FcrReset = 0x21;
        private const uint PeripheralToMemory = 0;
        private const uint MemoryToMemory = 2;
        private const ulong Dma1Base = 0x40020000;
        private const ulong Dma2Base = 0x40020400;
        private const ulong ItcmEnd = 0x0000FFFF;
        private const ulong DtcmStart = 0x20000000;
        private const ulong DtcmEnd = 0x2001FFFF;

        private const string Unmodeled = "unmodeled";
        private const string PredictedFault = "predicted-fault";

        // The regions Table 7 maps (RM0433 Rev 9 p130-131) besides the TCMs, which TVI-DMA-TCM judges first. System memory
        // bank 2 (0x1FF40000-0x1FF5FFFF) is left out: p131 prints it inside a Reserved row (0x1FF20000-0x1FFFFFFF), so the
        // twin cannot say which holds, and a span there is unmodeled (a FEEDBACK row). 0x40000000-
        // 0x5FFFFFFF is the peripheral area as a whole (Table 8 p134 lists its holes, which this rule does not).
        private static readonly Tuple<ulong, ulong>[] Mapped =
        {
            Tuple.Create(0x08000000ul, 0x081FFFFFul), Tuple.Create(0x1FF00000ul, 0x1FF1FFFFul), Tuple.Create(0x24000000ul, 0x2407FFFFul),
            Tuple.Create(0x30000000ul, 0x30047FFFul), Tuple.Create(0x38000000ul, 0x3800FFFFul), Tuple.Create(0x38800000ul, 0x38800FFFul),
            Tuple.Create(0x40000000ul, 0x5FFFFFFFul), Tuple.Create(0x60000000ul, 0x9FFFFFFFul), Tuple.Create(0xC0000000ul, 0xDFFFFFFFul),
        };

        // Requests a model raises (measure.md §2.2): 37, 39 SPI1/SPI2 RX (wired by the consumer's platforms), 77 cryp, 83 spi4,
        // 9, 115 the ADCs; and those acknowledged by name for ArduPilot's recorded boot (ruled Q5 (c)): 41 usart1_rx_dma
        // (no model raised it then), and memory-to-peripheral 38, 40, 42 (moves at EN, unpaced).
        // TVI-WO-1305 (ruled 2026-11-24T18:55Z Q6): 41, 43 and 45 (usart1/2/3_rx_dma) are raised by the USART guard where the
        // platform wires its ReceiveDmaRequest, and move nothing where it does not; 46 (usart3_tx_dma) is the armed run's.
        private static readonly uint[] RaisedToMemory = { 9, 37, 39, 41, 43, 45, 77, 83, 115 };
        private static readonly uint[] RaisedFromMemory = { 38, 40, 42, 46 };

        // The stock STM32DMA's private members a suspension is modeled through (STM32DMA.cs L118-136, L160, L256, L262).
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo StreamsField = typeof(STM32DMA).GetField("streams", Private);
        private static readonly FieldInfo IrqStatusField = typeof(STM32DMA).GetField("transferCompleteIrqStatus", Private);
        private static readonly MethodInfo UpdateInterruptsMethod = typeof(STM32DMA).GetMethod("UpdateInterrupts", Private);
        private static readonly FieldInfo OffsetField = StreamsField?.FieldType.GetElementType()?.GetField("dataOffset", Private);

        // RM0433 Rev 9 Table 124 (p697-698), by DMAREQ_ID.
        private static readonly string[] RequestNames =
        {
            "none", "dmamux1_req_gen0", "dmamux1_req_gen1", "dmamux1_req_gen2", "dmamux1_req_gen3", "dmamux1_req_gen4", "dmamux1_req_gen5",
            "dmamux1_req_gen6", "dmamux1_req_gen7", "adc1_dma", "adc2_dma", "TIM1_CH1", "TIM1_CH2", "TIM1_CH3", "TIM1_CH4", "TIM1_UP",
            "TIM1_TRIG", "TIM1_COM", "TIM2_CH1", "TIM2_CH2", "TIM2_CH3", "TIM2_CH4", "TIM2_UP", "TIM3_CH1", "TIM3_CH2", "TIM3_CH3",
            "TIM3_CH4", "TIM3_UP", "TIM3_TRIG", "TIM4_CH1", "TIM4_CH2", "TIM4_CH3", "TIM4_UP", "i2c1_rx_dma", "i2c1_tx_dma",
            "i2c2_rx_dma", "i2c2_tx_dma", "spi1_rx_dma", "spi1_tx_dma", "spi2_rx_dma", "spi2_tx_dma", "usart1_rx_dma", "usart1_tx_dma",
            "usart2_rx_dma", "usart2_tx_dma", "usart3_rx_dma", "usart3_tx_dma", "TIM8_CH1", "TIM8_CH2", "TIM8_CH3", "TIM8_CH4",
            "TIM8_UP", "TIM8_TRIG", "TIM8_COM", "reserved", "TIM5_CH1", "TIM5_CH2", "TIM5_CH3", "TIM5_CH4", "TIM5_UP", "TIM5_TRIG",
            "spi3_rx_dma", "spi3_tx_dma", "uart4_rx_dma", "uart4_tx_dma", "uart5_rx_dma", "uart5_tx_dma", "dac_ch1_dma", "dac_ch2_dma",
            "TIM6_UP", "TIM7_UP", "usart6_rx_dma", "usart6_tx_dma", "i2c3_rx_dma", "i2c3_tx_dma", "dcmi_dma", "cryp_in_dma",
            "cryp_out_dma", "hash_in_dma", "uart7_rx_dma", "uart7_tx_dma", "uart8_rx_dma", "uart8_tx_dma", "spi4_rx_dma", "spi4_tx_dma",
            "spi5_rx_dma", "spi5_tx_dma", "sai1a_dma", "sai1b_dma", "sai2a_dma", "sai2b_dma", "swpmi_rx_dma", "swpmi_tx_dma",
            "spdifrx_dat_dma", "spdifrx_ctrl_dma", "HR_REQ(1)", "HR_REQ(2)", "HR_REQ(3)", "HR_REQ(4)", "HR_REQ(5)", "HR_REQ(6)",
            "dfsdm1_dma0", "dfsdm1_dma1", "dfsdm1_dma2", "dfsdm1_dma3", "TIM15_CH1", "TIM15_UP", "TIM15_TRIG", "TIM15_COM",
            "TIM16_CH1", "TIM16_UP", "TIM17_CH1", "TIM17_UP", "sai3_a_dma", "sai3_b_dma", "adc3_dma",
        };

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
        private static readonly uint[] Sizes = { 0, 1, 2 };
        private static readonly uint[] Directions = { 0, 1, 2 };

        private static Row R(string reg, long off, string sec, string name, int msb, int lsb, uint[] allowed, string cls, string behaves, int page)
        {
            return new Row(reg, off, sec, name, msb, lsb, allowed, behaves, page);
        }

        // Generated by tests/1303/fields.py from RM0433 Rev 9 §15.5 (pp. 659-667), with the classes ruled 2026-11-24T14:10Z.
        // Stream registers at stream 0's offset; the guard applies each row to all eight streams.
        private static readonly Row[] Fields =
        {
            R("DMA_LISR", 0x00, "§15.5.1", "Res31_28", 31, 28, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TCIF3", 27, 27, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "HTIF3", 26, 26, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TEIF3", 25, 25, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "DMEIF3", 24, 24, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "FEIF3", 22, 22, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TCIF2", 21, 21, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "HTIF2", 20, 20, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TEIF2", 19, 19, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "DMEIF2", 18, 18, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "FEIF2", 16, 16, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "Res15_12", 15, 12, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TCIF1", 11, 11, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "HTIF1", 10, 10, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TEIF1", 9, 9, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "DMEIF1", 8, 8, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "FEIF1", 6, 6, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TCIF0", 5, 5, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "HTIF0", 4, 4, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "TEIF0", 3, 3, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "DMEIF0", 2, 2, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "Res1", 1, 1, Zero, "reserved", "reserved: must be kept at reset value", 659),
            R("DMA_LISR", 0x00, "§15.5.1", "FEIF0", 0, 0, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 659),
            R("DMA_HISR", 0x04, "§15.5.2", "Res31_28", 31, 28, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TCIF7", 27, 27, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "HTIF7", 26, 26, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TEIF7", 25, 25, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "DMEIF7", 24, 24, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "FEIF7", 22, 22, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TCIF6", 21, 21, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "HTIF6", 20, 20, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TEIF6", 19, 19, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "DMEIF6", 18, 18, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "FEIF6", 16, 16, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "Res15_12", 15, 12, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TCIF5", 11, 11, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "HTIF5", 10, 10, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TEIF5", 9, 9, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "DMEIF5", 8, 8, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "FEIF5", 6, 6, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TCIF4", 5, 5, Any, "read-only", "read-only (silicon ignores a write; so does the guard's stock model): set when the stock model completes a transfer (probed, TVI-WO-1303.measure.md D2), and by the guard when software clears EN while the stream runs (p654; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "HTIF4", 4, 4, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "TEIF4", 3, 3, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "DMEIF4", 2, 2, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "Res1", 1, 1, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_HISR", 0x04, "§15.5.2", "FEIF4", 0, 0, Any, "read-only", "read-only (silicon ignores a write): the stock model never sets it; silicon sets HTIF at half transfer and TEIF, DMEIF, FEIF on errors, none modeled", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res31_28", 31, 28, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTCIF3", 27, 27, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CHTIF3", 26, 26, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTEIF3", 25, 25, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CDMEIF3", 24, 24, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CFEIF3", 22, 22, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTCIF2", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CHTIF2", 20, 20, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTEIF2", 19, 19, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CDMEIF2", 18, 18, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CFEIF2", 16, 16, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res15_12", 15, 12, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTCIF1", 11, 11, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CHTIF1", 10, 10, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTEIF1", 9, 9, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CDMEIF1", 8, 8, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CFEIF1", 6, 6, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTCIF0", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CHTIF0", 4, 4, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CTEIF0", 3, 3, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CDMEIF0", 2, 2, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "Res1", 1, 1, Zero, "reserved", "reserved: must be kept at reset value", 660),
            R("DMA_LIFCR", 0x08, "§15.5.3", "CFEIF0", 0, 0, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 660),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res31_28", 31, 28, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTCIF7", 27, 27, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CHTIF7", 26, 26, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTEIF7", 25, 25, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CDMEIF7", 24, 24, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res23", 23, 23, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CFEIF7", 22, 22, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTCIF6", 21, 21, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CHTIF6", 20, 20, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTEIF6", 19, 19, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CDMEIF6", 18, 18, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res17", 17, 17, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CFEIF6", 16, 16, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res15_12", 15, 12, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTCIF5", 11, 11, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CHTIF5", 10, 10, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTEIF5", 9, 9, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CDMEIF5", 8, 8, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res7", 7, 7, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CFEIF5", 6, 6, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTCIF4", 5, 5, Any, "inherited, unaudited (acknowledged by name)", "the stock model: writing 1 clears TCIFx and its interrupt (probed, TVI-WO-1303.measure.md D2; ArduPilot writes it)", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CHTIF4", 4, 4, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CTEIF4", 3, 3, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CDMEIF4", 2, 2, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "Res1", 1, 1, Zero, "reserved", "reserved: must be kept at reset value", 661),
            R("DMA_HIFCR", 0x0C, "§15.5.4", "CFEIF4", 0, 0, Any, "stock, wrong (the stock model never sets HTIF, TEIF, DMEIF or FEIF, so clearing one changes nothing, where silicon sets HTIF at half transfer and the others on errors; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes them)", "a stock tag: dropped; the flag it clears never sets in the twin", 661),
            R("DMA_SxCR", 0x10, "§15.5.5", "Res31_25", 31, 25, Zero, "reserved", "reserved: must be kept at reset value", 662),
            R("DMA_SxCR", 0x10, "§15.5.5", "MBURST[1:0]", 24, 23, Zero, "tag", "00 only: a stock tag; burst transfers are not modeled", 662),
            R("DMA_SxCR", 0x10, "§15.5.5", "PBURST[1:0]", 22, 21, Zero, "tag", "00 only: a stock tag; burst transfers are not modeled", 662),
            R("DMA_SxCR", 0x10, "§15.5.5", "TRBUFF", 20, 20, Any, "stock, wrong (TRBUFF is a stock tag: bufferable transfers are not modeled, and its value is kept by the guard and served on read; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes it)", "kept by the guard and served on read; no effect modeled", 662),
            R("DMA_SxCR", 0x10, "§15.5.5", "CT", 19, 19, Zero, "tag", "0 only: a stock tag; double-buffer mode is not modeled", 662),
            R("DMA_SxCR", 0x10, "§15.5.5", "DBM", 18, 18, Zero, "tag", "0 only: a stock tag; double-buffer mode is not modeled", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "PL[1:0]", 17, 16, Any, "stock, wrong (PL is a stock tag: no priority between streams is modeled, and its value is kept by the guard and served on read; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes it)", "kept by the guard and served on read; no effect modeled; protected under EN (TVI-DMA-EN)", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "PINCOS", 15, 15, Zero, "tag", "0 only: a stock tag; the fixed 4-byte peripheral increment is not modeled", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "MSIZE[1:0]", 14, 13, Sizes, "inherited, unaudited (acknowledged by name)", "the stock model: the memory data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "PSIZE[1:0]", 12, 11, Sizes, "inherited, unaudited (acknowledged by name)", "the stock model: the peripheral data size, byte, half-word or word (probed: word D2, half-word msize16, byte ArduPilot); 11 is reserved; protected under EN", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "MINC", 10, 10, Any, "inherited, unaudited (acknowledged by name)", "the stock model: memory address increment (probed, D2); protected under EN", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "PINC", 9, 9, Any, "inherited, unaudited (acknowledged by name)", "the stock model: peripheral address increment (probed, D2); protected under EN", 663),
            R("DMA_SxCR", 0x10, "§15.5.5", "CIRC", 8, 8, Zero, "tag", "0 only: a stock tag; circular mode is not modeled", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "DIR[1:0]", 7, 6, Directions, "stock, wrong (a memory-to-peripheral transfer completes at the EN write, unpaced by any request, where silicon moves one item per request: acknowledged for requests 38 spi1_tx_dma, 40 spi2_tx_dma, 42 usart1_tx_dma and 46 usart3_tx_dma, as ArduPilot's recorded boot and armed run use them; a peripheral-to-memory stream on request 41 usart1_rx_dma, 43 usart2_rx_dma or 45 usart3_rx_dma moves the bytes the USART guard's ReceiveDmaRequest raises where the platform wires it, and nothing where it does not: acknowledged by name; any other request no model raises refuses TVI-DMA-REQUEST; ruled 2026-11-24T14:10Z Q5 (c); 43, 45, 46 ruled 2026-11-24T18:55Z Q6)", "the stock model: 00 peripheral-to-memory, 01 memory-to-peripheral, 10 memory-to-memory (probed, D2, D9, D11); 11 is reserved; protected under EN", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "PFCTRL", 5, 5, Zero, "tag", "0 only: a stock tag; the peripheral as flow controller is not modeled", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "TCIE", 4, 4, Any, "inherited, unaudited (acknowledged by name)", "the stock model: the transfer-complete interrupt (probed, D2; ArduPilot's streams take it)", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "HTIE", 3, 3, Zero, "tag", "0 only: a stock tag; the half-transfer flag never sets in the twin", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "TEIE", 2, 2, Any, "stock, wrong (TEIE is a stock tag: no transfer error is modeled, so its interrupt never fires; its value is kept by the guard and served on read; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes it)", "kept by the guard and served on read; no transfer error is modeled", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "DMEIE", 1, 1, Any, "stock, wrong (DMEIE is a stock tag: no direct-mode error is modeled, so its interrupt never fires; its value is kept by the guard and served on read; acknowledged by name, ruled 2026-11-24T14:10Z Q6; ArduPilot writes it)", "kept by the guard and served on read; no direct-mode error is modeled", 664),
            R("DMA_SxCR", 0x10, "§15.5.5", "EN", 0, 0, Any, "modeled by the guard", "the stock model starts the stream; at its rise the guard checks its sizes (TVI-DMA-SIZE, p663), its addresses (TVI-DMA-TCM, TVI-DMA-ADDRESS, p108, Table 7 p131), a reload (TVI-DMA-RELOAD, p665) and its DMAMUX1 request (TVI-DMA-ROUTE, TVI-DMA-SHARED, TVI-DMA-REQUEST, TVI-DMA-MUX); while it is set, a changed protected field refuses (TVI-DMA-EN running, TVI-DMA-DONE once the twin completed the transfer); the stock model never clears it at the end of a transfer (stock-wrong, p665); cleared by software while the stream runs, the guard sets TCIF and restarts the stream's next transfer at its programmed addresses, as silicon's suspension does (p654, p666; TVI-WO-1305, ruled 2026-11-24T18:55Z Q6)", 665),
            R("DMA_SxNDTR", 0x14, "§15.5.6", "Res31_16", 31, 16, Zero, "reserved", "reserved: must be kept at reset value", 665),
            R("DMA_SxNDTR", 0x14, "§15.5.6", "NDT[15:0]", 15, 0, Any, "inherited, unaudited (acknowledged by name)", "the stock model: the number of items, counted down by its transfers and never reloaded (probed, D2); protected under EN", 665),
            R("DMA_SxPAR", 0x18, "§15.5.7", "PAR[31:0]", 31, 0, Any, "modeled by the guard", "the stock model's peripheral address (probed, D2, D9); at EN the guard refuses a span in ITCM or DTCM (p108) and one in no mapped region (Table 7 p131); protected under EN", 666),
            R("DMA_SxM0AR", 0x1C, "§15.5.8", "M0A[31:0]", 31, 0, Any, "modeled by the guard", "the stock model's memory address (probed, D2); at EN the guard refuses a span in ITCM or DTCM (p108) and one in no mapped region (Table 7 p131); protected under EN", 666),
            R("DMA_SxM1AR", 0x20, "§15.5.9", "M1A[31:0]", 31, 0, Zero, "tag", "0 only: a stock tag that reads 0; double-buffer mode is not modeled", 666),
            R("DMA_SxFCR", 0x24, "§15.5.10", "Res31_8", 31, 8, Zero, "reserved", "reserved: must be kept at reset value", 667),
            R("DMA_SxFCR", 0x24, "§15.5.10", "FEIE", 7, 7, Zero, "tag", "0 only: a stock tag; FIFO errors are not modeled", 667),
            R("DMA_SxFCR", 0x24, "§15.5.10", "Res6", 6, 6, Zero, "reserved", "reserved: must be kept at reset value", 667),
            R("DMA_SxFCR", 0x24, "§15.5.10", "FS[2:0]", 5, 3, Any, "read-only", "read-only: served by the guard as 100 (FIFO empty, the reset value; not relevant in direct mode); the stock model reads 000", 667),
            R("DMA_SxFCR", 0x24, "§15.5.10", "DMDIS", 2, 2, Zero, "inherited, unaudited (acknowledged by name)", "0 only: direct mode; FIFO mode is not audited; protected under EN", 667),
            R("DMA_SxFCR", 0x24, "§15.5.10", "FTH[1:0]", 1, 0, Any, "stock, wrong (the stock model resets FCR to 0x00 where silicon resets it to 0x21 (p667); the guard serves FCR from its copy, reset 0x21)", "kept by the guard and served on read; not used in direct mode (p667), the only mode allowed (DMDIS = 0); protected under EN", 667),
        };
    }
}
