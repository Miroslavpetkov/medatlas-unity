using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>"Пътят през тялото": the body pack (mouth → anus, kidneys → urethra) shown as glass, with
    /// food portions, gas bubbles and urine drops moving along centre-lines computed from the organs.
    /// Driven by the channels of the journey models (q/s/k, b/bs, G_*, ur, vo, fx).</summary>
    public class JourneyView : MonoBehaviour
    {
        OrganView organ;
        readonly List<List<Vector3>> food = new List<List<Vector3>>();
        readonly List<List<Vector3>>[] urine = { new List<List<Vector3>>(), new List<List<Vector3>>() };
        class GlassPart { public string Name, Cls; public Material M; public Color C; public float A; }
        readonly List<GlassPart> glass = new List<GlassPart>();
        readonly List<(Transform t, Material m)> bolus = new List<(Transform, Material)>();
        readonly List<Transform> bubbles = new List<Transform>();
        readonly List<Transform> drops = new List<Transform>();
        readonly List<(int path, float s, bool voidDrop)> dropState = new List<(int, float, bool)>();
        readonly List<(Transform t, Vector3 v, float life)> puffs = new List<(Transform, Vector3, float)>();
        bool ready; float lastFx;
        /// <summary>0.01–1 from the "Плътност" slider; 0.35 keeps the default glass look.</summary>
        public float Opacity = 0.35f;

        static readonly Color[] KindColor =
        {
            new Color(0.35f, 0.65f, 1f), new Color(0.95f, 0.60f, 0.25f), new Color(0.95f, 0.88f, 0.70f), new Color(0.86f, 0.56f, 0.36f),
            new Color(1f, 0.84f, 0.30f), new Color(0.42f, 0.78f, 0.30f), new Color(0.45f, 0.28f, 0.12f), new Color(0.78f, 0.82f, 0.22f),
        };
        static readonly Color Faeces = new Color(0.45f, 0.28f, 0.12f);

        public void Init(OrganView o, string pathsJson)
        {
            organ = o;
            var root = MiniJson.O(MiniJson.Parse(pathsJson));
            if (root == null) return;
            Read(root, "food", food); Read(root, "urineR", urine[0]); Read(root, "urineL", urine[1]);
            // every organ becomes glass so what moves inside stays visible
            var xray = Resources.Load<Material>("Materials/XRay");
            foreach (Transform ch in o.transform)
            {
                var r = ch.GetComponent<Renderer>(); if (r == null) continue;
                var part = o.Parts.Find(p => p.Name == ch.name); if (part == null) continue;
                var m = new Material(xray);
                var c = part.Base; float a = part.Cls == "liver" || part.Cls == "kidney" || part.Cls == "pancreas" ? 0.30f : 0.42f;
                m.SetColor("_Color", Color.Lerp(c, Color.white, 0.25f)); m.SetFloat("_MinAlpha", 0.03f); m.SetFloat("_MaxAlpha", a);
                m.renderQueue = 3010;
                r.sharedMaterial = m;
                glass.Add(new GlassPart { Name = ch.name, Cls = part.Cls, M = m, C = Color.Lerp(c, Color.white, 0.25f), A = a });
            }
            var lit = Resources.Load<Material>("Materials/Highlight");
            for (int i = 0; i < 24; i++) { var bt = Ball("Bolus" + i, lit, Color.white, out var bm); bolus.Add((bt, bm)); }
            for (int i = 0; i < 16; i++) bubbles.Add(Ball("Gas" + i, lit, new Color(0.65f, 0.9f, 1f), out _));
            for (int i = 0; i < 90; i++) drops.Add(Ball("Urine" + i, lit, new Color(1f, 0.88f, 0.3f), out _));
            for (int i = 0; i < 14; i++) { var p = Ball("Puff" + i, lit, new Color(0.7f, 0.95f, 0.8f), out _); puffs.Add((p, Vector3.zero, 0)); }
            ready = food.Count > 0;
        }

        Transform Ball(string name, Material tpl, Color c, out Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(organ.transform, false);
            m = new Material(tpl); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.7f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.6f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.SetActive(false);
            return go.transform;
        }

        static void Read(Dictionary<string, object> root, string key, List<List<Vector3>> into)
        {
            foreach (var so in MiniJson.L(root.ContainsKey(key) ? root[key] : null))
            {
                var seg = MiniJson.O(so); var pts = new List<Vector3>();
                foreach (var po in MiniJson.L(seg["pts"])) { var a = MiniJson.L(po); pts.Add(new Vector3((float)MiniJson.Num(a[0]), (float)MiniJson.Num(a[1]), (float)MiniJson.Num(a[2]))); }
                if (pts.Count == 1) pts.Add(pts[0]);
                into.Add(pts);
            }
        }

        /// <summary>Point on a path given "segment units" (integer part = segment, fraction = along it).</summary>
        static Vector3 At(List<List<Vector3>> path, float p)
        {
            int n = path.Count;
            if (p < 0)
            {
                var s0 = path[0]; var dir = (s0[0] - s0[Mathf.Min(3, s0.Count - 1)]).normalized;
                return s0[0] + (dir + Vector3.back * 0.6f).normalized * (-p) * 0.35f;
            }
            if (p >= n)
            {
                var sl = path[n - 1]; var dir = (sl[sl.Count - 1] - sl[Mathf.Max(0, sl.Count - 4)]).normalized;
                return sl[sl.Count - 1] + dir * (p - n) * 0.35f;
            }
            int seg = Mathf.Clamp((int)p, 0, n - 1); float f = p - seg;
            var pts = path[seg]; float x = f * (pts.Count - 1); int i = Mathf.Min((int)x, pts.Count - 2);
            return Vector3.Lerp(pts[i], pts[i + 1], x - i);
        }

        static float Ch(Dictionary<string, object> now, string k, float def = -1) => now != null && now.TryGetValue(k, out var v) && v is double d ? (float)d : def;

        public void Tick(Dictionary<string, object> now, float dt)
        {
            if (!ready || now == null) return;
            // food portions
            for (int i = 0; i < bolus.Count; i++)
            {
                float p = Ch(now, "q" + i);
                var (t, m) = bolus[i];
                if (p < -0.95f || p > 11.6f) { t.gameObject.SetActive(false); continue; }
                t.gameObject.SetActive(true);
                t.localPosition = At(food, p);
                float s = Mathf.Max(0.15f, Ch(now, "s" + i, 0.5f));
                float r = (p >= 4 && p < 5 ? 0.085f : 0.05f) + 0.05f * s;
                t.localScale = Vector3.one * r;
                int k = Mathf.Clamp((int)Ch(now, "k" + i, 3), 0, KindColor.Length - 1);
                var c = KindColor[k];
                if (k != 0 && k != 6 && k != 7 && p > 7) c = Color.Lerp(c, Faeces, Mathf.Clamp01((p - 7f) / 2.5f));
                m.SetColor("_BaseColor", c); m.SetColor("_EmissionColor", c * 0.8f);
            }
            // gas bubbles
            for (int i = 0; i < bubbles.Count; i++)
            {
                float p = Ch(now, "b" + i); var t = bubbles[i];
                if (p < 0 || p > 11.6f) { t.gameObject.SetActive(false); continue; }
                t.gameObject.SetActive(true);
                float wob = Mathf.Sin(Time.time * 3f + i) * 0.012f;
                t.localPosition = At(food, p) + new Vector3(wob, wob * 0.7f, 0);
                t.localScale = Vector3.one * (0.035f + 0.045f * Mathf.Clamp(Ch(now, "bs" + i, 0.5f), 0.2f, 2f));
            }
            // gas leaving the body
            float fx = Ch(now, "fx", 0);
            if (fx > 0.8f && lastFx <= 0.8f)
            {
                var o = At(food, 11.02f); var dir = (At(food, 11.2f) - o).normalized;
                for (int i = 0; i < puffs.Count; i++) { var pf = puffs[i]; pf.t.localPosition = o; pf.t.gameObject.SetActive(true); puffs[i] = (pf.t, (dir + Random.insideUnitSphere * 0.7f) * Random.Range(0.25f, 0.6f), 1.6f); }
            }
            lastFx = fx;
            for (int i = 0; i < puffs.Count; i++)
            {
                var pf = puffs[i]; if (pf.life <= 0) continue;
                pf.life -= dt; pf.t.localPosition += pf.v * dt; pf.t.localScale = Vector3.one * (0.03f + 0.05f * (1.6f - pf.life));
                if (pf.life <= 0) pf.t.gameObject.SetActive(false);
                puffs[i] = pf;
            }
            // urine: drops from the renal pelvis down the ureters into the bladder; voiding through the urethra
            float ur = Ch(now, "ur", 0), vo = Ch(now, "vo", 0);
            if (urine[0].Count >= 5)
            {
                if (ur > 0 && Random.value < ur * dt * 30f && dropState.Count < drops.Count) dropState.Add((Random.value < 0.5f ? 0 : 1, 1f, false));
                if (vo > 0.5f && Random.value < dt * 40f && dropState.Count < drops.Count) dropState.Add((0, 4f, true));
                for (int i = dropState.Count - 1; i >= 0; i--)
                {
                    var d = dropState[i]; d.s += dt * (d.voidDrop ? 1.6f : (d.s < 2 ? 0.8f : 0.35f + ur * 0.5f));
                    if ((!d.voidDrop && d.s > 3.3f) || d.s > 5.4f) { dropState.RemoveAt(i); continue; }
                    dropState[i] = d;
                }
                for (int i = 0; i < drops.Count; i++)
                {
                    var t = drops[i];
                    if (i >= dropState.Count) { t.gameObject.SetActive(false); continue; }
                    var d = dropState[i]; t.gameObject.SetActive(true);
                    t.localPosition = At(urine[d.path], d.s); t.localScale = Vector3.one * 0.032f;
                }
            }
            // glowing organs
            float ok = Mathf.Clamp(Opacity, 0.01f, 1f);
            float baseMin = Mathf.Lerp(0.03f * ok * 2f, 0.92f, ok * ok);
            foreach (var g in glass) { g.M.SetColor("_Color", g.C); g.M.SetFloat("_MinAlpha", baseMin); g.M.SetFloat("_MaxAlpha", Mathf.Lerp(g.A * Mathf.Min(1f, ok * 2.5f), 1f, ok * ok)); }
            foreach (var kv in now)
            {
                if (!kv.Key.StartsWith("G_") || !(kv.Value is double v)) continue;
                string key = kv.Key.Substring(2); float a = Mathf.Clamp01((float)v);
                if (a <= 0.01f) continue;
                foreach (var g in glass)
                {
                    if (g.Name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0 && g.Cls != key) continue;
                    g.M.SetColor("_Color", Color.Lerp(g.C, new Color(1f, 0.78f, 0.4f), a * 0.6f) * (1f + 0.25f * a));
                    g.M.SetFloat("_MaxAlpha", Mathf.Max(g.M.GetFloat("_MaxAlpha"), Mathf.Lerp(g.A, 0.62f, a)));
                }
            }
        }

        /// <summary>Where the camera should look for a given journey (pack-local point) and how far.</summary>
        public static (float yFrac, float dist) Framing(string id)
        {
            switch (id)
            {
                case "swallow": return (0.86f, 3.4f);
                case "vomit": return (0.62f, 4.2f);
                case "defecation": return (0.12f, 2.6f);
                default: return (0.42f, 5.2f);
            }
        }
    }
}
