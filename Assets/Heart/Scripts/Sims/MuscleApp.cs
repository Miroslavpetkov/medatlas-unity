using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using MedAtlas.Heart;
using D = System.Collections.Generic.Dictionary<string, object>;

namespace MedAtlas.Sims
{
    /// <summary>
    /// The muscle section, as on the web (#muscle): skeletal muscle (sarcomere, cross-bridge cycle, triad, length–tension,
    /// motor units) and smooth muscle (pathway, single- vs multi-unit). The page runs the web screen's own scenes, state,
    /// steps and texts (Tools/physio-bridge/muscle.ts); the 3D is the mirrored web scene and the panel is drawn from
    /// muscleView(), so both versions show the same thing.
    /// </summary>
    public class MuscleApp : MonoBehaviour
    {
        Camera cam; OrbitCamera orbit; SceneMirror mirror; int handle = -1;
        Transform C; RectTransform panel, content; ScrollRect scroll;
        Text chipText; readonly List<Text> cardVals = new List<Text>(); RawImage chartImg; Texture2D chartTex; string chartKind = "";
        Text sliderVal; Slider slider; bool settingSlider;
        D view; string layoutKey = ""; float refreshT; PartPicker picker; View2D view2d;
        static bool Portrait => Screen.height > Screen.width * 1.05f;
        /// <summary>Canvas width on a phone held upright: 480 units keep the panel's 15–17 pt text at about 14 CSS px on a 390-px phone.</summary>
        const float PortraitW = 480f;
        float W => Portrait ? 1040f : 600f;   // panel width in canvas units (reference 1920 × 1080)

        public static bool Handles(string id) => id == "muscle" || id == "smooth";

        public void Init(Camera cam, OrbitCamera orbit, string modelId)
        {
            this.cam = cam; this.orbit = orbit;
            cam.fieldOfView = 40f;   // the web camera
            BuildCanvas();
            SceneTabs.Add(C, "Sim");
            Open(PhysioBridge.MuscleOpen(modelId == "smooth" ? "smooth-path" : "sarcomere", Portrait));
            Rebuild(true);
            // 2D schematic of the section's model (web: the "2D" button in the section header), over the free 3D area
            view2d = View2D.AttachMuscle(gameObject, C);
            view2d.Changed = on2d => { if (on2d) picker?.Hide(); };
            if (View2D.Want) view2d.Set(true);
        }

        void BuildCanvas()
        {
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>(); }
            var bgGo = new GameObject("BackdropCanvas"); var bgc = bgGo.AddComponent<Canvas>();
            bgc.renderMode = RenderMode.ScreenSpaceCamera; bgc.worldCamera = cam; bgc.planeDistance = 60f; bgc.sortingOrder = -100;
            var bgr = UIKit.Rect("bg", bgGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bgr.gameObject.AddComponent<RawImage>(); raw.texture = HeartApp.RadialTexture(); raw.raycastTarget = false;
            var cgo = new GameObject("UI"); var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var sc = cgo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.6f;
            cgo.AddComponent<GraphicRaycaster>();
            C = cgo.transform;
        }

        // ------------------------------------------------------------------ 3D

        void Open(int h)
        {
            if (h == handle && mirror != null) return;
            if (mirror != null) Destroy(mirror.gameObject);
            handle = h;
            mirror = h >= 0 ? SceneMirror.Create(h, cam, true) : null;
            if (mirror != null) { Frame(); mirror.OnStructure = () => { if (mirror.Settling) Frame(); }; }
            picker?.Hide();
        }

        /// <summary>The web framing (fitView, margin 4 %) into the part of the screen the panel leaves free.</summary>
        void Frame()
        {
            if (mirror == null) return;
            bool portrait = Portrait;
            var crt = (RectTransform)C;
            float pf = portrait ? 0f : Mathf.Clamp01((W + 40f) / Mathf.Max(1f, crt.rect.width));
            float fw = 1f - pf, fh = portrait ? 0.5f : 1f;
            if (!mirror.Fit(Mathf.Max(0.3f, cam.aspect * fw / fh), cam.fieldOfView, out var target, out var dist)) return;
            dist /= fh;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            orbit.Target = target;
            orbit.ViewOffsetX = pf * tan * cam.aspect;   // the scene sits in the middle of the free area
            orbit.ViewOffsetY = portrait ? (1f - fh) * tan : 0f;
            orbit.DistanceScale = 1f;
            orbit.SetView(mirror.Yaw, mirror.Pitch, dist);
            orbit.MinDistance = dist * 0.15f; orbit.MaxDistance = dist * 4f;
            cam.nearClipPlane = Mathf.Max(0.002f, dist / 150f);
            orbit.Yaw = mirror.Yaw; orbit.Pitch = mirror.Pitch; orbit.Distance = dist; orbit.ResetView();
        }

