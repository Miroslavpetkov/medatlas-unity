# Converts an HRT1 pack (float32, from build_pack.py / build_heart.py) into the compact HRT2 format the app loads
# (int16-quantized positions with per-part min/max, int8 normals, uint16/int32 indices, quantized blend shapes).
#   python Tools/hrt1_to_hrt2.py <in HRT1> <out .wasm>
import struct, sys, array

src, dst = sys.argv[1], sys.argv[2]
d = open(src, 'rb').read()
o = 0
def rd(fmt):
    global o
    v = struct.unpack_from(fmt, d, o); o += struct.calcsize(fmt); return v
def rstr():
    (n,) = rd('<i'); global o
    s = d[o:o + n]; o += n; return s
def rfloats(n):
    global o
    a = array.array('f'); a.frombytes(d[o:o + 4 * n]); o += 4 * n; return a
def rints(n):
    global o
    a = array.array('i'); a.frombytes(d[o:o + 4 * n]); o += 4 * n; return a

assert d[:4] == b'HRT1'; o = 4
(nparts,) = rd('<i')
out = bytearray(b'HRT2') + struct.pack('<i', nparts)
def wstr(b): out.extend(struct.pack('<i', len(b))); out.extend(b)
def q16(vals, scale):
    inv = 32767.0 / scale if scale > 0 else 0
    return array.array('h', [max(-32767, min(32767, int(round(v * inv)))) for v in vals]).tobytes()

for _ in range(nparts):
    name = rstr(); cls = rstr()
    nv, nt = rd('<ii')
    pos = rfloats(nv * 3); nrm = rfloats(nv * 3); idx = rints(nt * 3)
    (nk,) = rd('<i')
    keys = []
    for _k in range(nk):
        kn = rstr(); dp = rfloats(nv * 3); dn = rfloats(nv * 3); keys.append((kn, dp, dn))
    wstr(name); wstr(cls)
    out.extend(struct.pack('<ii', nv, nt))
    mn = [min(pos[i::3]) for i in range(3)]; mx = [max(pos[i::3]) for i in range(3)]
    out.extend(struct.pack('<6f', *mn, *mx))
    qp = []
    for i in range(nv):
        for c in range(3):
            r = mx[c] - mn[c]
            qp.append(int(round(((pos[i * 3 + c] - mn[c]) / r if r > 0 else 0) * 65535)) - 32768)
    out.extend(array.array('h', qp).tobytes())
    out.extend(array.array('b', [max(-127, min(127, int(round(v * 127)))) for v in nrm]).tobytes())
    if nv < 65536:
        out.extend(b'\x02'); out.extend(array.array('H', idx).tobytes())
    else:
        out.extend(b'\x04'); out.extend(idx.tobytes())
    out.extend(struct.pack('<i', nk))
    for kn, dp, dn in keys:
        wstr(kn)
        sp = max(abs(v) for v in dp) or 1e-9
        sn = max(abs(v) for v in dn) or 1e-9
        out.extend(struct.pack('<ff', sp, sn))
        out.extend(q16(dp, sp)); out.extend(q16(dn, sn))
open(dst, 'wb').write(out)
print('HRT2 bytes', len(out), 'from', len(d))
