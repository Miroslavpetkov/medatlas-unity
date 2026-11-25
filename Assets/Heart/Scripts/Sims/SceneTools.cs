using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MedAtlas.Heart;

namespace MedAtlas.Sims
{
    /// <summary>
    /// The 3D tools of a mirrored web scene, the same as on the web (instructions v4, part III):
    /// a tapped part gets a mini-bar (Скрий / Полупрозрачен / Изолирай / Фокус / Покажи всичко), a double tap flies the
    /// camera to it, and a "Слоеве и срез" button opens the layers slider (skin → muscles → vessels and nerves → organs)
    /// and the live section (sagittal / frontal / horizontal plane with a slider). The parts are hidden or faded in the
    /// web scene itself (mirror.ts), so Unity just draws what it is sent; the section is a clip plane in the mirror shaders.
    /// </summary>
    public class SceneTools : MonoBehaviour
    {
        int h; SceneMirror mirror; OrbitCamera orbit; PartPicker picker; Transform canvas;
        RectTransform bar; Text barTitle; readonly List<(UIKit.Btn b, string cmd)> barBtns = new List<(UIKit.Btn, string)>();
        RectTransform toolBtn, panel; Text peelText; Slider peelSlider, secSlider;
        readonly List<(UIKit.Btn b, string axis)> axisBtns = new List<(UIKit.Btn, string)>();
        string partId = ""; Renderer partR; List<string> layers = new List<string>();
        string axis = ""; float secPos; Vector4 localPlane; bool planeOn;

        static readonly int ClipId = Shader.PropertyToID("_MirClip"), ClipOnId = Shader.PropertyToID("_MirClipOn");

        public static SceneTools Attach(GameObject host, int handle, SceneMirror mirror, OrbitCamera orbit, PartPicker picker, Transform canvas)
        {
            var t = host.AddComponent<SceneTools>();
            t.h = handle; t.mirror = mirror; t.orbit = orbit; t.picker = picker; t.canvas = canvas;
            t.Build();
            picker.OnPicked += t.Picked;
            picker.Keep = t.Kept;
            return t;
        }

        static bool Portrait => Screen.height > Screen.width * 1.05f;

        void Build()
        {
            // the mini-bar under the part card
            bar = UIKit.Rect("PartTools", canvas, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -340), new Vector2(760, 64));
            UIKit.PanelImage(bar, new Color(11 / 255f, 18 / 255f, 32 / 255f, 0.85f));
            barTitle = UIKit.LabelAt(bar, "", 17, UIKit.TextMain, new Vector2(16, -20), new Vector2(150, 26), UIKit.SemiBold);
            float x = 170;
            foreach (var (label, cmd, w) in new[] { ("Скрий", "hide", 92f), ("Полупрозрачен", "fade", 170f), ("Изолирай", "iso", 118f), ("Фокус", "focus", 92f), ("Покажи всичко", "all", 168f), ("✕", "close", 52f) })
            {
                var c = cmd;
                var b = UIKit.Button(bar, label, new Vector2(x, 0), new Vector2(w, 48), () => BarCmd(c), 16);
                barBtns.Add((b, cmd)); x += w + 6;
            }
            bar.sizeDelta = new Vector2(x + 10, 64);
            bar.gameObject.SetActive(false);

