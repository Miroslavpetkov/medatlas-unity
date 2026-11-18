# Generic organ pack from Z-Anatomy: blender -b --python build_pack.py -- <Startup.blend> <spec.json> <out.bytes>
# spec: {"objects": {"<Z-Anatomy name>": "<class>", ...}, "contract": ["<class>", ...]}
import bpy, bmesh, sys, json, struct
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, SPEC, OUT = argv[0], argv[1], argv[2]
spec = json.load(open(SPEC, encoding='utf8'))
CLS = spec['objects']; CONTRACT = set(spec.get('contract', []))

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
    if len(b.faces) > 30000:
        b.to_mesh(me); b.free()
        no = bpy.data.objects.new('tmp', me); sc.collection.objects.link(no)
        m = no.modifiers.new('d', 'DECIMATE'); m.ratio = 30000 / len(me.polygons)
        me2 = bpy.data.meshes.new_from_object(no.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(no); me = me2
    else:
        b.to_mesh(me); b.free()
    objs[o.name] = me
print('MISSING', [n for n in CLS if n not in objs])

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
print('EXPORTED', len(objs), sum(len(m.vertices) for m in objs.values()))
