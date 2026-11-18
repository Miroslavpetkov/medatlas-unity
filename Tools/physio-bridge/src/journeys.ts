// "Пътят през тялото" — physiology journeys for Med Simulation 3D (Unity).
// Facts: Витанова (ред.), Физиология на човека, 2008 — гл. 9 (храносмилане, с. 195–235),
// гл. 13 (бъбреци, с. 297–298) и гл. 15 (воден баланс, с. 303–304). Where the textbook gives
// no number the value is marked "ориентировъчно" in the texts.
//
// 3D protocol read by Unity (JourneyView): positions are in "segment units" along the food path
// 0 mouth, 1–2 pharynx, 3 oesophagus, 4 stomach, 5 duodenum, 6 small intestine, 7 ascending colon,
// 8 transverse colon, 9 descending colon, 10 sigmoid colon and rectum, 11 = outside (anus).
//   q<i>/s<i>/k<i>  food portions (position, size 0..1, kind)      i < 24
//   b<i>/bs<i>      gas bubbles (position, size 0..1)                i < 16
//   G_<part>        glow of an organ part (0..1)
//   ur, vo          urine flow from the kidneys / voiding through the urethra (0..1)
//   fx              a burst of gas leaving the body (0..1)

type Tx = { en: string; bg: string };
type V = Record<string, number | boolean>;
const tx = (en: string, bg: string): Tx => ({ en, bg });
const H = 3600, MIN = 60;
const clamp = (x: number, a: number, b: number) => Math.max(a, Math.min(b, x));
const num = (v: V, k: string, d: number) => (typeof v[k] === 'number' ? (v[k] as number) : d);
const bool = (v: V, k: string, d: boolean) => (typeof v[k] === 'boolean' ? (v[k] as boolean) : d);
const fmtH = (s: number) => { const h = Math.floor(s / H), m = Math.floor((s % H) / MIN); return h > 0 ? `${h} ч ${m} мин` : `${m} мин`; };

const STAGE_BG = ['Устна кухина', 'Фаринкс', 'Фаринкс', 'Хранопровод', 'Стомах', 'Дуоденум', 'Тънко черво', 'Възходящо дебело черво', 'Напречно дебело черво', 'Низходящо дебело черво', 'Сигма и ректум', 'Навън'];
// Consistency of the colon content along the colon (Фиг. 9.27)
function consistency(p: number): string {
  if (p < 7) return '—';
  if (p < 7.5) return 'течна';
  if (p < 8) return 'полутечна';
  if (p < 8.5) return 'кашеста';
  if (p < 9) return 'полукашеста';
  if (p < 10) return 'твърда';
  return 'много твърда';
}

function writeQ(out: Record<string, number>, items: { pos: number; size: number; kind: number }[], n = 24) {
  for (let i = 0; i < n; i++) {
    const it = items[i];
    out['q' + i] = it ? it.pos : -1; out['s' + i] = it ? it.size : 0; out['k' + i] = it ? it.kind : 0;
  }
}
function writeB(out: Record<string, number>, items: { pos: number; size: number }[], n = 16) {
  for (let i = 0; i < n; i++) { const it = items[i]; out['b' + i] = it ? it.pos : -1; out['bs' + i] = it ? it.size : 0; }
}

// ======================================================================= 1. FOOD JOURNEY
interface Portion { meal: number; kind: number; vol0: number; vol: number; pos: number; tIn: number; chew: number; oes: number; gExit: number; duo: number; si: number; col: number; res: number; solid: number; water: number; out: boolean; outT: number }

class FoodJourney {
  t = 0; p: V; portions: Portion[] = []; meals = 0; lastMeal = 0;
  waterSi = 0; waterCol = 0; faeces = 0; urgeT = -1; mass: number[] = []; massT = 0; lastMass = -1;
  constructor(v: V) { this.p = v; this.eat(); }
  eat(kindOverride?: number) {
    const v = this.p; const kind = kindOverride ?? num(v, 'kind', 3);
    const vol = kindOverride === 0 ? 250 : num(v, 'vol', 500);
    const solid = kindOverride === 0 ? 0 : num(v, 'solid', 0.75), fat = kindOverride === 0 ? 0 : num(v, 'fat', 0.3), fibre = kindOverride === 0 ? 0 : num(v, 'fibre', 0.3);
    const K = vol < 300 ? 6 : 10;
    const bite = solid < 0.15 ? 2 : 6 + 22 * solid;        // a gulp vs a chewed mouthful (ориентировъчно)
    const speed = solid > 0.5 ? 3 : 4;                       // peristaltic wave 2–4 cm/s (с. 206)
    const lag = solid * 30 * MIN;                            // solid food must be ground to < 2 mm first (с. 206)
    const tau = (15 + 70 * solid + 120 * fat) * MIN / Math.LN2;   // fat stays longest (с. 206); ~2 h for a mixed meal
    const si = (3 + 2 * fat - 0.5 * fibre) * H;              // fat moves slower than fibre in the small intestine (с. 208)
    const col = (30 - 12 * fibre) * H;                       // ориентировъчно
    const res = clamp(0.03 + 0.12 * solid + 0.25 * fibre, 0, 0.5);
    this.meals++; this.lastMeal = this.t;
    for (let i = 0; i < K; i++) {
      const q = (i + 0.5) / K;
      this.portions.push({ meal: this.meals, kind, vol0: vol / K, vol: vol / K, pos: -1, tIn: this.t + i * bite, chew: bite, oes: 25 / speed, gExit: lag + tau * -Math.log(1 - q) + 60,
        duo: 4 * MIN, si: si * (0.85 + 0.3 * ((i * 7) % K) / K), col: col * (0.85 + 0.3 * ((i * 3) % K) / K), res, solid, water: 1 - solid, out: false, outT: 0 });
    }
    if (this.portions.length > 60) this.portions.splice(0, this.portions.length - 60);
  }
  toilet() {
    let any = false;
    for (const q of this.portions) if (!q.out && q.pos >= 10.3 && q.pos < 11) { q.out = true; q.outT = this.t; any = true; }
    if (any) this.urgeT = -1;
  }
  rectFill() { let s = 0; for (const q of this.portions) if (!q.out && q.pos >= 10.3 && q.pos < 11) s += q.vol; return s / 300; } // capacity ~300 ml (ориентировъчно)
  advance(dt: number) {
    this.t += dt; const t = this.t;
    // mass movements 1–3 times a day (с. 209): here every 8 h
    if (Math.floor(t / (8 * H)) !== this.lastMass) { this.lastMass = Math.floor(t / (8 * H)); if (t > 60) this.massT = t; }
    const mass = t - this.massT < 40;
    for (const q of this.portions) {
      if (q.out) { q.pos = 10.9 + (t - q.outT) / 25; continue; }
      const age = t - q.tIn;
      if (age < 0) { q.pos = -1; continue; }
      if (age < q.chew) { q.pos = 0.85 * age / q.chew; continue; }
      const a2 = age - q.chew;
      if (a2 < 0.8) { q.pos = 1 + 2 * a2 / 0.8; continue; }                     // pharyngeal phase < 1 s
      const a3 = a2 - 0.8;
      if (a3 < q.oes) { q.pos = 3 + a3 / q.oes; continue; }                     // oesophageal phase ≤ 10 s
      const a4 = a3 - q.oes;
      if (a4 < q.gExit) {                                                      // stomach: store, then mix (3/min slow waves)
        const store = clamp(a4 / (3 * MIN), 0, 1);
        q.pos = 4.05 + 0.55 * store + (store >= 1 ? 0.12 * (0.5 + 0.5 * Math.sin(2 * Math.PI * a4 / 20)) * Math.min(1, q.solid * 1.5 + 0.2) : 0);
        continue;
      }
      const a5 = a4 - q.gExit;
      if (a5 < q.duo) { q.pos = 5 + a5 / q.duo; continue; }
      const a6 = a5 - q.duo;
      if (a6 < q.si) {                                                         // small intestine: segmentation + peristalsis
        const f = a6 / q.si; q.pos = 6 + f + 0.01 * Math.sin(2 * Math.PI * a6 / 6);
        const target = q.vol0 * (0.17 * q.water + 3 * q.res);
        const nv = q.vol0 + (target - q.vol0) * f; if (nv < q.vol) { this.waterSi += q.vol - nv; } q.vol = nv; continue;
      }
      // colon: slow haustral mixing, mass movements carry it far (с. 209)
      if (q.pos < 7) q.pos = 7;
      const drift = 3.4 / q.col * 0.45 * dt + (mass ? 1.1 * dt / 40 : 0);
      q.pos = Math.min(10.85 - 0.04 * ((q.meal + q.vol0) % 5), q.pos + drift);
      const f = clamp((q.pos - 7) / 3.4, 0, 1);
      const target = q.vol0 * (0.17 * q.water + 3 * q.res) * (1 - f) + q.vol0 * (q.res + 0.01) * f;
      if (target < q.vol) { this.waterCol += q.vol - target; q.vol = target; }
      if (q.vol < 0.5) { q.out = true; q.outT = -1e9; q.pos = 99; }            // all water absorbed
    }
    // remove what has left
    for (let i = this.portions.length - 1; i >= 0; i--) { const q = this.portions[i]; if (q.out && q.pos > 11.6) { if (q.outT > 0) this.faeces += q.vol; this.portions.splice(i, 1); } }
    const fill = this.rectFill();
    if (fill >= 0.25) { if (this.urgeT < 0) this.urgeT = t; } else this.urgeT = -1;     // 25 % → urge (с. 209)
    if (bool(this.p, 'auto', true) && this.urgeT >= 0 && t - this.urgeT > 20 * MIN) this.toilet();
    // eat again every 6 h if asked
    if (bool(this.p, 'repeat', false) && t - this.lastMeal > 6 * H) this.eat();
  }
  where() {
    let st = [0, 0, 0, 0, 0, 0];
    for (const q of this.portions) { if (q.out || q.pos < 0) continue; const s = q.pos < 4 ? 0 : q.pos < 5 ? 1 : q.pos < 7 ? 2 : q.pos < 10.3 ? 3 : 4; st[s] += q.vol; }
    return st; // mouth+oesophagus, stomach, small, colon, rectum
  }
}

