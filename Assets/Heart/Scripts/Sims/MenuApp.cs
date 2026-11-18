using System.Collections.Generic;
using MedAtlas.Heart;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedAtlas.Sims
{
    /// <summary>Catalog of all simulations (scene "Menu"), grouped by organ system.</summary>
    public class MenuApp : MonoBehaviour
    {
        static readonly (string area, string bg)[] Areas =
        {
            ("Heart", "Сърце"), ("Circulation", "Кръвообращение"), ("Lungs", "Дишане"), ("Blood", "Кръв"),
            ("Kidney", "Бъбреци, вода и соли"), ("Nerve", "Нервна система и сетива"), ("Muscle", "Мускули"),
            ("Metabolism", "Метаболизъм и хормони"), ("Body", "Цялото тяло"),
        };

        CanvasScaler scaler; RectTransform content, viewport;
        readonly List<(RectTransform rt, bool header, float h)> items = new List<(RectTransform, bool, float)>();
        bool? portrait;

        void Start()
        {
            Application.targetFrameRate = 60;
            var cla = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(cla, "-organ") >= 0) { SceneManager.LoadScene("Sim"); return; }
            if (System.Array.IndexOf(cla, "-lungs") >= 0) { SceneManager.LoadScene("Lungs"); return; }
            var camGo = new GameObject("Main Camera"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.028f, 0.03f, 0.038f);
            UIKit.Init();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>(); }
            var cgo = new GameObject("UI"); var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.6f;
            cgo.AddComponent<GraphicRaycaster>();
            var bg = UIKit.Rect("bg", cgo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bg.gameObject.AddComponent<RawImage>(); raw.texture = HeartApp.RadialTexture(); raw.raycastTarget = false;
            var C = cgo.transform;
            UIKit.LabelAt(C, "Физиология", 38, UIKit.TextMain, new Vector2(44, -30), new Vector2(320, 50), UIKit.SemiBold);
            SceneTabs.Add(C, "Menu");
            UIKit.LabelAt(C, "Живи модели на физиологията за студенти по медицина · избери тема", 17, UIKit.TextDim, new Vector2(46, -80), new Vector2(1000, 24), UIKit.Italic);

            // scroll area
            viewport = UIKit.Rect("Viewport", C, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(30, 20); viewport.offsetMax = new Vector2(-30, -120);
            var vimg = viewport.gameObject.AddComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UIKit.Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 100));
            var sr = viewport.gameObject.AddComponent<ScrollRect>(); sr.content = content; sr.viewport = viewport; sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;

            var list = new List<Dictionary<string, object>>();
            foreach (var o in MiniJson.L(MiniJson.Parse(PhysioBridge.List()))) list.Add(MiniJson.O(o));
            if (list.Count == 0)
            {
                list.Add(new Dictionary<string, object> { { "id", "cardiac-cycle" }, { "title", "Сърдечен цикъл" }, { "area", "Heart" } });
                list.Add(new Dictionary<string, object> { { "id", "breathing" }, { "title", "Дишане и газообмен" }, { "area", "Lungs" } });
            }
            foreach (var a in Areas)
            {
                var inArea = list.FindAll(m => MiniJson.S(m, "area") == a.area);
                if (inArea.Count == 0) continue;
                var h = UIKit.Label(content, a.bg.ToUpperInvariant(), 15, UIKit.Accent, UIKit.SemiBold, TextAnchor.LowerLeft);
                items.Add((h.rectTransform, true, 40));
                foreach (var m in inArea)
                {
                    string id = MiniJson.S(m, "id"); string title = MiniJson.S(m, "title");
                    bool rich = id == "cardiac-cycle" || id == "breathing";
                    var b = UIKit.Button(content, title + (rich || a.area == "Heart" || a.area == "Circulation" || a.area == "Lungs" || SimApp.PackFor(a.area, id) != null ? "   · 3D" : ""), Vector2.zero, new Vector2(560, 52), () => Open(id), 16);
                    b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.offsetMin = new Vector2(16, 0); b.Text.rectTransform.offsetMax = new Vector2(-10, 0);
                    b.Text.horizontalOverflow = HorizontalWrapMode.Wrap; b.Text.resizeTextForBestFit = true; b.Text.resizeTextMinSize = 11; b.Text.resizeTextMaxSize = 16;
                    items.Add((b.Button.GetComponent<RectTransform>(), false, 56));
                }
            }
        }

        static void Open(string id)
        {
            SimApp.SelectedId = id; SceneManager.LoadScene("Sim");
        }

        void Update()
        {
            bool p = Screen.height > Screen.width * 1.05f;
            if (portrait == p) return;
            portrait = p;
            scaler.referenceResolution = p ? new Vector2(1080, 1920) : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = p ? 0f : 0.6f;
            Canvas.ForceUpdateCanvases();
            float W = viewport.rect.width;
            int cols = p ? 1 : 3;
            float gap = 10, colW = (W - gap * (cols - 1)) / cols;
            float y = 0; int col = 0;
            foreach (var it in items)
            {
                var rt = it.rt;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                if (it.header)
                {
                    if (col != 0) { y -= 56; col = 0; }
                    rt.anchoredPosition = new Vector2(4, y - 8); rt.sizeDelta = new Vector2(W, 30); y -= it.h;
                    continue;
                }
                rt.anchoredPosition = new Vector2(col * (colW + gap), y); rt.sizeDelta = new Vector2(colW, p ? 64 : 50);
                col++;
                if (col >= cols) { col = 0; y -= p ? 72 : 58; }
            }
            if (col != 0) y -= p ? 72 : 58;
            content.sizeDelta = new Vector2(0, -y + 20);
        }
    }
}
