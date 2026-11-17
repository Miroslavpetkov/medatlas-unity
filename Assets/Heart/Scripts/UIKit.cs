using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MedAtlas.Heart
{
    /// <summary>Small helpers to build a clean uGUI interface from code.</summary>
    public static class UIKit
    {
        public static Font Regular, SemiBold, Italic;
        static Sprite rounded, circle;
        public static readonly Color Panel = new Color(0.075f, 0.08f, 0.095f, 0.78f);
        public static readonly Color Accent = new Color(1f, 0.42f, 0.40f, 1f);
        public static readonly Color TextMain = new Color(0.95f, 0.95f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.68f, 0.70f, 0.74f, 1f);
        public static readonly Color ButtonOff = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color ButtonOn = new Color(1f, 0.42f, 0.40f, 0.85f);

        public static void Init()
        {
            Regular = Resources.Load<Font>("Fonts/Inter-Regular");
            SemiBold = Resources.Load<Font>("Fonts/Inter-SemiBold");
            Italic = Resources.Load<Font>("Fonts/Inter-Italic");
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (Regular == null) Regular = fallback;
            if (SemiBold == null) SemiBold = Regular;
            if (Italic == null) Italic = Regular;
            rounded = MakeRounded(64, 18);
            circle = MakeRounded(32, 16);
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(); t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static Image PanelImage(RectTransform rt, Color c, bool roundedCorners = true)
        {
            var img = rt.gameObject.AddComponent<Image>();
            if (roundedCorners) { img.sprite = rounded; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.6f; }
            img.color = c;
            return img;
        }

        public static Image Dot(Transform parent, Vector2 pos, float size, Color c)
        {
            var rt = Rect("dot", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = circle; img.color = c; img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, Font font = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font ?? Regular; t.fontSize = size; t.color = color; t.text = text; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.lineSpacing = 1.1f;
            return t;
        }

        public static Text LabelAt(Transform parent, string text, int size, Color color, Vector2 pos, Vector2 sz, Font font = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = Label(parent, text, size, color, font, align);
            var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = sz;
            return t;
        }

        public class Btn
        {
            public Button Button; public Image Bg; public Text Text; bool on;
            public bool On { get => on; set { on = value; Bg.color = on ? ButtonOn : ButtonOff; Text.color = on ? Color.white : TextMain; } }
        }

        public static Btn Button(Transform parent, string label, Vector2 pos, Vector2 size, UnityAction onClick, int fontSize = 17)
        {
            var rt = Rect("btn_" + label, parent, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), pos, size);
            var img = PanelImage(rt, ButtonOff);
            var b = rt.gameObject.AddComponent<Button>();
            var cb = b.colors; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); b.colors = cb;
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Label(rt, label, fontSize, TextMain, SemiBold, TextAnchor.MiddleCenter);
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            return new Btn { Button = b, Bg = img, Text = t };
        }

        public static Slider Slider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityAction<float> onChange, bool whole = true)
        {
            var rt = Rect("slider", parent, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), pos, size);
            var bg = Rect("bg", rt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 6));
            PanelImage(bg, new Color(1, 1, 1, 0.15f));
            var fillArea = Rect("fillArea", rt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 6));
            var fill = Rect("fill", fillArea, Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            PanelImage(fill, Accent);
            var handleArea = Rect("handleArea", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20, 0));
            var handle = Rect("handle", handleArea, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            var himg = handle.gameObject.AddComponent<Image>(); himg.sprite = circle; himg.color = Color.white;
            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = himg; s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.wholeNumbers = whole; s.minValue = min; s.maxValue = max; s.value = value;
            s.onValueChanged.AddListener(onChange);
            return s;
        }
    }
}