function foodPhase(m: FoodJourney): { title: Tx; text: Tx; color: string } {
  const last = m.portions.filter((q) => q.meal === m.meals && !q.out && q.pos >= 0);
  const urge = m.urgeT >= 0;
  if (urge) return { title: tx('Urge to defecate', 'Позив за дефекация'), text: tx('', 'Ректумът е изпълнен с около 25 % от обема си — това предизвиква позива (с. 209). Рефлексът за дефекация се затваря в сакралните сегменти на гръбначния мозък; ако е удобно, външният анален сфинктер се отпуска (натисни „Тоалетна“).'), color: '#ffb347' };
  if (!last.length) return { title: tx('', 'Между храненията'), text: tx('', 'Между храненията в тънкото черво на всеки 90–120 мин минава мигриращият миоелектричен комплекс (ММК) и „почиства“ остатъците към дебелото черво (с. 204). Натисни „Хапни“.'), color: '#7fd6b5' };
  // the stage holding most of the meal; while it is being eaten, the mouth / oesophagus
  const vol = [0, 0, 0, 0, 0, 0, 0]; const edge = [1, 3, 4, 5, 6, 7, 10.3];
  for (const q of last) { let k = 0; while (k < 6 && q.pos >= edge[k]) k++; vol[k] += q.vol0; }
  const moving = last.find((q) => q.pos < 4);
  const kmax = vol.indexOf(Math.max(...vol));
  const p = moving ? moving.pos : [0.5, 2, 3.5, 4.5, 5.5, 6.5, 8, 10.5][kmax];
  if (p < 1) return { title: tx('', 'Дъвкане'), text: tx('', 'Храната се раздробява между зъбите и се смесва със слюнка, докато се оформи хранителният болус. Ритъмът на дъвкане идва от дъвкателния център в мозъчния ствол (с. 205). Течностите се поглъщат почти веднага.'), color: '#ffd166' };
  if (p < 3) return { title: tx('', 'Гълтане — фарингеална фаза'), text: tx('', 'Неволева фаза под 1 s: мекото небце затваря назофаринкса, глотисът се затваря, ларинксът се повдига, епиглотисът покрива трахеята и горният езофагеален сфинктер се отпуска. Дишането спира за момент (с. 205–206).'), color: '#ffd166' };
  if (p < 4) return { title: tx('', 'Хранопровод'), text: tx('', 'Перисталтична вълна с 2–4 cm/s носи болуса до стомаха за до 10 s. Долният езофагеален сфинктер се отпуска по вагусов рефлекс (с. 206).'), color: '#ffd166' };
  if (p < 5) return { title: tx('', 'Стомах — рецептивно отпускане и смесване'), text: tx('', 'Фундусът се отпуска и стомахът складира до 2 l без да расте налягането. После бавни вълни 3/min смесват храната със стомашния сок; антрумът я смила до химус и връща по-твърдото обратно (ретропулсия). Пилорът пропуска само частици под 2 mm. Средно ~2 часа; мазната храна стои най-дълго (с. 206–207).'), color: '#ff9f6b' };
  if (p < 6) return { title: tx('', 'Дуоденум'), text: tx('', 'Киселият химус освобождава секретин, мазнините — ХЦК и ГИП: те забавят изпразването на стомаха, за да може дуоденумът да „обработи“ постъпилото (с. 207). Тук се добавят панкреасен сок и жлъчка.'), color: '#ffd166' };
  if (p < 7) return { title: tx('', 'Тънко черво — сегментиране и перисталтика'), text: tx('', 'Сегментиращите движения смесват химуса с панкреатичния и чревния сок и го допират до лигавицата, перисталтиката го придвижва бавно. Тук се резорбират 83–84 % от водата и почти всички хранителни вещества. Мазната храна минава по-бавно от богатата на фибри (с. 208, 228).'), color: '#3dff8a' };
  if (p < 10.3) return { title: tx('', 'Дебело черво — хаустриране'), text: tx('', 'Илеоцекалният сфинктер пропуска малки порции. Хаустриращите и антиперисталтичните движения задържат съдържимото, водата се резорбира (още 16–17 %) и то става от течно кашесто, после твърдо. 1–3 пъти дневно масовите движения го пренасят към ректума (с. 209).'), color: '#c08a5b' };
  return { title: tx('', 'Ректум'), text: tx('', 'По-голямата част от времето ректумът е празен. Когато се изпълни с около 25 % от обема си, се появява позив за дефекация (с. 209).'), color: '#c08a5b' };
}

const PRESETS_FOOD = [
  { id: 'mixed', icon: '🍽️', label: tx('Mixed meal', 'Смесена храна (хляб, месо, зеленчуци)'), text: tx('', 'Обикновено хранене. Стомахът се изпразва за около 2 часа, тънкото черво — за няколко часа, а остатъкът стига до ректума за около ден.'), values: { kind: 3, vol: 500, solid: 0.75, fat: 0.3, fibre: 0.3 } },
  { id: 'water', icon: '💧', label: tx('Water', 'Чаша вода'), text: tx('', 'Течностите не се дъвчат и минават за секунди през хранопровода. Стомахът ги пропуска бързо; 83–84 % се резорбират в тънкото черво и почти всичко останало — в дебелото. С изпражненията се губят само ~100 ml вода на ден (с. 228).'), values: { kind: 0, vol: 250, solid: 0, fat: 0, fibre: 0 } },
  { id: 'soup', icon: '🥣', label: tx('Soup / juice', 'Супа, сок'), text: tx('', 'Течна храна с малко частици: почти веднага е под 2 mm и стомахът я изпразва бързо.'), values: { kind: 1, vol: 350, solid: 0.1, fat: 0.15, fibre: 0.1 } },
  { id: 'soft', icon: '🍮', label: tx('Soft food', 'Мека храна (пюре, кисело мляко)'), text: tx('', 'Малко дъвкане и малко смилане в антрума — изпразва се по-бързо от твърдата храна.'), values: { kind: 2, vol: 300, solid: 0.35, fat: 0.2, fibre: 0.1 } },
  { id: 'fat', icon: '🍟', label: tx('Fatty meal', 'Мазна храна (пържено)'), text: tx('', 'Мастните киселини в дуоденума освобождават ХЦК и ГИП и забавят изпразването на стомаха — мазната храна престоява най-дълго (с. 206–207). И в тънкото черво минава по-бавно (с. 208).'), values: { kind: 4, vol: 450, solid: 0.7, fat: 0.9, fibre: 0.05 } },
  { id: 'fibre', icon: '🥗', label: tx('High-fibre meal', 'Богата на фибри (салата, боб, пълнозърнесто)'), text: tx('', 'Целулозата не се разгражда от нашите ензими — минава по-бързо през тънкото черво, остава повече обем за дебелото черво и бактериите в него я ферментират (с. 201, 208).'), values: { kind: 5, vol: 450, solid: 0.7, fat: 0.1, fibre: 0.9 } },
];

