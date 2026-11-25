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
    /// </summary>
    public class View2D : MonoBehaviour
    {
        /// <summary>The student's choice stays for the next simulation, as on the web.</summary>
        public static bool Want;

        int handle = -1;
        bool on;
        Rect last;
        System.Func<bool> playing;
        RectTransform title, card, stats, bar;
        RectTransform[] leftPanels;
        ResponsiveLayout layout;
        PhoneSheet sheet;
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
            v.title = canvas.Find("Title") as RectTransform;
            v.card = canvas.Find("PhaseCard") as RectTransform;
            v.stats = canvas.Find("Stats") as RectTransform;
            v.bar = canvas.Find("Controls") as RectTransform;
            v.sheet = sheet;
            v.Has = PhysioBridge.View2dHas(simId);
            return v;
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
                PhysioBridge.View2dOpen(handle);
                Place();
            }
            else PhysioBridge.View2dClose();
            Changed?.Invoke(on);
        }

        public void Toggle() => Set(!on);

        void LateUpdate()
        {
            if (!on) return;
            Place();
            PhysioBridge.View2dFrame(handle, Mathf.Min(Time.deltaTime, 0.05f), playing == null || playing());
        }

        bool Visible(RectTransform rt) => rt != null && rt.gameObject.activeInHierarchy;
        float Bottom(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Min(c[0].y, c[3].y); }
        float Top(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Max(c[1].y, c[2].y); }
        float Left(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Min(c[0].x, c[1].x); }
        float Right(RectTransform rt) { rt.GetWorldCorners(c); return Mathf.Max(c[2].x, c[3].x); }

        /// <summary>The 3D area in screen pixels (origin bottom left), sent to the HTML layer when it changes.</summary>
        void Place()
        {
            float W = Screen.width, H = Screen.height, pad = Mathf.Max(6, H * 0.008f);
            float x0 = pad, x1 = W - pad, y0 = pad, y1 = H - pad;
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
            var r = new Rect(x0, y0, Mathf.Max(40, x1 - x0), Mathf.Max(40, y1 - y0));
            if (r == last) return;
            last = r;
            PhysioBridge.View2dRect(r.x, r.y, r.width, r.height);
        }

        void OnDisable() { if (on) PhysioBridge.View2dClose(); }
        void OnEnable() { if (on) { last = default; PhysioBridge.View2dOpen(handle); } }
    }
}