        // ------------------------------------------------------------------ panel

        void Act(string cmd, string arg = "")
        {
            int h = PhysioBridge.MuscleAct(cmd, arg, Portrait);
            if (h != handle) Open(h);
            Rebuild(false);
        }

        static string LayoutKey(D v)
        {
            // what changes the panel's shape: tab, step, switch, buttons (the chip and the numbers update in place)
            var st = MiniJson.O(v.ContainsKey("steps") ? v["steps"] : null);
            return MiniJson.S(v, "tab") + "|" + MiniJson.S(st, "cur") + "|" + MiniJson.Write(v.ContainsKey("ctl") ? v["ctl"] : null) + "|" + MiniJson.Write(v.ContainsKey("sw") ? v["sw"] : null) + "|" + MiniJson.S(v, "labels") + "|" + (Portrait ? "p" : "l");
        }

        void Rebuild(bool force)
        {
            view = MiniJson.O(MiniJson.Parse(PhysioBridge.MuscleView()));
            if (view == null) return;
            string key = LayoutKey(view);
            if (!force && key == layoutKey) { Refresh(); return; }
            layoutKey = key;
            float keepScroll = scroll != null ? scroll.verticalNormalizedPosition : 1f;
            if (panel != null) Destroy(panel.gameObject);
            cardVals.Clear(); chipText = null; chartImg = null; slider = null; sliderVal = null;
            BuildPanel();
            if (!force && scroll != null) scroll.verticalNormalizedPosition = keepScroll;
            if (picker == null) picker = PartPicker.Attach(gameObject, cam, C, MiniJson.L(view.ContainsKey("parts") ? view["parts"] : null));
        }

        float y; const float Pad = 26f;

        void BuildPanel()
        {
            bool portrait = Portrait;
            var crt = (RectTransform)C;
            var scaler = C.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                // a phone held upright is laid out on a 1080-wide canvas (like the physiology screens), landscape on 1920 × 1080
                scaler.referenceResolution = portrait ? new Vector2(PortraitW, PortraitW * 16f / 9f) : new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = portrait ? 0f : 0.6f;
                Canvas.ForceUpdateCanvases();
            }
            float cw0 = portrait ? PortraitW : crt.rect.width, ch0 = portrait ? PortraitW * Screen.height / Mathf.Max(1, Screen.width) : crt.rect.height;
            float ph = portrait ? ch0 * 0.5f : ch0 - 40f;
            panel = portrait
                ? UIKit.Rect("MusclePanel", C, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(0, ph))
                : UIKit.Rect("MusclePanel", C, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(W, ph));
            UIKit.PanelImage(panel, UIKit.Panel);
            var vp = UIKit.Rect("Viewport", panel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            vp.gameObject.AddComponent<RectMask2D>();
            var vimg = vp.gameObject.AddComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f);
            content = UIKit.Rect("Content", vp, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vp; scroll.content = content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            float w = (portrait ? cw0 : W) - 2 * Pad;
            var v = view;
            y = -Pad;

            Text(MiniJson.S(v, "section").ToUpperInvariant(), 15, UIKit.Accent, UIKit.SemiBold, w);
            Row(MiniJson.L(v["groups"]), w, (id) => Act("group", id), 17, 46);
            y -= 6;
            Row(MiniJson.L(v["tabs"]), w, (id) => Act("tab", id), 16, 44);
            y -= 10;

            // the chip (state / step) and the play controls, as on the 3D in the web
            var chipRt = UIKit.Rect("Chip", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, y), new Vector2(w, 44));
            UIKit.PanelImage(chipRt, UIKit.Card);
            UIKit.Dot(chipRt, new Vector2(14, -16), 12, UIKit.Accent);
            chipText = UIKit.LabelAt(chipRt, MiniJson.S(v, "chip"), 17, UIKit.TextMain, new Vector2(36, -11), new Vector2(w - 48, 26), UIKit.SemiBold);
            chipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float chH = Mathf.Max(44f, chipText.preferredHeight + 20f); chipRt.sizeDelta = new Vector2(w, chH); chipText.rectTransform.sizeDelta = new Vector2(w - 48, chH - 18);
            y -= chH + 10;
            var ctl = MiniJson.L(v["ctl"]);
            if (ctl.Count > 0) { Row(ctl, w, (id) => Act(id), 17, 48); y -= 4; }
            var sw = MiniJson.O(v.ContainsKey("sw") ? v["sw"] : null);
            if (sw != null) { string cmd = MiniJson.S(sw, "cmd"); Row(MiniJson.L(sw["options"]), w, (id) => Act(cmd, id), 16, 44); y -= 4; }