export const foodJourney = {
  id: 'food-journey', area: 'Journey', icon: '🍽️', tags: [],
  title: tx('Journey of food', 'Пътят на храната през тялото'),
  summary: tx('', 'От хапката до тоалетната: дъвкане, гълтане, стомах, тънко и дебело черво — за различни видове храна.'),
  defaults: { kind: 3, vol: 500, solid: 0.75, fat: 0.3, fibre: 0.3, auto: true, repeat: false } as V,
  sliders: [
    { id: 'vol', label: tx('', 'Количество храна'), min: 50, max: 1000, step: 50, unit: 'ml' },
    { id: 'solid', label: tx('', 'Твърдост (течно → твърдо)'), min: 0, max: 1, step: 0.05, unit: '', hint: tx('', 'Твърдата храна се дъвче по-дълго и трябва да се смели до частици под 2 mm, преди да мине през пилора.') },
    { id: 'fat', label: tx('', 'Мазнини'), min: 0, max: 1, step: 0.05, unit: '', hint: tx('', 'Забавят изпразването на стомаха (ХЦК, ГИП) и придвижването в тънкото черво.') },
    { id: 'fibre', label: tx('', 'Фибри (целулоза)'), min: 0, max: 1, step: 0.05, unit: '', hint: tx('', 'Не се смилат: остават като обем за дебелото черво и храна за бактериите.') },
  ],
  toggles: [
    { id: 'auto', label: tx('', 'Отива до тоалетна при позив'), hint: tx('', 'Изключено: позивът се потиска и съдържимото се трупа в ректума.') },
    { id: 'repeat', label: tx('', 'Хранене на всеки 6 часа') },
  ],
  actions: [
    { id: 'eat', label: tx('', 'Хапни'), icon: '🍽️', hint: tx('', 'Изяж избраната храна още веднъж') },
    { id: 'drink', label: tx('', 'Пий вода'), icon: '💧' },
    { id: 'toilet', label: tx('', 'Тоалетна'), icon: '🚽' },
  ],
  presets: PRESETS_FOOD,
  charts: [
    { title: tx('', 'Съдържимо по отдели (ml)'), min: 0, max: 800, window: 12 * H, channels: [
      { id: 'st', label: tx('', 'Стомах'), color: '#ff9f6b', unit: 'ml' }, { id: 'si', label: tx('', 'Тънко черво'), color: '#3dff8a', unit: 'ml' }, { id: 'co', label: tx('', 'Дебело черво'), color: '#c08a5b', unit: 'ml' }] },
    { title: tx('', 'Пълнене на ректума (%)'), min: 0, max: 60, window: 48 * H, channels: [{ id: 'rf', label: tx('', 'Ректум'), color: '#ffb347', unit: '%' }], guides: [{ y: 25, label: 'позив 25 %', color: '#ffb347' }] },
  ],
  stats: [
    { id: 'tm', label: tx('', 'Време след храненето'), unit: '' },
    { id: 'where', label: tx('', 'Най-напред е стигнала до'), unit: '' },
    { id: 'st', label: tx('', 'В стомаха'), unit: 'ml' },
    { id: 'emp', label: tx('', 'Изпразнен стомах'), unit: '%' },
    { id: 'cons', label: tx('', 'Консистенция в дебелото черво'), unit: '' },
    { id: 'wsi', label: tx('', 'Резорбирана вода — тънко черво'), unit: 'ml' },
    { id: 'wco', label: tx('', 'Резорбирана вода — дебело черво'), unit: 'ml' },
    { id: 'fec', label: tx('', 'Изхвърлени изпражнения'), unit: 'ml' },
  ],
  speeds: [{ label: '×60', k: 60 }, { label: '×600', k: 600 }, { label: '1 ч/s', k: 3600 }, { label: '×10', k: 10 }],
  learn: [
    tx('', 'Гълтането има 3 фази: орална (волева), фарингеална (под 1 s) и езофагеална (до 10 s, вълна 2–4 cm/s) (с. 205–206).'),
    tx('', 'Стомахът е резервоар до 2 l. Пилорът пропуска само частици под 2 mm; храната престоява ~2 часа, мазната — най-дълго (с. 206–207).'),
    tx('', 'Изпразването се забавя от киселина (секретин), мазнини (ХЦК, ГИП), хиперосмоларност и аминокиселини в дуоденума (с. 207).'),
    tx('', 'За ден храносмилателната система резорбира 8–9 l вода (2 l приети + ~7 l секрети): 83–84 % в тънкото, 16–17 % в дебелото черво; с изпражненията — ~100 ml (с. 228).'),
    tx('', 'Консистенцията по дебелото черво: течна → полутечна → кашеста → полукашеста → твърда → много твърда (Фиг. 9.27).'),
    tx('', 'Времето в дебелото черво (около ден) и капацитетът на ректума в модела са ориентировъчни — учебникът не дава точни стойности.'),
  ],
  parts: [],
  sampleDt: 1,
  create(v: V) {
    const m = new FoodJourney(v);
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const w = m.where(); const fill = m.rectFill();
        const out: Record<string, number> = { st: w[1], si: w[2], co: w[3] + w[4], rf: fill * 100 };
        const vis = m.portions.filter((q) => q.pos >= 0 && q.pos < 11.6).slice(-24);
        writeQ(out, vis.map((q) => ({ pos: q.pos, size: Math.cbrt(q.vol / 60), kind: q.kind })));
        out.G_Stomach = clamp(w[1] / 400, 0, 1); out.G_Jejunum = clamp(w[2] / 300, 0, 1); out.G_Duodenum = clamp(w[2] / 300, 0, 1);
        out['G_Sigmoid colon'] = clamp(fill * 2.5, 0, 1); out.G_sphincter = m.urgeT >= 0 ? 1 : 0;
        return out;
      },
      stats: () => {
        const last = m.portions.filter((q) => q.meal === m.meals);
        const lead = last.length ? Math.max(...last.map((q) => (q.out ? 11 : q.pos))) : 11;
        const w = m.where();
        const meal = m.portions.filter((q) => q.meal === m.meals);
        const tot = meal.reduce((s, q) => s + q.vol0, 0) || 1;
        const left = meal.filter((q) => q.pos >= 5 || q.out).reduce((s, q) => s + q.vol0, 0);
        const colon = m.portions.filter((q) => !q.out && q.pos >= 7 && q.pos < 11);
        return { tm: fmtH(m.t - m.lastMeal), where: STAGE_BG[clamp(Math.floor(lead), 0, 11)], st: Math.round(w[1]), emp: Math.round(100 * left / tot),
          cons: colon.length ? consistency(Math.max(...colon.map((q) => q.pos))) : '—', wsi: Math.round(m.waterSi), wco: Math.round(m.waterCol), fec: Math.round(m.faeces) } as Record<string, number | string>;
      },
      phase: () => foodPhase(m),
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'eat') m.eat(); else if (id === 'drink') m.eat(0); else if (id === 'toilet') m.toilet(); },
    };
  },
};

// ======================================================================= 2. GAS AND FLATUS
interface Bubble { pos: number; vol: number; out: number }
class Gas {
  t = 0; p: V; fibre: { arrive: number; g: number; pos: number }[] = []; colonFibre = 0; bubbles: Bubble[] = []; rectGas = 0; fermRate = 0; expelled = 0; absorbed = 0; lastMeal = 0; puffT = -1e9; lastMass = -1; massT = -1e9; air = 0;
  constructor(v: V) { this.p = v; this.meal(); }
  meal() {
    const g = num(this.p, 'fibre', 15);
    const si = (3 - 0.5 * clamp(g / 40, 0, 1)) * H;                  // fibre reaches the colon after the small intestine
    this.fibre.push({ arrive: this.t + 2 * H + si, g, pos: 7 });
    this.air += num(this.p, 'air', 0.3) * 60;                        // swallowed air (ориентировъчно)
    this.lastMeal = this.t;
  }
  release() { if (this.rectGas > 5) { this.puffT = this.t; this.expelled += this.rectGas; for (const b of this.bubbles) if (b.pos > 10.2) b.out = this.t; this.rectGas = 0; } }
  advance(dt: number) {
    this.t += dt; const t = this.t;
    const bact = num(this.p, 'bact', 1), hold = bool(this.p, 'hold', false);
    if (Math.floor(t / (8 * H)) !== this.lastMass) { this.lastMass = Math.floor(t / (8 * H)); if (t > 60) this.massT = t; }
    const mass = t - this.massT < 40;
    // fermentation of cellulose by the anaerobic colon bacteria (с. 201)
    let rate = 0;
    for (const f of this.fibre) {
      if (t < f.arrive || f.g <= 0) continue;
      const k = 0.12 / H * bact;                                     // t½ ≈ 6 h (ориентировъчно)
      const used = f.g * k * dt; f.g -= used; rate += used * 25 / dt;  // ~25 ml gas per g fermented (ориентировъчно)
      f.pos = Math.min(10.4, f.pos + (3.4 / (24 * H)) * dt + (mass ? 1.0 * dt / 40 : 0));
      if (Math.random() < used * 25 / 8) this.bubbles.push({ pos: f.pos + (Math.random() - 0.5) * 0.3, vol: 8, out: 0 });
    }
    this.fibre = this.fibre.filter((f) => f.g > 0.05 || t < f.arrive);
    this.colonFibre = this.fibre.filter((f) => t >= f.arrive).reduce((s, f) => s + f.g, 0);
    this.fermRate = rate * H; // ml/h
    // swallowed air goes through the small intestine within a few hours
    if (this.air > 0) { const a = Math.min(this.air, this.air * dt / (1.5 * H)); this.air -= a; if (Math.random() < a / 8) this.bubbles.push({ pos: 6 + Math.random() * 0.9, vol: 8, out: 0 }); }
    // bubbles move distally with peristalsis and mass movements; part of the gas diffuses into the blood
    for (const b of this.bubbles) {
      if (b.out) { b.pos = 10.9 + (t - b.out) / 2; continue; }
      b.pos = Math.min(10.75, b.pos + (b.pos < 7 ? 1 / (1.5 * H) : 3.4 / (5 * H)) * dt + (mass ? 1.2 * dt / 40 : 0));
      const ab = b.vol * dt / (8 * H); b.vol -= ab; this.absorbed += ab;
    }
    this.rectGas = this.bubbles.filter((b) => !b.out && b.pos >= 10.2).reduce((s, b) => s + b.vol, 0);
    this.bubbles = this.bubbles.filter((b) => b.vol > 0.5 && b.pos < 11.6);
    if (this.bubbles.length > 140) this.bubbles.splice(0, this.bubbles.length - 140);
    if (!hold && this.rectGas > 60) this.release();                  // distension of the rectum → the gas is let out
    if (bool(this.p, 'repeat', true) && t - this.lastMeal > 6 * H) this.meal();
  }
}

