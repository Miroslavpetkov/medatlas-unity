// Bridge between Unity and the medAtlas physiology models (same code as the web version).
import { PHYSIO as BASE } from '../medatlas/medAtlas-3D-main/src/physio/list/index';
import { drugsFor } from '../medatlas/medAtlas-3D-main/src/physio/drugs';
import { muscleOpen, muscleAct, muscleView, musclePart, muscleChart, muscleChartLen } from './muscle';
import { mirrorBuild, mirrorDispose, mirrorFrame, mirrorFrameData, mirrorStructure, mirrorGeom, mirrorIndex, mirrorTex, mirrorFit, mirrorCam } from './mirror';
import { setLodBias } from '../medatlas/medAtlas-3D-main/src/core/lod';

// The web scenes load real anatomy (real/<topic>.bin, body/…) with relative URLs. The Unity page lives on another
// path, so those requests go to the published web version (same origin on GitHub Pages, and it allows CORS).
const WEB = 'https://miroslavpetkov.github.io/medatlas3d-site-public/';
if (typeof window !== 'undefined' && window.fetch && !(window as any).__medMirrorFetch) {
  (window as any).__medMirrorFetch = true;
  const f0 = window.fetch.bind(window);
  window.fetch = ((input: any, init?: any) => {
    if (typeof input === 'string' && /^(\.\/)?(real|body)\//.test(input)) input = ((window as any).MEDMIRROR_WEB ?? WEB) + input.replace(/^\.\//, '');
    return f0(input, init);
  }) as typeof fetch;
}
// "Пътят през тялото" (the journeys) now live in the web list too (web #88–#93), so Unity uses them from there.
// Journeys first, so they open the list as before.
const PHYSIO: any[] = [...BASE.filter((m: any) => m.area === 'Journey'), ...BASE.filter((m: any) => m.area !== 'Journey')];

/** The web journey scene and Unity's JourneyView read the same q/s/k, b/bs, ur, vo protocol, with two differences:
 *  the kinds 6/7 are swapped (web 6 = vomitus, 7 = faeces; Unity 6 = faeces, 7 = vomitus) and Unity lights more
 *  organ parts. The extra glows are derived here from what the model already reports, so the models stay pure. */
const COLON = ['Ascending colon', 'Transverse colon', 'Descending colon', 'Sigmoid colon'];
function forUnity(id: string, r: Record<string, number>): Record<string, number> {
  const o: Record<string, number> = { ...r };
  const up = (k: string, v: number) => { o[k] = Math.max(o[k] ?? 0, v); };
  for (let i = 0; i < 24; i++) {
    const k = o['k' + i];
    if (k === 6) o['k' + i] = 7; else if (k === 7) o['k' + i] = 6;
    const p = o['q' + i];
    if (p === undefined || p < 0 || p >= 11) continue;
    if (p < 1) up('G_Tongue', 1);
    else if (p < 3) up('G_pharynx', 1);
    else if (p < 4) up('G_Oesophagus', 0.8);
    else if (p >= 5 && p < 6) up('G_Duodenum', 0.6);
    else if (p >= 7) up('G_' + COLON[Math.min(3, Math.floor(p) - 7)], 0.6);
  }
  if ('ur' in r) up('G_Kidney', Math.max(0.1, r.ur));
  if (id === 'swallow') {
    const ph = (o.G_pharynx ?? 0) > 0 ? 1 : 0; // soft palate and epiglottis close the airways while the bolus is in the pharynx
    up('G_Epiglottis', ph); up('G_Soft palate', ph); up('G_Uvula', ph);
    if ((r.br ?? 10) < 5) up('G_Trachea', 0.6);
  }
  if (id === 'defecation' && (r.eas ?? 0) > 120) up('G_sphincter', 1);
  if (id === 'vomit') {
    if ((r.br ?? 1) < 0.5) up('G_Trachea', 0.6);
    if ((r.les ?? 100) < 50) up('G_Oesophagus', 1);
    if ((r.ues ?? 100) < 50) up('G_Epiglottis', 1);
  }
  return o;
}
const readFor = (s: any) => (s.m.area === 'Journey' ? forUnity(s.m.id, s.inst.read()) : s.inst.read());

const bg = (t: any): any => (t && typeof t === 'object' && 'bg' in t ? t.bg : t);
const find = (id: string) => PHYSIO.find((m) => m.id === id)!;
const insts: any[] = [];

function def(m: any) {
  return {
    id: m.id, area: m.area, title: bg(m.title), summary: bg(m.summary), icon: m.icon, defaults: m.defaults,
    sliders: m.sliders.map((s: any) => ({ id: s.id, label: bg(s.label), min: s.min, max: s.max, step: s.step, unit: bg(s.unit) ?? '', hint: bg(s.hint) ?? '', dp: s.dp ?? -1 })),
    toggles: (m.toggles ?? []).map((s: any) => ({ id: s.id, label: bg(s.label), hint: bg(s.hint) ?? '' })),
    actions: (m.actions ?? []).map((s: any) => ({ id: s.id, label: bg(s.label), icon: s.icon, hint: bg(s.hint) ?? '' })),
    presets: m.presets.map((p: any) => ({ id: p.id, label: bg(p.label), icon: p.icon, text: bg(p.text), values: p.values })),
    charts: m.charts.map((c: any) => ({ title: bg(c.title), min: c.min, max: c.max, window: c.window,
      channels: c.channels.map((ch: any) => ({ id: ch.id, label: bg(ch.label), color: ch.color, unit: bg(ch.unit) ?? '' })),
      guides: (c.guides ?? []).map((g: any) => ({ y: g.y, label: g.label ?? '', color: g.color ?? '' })) })),
    xy: (m.xy ?? []).map((x: any) => ({ title: bg(x.title), x: x.x, y: x.y, xl: x.xl, yl: x.yl, xr: x.xr, yr: x.yr, color: x.color, trail: x.trail,
      hasCurve: !!x.curve, normal: x.normal ?? null })),
    stats: m.stats.map((s: any) => ({ id: s.id, label: bg(s.label), unit: bg(s.unit) ?? '', dp: s.dp ?? 0, normal: s.normal ?? null })),
    speeds: m.speeds, learn: (m.learn ?? []).map(bg),
    parts: (m.parts ?? []).map((p: any) => ({ id: p.id, name: bg(p.name), latin: p.latin ?? '', description: bg(p.description) ?? '', fn: bg(p.fn) ?? '' })),
    sampleDt: m.sampleDt ?? 0.004,
    // the drug layer: a time course laid over sliders the model already has (web drugs.ts); Unity applies it the same way
    drugs: drugsFor(m.id).map((d: any) => ({ id: d.id, label: bg(d.label), group: bg(d.group), how: bg(d.how), onset: d.onset, peak: d.peak, half: d.half, moves: d.moves })),
  };
}

const api = {
  list: () => JSON.stringify(PHYSIO.map((m) => ({ id: m.id, title: bg(m.title), area: m.area, icon: m.icon, summary: bg(m.summary), presets: m.presets.length, charts: m.charts.length + (m.xy?.length ?? 0), controls: m.sliders.length + (m.toggles?.length ?? 0) }))),
  def: (id: string) => JSON.stringify(def(find(id))),
  create: (id: string, valuesJson: string) => {
    const m = find(id);
    const v = { ...m.defaults, ...(valuesJson ? JSON.parse(valuesJson) : {}) };
    insts.push({ m, inst: m.create(v), v, acc: 0 });
    return insts.length - 1;
  },
  set: (h: number, valuesJson: string) => { const s = insts[h]; s.v = { ...s.m.defaults, ...JSON.parse(valuesJson) }; s.inst.set(s.v); },
  act: (h: number, id: string) => { const s = insts[h]; s.inst.act?.(id); },
  /** Advance by dt model seconds; returns the chart samples taken every sampleDt, plus stats and phase. */
  step: (h: number, dt: number, maxSamples: number) => {
    const s = insts[h]; const sd = s.m.sampleDt ?? 0.004;
    const samples: any[] = [];
    let left = dt + s.acc;
    while (left >= sd) { s.inst.advance(sd); left -= sd; samples.push(readFor(s)); }
    s.acc = left;
    let out = samples;
    if (samples.length > maxSamples) { out = []; const k = samples.length / maxSamples; for (let i = 0; i < maxSamples; i++) out.push(samples[Math.floor(i * k)]); }
    const ph = s.inst.phase();
    const st = s.inst.stats();
    return JSON.stringify({ t: s.inst.t, samples: out, now: samples.length ? samples[samples.length - 1] : readFor(s), stats: st,
      phase: { title: bg(ph.title), text: bg(ph.text), color: ph.color ?? '' } });
  },
  // the scene mirror (mirror.ts): the web 3D scene of instance h, drawn by Unity
  sceneBuild: (h: number) => mirrorBuild(h, insts[h]),
  sceneDispose: (h: number) => mirrorDispose(h),
  sceneFrame: (h: number, dt: number) => mirrorFrame(h, dt),
  sceneFrameData: (h: number) => mirrorFrameData(h),
  sceneStructure: (h: number) => mirrorStructure(h),
  sceneGeom: (h: number, gid: number) => mirrorGeom(h, gid),
  sceneIndex: (h: number, gid: number, tri: number) => mirrorIndex(h, gid, !!tri),
  sceneTex: (h: number, tid: number) => mirrorTex(h, tid),
  sceneFit: (h: number, aspect: number, fov: number) => mirrorFit(h, aspect, fov),
  sceneCam: (h: number, x: number, y: number, z: number) => mirrorCam(h, x, y, z),
  lodBias: (b: number) => setLodBias(b),
  // the muscle section (#muscle on the web): its scene is mirrored under the returned handle
  muscleOpen: (tab: string, phone: number) => muscleOpen(tab, phone),
  muscleAct: (cmd: string, arg: string, phone: number) => muscleAct(cmd, arg, phone),
  muscleView: () => muscleView(),
  musclePart: (id: string) => musclePart(id),
  muscleChart: (kind: string, w: number, h: number) => muscleChart(kind, w, h),
  muscleChartLen: (x: number, w: number) => muscleChartLen(x, w),
  curve: (h: number, i: number) => { const s = insts[h]; const x = s.m.xy[i]; return JSON.stringify(x.curve ? x.curve(s.v) : []); },
};
(globalThis as any).MedPhysio = api;
