# Blender 5.x CLI: builds the heart model from BodyParts3D OBJ parts.
# Usage: blender -b --python build_heart.py -- <obj_dir> <out_blend>
import bpy, bmesh, sys, os, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]

# id -> (name, class)
PARTS = {
    # chambers (blood casts) and atrial walls
    'FJ2422': ('cavum_LV', 'cavity'), 'FJ2423': ('cavum_RV', 'cavity'),
    'FJ2424': ('cavum_RA', 'cavity'), 'FJ2425': ('cavum_LA', 'cavity'),
    'FJ2438': ('paries_LA', 'atrium'), 'FJ2439': ('paries_RA', 'atrium'),
    # AV valves (leaflets + chordae)
    'FJ2420': ('mitral_ant', 'mitral'), 'FJ2432': ('mitral_post', 'mitral'),
    'FJ2421': ('tricusp_ant', 'tricuspid'), 'FJ2433': ('tricusp_post', 'tricuspid'), 'FJ2436': ('tricusp_sept', 'tricuspid'),
    # papillary muscles
    'FJ2418': ('papLV_antlat', 'papLV'), 'FJ2429': ('papLV_lat', 'papLV'),
    'FJ2419': ('papRV_ant', 'papRV'), 'FJ2430': ('papRV_post', 'papRV'), 'FJ2437': ('papRV_sept', 'papRV'),
    # semilunar cusps
    'FJ2426': ('aortic_lp', 'aorticValve'), 'FJ2431': ('aortic_rp', 'aorticValve'), 'FJ2435': ('aortic_ant', 'aorticValve'),
    'FJ2417': ('pulm_la', 'pulmValve'), 'FJ2427': ('pulm_post', 'pulmValve'), 'FJ2434': ('pulm_ra', 'pulmValve'),
    # great vessels
    'FJ3413': ('aorta_asc', 'aorta'), 'FJ3411': ('aorta_arch', 'aorta'), 'FJ3427': ('aorta_desc', 'aorta'),
    'FJ3483': ('a_carotis_sin', 'aorta'), 'FJ3479': ('a_subclavia_sin', 'aorta'), 'FJ3564': ('a_carotis_dx', 'aorta'),
    'FJ2966': ('truncus_pulm', 'pulmArtery'), 'FJ2924': ('a_pulm_sin', 'pulmArtery'), 'FJ3019': ('a_pulm_dx', 'pulmArtery'),
    # 4 pulmonary veins: left superior, left inferior, right superior, right inferior (segmental fragments left out)
    'FJ3645': ('v_cava_sup', 'cava'), 'FJ3441': ('v_cava_inf', 'cava'),
}
CORONARY_VEINS = {'FJ2655', 'FJ2656', 'FJ2724', 'FJ2731', 'FJ2727', 'FJ2728', 'FJ2729'} | {'FJ%d' % i for i in list(range(2678, 2692)) + list(range(2701, 2714))}
for i in list(range(2631, 2657)) + list(range(2667, 2738)):
    fid = 'FJ%d' % i
    if os.path.exists(os.path.join(SRC, fid + '.obj')) and fid not in PARTS:
        PARTS[fid] = ('cor_v_' + fid if fid in CORONARY_VEINS else 'cor_a_' + fid,
                      'coronaryVein' if fid in CORONARY_VEINS else 'coronaryArtery')

bpy.ops.wm.read_factory_settings(use_empty=True)
objs = {}
for fid, (name, cls) in PARTS.items():
    bpy.ops.wm.obj_import(filepath=os.path.join(SRC, fid + '.obj'), forward_axis='Y', up_axis='Z')
    o = bpy.context.selected_objects[0]
    o.name = name; o.data.name = name
    o['cls'] = cls; o['fid'] = fid
    objs[name] = o