export const gasJourney = {
  id: 'gas', area: 'Journey', icon: '💨', tags: [],
  title: tx('Intestinal gas and flatus', 'Чревни газове и пърдене'),
  summary: tx('', 'Откъде идват газовете: ферментация на фибрите от бактериите в дебелото черво и погълнат въздух. Как стигат до ректума и излизат.'),
  defaults: { fibre: 15, bact: 1, air: 0.3, hold: false, repeat: true } as V,
  sliders: [
    { id: 'fibre', label: tx('', 'Фибри в храненето'), min: 0, max: 50, step: 1, unit: 'g', hint: tx('', 'Целулозата не се смила от нашите ензими и стига до дебелото черво.') },
    { id: 'bact', label: tx('', 'Активност на чревните бактерии'), min: 0.2, max: 2, step: 0.1, unit: '×', hint: tx('', 'В дебелото черво има 10¹⁰–10¹² бактерии в грам, предимно анаеробни (Табл. 9.1).') },
    { id: 'air', label: tx('', 'Погълнат въздух при хранене'), min: 0, max: 1, step: 0.05, unit: '', hint: tx('', 'Бързо хранене, газирани напитки, дъвка (ориентировъчно).') },
  ],
  toggles: [{ id: 'hold', label: tx('', 'Задържа газовете'), hint: tx('', 'Външният анален сфинктер е волево свит — газът се трупа в ректума.') }, { id: 'repeat', label: tx('', 'Хранене на всеки 6 часа') }],
  actions: [{ id: 'meal', label: tx('', 'Хапни'), icon: '🍽️' }, { id: 'beans', label: tx('', 'Порция боб'), icon: '🫘' }, { id: 'release', label: tx('', 'Пусни газ'), icon: '💨' }],
  presets: [
    { id: 'normal', icon: '🍽️', label: tx('', 'Обикновено хранене'), text: tx('', 'Малко фибри: умерена ферментация, газовете излизат няколко пъти на ден.'), values: { fibre: 15, bact: 1, air: 0.3, hold: false } },
    { id: 'beans', icon: '🫘', label: tx('', 'Много фибри (боб, зеле)'), text: tx('', 'Повече неразградими въглехидрати стигат до дебелото черво: бактериите ги ферментират и газовете рязко се увеличават няколко часа след храненето.'), values: { fibre: 45, bact: 1.2, air: 0.3, hold: false } },
    { id: 'air', icon: '🥤', label: tx('', 'Бързо хранене и газирано'), text: tx('', 'Повече погълнат въздух минава през тънкото черво още в първите часове (ориентировъчно).'), values: { fibre: 15, bact: 1, air: 1, hold: false } },
    { id: 'hold', icon: '🤐', label: tx('', 'Задържане'), text: tx('', 'Външният сфинктер е под волев контрол (n. pudendus, с. 209). При задържане газът разтяга ректума, част от него дифундира в кръвта и се издишва, а при следващото отпускане излиза по-голямо количество.'), values: { fibre: 25, bact: 1, air: 0.3, hold: true } },
    { id: 'none', icon: '🍚', label: tx('', 'Почти без фибри'), text: tx('', 'Малко субстрат за бактериите — малко газове.'), values: { fibre: 3, bact: 1, air: 0.2, hold: false } },
  ],
  charts: [
    { title: tx('', 'Газове (ml)'), min: 0, max: 200, window: 24 * H, channels: [{ id: 'cg', label: tx('', 'В червата'), color: '#9fe8ff', unit: 'ml' }, { id: 'rg', label: tx('', 'В ректума'), color: '#ffb347', unit: 'ml' }] },
    { title: tx('', 'Ферментация (ml/h)'), min: 0, max: 120, window: 24 * H, channels: [{ id: 'fr', label: tx('', 'Образуване на газ'), color: '#3dff8a', unit: 'ml/h' }] },
  ],
  stats: [
    { id: 'tm', label: tx('', 'Време след храненето') }, { id: 'cf', label: tx('', 'Фибри в дебелото черво'), unit: 'g', dp: 1 },
    { id: 'fr', label: tx('', 'Образуване на газ'), unit: 'ml/h' }, { id: 'rg', label: tx('', 'Газ в ректума'), unit: 'ml' },
    { id: 'ex', label: tx('', 'Изпуснати газове'), unit: 'ml' }, { id: 'ab', label: tx('', 'Резорбирани в кръвта'), unit: 'ml' },
  ],
  speeds: [{ label: '×600', k: 600 }, { label: '1 ч/s', k: 3600 }, { label: '×60', k: 60 }],
  learn: [
    tx('', 'Стомахът и началото на тънкото черво са почти стерилни (солна киселина); в дебелото черво има 10¹⁰–10¹² бактерии в грам, предимно анаеробни (с. 201, Табл. 9.1).'),
    tx('', 'Бактериите ферментират целулозата и произвеждат витамин K, биотин, пантотенова киселина (с. 201). Газовете са страничен продукт на ферментацията.'),
    tx('', 'Ректумът е обикновено празен. Разтягането му отпуска рефлексно вътрешния сфинктер; външният е волев (n. pudendus) — затова можем да задържим газа (с. 209).'),
    tx('', 'Количествата газ в модела са ориентировъчни; учебникът описва механизма, не дава числа.'),
  ],
  parts: [], sampleDt: 1,
  create(v: V) {
    const m = new Gas(v);
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const cg = m.bubbles.filter((b) => !b.out).reduce((s, b) => s + b.vol, 0);
        const out: Record<string, number> = { cg, rg: m.rectGas, fr: m.fermRate, fx: clamp(1 - (m.t - m.puffT) / 3, 0, 1) };
        const big = m.bubbles.slice(-16);
        writeB(out, big.map((b) => ({ pos: b.pos, size: Math.cbrt(b.vol / 8) })));
        const fib = m.fibre.filter((f) => m.t >= f.arrive);
        writeQ(out, fib.slice(-6).map((f) => ({ pos: f.pos, size: Math.cbrt(f.g / 20), kind: 5 })), 6);
        out['G_Ascending colon'] = clamp(m.fermRate / 80, 0, 1); out['G_Transverse colon'] = clamp(m.fermRate / 100, 0, 1);
        out['G_Sigmoid colon'] = clamp(m.rectGas / 60, 0, 1); out.G_sphincter = bool(m.p, 'hold', false) ? 0.8 : 0;
        return out;
      },
      stats: () => ({ tm: fmtH(m.t - m.lastMeal), cf: m.colonFibre, fr: Math.round(m.fermRate), rg: Math.round(m.rectGas), ex: Math.round(m.expelled), ab: Math.round(m.absorbed) }) as Record<string, number | string>,
      phase: () => {
        if (m.t - m.puffT < 4) return { title: tx('', 'Флатус'), text: tx('', 'Разтягането на ректума отпусна вътрешния сфинктер, волево се отпусна и външният — газът излиза.'), color: '#9fe8ff' };
        if (bool(m.p, 'hold', false) && m.rectGas > 60) return { title: tx('', 'Задържане'), text: tx('', 'Външният анален сфинктер е свит волево. Газът разтяга ректума; част от него дифундира в кръвта и се издишва през белите дробове.'), color: '#ffb347' };
        if (m.fermRate > 20) return { title: tx('', 'Ферментация в дебелото черво'), text: tx('', 'Фибрите стигнаха дебелото черво. Анаеробните бактерии ферментират целулозата и образуват газ, който хаустрирането и масовите движения носят към ректума.'), color: '#3dff8a' };
        if (m.t - m.lastMeal < 4 * H) return { title: tx('', 'Храната е в стомаха и тънкото черво'), text: tx('', 'Тук почти няма бактерии. Фибрите не се смилат и продължават към дебелото черво; погълнатият въздух минава по-рано.'), color: '#ffd166' };
        return { title: tx('', 'Спокойно'), text: tx('', 'Малко ферментация. Натисни „Порция боб“, за да видиш какво става след няколко часа.'), color: '#7fd6b5' };
      },
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'meal') m.meal(); else if (id === 'beans') { const f = m.p.fibre; m.p = { ...m.p, fibre: 40 }; m.meal(); m.p = { ...m.p, fibre: f as number }; } else if (id === 'release') m.release(); },
    };
  },
};

// ======================================================================= 3. SWALLOWING
class Swallow {
  t = 0; p: V; start = 0; pos = -1; ues = 100; les = 100; resp = 1; ep = 0; sp = 0; wave = -1; fund = 100; reflux = 0; stuck = false; next = 1;
  constructor(v: V) { this.p = v; }
  swallow() { if (this.pos < 0 || this.pos >= 4.2) { this.start = this.t; this.pos = 0; this.stuck = false; } }
  advance(dt: number) {
    this.t += dt; const t = this.t; const v = this.p;
    const speed = num(v, 'speed', 3), dis = num(v, 'dis', 0), solid = num(v, 'solid', 0.5);
    const achal = dis === 1, refl = dis === 2, dysph = dis === 3;
    if (bool(v, 'auto', true) && t > this.next) { this.swallow(); this.next = t + 12; }
    const oral = 0.6 + 0.6 * solid, phar = 0.8, oes = 25 / speed * (achal ? 1.5 : 1);
    const a = t - this.start;
    let pos = this.pos, ues = 100, les = achal ? 160 : refl ? 25 : 100, resp = 1, ep = 0, sp = 0, wave = -1, fund = 100;
    if (pos >= 0 && pos < 4.2) {
      if (a < oral) pos = 0.9 * a / oral;                                                 // oral phase (voluntary)
      else if (a < oral + phar) {                                                         // pharyngeal phase (< 1 s)
        const f = (a - oral) / phar; pos = 1 + 2 * f; sp = 1; ep = Math.min(1, f * 3); resp = 0; ues = f > 0.35 ? 5 : 100; les = achal ? 160 : 40;
      } else {
        const f = (a - oral - phar) / oes;                                                // oesophageal phase
        les = achal ? 160 : refl ? 15 : 10; fund = 40;                                    // LES relaxes, fundus receptive relaxation
        if (dysph && f > 0.4 && f < 0.75) { pos = 3 + 0.4 - (f - 0.4) * 2.2; ues = 40; }  // bolus thrown back
        else if (achal) { pos = 3 + Math.min(0.93, f * 1.2); if (f > 1.6) pos = 3.93 + (f - 1.6) * 0.15; }
        else pos = 3 + Math.min(1.2, f);
        wave = clamp(f, 0, 1) * 25; ep = Math.max(0, 1 - (a - oral - phar) * 3); resp = (a - oral - phar) > 0.3 ? 1 : 0;
        if (pos >= 4.2 || (dysph && f > 2)) pos = 4.2;
        this.stuck = achal && f > 1;
      }
    } else if (refl) {
      // weak LES: acid stomach content rises into the lower oesophagus from time to time (с. 206)
      this.reflux = Math.max(0, Math.sin(t * 0.7)) > 0.92 ? 1 : this.reflux * Math.exp(-dt / 1.5);
    }
    if (!refl) this.reflux *= Math.exp(-dt / 1);
    Object.assign(this, { pos, ues, les, resp, ep, sp, wave, fund });
  }
}

