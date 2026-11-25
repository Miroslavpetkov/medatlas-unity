using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MedAtlas.Heart;

namespace MedAtlas.Sims
{
    /// <summary>
    /// Phone held upright ("вариант Б", like the web physiology screen): the 3D view fills the screen and a bottom sheet
    /// holds the rest. Folded, the sheet always shows the first four live values and the current state; its tabs open
    /// Състояния (presets and actions), Графики (the charts), Регулатори (sliders and toggles) and Лекарства (the
    /// model's drugs, only when it has any). The sheet snaps to three heights and is dragged by its handle or the
    /// numbers. Play, speed and view buttons sit in one row below it. Landscape is left exactly as it was.
    /// </summary>
    public class PhoneSheet : MonoBehaviour
    {
        SimApp app; RectTransform canvas; ResponsiveLayout layout; OrbitCamera orbit; Canvas cv;
        RectTransform root, sheet, viewport, scrollContent, statesBox, drugsBox; ScrollRect scroll;
        readonly Text[] keyLbl = new Text[4], keyVal = new Text[4];
        Text stateName, statePhase, presetText, drugHow, drugStatus; Image stateDot;
        readonly List<(UIKit.Btn b, string id)> tabBtns = new List<(UIKit.Btn, string)>();
        readonly List<(UIKit.Btn b, string id)> presetBtns = new List<(UIKit.Btn, string)>();
        string tab = "states"; int snap; float height = -1, stripH = 100; bool dragging, active;
        RectTransform graphs, par, bar; Transform graphsParent, parParent; int graphsSibling, parSibling;
        readonly List<GameObject> hiddenBarKids = new List<GameObject>();
        const float W = 1080, Pad = 24, Head = 56, KeysH = 128, StateH = 92, TabsH = 92;
        float CollapsedH => Head + KeysH + StateH + TabsH + 18;
        float H => canvas.rect.height;
        float SnapH(int s) => s == 0 ? CollapsedH : s == 1 ? Mathf.Max(CollapsedH + 260, (H - stripH) * 0.55f) : H - stripH - 250;

        public static PhoneSheet Attach(SimApp app, Transform canvas, ResponsiveLayout layout, OrbitCamera orbit)
        {
            var s = canvas.gameObject.AddComponent<PhoneSheet>();
            s.app = app; s.canvas = (RectTransform)canvas; s.layout = layout; s.orbit = orbit; s.cv = canvas.GetComponent<Canvas>();
            s.graphs = app.GraphsPanel; s.par = app.ParamsPanel; s.bar = app.BarPanel;
            s.Build();
            return s;
        }

        // ------------------------------------------------------------------ building
        RectTransform Top(string name, Transform parent, float y, float h)
        {
            var r = UIKit.Rect(name, parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(0, h));
            return r;
        }

        void Build()
        {
            root = UIKit.Rect("PhoneSheet", canvas, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            sheet = UIKit.Rect("Sheet", root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(0, CollapsedH));
            UIKit.PanelImage(sheet, UIKit.Card);

            // handle (drag / tap)
            var handle = Top("Handle", sheet, 0, Head);
            var hi = handle.gameObject.AddComponent<Image>(); hi.color = new Color(0, 0, 0, 0.001f);
            handle.gameObject.AddComponent<SheetHandle>().Sheet = this;
            var grip = UIKit.Rect("Grip", handle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 10));
            UIKit.PanelImage(grip, new Color(1, 1, 1, 0.28f));

            // the four key numbers (dragging them moves the sheet too)
            var keys = Top("Keys", sheet, -Head, KeysH);
            var ki = keys.gameObject.AddComponent<Image>(); ki.color = new Color(0, 0, 0, 0.001f);
            keys.gameObject.AddComponent<SheetHandle>().Sheet = this;
            var statsDef = MiniJson.L(app.Def["stats"]);
            float tw = (W - 2 * Pad - 3 * 14) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var tile = UIKit.Rect("Key" + i, keys, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + i * (tw + 14), 0), new Vector2(tw, KeysH - 10));
                UIKit.PanelImage(tile, UIKit.ButtonOff);
                tile.GetComponent<Image>().raycastTarget = false;
                string lbl = i < statsDef.Count ? MiniJson.S(MiniJson.O(statsDef[i]), "label") : "";
                keyLbl[i] = UIKit.LabelAt(tile, lbl, 19, UIKit.TextDim, new Vector2(14, -10), new Vector2(tw - 22, 44));
                keyLbl[i].resizeTextForBestFit = true; keyLbl[i].resizeTextMinSize = 13; keyLbl[i].resizeTextMaxSize = 19; keyLbl[i].verticalOverflow = VerticalWrapMode.Truncate;
                keyVal[i] = UIKit.LabelAt(tile, "—", 32, UIKit.TextMain, new Vector2(14, -58), new Vector2(tw - 22, 54), UIKit.SemiBold);
                keyVal[i].resizeTextForBestFit = true; keyVal[i].resizeTextMinSize = 16; keyVal[i].resizeTextMaxSize = 34; keyVal[i].horizontalOverflow = HorizontalWrapMode.Wrap; keyVal[i].verticalOverflow = VerticalWrapMode.Truncate;
                if (i >= statsDef.Count) tile.gameObject.SetActive(false);
            }

