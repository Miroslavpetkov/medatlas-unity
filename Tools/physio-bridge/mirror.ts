// Scene mirror: builds the web version's own three.js scene for a running model and hands it to Unity
// every frame, so the Unity view shows exactly the same objects, animation, particles and labels.
//
// Unity asks for:
//   build(h)            -> JSON { ok, err }                      (builds the web scene for instance h)
//   frame(h, dt)        -> number of floats in the frame          (runs the scene's update(dt), packs the frame)
//   structure(h)        -> JSON of nodes / geometries / materials / textures (when frame header says it changed)
//   copy*()             -> binary data into Unity's arrays (the jslib does the copying)
//   fit(h, aspect, fov) -> JSON [cx, cy, cz, dist]               (the web viewer's fitView, in Unity axes)
//
// Axes: three.js is right-handed, Unity left-handed. Everything leaves here with z negated
// (positions, normals, matrices) and triangle winding reversed, so Unity can use it as is.
// The build aliases `three` to one copy, so this file and the scenes share it.

/* eslint-disable @typescript-eslint/no-explicit-any */
import * as THREE from 'three';
import { pickLods } from '../medatlas/medAtlas-3D-main/src/core/lod';

type Obj = any;

const K_MESH = 0, K_INST = 1, K_POINTS = 2, K_LINES = 3, K_STRIP = 4, K_LOOP = 5, K_SPRITE = 6;
const M_LIT = 0, M_BASIC = 1, M_POINTS = 2, M_LINE = 3, M_SPRITE = 4;

const TAN20 = Math.tan((20 * Math.PI) / 180); // the web camera's half field of view (40 degrees)

interface Mirror {
  built: any;
  /** Direction from the target to the camera (three.js axes) and the framing margin. */
  dir: number[];
  margin: number;
  root: Obj;
  partOf: Map<Obj, string>;
  ids: WeakMap<object, number>;
  next: number;
  sig: string;
  ver: number;
  nodes: Obj[];
  kinds: number[];
  geoVer: Map<number, string>;
  texVer: Map<number, number>;
  structure: string;
  frame: Float32Array;
  frameLen: number;
  geoms: Map<number, Obj>;
  texs: Map<number, Obj>;
  /** Unity's camera in this scene's world (three.js coordinates), for the levels of detail. */
  cam: THREE.Vector3 | null;
}

const mirrors: (Mirror | null)[] = [];

function idOf(m: Mirror, o: object): number {
  let id = m.ids.get(o);
  if (id === undefined) {
    id = ++m.next;
    m.ids.set(o, id);
  }
  return id;
}

function kindOf(o: Obj): number {
  if (o.isInstancedMesh) return K_INST;
  if (o.isMesh) return K_MESH;
  if (o.isPoints) return K_POINTS;
  if (o.isLineSegments) return K_LINES;
  if (o.isLineLoop) return K_LOOP;
  if (o.isLine) return K_STRIP;
  if (o.isSprite) return K_SPRITE;
  return -1;
}

function matType(mt: Obj): number {
  if (mt.isSpriteMaterial) return M_SPRITE;
  if (mt.isPointsMaterial) return M_POINTS;
  if (mt.isLineBasicMaterial || mt.isLineDashedMaterial) return M_LINE;
  if (mt.isMeshBasicMaterial) return M_BASIC;
  return M_LIT;
}

/** The same materials the web viewer makes (viewer.makeMat): part-owned, double sided. */
export const mirrorCtx = {
  mat: (_pid: string, o: any) => {
    const opacity = o.opacity ?? 1;
    return new THREE.MeshStandardMaterial({
      color: o.color, roughness: o.roughness ?? 0.55, metalness: o.metalness ?? 0.05,
      transparent: opacity < 1, opacity, emissive: o.emissive ?? 0, side: THREE.DoubleSide, depthWrite: opacity >= 1,
    });
  },
};

/** Which part each object belongs to (scenes add parts later, e.g. organs that load after the scene is built). */
function partsOf(built: any): Map<Obj, string> {
  const partOf = new Map<Obj, string>();
  for (const [pid, objs] of Object.entries(built.parts ?? {}))
    for (const o of objs as Obj[]) o.traverse?.((c: Obj) => { if (!partOf.has(c)) partOf.set(c, pid); });
  return partOf;
}

