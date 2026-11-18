using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Heart
{
    /// <summary>Shared rule for the "Плътност" (opacity 1–100) slider: scales an X-ray glass material
    /// from its own see-through look (low values) up to nearly solid (high values).</summary>
    public static class Opacity
    {
        static readonly Dictionary<Material, (float mn, float mx)> baseAlpha = new Dictionary<Material, (float, float)>();

        public static void Apply(Material m, float k)
        {
            if (m == null || !m.HasProperty("_MaxAlpha")) return;
            if (!baseAlpha.TryGetValue(m, out var b)) { b = (m.GetFloat("_MinAlpha"), m.GetFloat("_MaxAlpha")); baseAlpha[m] = b; }
            k = Mathf.Clamp(k, 0.01f, 1f);
            m.SetFloat("_MinAlpha", Mathf.Lerp(b.mn * k * 2f, 0.92f, k * k));
            m.SetFloat("_MaxAlpha", Mathf.Lerp(b.mx * Mathf.Min(1f, k * 2.5f), 1f, k * k));
        }

        public static Material Glass(Color c, float minA = 0.03f, float maxA = 0.42f)
        {
            var m = new Material(Resources.Load<Material>("Materials/XRay"));
            m.SetColor("_Color", c); m.SetFloat("_MinAlpha", minA); m.SetFloat("_MaxAlpha", maxA); m.renderQueue = 3010;
            return m;
        }
    }
}