            // current state; tapping it opens the states tab
            var st = Top("State", sheet, -Head - KeysH, StateH - 12);
            st.offsetMin = new Vector2(Pad, st.offsetMin.y); st.offsetMax = new Vector2(-Pad, st.offsetMax.y);
            var sb = st.gameObject.AddComponent<Image>(); sb.color = new Color(UIKit.Accent.r * 0.18f, UIKit.Accent.g * 0.18f, UIKit.Accent.b * 0.25f, 0.95f);
            st.gameObject.AddComponent<Button>().onClick.AddListener(() => OpenTab("states"));
            stateDot = UIKit.Dot(st, new Vector2(22, -40), 14, Color.white);
            UIKit.LabelAt(st, "СЪСТОЯНИЕ", 16, UIKit.Accent, new Vector2(48, -8), new Vector2(300, 22), UIKit.SemiBold);
            stateName = UIKit.LabelAt(st, "", 24, UIKit.TextMain, new Vector2(48, -32), new Vector2(W - 2 * Pad - 400, 32), UIKit.SemiBold);   // ends before the phase text
            stateName.horizontalOverflow = HorizontalWrapMode.Wrap; stateName.verticalOverflow = VerticalWrapMode.Truncate;
            statePhase = UIKit.LabelAt(st, "", 20, UIKit.TextDim, new Vector2(W - 2 * Pad - 340, -30), new Vector2(205, 30), null, TextAnchor.UpperRight);   // ends 15 px before "Смени ›"
            statePhase.horizontalOverflow = HorizontalWrapMode.Wrap; statePhase.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.LabelAt(st, "Смени ›", 22, UIKit.Accent, new Vector2(W - 2 * Pad - 118, -28), new Vector2(104, 30), UIKit.SemiBold, TextAnchor.UpperRight);

            // tabs
            var tabs = Top("Tabs", sheet, -Head - KeysH - StateH, TabsH - 12);
            var tabDefs = new List<(string id, string label)> { ("states", "Състояния"), ("charts", "Графики"), ("controls", "Регулатори") };
            var drugs = MiniJson.L(app.Def.ContainsKey("drugs") ? app.Def["drugs"] : null);
            if (drugs.Count > 0) tabDefs.Add(("drugs", "Лекарства"));
            if (graphs == null) tabDefs.RemoveAll(t => t.id == "charts");
            float bw = (W - 2 * Pad - (tabDefs.Count - 1) * 10) / tabDefs.Count;
            for (int i = 0; i < tabDefs.Count; i++)
            {
                string id = tabDefs[i].id;
                var b = UIKit.Button(tabs, tabDefs[i].label, new Vector2(Pad + i * (bw + 10), 0), new Vector2(bw, TabsH - 12), () => TabClicked(id), 24);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(Pad + i * (bw + 10), 0);
                tabBtns.Add((b, id));
            }

            // scrolling content under the tabs
            viewport = UIKit.Rect("Viewport", sheet, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(0, 8); viewport.offsetMax = new Vector2(0, -(Head + KeysH + StateH + TabsH));
            var vi = viewport.gameObject.AddComponent<Image>(); vi.color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            scrollContent = UIKit.Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = scrollContent; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;

            BuildStates();
            if (drugs.Count > 0) BuildDrugs(drugs);
            root.gameObject.SetActive(false);
        }