export const swallowJourney = {
  id: 'swallow', area: 'Journey', icon: '👅', tags: [],
  title: tx('Swallowing', 'Преглъщане'),
  summary: tx('', 'Трите фази на гълтането секунда по секунда: езикът, мекото небце, епиглотисът, сфинктерите и перисталтичната вълна.'),
  defaults: { speed: 3, solid: 0.5, dis: 0, auto: true } as V,
  sliders: [
    { id: 'speed', label: tx('', 'Скорост на перисталтичната вълна'), min: 2, max: 4, step: 0.1, unit: 'cm/s', hint: tx('', 'Нормално 2–4 cm/s (с. 206).') },
    { id: 'solid', label: tx('', 'Твърдост на хапката'), min: 0, max: 1, step: 0.05, unit: '' },
  ],
  toggles: [{ id: 'auto', label: tx('', 'Преглъща на всеки 12 s') }],
  actions: [{ id: 'swallow', label: tx('', 'Преглътни'), icon: '👅' }],
  presets: [
    { id: 'normal', icon: '✅', label: tx('', 'Нормално гълтане'), text: tx('', 'Оралната фаза е волева. Щом болусът стигне предните небни дъги, започва неволевата фарингеална фаза (<1 s), после езофагеалната (до 10 s) (с. 205–206).'), values: { speed: 3, solid: 0.5, dis: 0 } },
    { id: 'liquid', icon: '💧', label: tx('', 'Глътка вода'), text: tx('', 'Почти без орална фаза — болусът веднага задейства гълтателния рефлекс.'), values: { speed: 4, solid: 0, dis: 0 } },
    { id: 'solid', icon: '🍖', label: tx('', 'Твърда хапка'), text: tx('', 'По-дълга орална фаза и по-бавна вълна.'), values: { speed: 2.2, solid: 1, dis: 0 } },
    { id: 'achal', icon: '⛔', label: tx('', 'Ахалазия (от учебника)'), text: tx('', 'Пълна липса на езофагеална перисталтика и повишен тонус на долния езофагеален сфинктер, който не се отпуска — храната трудно постъпва в стомаха. Причината е загуба на неврони от ентералната нервна система в долния езофагус (с. 206).'), values: { speed: 2, solid: 0.5, dis: 1 } },
    { id: 'reflux', icon: '🔥', label: tx('', 'Трансезофагеален рефлукс (от учебника)'), text: tx('', 'Отслабен долен езофагеален сфинктер: кисело стомашно съдържимо прониква в хранопровода и предизвиква възпалителни изменения в долната му част (с. 206).'), values: { speed: 3, solid: 0.5, dis: 2 } },
    { id: 'dysph', icon: '⚠️', label: tx('', 'Дисфагия (от учебника)'), text: tx('', 'Неефективно гълтане при нарушение в центъра на гълтането или некоординирано съкращение на сфинктерите: храната се връща към устата (с. 206).'), values: { speed: 3, solid: 0.6, dis: 3 } },
  ],
  charts: [
    { title: tx('', 'Тонус на сфинктерите (%)'), min: 0, max: 170, window: 14, channels: [{ id: 'ues', label: tx('', 'Горен (ГЕС)'), color: '#9fe8ff', unit: '%' }, { id: 'les', label: tx('', 'Долен (ДЕС)'), color: '#ffb347', unit: '%' }, { id: 'fund', label: tx('', 'Фундус'), color: '#ff9f6b', unit: '%' }] },
    { title: tx('', 'Болус (cm от устата) и дишане'), min: 0, max: 45, window: 14, channels: [{ id: 'cm', label: tx('', 'Болус'), color: '#3dff8a', unit: 'cm' }, { id: 'br', label: tx('', 'Дишане'), color: '#5ad1ff', unit: '' }] },
  ],
  stats: [{ id: 'ph', label: tx('', 'Фаза') }, { id: 'cm', label: tx('', 'Болусът е на'), unit: 'cm', dp: 0 }, { id: 'ues', label: tx('', 'ГЕС'), unit: '%' }, { id: 'les', label: tx('', 'ДЕС'), unit: '%' }, { id: 'br', label: tx('', 'Дишане') }],
  speeds: [{ label: '×0.5', k: 0.5 }, { label: '×1', k: 1 }, { label: '×0.2', k: 0.2 }],
  learn: [
    tx('', 'Гълтането включва повече от 25 двойки мускули на устата, фаринкса, гръкляна и хранопровода (с. 205).'),
    tx('', 'Фарингеална фаза: мекото небце затваря назофаринкса, гласните връзки се доближават, ларинксът се повдига, епиглотисът се навежда, ГЕС се отпуска. Дишането се потиска (с. 205–206).'),
    tx('', 'Езофагеална фаза: перисталтична вълна 2–4 cm/s, до 10 s; ДЕС се отпуска по вагусов рефлекс, фундусът — „рецептивно отпускане“ (с. 206).'),
    tx('', 'Центърът на гълтането е в мозъчния ствол: дорзална група неврони генерира ритъма, вентролатералната го разпределя към мотоневроните (с. 206).'),
  ],
  parts: [], sampleDt: 0.01,
  create(v: V) {
    const m = new Swallow(v);
    const phName = () => (m.pos < 0 || m.pos >= 4.2 ? 'покой' : m.pos < 1 ? 'орална' : m.pos < 3 ? 'фарингеална' : 'езофагеална');
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const cm = m.pos < 0 ? 0 : m.pos < 1 ? m.pos * 6 : m.pos < 3 ? 6 + (m.pos - 1) * 7 : 20 + (m.pos - 3) * 25;
        const out: Record<string, number> = { ues: m.ues, les: m.les, fund: m.fund, cm, br: m.resp * 10 };
        const it: { pos: number; size: number; kind: number }[] = [];
        if (m.pos >= 0 && m.pos < 4.2) it.push({ pos: m.pos, size: 0.55, kind: 3 });
        if (m.reflux > 0.05) it.push({ pos: 3.85 - 0.3 * m.reflux, size: 0.45 * m.reflux + 0.1, kind: 7 });
        writeQ(out, it, 2);
        out.G_Epiglottis = m.ep; out['G_Soft palate'] = m.sp; out.G_Uvula = m.sp; out.G_Tongue = m.pos >= 0 && m.pos < 1 ? 1 : 0;
        out.G_pharynx = m.pos >= 1 && m.pos < 3 ? 1 : 0; out.G_Oesophagus = m.wave >= 0 ? 0.8 : 0; out.G_Stomach = m.fund < 60 ? 0.5 : 0; out.G_Trachea = m.resp < 0.5 ? 0.6 : 0;
        return out;
      },
      stats: () => ({ ph: phName(), cm: m.pos < 0 ? 0 : m.pos < 1 ? m.pos * 6 : m.pos < 3 ? 6 + (m.pos - 1) * 7 : 20 + (m.pos - 3) * 25, ues: Math.round(m.ues), les: Math.round(m.les), br: m.resp > 0.5 ? 'да' : 'спряно' }) as Record<string, number | string>,
      phase: () => {
        const n = phName();
        if (m.stuck) return { title: tx('', 'Болусът спира над ДЕС'), text: tx('', 'При ахалазия долният езофагеален сфинктер не се отпуска и няма ефективна перисталтика — храната се задържа в хранопровода (с. 206).'), color: '#ff5d6c' };
        if (m.reflux > 0.3) return { title: tx('', 'Рефлукс'), text: tx('', 'Кисело стомашно съдържимо се връща през отслабения ДЕС в долната част на хранопровода (с. 206).'), color: '#ff5d6c' };
        if (n === 'орална') return { title: tx('', 'Орална фаза (волева)'), text: tx('', 'Върхът на езика отделя болуса, после езикът го притиска към твърдото небце и го тласка назад. Може да бъде прекъсната по всяко време (с. 205).'), color: '#ffd166' };
        if (n === 'фарингеална') return { title: tx('', 'Фарингеална фаза (неволева, < 1 s)'), text: tx('', 'Тактилни рецептори в небните дъги задействат рефлекса: мекото небце се вдига, гласните връзки се доближават, ларинксът се вдига, епиглотисът се навежда, горният констриктор тласка болуса, ГЕС се отпуска. Дишането е спряно (с. 205–206).'), color: '#ff9f6b' };
        if (n === 'езофагеална') return { title: tx('', 'Езофагеална фаза'), text: tx('', 'Перисталтична вълна носи болуса към стомаха; ДЕС се отпуска по вагусов рефлекс и стомахът се отпуска рецептивно (с. 206).'), color: '#3dff8a' };
        return { title: tx('', 'Покой'), text: tx('', 'Горният и долният езофагеален сфинктер са затворени: ДЕС не позволява стомашното съдържимо да се върне в хранопровода (с. 205).'), color: '#7fd6b5' };
      },
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'swallow') m.swallow(); },
    };
  },
};

