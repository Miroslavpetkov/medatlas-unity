// 2D schematic mode for Unity (instructions v4, part VI): the web version's own 2D view (src/physio/views2d) drawn as
// an SVG in an HTML layer over the 3D area of the Unity canvas. It reads the same running model instance as the 3D
// mirror and Unity's charts, so switching between 3D and 2D keeps the state, sliders, phase and numbers.
// Unity tells the layer where the 3D area is (in canvas pixels) and calls view2dFrame every frame.

/* eslint-disable @typescript-eslint/no-explicit-any */
import { VIEWS_2D } from '../medatlas/medAtlas-3D-main/src/physio/views2d/index';
import { Host2D } from '../medatlas/medAtlas-3D-main/src/physio/views2d/host';

const bg = (t: any): string => (t && typeof t === 'object' && 'bg' in t ? t.bg : t ?? '');
const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!);

const CSS = `
#u2d { position: fixed; z-index: 20; display: flex; flex-direction: column; border-radius: 14px; overflow: hidden;
  background: linear-gradient(180deg, #0b1220, #162238); border: 1px solid #ffffff1f; color: #f4f1e8;
  font-family: Inter, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; }
#u2d.hidden { display: none; }
#u2d svg.p2d { width: 100%; flex: 1 1 auto; min-height: 0; display: block; touch-action: none; user-select: none; -webkit-user-select: none; font-family: inherit; }
#u2d .p2d-part { cursor: pointer; }
#u2d .p2d-part.p2d-on { filter: drop-shadow(0 0 6px #ffe07a) drop-shadow(0 0 2px #ffe07a); }
#u2d svg.p2d.p2d-compact .p2d-wide, #u2d svg.p2d:not(.p2d-compact) .p2d-narrow { display: none; }
#u2d .p2d-foot { flex: none; padding: 0 8px 8px; display: flex; flex-wrap: wrap; gap: 6px; align-items: center; justify-content: space-between; }
#u2d .p2d-ref { font-size: 13px; color: #aab3c5; background: #0b1220d9; border: 1px solid #ffffff1f; border-radius: 999px; padding: 4px 10px; }
#u2d .p2d-steps { display: inline-flex; align-items: center; gap: 6px; background: #0b1220d9; border: 1px solid #ffffff1f; border-radius: 12px; padding: 3px; max-width: 100%; }
#u2d .p2d-steps button { min-width: 44px; min-height: 44px; font-size: 20px; border-radius: 10px; border: 1px solid #ffffff1f; background: #1b2538; color: #f4f1e8; cursor: pointer; }
#u2d .p2d-steps b { font-size: 15px; padding: 0 4px; max-width: 60vw; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#u2d .u2d-tip { position: absolute; max-width: min(340px, 80%); background: #0b1220f2; border: 1px solid #ffffff2a; border-radius: 12px; padding: 10px 12px;
  font-size: 15px; line-height: 1.35; pointer-events: none; box-shadow: 0 8px 24px #0008; }
#u2d .u2d-tip i { color: #aab3c5; }
#u2d .u2d-tip span { color: #aab3c5; }
`;

let box: HTMLDivElement | null = null;
let host: Host2D | null = null;
let tip: HTMLDivElement | null = null;
let open = -1;      // the instance shown
let pending = 0;    // a view module is loading
let step = -1;
let tipTimer = 0;

function ensureBox(): HTMLDivElement {
  if (box) return box;
  const st = document.createElement('style');
  st.textContent = CSS;
  document.head.appendChild(st);
  box = document.createElement('div');
  box.id = 'u2d';
  box.className = 'hidden';
  document.body.appendChild(box);
  box.addEventListener('click', (e) => {
    const b = (e.target as HTMLElement).closest('[data-p2dstep]') as HTMLElement | null;
    if (b) setStep(Number(b.dataset.p2dstep));
  });
  return box;
}

function setStep(d: number): void {
  const v = host?.view;
  if (!v?.steps?.length) return;
  step = Math.max(-1, Math.min(v.steps.length - 1, step + d));
  v.setStep?.(step);
  const b = box?.querySelector('#u2d-step');
  if (b) b.textContent = step < 0 ? 'Всички стъпки' : `${step + 1}/${v.steps.length} · ${bg(v.steps[step])}`;
  navigator.vibrate?.(8);
}