        UIKit.Btn GridButton(RectTransform parent, string label, int i, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            float cw = (W - 2 * Pad - 14) / 2, ch = 84;
            int col = i % 2; if (col == 0 && i > 0) y -= ch + 12;
            var b = UIKit.Button(parent, label, Vector2.zero, new Vector2(cw, ch), onClick, 23);
            var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Pad + col * (cw + 14), y);
            b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.offsetMin = new Vector2(18, 0); b.Text.rectTransform.offsetMax = new Vector2(-12, 0);
            b.Text.resizeTextForBestFit = true; b.Text.resizeTextMinSize = 15; b.Text.resizeTextMaxSize = 23;
            return b;
        }

        void BuildStates()
        {
            statesBox = UIKit.Rect("States", scrollContent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            float y = -6; int i = 0;
            UIKit.LabelAt(statesBox, "СЪСТОЯНИЯ И БОЛЕСТИ", 18, new Color(1, 1, 1, 0.45f), new Vector2(Pad, y), new Vector2(600, 26), UIKit.SemiBold); y -= 36;
            foreach (var o in MiniJson.L(app.Def["presets"]))
            {
                var p = MiniJson.O(o); string id = MiniJson.S(p, "id");
                var b = GridButton(statesBox, MiniJson.S(p, "label"), i++, ref y, () => app.Preset(id));
                presetBtns.Add((b, id));
            }
            y -= 84 + 20;
            presetText = UIKit.LabelAt(statesBox, "", 22, new Color(0.84f, 0.86f, 0.9f), new Vector2(Pad, y), new Vector2(W - 2 * Pad, 120));
            presetText.name = "PresetText";
            var acts = MiniJson.L(app.Def["actions"]);
            if (acts.Count > 0)
            {
                var hdr = UIKit.LabelAt(statesBox, "НАПРАВИ СЕГА", 18, new Color(1, 1, 1, 0.45f), new Vector2(Pad, y), new Vector2(600, 26), UIKit.SemiBold);
                hdr.name = "ActsHeader";
                var actsBox = UIKit.Rect("Acts", statesBox, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, y - 36), new Vector2(0, 100));
                float ay = 0; int k = 0;
                foreach (var o in acts) { var a = MiniJson.O(o); string id = MiniJson.S(a, "id"); GridButton(actsBox, MiniJson.S(a, "label"), k++, ref ay, () => app.Act(id)); }
                actsBox.sizeDelta = new Vector2(0, -ay + 84);
            }
        }

        void BuildDrugs(List<object> drugs)
        {
            drugsBox = UIKit.Rect("Drugs", scrollContent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            float y = -6; int i = 0;
            UIKit.LabelAt(drugsBox, "ДАЙ ЛЕКАРСТВО", 18, new Color(1, 1, 1, 0.45f), new Vector2(Pad, y), new Vector2(600, 26), UIKit.SemiBold); y -= 36;
            foreach (var o in drugs)
            {
                var d = MiniJson.O(o); string id = MiniJson.S(d, "id");
                GridButton(drugsBox, MiniJson.S(d, "label"), i++, ref y, () => app.GiveDrug(id));
            }
            y -= 84 + 18;
            drugStatus = UIKit.LabelAt(drugsBox, "", 22, UIKit.Accent, new Vector2(Pad, y), new Vector2(W - 2 * Pad, 30), UIKit.SemiBold);
            drugHow = UIKit.LabelAt(drugsBox, "Лекарството тук само движи регулаторите, както го обяснява учебникът: механизмът се изписва, щом го дадеш.", 22, new Color(0.84f, 0.86f, 0.9f), new Vector2(Pad, y - 40), new Vector2(W - 2 * Pad, 200));
        }

        // ------------------------------------------------------------------ sheet behaviour
        float dragStartH;
        public void BeginDrag() { dragging = true; dragStartH = height; }
        public void Drag(float dyScreen) { height = Mathf.Clamp(height + dyScreen / Mathf.Max(0.01f, cv.scaleFactor), CollapsedH, SnapH(2)); }
        public void EndDrag(float lastDy)
        {
            dragging = false;
            int best = 0; float bd = float.MaxValue;
            for (int s = 0; s < 3; s++) { float d = Mathf.Abs(SnapH(s) - height); if (d < bd) { bd = d; best = s; } }
            // the direction of the whole drag decides: down folds, up opens (a short flick moves one step)
            float moved = height - dragStartH;
            if ((moved > 20 || lastDy > 6) && best < 2 && SnapH(best) < height) best++;
            if ((moved < -20 || lastDy < -6) && best > 0 && SnapH(best) > height) best--;
            if (moved < -20 && SnapH(best) > dragStartH - 1) best = Mathf.Max(0, best - 1);
            snap = best;
        }
        public void Toggle() { snap = snap == 0 ? 1 : 0; }

        void TabClicked(string id)
        {
            if (snap > 0 && tab == id) { snap = 0; return; }   // tapping the open tab folds the sheet, like closing a drawer
            OpenTab(id);
        }

        void OpenTab(string id)
        {
            tab = id; if (snap == 0) snap = 1;
            ShowTab();
            scrollContent.anchoredPosition = Vector2.zero;
        }

        void ShowTab()
        {
            foreach (var (b, id) in tabBtns) b.On = snap > 0 && id == tab;
            statesBox.gameObject.SetActive(tab == "states");
            if (drugsBox) drugsBox.gameObject.SetActive(tab == "drugs");
            if (graphs) graphs.gameObject.SetActive(tab == "charts");
            if (par) par.gameObject.SetActive(tab == "controls");
        }

        float ContentHeight()
        {
            RectTransform c = tab == "states" ? statesBox : tab == "drugs" ? drugsBox : tab == "charts" ? graphs : par;
            if (c == null) return 100;
            if (c == statesBox || c == drugsBox)
            {
                float lowest = 0;
                foreach (RectTransform ch in c)
                {
                    if (!ch.gameObject.activeSelf) continue;
                    float bottom = -ch.anchoredPosition.y + (ch.pivot.y) * ch.rect.height;
                    var t = ch.GetComponent<Text>(); if (t != null) bottom = -ch.anchoredPosition.y + t.preferredHeight;
                    lowest = Mathf.Max(lowest, bottom);
                }
                return lowest + 30;
            }
            return c.rect.height * c.localScale.y + 30;
        }

        // ------------------------------------------------------------------ portrait on / off
        void Enter()
        {
            active = true; root.gameObject.SetActive(true);
            if (height < 0) height = CollapsedH;
            // the charts and the slider panel move into the sheet, scaled to its width
            if (graphs) { graphsParent = graphs.parent; graphsSibling = graphs.GetSiblingIndex(); Embed(graphs); }
            if (par) { parParent = par.parent; parSibling = par.GetSiblingIndex(); Embed(par); }
            // the bar keeps play, speed and view in one row: the panel buttons are now sheet tabs
            hiddenBarKids.Clear();
            if (bar)
            {
                foreach (Transform c in bar)
                {
                    var t = c.GetComponentInChildren<Text>();
                    if (t != null && (t.text == "Сценарии" || t.text == "Параметри" || t.text == "Учи")) { c.gameObject.SetActive(false); hiddenBarKids.Add(c.gameObject); }
                }
                LayoutStrip();
            }
            ShowTab();
        }

        void Embed(RectTransform rt)
        {
            rt.SetParent(scrollContent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1);
            float k = Mathf.Min(2.4f, (W - 2 * Pad) / Mathf.Max(1, rt.sizeDelta.x));
            rt.localScale = new Vector3(k, k, 1); rt.anchoredPosition = new Vector2(0, -8);
        }

        void Exit()
        {
            active = false;
            if (graphs && graphsParent) { graphs.SetParent(graphsParent, false); graphs.SetSiblingIndex(graphsSibling); graphs.gameObject.SetActive(true); }
            if (par && parParent) { par.SetParent(parParent, false); par.SetSiblingIndex(parSibling); par.gameObject.SetActive(false); }
            foreach (var g in hiddenBarKids) if (g) g.SetActive(true);
            hiddenBarKids.Clear();
            root.gameObject.SetActive(false);
        }

        void LayoutStrip()
        {
            var kids = new List<RectTransform>();
            foreach (Transform c in bar) if (c.gameObject.activeSelf) kids.Add((RectTransform)c);
            kids.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
            float x = 10;
            foreach (var c in kids)
            {
                c.anchorMin = c.anchorMax = new Vector2(0, 0.5f); c.pivot = new Vector2(0, 0.5f);
                c.anchoredPosition = new Vector2(x, 0); x += c.sizeDelta.x + 6;
            }
            float total = x + 4;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(total, 64);
            float k = Mathf.Min(1.6f, (W - 20) / total);
            bar.localScale = new Vector3(k, k, 1); bar.anchoredPosition = new Vector2(0, 10);
            stripH = 64 * k + 20;
        }

        void Update()
        {
            bool portrait = Screen.height > Screen.width * 1.05f;
            if (portrait != active) { if (portrait) Enter(); else Exit(); }
            if (!active) return;

            // the landscape panels stay hidden while the sheet is up
            foreach (var n in new[] { "PhaseCard", "Stats", "Scenarios", "Learn" }) { var t = canvas.Find(n); if (t && t.gameObject.activeSelf) t.gameObject.SetActive(false); }
            if (par && tab == "controls" && !par.gameObject.activeSelf) par.gameObject.SetActive(true);

            float target = SnapH(snap);
            if (!dragging) height = Mathf.Lerp(height, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            sheet.anchoredPosition = new Vector2(0, stripH); sheet.sizeDelta = new Vector2(0, height);
            viewport.gameObject.SetActive(height > CollapsedH + 30);
            scrollContent.sizeDelta = new Vector2(0, ContentHeight());
            foreach (var (b, id) in tabBtns) b.On = height > CollapsedH + 30 && id == tab;

            // live text
            for (int i = 0; i < 4; i++)
            {
                keyVal[i].text = app.KeyVal[i];
                keyVal[i].color = app.KeyBad[i] ? new Color(1f, 0.7f, 0.28f) : UIKit.TextMain;
            }
            stateName.text = app.PresetLabel();
            statePhase.text = app.PhaseTitle; stateDot.color = app.PhaseColor;
            foreach (var (b, id) in presetBtns) b.On = app.PresetId == id;
            if (presetText)
            {
                var p = MiniJson.O(MiniJson.L(app.Def["presets"]).Find(o => MiniJson.S(MiniJson.O(o), "id") == app.PresetId));
                string txt = p != null ? MiniJson.S(p, "text") : "Твои настройки.";
                if (presetText.text != txt)
                {
                    presetText.text = txt;
                    var hdr = statesBox.Find("ActsHeader") as RectTransform; var acts = statesBox.Find("Acts") as RectTransform;
                    float y = presetText.rectTransform.anchoredPosition.y - presetText.preferredHeight - 24;
                    if (hdr) hdr.anchoredPosition = new Vector2(Pad, y);
                    if (acts) acts.anchoredPosition = new Vector2(0, y - 36);
                }
            }
            if (drugsBox)
            {
                drugStatus.text = app.DrugStatus();
                if (!string.IsNullOrEmpty(app.LastDrugHow) && drugHow.text != app.LastDrugHow) drugHow.text = app.LastDrugHow;
                drugHow.rectTransform.anchoredPosition = new Vector2(Pad, drugStatus.rectTransform.anchoredPosition.y - Mathf.Max(30, drugStatus.preferredHeight) - 12);
            }

            // keep the organ in the free space between the header and the sheet
            float headerH = 250, covered = stripH + height, h = H;
            float free = Mathf.Max(0.12f, (h - headerH - covered) / h);
            float centre = (covered + (h - headerH)) * 0.5f / h;
            orbit.ViewOffsetY = 0.536f * (centre - 0.5f);
            orbit.DistanceScale = layout.PortraitScale * 0.8f * Mathf.Clamp(0.58f / free, 1f, 1.8f);   // keep the organ large in the free space
        }
    }

    /// <summary>The sheet's handle and number row: drag to resize, tap to open or fold.</summary>
    public class SheetHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public PhoneSheet Sheet; float lastDy; bool moved;
        public void OnPointerDown(PointerEventData e) { moved = false; }
        public void OnBeginDrag(PointerEventData e) { Sheet.BeginDrag(); lastDy = 0; moved = true; }
        public void OnDrag(PointerEventData e) { Sheet.Drag(e.delta.y); lastDy = e.delta.y; }
        public void OnEndDrag(PointerEventData e) => Sheet.EndDrag(lastDy);
        // the input module also sends a click at the end of a drag on the same object: only a tap without movement toggles
        public void OnPointerClick(PointerEventData e) { if (!moved && !e.dragging) Sheet.Toggle(); }
    }
}