// ======================================================================= 4. WATER TO URINE
class Water {
  t = 0; p: V; sips: { pos: number; vol: number; t0: number }[] = []; stom = 0; gut = 0; excess = 0; lag = 0; bladder = 150; urine = 1; totalUrine = 0; voidT = -1e9; drunk = 0; lastDrink = 0; sweatLoss = 0; insens = 0;
  constructor(v: V) { this.p = v; this.drink(num(v, 'ml', 500)); }
  drink(ml: number) { this.stom += ml; this.drunk += ml; this.lastDrink = this.t; for (let i = 0; i < 8; i++) this.sips.push({ pos: -0.2 - i * 0.12, vol: ml / 8, t0: this.t + i * 1.5 }); }
  void() { this.voidT = this.t; }
  advance(dt: number) {
    this.t += dt; const t = this.t; const v = this.p;
    const sweat = num(v, 'sweat', 0) * 2000 / H;                  // up to 2 l/h with active sweat glands (с. 304)
    const kS = Math.LN2 / (12 * MIN), kG = Math.LN2 / (20 * MIN);   // ориентировъчно: liquids leave the stomach fast
    const e = this.stom * kS * dt; this.stom -= e; this.gut += e;
    const a = this.gut * kG * dt; this.gut -= a; this.excess += a; // 83–84 % in the small intestine (с. 228)
    const loss = ((750 + 350) / (24 * H)) * dt;                    // skin 700–800 + breath 300–400 ml/day (с. 304)
    this.insens += loss; this.sweatLoss += sweat * dt; this.excess -= loss + sweat * dt;
    this.excess += ((1500 + 1100) / (24 * H)) * dt;                // the rest of the daily intake (food water, oxidation water, other drinks) — ~2.5 l/day (с. 304)
    // ADH follows plasma osmolality with a delay; less ADH → more dilute urine
    this.lag += (this.excess - this.lag) * Math.min(1, dt / (25 * MIN));
    const base = 1500 / (24 * H), min = 500 / (24 * H);           // 1.5 l/day, at least 0.5 l/day (с. 298, 304)
    const u = Math.max(min, base + Math.max(0, this.lag) / (45 * MIN) + Math.min(0, this.lag) / (240 * MIN));
    this.urine = u * MIN;                                          // ml/min
    const made = u * dt; this.excess -= made; this.bladder += made; this.totalUrine += made;
    if (t - this.voidT < 25) { const o = Math.min(this.bladder, 25 * dt); this.bladder -= o; }
    else if (bool(v, 'auto', true) && this.bladder > 400) this.void();   // ориентировъчно
    for (const s of this.sips) {
      const age = t - s.t0;
      if (age < 0) continue;
      const share = this.stom + this.gut > 1 ? this.gut / (this.stom + this.gut) : 1;
      s.pos = age < 1 ? -0.2 + 3.2 * age : age < 8 ? 3 + (age - 1) / 7 : age < 60 ? 4.3 : 4.3 + share * 1.6 + (this.gut < 5 && this.stom < 5 ? 2 : 0);
      if (s.pos > 4.4) s.vol *= Math.exp(-dt / (20 * MIN));
    }
    this.sips = this.sips.filter((s) => s.vol > 2 && s.pos < 7);
    if (bool(v, 'repeat', false) && t - this.lastDrink > 3 * H) this.drink(num(v, 'ml', 500));
  }
}

export const waterJourney = {
  id: 'water-journey', area: 'Journey', icon: '💧', tags: [],
  title: tx('From a glass of water to urine', 'Пътят на водата: от чашата до урината'),
  summary: tx('', 'Изпитата вода минава през стомаха, резорбира се в тънкото черво, бъбреците изхвърлят излишъка и пикочният мехур се пълни.'),
  defaults: { ml: 500, sweat: 0, auto: true, repeat: false } as V,
  sliders: [
    { id: 'ml', label: tx('', 'Изпита вода'), min: 100, max: 1500, step: 50, unit: 'ml' },
    { id: 'sweat', label: tx('', 'Изпотяване (жега, работа)'), min: 0, max: 1, step: 0.05, unit: '', hint: tx('', 'При активни потни жлези загубата стига до 2 l/час (с. 304).') },
  ],
  toggles: [{ id: 'auto', label: tx('', 'Уринира, когато мехурът е пълен') }, { id: 'repeat', label: tx('', 'Пие на всеки 3 часа') }],
  actions: [{ id: 'drink', label: tx('', 'Изпий'), icon: '💧' }, { id: 'void', label: tx('', 'Уриниране'), icon: '🚽' }],
  presets: [
    { id: 'glass', icon: '🥛', label: tx('', 'Половин литър вода'), text: tx('', 'Водата бързо напуска стомаха, резорбира се в тънкото черво (пасивно, след Na⁺ и Cl⁻), излишъкът намалява АДХ и диурезата нараства (с. 228, 298).'), values: { ml: 500, sweat: 0 } },
    { id: 'small', icon: '☕', label: tx('', 'Чаша (250 ml)'), text: tx('', 'Малко количество — диурезата почти не се променя.'), values: { ml: 250, sweat: 0 } },
    { id: 'litre', icon: '🍶', label: tx('', 'Литър наведнъж'), text: tx('', 'Голям воден товар: урината става обилна и разредена, мехурът се пълни бързо.'), values: { ml: 1000, sweat: 0 } },
    { id: 'hot', icon: '🥵', label: tx('', 'Жега и физическа работа'), text: tx('', 'Потенето отнема вода (до 2 l/час). Изпитата вода покрива загубата, АДХ остава висок и урината е малко и концентрирана (с. 298, 304).'), values: { ml: 500, sweat: 0.4 } },
  ],
  charts: [
    { title: tx('', 'Къде е водата (ml)'), min: 0, max: 1000, window: 6 * H, channels: [{ id: 'st', label: tx('', 'Стомах'), color: '#ff9f6b', unit: 'ml' }, { id: 'gt', label: tx('', 'Черва'), color: '#3dff8a', unit: 'ml' }, { id: 'bl', label: tx('', 'Пикочен мехур'), color: '#ffd166', unit: 'ml' }] },
    { title: tx('', 'Диуреза (ml/min)'), min: 0, max: 12, window: 6 * H, channels: [{ id: 'uf', label: tx('', 'Урина'), color: '#ffe066', unit: 'ml/min' }], guides: [{ y: 1.04, label: '1,5 l/ден', color: '#888' }] },
  ],
  stats: [
    { id: 'tm', label: tx('', 'Време след пиенето') }, { id: 'st', label: tx('', 'В стомаха'), unit: 'ml' }, { id: 'gt', label: tx('', 'В червата'), unit: 'ml' },
    { id: 'ex', label: tx('', 'Излишък в организма'), unit: 'ml' }, { id: 'uf', label: tx('', 'Диуреза'), unit: 'ml/min', dp: 1 }, { id: 'adh', label: tx('', 'АДХ (относително)'), unit: '%' },
    { id: 'bl', label: tx('', 'Пикочен мехур'), unit: 'ml' }, { id: 'tu', label: tx('', 'Отделена урина'), unit: 'ml' }, { id: 'ins', label: tx('', 'Кожа + дишане + пот'), unit: 'ml' },
  ],
  speeds: [{ label: '×120', k: 120 }, { label: '×600', k: 600 }, { label: '×20', k: 20 }],
  learn: [
    tx('', 'В стомаха се резорбира незначително количество вода; 83–84 % се резорбират в тънкото черво, 16–17 % в дебелото (с. 228).'),
    tx('', 'Резорбцията на водата е пасивна — следва осмотичния градиент, създаден от резорбцията на Na⁺, Cl⁻ и други вещества (с. 228).'),
    tx('', 'Урината се концентрира в събирателните тубули при наличие на АДХ; крайната урина е ~1,5 l/24 ч, минимум ~0,5 l (с. 298, 304).'),
    tx('', 'Невидима загуба: кожа 700–800 ml/ден, дишане 300–400 ml/ден, изпражнения 100–150 ml/ден (с. 304).'),
    tx('', 'Скоростта на изпразване на стомаха за течности и обемът на мехура, при който се уринира, са ориентировъчни.'),
  ],
  parts: [], sampleDt: 1,
  create(v: V) {
    const m = new Water(v);
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const out: Record<string, number> = { st: m.stom, gt: m.gut, bl: m.bladder, uf: m.urine, ur: clamp(m.urine / 8, 0.05, 1), vo: m.t - m.voidT < 25 ? 1 : 0 };
        writeQ(out, m.sips.filter((s) => s.pos > -0.3).map((s) => ({ pos: s.pos, size: Math.cbrt(s.vol / 60), kind: 0 })), 8);
        out.G_Kidney = clamp(m.urine / 8, 0.1, 1); out['G_Urinary bladder'] = clamp(m.bladder / 400, 0, 1); out.G_Stomach = clamp(m.stom / 500, 0, 1); out.G_Jejunum = clamp(m.gut / 400, 0, 1);
        return out;
      },
      stats: () => ({ tm: fmtH(m.t - m.lastDrink), st: Math.round(m.stom), gt: Math.round(m.gut), ex: Math.round(m.excess), uf: m.urine, adh: Math.round(clamp(100 - m.lag / 6, 5, 160)),
        bl: Math.round(m.bladder), tu: Math.round(m.totalUrine), ins: Math.round(m.insens + m.sweatLoss) }) as Record<string, number | string>,
      phase: () => {
        if (m.t - m.voidT < 25) return { title: tx('', 'Уриниране'), text: tx('', 'Пикочният мехур се изпразва през уретрата.'), color: '#ffe066' };
        if (m.stom > 50) return { title: tx('', 'Водата е в стомаха'), text: tx('', 'Тук се резорбира незначително количество вода. Течностите не се смилат и бързо минават през пилора (с. 228).'), color: '#ff9f6b' };
        if (m.gut > 30) return { title: tx('', 'Резорбция в тънкото черво'), text: tx('', 'Водата преминава пасивно през епитела след Na⁺ и Cl⁻ — по парацелуларен и трансцелуларен път (с. 228). Плазмата се разрежда.'), color: '#3dff8a' };
        if (m.urine > 2) return { title: tx('', 'Бъбреците изхвърлят излишъка'), text: tx('', 'По-ниският осмолалитет намалява АДХ, събирателните тубули резорбират по-малко вода и урината става обилна и разредена (с. 298).'), color: '#ffe066' };
        if (m.excess < -200) return { title: tx('', 'Воден дефицит'), text: tx('', 'Загубите (кожа, дишане, пот) надвишават приема: АДХ се повишава и урината намалява до минимум ~500 ml/ден (с. 304).'), color: '#ff5d6c' };
        return { title: tx('', 'Воден баланс'), text: tx('', 'Приемът (~2,5 l/ден) се изравнява с отделянето: урина ~1,5 l, кожа 0,7–0,8 l, дишане 0,3–0,4 l, изпражнения 0,1–0,15 l (с. 304).'), color: '#7fd6b5' };
      },
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'drink') m.drink(num(m.p, 'ml', 500)); else if (id === 'void') m.void(); },
    };
  },
};

