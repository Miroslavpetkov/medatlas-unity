using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedAtlas.Heart
{
    /// <summary>Top tabs to switch between the simulations (scenes "Heart" and "Lungs").</summary>
    public static class SceneTabs
    {
        static readonly (string scene, string label)[] Tabs = { ("Menu", "Каталог"), ("Heart", "Сърце"), ("Lungs", "Бели дробове") };

        public static void Add(Transform canvas, string current)
        {
            float x = 360;
            foreach (var t in Tabs)
            {
                string scene = t.scene;
                var b = UIKit.Button(canvas, t.label, Vector2.zero, new Vector2(t.label.Length * 11 + 40, 38), () =>
                {
                    if (scene != current) SceneManager.LoadScene(scene);
                }, 16);
                var rt = b.Button.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(x, -34);
                b.On = scene == current;
                x += t.label.Length * 11 + 48;
            }
        }
    }
}
