using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Lungs
{
    /// <summary>Air "parcels" that move down the airways during inspiration and back during expiration
    /// (speed from the simulated airflow). Fresh air is cyan; air that has stayed in the alveoli turns lilac (CO2).</summary>
    public class AirFlow : MonoBehaviour
    {
        class Path { public List<Vector3> P = new List<Vector3>(); public List<float> S = new List<float>(); public List<float> R = new List<float>(); public float Length; }
        struct Parcel { public int path; public float s; public Vector3 off; public float alv; public float speed; }

        readonly List<Path> paths = new List<Path>();
        readonly List<Parcel> parcels = new List<Parcel>();
        ParticleSystem ps; ParticleSystem.Particle[] buf = new ParticleSystem.Particle[1200];
        public bool Visible = true;

        public void Build(LungAnatomy a)
        {
            Vector3 top = a.LM("tracheaTop"), car = a.LM("carina");
            void Add(string main, string lobar, string lobe, float share)
            {
                var ctrl = new List<(Vector3 p, float r)> { (top, 0.05f), (car, 0.05f), (a.LM(main), 0.05f), (a.LM(lobar), 0.06f), (a.LM(lobe), 0.45f) };
                var path = new Path();
                const int sub = 12;
                for (int i = 0; i < ctrl.Count - 1; i++)
                {
                    Vector3 p0 = ctrl[Mathf.Max(i - 1, 0)].p, p1 = ctrl[i].p, p2 = ctrl[i + 1].p, p3 = ctrl[Mathf.Min(i + 2, ctrl.Count - 1)].p;
                    for (int j = 0; j < sub; j++)
                    {
                        float t = j / (float)sub, t2 = t * t, t3 = t2 * t;
                        path.P.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                        path.R.Add(Mathf.Lerp(ctrl[i].r, ctrl[i + 1].r, t * t));
                    }
                }
                path.P.Add(ctrl[ctrl.Count - 1].p); path.R.Add(ctrl[ctrl.Count - 1].r);
                float s = 0; path.S.Add(0);
                for (int i = 1; i < path.P.Count; i++) { s += Vector3.Distance(path.P[i - 1], path.P[i]); path.S.Add(s); }
                path.Length = s;
                int pi = paths.Count; paths.Add(path);
                int n = Mathf.RoundToInt(900 * share);
                for (int i = 0; i < n; i++)
                    parcels.Add(new Parcel { path = pi, s = Random.value * s, off = Random.insideUnitSphere, alv = 0, speed = Random.Range(0.8f, 1.2f) });
            }
            Add("rMain", "rSupB", "rSup", 0.20f);
            Add("rMain", "rMidB", "rMid", 0.12f);
            Add("rMain", "rInfB", "rInf", 0.23f);
            Add("lMain", "lSupB", "lSup", 0.22f);
            Add("lMain", "lInfB", "lInf", 0.23f);

            var go = new GameObject("AirParticles"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 1200; main.playOnAwake = false; main.startLifetime = 1000f;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Resources.Load<Material>("Materials/Blood"));
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    float dx = (x + 0.5f) / 16f - 1, dy = (y + 0.5f) / 16f - 1; float al = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); al = al * al * (3 - 2 * al);
                    tex.SetPixel(x, y, new Color(1, 1, 1, al));
                }
            tex.Apply();
            mat.SetTexture("_BaseMap", tex);
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
        }

        int Seg(Path p, float s)
        {
            int lo = 0, hi = p.S.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (p.S[mid] <= s) lo = mid; else hi = mid; }
            return lo;
        }

        /// <param name="flowMlS">airflow, + into the lungs</param>
        public void Tick(float physDt, float flowMlS)
        {
            if (ps == null) return;
            if (!Visible) { ps.Clear(); return; }
            Color fresh = new Color(0.45f, 0.9f, 1f, 0.85f), used = new Color(0.8f, 0.55f, 1f, 0.85f);
            if (buf.Length < parcels.Count) buf = new ParticleSystem.Particle[parcels.Count];
            for (int i = 0; i < parcels.Count; i++)
            {
                var c = parcels[i]; var p = paths[c.path];
                c.s += flowMlS * 0.0032f * c.speed * physDt;
                if (c.s >= p.Length) c.s = p.Length;
                if (c.s > p.Length * 0.8f) c.alv = Mathf.Min(1f, c.alv + physDt * 0.6f);
                if (c.s <= 0f) { c.s = 0f; c.alv = 0f; c.off = Random.insideUnitSphere; }
                parcels[i] = c;
                int seg = Seg(p, c.s); int nx = Mathf.Min(seg + 1, p.P.Count - 1);
                float t = Mathf.Clamp01((c.s - p.S[seg]) / Mathf.Max(p.S[nx] - p.S[seg], 1e-5f));
                Vector3 pos = Vector3.Lerp(p.P[seg], p.P[nx], t);
                Vector3 tan = (p.P[nx] - p.P[seg]).normalized;
                Vector3 off = c.off - tan * Vector3.Dot(c.off, tan);
                pos += off * Mathf.Lerp(p.R[seg], p.R[nx], t);
                var col = Color.Lerp(fresh, used, c.alv);
                col.a *= Mathf.Clamp01(c.s / 0.05f + 0.15f);
                buf[i].position = pos; buf[i].startSize = 0.07f; buf[i].startColor = col;
                buf[i].remainingLifetime = 100f; buf[i].startLifetime = 1000f;
            }
            ps.SetParticles(buf, parcels.Count);
        }
    }
}