// ======================================================================= 5. DEFECATION
class Defecation {
  t = 0; p: V; jam = 0; fill = 0.1; ias = 100; eas = 100; pabd = 5; urge = 0; out = 0; push = -1e9; nextMass = 30; massT = -1e9; evac = 0; total = 0; decided = -1;
  constructor(v: V) { this.p = v; }
  advance(dt: number) {
    this.t += dt; const t = this.t; const v = this.p;
    const hirsch = bool(v, 'hirsch', false), squat = bool(v, 'squat', false), ok = bool(v, 'ok', true);
    if (t > this.nextMass) { this.massT = t; this.nextMass = t + num(v, 'every', 90); }
    const mass = t - this.massT < 12;
    if (mass && !hirsch) this.fill = Math.min(0.9, this.fill + 0.022 * dt);              // a mass movement fills the rectum
    if (mass && hirsch) this.jam = Math.min(1, this.jam + 0.01 * dt);                    // aganglionic segment: no propulsion, content piles up above it (с. 209)
    // rectoanal reflex: stretch relaxes the internal sphincter (involuntary); ENS missing → no relaxation
    const stretch = clamp((this.fill - 0.15) / 0.3, 0, 1);
    this.ias = hirsch ? 110 : 100 - 75 * stretch;
    this.urge = this.fill >= 0.25 ? 1 : 0;                                                // urge at 25 % (с. 209)
    const pushing = t - this.push < 15;
    if (this.urge && ok && !hirsch && t - this.push > 25 && this.fill > 0.25) this.push = t;          // decides to go
    if (pushing && ok) {
      this.eas = 10; this.pabd = 60;                                                      // deep breath, abdominal press, external sphincter relaxed
      const angle = squat ? 1 : 0.55;                                                     // squatting straightens the anorectal angle (с. 209)
      const flow = hirsch ? 0 : 0.06 * angle * (this.ias < 50 ? 1 : 0.15);
      const o = Math.min(this.fill, flow * dt); this.fill -= o; this.evac = o / dt; this.total += o;
    } else {
      this.eas = this.urge ? 160 : 100; this.pabd = 5; this.evac = 0;                     // voluntary holding when not convenient
    }
  }
}

export const defecationJourney = {
  id: 'defecation', area: 'Journey', icon: '🚽', tags: [],
  title: tx('Defecation', 'Дефекация'),
  summary: tx('', 'Масово движение пълни ректума, разтягането отпуска вътрешния сфинктер, а външният е волев.'),
  defaults: { every: 90, ok: true, squat: false, hirsch: false } as V,
  sliders: [{ id: 'every', label: tx('', 'Масово движение на всеки (ускорено)'), min: 30, max: 300, step: 10, unit: 's', hint: tx('', 'В действителност 1–3 пъти дневно (с. 209); тук е ускорено.') }],
  toggles: [
    { id: 'ok', label: tx('', 'Удобно е (тоалетна)'), hint: tx('', 'Изключено: външният сфинктер се свива волево и позивът се потиска.') },
    { id: 'squat', label: tx('', 'Клекнало положение'), hint: tx('', 'Намалява ъгъла между ректума и ануса и улеснява дефекацията (с. 209).') },
    { id: 'hirsch', label: tx('', 'Без ганглии в дисталното черво'), hint: tx('', 'Болест на Hirschsprung (с. 209).') },
  ],
  actions: [{ id: 'mass', label: tx('', 'Масово движение'), icon: '➡️' }],
  presets: [
    { id: 'normal', icon: '✅', label: tx('', 'Нормално'), text: tx('', 'Масовото движение пълни ректума; при 25 % се появява позив. Вътрешният сфинктер се отпуска рефлексно, външният — волево, следва дълбоко вдишване и напън на коремните мускули (с. 209).'), values: { every: 90, ok: true, squat: false, hirsch: false } },
    { id: 'hold', icon: '🤐', label: tx('', 'Задържане'), text: tx('', 'Не е удобно: външният анален сфинктер (n. pudendus) се свива волево и задържа, въпреки че вътрешният е отпуснат.'), values: { every: 90, ok: false, squat: false, hirsch: false } },
    { id: 'squat', icon: '🧎', label: tx('', 'Клекнало положение'), text: tx('', 'Ъгълът между ректума и ануса намалява и изпразването е по-лесно и по-бързо (с. 209).'), values: { every: 90, ok: true, squat: true, hirsch: false } },
    { id: 'hirsch', icon: '⚠️', label: tx('', 'Болест на Hirschsprung (от учебника)'), text: tx('', 'Вроден мегаколон: невроните на ентералната система не са мигрирали в дисталния колон. Без миентерален и субмукозен плексус няма двигателна активност и фекалните маси не се придвижват към ануса (с. 209).'), values: { every: 90, ok: true, squat: false, hirsch: true } },
  ],
  charts: [
    { title: tx('', 'Ректум (%) и коремно налягане'), min: 0, max: 100, window: 300, channels: [{ id: 'fill', label: tx('', 'Пълнене'), color: '#c08a5b', unit: '%' }, { id: 'pabd', label: tx('', 'Коремно налягане'), color: '#ff9f6b', unit: '' }], guides: [{ y: 25, label: 'позив', color: '#ffb347' }] },
    { title: tx('', 'Тонус на сфинктерите (%)'), min: 0, max: 170, window: 300, channels: [{ id: 'ias', label: tx('', 'Вътрешен (неволев)'), color: '#9fe8ff', unit: '%' }, { id: 'eas', label: tx('', 'Външен (волев)'), color: '#ffb347', unit: '%' }] },
  ],
  stats: [{ id: 'fill', label: tx('', 'Пълнене на ректума'), unit: '%' }, { id: 'urge', label: tx('', 'Позив') }, { id: 'ias', label: tx('', 'Вътрешен сфинктер'), unit: '%' }, { id: 'eas', label: tx('', 'Външен сфинктер'), unit: '%' }, { id: 'tot', label: tx('', 'Изпразнено'), unit: '%' }, { id: 'jam', label: tx('', 'Застой над безганглийния сегмент'), unit: '%' }],
  speeds: [{ label: '×1', k: 1 }, { label: '×4', k: 4 }],
  learn: [
    tx('', 'Дефекацията включва неволеви (рефлекс за дефекация) и волеви механизми. Интегративният център е в сакралните сегменти на гръбначния мозък (с. 209).'),
    tx('', 'Разтягането на ректума води до рефлексното му съкращение и отпускане на вътрешния анален сфинктер (с. 209).'),
    tx('', 'Ако е удобно: отпуска се външният сфинктер, следва дълбоко вдишване, съкращение на коремната стена и тазовото дъно (с. 209).'),
  ],
  parts: [], sampleDt: 0.05,
  create(v: V) {
    const m = new Defecation(v);
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const out: Record<string, number> = { fill: m.fill * 100, pabd: m.pabd, ias: m.ias, eas: m.eas };
        const items: { pos: number; size: number; kind: number }[] = [];
        const n = Math.round(m.fill * 10);
        for (let i = 0; i < n; i++) items.push({ pos: 10.25 + 0.06 * i, size: 0.5, kind: 6 });
        if (m.evac > 0.001) for (let i = 0; i < 3; i++) items.push({ pos: 10.9 + ((m.t * 0.8 + i / 3) % 1) * 0.5, size: 0.45, kind: 6 });
        if (m.t - m.massT < 12) for (let i = 0; i < 4; i++) items.push({ pos: Math.min(bool(m.p, 'hirsch', false) ? 9.6 : 10.3, 9 + (m.t - m.massT) / 12 * 1.2 + i * 0.05), size: 0.45, kind: 6 });
        for (let i = 0; i < Math.round(m.jam * 8); i++) items.push({ pos: 9.0 + i * 0.08, size: 0.5 + m.jam * 0.4, kind: 6 });
        writeQ(out, items, 16);
        out['G_Sigmoid colon'] = m.urge ? 0.8 : m.fill; out['G_Descending colon'] = m.jam; out.G_sphincter = m.eas > 120 ? 1 : 0; if (m.t - m.massT < 12) out['G_Descending colon'] = 0.8;
        return out;
      },
      stats: () => ({ fill: Math.round(m.fill * 100), urge: m.urge ? 'да' : 'не', ias: Math.round(m.ias), eas: Math.round(m.eas), tot: Math.round(m.total * 100), jam: Math.round(m.jam * 100) }) as Record<string, number | string>,
      phase: () => {
        if (m.evac > 0.001) return { title: tx('', 'Евакуация'), text: tx('', 'Външният сфинктер е отпуснат, следва дълбоко вдишване и напън на коремната стена; силни контракции на низходящото и сигмоидното черво изпразват ректума (с. 209).'), color: '#c08a5b' };
        if (bool(m.p, 'hirsch', false) && m.jam > 0.15) return { title: tx('', 'Няма придвижване'), text: tx('', 'Без ентерални неврони в дисталния сегмент липсва двигателна активност — съдържимото се трупа и червото се разширява (с. 209).'), color: '#ff5d6c' };
        if (m.urge && m.eas > 120) return { title: tx('', 'Позив — задържане'), text: tx('', 'Ректумът е изпълнен над 25 %, вътрешният сфинктер е отпуснат рефлексно, но външният се държи свит волево.'), color: '#ffb347' };
        if (m.t - m.massT < 12) return { title: tx('', 'Масово движение'), text: tx('', 'Силна, продължителна контракция от проксималното дебело черво към ректума (1–3 пъти дневно) — пренася фекалните маси на голямо разстояние (с. 209).'), color: '#ffd166' };
        return { title: tx('', 'Ректумът е почти празен'), text: tx('', 'По-голямата част от времето ректумът е празен; двата сфинктера са затворени (с. 209).'), color: '#7fd6b5' };
      },
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'mass') m.nextMass = m.t; },
    };
  },
};

