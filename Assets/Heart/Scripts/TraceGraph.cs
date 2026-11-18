using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedAtlas.Heart
{
    /// <summary>Lightweight line chart drawn as a UI mesh (several series, grid, phase bands, sweep cursor).</summary>
    public class TraceGraph : MaskableGraphic
    {
        public class Series { public float[] Data; public float[] X; public int Count = -1; public Color Color; public float Width = 2f; public bool Dashed; }
        public float XMin = 0, XMax = 1;
        float XP(Rect plot, float v) => plot.xMin + plot.width * Mathf.Clamp01((v - XMin) / (XMax - XMin));
        public readonly List<Series> Lines = new List<Series>();
        public float Min = 0, Max = 1;
        public float[] GridValues;
        public byte[] Phases; public Color[] PhaseColors; public float BandHeight = 0f;
        public byte[] Marks;
        public float Cursor = -1;
        public float MarkerX = -1, MarkerY = -1;   // normalized 0..1 position of a highlighted point
        public Color MarkerColor = Color.white;
        public Color GridColor = new Color(1, 1, 1, 0.07f);
        public Color CursorColor = new Color(1, 1, 1, 0.55f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float bandH = BandHeight;
            Rect plot = new Rect(r.xMin, r.yMin, r.width, r.height - bandH);

            if (GridValues != null)
                foreach (var g in GridValues)
                {
                    float y = Y(plot, g);
                    Quad(vh, new Vector2(plot.xMin, y - 0.5f), new Vector2(plot.xMax, y + 0.5f), GridColor);
                }

            if (Phases != null && PhaseColors != null && bandH > 0)
            {
                int n = Phases.Length;
                int start = 0;
                for (int i = 1; i <= n; i++)
                {
                    if (i == n || Phases[i] != Phases[start])
                    {
                        float x0 = plot.xMin + plot.width * start / n, x1 = plot.xMin + plot.width * i / n;
                        var c = PhaseColors[Mathf.Min(Phases[start], PhaseColors.Length - 1)];
                        Quad(vh, new Vector2(x0, plot.yMax + 2), new Vector2(x1, r.yMax), c);
                        var cc = c; cc.a *= 0.18f;
                        Quad(vh, new Vector2(x0, plot.yMin), new Vector2(x1, plot.yMax), cc);
                        start = i;
                    }
                }
            }

            if (Marks != null)
            {
                for (int i = 0; i < Marks.Length; i++)
                {
                    if (Marks[i] == 0) continue;
                    float x = plot.xMin + plot.width * i / Marks.Length;
                    var c = Marks[i] == 1 ? new Color(1f, 0.85f, 0.4f, 0.6f) : new Color(0.6f, 0.9f, 1f, 0.6f);
                    Quad(vh, new Vector2(x - 0.75f, plot.yMin), new Vector2(x + 0.75f, plot.yMax), c);
                }
            }

            foreach (var s in Lines)
            {
                if (s.Data == null || s.Data.Length < 2) continue;
                int n = s.Count >= 0 ? s.Count : s.Data.Length;
                if (s.X != null)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        if (float.IsNaN(s.Data[i]) || float.IsNaN(s.Data[i + 1])) continue;
                        Line(vh, new Vector2(XP(plot, s.X[i]), Y(plot, s.Data[i])), new Vector2(XP(plot, s.X[i + 1]), Y(plot, s.Data[i + 1])), s.Width, s.Color);
                    }
                    continue;
                }
                for (int i = 0; i < n - 1; i++)
                {
                    if (s.Dashed && (i / 6) % 2 == 1) continue;
                    // do not connect across the sweep cursor gap
                    if (Cursor >= 0)
                    {
                        float ci = Cursor * n;
                        if (i < ci && i + 1 >= ci) continue;
                        if (i > ci && i < ci + 6) continue;
                    }
                    if (float.IsNaN(s.Data[i]) || float.IsNaN(s.Data[i + 1])) continue;
                    Vector2 a = new Vector2(plot.xMin + plot.width * i / (n - 1), Y(plot, s.Data[i]));
                    Vector2 b = new Vector2(plot.xMin + plot.width * (i + 1) / (n - 1), Y(plot, s.Data[i + 1]));
                    Line(vh, a, b, s.Width, s.Color);
                }
            }

            if (MarkerX >= 0)
            {
                float mx = plot.xMin + plot.width * Mathf.Clamp01(MarkerX), my = plot.yMin + plot.height * Mathf.Clamp01(MarkerY);
                Quad(vh, new Vector2(mx - 5, my - 5), new Vector2(mx + 5, my + 5), MarkerColor);
                Quad(vh, new Vector2(mx - 0.5f, plot.yMin), new Vector2(mx + 0.5f, my), new Color(1, 1, 1, 0.25f));
                Quad(vh, new Vector2(plot.xMin, my - 0.5f), new Vector2(mx, my + 0.5f), new Color(1, 1, 1, 0.25f));
            }

            if (Cursor >= 0)
            {
                float x = plot.xMin + plot.width * Cursor;
                Quad(vh, new Vector2(x - 0.75f, plot.yMin), new Vector2(x + 0.75f, r.yMax), CursorColor);
            }
        }

        float Y(Rect plot, float v) => plot.yMin + plot.height * Mathf.Clamp01((v - Min) / (Max - Min));

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, a.y), c, Vector2.zero);
            vh.AddVert(new Vector3(a.x, b.y), c, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), c, Vector2.zero);
            vh.AddVert(new Vector3(b.x, a.y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a; if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public void Refresh() => SetVerticesDirty();
    }
}