/** Mirror a scene that is already built ({ root, parts, update }) under handle h. */
export function mirrorAdopt(h: number, built: any, opts: { dir?: number[]; margin?: number } = {}): void {
  const partOf = partsOf(built);
  const d = opts.dir ?? [0.35, 0.25, 1];
  const n = Math.hypot(d[0], d[1], d[2]);
  mirrors[h] = {
    built, dir: d.map((v) => v / n), margin: opts.margin ?? 0.06, root: built.root, partOf, ids: new WeakMap(), next: 0, sig: '', ver: 0, nodes: [], kinds: [],
    geoVer: new Map(), texVer: new Map(), structure: '', frame: new Float32Array(4096), frameLen: 0,
    geoms: new Map(), texs: new Map(), cam: null,
  };
}

/** The web scene for instance `s` (bridge record with m, inst, v). */
export function mirrorBuild(h: number, s: any): string {
  try {
    const built = s.m.scene(mirrorCtx, s.inst, () => s.v);
    mirrorAdopt(h, built);
    return JSON.stringify({ ok: true, distance: built.distance ?? 0 });
  } catch (e: any) {
    mirrors[h] = null;
    return JSON.stringify({ ok: false, err: String((e && e.message) || e).slice(0, 300) });
  }
}

export function mirrorDispose(h: number): void {
  const m = mirrors[h];
  if (!m) return;
  m.root.traverse((o: Obj) => {
    o.geometry?.dispose?.();
    const mt = o.material;
    if (mt) for (const x of Array.isArray(mt) ? mt : [mt]) { x.map?.dispose?.(); x.dispose?.(); }
  });
  mirrors[h] = null;
}

// ------------------------------------------------------------------ packing helpers

/** Write a three.js column-major world matrix into `out` at `at`, converted to Unity axes (S·M·S, S = diag(1,1,-1,1)). */
function putMatrix(out: Float32Array, at: number, e: ArrayLike<number>): void {
  for (let i = 0; i < 16; i++) out[at + i] = e[i];
  out[at + 2] = -e[2]; out[at + 6] = -e[6]; out[at + 14] = -e[14];
  out[at + 8] = -e[8]; out[at + 9] = -e[9]; out[at + 11] = -e[11];
}

