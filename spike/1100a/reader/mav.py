"""ArduPilot's MAVLink 2 replies from a console's usart1 TDR access lines (no CRC check; framing only). Usage: mav.py CONSOLE [PORT]"""
import re, sys, collections
port = sys.argv[2] if len(sys.argv) > 2 else 'usart1'
pat = re.compile(r'^\r?\[(\d\d):(\d\d):(\d\d\.\d+)\] \[INFO\] ' + port + r': \[cpu: [0-9A-Fx]+\] WriteByte to 0x28 \(TransmitData\), value 0x([0-9A-F]+)')
data = []
for l in open(sys.argv[1], errors='replace'):
    m = pat.match(l)
    if m: data.append((int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)), int(m.group(4), 16)))
b = bytes(v for _, v in data); i = 0; msgs = []
NAMES = {0: 'HEARTBEAT', 22: 'PARAM_VALUE', 77: 'COMMAND_ACK', 253: 'STATUSTEXT', 36: 'SERVO_OUTPUT_RAW'}
while i < len(b) - 12:
    if b[i] != 0xFD: i += 1; continue
    n = b[i + 1]; msgid = b[i + 7] | b[i + 8] << 8 | b[i + 9] << 16; payload = b[i + 10:i + 10 + n]
    msgs.append((data[i][0], msgid, payload)); i += 12 + n + (13 if b[i + 2] & 1 else 0)
c = collections.Counter(NAMES.get(m, m) for _, m, _ in msgs)
print(len(b), 'bytes,', len(msgs), 'frames:', dict(c.most_common(8)))
for t, m, p in msgs:
    if m == 77:
        print('  %.3f COMMAND_ACK command %d result %d' % (t, p[0] | p[1] << 8, p[2] if len(p) > 2 else 0))
    elif m == 22:
        print('  %.3f PARAM_VALUE %s' % (t, p[8:24].split(b'\0')[0].decode(errors='replace')))
    elif m == 253:
        print('  %.3f STATUSTEXT %s' % (t, p[1:51].split(b'\0')[0].decode(errors='replace')))
hb = [(t, p[6] if len(p) > 6 else 0) for t, m, p in msgs if m == 0]
armed = [t for t, bm in hb if bm & 0x80]
print('heartbeats', len(hb), '; armed (base_mode & 0x80) from', '%.3f' % armed[0] if armed else 'never', '; base_mode values', sorted(set(bm for _, bm in hb)))
