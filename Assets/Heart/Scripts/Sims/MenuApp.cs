using System.Collections.Generic;
using MedAtlas.Heart;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedAtlas.Sims
{
    /// <summary>Catalog of all simulations (scene "Menu") in the style of the Med Simulation 3D web app:
    /// a hero with numbers, colour filter chips per organ system and a grid of cards.</summary>
    public class MenuApp : MonoBehaviour
    {
        public static readonly (string area, string bg, string color, string glyph)[] Areas =
        {
            ("Journey", "Пътят през тялото", "#3ddc97", "П"), ("Heart", "Сърце", "#ff5d8f", "С"), ("Circulation", "Кръвообращение и налягане", "#ff6b6b", "К"),
            ("Lungs", "Дишане", "#7cc4ff", "Д"), ("Blood", "Кръв и газове", "#ff7a7a", "Кр"), ("Kidney", "Бъбрек, вода и соли", "#c39bff", "Б"),
            ("Nerve", "Нервна система и сетива", "#ffd166", "Н"), ("Muscle", "Мускул", "#ff9f6b", "М"), ("Metabolism", "Метаболизъм и хормони", "#7ee0a6", "Мх"),
            ("Body", "Цялото тяло", "#3b8bff", "Т"),
        };
        public static (string bg, Color color, string glyph) AreaInfo(string area)
        {
            foreach (var a in Areas) if (a.area == area) return (a.bg, UIKit.Hex(a.color, UIKit.Accent), a.glyph);
            return (area, UIKit.Accent, "•");
        }

        static bool deepLinked;
        CanvasScaler scaler; RectTransform content, viewport, hero;
        Text heroText; RectTransform[] statTiles;
        readonly List<(RectTransform rt, string area)> cards = new List<(RectTransform, string)>();
        readonly List<(RectTransform rt, string area)> headers = new List<(RectTransform, string)>();
        readonly List<(UIKit.Btn b, string area, float w)> chips = new List<(UIKit.Btn, string, float)>();
        RectTransform allTitle; Text allCount;
        string filter = "all";
        bool? portrait; bool dirty = true;

        void Start()
        {
            Application.targetFrameRate = 60;
            var cla = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(cla, "-organ") >= 0) { SceneManager.LoadScene("Sim"); return; }
            if (System.Array.IndexOf(cla, "-lungs") >= 0) { SceneManager.LoadScene("Lungs"); return; }
            // shareable deep link: ...index.html?sim=<id> opens that simulation directly (once per page load)
            if (!deepLinked)
            {
                deepLinked = true;
                string url = Application.absoluteURL ?? ""; int k = url.IndexOf("sim=");
                if (k >= 0)
                {
                    string id = url.Substring(k + 4); int e = id.IndexOfAny(new[] { '&', '#' }); if (e >= 0) id = id.Substring(0, e);
                    id = UnityEngine.Networking.UnityWebRequest.UnEscapeURL(id);
                    if (id.Length > 0) { Open(id); return; }
                }
            }
            var camGo = new GameObject("Main Camera"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = UIKit.Bg;
            UIKit.Init();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>(); }
            var cgo = new GameObject("UI"); var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.6f;
            cgo.AddComponent<GraphicRaycaster>();
            var bg = UIKit.Rect("bg", cgo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bg.gameObject.AddComponent<RawImage>(); raw.texture = HeartApp.RadialTexture(); raw.raycastTarget = false;
            var C = cgo.transform;

            // the whole page scrolls
            viewport = UIKit.Rect("Viewport", C, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var vimg = viewport.gameObject.AddComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UIKit.Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            var sr = viewport.gameObject.AddComponent<ScrollRect>(); sr.content = content; sr.viewport = viewport; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;

            var list = new List<Dictionary<string, object>>();
            foreach (var o in MiniJson.L(MiniJson.Parse(PhysioBridge.List()))) list.Add(MiniJson.O(o));
            if (list.Count == 0)
            {
                list.Add(new Dictionary<string, object> { { "id", "cardiac-cycle" }, { "title", "Сърдечен цикъл" }, { "area", "Heart" }, { "summary", "Отваря се в уеб версията." } });
                list.Add(new Dictionary<string, object> { { "id", "breathing" }, { "title", "Дишане и газообмен" }, { "area", "Lungs" }, { "summary", "Отваря се в уеб версията." } });
            }

            // hero
            hero = UIKit.Rect("Hero", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(100, 300));
            UIKit.PanelImage(hero, UIKit.Panel);
            UIKit.LabelAt(hero, "MED SIMULATION 3D", 14, UIKit.Accent, new Vector2(36, -30), new Vector2(600, 20), UIKit.SemiBold);
            UIKit.LabelAt(hero, "Физиология на живо", 40, UIKit.TextMain, new Vector2(34, -54), new Vector2(1000, 60), UIKit.SemiBold);
            heroText = UIKit.LabelAt(hero, "Виж как работи тялото, не само как изглежда. Всеки модел е истински математически модел, който работи на живо: промени пулса, кръвозагубата, храната или болестта и гледай как отговарят органите в 3D и графиките. Новото: пътят на храната, водата и газовете през цялото тяло.",
                17, UIKit.TextDim, new Vector2(36, -122), new Vector2(900, 80));
            int nPresets = 0, nCharts = 0, nControls = 0;
            foreach (var m in list) { nPresets += (int)MiniJson.D(m, "presets"); nCharts += (int)MiniJson.D(m, "charts"); nControls += (int)MiniJson.D(m, "controls"); }
            var stats = new[] { (list.Count.ToString(), "живи модела"), (nPresets.ToString(), "състояния и болести"), (nControls.ToString(), "регулатора"), (nCharts.ToString(), "живи графики") };
            statTiles = new RectTransform[stats.Length];
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = UIKit.Rect("stat", hero, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(200, 84));
                UIKit.PanelImage(tile, UIKit.Card);
                UIKit.LabelAt(tile, stats[i].Item1, 32, UIKit.TextMain, new Vector2(20, -12), new Vector2(170, 40), UIKit.SemiBold);
                UIKit.LabelAt(tile, stats[i].Item2, 14, UIKit.TextDim, new Vector2(20, -52), new Vector2(180, 22));
                statTiles[i] = tile;
            }

            // "All models" + filter chips
            allTitle = UIKit.Rect("AllTitle", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(600, 40));
            UIKit.LabelAt(allTitle, "Всички модели", 26, UIKit.TextMain, Vector2.zero, new Vector2(260, 36), UIKit.SemiBold);
            allCount = UIKit.LabelAt(allTitle, list.Count.ToString(), 22, UIKit.TextDim, new Vector2(214, -3), new Vector2(120, 32));
            AddChip("all", "Всички", UIKit.Accent);
            foreach (var a in Areas)
            {
                if (!list.Exists(m => MiniJson.S(m, "area") == a.area)) continue;
                AddChip(a.area, a.bg, UIKit.Hex(a.color, UIKit.Accent));
            }

            // cards grouped by area
            foreach (var a in Areas)
            {
                var inArea = list.FindAll(m => MiniJson.S(m, "area") == a.area);
                if (inArea.Count == 0) continue;
                var col = UIKit.Hex(a.color, UIKit.Accent);
                var hrt = UIKit.Rect("H_" + a.area, content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(800, 40));
                var dot = UIKit.Rect("bar", hrt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(6, 26));
                UIKit.PanelImage(dot, col, false);
                UIKit.LabelAt(hrt, a.bg, 22, col, new Vector2(18, -4), new Vector2(760, 32), UIKit.SemiBold);
                headers.Add((hrt, a.area));
                foreach (var m in inArea) cards.Add((Card(m, a.bg, col, a.glyph), a.area));
            }
        }

        void AddChip(string area, string label, Color c)
        {
            UIKit.Btn b = null;
            float w = label.Length * 9.6f + 44;
            b = UIKit.Button(content, label, Vector2.zero, new Vector2(w, 42), () => { filter = area; dirty = true; foreach (var ch in chips) ch.b.On = ch.area == filter; }, 15);
            b.OnColor = new Color(c.r, c.g, c.b, 0.85f);
            b.On = area == filter;
            chips.Add((b, area, w));
        }

        RectTransform Card(Dictionary<string, object> m, string areaBg, Color col, string glyph)
        {
            string id = MiniJson.S(m, "id");
            var rt = UIKit.Rect("card_" + id, content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(560, 150));
            var img = UIKit.PanelImage(rt, UIKit.Card);
            var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            var cb = btn.colors; cb.highlightedColor = new Color(1.35f, 1.35f, 1.45f, 1f); cb.pressedColor = new Color(0.85f, 0.85f, 0.9f, 1f); btn.colors = cb;
            btn.onClick.AddListener(() => Open(id));
            // coloured icon tile
            var ic = UIKit.Rect("icon", rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(58, 58));
            UIKit.PanelImage(ic, new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 0.95f));
            var gl = UIKit.Label(ic, glyph, 24, col, UIKit.SemiBold, TextAnchor.MiddleCenter);
            gl.rectTransform.anchorMin = Vector2.zero; gl.rectTransform.anchorMax = Vector2.one; gl.rectTransform.offsetMin = gl.rectTransform.offsetMax = Vector2.zero;
            // text
            var tag = UIKit.Label(rt, areaBg.ToUpperInvariant(), 12, col, UIKit.SemiBold);
            Stretch(tag.rectTransform, 94, -18, 18, 18);
            var title = UIKit.Label(rt, MiniJson.S(m, "title"), 18, UIKit.TextMain, UIKit.SemiBold);
            Stretch(title.rectTransform, 94, -38, 18, 48); title.verticalOverflow = VerticalWrapMode.Truncate;
            var sum = UIKit.Label(rt, MiniJson.S(m, "summary"), 13, UIKit.TextDim);
            Stretch(sum.rectTransform, 94, -84, 18, 36); sum.verticalOverflow = VerticalWrapMode.Truncate;
            int presets = (int)MiniJson.D(m, "presets"), charts = (int)MiniJson.D(m, "charts");
            var meta = UIKit.Label(rt, presets + " състояния  ·  " + charts + " графики  ·  3D", 12, new Color(UIKit.TextDim.r, UIKit.TextDim.g, UIKit.TextDim.b, 0.8f), UIKit.SemiBold);
            Stretch(meta.rectTransform, 94, -122, 18, 18);
            return rt;
        }

        static void Stretch(RectTransform r, float left, float top, float right, float h)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0, 1);
            r.offsetMin = new Vector2(left, top - h); r.offsetMax = new Vector2(-right, top);
        }

        static void Open(string id)
        {
            SimApp.SelectedId = id; SceneManager.LoadScene("Sim");
        }

        void Update()
        {
            bool p = Screen.height > Screen.width * 1.05f;
            if (portrait != p) { portrait = p; dirty = true; }
            if (!dirty || content == null) return;
            dirty = false;
            scaler.referenceResolution = p ? new Vector2(480, 854) : new Vector2(1920, 1080);   // phone: big, readable text
            scaler.matchWidthOrHeight = p ? 0f : 0.6f;
            Canvas.ForceUpdateCanvases();
            float pad = p ? 12 : 64;
            float W = Mathf.Min(viewport.rect.width - pad * 2, 1640);
            float x0 = (viewport.rect.width - W) / 2;
            float y = -(p ? 16 : 44);

            // hero
            float statW = p ? (W - 48 - 12) / 2 : 200;
            heroText.rectTransform.sizeDelta = new Vector2(p ? W - 72 : Mathf.Min(W - 72 - 4 * (statW + 14), 940), 100);
            float th = heroText.preferredHeight;
            heroText.rectTransform.sizeDelta = new Vector2(heroText.rectTransform.sizeDelta.x, th);
            float heroH;
            if (p)
            {
                for (int i = 0; i < statTiles.Length; i++) { statTiles[i].anchoredPosition = new Vector2(24 + (i % 2) * (statW + 12), -140 - th - (i / 2) * 98); statTiles[i].sizeDelta = new Vector2(statW, 84); }
                heroH = 140 + th + 2 * 98 + 12;
            }
            else
            {
                for (int i = 0; i < statTiles.Length; i++) { statTiles[i].anchoredPosition = new Vector2(W - 24 - (4 - i) * (statW + 14) + 14, -60); statTiles[i].sizeDelta = new Vector2(statW, 84); }
                heroH = Mathf.Max(140 + th, 170) + 26;
            }
            hero.anchoredPosition = new Vector2(x0, y); hero.sizeDelta = new Vector2(W, heroH);
            y -= heroH + 34;

            // title + chips
            allTitle.anchoredPosition = new Vector2(x0 + 4, y); y -= 52;
            int visible = cards.FindAll(c => filter == "all" || c.area == filter).Count;
            allCount.text = visible.ToString();
            float cx = 0;
            foreach (var ch in chips)
            {
                var rt = ch.b.Button.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                if (cx + ch.w > W) { cx = 0; y -= 52; }
                rt.anchoredPosition = new Vector2(x0 + cx, y); cx += ch.w + 10;
            }
            y -= 52 + 22;

            // cards
            int cols = p ? 1 : (W > 1500 ? 3 : 2);
            float gap = p ? 10 : 16, colW = (W - gap * (cols - 1)) / cols, cardH = p ? 156 : 150;
            foreach (var h in headers)
            {
                bool showH = filter == "all" || h.area == filter;
                h.rt.gameObject.SetActive(showH);
                var inArea = cards.FindAll(c => c.area == h.area);
                foreach (var c in inArea) c.rt.gameObject.SetActive(showH);
                if (!showH) continue;
                h.rt.anchoredPosition = new Vector2(x0, y); h.rt.sizeDelta = new Vector2(W, 40); y -= 52;
                int col = 0;
                foreach (var c in inArea)
                {
                    c.rt.anchoredPosition = new Vector2(x0 + col * (colW + gap), y); c.rt.sizeDelta = new Vector2(colW, cardH);
                    if (++col >= cols) { col = 0; y -= cardH + gap; }
                }
                if (col != 0) y -= cardH + gap;
                y -= 22;
            }
            content.sizeDelta = new Vector2(0, -y + 40);
        }
    }
}
