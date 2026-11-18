using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedAtlas.Heart
{
    /// <summary>Top tabs: the catalog and the two main simulations (the original web models of the cardiac cycle and breathing).</summary>
    public static class SceneTabs
    {
        static readonly (string id, string label)[] Tabs = { ("menu", "Каталог"), ("cardiac-cycle", "Сърце"), ("breathing", "Бели дробове") };

        public static void Add(Transform canvas, string current)
        {
            if (current == "Sim") current = MedAtlas.Sims.SimApp.SelectedId;
            else if (current == "Menu") current = "menu";
            float x = 360;
            foreach (var t in Tabs)
            {
                string id = t.id;
                var b = UIKit.Button(canvas, t.label, Vector2.zero, new Vector2(t.label.Length * 11 + 40, 38), () =>
                {
                    if (id == current) return;
                    if (id == "menu") SceneManager.LoadScene("Menu");
                    else { MedAtlas.Sims.SimApp.SelectedId = id; SceneManager.LoadScene("Sim"); }
                }, 16);
                var rt = b.Button.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(x, -34);
                b.On = id == current;
                x += t.label.Length * 11 + 48;
            }
        }
    }
}