// ======================================================================= 6. VOMITING (reflex described in the textbook)
class Vomit {
  t = 0; p: V; start = 2; pos = 4.6; pabd = 5; les = 100; ues = 100; resp = 1; dia = 0; stage = 0;
  constructor(v: V) { this.p = v; }
  advance(dt: number) {
    this.t += dt; const a = this.t - this.start;
    let pos = 4.6, pabd = 5, les = 100, ues = 100, resp = 1, dia = 0, stage = 0;
    if (a < 0) stage = 0;
    else if (a < 3) { stage = 1; pos = 5.8 - 1.3 * (a / 3); }                                     // retroperistalsis from the mid small intestine, pylorus opens
    else if (a < 4.5) { stage = 2; pos = 4.5; dia = 1; resp = 1; }                                  // forced inspiration, diaphragm down
    else if (a < 6) { stage = 3; pos = 4.5 - (a - 4.5) / 1.5 * 0.4; pabd = 80; dia = 1; resp = 0; }  // abdominal muscles contract
    else if (a < 7.5) { stage = 4; pos = 4.1 - (a - 6) / 1.5 * 1.1; pabd = 90; les = 5; resp = 0; } // LES relaxes, content fills the oesophagus
    else if (a < 9) { stage = 5; pos = 3 - (a - 7.5) / 1.5 * 3.6; pabd = 90; les = 5; ues = 5; resp = 0; } // UES opens → out
    else { stage = 6; pos = -1; if (a > 14) this.start = this.t + (bool(this.p, 'repeat', false) ? 4 : 1e9); }
    Object.assign(this, { pos, pabd, les, ues, resp, dia, stage });
  }
}

export const vomitJourney = {
  id: 'vomit', area: 'Journey', icon: '🤢', tags: [],
  title: tx('Vomiting reflex', 'Рефлекс на повръщане'),
  summary: tx('', 'Съдържимото тръгва в обратна посока: от тънкото черво към стомаха, през хранопровода и навън.'),
  defaults: { repeat: false } as V,
  sliders: [],
  toggles: [{ id: 'repeat', label: tx('', 'Повтаря се') }],
  actions: [{ id: 'go', label: tx('', 'Задействай'), icon: '🤢' }],
  presets: [
    { id: 'stretch', icon: '🍽️', label: tx('', 'Силно разтягане на стомаха'), text: tx('', 'Един от стимулите за центъра на повръщане в продълговатия мозък (с. 207–208).'), values: {} },
    { id: 'vest', icon: '🌀', label: tx('', 'Вестибуларно (морска болест)'), text: tx('', 'Възбуждане на вестибуларните рецептори също задейства центъра на повръщане (с. 208).'), values: {} },
    { id: 'ctz', icon: '🧪', label: tx('', 'Хеморецепторна тригерна зона'), text: tx('', 'Химични стимули в зоната в 4-то мозъчно стомахче или в дуоденума (с. 208).'), values: {} },
  ],
  charts: [
    { title: tx('', 'Коремно налягане и сфинктери'), min: 0, max: 110, window: 15, channels: [{ id: 'pabd', label: tx('', 'Коремно налягане'), color: '#ff9f6b', unit: '' }, { id: 'les', label: tx('', 'ДЕС'), color: '#ffb347', unit: '%' }, { id: 'ues', label: tx('', 'ГЕС'), color: '#9fe8ff', unit: '%' }] },
    { title: tx('', 'Дишане'), min: 0, max: 1.2, window: 15, channels: [{ id: 'br', label: tx('', 'Дишане'), color: '#5ad1ff', unit: '' }] },
  ],
  stats: [{ id: 'st', label: tx('', 'Етап') }, { id: 'pabd', label: tx('', 'Коремно налягане'), unit: '' }, { id: 'br', label: tx('', 'Дишане') }],
  speeds: [{ label: '×1', k: 1 }, { label: '×0.5', k: 0.5 }],
  learn: [
    tx('', 'Повръщането е рефлекс с център в продълговатия мозък. Стимули: разтягане на стомаха и дуоденума, дразнене на фаринкса, вестибуларно възбуждане, увреждане на урогениталната система, хеморецепторната тригерна зона (с. 207–208).'),
    tx('', 'Последователност: перисталтика от средата на тънкото черво в орална посока → пилорът се отваря → форсирано вдишване → коремната мускулатура се съкращава → ДЕС се отпуска → ГЕС се отваря (с. 208).'),
    tx('', 'Дишането е потиснато, за да не попадне съдържимо в трахеята (с. 208).'),
  ],
  parts: [], sampleDt: 0.02,
  create(v: V) {
    const m = new Vomit(v);
    const ST = ['Покой', 'Ретроперисталтика', 'Форсирано вдишване', 'Напън на корема', 'ДЕС се отпуска', 'ГЕС се отваря', 'Край'];
    return {
      get t() { return m.t; }, set t(_x: number) {},
      advance: (dt: number) => m.advance(dt),
      read: () => {
        const out: Record<string, number> = { pabd: m.pabd, les: m.les, ues: m.ues, br: m.resp };
        const items = m.pos > -0.9 && m.stage > 0 && m.stage < 6 ? [0, 1, 2, 3].map((i) => ({ pos: m.pos + i * 0.08, size: 0.5, kind: 7 })) : [];
        writeQ(out, items, 4);
        out.G_Stomach = m.stage >= 3 && m.stage <= 4 ? 1 : 0.2 * m.stage; out.G_Duodenum = m.stage === 1 ? 1 : 0; out.G_Jejunum = m.stage === 1 ? 0.7 : 0;
        out.G_Oesophagus = m.stage >= 4 && m.stage <= 5 ? 1 : 0; out.G_Trachea = m.resp < 0.5 ? 0.6 : 0; out.G_Epiglottis = m.stage === 5 ? 1 : 0;
        return out;
      },
      stats: () => ({ st: ST[m.stage], pabd: Math.round(m.pabd), br: m.resp > 0.5 ? 'да' : 'потиснато' }) as Record<string, number | string>,
      phase: () => {
        const T = [
          ['Покой', 'Натисни „Задействай“: центърът на повръщане в продълговатия мозък получава стимул.'],
          ['Ретроперисталтика', 'Перисталтично съкращение от средата на тънкото черво се разпространява в орална посока; пилорът се отпуска и дуоденалното съдържимо минава в стомаха (с. 208).'],
          ['Форсирано вдишване', 'Диафрагмата слиза надолу и увеличава налягането в коремната кухина (с. 208).'],
          ['Напън на корема', 'Коремната мускулатура се съкращава форсирано и налягането рязко се покачва (с. 208).'],
          ['ДЕС се отпуска', 'Долният езофагеален сфинктер се отпуска, долната част на стомаха се съкращава и съдържимото изпълва хранопровода (с. 208).'],
          ['ГЕС се отваря', 'Горният езофагеален сфинктер се отваря рефлексно и съдържимото излиза през устата. Дишането е потиснато (с. 208).'],
          ['Край', 'Коремната и дихателната мускулатура се отпускат.'],
        ][m.stage];
        return { title: tx('', T[0]), text: tx('', T[1]), color: m.stage >= 3 && m.stage <= 5 ? '#ff5d6c' : '#ffd166' };
      },
      set: (nv: V) => { m.p = nv; },
      act: (id: string) => { if (id === 'go') m.start = m.t; },
    };
  },
};

export const JOURNEYS = [foodJourney, gasJourney, swallowJourney, waterJourney, defecationJourney, vomitJourney];