            var sl = MiniJson.O(v.ContainsKey("slider") ? v["slider"] : null);
            if (sl != null)
            {
                string sid = MiniJson.S(sl, "id");
                UIKit.LabelAt(content, MiniJson.S(sl, "label"), 16, UIKit.TextMain, new Vector2(Pad, y), new Vector2(w - 120, 24), UIKit.SemiBold);
                sliderVal = UIKit.LabelAt(content, MiniJson.S(sl, "text"), 16, UIKit.Accent, new Vector2(Pad + w - 120, y), new Vector2(120, 24), UIKit.SemiBold, TextAnchor.UpperRight);
                y -= 34;
                var holder = UIKit.Rect("SliderRow", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, y), new Vector2(w, 30));
                slider = UIKit.Slider(holder, new Vector2(0, -15), new Vector2(w, 26), (float)MiniJson.D(sl, "min"), (float)MiniJson.D(sl, "max"), (float)MiniJson.D(sl, "value"),
                    val => { if (!settingSlider) Act(sid, val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }, false);
                y -= 40;
            }

            var cards = MiniJson.L(v["cards"]);
            if (cards.Count > 0)
            {
                int cols = cards.Count; float g = 8, cw = (w - g * (cols - 1)) / cols;
                for (int i = 0; i < cols; i++)
                {
                    var cd = MiniJson.O(cards[i]);
                    var rt = UIKit.Rect("Card", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + i * (cw + g), y), new Vector2(cw, 76));
                    UIKit.PanelImage(rt, UIKit.Card);
                    UIKit.LabelAt(rt, MiniJson.S(cd, "k"), 14, UIKit.TextDim, new Vector2(12, -8), new Vector2(cw - 20, 20));
                    var val = UIKit.LabelAt(rt, Card(cd), 24, UIKit.TextMain, new Vector2(12, -32), new Vector2(cw - 16, 36), UIKit.SemiBold);
                    val.supportRichText = true; cardVals.Add(val);
                }
                y -= 88;
            }

            chartKind = MiniJson.S(v, "chart");
            if (chartKind != "")
            {
                float chh = chartKind == "lt" ? Mathf.Round(w * 0.55f) : 120f;
                var rt = UIKit.Rect("Chart", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, y), new Vector2(w, chh));
                chartImg = rt.gameObject.AddComponent<RawImage>();
                if (chartKind == "lt")
                {
                    // tap or drag on the curve to set the length, as on the web chart
                    var dr = rt.gameObject.AddComponent<ChartDrag>();
                    dr.OnX = x01 => Act("len", PhysioBridge.MuscleChartLen(x01, Mathf.RoundToInt(w)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
                DrawChart();
                y -= chh + 10;
            }
            var presets = MiniJson.L(v["presets"]);
            if (presets.Count > 0)
            {
                var items = new List<object>();
                foreach (var o in presets) { var p = MiniJson.O(o); items.Add(new D { { "id", MiniJson.S(p, "cmd") + "|" + MiniJson.S(p, "arg") }, { "label", MiniJson.S(p, "label") } }); }
                Row(items, w, id => { var a = id.Split('|'); Act(a[0], a[1]); }, 15, 40);
                y -= 4;
            }

            var steps = MiniJson.O(v.ContainsKey("steps") ? v["steps"] : null);
            if (steps != null)
            {
                int n = (int)MiniJson.D(steps, "n"), cur = (int)MiniJson.D(steps, "cur");
                var chips = new List<object>();
                for (int i = 0; i < n; i++) chips.Add(new D { { "id", i.ToString() }, { "label", (i + 1).ToString() }, { "on", i == cur } });
                Row(chips, w, id => Act("step", id), 16, 40, 52);
                y -= 4;
                Text("Стъпка " + (cur + 1) + " / " + n, 14, UIKit.TextDim, UIKit.Regular, w);
                Text(MiniJson.S(steps, "title"), 21, UIKit.TextMain, UIKit.SemiBold, w);
                Text(MiniJson.S(steps, "text"), 17, UIKit.TextMain, UIKit.Regular, w);
                y -= 10;
            }
            Text(MiniJson.S(v, "title"), 23, UIKit.TextMain, UIKit.SemiBold, w);
            Text(MiniJson.S(v, "intro"), 17, UIKit.TextDim, UIKit.Regular, w);
            y -= 4;
            bool labels = v.TryGetValue("labels", out var lo) && lo is bool lb && lb;
            Row(new List<object> { new D { { "id", "labels" }, { "label", "Надписи" }, { "on", labels } }, new D { { "id", "reset" }, { "label", "Изглед" } } }, w,
                id => { if (id == "reset") Frame(); else Act(id); }, 16, 42);
            y -= 6;
            foreach (var f in MiniJson.L(v["facts"])) Text("•  " + (f as string), 16, UIKit.TextMain, UIKit.Regular, w);
            y -= Pad;
            content.sizeDelta = new Vector2(0, -y);
        }

        static string Card(D cd)
        {
            string u = MiniJson.S(cd, "u");
            return MiniJson.S(cd, "v") + (u != "" ? " <size=14><color=#8D9CB8>" + u + "</color></size>" : "");
        }

        void Text(string s, int size, Color c, Font f, float w)
        {
            if (string.IsNullOrEmpty(s)) return;
            var t = UIKit.LabelAt(content, s, size, c, new Vector2(Pad, y), new Vector2(w, 20), f);
            float h = t.preferredHeight; t.rectTransform.sizeDelta = new Vector2(w, h);
            y -= h + 8;
        }

        /// <summary>A wrapping row of buttons from [{id, label, on}].</summary>
        void Row(List<object> items, float w, System.Action<string> onClick, int font, float h, float minW = 0)
        {
            float x = 0, g = 8;
            foreach (var o in items)
            {
                var d = MiniJson.O(o); string id = MiniJson.S(d, "id"), label = MiniJson.S(d, "label");
                float bw = Mathf.Max(minW, label.Length * font * 0.58f + 30f);
                if (x > 0 && x + bw > w) { x = 0; y -= h + g; }
                var holder = UIKit.Rect("row", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, y), new Vector2(w, h));
                var b = UIKit.Button(holder, label, new Vector2(x, 0), new Vector2(bw, h), () => onClick(id), font);   // the button is anchored at the holder's middle
                bool on = d.TryGetValue("on", out var ov) && ov is bool ob && ob;
                bool main = d.TryGetValue("main", out var mv) && mv is bool mb && mb;
                b.On = on || main;
                x += bw + g;
            }
            y -= h + g;
        }