function showTip(html: string, x: number, y: number): void {
  if (!box) return;
  if (!tip) {
    tip = document.createElement('div');
    tip.className = 'u2d-tip';
    box.appendChild(tip);
  }
  tip.innerHTML = html;
  tip.style.display = '';
  const r = box.getBoundingClientRect();
  const w = tip.offsetWidth, h = tip.offsetHeight;
  tip.style.left = `${Math.max(8, Math.min(r.width - w - 8, x + 12))}px`;
  tip.style.top = `${Math.max(8, Math.min(r.height - h - 8, y + 12))}px`;
  clearTimeout(tipTimer);
  tipTimer = window.setTimeout(() => { if (tip) tip.style.display = 'none'; }, 5000);
}

/** 1 when this model has a 2D view. */
export function view2dHas(id: string): number {
  return id in VIEWS_2D ? 1 : 0;
}

/** Show the 2D view of instance h (s = the bridge's record of it: model, instance, values) over the 3D area. */
export function view2dOpen(h: number, s: any, actFn: (id: string) => void): number {
  if (!s || !(s.m.id in VIEWS_2D)) return 0;
  const b = ensureBox();
  if (open === h && host) { b.classList.remove('hidden'); return 1; }
  view2dClose();
  open = h;
  const ticket = ++pending;
  VIEWS_2D[s.m.id]().then((make) => {
    if (ticket !== pending || open !== h) return;
    const view = make();
    b.innerHTML = '';
    tip = null;
    step = -1;
    host = new Host2D(b, view, (pid, x, y) => {
      if (!pid) return;
      const p = (s.m.parts ?? []).find((q: any) => q.id === pid);
      if (p) showTip(`<b>${esc(bg(p.name))}</b>${p.latin ? ` <i>${esc(p.latin)}</i>` : ''}<br>${esc(bg(p.description))}${p.fn ? `<br><span>${esc(bg(p.fn))}</span>` : ''}`, x, y);
      const pa = s.m.partActions?.[pid];
      if (pa) actFn(pa);
      navigator.vibrate?.(8);
    });
    const foot = document.createElement('div');
    foot.className = 'p2d-foot';
    foot.innerHTML = `<span class="p2d-ref">📖 ${esc(bg(view.textbookRef))}</span>${
      view.steps?.length ? `<span class="p2d-steps"><button data-p2dstep="-1" aria-label="Предишна стъпка">‹</button><b id="u2d-step">Стъпки</b><button data-p2dstep="1" aria-label="Следваща стъпка">›</button></span>` : ''
    }`;
    b.appendChild(foot);
    b.classList.remove('hidden');
    host.resize();
  });
  return 1;
}

/** Where the 3D area is, in canvas pixels with the origin at the bottom left (Unity's Screen coordinates). */
export function view2dRect(x: number, y: number, w: number, h: number): void {
  const b = ensureBox();
  const cv = document.querySelector('canvas') as HTMLCanvasElement | null;
  if (!cv || !cv.width || !cv.height) return;
  const r = cv.getBoundingClientRect();
  const kx = r.width / cv.width, ky = r.height / cv.height;
  b.style.left = `${(r.left + x * kx).toFixed(1)}px`;
  b.style.top = `${(r.top + (cv.height - y - h) * ky).toFixed(1)}px`;
  b.style.width = `${Math.max(0, w * kx).toFixed(1)}px`;
  b.style.height = `${Math.max(0, h * ky).toFixed(1)}px`;
  host?.resize();
}

/** One frame: the view reads the instance's state (dt in real seconds). */
export function view2dFrame(h: number, s: any, dt: number, playing: number): void {
  if (!host || open !== h || !s) return;
  host.update({ model: s.m, inst: s.inst, values: s.v, t: s.inst.t, playing: !!playing }, dt);
}

export function view2dClose(): void {
  pending++;
  host?.destroy();
  host = null;
  open = -1;
  tip = null;
  if (box) {
    box.innerHTML = '';
    box.classList.add('hidden');
  }
}