            // the layers / section button and its panel (right edge of the view)
            toolBtn = UIKit.Button(canvas, "Слоеве и срез", Vector2.zero, new Vector2(180, 52), TogglePanel, 16).Button.GetComponent<RectTransform>();
            toolBtn.anchorMin = toolBtn.anchorMax = new Vector2(1, 0.5f); toolBtn.pivot = new Vector2(1, 0.5f);
            toolBtn.anchoredPosition = new Vector2(-24, 120);
            panel = UIKit.Rect("ToolsPanel", canvas, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 1), new Vector2(-24, 84), new Vector2(460, 300));
            UIKit.PanelImage(panel, new Color(11 / 255f, 18 / 255f, 32 / 255f, 0.92f));
            UIKit.LabelAt(panel, "Слоеве", 18, UIKit.TextMain, new Vector2(18, -14), new Vector2(200, 26), UIKit.SemiBold);
            peelText = UIKit.LabelAt(panel, "всичко се вижда", 15, UIKit.TextDim, new Vector2(18, -42), new Vector2(424, 22));
            peelSlider = UIKit.Slider(panel, new Vector2(18, 0), new Vector2(424, 26), 0, 1, 0, v => Peel(Mathf.RoundToInt(v)));
            ((RectTransform)peelSlider.transform).anchorMin = ((RectTransform)peelSlider.transform).anchorMax = new Vector2(0, 1);
            ((RectTransform)peelSlider.transform).anchoredPosition = new Vector2(18, -84);
            UIKit.LabelAt(panel, "Срез", 18, UIKit.TextMain, new Vector2(18, -116), new Vector2(200, 26), UIKit.SemiBold);
            float ax = 18;
            foreach (var (label, a, w) in new[] { ("Без", "", 70f), ("Сагитален", "x", 116f), ("Фронтален", "z", 116f), ("Хоризонтален", "y", 136f) })
            {
                var aa = a; UIKit.Btn b = null;
                b = UIKit.Button(panel, label, new Vector2(ax, 0), new Vector2(w - 6, 44), () => Section(aa, aa == axis ? secPos : 0f), 15);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(ax, -150);
                axisBtns.Add((b, a)); ax += w;
            }
            secSlider = UIKit.Slider(panel, new Vector2(18, 0), new Vector2(424, 26), -1, 1, 0, v => Section(axis, v), false);
            ((RectTransform)secSlider.transform).anchorMin = ((RectTransform)secSlider.transform).anchorMax = new Vector2(0, 1);
            ((RectTransform)secSlider.transform).anchoredPosition = new Vector2(18, -232);
            panel.gameObject.SetActive(false);
            Section("", 0);
        }

        void TogglePanel()
        {
            bool on = !panel.gameObject.activeSelf;
            panel.gameObject.SetActive(on);
            if (on)
            {
                var js = MiniJson.Parse(PhysioBridge.SceneTool(h, "layers", "") ?? "[]") as List<object>;
                layers.Clear(); if (js != null) foreach (var o in js) layers.Add(o as string ?? "");
                peelSlider.maxValue = Mathf.Max(1, layers.Count); peelSlider.interactable = layers.Count > 0;
                PeelLabel((int)peelSlider.value);
                panel.SetAsLastSibling();
            }
            Haptics.Tick();
        }

        void Peel(int n)
        {
            PhysioBridge.SceneTool(h, "peel", n.ToString());
            PeelLabel(n);
        }

        void PeelLabel(int n)
        {
            if (layers.Count == 0) { peelText.text = "тази сцена няма анатомични слоеве"; return; }
            peelText.text = n <= 0 ? "всичко се вижда" : "без " + string.Join(", ", layers.GetRange(0, Mathf.Min(n, layers.Count)));
        }

        void Section(string a, float pos)
        {
            axis = a; secPos = pos;
            foreach (var (b, x) in axisBtns) b.On = x == a;
            secSlider.gameObject.SetActive(a != "");
            if (Mathf.Abs(secSlider.value - pos) > 1e-4f) secSlider.SetValueWithoutNotify(pos);
            planeOn = false;
            if (a != "")
            {
                var p = (PhysioBridge.SceneTool(h, "section", a + "|" + pos.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? "").Split(',');
                if (p.Length == 4 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nx)
                    && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ny)
                    && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nz)
                    && float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                { localPlane = new Vector4(nx, ny, nz, d); planeOn = true; }
            }
            ApplyPlane();
        }

        /// <summary>The plane comes in the mirror's own axes; the shaders want it in world space (the mirror can move).</summary>
        Vector4 WorldPlane()
        {
            var n = new Vector3(localPlane.x, localPlane.y, localPlane.z);
            var p0 = -localPlane.w * n;
            var nw = mirror.transform.TransformDirection(n).normalized;
            var pw = mirror.transform.TransformPoint(p0);
            return new Vector4(nw.x, nw.y, nw.z, -Vector3.Dot(nw, pw));
        }

        void ApplyPlane()
        {
            Shader.SetGlobalFloat(ClipOnId, planeOn ? 1f : 0f);
            if (planeOn) Shader.SetGlobalVector(ClipId, WorldPlane());
        }

        bool Kept(Vector3 world)
        {
            if (!planeOn) return true;
            var w = WorldPlane();
            return w.x * world.x + w.y * world.y + w.z * world.z + w.w >= 0;
        }

        void Picked(Renderer r, bool twice)
        {
            if (r == null || r.GetComponentInParent<SceneMirror>() != mirror) { HideBar(); return; }
            partId = r.gameObject.name; partR = r;
            if (twice) Focus();
            ShowBar();
        }

        void ShowBar()
        {
            string mode = PhysioBridge.SceneTool(h, "modeOf", partId) ?? "show";
            bool changed = PhysioBridge.SceneTool(h, "changed", "") == "1";
            barTitle.text = picker.Card != null && picker.Card.gameObject.activeSelf ? "" : partId;
            foreach (var (b, cmd) in barBtns)
            {
                b.On = (cmd == "hide" && mode == "hide") || (cmd == "fade" && mode == "fade") || (cmd == "iso" && mode == "isolate");
                if (cmd == "all") b.Button.gameObject.SetActive(changed);
            }
            bar.gameObject.SetActive(true); bar.SetAsLastSibling();
            Place();
        }

        void HideBar()
        {
            // keep "Покажи всичко" reachable while something is hidden or faded
            if (PhysioBridge.SceneTool(h, "changed", "") == "1")
            {
                partId = ""; barTitle.text = "";
                foreach (var (b, cmd) in barBtns) { b.Button.gameObject.SetActive(cmd == "all" || cmd == "close"); b.On = false; }
                bar.gameObject.SetActive(true); Place();
            }
            else bar.gameObject.SetActive(false);
        }

        void BarCmd(string cmd)
        {
            Haptics.Tick();
            if (cmd == "close") { foreach (var (b, _) in barBtns) b.Button.gameObject.SetActive(true); bar.gameObject.SetActive(false); picker.Hide(); return; }
            if (cmd == "all") { PhysioBridge.SceneTool(h, "all", ""); foreach (var (b, _) in barBtns) b.Button.gameObject.SetActive(true); bar.gameObject.SetActive(false); picker.Hide(); return; }
            if (partId == "") return;
            string mode = PhysioBridge.SceneTool(h, "modeOf", partId) ?? "show";
            if (cmd == "hide") PhysioBridge.SceneTool(h, "mode", partId + "|" + (mode == "hide" ? "show" : "hide"));
            else if (cmd == "fade") PhysioBridge.SceneTool(h, "mode", partId + "|" + (mode == "fade" ? "show" : "fade"));
            else if (cmd == "iso") PhysioBridge.SceneTool(h, "iso", mode == "isolate" ? "" : partId);
            else if (cmd == "focus") Focus();
            ShowBar();
        }

        void Focus()
        {
            if (partR == null) return;
            var b = partR.bounds;
            orbit.FocusOn(b.center, Mathf.Max(b.extents.magnitude * 3.2f, orbit.MinDistance));
        }

        void Place()
        {
            // right under the part card (its height follows the text), centred
            float y = -340;
            var c = picker.Card;
            if (c != null && c.gameObject.activeSelf) y = c.anchoredPosition.y - c.sizeDelta.y - 12;
            bar.anchoredPosition = new Vector2(0, y);
            float scale = Portrait ? 1.35f : 1f;   // portrait phones scale the 1920-wide canvas down a lot
            bar.localScale = Vector3.one * Mathf.Min(scale, (((RectTransform)canvas).rect.width - 24) / Mathf.Max(1, bar.sizeDelta.x));
        }

        void Update()
        {
            if (planeOn) ApplyPlane();
            if (bar.gameObject.activeSelf) Place();
            float k = Portrait ? 1.3f : 1f;
            toolBtn.localScale = panel.localScale = Vector3.one * k;
        }

        void OnDisable() { Shader.SetGlobalFloat(ClipOnId, 0f); }
        void OnDestroy()
        {
            Shader.SetGlobalFloat(ClipOnId, 0f);
            if (bar) Destroy(bar.gameObject);
            if (toolBtn) Destroy(toolBtn.gameObject);
            if (panel) Destroy(panel.gameObject);
        }
    }

    /// <summary>A short vibration on phones (WebGL: navigator.vibrate through the page; elsewhere nothing).</summary>
    public static class Haptics
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void MedPhysio_Vibrate(int ms);
        public static void Tick(int ms = 8) { try { MedPhysio_Vibrate(ms); } catch { } }
#else
        public static void Tick(int ms = 8) { }
#endif
    }
}