        /// <summary>The live parts: chip, numbers, slider text and the chart.</summary>
        void Refresh()
        {
            if (view == null) return;
            if (chipText != null) chipText.text = MiniJson.S(view, "chip");
            var cards = MiniJson.L(view["cards"]);
            for (int i = 0; i < cardVals.Count && i < cards.Count; i++) cardVals[i].text = Card(MiniJson.O(cards[i]));
            var sl = MiniJson.O(view.ContainsKey("slider") ? view["slider"] : null);
            if (sl != null && sliderVal != null)
            {
                sliderVal.text = MiniJson.S(sl, "text");
                settingSlider = true; slider.SetValueWithoutNotify((float)MiniJson.D(sl, "value")); settingSlider = false;
            }
            DrawChart();
        }

        byte[] chartBuf = new byte[1 << 20];
        void DrawChart()
        {
            if (chartImg == null || chartKind == "") return;
            var rt = chartImg.rectTransform;
            int w = Mathf.RoundToInt(rt.sizeDelta.x), h = Mathf.RoundToInt(rt.sizeDelta.y);
            int need = w * 2 * h * 2 * 4;
            if (chartBuf.Length < need) chartBuf = new byte[Mathf.NextPowerOfTwo(need)];
            if (PhysioBridge.MuscleChart(chartKind, w, h, chartBuf) != need) return;
            if (chartTex == null || chartTex.width != w * 2 || chartTex.height != h * 2)
            {
                if (chartTex) Destroy(chartTex);
                chartTex = new Texture2D(w * 2, h * 2, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            var data = chartTex.GetPixelData<byte>(0);
            Unity.Collections.NativeArray<byte>.Copy(chartBuf, data, need);
            chartTex.Apply(false, false);
            chartImg.texture = chartTex;
        }

        bool wasPortrait;
        void Update()
        {
            if (mirror == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            mirror.Tick(dt);   // the web loop: steps advance, the scene updates, the motor pool runs
            if (Portrait != wasPortrait) { wasPortrait = Portrait; Rebuild(true); Frame(); }
            refreshT += dt;
            if (refreshT > (chartKind == "emg" ? 0.033f : 0.15f)) { refreshT = 0; Rebuild(false); }
        }

        void OnDestroy() { if (chartTex) Destroy(chartTex); }

        /// <summary>Tap or drag on the length–tension chart.</summary>
        class ChartDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public System.Action<float> OnX;
            void At(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var p))
                    OnX?.Invoke(Mathf.Clamp01((p.x - rt.rect.xMin) / rt.rect.width));
            }
            public void OnPointerDown(PointerEventData e) => At(e);
            public void OnDrag(PointerEventData e) => At(e);
        }
    }
}