def apply_mods(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear()
    old = o.data; o.data = me; me.name = o.name
    bpy.data.meshes.remove(old)

def trim(o, co, no):
    """remove part of mesh on +no side of plane, cap the hole"""
    bm = bmesh.new(); bm.from_mesh(o.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_outer=True)
    cut_edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
    if cut_edges:
        try:
            bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
        except Exception as ex:
            print('holes_fill failed', o.name, ex)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(o.data); bm.free()

# ---- trimming of vessels (BodyParts3D: +X patient left, -Y anterior, +Z up; mm)
trim(objs['aorta_desc'], Vector((0, 0, 1188)), Vector((0, 0, -1)))
trim(objs['v_cava_inf'], Vector((0, 0, 1168)), Vector((0, 0, -1)))
for n in ('a_carotis_sin', 'a_subclavia_sin', 'a_carotis_dx'):
    trim(objs[n], Vector((0, 0, 1338)), Vector((0, 0, 1)))
trim(objs['a_subclavia_sin'], Vector((52, 0, 0)), Vector((1, 0, 0)))
trim(objs['a_pulm_sin'], Vector((66, 0, 0)), Vector((1, 0, 0)))
trim(objs['a_pulm_dx'], Vector((-42, 0, 0)), Vector((-1, 0, 0)))

# ---- left atrium: close the stray openings of the wall and build the 4 pulmonary veins exactly on their ostia
def boundary_loops(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    seen = set(); loops = []
    for e in [e for e in bm.edges if e.is_boundary]:
        if e in seen: continue
        loop = []; stack = [e]
        while stack:
            x = stack.pop()
            if x in seen: continue
            seen.add(x); loop.append(x)
            for v in x.verts:
                for y in v.link_edges:
                    if y.is_boundary and y not in seen: stack.append(y)
        vs = list({v for x in loop for v in x.verts})
        c = sum((v.co for v in vs), Vector()) / len(vs)
        r = sum((v.co - c).length for v in vs) / len(vs)
        loops.append((len(vs), c.copy(), r, [x.index for x in loop]))
    bm.free()
    return loops

la = objs['paries_LA']
loops = boundary_loops(la)
loops.sort(key=lambda t: -t[0])
ostia = [(c, r) for n, c, r, _ in loops[1:] if 4.8 <= r <= 9.0]
print('PV ostia', [(tuple(round(x, 1) for x in c), round(r, 1)) for c, r in ostia])
# fill every opening except the mitral orifice (largest loop)
bm = bmesh.new(); bm.from_mesh(la.data); bm.edges.ensure_lookup_table()
for n, c, r, eidx in loops[1:]:
    try:
        bmesh.ops.holes_fill(bm, edges=[bm.edges[i] for i in eidx], sides=0)
    except Exception as ex:
        print('fill failed', ex)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.to_mesh(la.data); bm.free()
la_c = sum((v.co for v in objs['cavum_LA'].data.vertices), Vector()) / len(objs['cavum_LA'].data.vertices)
names = {}
left = sorted([o for o in ostia if o[0].x > la_c.x], key=lambda t: -t[0].z)
right = sorted([o for o in ostia if o[0].x <= la_c.x], key=lambda t: -t[0].z)
# superior and inferior vein of each side lie one above the other on the back of the atrium:
# put them on the wall around the pair's centre, 9 mm above and below
from mathutils.bvhtree import BVHTree
_bm = bmesh.new(); _bm.from_mesh(la.data); _bvh = BVHTree.FromBMesh(_bm); _bm.free()
def stacked(pair):
    if len(pair) < 2: return pair
    cm = (pair[0][0] + pair[1][0]) / 2; rr = (pair[0][1] + pair[1][1]) / 2
    out = []
    for dz in (9.0, -9.0):
        tgt = cm + Vector((0, 0, dz))
        hit = _bvh.find_nearest(tgt)
        out.append((hit[0] if hit and hit[0] is not None else tgt, rr))
    return out
left = stacked(left); right = stacked(right)
print('PV stacked', [(tuple(round(x, 1) for x in c), round(r, 1)) for c, r in left + right])
for (c, r), nm in list(zip(left, ['vv_pulm_2', 'vv_pulm_5'])) + list(zip(right, ['vv_pulm_6', 'vv_pulm_7'])):
    # short stumps (~1.5 cm) leaving the back of the left atrium towards the hilum of each lung:
    # mostly lateral, a little posterior; the superior veins rise slightly, the inferior run horizontally
    d = (c - la_c); d.z *= 0.3; d.normalize()
    lateral = Vector((1 if c.x > la_c.x else -1, 0, 0))
    sup = nm in ('vv_pulm_2', 'vv_pulm_6')
    dirv = (lateral * 1.0 + d * 0.55 + Vector((0, 0, 0.3 if sup else -0.05))).normalized()
    p0 = c - d * 2.0; p1 = c + d * 4.0 + dirv * 3.0; p2 = c + d * 4.0 + dirv * 13.0
    cd = bpy.data.curves.new(nm, 'CURVE'); cd.dimensions = '3D'
    sp = cd.splines.new('BEZIER'); sp.bezier_points.add(2)
    for bp, pt in zip(sp.bezier_points, (p0, p1, p2)):
        bp.co = pt; bp.handle_left_type = bp.handle_right_type = 'AUTO'
    cd.bevel_depth = r * 0.95; cd.bevel_resolution = 4; cd.use_fill_caps = True; cd.resolution_u = 10
    co = bpy.data.objects.new(nm, cd); bpy.context.scene.collection.objects.link(co)
    me = bpy.data.meshes.new_from_object(co.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    vo = bpy.data.objects.new(nm, me); bpy.context.scene.collection.objects.link(vo)
    bpy.data.objects.remove(co)
    vo.name = nm; vo.data.name = nm
    vo['cls'] = 'pulmVein'; vo['fid'] = 'gen'
    objs[nm] = vo
print('PV built', [n for n in objs if n.startswith('vv_pulm')])
trim(objs['v_cava_sup'], Vector((0, 0, 1305)), Vector((0, 0, 1)))

# ---- brachiocephalic trunk (missing in BodyParts3D partof set): tube from arch to right carotid base
arch = objs['aorta_arch']; rcca = objs['a_carotis_dx']
rv = min((v.co for v in rcca.data.vertices), key=lambda c: c.z)
low = [v.co for v in rcca.data.vertices if v.co.z < rv.z + 3]
p1 = sum(low, Vector()) / len(low)
ac = sum((v.co for v in arch.data.vertices), Vector()) / len(arch.data.vertices)
p0 = min((v.co for v in arch.data.vertices), key=lambda c: (c - p1).length)
p0 = p0 + (ac - p0).normalized() * 7.0
cd = bpy.data.curves.new('bct', 'CURVE'); cd.dimensions = '3D'
sp = cd.splines.new('BEZIER'); sp.bezier_points.add(1)
for bp, p in zip(sp.bezier_points, (p0, p1 + Vector((0, 0, -2)))):
    bp.co = p; bp.handle_left_type = bp.handle_right_type = 'AUTO'
cd.bevel_depth = 5.5; cd.bevel_resolution = 4; cd.use_fill_caps = True; cd.resolution_u = 8
co = bpy.data.objects.new('truncus_brachiocephalicus', cd); bpy.context.scene.collection.objects.link(co)
dg = bpy.context.evaluated_depsgraph_get()
me = bpy.data.meshes.new_from_object(co.evaluated_get(dg))
bo = bpy.data.objects.new('truncus_brachiocephalicus', me); bpy.context.scene.collection.objects.link(bo)
bpy.data.objects.remove(co)
bo['cls'] = 'aorta'; bo['fid'] = 'gen'
objs['truncus_brachiocephalicus'] = bo

# ---- landmarks
def centroid(names):
    pts = [v.co.copy() for n in names for v in objs[n].data.vertices]
    return sum(pts, Vector()) / len(pts)

LM = {}
LM['mitral'] = centroid(['mitral_ant', 'mitral_post'])
LM['tricuspid'] = centroid(['tricusp_ant', 'tricusp_post', 'tricusp_sept'])
LM['aorticValve'] = centroid(['aortic_lp', 'aortic_rp', 'aortic_ant'])
LM['pulmValve'] = centroid(['pulm_la', 'pulm_post', 'pulm_ra'])
LM['papLV'] = centroid(['papLV_antlat', 'papLV_lat'])
LM['papRV'] = centroid(['papRV_ant', 'papRV_post', 'papRV_sept'])

def annulus(names, pap):
    pts = [v.co.copy() for n in names for v in objs[n].data.vertices]
    c0 = sum(pts, Vector()) / len(pts)
    n = (pap - c0).normalized()
    hs = [(p - c0).dot(n) for p in pts]
    hmin = min(hs)
    ring = [p for p, h in zip(pts, hs) if h < hmin + 2.5]
    c = sum(ring, Vector()) / len(ring)
    n = (pap - c).normalized()
    rs = sorted(((p - c) - n * (p - c).dot(n)).length for p in ring)
    R = rs[len(rs) // 2]
    return c, n, R

LM['mitralC'], LM['mitralN'], LM['mitralR'] = annulus(['mitral_ant', 'mitral_post'], LM['papLV'])
LM['tricC'], LM['tricN'], LM['tricR'] = annulus(['tricusp_ant', 'tricusp_post', 'tricusp_sept'], LM['papRV'])
lvc = objs['cavum_LV']
LM['apexLV'] = max((v.co for v in lvc.data.vertices), key=lambda c: (c - LM['mitralC']).length).copy()
rvc = objs['cavum_RV']
LM['apexRV'] = max((v.co for v in rvc.data.vertices), key=lambda c: (c - LM['tricC']).length).copy()
for k, v in LM.items():
    print('LM', k, v)

# ---- ventricular myocardium: inflate LV / RV cavities and union
U_AXIS = (LM['mitralC'] - LM['apexLV']).normalized()

def inflate(src, thick, name):
    o = src.copy(); o.data = src.data.copy(); o.name = name
    bpy.context.scene.collection.objects.link(o)
    m = o.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 1.2
    apply_mods(o)
    me = o.data
    for v in me.vertices:
        up = v.normal.dot(U_AXIS)
        w = 1.0
        if up > 0.35:
            w = max(0.25, 1.0 - (up - 0.35) * 1.6)
        v.co += v.normal * thick * w
    me.update()
    return o

lv_in = inflate(objs['cavum_LV'], 8.2, 'tmpLV')
rv_in = inflate(objs['cavum_RV'], 2.8, 'tmpRV')
bpy.ops.object.select_all(action='DESELECT')
lv_in.select_set(True); rv_in.select_set(True)
bpy.context.view_layer.objects.active = lv_in
bpy.ops.object.join()
myo = lv_in; myo.name = 'myocardium'; myo.data.name = 'myocardium'
m = myo.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 1.1
m2 = myo.modifiers.new('s', 'SMOOTH'); m2.factor = 0.6; m2.iterations = 12
m3 = myo.modifiers.new('d', 'DECIMATE'); m3.ratio = 0.35
apply_mods(myo)
myo['cls'] = 'myocardium'; myo['fid'] = 'gen'
objs['myocardium'] = myo
print('myocardium verts', len(myo.data.vertices))

# ---- smoothing / subdivision for low-poly parts
for n, o in objs.items():
    if o.get('fid') == 'gen' and n == 'myocardium':
        continue
    nv = len(o.data.vertices)
    if nv < 2500 and o['cls'] not in ('mitral', 'tricuspid'):
        m = o.modifiers.new('sub', 'SUBSURF'); m.levels = 1; m.render_levels = 1
        apply_mods(o)
    for p in o.data.polygons:
        p.use_smooth = True

for o in objs.values():
    for p in o.data.polygons:
        p.use_smooth = True

# store landmarks on scene
import json
bpy.context.scene['landmarks'] = json.dumps({k: (list(v) if isinstance(v, Vector) else v) for k, v in LM.items()})
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('TOTAL verts', sum(len(o.data.vertices) for o in objs.values()), 'tris', sum(len(o.data.polygons) for o in objs.values()))
