using MedAtlas.Heart;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>
    /// 2D schematic mode (instructions v4, part VI): the web version's own 2D view of this simulation, drawn by the
    /// bridge as an SVG in an HTML layer over the 3D area (Tools/physio-bridge/view2d.ts). It reads the same running
    /// instance as the 3D scene and the charts, so switching keeps the state, sliders, phase and numbers.
    /// The 3D area is the screen between the header, the phase card / panels on the left, the values and charts on the
    /// right and the control bar; on a phone held upright, between the header and the bottom sheet.
    /// A "2D схема" button sits in the top right corner of the 3D area, and the layer has a "3D сцена" button in the
    /// same place, so the switch is always where the student looks. The muscle section uses the same layer on its own
    /// instance of the 'muscle' / 'smooth' model, driven by the section's controls (AttachMuscle).
    /// </summary>
    public class View2D : MonoBehaviour
    {
        /// <summary>The student's choice stays for the next simulation, as on the web.</summary>
        public static bool Want;

        int handle = -1;
        bool on, muscle;
        Rect last;
        System.Func<bool> playing;
        Transform canvas;
        RectTransform title, card, stats, bar;
        RectTransform[] leftPanels;
        ResponsiveLayout layout;
        PhoneSheet sheet;
        UIKit.Btn toggle;
        readonly Vector3[] c = new Vector3[4];

        public bool Has { get; private set; }
        public bool On => on;
        public System.Action<bool> Changed;

        public static View2D Attach(GameObject host, int handle, string simId, Transform canvas, ResponsiveLayout layout, PhoneSheet sheet, System.Func<bool> playing, params RectTransform[] leftPanels)
        {
            var v = host.AddComponent<View2D>();
            v.handle = handle;
            v.playing = playing;
            v.layout = layout;
            v.leftPanels = leftPanels;
            v.canvas = canvas;
            v.title = canvas.Find("Title") as RectTransform;
            v.card = canvas.Find("PhaseCard") as RectTransform;
            v.stats = canvas.Find("Stats") as RectTransform;
            v.bar = canvas.Find("Controls") as RectTransform;
            v.sheet = sheet;
            v.Has = PhysioBridge.View2dHas(simId);
            v.MakeToggle();
            return v;
        }

        /// <summary>The muscle section: the 3D area is what its panel leaves free (right in landscape, bottom on a phone).</summary>
        public static View2D AttachMuscle(GameObject host, Transform canvas)
        {
            var v = host.AddComponent<View2D>();
            v.muscle = true;
            v.canvas = canvas;
            v.Has = PhysioBridge.View2dHas("muscle");
            v.MakeToggle();
            return v;
        }

        void MakeToggle()
        {
            if (!Has) return;
            toggle = UIKit.Button(canvas, "2D схема", Vector2.zero, new Vector2(150, 44), () => Set(true), 17);
            toggle.On = true;   // accent colour: easy to find
            var rt = toggle.Button.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(1, 1);
            toggle.Button.gameObject.name = "View2DToggle";
        }

        public void Set(bool show)
        {
            if (!Has) show = false;
            Want = show;
            if (show == on) return;
            on = show;
            if (on)
            {
                last = default;
                if (muscle) PhysioBridge.Muscle2dOpen(); else PhysioBridge.View2dOpen(handle);
                Place();
            }
            else PhysioBridge.View2dClose();
            if (toggle != null) toggle.Button.gameObject.SetActive(!on);
            Changed?.Invoke(on);
        }

        public void Toggle() => Set(!on);

        void LateUpdate()
        {
            if (!Has) return;
            Place();
            if (!on) return;
            if (PhysioBridge.View2dPoll()) { Set(false); return; }   // "3D сцена" on the layer
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (muscle) PhysioBridge.Muscle2dFrame(dt);
            else PhysioBridge.View2dFrame(handle, dt, playing == null || playing());
        }

        bool Visible(RectTransform rt) => rt != null && rt.gameObject.activeInHierarchy;
        float Bottom(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Min(c[0].y, c[3].y); }
        float Top(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Max(c[1].y, c[2].y); }
        float Left(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Min(c[0].x, c[1].x); }
        float Right(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Max(c[2].x, c[3].x); }

        Rect Area()
        {
            float W = Screen.width, H = Screen.height, pad = Mathf.Max(6, H * 0.008f);
            float x0 = pad, x1 = W - pad, y0 = pad, y1 = H - pad;
            if (muscle)
            {
                // under the scene tabs; beside (landscape) or above (phone) the section's panel
                var tabs = canvas.Find("btn_Каталог") as RectTransform;
                if (Visible(tabs)) y1 = Bottom(tabs) - pad;
                var panel = canvas.Find("MusclePanel") as RectTransform;
                if (Visible(panel))
                {
                    if (Screen.height > Screen.width * 1.05f) y0 = Top(panel) + pad;
                    else x1 = Left(panel) - pad;
                }
            }
            else
            {
                if (Visible(title)) y1 = Bottom(title) - pad;
                bool portrait = layout != null && layout.Portrait;
                if (portrait)
                {
                    // phone: from the header down to the bottom sheet (or the control strip under it)
                    var sh = sheet != null ? sheet.SheetRect : null;
                    if (Visible(sh)) y0 = Top(sh) + pad;
                    else if (Visible(bar)) y0 = Top(bar) + pad;
                }
                else
                {
                    if (Visible(bar)) y0 = Top(bar) + pad;
                    if (Visible(card)) x0 = Mathf.Max(x0, Right(card) + pad);
                    if (leftPanels != null) foreach (var p in leftPanels) if (Visible(p)) x0 = Mathf.Max(x0, Right(p) + pad);
                    if (Visible(stats)) x1 = Mathf.Min(x1, Left(stats) - pad);
                }
            }
            return new Rect(x0, y0, Mathf.Max(40, x1 - x0), Mathf.Max(40, y1 - y0));
        }

        /// <summary>The 3D area in screen pixels (origin bottom left): the toggle sits in its top right corner, and the
        /// HTML layer is told the area when it changes.</summary>
        void Place()
        {
            var r = Area();
            if (toggle != null && !on)
            {
                var rt = toggle.Button.GetComponent<RectTransform>();
                var cv = canvas.GetComponentInParent<Canvas>();
                float sf = cv != null ? Mathf.Max(0.01f, cv.scaleFactor) : 1f;
                bool tall = Screen.height > Screen.width * 1.05f;
                // about 44 CSS px high on a phone and 48 px on a desktop, whatever the canvas scaling
                float px = Mathf.Max(36f, Mathf.Min(Screen.width, Screen.height) * (tall ? 0.1f : 0.05f));
                float k = px / (44f * sf);
                rt.localScale = new Vector3(k, k, 1);
                float m = Mathf.Max(6, Screen.height * 0.01f);
                rt.anchoredPosition = new Vector2((r.xMax - m) / sf, (r.yMax - m) / sf);
            }
            if (!on || r == last) return;
            last = r;
            PhysioBridge.View2dRect(r.x, r.y, r.width, r.height);
        }

        void OnDisable() { if (on) PhysioBridge.View2dClose(); }
        void OnEnable() { if (on) { last = default; if (muscle) PhysioBridge.Muscle2dOpen(); else PhysioBridge.View2dOpen(handle); } }
    }
}