function mul4(a: ArrayLike<number>, b: ArrayLike<number>, out: Float32Array | number[], at: number): void {
  for (let c = 0; c < 4; c++)
    for (let r = 0; r < 4; r++)
      out[at + c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
}

const rgb = { r: 0, g: 0, b: 0 };
function srgb(c: Obj, k = 1): [number, number, number] {
  if (!c) return [0, 0, 0];
  c.getRGB(rgb, 'srgb');
  return [rgb.r * k, rgb.g * k, rgb.b * k];
}

/** Effective visibility: three draws an object only when it and all its ancestors are visible. */
function walk(o: Obj, vis: boolean, cb: (o: Obj, vis: boolean) => void): void {
  const v = vis && o.visible !== false;
  cb(o, v);
  for (const c of o.children) walk(c, v, cb);
}

function ensure(m: Mirror, n: number): void {
  if (m.frame.length >= n) return;
  let len = m.frame.length;
  while (len < n) len *= 2;
  const f = new Float32Array(len);
  f.set(m.frame.subarray(0, m.frameLen));
  m.frame = f;
}

/** Version string of a geometry's attributes: when it changes, Unity gets the new vertex data. */
function geoVersion(g: Obj): string {
  const a = g.attributes;
  return `${a.position?.version ?? 0}.${a.normal?.version ?? 0}.${a.color?.version ?? 0}.${a.position?.count ?? 0}`;
}

// ------------------------------------------------------------------ frame

/**
 * Run the web scene's update and pack one frame.
 *
 * Layout (floats):
 *   header  [structVer, nMat, nNode, nDyn, nTex, nInst, 0, 0]
 *   mats    nMat  × 16: id, r g b, er eg eb, opacity, roughness, metalness, size, flags, 0 0 0 0
 *   nodes   nNode × 24: id, visible, renderOrder, drawStart, drawCount, 16 × matrix, 0 0 0
 *   dyn     nDyn  × (4 + data): gid, verts, flags (1 pos, 2 normal, 4 colour), 0, then pos / normal / colour
 *   texs    nTex  × 4: tid, width, height, 0   (Unity then copies the pixels)
 *   inst    nInst × (4 + count × 16 [+ count × 3]): nid, count, hasColour, 0, matrices, colours
 */
export function mirrorFrame(h: number, dt: number): number {
  const m = mirrors[h];
  if (!m) return 0;
  m.built.update(dt);
  m.root.updateMatrixWorld(true);
  // heavy meshes show the level of detail Unity's camera would see (as the web viewer does before drawing)
  if (m.cam) pickLods(m.root, m.cam);

  // nodes in draw order, and the structure signature
  const nodes: Obj[] = [];
  const vis: boolean[] = [];
  const sig: string[] = [];
  walk(m.root, true, (o, v) => {
    const k = kindOf(o);
    if (k < 0 || !o.geometry && k !== K_SPRITE) return;
    const mt = Array.isArray(o.material) ? o.material[0] : o.material;
    if (!mt) return;
    nodes.push(o);
    vis.push(v && mt.visible !== false);
    const g = o.geometry;
    const tex = mt.map;
    sig.push(`${idOf(m, o)}:${k}:${g ? idOf(m, g) : 0}:${g?.index?.count ?? -1}:${idOf(m, mt)}:${tex ? idOf(m, tex) : 0}`);
  });
  const s = sig.join(',');
  if (s !== m.sig) {
    m.sig = s;
    m.ver++;
    m.nodes = nodes;
    m.structure = '';
  }

  const mats = new Map<number, Obj>();
  for (const o of nodes) {
    const mt = Array.isArray(o.material) ? o.material[0] : o.material;
    mats.set(idOf(m, mt), mt);
  }

  // dynamic geometry and textures: what moved since Unity last read it. A geometry or texture seen for the first
  // time is only recorded: Unity fetches it whole with the structure.
  const dyn: Obj[] = [];
  const texUp: Obj[] = [];
  const seen = new Set<number>();
  for (const o of nodes) {
    const g = o.geometry;
    if (!g) continue;
    const gid = idOf(m, g);
    if (seen.has(gid)) continue;
    seen.add(gid);
    const v = geoVersion(g);
    const had = m.geoVer.get(gid);
    if (had !== v) { m.geoVer.set(gid, v); if (had !== undefined) dyn.push(g); }
  }
  const tseen = new Set<number>();
  m.texs.clear();   // only the textures the scene still uses (labels are replaced often: let the old ones go)
  for (const mt of mats.values()) {
    const t = mt.map;
    if (!t) continue;
    const tid = idOf(m, t);
    if (tseen.has(tid)) continue;
    tseen.add(tid);
    m.texs.set(tid, t);
    const had = m.texVer.get(tid);
    if (had !== t.version) { m.texVer.set(tid, t.version); if (had !== undefined) texUp.push(t); }
  }
  if (m.structure === '') {
    for (const k of [...m.geoVer.keys()]) if (!seen.has(k)) m.geoVer.delete(k);
    for (const k of [...m.texVer.keys()]) if (!tseen.has(k)) m.texVer.delete(k);
  }

  const insts = nodes.filter((o) => o.isInstancedMesh);
  let need = 8 + mats.size * 16 + nodes.length * 24 + texUp.length * 4;
  for (const g of dyn) {
    const n = g.attributes.position?.count ?? 0;
    need += 4 + n * 3 + (g.attributes.normal ? n * 3 : 0) + (g.attributes.color ? n * 3 : 0);
  }
  for (const o of insts) need += 4 + o.count * 19;
  ensure(m, need);
  const f = m.frame;
  let p = 8;
  f[0] = m.ver; f[1] = mats.size; f[2] = nodes.length; f[3] = dyn.length; f[4] = texUp.length; f[5] = insts.length; f[6] = 0; f[7] = 0;

  for (const [id, mt] of mats) {
    const t = matType(mt);
    const ek = mt.emissiveIntensity ?? 1;
    const c = srgb(mt.color);
    const e = mt.emissive ? srgb(mt.emissive, ek) : [0, 0, 0];
    let flags = 0;
    if (mt.transparent) flags |= 1;
    if (mt.depthWrite !== false) flags |= 2;
    if (mt.depthTest !== false) flags |= 4;
    if (mt.vertexColors) flags |= 8;
    if (mt.side === 2) flags |= 16;
    if (mt.side === 1) flags |= 32;
    if (mt.blending === 2) flags |= 64;
    if (t === M_POINTS && mt.sizeAttenuation === false) flags |= 128;
    if (mt.toneMapped === false) flags |= 256;
    // points: world diameter of a size-attenuated point under the web camera (40 degree field of view)
    const size = t === M_POINTS ? (mt.sizeAttenuation === false ? mt.size : mt.size * TAN20) : 0;
    f[p++] = id; f[p++] = c[0]; f[p++] = c[1]; f[p++] = c[2]; f[p++] = e[0]; f[p++] = e[1]; f[p++] = e[2];
    f[p++] = mt.opacity ?? 1; f[p++] = mt.roughness ?? 1; f[p++] = mt.metalness ?? 0; f[p++] = size; f[p++] = flags;
    f[p++] = t; f[p++] = mt.rotation ?? 0; f[p++] = 0; f[p++] = 0;
  }

  for (let i = 0; i < nodes.length; i++) {
    const o = nodes[i];
    const g = o.geometry;
    f[p++] = idOf(m, o);
    f[p++] = vis[i] ? 1 : 0;
    f[p++] = o.renderOrder ?? 0;
    f[p++] = g ? g.drawRange.start : 0;
    f[p++] = g ? (Number.isFinite(g.drawRange.count) ? g.drawRange.count : -1) : -1;
    putMatrix(f, p, o.matrixWorld.elements);
    p += 16;
    f[p++] = o.isSprite ? o.center.x : 0;
    f[p++] = o.isSprite ? o.center.y : 0;
    f[p++] = 0;
  }

  for (const g of dyn) {
    const a = g.attributes;
    const n = a.position?.count ?? 0;
    f[p++] = idOf(m, g); f[p++] = n; f[p++] = 1 | (a.normal ? 2 : 0) | (a.color ? 4 : 0); f[p++] = 0;
    p = putAttr(f, p, a.position, n, true);
    if (a.normal) p = putAttr(f, p, a.normal, n, true);
    if (a.color) p = putAttr(f, p, a.color, n, false);
  }

  for (const t of texUp) {
    const img = t.image;
    f[p++] = idOf(m, t); f[p++] = img?.width ?? 0; f[p++] = img?.height ?? 0; f[p++] = 0;
  }

  const tmp = new Float32Array(16);
  for (const o of insts) {
    const n = o.count;
    const col = o.instanceColor;
    f[p++] = idOf(m, o); f[p++] = n; f[p++] = col ? 1 : 0; f[p++] = 0;
    const w = o.matrixWorld.elements;
    const im = o.instanceMatrix.array;
    for (let i = 0; i < n; i++) {
      mul4(w, im.subarray(i * 16, i * 16 + 16), tmp, 0);
      putMatrix(f, p, tmp);
      p += 16;
    }
    if (col) {
      const ca = col.array;
      for (let i = 0; i < n * 3; i++) f[p++] = linToSrgb(ca[i]);
    }
  }
  m.frameLen = p;
  return p;
}

const linToSrgb = (c: number) => (c < 0.0031308 ? c * 12.92 : 1.055 * Math.pow(c, 0.41666) - 0.055);

/** Copy an attribute (any item size, possibly interleaved / normalised) as xyz, negating z when `flipZ`. */
function putAttr(f: Float32Array, p: number, a: Obj, n: number, flipZ: boolean): number {
  const sz = a.itemSize;
  for (let i = 0; i < n; i++) {
    const x = a.getX(i), y = sz > 1 ? a.getY(i) : 0, z = sz > 2 ? a.getZ(i) : 0;
    f[p++] = x; f[p++] = y; f[p++] = flipZ ? -z : z;
  }
  return p;
}

export function mirrorFrameData(h: number): Float32Array {
  const m = mirrors[h]!;
  return m.frame.subarray(0, m.frameLen);
}

// ------------------------------------------------------------------ structure

/** Nodes, geometries, materials and textures of the current frame. Also resets what Unity is known to hold. */
export function mirrorStructure(h: number): string {
  const m = mirrors[h];
  if (!m) return '{}';
  if (m.structure) return m.structure;
  m.partOf = partsOf(m.built);
  const nodes: any[] = [];
  const geoms = new Map<number, any>();
  const mats = new Map<number, any>();
  const texs = new Map<number, any>();
  m.geoms.clear();
  for (const o of m.nodes) {
    const k = kindOf(o);
    const mt = Array.isArray(o.material) ? o.material[0] : o.material;
    const g = o.geometry;
    const gid = g ? idOf(m, g) : 0;
    const mid = idOf(m, mt);
    let part = '';
    for (let x: Obj = o; x && !part; x = x.parent) part = m.partOf.get(x) ?? '';
    nodes.push({ id: idOf(m, o), k, g: gid, m: mid, part, name: o.name || '', cap: o.isInstancedMesh ? o.instanceMatrix.count : 0 });
    if (g && !geoms.has(gid)) {
      const a = g.attributes;
      geoms.set(gid, { id: gid, n: a.position?.count ?? 0, idx: g.index ? g.index.count : 0, nrm: !!a.normal, uv: !!a.uv, col: !!a.color });
      m.geoms.set(gid, g);
    }
    if (!mats.has(mid)) {
      const t = mt.map;
      const tid = t ? idOf(m, t) : 0;
      // a lit surface's texture with its repeat / offset (e.g. the striations of the myofibril in the muscle section)
      mats.set(mid, { id: mid, t: matType(mt), tex: tid, tr: t ? [t.repeat.x, t.repeat.y, t.offset.x, t.offset.y] : [1, 1, 0, 0] });
      if (t && !texs.has(tid)) {
        texs.set(tid, { id: tid, w: t.image?.width ?? 0, h: t.image?.height ?? 0, ws: t.wrapS === 1000 ? 1 : 0, wt: t.wrapT === 1000 ? 1 : 0 });
        m.texs.set(tid, t);
      }
    }
  }
  m.structure = JSON.stringify({ ver: m.ver, nodes, geoms: [...geoms.values()], mats: [...mats.values()], texs: [...texs.values()] });
  return m.structure;
}

/** Vertex data of a geometry: position, then normal, uv, colour as the structure says (xyz / uv / rgb). */
export function mirrorGeom(h: number, gid: number): Float32Array {
  const m = mirrors[h];
  const g = m?.geoms.get(gid);
  if (!g) return new Float32Array(0);
  const a = g.attributes;
  const n = a.position?.count ?? 0;
  const out = new Float32Array(n * 3 + (a.normal ? n * 3 : 0) + (a.uv ? n * 2 : 0) + (a.color ? n * 3 : 0));
  let p = putAttr(out, 0, a.position, n, true);
  if (a.normal) p = putAttr(out, p, a.normal, n, true);
  if (a.uv) for (let i = 0; i < n; i++) { out[p++] = a.uv.getX(i); out[p++] = a.uv.getY(i); }
  if (a.color) putAttr(out, p, a.color, n, false);
  return out;
}

/** Triangle / line indices of a geometry; triangles get their winding reversed for Unity. */
export function mirrorIndex(h: number, gid: number, triangles: boolean): Int32Array {
  const m = mirrors[h];
  const g = m?.geoms.get(gid);
  if (!g?.index) return new Int32Array(0);
  const src = g.index.array;
  const out = new Int32Array(src.length);
  for (let i = 0; i < src.length; i++) out[i] = src[i];
  if (triangles) for (let i = 0; i + 2 < out.length; i += 3) { const t = out[i + 1]; out[i + 1] = out[i + 2]; out[i + 2] = t; }
  return out;
}

const scratch = typeof document !== 'undefined' ? document.createElement('canvas') : null;
/** RGBA pixels of a texture, bottom row first (Unity's order). */
export function mirrorTex(h: number, tid: number): Uint8Array {
  const m = mirrors[h];
  const t = m?.texs.get(tid);
  const img = t?.image;
  if (!img || !img.width || !img.height || !scratch) return new Uint8Array(0);
  const w = img.width, hh = img.height;
  let data: Uint8ClampedArray;
  const g0 = img.getContext ? img.getContext('2d') : null;
  if (g0) data = g0.getImageData(0, 0, w, hh).data;
  else {
    scratch.width = w; scratch.height = hh;
    const g = scratch.getContext('2d')!;
    g.clearRect(0, 0, w, hh);
    g.drawImage(img, 0, 0);
    data = g.getImageData(0, 0, w, hh).data;
  }
  const out = new Uint8Array(w * hh * 4);
  const row = w * 4;
  for (let y = 0; y < hh; y++) out.set(data.subarray((hh - 1 - y) * row, (hh - y) * row), y * row);
  return out;
}

// ------------------------------------------------------------------ framing

/**
 * The web viewer's framing (viewer.fitView): look from (0.35, 0.25, 1) and back off until every object's
 * box corners and every label's position fit the view with a 6 % margin. Helpers flagged userData.frame
 * win when present; userData.noFrame, points and instanced meshes do not count.
 * Returns [cx, cy, cz, dist] in Unity axes and web units, or [] when there is nothing to frame.
 */
export function mirrorFit(h: number, aspect: number, fovDeg: number): string {
  const m = mirrors[h];
  if (!m) return '[]';
  const margin = m.margin;
  m.root.updateMatrixWorld(true);
  const pts: number[][] = [];
  const corner = (x: number, y: number, z: number, e: ArrayLike<number>) => {
    pts.push([e[0] * x + e[4] * y + e[8] * z + e[12], e[1] * x + e[5] * y + e[9] * z + e[13], e[2] * x + e[6] * y + e[10] * z + e[14]]);
  };
  const add = (o: Obj) => {
    if (o.userData?.noFrame) return;
    const g = o.geometry;
    const e = o.matrixWorld.elements;
    if (o.isSprite) pts.push([e[12], e[13], e[14]]);
    else if (g && !o.isPoints && !o.isInstancedMesh) {
      if (!g.boundingBox) g.computeBoundingBox();
      const b = g.boundingBox;
      if (b && b.min.x <= b.max.x)
        for (let i = 0; i < 8; i++) corner(i & 1 ? b.max.x : b.min.x, i & 2 ? b.max.y : b.min.y, i & 4 ? b.max.z : b.min.z, e);
    }
    for (const c of o.children) add(c);
  };
  const frames: Obj[] = [];
  m.root.traverse((o: Obj) => { if (o.userData?.frame) frames.push(o); });
  if (frames.length) frames.forEach(add);
  else add(m.root);
  if (!pts.length) return '[]';
  const dir = m.dir;
  // right = up × dir, up' = dir × right (camera up is +y)
  let right = [dir[2], 0, -dir[0]];
  const rl = Math.hypot(right[0], right[1], right[2]);
  right = right.map((v) => v / rl);
  const up = [dir[1] * right[2] - dir[2] * right[1], dir[2] * right[0] - dir[0] * right[2], dir[0] * right[1] - dir[1] * right[0]];
  const dot = (a: number[], b: number[]) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const mid = (ax: number[]) => {
    let lo = Infinity, hi = -Infinity;
    for (const q of pts) { const d = dot(q, ax); if (d < lo) lo = d; if (d > hi) hi = d; }
    return (lo + hi) / 2;
  };
  const cr = mid(right), cu = mid(up), cd = mid(dir);
  const c = [0, 1, 2].map((i) => right[i] * cr + up[i] * cu + dir[i] * cd);
  const tanV = Math.tan(((fovDeg * Math.PI) / 180) / 2) * (1 - margin);
  const tanH = tanV * aspect;
  let dist = 0;
  for (const q of pts) {
    const v = [q[0] - c[0], q[1] - c[1], q[2] - c[2]];
    dist = Math.max(dist, dot(v, dir) + Math.abs(dot(v, right)) / tanH, dot(v, dir) + Math.abs(dot(v, up)) / tanV);
  }
  if (!(dist > 0)) return '[]';
  // the look direction in Unity axes, so Unity can turn its orbit camera the same way
  return JSON.stringify([c[0], c[1], -c[2], dist, dir[0], dir[1], -dir[2]]);
}

/** Where Unity's camera is, in the scene's three.js world coordinates. */
export function mirrorCam(h: number, x: number, y: number, z: number): void {
  const m = mirrors[h];
  if (!m) return;
  (m.cam ??= new THREE.Vector3()).set(x, y, z);
}
