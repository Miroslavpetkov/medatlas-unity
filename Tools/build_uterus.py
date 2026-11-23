# Female internal genital organs pack for the cycle / pregnancy simulations (organ_uterus.wasm, HRT2 format).
#
# Z-Anatomy has no uterus or ovaries, so this pack is built from the HRA 3D reference organs of the
# Human Reference Atlas, made from the NLM Visible Human female and published on NIH 3D under CC BY 4.0:
#   uterus      https://3d.nih.gov/entries/3DPX-020996  (cdn.humanatlas.io ref-organ/uterus-female)
#   left ovary  https://3d.nih.gov/entries/3DPX-020979  (ref-organ/ovary-female-left)
#   right ovary https://3d.nih.gov/entries/3DPX-020980  (ref-organ/ovary-female-right)
# The uterine tubes are not in the reference organs; they are generated here as simple curved tubes from the
# cornua of the uterus to the ovaries (schematic, not anatomical data). Credit: see docs/ATTRIBUTION.md.
#
#   python3 Tools/build_uterus.py <dir with uterus.glb ovary-left.glb ovary-right.glb> <out organ_uterus.wasm>
# Needs only numpy. The HRA files are Z-up in metres, like Z-Anatomy in Blender, so the axes are mapped the
# same way as Tools/build_pack.py: Unity (x, y, z) = (x, z, y), scaled so the pack is ~3 units across.
import json, math, struct, sys
import numpy as np

SRC, OUT = sys.argv[1], sys.argv[2]

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def glb(path):
    b = open(path, 'rb').read()
    jl = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + jl])
    off = 20 + jl
    bl = struct.unpack('<I', b[off:off + 4])[0]
    bin_ = b[off + 8:off + 8 + bl]

    def acc(i):
        a = j['accessors'][i]
        v = j['bufferViews'][a['bufferView']]
        dt = CT[a['componentType']]
        n = a['count'] * NC[a['type']]
        start = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        arr = np.frombuffer(bin_, dtype=dt, count=n, offset=start)
        return arr.reshape(a['count'], NC[a['type']]) if NC[a['type']] > 1 else arr

    meshes = {}
    for m in j['meshes']:
        P, I = [], []
        base = 0
        for p in m['primitives']:
            pos = acc(p['attributes']['POSITION']).astype(np.float64)
            idx = acc(p['indices']).astype(np.int64).reshape(-1, 3) if 'indices' in p else np.arange(len(pos)).reshape(-1, 3)
            P.append(pos); I.append(idx + base); base += len(pos)
        meshes[m['name']] = (np.vstack(P), np.vstack(I))
    return meshes


U = glb(f'{SRC}/uterus.glb')
L = glb(f'{SRC}/ovary-left.glb')
R = glb(f'{SRC}/ovary-right.glb')


def merge(parts):
    P, I, base = [], [], 0
    for p, i in parts:
        P.append(p); I.append(i + base); base += len(p)
    return np.vstack(P), np.vstack(I)


# The reference uterus is split into regions (some overlap, e.g. the walls); fundus, body, lower segment and
# cervix together make the closed outer surface.
objs = {
    'Uterus': (merge([U['VH_F_fundus_of_uterus'], U['VH_F_body_of_uterus'], U['VH_F_lower_uterine_segment']]), 'uterus'),
    'Cervix uteri': (merge([U['VH_F_cervix']]), 'cervix'),
    'Ovary.l': (L['VH_F_left_ovary'], 'ovary'),
    'Ovary.r': (R['VH_F_right_ovary'], 'ovary'),
}


def tube(a, b, lift, r0, r1, N=36, M=18):
    """Curved tube from a to b, bulging by lift; wider towards the ovary (ampulla, infundibulum)."""
    ctrl = (a + b) / 2 + lift
    bez = lambda t: a * (1 - t) ** 2 + ctrl * 2 * t * (1 - t) + b * t * t
    V, F = [], []
    for i in range(N + 1):
        t = i / N
        p = bez(t)
        tg = bez(min(1, t + 0.01)) - bez(max(0, t - 0.01)); tg /= np.linalg.norm(tg)
        u = np.cross(tg, [0, 1, 0]); u = u if np.linalg.norm(u) > 1e-3 else np.cross(tg, [1, 0, 0]); u /= np.linalg.norm(u)
        w = np.cross(tg, u)
        r = r0 + (r1 - r0) * t ** 1.6
        for k in range(M):
            ang = 2 * math.pi * k / M
            V.append(p + (u * math.cos(ang) + w * math.sin(ang)) * r)
    for i in range(N):
        for k in range(M):
            a0, a1 = i * M + k, i * M + (k + 1) % M
            b0, b1 = a0 + M, a1 + M
            F += [(a0, a1, b1), (a0, b1, b0)]
    V, F = np.array(V), np.array(F)
    axis = np.repeat(np.array([bez(i / N) for i in range(N + 1)]), M, axis=0)
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    out = V[F].mean(axis=1) - axis[F[:, 0]]
    if (np.einsum('ij,ij->i', fn, out) < 0).mean() > 0.5:
        F = F[:, ::-1].copy()
    return V, F


