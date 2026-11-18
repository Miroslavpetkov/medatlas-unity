using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Heart
{
    /// <summary>Blood "corpuscle" streams that follow anatomical paths. Their speed is driven by the
    /// simulated flow in every segment and they queue in front of closed valves.</summary>
    public class BloodFlow : MonoBehaviour
    {
        enum Drv { Ven, Tr, Pu, Pa, Pv, Mi, Ao, Sys }
        enum Gate { None, Tricuspid, Pulmonary, Mitral, Aortic }

        class Path
        {
            public List<Vector3> P = new List<Vector3>();
            public List<float> S = new List<float>();
            public List<Drv> D = new List<Drv>();
            public List<float> R = new List<float>();
            public List<(float s, Gate g)> Gates = new List<(float, Gate)>();
            public float Length;
            public bool Left;
            public float SpawnShare;
        }

        struct Corp { public int path; public float s; public Vector3 off; public float jitter; }

        readonly List<Path> paths = new List<Path>();
        readonly List<Corp> corps = new List<Corp>();
        ParticleSystem ps;
        ParticleSystem.Particle[] buf = new ParticleSystem.Particle[2048];
        float spawnAcc;
        public bool Visible = true;
        CardiacModel model;
        HeartModel heart;
        const int MaxCorps = 1600;

        public void Build(HeartModel h, CardiacModel m)
        {
            heart = h; model = m;
            BuildPaths();
            var go = new GameObject("BloodParticles");
            go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 2048;
            main.playOnAwake = false;
            main.startLifetime = 1000f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var tpl = Resources.Load<Material>("Materials/Blood");
            var mat = tpl != null ? new Material(tpl) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", DotTexture());
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f); // additive
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_ADD");
            mat.renderQueue = 3100;
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
        }

        static Texture2D DotTexture()
        {
            int n = 32; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        Vector3 L(string k) => heart.LM(k);

        void BuildPaths()
        {
            Vector3 tr = L("tricuspid"), trA = L("tricuspidAxis"), pv = L("pulmValve"), pvA = L("pulmAxis");
            Vector3 mi = L("mitral"), miA = L("mitralAxis"), av = L("aorticValve"), avA = L("aorticAxis");
            Vector3 RA = L("RA"), RV = L("RV"), LA = L("LA"), LV = L("LV");

            // ---- right heart tail (from RA through to lungs), two branches
            List<(Vector3, Drv, float, Gate)> RightTail(bool leftLung)
            {
                var l = new List<(Vector3, Drv, float, Gate)>
                {
                    (RA, Drv.Tr, 0.10f, Gate.None),
                    (tr - trA * 0.04f, Drv.Tr, 0.05f, Gate.None),
                    (tr, Drv.Tr, 0.05f, Gate.Tricuspid),
                    (tr + trA * 0.12f, Drv.Tr, 0.09f, Gate.None),
                    (Vector3.Lerp(RV, L("apexRV"), 0.35f), Drv.Pu, 0.09f, Gate.None),
                    (Vector3.Lerp(RV, pv, 0.6f), Drv.Pu, 0.06f, Gate.None),
                    (pv - pvA * 0.03f, Drv.Pu, 0.045f, Gate.None),
                    (pv, Drv.Pu, 0.045f, Gate.Pulmonary),
                    (pv + pvA * 0.10f, Drv.Pa, 0.055f, Gate.None),
                    (L("truncus_pulm"), Drv.Pa, 0.06f, Gate.None),
                };
                if (leftLung) { l.Add((L("a_pulm_sin"), Drv.Pa, 0.045f, Gate.None)); l.Add((L("a_pulm_sin_xmax"), Drv.Pa, 0.03f, Gate.None)); }
                else { l.Add((L("a_pulm_dx"), Drv.Pa, 0.045f, Gate.None)); l.Add((L("a_pulm_dx_xmin"), Drv.Pa, 0.03f, Gate.None)); }
                return l;
            }
            List<(Vector3, Drv, float, Gate)> LeftTail(int variant)
            {
                var l = new List<(Vector3, Drv, float, Gate)>
                {
                    (LA, Drv.Mi, 0.10f, Gate.None),
                    (mi - miA * 0.04f, Drv.Mi, 0.06f, Gate.None),
                    (mi, Drv.Mi, 0.06f, Gate.Mitral),
                    (mi + miA * 0.12f, Drv.Mi, 0.09f, Gate.None),
                    (Vector3.Lerp(LV, L("apexLV"), 0.45f), Drv.Ao, 0.08f, Gate.None),
                    (Vector3.Lerp(LV, av, 0.55f), Drv.Ao, 0.06f, Gate.None),
                    (av - avA * 0.03f, Drv.Ao, 0.05f, Gate.None),
                    (av, Drv.Ao, 0.05f, Gate.Aortic),
                    (av + avA * 0.10f, Drv.Sys, 0.06f, Gate.None),
                    (L("aorta_asc"), Drv.Sys, 0.07f, Gate.None),
                    (L("aorta_asc_max"), Drv.Sys, 0.07f, Gate.None),
                };
                if (variant == 1) { l.Add((L("truncus_brachio"), Drv.Sys, 0.03f, Gate.None)); }
                l.Add((L("aorta_arch_max"), Drv.Sys, 0.07f, Gate.None));
                l.Add((L("aorta_desc_max"), Drv.Sys, 0.07f, Gate.None));
                l.Add((L("aorta_desc"), Drv.Sys, 0.07f, Gate.None));
                l.Add((L("aorta_desc_min"), Drv.Sys, 0.06f, Gate.None));
                return l;
            }

            // venae cavae
            var svc = new List<(Vector3, Drv, float, Gate)> { (L("v_cava_sup_max"), Drv.Ven, 0.07f, Gate.None), (L("v_cava_sup"), Drv.Ven, 0.07f, Gate.None) };
            var ivc = new List<(Vector3, Drv, float, Gate)> { (L("v_cava_inf_min"), Drv.Ven, 0.07f, Gate.None), (L("v_cava_inf"), Drv.Ven, 0.07f, Gate.None) };
            AddPath(Concat(svc, RightTail(true)), false, 0.18f);
            AddPath(Concat(svc, RightTail(false)), false, 0.18f);
            AddPath(Concat(ivc, RightTail(true)), false, 0.32f);
            AddPath(Concat(ivc, RightTail(false)), false, 0.32f);

            string[] leftVeins = { "vv_pulm_2", "vv_pulm_5" };
            string[] rightVeins = { "vv_pulm_6", "vv_pulm_7" };
            int k = 0;
            foreach (var v in leftVeins)
            {
                var head = new List<(Vector3, Drv, float, Gate)> { (L(v + "_xmax"), Drv.Pv, 0.035f, Gate.None), (L(v), Drv.Pv, 0.04f, Gate.None) };
                AddPath(Concat(head, LeftTail(0)), true, 0.25f); k++;
            }
            foreach (var v in rightVeins)
            {
                var head = new List<(Vector3, Drv, float, Gate)> { (L(v + "_xmin"), Drv.Pv, 0.035f, Gate.None), (L(v), Drv.Pv, 0.04f, Gate.None) };
                AddPath(Concat(head, LeftTail(0)), true, 0.25f);
            }
        }

        static List<T> Concat<T>(List<T> a, List<T> b) { var r = new List<T>(a); r.AddRange(b); return r; }

        void AddPath(List<(Vector3 p, Drv d, float r, Gate g)> ctrl, bool left, float share)
        {
            // drop missing landmarks (zero vectors)
            ctrl.RemoveAll(c => c.p == Vector3.zero);
            var path = new Path { Left = left, SpawnShare = share };
            const int sub = 10;
            for (int i = 0; i < ctrl.Count - 1; i++)
            {
                Vector3 p0 = ctrl[Mathf.Max(i - 1, 0)].p, p1 = ctrl[i].p, p2 = ctrl[i + 1].p, p3 = ctrl[Mathf.Min(i + 2, ctrl.Count - 1)].p;
                for (int j = 0; j < sub; j++)
                {
                    float t = j / (float)sub;
                    path.P.Add(CatmullRom(p0, p1, p2, p3, t));
                    path.D.Add(ctrl[i].d);
                    path.R.Add(Mathf.Lerp(ctrl[i].r, ctrl[i + 1].r, t));
                }
            }
            path.P.Add(ctrl[ctrl.Count - 1].p); path.D.Add(ctrl[ctrl.Count - 1].d); path.R.Add(ctrl[ctrl.Count - 1].r);
            float s = 0; path.S.Add(0);
            for (int i = 1; i < path.P.Count; i++) { s += Vector3.Distance(path.P[i - 1], path.P[i]); path.S.Add(s); }
            path.Length = s;
            for (int i = 0; i < ctrl.Count; i++)
                if (ctrl[i].g != Gate.None) path.Gates.Add((path.S[i * sub], ctrl[i].g));
            paths.Add(path);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        float Flow(Drv d)
        {
            switch (d)
            {
                case Drv.Ven: return model.Qven;
                case Drv.Tr: return model.Tricuspid.Q;
                case Drv.Pu: return model.Pulmonary.Q;
                case Drv.Pa: return Mathf.Max(model.Pulmonary.Q, model.Qpul);
                case Drv.Pv: return model.Qpv;
                case Drv.Mi: return model.Mitral.Q;
                case Drv.Ao: return model.Aortic.Q;
                case Drv.Sys: return Mathf.Max(model.Aortic.Q, model.Qsys);
            }
            return 0;
        }

        bool GateOpen(Gate g)
        {
            switch (g)
            {
                case Gate.Tricuspid: return model.Tricuspid.Leaflet > 0.35f;
                case Gate.Pulmonary: return model.Pulmonary.Leaflet > 0.35f;
                case Gate.Mitral: return model.Mitral.Leaflet > 0.35f;
                case Gate.Aortic: return model.Aortic.Leaflet > 0.35f;
            }
            return true;
        }

        int Seg(Path p, float s)
        {
            int lo = 0, hi = p.S.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (p.S[mid] <= s) lo = mid; else hi = mid; }
            return lo;
        }

        /// <param name="physDt">physiological seconds advanced this frame</param>
        public void Tick(float physDt)
        {
            if (ps == null) return;
            if (!Visible) { ps.Clear(); return; }
            // spawn proportional to venous inflow (right) and pulmonary venous inflow (left)
            float inflow = Mathf.Max(model.Qven, 0f) + Mathf.Max(model.Qpv, 0f);
            spawnAcc += inflow * physDt * 1.6f;
            while (spawnAcc > 1f && corps.Count < MaxCorps)
            {
                spawnAcc -= 1f;
                bool right = Random.value * inflow < Mathf.Max(model.Qven, 0f);
                // pick path weighted by share among this side
                float r = Random.value, acc = 0;
                int chosen = -1;
                float tot = 0; foreach (var p in paths) if (p.Left != right) tot += p.SpawnShare;
                for (int i = 0; i < paths.Count; i++)
                {
                    if (paths[i].Left == right) continue;
                    acc += paths[i].SpawnShare / tot;
                    if (r <= acc) { chosen = i; break; }
                }
                if (chosen < 0) continue;
                corps.Add(new Corp { path = chosen, s = 0, off = Random.insideUnitSphere, jitter = Random.Range(0.85f, 1.15f) });
            }
            if (corps.Count >= MaxCorps) spawnAcc = 0;

            for (int i = corps.Count - 1; i >= 0; i--)
            {
                var c = corps[i]; var p = paths[c.path];
                int seg = Seg(p, c.s);
                float q = Flow(p.D[seg]);
                float v = 0.032f * Mathf.Sqrt(Mathf.Max(q, 0f)) * c.jitter; // units / physiological second
                float ns = c.s + v * physDt;
                foreach (var g in p.Gates)
                {
                    if (c.s <= g.s && ns > g.s && !GateOpen(g.g)) { ns = Mathf.Min(ns, g.s - 0.004f - c.jitter * 0.01f); ns = Mathf.Max(ns, c.s); }
                }
                c.s = ns;
                if (c.s >= p.Length) { corps.RemoveAt(i); continue; }
                corps[i] = c;
            }

            if (buf.Length < corps.Count) buf = new ParticleSystem.Particle[corps.Count];
            Color red = new Color(1f, 0.22f, 0.2f, 0.9f), blue = new Color(0.35f, 0.5f, 1f, 0.9f);
            for (int i = 0; i < corps.Count; i++)
            {
                var c = corps[i]; var p = paths[c.path];
                int seg = Seg(p, c.s);
                int nx = Mathf.Min(seg + 1, p.P.Count - 1);
                float segLen = Mathf.Max(p.S[nx] - p.S[seg], 1e-5f);
                float t = Mathf.Clamp01((c.s - p.S[seg]) / segLen);
                Vector3 pos = Vector3.Lerp(p.P[seg], p.P[nx], t);
                Vector3 tan = (p.P[nx] - p.P[seg]).normalized;
                Vector3 off = c.off - tan * Vector3.Dot(c.off, tan);
                float rad = Mathf.Lerp(p.R[seg], p.R[nx], t);
                pos += off * rad;
                // fade in at start and out at the end of the path
                float fade = Mathf.Clamp01(c.s / 0.06f) * Mathf.Clamp01((p.Length - c.s) / 0.08f);
                var col = p.Left ? red : blue; col.a *= fade;
                buf[i].position = pos;
                buf[i].startSize = 0.034f;
                buf[i].startColor = col;
                buf[i].remainingLifetime = 100f;
                buf[i].startLifetime = 1000f;
            }
            ps.SetParticles(buf, corps.Count);
        }
    }
}
