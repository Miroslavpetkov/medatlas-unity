using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>
    /// Microscopic scenes for the cell- and vessel-level simulations, built in code and driven by the model:
    /// Membrane – lipid bilayer with Na+/K+ channels; ions stream through with the currents, the membrane
    ///            colour follows the membrane potential (blue at rest, orange when depolarised).
    /// Axon     – a nerve fibre with an action potential travelling along it.
    /// Vessel   – a blood vessel with red cells flowing at the model's flow; options for leakage (capillary),
    ///            a clot growing at a wound (haemostasis) and white cells (immune).
    /// </summary>
    public class MicroScene : MonoBehaviour
    {
        public enum Kind { Membrane, Axon, Vessel }
        Kind kind; string id;
        Material membraneMat, glowMat;
        ParticleSystem ps; ParticleSystem.Particle[] buf = new ParticleSystem.Particle[1500];
        readonly List<P> parts = new List<P>();
        struct P { public Vector3 pos, vel; public Color col; public float size, life; public int type; }
        readonly Dictionary<string, (float lo, float hi)> norms = new Dictionary<string, (float, float)>();
        Transform clot; float clotSize;
        readonly List<Transform> channelsNa = new List<Transform>(), channelsK = new List<Transform>();
        float apPos = -1f, lastV = -999f;
        public bool Ready;

        static Material Lit(Color c, float smooth = 0.6f)
        {
            var m = new Material(Resources.Load<Material>("Materials/Highlight"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        static Mesh SphereMesh()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); var m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return m;
        }

        GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent ?? transform, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }

        public void Build(Kind k, string simId)
        {
            kind = k; id = simId;
            switch (k)
            {
                case Kind.Membrane: BuildMembrane(); break;
                case Kind.Axon: BuildAxon(); break;
                case Kind.Vessel: BuildVessel(); break;
            }
            var go = new GameObject("Particles"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 1500; main.playOnAwake = false; main.startLifetime = 1000;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Resources.Load<Material>("Materials/Blood"));
            var tex = new Texture2D(32, 32); for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float dx = (x - 15.5f) / 16f, dy = (y - 15.5f) / 16f; float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); tex.SetPixel(x, y, new Color(1, 1, 1, a * a * (3 - 2 * a))); }
            tex.Apply(); mat.SetTexture("_BaseMap", tex); r.sharedMaterial = mat;
            if (k == Kind.Vessel)
            {
                // red cells as solid lit discs: use mesh particles
                r.renderMode = ParticleSystemRenderMode.Mesh;
                var rbc = new Mesh(); rbc = DiscMesh();
                r.mesh = rbc; r.sharedMaterial = Lit(new Color(0.75f, 0.08f, 0.08f), 0.7f);
                r.alignment = ParticleSystemRenderSpace.World;
            }
            ps.Play();
            Ready = true;
        }

        static Mesh DiscMesh()
        {
            // biconcave red-cell-like disc
            var m = new Mesh(); var v = new List<Vector3>(); var t = new List<int>();
            int seg = 20, rings = 6;
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1 : -1; int b = v.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float rr = r / (float)rings;
                    float h = (0.18f + 0.22f * rr * rr - 0.25f * rr * rr * rr * rr) * s * (1 - rr * rr * 0.2f);
                    for (int i = 0; i < seg; i++) { float a = i / (float)seg * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * rr * 0.5f, h * 0.5f, Mathf.Sin(a) * rr * 0.5f)); }
                }
                for (int r = 0; r < rings; r++) for (int i = 0; i < seg; i++)
                    {
                        int a0 = b + r * seg + i, a1 = b + r * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                        if (side == 0) { t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1); }
                        else { t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0); }
                    }
            }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }

        // ------------------------------------------------------------ membrane
        void BuildMembrane()
        {
            membraneMat = Lit(new Color(0.85f, 0.72f, 0.55f), 0.55f);
            var tailMat = Lit(new Color(0.95f, 0.88f, 0.7f), 0.3f);
            // phospholipid heads: two leaflets of spheres combined into one mesh
            var sphere = SphereMesh();
            var combine = new List<CombineInstance>();
            int n = 22; float step = 0.14f;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var pos = new Vector3((i - n / 2f) * step + (j % 2) * step * 0.5f, s * 0.32f, (j - n / 2f) * step * 0.87f);
                        combine.Add(new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.12f) });
                    }
            var heads = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; heads.CombineMeshes(combine.ToArray());
            var hg = new GameObject("Heads"); hg.transform.SetParent(transform, false);
            hg.AddComponent<MeshFilter>().sharedMesh = heads; hg.AddComponent<MeshRenderer>().sharedMaterial = membraneMat;
            Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(n * step, 0.5f, n * step * 0.87f), tailMat);
            // channels
            var naMat = Lit(new Color(0.85f, 0.25f, 0.2f), 0.7f); var kMat = Lit(new Color(0.25f, 0.45f, 0.95f), 0.7f);
            var pumpMat = Lit(new Color(0.55f, 0.8f, 0.35f), 0.7f);
            for (int c = 0; c < 4; c++)
            {
                var na = Prim(PrimitiveType.Cylinder, new Vector3(-1.1f + c * 0.75f, 0, -0.35f), new Vector3(0.2f, 0.42f, 0.2f), naMat); channelsNa.Add(na.transform);
                var kk = Prim(PrimitiveType.Cylinder, new Vector3(-0.75f + c * 0.75f, 0, 0.45f), new Vector3(0.2f, 0.42f, 0.2f), kMat); channelsK.Add(kk.transform);
            }
            Prim(PrimitiveType.Capsule, new Vector3(1.3f, 0, 0.9f), new Vector3(0.3f, 0.4f, 0.3f), pumpMat);
        }

        // ------------------------------------------------------------ axon
        Material[] segMats;
        void BuildAxon()
        {
            int segs = 24; segMats = new Material[segs];
            var myelin = Lit(new Color(0.95f, 0.93f, 0.88f), 0.5f);
            for (int i = 0; i < segs; i++)
            {
                segMats[i] = Lit(new Color(0.85f, 0.55f, 0.45f), 0.6f);
                var g = Prim(PrimitiveType.Cylinder, new Vector3(-2.4f + i * 0.2f, 0, 0), new Vector3(0.22f, 0.1f, 0.22f), segMats[i]);
                g.transform.localRotation = Quaternion.Euler(0, 0, 90);
                if (i % 4 != 0) { var s = Prim(PrimitiveType.Cylinder, new Vector3(-2.4f + i * 0.2f, 0, 0), new Vector3(0.36f, 0.095f, 0.36f), myelin); s.transform.localRotation = Quaternion.Euler(0, 0, 90); }
            }
            var soma = Prim(PrimitiveType.Sphere, new Vector3(-2.85f, 0, 0), Vector3.one * 0.7f, Lit(new Color(0.8f, 0.5f, 0.45f), 0.6f));
            for (int d = 0; d < 6; d++)
            {
                var den = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.06f, 0.35f, 0.06f), soma.GetComponent<Renderer>().sharedMaterial, soma.transform);
                den.transform.localPosition = Quaternion.Euler(0, d * 60, 50) * Vector3.up * 0.55f; den.transform.localRotation = Quaternion.Euler(0, d * 60, 50);
            }
        }

        // ------------------------------------------------------------ vessel
        void BuildVessel()
        {
            var wall = new Material(Resources.Load<Material>("Materials/XRay"));
            wall.SetColor("_Color", new Color(1f, 0.45f, 0.42f, 1f)); wall.SetFloat("_MinAlpha", 0.06f); wall.SetFloat("_MaxAlpha", 0.55f);
            var tube = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.3f, 2.8f, 1.3f), wall);
            tube.transform.localRotation = Quaternion.Euler(0, 0, 90);
            if (id == "haemostasis")
            {
                clot = Prim(PrimitiveType.Sphere, new Vector3(0, 0.62f, 0), Vector3.zero, Lit(new Color(0.55f, 0.05f, 0.08f), 0.4f)).transform;
            }
        }

        // ------------------------------------------------------------ drive
        float N(Dictionary<string, object> now, string ch)
        {
            if (now == null || !now.TryGetValue(ch, out var o) || !(o is double d)) return -1;
            float v = (float)d;
            norms.TryGetValue(ch, out var r);
            if (!norms.ContainsKey(ch)) r = (v, v);
            r = (Mathf.Min(r.lo, v), Mathf.Max(r.hi, v)); norms[ch] = r;
            return r.hi - r.lo < 1e-6f ? 0.5f : (v - r.lo) / (r.hi - r.lo);
        }

        float Raw(Dictionary<string, object> now, params string[] chs)
        {
            foreach (var ch in chs) if (now != null && now.TryGetValue(ch, out var o) && o is double d) return (float)d;
            return float.NaN;
        }

        public void Tick(Dictionary<string, object> now, float dt)
        {
            if (!Ready) return;
            switch (kind)
            {
                case Kind.Membrane: TickMembrane(now, dt); break;
                case Kind.Axon: TickAxon(now, dt); break;
                case Kind.Vessel: TickVessel(now, dt); break;
            }
            // integrate particles
            int n = 0;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i]; p.pos += p.vel * dt; p.life -= dt;
                if (p.life <= 0) { parts.RemoveAt(i); continue; }
                parts[i] = p;
            }
            if (buf.Length < parts.Count) buf = new ParticleSystem.Particle[parts.Count];
            foreach (var p in parts)
            {
                buf[n].position = p.pos; buf[n].startSize = p.size; buf[n].startColor = p.col; buf[n].remainingLifetime = 100; buf[n].startLifetime = 1000;
                if (kind == Kind.Vessel) buf[n].rotation3D = new Vector3(p.type * 37 % 360, p.type * 71 % 360, 90);
                n++;
            }
            ps.SetParticles(buf, n);
        }

        void Spawn(Vector3 pos, Vector3 vel, Color col, float size, float life, int type = 0)
        {
            if (parts.Count < 1400) parts.Add(new P { pos = pos, vel = vel, col = col, size = size, life = life, type = type });
        }

        float accNa, accK, accPump;
        void TickMembrane(Dictionary<string, object> now, float dt)
        {
            float v = Raw(now, "v", "vm");
            float depol = float.IsNaN(v) ? 0.3f : Mathf.InverseLerp(-90f, 30f, v);
            var c = Color.Lerp(new Color(0.2f, 0.45f, 1f), new Color(1f, 0.55f, 0.15f), depol);
            membraneMat.SetColor("_EmissionColor", c * (0.25f + depol * 0.9f));
            // currents: inward Na (red, from top/outside down), outward K (blue, upward)
            float iNa = Mathf.Abs(Raw(now, "iNa", "na", "ampa", "eNa")); if (float.IsNaN(iNa)) iNa = depol;
            float iK = Mathf.Abs(Raw(now, "iK", "k", "eK")); if (float.IsNaN(iK)) iK = 1 - depol;
            float nNa = N(now, now != null && now.ContainsKey("iNa") ? "iNa" : now != null && now.ContainsKey("gNa") ? "gNa" : "v");
            float nK = N(now, now != null && now.ContainsKey("iK") ? "iK" : now != null && now.ContainsKey("gK") ? "gK" : "v");
            if (nNa < 0) nNa = depol; if (nK < 0) nK = 1 - depol;
            accNa += dt * (2f + nNa * 60f); accK += dt * (2f + nK * 40f); accPump += dt * 6f;
            foreach (var ch in channelsNa) ch.localScale = new Vector3(0.2f + nNa * 0.08f, 0.42f, 0.2f + nNa * 0.08f);
            foreach (var ch in channelsK) ch.localScale = new Vector3(0.2f + nK * 0.08f, 0.42f, 0.2f + nK * 0.08f);
            while (accNa > 1) { accNa--; var ch = channelsNa[Random.Range(0, channelsNa.Count)]; Spawn(ch.localPosition + Vector3.up * 1.4f + Random.insideUnitSphere * 0.15f, Vector3.down * 1.4f, new Color(1f, 0.35f, 0.25f, 0.95f), 0.09f, 2f); }
            while (accK > 1) { accK--; var ch = channelsK[Random.Range(0, channelsK.Count)]; Spawn(ch.localPosition + Vector3.down * 1.4f + Random.insideUnitSphere * 0.15f, Vector3.up * 1.4f, new Color(0.4f, 0.6f, 1f, 0.95f), 0.09f, 2f); }
            while (accPump > 1) { accPump--; Spawn(new Vector3(1.3f, -0.3f, 0.9f), new Vector3(0, 0.9f, 0), new Color(1f, 0.4f, 0.3f, 0.8f), 0.07f, 1.4f); }
        }

        void TickAxon(Dictionary<string, object> now, float dt)
        {
            float v = Raw(now, "v", "vm");
            if (!float.IsNaN(v))
            {
                if (lastV < -20 && v >= -20 && apPos < 0) apPos = 0; // a spike starts at the axon hillock
                lastV = v;
            }
            if (apPos >= 0) apPos += dt * 6f;  // shown slowed down
            if (apPos > 1.2f) apPos = -1;
            for (int i = 0; i < segMats.Length; i++)
            {
                float x = i / (float)segMats.Length;
                float k = apPos < 0 ? 0 : Mathf.Exp(-Mathf.Pow((x - apPos) * 9f, 2));
                segMats[i].SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * k * 2.5f + new Color(0.15f, 0.3f, 0.8f) * (1 - k) * 0.25f);
                if (k > 0.6f && Random.value < dt * 30f)
                    Spawn(new Vector3(-2.4f + i * 0.2f, 0.45f, 0), Vector3.down * 0.6f, new Color(1f, 0.35f, 0.25f, 0.9f), 0.07f, 0.7f);
            }
        }

        float accRbc, accLeak;
        void TickVessel(Dictionary<string, object> now, float dt)
        {
            float q = N(now, now != null && now.ContainsKey("q") ? "q" : now != null && now.ContainsKey("flow") ? "flow" : "map");
            float speed = 0.6f + (q < 0 ? 0.5f : q) * 2.6f;
            float density = 1f;
            if (id == "blood") { float hb = Raw(now, "hb"); if (!float.IsNaN(hb)) density = Mathf.Clamp(hb / 15f, 0.2f, 1.5f); }
            accRbc += dt * 22f * density * speed;
            while (accRbc > 1)
            {
                accRbc--;
                var r = Random.insideUnitCircle * 0.5f;
                Spawn(new Vector3(-1.45f, r.x, r.y), new Vector3(speed, 0, 0), Color.white, 0.18f, 2.9f / speed, Random.Range(0, 1000));
            }
            if (id == "capillary")
            {
                float f = N(now, "filt"); accLeak += dt * (1 + Mathf.Max(0, f) * 25f);
                while (accLeak > 1) { accLeak--; Spawn(new Vector3(Random.Range(-1.2f, 1.2f), 0.6f, Random.Range(-0.3f, 0.3f)), new Vector3(0, 0.5f, 0), Color.white, 0.06f, 1.5f, 0); }
            }
            if (clot != null)
            {
                float s = N(now, "seal"); if (s < 0) s = N(now, "agg");
                clotSize = Mathf.Lerp(clotSize, Mathf.Max(0, s) * 0.55f, dt * 2f);
                clot.localScale = new Vector3(clotSize * 1.6f, clotSize, clotSize);
            }
        }
    }
}