uw = objs['Uterus'][0][0]
top = uw[:, 2].max()
for side, ov in (('l', objs['Ovary.l'][0][0]), ('r', objs['Ovary.r'][0][0])):
    oc = ov.mean(axis=0)
    sgn = 1 if oc[0] > uw[:, 0].mean() else -1
    # cornu: the most lateral point of the upper uterus on this side
    upper = uw[uw[:, 2] > top - 0.012]
    corn = upper[np.argmax(sgn * upper[:, 0])]
    # the tube ends at the upper lateral pole of the ovary (infundibulum over the ovary)
    end = ov[np.argmax(sgn * ov[:, 0] + 0.6 * ov[:, 2])] + np.array([sgn * 0.003, 0, 0.004])
    objs[f'Uterine tube.{side}'] = (tube(corn, end, np.array([0, 0.004, 0.018]), 0.0018, 0.0042), 'tube')


def normals(P, I):
    n = np.zeros_like(P)
    fn = np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]])
    for c in range(3):
        np.add.at(n, I[:, c], fn)
    ln = np.linalg.norm(n, axis=1, keepdims=True); ln[ln == 0] = 1
    return n / ln


# orient faces outwards per object (the reference meshes are closed; flip if the volume is negative)
for name, ((P, I), cls) in list(objs.items()):
    if cls == 'tube': continue  # already oriented, and open at both ends
    vol = np.einsum('ij,ij->i', P[I[:, 0]], np.cross(P[I[:, 1]], P[I[:, 2]])).sum()
    if vol < 0:
        objs[name] = ((P, I[:, ::-1].copy()), cls)

allp = np.vstack([p for (p, _), _ in objs.values()])
mn, mx = allp.min(axis=0), allp.max(axis=0)
ctr = (mn + mx) / 2
S = 3.0 / (mx - mn).max()


def to_unity(P):  # Blender/HRA Z-up -> Unity Y-up, same mapping as build_pack.py
    Q = (P - ctr) * S
    return np.stack([Q[:, 0], Q[:, 2], Q[:, 1]], axis=1)


def wstr(f, s):
    b = s.encode('utf8'); f.write(struct.pack('<i', len(b))); f.write(b)


with open(OUT, 'wb') as f:
    f.write(b'HRT2'); f.write(struct.pack('<i', len(objs)))
    for name in sorted(objs):
        (P, I), cls = objs[name]
        Pu = to_unity(P)
        Nu = normals(P, I); Nu = np.stack([Nu[:, 0], Nu[:, 2], Nu[:, 1]], axis=1)
        wstr(f, name); wstr(f, cls)
        f.write(struct.pack('<ii', len(Pu), len(I)))
        lo, hi = Pu.min(axis=0), Pu.max(axis=0); rng = np.where(hi - lo > 1e-9, hi - lo, 1)
        f.write(struct.pack('<3f', *lo)); f.write(struct.pack('<3f', *hi))
        q = np.round((Pu - lo) / rng * 65535 - 32768).clip(-32768, 32767).astype('<i2')
        f.write(q.tobytes())
        f.write(np.round(Nu * 127).clip(-127, 127).astype(np.int8).tobytes())
        if len(Pu) < 65536:
            f.write(bytes([2])); f.write(I.astype('<u2').tobytes())
        else:
            f.write(bytes([4])); f.write(I.astype('<i4').tobytes())
        f.write(struct.pack('<i', 0))  # no blend shapes
        print(name, cls, len(Pu), 'verts', len(I), 'tris')
print('pack', OUT, 'size', (mx - mn).round(4), 'm')
