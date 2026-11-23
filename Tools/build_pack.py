# Generic organ pack from Z-Anatomy: blender -b --python build_pack.py -- <Startup.blend> <spec.json> <out.bytes>
# spec: {"objects": {"<Z-Anatomy name>": "<class>", ...}, "contract": ["<class>", ...]}
import bpy, bmesh, sys, json, struct
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, SPEC, OUT = argv[0], argv[1], argv[2]
spec = json.load(open(SPEC, encoding='utf8'))
CLS = spec['objects']; CONTRACT = set(spec.get('contract', []))
MAXF = spec.get('maxFaces', 30000)  # per-part face budget; lower it in a spec to keep a pack light

bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(SRC, link=False) as (src, dst):
    dst.objects = [n for n in src.objects if n in CLS]
sc = bpy.context.scene
for o in dst.objects:
    if o is not None: sc.collection.objects.link(o)
dg = bpy.context.evaluated_depsgraph_get()
objs = {}
for o in list(sc.objects):
    if o.name not in CLS or o.type not in ('MESH', 'CURVE'): continue
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), depsgraph=dg)
    me.transform(o.matrix_world)
    if o.matrix_world.determinant() < 0: me.flip_normals()
    b = bmesh.new(); b.from_mesh(me); bmesh.ops.recalc_face_normals(b, faces=b.faces[:])
    # keep packs light: decimate very dense parts
    if len(b.faces) > MAXF:
        b.to_mesh(me); b.free()
        no = bpy.data.objects.new('tmp', me); sc.collection.objects.link(no)
        m = no.modifiers.new('d', 'DECIMATE'); m.ratio = MAXF / len(me.polygons)
        me2 = bpy.data.meshes.new_from_object(no.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(no); me = me2
    else:
        b.to_mesh(me); b.free()
    objs[o.name] = me
print('MISSING', [n for n in CLS if n not in objs])
# generated tubes for parts missing in Z-Anatomy (e.g. rectum): from the point of one part nearest to another part's centre
import math
for tb in spec.get('tubes', []):
    A = objs[tb['from']]; B = objs[tb['to']]
    cb = sum((v.co for v in B.vertices), Vector()) / len(B.vertices)
    pa = min((v.co for v in A.vertices), key=lambda p: (p - cb).length)
    ca = sum((v.co for v in A.vertices), Vector()) / len(A.vertices)
    pa = pa + (pa - ca).normalized() * 0.0  # start at the surface end
    end = cb + Vector(tb.get('endOffset', (0, 0, 0)))
    ctrl = (pa + end) / 2 + Vector(tb.get('bend', (0, 0, 0)))
    r0, r1 = tb['r0'], tb['r1']; N = 28; M = 20
    bm = bmesh.new(); rings = []
    def bez(t): return pa * (1 - t) ** 2 + ctrl * 2 * t * (1 - t) + end * t * t
    for i in range(N + 1):
        t = i / N; p = bez(t); tg = (bez(min(1, t + 0.01)) - bez(max(0, t - 0.01))).normalized()
        u = tg.cross(Vector((1, 0, 0))); u = (u if u.length > 1e-3 else tg.cross(Vector((0, 1, 0)))).normalized(); w = tg.cross(u)
        r = r0 + (r1 - r0) * t; r *= 1 + 0.35 * math.sin(math.pi * t)  # ampulla
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / M) + w * math.sin(2 * math.pi * k / M)) * r) for k in range(M)])
    for i in range(N):
        for k in range(M):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % M], rings[i + 1][(k + 1) % M], rings[i + 1][k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(tb['name']); bm.to_mesh(me); bm.free()
    objs[tb['name']] = me; CLS[tb['name']] = tb['cls']
    print('TUBE', tb['name'], tuple(round(x, 3) for x in pa), tuple(round(x, 3) for x in end))

allv = [v.co for me in objs.values() for v in me.vertices]
mn = Vector((min(p.x for p in allv), min(p.y for p in allv), min(p.z for p in allv)))
mx = Vector((max(p.x for p in allv), max(p.y for p in allv), max(p.z for p in allv)))
CTR = (mn + mx) / 2
size = max(mx - mn)
S = 3.0 / size          # the whole pack is ~3 units across
def U3(p): return ((p.x - CTR.x) * S, (p.z - CTR.z) * S, (p.y - CTR.y) * S)
def D3(d): return (d.x * S, d.z * S, d.y * S)
def N3(d): return (d.x, d.z, d.y)
def normals(me, coords):
    b = bmesh.new(); b.from_mesh(me); b.verts.ensure_lookup_table()
    for v, c in zip(b.verts, coords): v.co = c
    b.normal_update(); ns = [v.normal.copy() for v in b.verts]; b.free(); return ns
def wstr(f, s):
    bb = s.encode('utf8'); f.write(struct.pack('<i', len(bb))); f.write(bb)

with open(OUT, 'wb') as f:
    f.write(b'HRT1'); f.write(struct.pack('<i', len(objs)))
    for name in sorted(objs):
        me = objs[name]; me.calc_loop_triangles()
        base = [v.co.copy() for v in me.vertices]; bn = normals(me, base)
        tris = [t.vertices[:] for t in me.loop_triangles]
        wstr(f, name); wstr(f, CLS[name])
        f.write(struct.pack('<ii', len(base), len(tris)))
        f.write(struct.pack('<%df' % (len(base) * 3), *[c for p in base for c in U3(p)]))
        f.write(struct.pack('<%df' % (len(base) * 3), *[c for n in bn for c in N3(n)]))
        f.write(struct.pack('<%di' % (len(tris) * 3), *[i for t in tris for i in t]))
        keys = {}
        if CLS[name] in CONTRACT:
            c = sum(base, Vector()) / len(base)
            # long axis by extent
            ext = [max(p[i] for p in base) - min(p[i] for p in base) for i in range(3)]
            ax = Vector([1 if i == ext.index(max(ext)) else 0 for i in range(3)])
            ds = []
            for p in base:
                r = p - c; a = r.dot(ax)
                radial = r - ax * a
                ds.append(-ax * a * 0.14 + radial * 0.18)
            keys['Contract'] = ds
        f.write(struct.pack('<i', len(keys)))
        for kn, ds in keys.items():
            wstr(f, kn)
            nn = normals(me, [p + d for p, d in zip(base, ds)])
            f.write(struct.pack('<%df' % (len(base) * 3), *[c for d in ds for c in D3(d)]))
            f.write(struct.pack('<%df' % (len(base) * 3), *[c for a, b in zip(nn, bn) for c in N3(a - b)]))
# ---- optional centre-line paths through hollow organs (food journey, urine) for the simulations
if spec.get('paths'):
    import heapq
    from mathutils import kdtree
    def kd(me):
        t = kdtree.KDTree(len(me.vertices))
        for v in me.vertices: t.insert(v.co, v.index)
        t.balance(); return t
    def nearest_pair(a, b):
        tb = kd(objs[b]); best = None
        for v in list(objs[a].vertices)[::2]:
            co, i, d = tb.find(v.co)
            if best is None or d < best[0]: best = (d, v.co.copy(), co.copy())
        return best[1], best[2]
    def geodesic(me, p0, p1):
        t = kd(me); s = t.find(p0)[1]; e = t.find(p1)[1]
        adj = [[] for _ in me.vertices]
        for ed in me.edges:
            a, b = ed.vertices; w = (me.vertices[a].co - me.vertices[b].co).length; adj[a].append((b, w)); adj[b].append((a, w))
        dist = {s: 0.0}; prev = {}; h = [(0.0, s)]
        while h:
            d, u = heapq.heappop(h)
            if u == e: break
            if d > dist.get(u, 1e9): continue
            for v, w in adj[u]:
                nd = d + w
                if nd < dist.get(v, 1e9): dist[v] = nd; prev[v] = u; heapq.heappush(h, (nd, v))
        path = [e]
        while path[-1] != s and path[-1] in prev: path.append(prev[path[-1]])
        return [me.vertices[i] for i in reversed(path)], t
    def resample(pts, step):
        out = [pts[0]]; acc = 0
        for a, b in zip(pts, pts[1:]):
            L = (b - a).length
            while acc + L >= step and L > 0:
                f = (step - acc) / L; a = a + (b - a) * f; L = (b - a).length; out.append(a.copy()); acc = 0
            acc += L
        if (out[-1] - pts[-1]).length > step * 0.3: out.append(pts[-1])
        return out
    def smooth(pts, n=3):
        for _ in range(n):
            pts = [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) / 4 for i in range(1, len(pts) - 1)] + [pts[-1]]
        return pts
    def zslices(me, top_to_bottom=True, bins=14):
        vs = [v.co for v in me.vertices]; z0 = max(v.z for v in vs); z1 = min(v.z for v in vs); out = []
        for i in range(bins):
            a = z0 + (z1 - z0) * i / bins; b = z0 + (z1 - z0) * (i + 1) / bins
            sel = [v for v in vs if b <= v.z <= a]
            if sel: out.append(sum(sel, Vector()) / len(sel))
        return out if top_to_bottom else out[::-1]
    allpaths = {}
    for pname, segs in spec['paths'].items():
        res = []; prev_end = None
        for i, sg in enumerate(segs):
            me = objs[sg['part']]
            if sg.get('mode') == 'z':
                pts = zslices(me, sg.get('down', True), sg.get('bins', 14))
            else:
                p0 = prev_end if prev_end is not None else Vector(sg['start'])
                p1 = nearest_pair(sg['part'], segs[i + 1]['part'])[0] if i + 1 < len(segs) else Vector(sg['end'])
                if sg.get('endPart'): p1 = nearest_pair(sg['part'], sg['endPart'])[0]
                gv, tree = geodesic(me, p0, p1)
                R = sg.get('r', 0.01); pts = []
                for v in gv:
                    if sg.get('mode') == 'centroid':
                        near = [me.vertices[j].co for (co, j, d) in tree.find_range(v.co, R)]
                        pts.append(sum(near, Vector()) / len(near))
                    else:
                        pts.append(v.co - v.normal * R)
                pts = smooth(resample(pts, 0.004), 6)
            prev_end = pts[-1]
            res.append({'part': sg['part'], 'stage': sg['stage'], 'pts': [U3(p) for p in pts]})
            print('PATH', pname, sg['part'], len(pts))
        allpaths[pname] = res
    json.dump(allpaths, open(OUT + '.paths.json', 'w', encoding='utf8'))
print('EXPORTED', len(objs), sum(len(m.vertices) for m in objs.values()))
