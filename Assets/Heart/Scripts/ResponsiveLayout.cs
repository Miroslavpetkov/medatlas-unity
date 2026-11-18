using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedAtlas.Heart
{
    /// <summary>
    /// Switches the interface between the landscape layout (panels around the organ) and a portrait layout for a
    /// phone held upright: the organ in the upper part, below it one panel at a time chosen with
    /// "Фаза | Графики | Стойности", and the control buttons wrapped on two rows.
    /// </summary>
    public class ResponsiveLayout : MonoBehaviour
    {
        struct RtState { public Vector2 aMin, aMax, pivot, pos, size; public Vector3 scale; }

        CanvasScaler scaler; OrbitCamera orbit;
        RectTransform card, graphs, stats, bar, scen, par, hint;
        readonly Dictionary<RectTransform, RtState> saved = new Dictionary<RectTransform, RtState>();
        RectTransform tabsRoot; UIKit.Btn[] tabs; int tab;
        bool? portrait;
        float distScale;
        public float LandscapeOffsetX = 0f;

        public static ResponsiveLayout Attach(Transform canvas, OrbitCamera orbit, float portraitDistanceScale = 1.35f)
        {
            var l = canvas.gameObject.AddComponent<ResponsiveLayout>();
            l.scaler = canvas.GetComponent<CanvasScaler>(); l.orbit = orbit; l.distScale = portraitDistanceScale;
            RectTransform F(string n) => canvas.Find(n) as RectTransform;
            l.card = F("PhaseCard"); l.graphs = F("Graphs"); l.stats = F("Stats"); l.bar = F("Controls");
            l.scen = F("Scenarios"); l.par = F("Params");
            foreach (Transform t in canvas)
            {
                var tx = t.GetComponent<Text>();
                if (tx != null && tx.text.StartsWith("Плъзни")) l.hint = (RectTransform)t;
            }
            foreach (var rt in new[] { l.card, l.graphs, l.stats, l.bar, l.scen, l.par }) if (rt) l.Save(rt);
            if (l.bar) foreach (Transform c in l.bar) l.Save((RectTransform)c);

            l.tabsRoot = UIKit.Rect("PortraitTabs", canvas, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1040, 60));
            string[] names = { "Фаза", "Графики", "Стойности" };
            l.tabs = new UIKit.Btn[3];
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                l.tabs[i] = UIKit.Button(l.tabsRoot, names[i], new Vector2(i * 350, 0), new Vector2(340, 56), () => { l.tab = k; l.UpdateTabs(); }, 22);
            }
            l.UpdateTabs();
            return l;
        }

        void Save(RectTransform rt)
        {
            saved[rt] = new RtState { aMin = rt.anchorMin, aMax = rt.anchorMax, pivot = rt.pivot, pos = rt.anchoredPosition, size = rt.sizeDelta, scale = rt.localScale };
        }

        void Restore(RectTransform rt)
        {
            if (!saved.TryGetValue(rt, out var s)) return;
            rt.anchorMin = s.aMin; rt.anchorMax = s.aMax; rt.pivot = s.pivot; rt.anchoredPosition = s.pos; rt.localScale = s.scale;
            if (rt != card) rt.sizeDelta = s.size;   // the phase card sizes itself to its text
        }

        void UpdateTabs() { for (int i = 0; i < tabs.Length; i++) tabs[i].On = i == tab; }

        void LateUpdate()
        {
            bool p = Screen.height > Screen.width * 1.05f;
            if (portrait != p) { portrait = p; Apply(p); }
            if (p) PortraitFrame();
        }

        void Apply(bool p)
        {
            if (!p)
            {
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.6f;
                foreach (var rt in saved.Keys) Restore(rt);
                if (graphs) graphs.gameObject.SetActive(true);
                if (stats) stats.gameObject.SetActive(true);
                if (hint) hint.gameObject.SetActive(true);
                bool ov = (scen && scen.gameObject.activeSelf) || (par && par.gameObject.activeSelf);
                if (card && !ov) card.gameObject.SetActive(true);
                tabsRoot.gameObject.SetActive(false);
                if (orbit) { orbit.DistanceScale = 1f; orbit.ViewOffsetY = 0f; orbit.ViewOffsetX = LandscapeOffsetX; }
                return;
            }
            scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = 0f;
            if (hint) hint.gameObject.SetActive(false);
            tabsRoot.gameObject.SetActive(true);
            if (orbit) { orbit.DistanceScale = distScale; orbit.ViewOffsetY = 0.13f; orbit.ViewOffsetX = 0f; }

            // control bar: wrap the buttons on rows of the screen width
            float x = 12, row = 0; const float BS = 1.2f, W = 1040 / BS, rowH = 56;
            var kids = new List<RectTransform>();
            foreach (Transform c in bar) kids.Add((RectTransform)c);
            kids.Sort((a, b) => saved[a].pos.x.CompareTo(saved[b].pos.x));
            foreach (var c in kids)
            {
                float w = saved[c].size.x;
                if (x + w > W - 8) { x = 12; row++; }
                c.anchorMin = c.anchorMax = new Vector2(0, 1); c.pivot = new Vector2(0, 1);
                c.anchoredPosition = new Vector2(x, -10 - row * rowH);
                x += w + 8;
            }
            bar.anchorMin = bar.anchorMax = new Vector2(0, 0); bar.pivot = new Vector2(0, 0);
            bar.anchoredPosition = new Vector2(20, 16); bar.sizeDelta = new Vector2(W, (row + 1) * rowH + 12);
            bar.localScale = new Vector3(BS, BS, 1);
        }

        void PortraitFrame()
        {
            var canvasRt = (RectTransform)transform;
            float H = canvasRt.rect.height;
            float barTop = bar.anchoredPosition.y + bar.sizeDelta.y * bar.localScale.y;
            float areaBottom = barTop + 10;
            float areaTop = H * 0.47f;                 // the organ uses the screen above this line
            tabsRoot.anchoredPosition = new Vector2(20, areaTop - 60);
            float avail = areaTop - 70 - areaBottom;

            bool overlay = (scen && scen.gameObject.activeSelf) || (par && par.gameObject.activeSelf);
            var show = overlay ? null : (tab == 0 ? card : tab == 1 ? graphs : stats);
            if (graphs) graphs.gameObject.SetActive(show == graphs);
            if (stats) stats.gameObject.SetActive(show == stats);
            if (card && !overlay) card.gameObject.SetActive(show == card);
            tabsRoot.gameObject.SetActive(!overlay);

            void Fit(RectTransform rt, float top, float bottom)
            {
                if (rt == null || !rt.gameObject.activeSelf) return;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 1);
                float w = rt.sizeDelta.x, h = Mathf.Max(rt.sizeDelta.y, 1);
                float k = Mathf.Min(1040f / w, (top - bottom) / h, 1.5f);
                rt.localScale = new Vector3(k, k, 1);
                rt.anchoredPosition = new Vector2(0, top);
            }
            if (overlay)
            {
                Fit(scen, H - 20, areaBottom);
                Fit(par, H - 20, areaBottom);
            }
            else Fit(show, areaTop - 74, areaBottom);
        }
    }
}
