using UnityEngine;
using UnityEngine.UI;

namespace MedAtlas.Heart
{
    /// <summary>On a phone held upright, asks the user to turn it sideways (the layout is landscape).</summary>
    public class PortraitHint : MonoBehaviour
    {
        GameObject overlay;

        public static void Add(Transform canvas)
        {
            var h = canvas.gameObject.AddComponent<PortraitHint>();
            var rt = UIKit.Rect("PortraitHint", canvas, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0.03f, 0.03f, 0.04f, 0.92f);
            var t = UIKit.Label(rt, "Завърти телефона хоризонтално ↻\n\nTurn your phone sideways", 44, UIKit.TextMain, UIKit.SemiBold, TextAnchor.MiddleCenter);
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(60, 0); trt.offsetMax = new Vector2(-60, 0);
            h.overlay = rt.gameObject;
            rt.SetAsLastSibling();
        }

        void Update()
        {
            if (overlay != null) overlay.SetActive(Screen.height > Screen.width * 1.1f);
        }
    }
}
