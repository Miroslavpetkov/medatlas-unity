// Bridge between Unity and the medAtlas physiology models (same code as the web version).
import { PHYSIO as BASE } from '../medatlas/medAtlas-3D-main/src/physio/list/index';
import { JOURNEYS } from './src/journeys';
const PHYSIO: any[] = [...JOURNEYS, ...BASE];

const bg = (t: any): any => (t && typeof t === 'object' && 'bg' in t ? t.bg : t);
const find = (id: string) => PHYSIO.find((m) => m.id === id)!;
const insts: any[] = [];

function def(m: any) {
  return {
    id: m.id, area: m.area, title: bg(m.title), summary: bg(m.summary), icon: m.icon, defaults: m.defaults,
    sliders: m.sliders.map((s: any) => ({ id: s.id, label: bg(s.label), min: s.min, max: s.max, step: s.step, unit: s.unit ?? '', hint: bg(s.hint) ?? '', dp: s.dp ?? -1 })),
    toggles: (m.toggles ?? []).map((s: any) => ({ id: s.id, label: bg(s.label), hint: bg(s.hint) ?? '' })),
    actions: (m.actions ?? []).map((s: any) => ({ id: s.id, label: bg(s.label), icon: s.icon, hint: bg(s.hint) ?? '' })),
    presets: m.presets.map((p: any) => ({ id: p.id, label: bg(p.label), icon: p.icon, text: bg(p.text), values: p.values })),
    charts: m.charts.map((c: any) => ({ title: bg(c.title), min: c.min, max: c.max, window: c.window,
      channels: c.channels.map((ch: any) => ({ id: ch.id, label: bg(ch.label), color: ch.color, unit: ch.unit ?? '' })),
      guides: (c.guides ?? []).map((g: any) => ({ y: g.y, label: g.label ?? '', color: g.color ?? '' })) })),
    xy: (m.xy ?? []).map((x: any) => ({ title: bg(x.title), x: x.x, y: x.y, xl: x.xl, yl: x.yl, xr: x.xr, yr: x.yr, color: x.color, trail: x.trail,
      hasCurve: !!x.curve, normal: x.normal ?? null })),
    stats: m.stats.map((s: any) => ({ id: s.id, label: bg(s.label), unit: s.unit ?? '', dp: s.dp ?? 0, normal: s.normal ?? null })),
    speeds: m.speeds, learn: (m.learn ?? []).map(bg),
    parts: (m.parts ?? []).map((p: any) => ({ id: p.id, name: bg(p.name), latin: p.latin ?? '', description: bg(p.description) ?? '', fn: bg(p.fn) ?? '' })),
    sampleDt: m.sampleDt ?? 0.004,
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
    while (left >= sd) { s.inst.advance(sd); left -= sd; samples.push(s.inst.read()); }
    s.acc = left;
    let out = samples;
    if (samples.length > maxSamples) { out = []; const k = samples.length / maxSamples; for (let i = 0; i < maxSamples; i++) out.push(samples[Math.floor(i * k)]); }
    const ph = s.inst.phase();
    const st = s.inst.stats();
    return JSON.stringify({ t: s.inst.t, samples: out, now: samples.length ? samples[samples.length - 1] : s.inst.read(), stats: st,
      phase: { title: bg(ph.title), text: bg(ph.text), color: ph.color ?? '' } });
  },
  curve: (h: number, i: number) => { const s = insts[h]; const x = s.m.xy[i]; return JSON.stringify(x.curve ? x.curve(s.v) : []); },
};
(globalThis as any).MedPhysio = api;
