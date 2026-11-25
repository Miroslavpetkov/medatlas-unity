// The muscle section of the web app (#muscle: sarcomere, cross-bridge cycle, triad, length–tension, motor units,
// smooth muscle pathway and types) for Unity. The same scenes, state, steps and texts as src/muscle/screen.ts; the
// 3D goes through the scene mirror and Unity draws the panel from muscleView().

/* eslint-disable @typescript-eslint/no-explicit-any */
import { GROUPS, PARTS, SECTION_TITLE, SMOOTH_STEPS, TABS, tabById, type StepText, type TabDef } from '../medatlas/medAtlas-3D-main/src/muscle/content';
import type { MuScene, MuState } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/common';
import { sarcomereScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/sarcomere';
import { crossBridgeScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/crossbridge';
import { eccScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/ecc';
import { smoothPathScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/smoothPath';
import { smoothUnitsScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/smoothUnits';
import { motorUnitsScene } from '../medatlas/medAtlas-3D-main/src/muscle/scenes/motorUnits';
import { MotorPool } from '../medatlas/medAtlas-3D-main/src/physio/models/motorunits';
import { lengthTension, overlap, passiveTension } from '../medatlas/medAtlas-3D-main/src/physio/models/muscle';
import { LTChart, LT_MAX, LT_MIN } from '../medatlas/medAtlas-3D-main/src/muscle/ltChart';
import { EmgChart } from '../medatlas/medAtlas-3D-main/src/muscle/emgChart';
import { mirrorAdopt, mirrorCtx, mirrorDispose } from './mirror';

const bg = (t: any): string => (t && typeof t === 'object' && 'bg' in t ? t.bg : t ?? '');
const num = (x: number, dp = 2) => x.toFixed(dp).replace('.', ',');

/** Handles of the muscle scenes live above the model instances. */
export const MUSCLE_BASE = 100000;

let tab: TabDef = TABS[0];
let scene: MuScene | null = null;
let handle = -1;
let opened = 0;
const pool = new MotorPool(0.3);
const st: MuState = {
  playing: false, step: 0, stepT: 0, contract: false, loop: false, len: 2.1, smoothMode: 'contract', units: 'both', stim: 0, drive: 0.3, labels: true,
};

const stepsOf = (d: TabDef): StepText[] | undefined => (d.id === 'smooth-path' ? SMOOTH_STEPS[st.smoothMode] : d.steps);

function resetTabState(phone: boolean): void {
  st.step = 0;
  st.stepT = 0;
  st.playing = !!stepsOf(tab) || tab.id === 'smooth-units';
  st.contract = false;
  st.loop = false;
  if (tab.id === 'smooth-units') st.units = phone ? 'single' : 'both';
}

function make(): MuScene {
  const c = mirrorCtx as any;
  switch (tab.id) {
    case 'sarcomere': return sarcomereScene(c, st, 'live');
    case 'graph': return sarcomereScene(c, st, 'graph');
    case 'cycle': return crossBridgeScene(c, st);
    case 'ecc': return eccScene(c, st);
    case 'smooth-path': return smoothPathScene(c, st);
    case 'smooth-units': return smoothUnitsScene(c, st);
    case 'units': return motorUnitsScene(c, st, pool);
  }
}

/** Build the scene of the current tab under a new handle (the old one is dropped). */
function build(): number {
  if (handle >= 0) mirrorDispose(handle);
  scene = make();
  handle = MUSCLE_BASE + ++opened;
  const sc = scene;
  // the screen's own loop (screen.ts loop()): steps advance in Play mode, the motor pool runs on the units tab
  const built = {
    root: sc.root,
    parts: sc.parts,
    update: (dt: number) => {
      const steps = stepsOf(tab);
      if (steps && sc.stepDur) {
        st.stepT += dt;
        if (st.playing && st.stepT > sc.stepDur + 0.7) { st.step = (st.step + 1) % steps.length; st.stepT = 0; }
      }
      sc.update(dt);
      if (tab.id === 'units') pool.advance(dt);
    },
  };
  // flat diagrams are read straight on, slightly from above (screen.ts frontView); fitView margin 0.04
  mirrorAdopt(handle, built, { dir: sc.front ? [0, 0.12, 1] : [0.35, 0.25, 1], margin: 0.04 });
  return handle;
}

/** Open the section on a tab ('' = the first). Returns the scene handle. */
export function muscleOpen(tabId: string, phone: number): number {
  tab = tabById(tabId) ?? TABS[0];
  resetTabState(!!phone);
  return build();
}

/**
 * A button or slider of the panel. Returns the scene handle: a new one when the 3D had to be rebuilt
 * (another tab, or the single-/multi-unit switch), so Unity mirrors the new scene.
 */
export function muscleAct(cmd: string, arg: string, phone: number): number {
  const v = Number(arg);
  switch (cmd) {
    case 'tab': {
      const next = tabById(arg);
      if (next && next.id !== tab.id) { tab = next; resetTabState(!!phone); return build(); }
      break;
    }
    case 'group': {
      if (arg !== tab.group) { tab = TABS.find((x) => x.group === arg) ?? tab; resetTabState(!!phone); return build(); }
      break;
    }
    case 'step': st.playing = false; goStep(v); break;
    case 'prev': st.playing = false; goStep(st.step - 1); break;
    case 'next': st.playing = false; goStep(st.step + 1); break;
    case 'play': st.playing = !st.playing; break;
    case 'contract': st.contract = !st.contract; st.loop = false; break;
    case 'loop': st.loop = !st.loop; st.playing = st.loop; break;
    case 'stim': st.stim++; break;
    case 'labels': st.labels = !st.labels; break;
    case 'len': st.len = Math.max(LT_MIN, Math.min(LT_MAX, v)); break;
    case 'drive': st.drive = Math.max(0, Math.min(1, v)); pool.setDrive(st.drive); break;
    case 'mode': st.smoothMode = arg === 'relax' ? 'relax' : 'contract'; st.step = 0; st.stepT = 0; st.playing = true; break;
    case 'units': st.units = (arg as MuState['units']) || 'both'; return build();
  }
  return handle;
}

function goStep(i: number): void {
  const steps = stepsOf(tab);
  if (!steps) return;
  st.step = ((i % steps.length) + steps.length) % steps.length;
  st.stepT = 0;
}

/** Everything the panel shows, as the web screen renders it (hud, summary, body), in Bulgarian. */
export function muscleView(): string {
  const steps = stepsOf(tab);
  let chip = '';
  const ctl: any[] = [];
  if (steps) {
    chip = `Стъпка ${st.step + 1}/${steps.length} · ${bg(steps[st.step].title)}`;
    ctl.push({ id: 'prev', label: '◀' }, { id: 'play', label: st.playing ? 'Пауза' : 'Пусни', on: st.playing }, { id: 'next', label: '▶' });
  } else if (tab.id === 'sarcomere') {
    chip = st.contract ? 'Съкращение: Ca²⁺ висок, филаментите се плъзгат' : 'Покой: Ca²⁺ нисък';
    ctl.push({ id: 'contract', label: st.contract ? 'Отпусни' : 'Съкрати', on: st.contract, main: true }, { id: 'loop', label: 'Повтаряй', on: st.loop });
  } else if (tab.id === 'units') {
    chip = `${pool.active}/${pool.units.length} единици · сила ${Math.round(pool.force * 100)} %`;
  } else if (tab.id === 'graph') {
    chip = `${num(st.len)} µm · сила ${Math.round(lengthTension(st.len) * 100)} %`;
  } else if (tab.id === 'smooth-units') {
    chip = st.units === 'single' ? 'Висцерална (single-unit)' : st.units === 'multi' ? 'Мултиюнит' : 'Двата един до друг';
    ctl.push({ id: 'stim', label: 'Дразни', main: true }, { id: 'play', label: 'Повтаряй', on: st.playing });
  }

  const cards: any[] = [];
  let slider: any = null;
  let sw: any = null;
  const presets: any[] = [];
  let chart = '';
  if (tab.id === 'sarcomere') {
    const r = scene?.readout?.();
    if (r) cards.push({ k: 'Саркомер', v: num(r.len), u: 'µm' }, { k: 'A ивица', v: num(r.a), u: 'µm' }, { k: 'I ивица', v: num(r.i), u: 'µm' }, { k: 'H зона', v: num(r.h), u: 'µm' });
  } else if (tab.id === 'graph') {
    slider = { id: 'len', label: 'Дължина на саркомера', min: LT_MIN, max: LT_MAX, step: 0.01, value: st.len, text: `${num(st.len)} µm` };
    const l = st.len;
    cards.push(
      { k: 'Застъпване', v: String(Math.round(Math.min(100, (overlap(l) / 0.725) * 100))), u: '%' },
      { k: 'Активна', v: String(Math.round(lengthTension(l) * 100)), u: '%' },
      { k: 'Пасивна', v: String(Math.round(passiveTension(l) * 100)), u: '%' },
    );
    for (const x of [1.6, 2.1, 2.8, 3.65]) presets.push({ cmd: 'len', arg: String(x), label: `${num(x)} µm` });
    chart = 'lt';
  } else if (tab.id === 'units') {
    slider = { id: 'drive', label: 'Волево усилие (задвижване)', min: 0, max: 1, step: 0.01, value: st.drive, text: `${Math.round(st.drive * 100)} %` };
    const ty = pool.types;
    cards.push(
      { k: 'Единици', v: String(pool.active), u: `/ ${pool.units.length}` },
      { k: 'Сила', v: String(Math.round(pool.force * 100)), u: '%' },
      { k: 'Типове', v: ty.length ? ty.join('+') : '—', u: '' },
      { k: 'Честота', v: String(Math.round(pool.meanRate)), u: 'Hz' },
    );
    for (const d of [0.05, 0.3, 0.6, 1]) presets.push({ cmd: 'drive', arg: String(d), label: `${Math.round(d * 100)} %` });
    chart = 'emg';
  } else if (tab.id === 'smooth-units') {
    sw = { cmd: 'units', options: [['both', 'Двата'], ['single', 'Висцерална'], ['multi', 'Мултиюнит']].map(([id, l]) => ({ id, label: l, on: st.units === id })) };
  } else if (tab.id === 'smooth-path') {
    sw = { cmd: 'mode', options: [['contract', 'Съкращение'], ['relax', 'Отпускане']].map(([id, l]) => ({ id, label: l, on: st.smoothMode === id })) };
  }
  return JSON.stringify({
    section: bg(SECTION_TITLE),
    group: tab.group,
    groups: (['skeletal', 'smooth'] as const).map((g) => ({ id: g, label: bg(GROUPS[g]), on: g === tab.group })),
    tab: tab.id,
    tabs: TABS.filter((x) => x.group === tab.group).map((x) => ({ id: x.id, label: bg(x.short), on: x.id === tab.id })),
    title: bg(tab.title),
    intro: bg(tab.intro),
    facts: (tab.facts ?? []).map(bg),
    chip, ctl, cards, slider, sw, presets, chart,
    steps: steps ? { n: steps.length, cur: st.step, title: bg(steps[st.step].title), text: bg(steps[st.step].text) } : null,
    labels: st.labels,
    parts: Object.entries(PARTS as any).map(([id, p]: [string, any]) => ({ id, name: bg(p.name), latin: p.latin ?? '', description: bg(p.description) })),
  });
}

/** Tap on a part of the 3D: its card (name, Latin, description), or null. */
export function musclePart(id: string): string {
  const p = (PARTS as any)[id];
  return p ? JSON.stringify({ name: bg(p.name), latin: p.latin ?? '', description: bg(p.description) }) : 'null';
}

// ---------- the two charts, drawn by the web code onto a canvas whose pixels Unity shows ----------

let ltCanvas: HTMLCanvasElement | null = null;
let lt: LTChart | null = null;
let emgCanvas: HTMLCanvasElement | null = null;
let emg: EmgChart | null = null;

function sized(c: HTMLCanvasElement, chart: any, w: number, h: number): void {
  if (c.width === w * 2 && c.height === h * 2) return;
  c.width = w * 2; c.height = h * 2;
  chart.w = w; chart.h = h;
  chart.ctx.setTransform(2, 0, 0, 2, 0, 0);
}

/** Draw the tab's chart at w × h CSS pixels (2× pixels) and return its RGBA, bottom row first. */
export function muscleChart(kind: string, w: number, h: number): Uint8Array {
  let c: HTMLCanvasElement;
  if (kind === 'lt') {
    if (!lt) {
      ltCanvas = document.createElement('canvas');
      lt = new LTChart(ltCanvas, { x: 'дължина на саркомера, µm', y: 'сила, %', active: 'активна', passive: 'пасивна', optimum: 'оптимум', comma: true });
    }
    c = ltCanvas!;
    sized(c, lt, w, h);
    lt.len = st.len;
    lt.draw();
  } else {
    if (!emg) {
      emgCanvas = document.createElement('canvas');
      emg = new EmgChart(emgCanvas, { title: 'Повърхностна ЕМГ', time: 'последната 1 s' });
      emg.scale = 0.9;
    }
    c = emgCanvas!;
    sized(c, emg, w, h);
    emg.draw(pool.trace());
  }
  const g = c.getContext('2d')!;
  const data = g.getImageData(0, 0, c.width, c.height).data;
  const out = new Uint8Array(c.width * c.height * 4);
  const row = c.width * 4;
  for (let y = 0; y < c.height; y++) out.set(data.subarray((c.height - 1 - y) * row, (c.height - y) * row), y * row);
  return out;
}

/** Length for a tap / drag at x (0–1 across the chart's plot area), as the web chart does. */
export function muscleChartLen(x: number, w: number): number {
  const padL = 44, padR = 12;
  const f = (x * w - padL) / (w - padL - padR);
  return Math.round((LT_MIN + Math.max(0, Math.min(1, f)) * (LT_MAX - LT_MIN)) * 100) / 100;
}
