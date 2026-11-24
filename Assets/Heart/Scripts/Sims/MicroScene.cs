using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>
    /// Microscopic scenes for the cell- and tissue-level simulations, built in code and driven by the model's live values
    /// (the same simulations are cell-level scenes in the web version):
    /// Membrane  – lipid bilayer with Na+/K+ channels and the pump; ions stream with the currents, the membrane colour
    ///             follows the membrane potential. Variants: vesicles (endo-/exocytosis), signal, potassium.
    /// Axon      – a nerve fibre with an action potential travelling along it; electrotonic spread; fibre types.
    /// Vessel    – a blood vessel with red cells at the model's flow; leakage (capillary), clot (haemostasis),
    ///             white cells (leukocytes), platelets (haemopoiesis), plasma proteins, lipoproteins, lymph.
    /// Synapse   – presynaptic ending with vesicles and Ca2+ channels, the cleft and postsynaptic receptors;
    ///             with an astrocyte and a capillary for neuron–glia.
    /// Sarcomere – Z discs, thick (myosin) and thin (actin) filaments, cross-bridges and Ca2+ (skeletal muscle).
    /// Smooth    – a spindle-shaped smooth muscle cell with dense bodies that shortens with its force.
    /// Nephron   – glomerulus in Bowman's capsule, the tubule with its epithelium and the peritubular capillary.
    /// Skin      – epidermis, dermis and hypodermis with the skin receptors, a vessel, a sweat gland and the nerve.
    /// </summary>
    public class MicroScene : MonoBehaviour
    {
        public enum Kind { Membrane, Axon, Vessel, Synapse, Sarcomere, Smooth, Nephron, Skin }
        Kind kind; string id;
        public bool Ready;

        struct P { public Vector3 pos, vel; public Color col; public float size, life; public int type; }
        class Layer { public ParticleSystem ps; public readonly List<P> parts = new List<P>(); public ParticleSystem.Particle[] buf = new ParticleSystem.Particle[256]; public bool mesh; }
        readonly List<Layer> layers = new List<Layer>();
        Layer dots;                       // soft coloured dots: ions, molecules, droplets
        Layer rbcs;                       // red cells (vessel scenes)
        static Texture2D dotTex;
        static Mesh sphereMesh;

        readonly Dictionary<string, (float lo, float hi)> norms = new Dictionary<string, (float, float)>();

        // ------------------------------------------------------------ helpers
        static Material Lit(Color c, float smooth = 0.6f)
        {
            var m = new Material(Resources.Load<Material>("Materials/Highlight"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        static Material Glass(Color c, float lo = 0.06f, float hi = 0.55f)
        {
            var m = new Material(Resources.Load<Material>("Materials/XRay"));
            m.SetColor("_Color", c); m.SetFloat("_MinAlpha", lo); m.SetFloat("_MaxAlpha", hi);
            return m;
        }

        static Mesh SphereMesh()
        {
            if (sphereMesh != null) return sphereMesh;
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphereMesh = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return sphereMesh;
        }

        GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent ?? transform, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m; return g;
        }

        /// <summary>A cylinder lying along x (Unity's cylinder is 2 units tall along y).</summary>
        GameObject RodX(Vector3 center, float length, float radius, Material m, Transform parent = null)
        {
            var g = Prim(PrimitiveType.Cylinder, center, new Vector3(radius * 2, length / 2, radius * 2), m, parent);
            g.transform.localRotation = Quaternion.Euler(0, 0, 90); return g;
        }

        /// <summary>A cylinder from a to b.</summary>
        GameObject Rod(Vector3 a, Vector3 b, float radius, Material m, Transform parent = null)
        {
            var g = Prim(PrimitiveType.Cylinder, (a + b) / 2, new Vector3(radius * 2, (b - a).magnitude / 2, radius * 2), m, parent);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized); return g;
        }

        Layer NewLayer(Mesh mesh = null, Material mat = null)
        {
            var go = new GameObject(mesh == null ? "Dots" : "Cells"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 1500; main.playOnAwake = false; main.startLifetime = 1000;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (mesh == null)
            {
                if (dotTex == null)
                {
                    dotTex = new Texture2D(32, 32);
                    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float dx = (x - 15.5f) / 16f, dy = (y - 15.5f) / 16f; float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); dotTex.SetPixel(x, y, new Color(1, 1, 1, a * a * (3 - 2 * a))); }
                    dotTex.Apply();
                }
                var m = new Material(Resources.Load<Material>("Materials/Blood")); m.SetTexture("_BaseMap", dotTex); r.sharedMaterial = m;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh; r.sharedMaterial = mat; r.alignment = ParticleSystemRenderSpace.World;
            }
            ps.Play();
            var L = new Layer { ps = ps, mesh = mesh != null }; layers.Add(L); return L;
        }

        void Spawn(Layer L, Vector3 pos, Vector3 vel, Color col, float size, float life, int type = 0)
        {
            if (L != null && L.parts.Count < 1400) L.parts.Add(new P { pos = pos, vel = vel, col = col, size = size, life = life, type = type });
        }

        /// <summary>Spawn rate helper: adds rate*dt to the accumulator and returns how many to spawn now.</summary>
        static int Due(ref float acc, float rate, float dt)
        {
            acc += Mathf.Max(0, rate) * dt; int n = (int)acc; acc -= n; return Mathf.Min(n, 40);
        }

        public void Build(Kind k, string simId)
        {
            kind = k; id = simId;
            dots = NewLayer();
            switch (k)
            {
                case Kind.Membrane: BuildMembrane(); break;
                case Kind.Axon: BuildAxon(); break;
                case Kind.Vessel: BuildVessel(); break;
                case Kind.Synapse: BuildSynapse(); break;
                case Kind.Sarcomere: BuildSarcomere(); break;
                case Kind.Smooth: BuildSmooth(); break;
                case Kind.Nephron: BuildNephron(); break;
                case Kind.Skin: BuildSkin(); break;
            }
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

        // ------------------------------------------------------------ live values
        /// <summary>The value normalised to the range seen so far (0.5 while it has not changed).</summary>
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

        /// <summary>The value mapped from a fixed physiological range to 0..1 (def when the model has no such value).</summary>
        float F(Dictionary<string, object> now, string ch, float lo, float hi, float def = 0f)
        {
            float v = Raw(now, ch); return float.IsNaN(v) ? def : Mathf.Clamp01((v - lo) / (hi - lo));
        }

        static readonly Color NaCol = new Color(1f, 0.35f, 0.25f, 0.95f), KCol = new Color(0.4f, 0.6f, 1f, 0.95f), CaCol = new Color(1f, 0.85f, 0.25f, 0.95f),
            ClCol = new Color(0.4f, 1f, 0.5f, 0.95f), Rest = new Color(0.2f, 0.45f, 1f), Excited = new Color(1f, 0.55f, 0.15f);

        static Color Potential(float depol) => Color.Lerp(Rest, Excited, depol) * (0.25f + depol * 0.9f);

        public void Tick(Dictionary<string, object> now, float dt)
        {
            if (!Ready) return;
            switch (kind)
            {
                case Kind.Membrane: TickMembrane(now, dt); break;
                case Kind.Axon: TickAxon(now, dt); break;
                case Kind.Vessel: TickVessel(now, dt); break;
                case Kind.Synapse: TickSynapse(now, dt); break;
                case Kind.Sarcomere: TickSarcomere(now, dt); break;
                case Kind.Smooth: TickSmooth(now, dt); break;
                case Kind.Nephron: TickNephron(now, dt); break;
                case Kind.Skin: TickSkin(now, dt); break;
            }
            foreach (var L in layers)
            {
                for (int i = L.parts.Count - 1; i >= 0; i--)
                {
                    var p = L.parts[i]; p.pos += p.vel * dt; p.life -= dt;
                    if (p.life <= 0) { L.parts.RemoveAt(i); continue; }
                    L.parts[i] = p;
                }
                if (L.buf.Length < L.parts.Count) L.buf = new ParticleSystem.Particle[L.parts.Count * 2];
                int n = 0;
                foreach (var p in L.parts)
                {
                    L.buf[n].position = p.pos; L.buf[n].startSize = p.size; L.buf[n].startColor = p.col; L.buf[n].remainingLifetime = 100; L.buf[n].startLifetime = 1000;
                    if (L.mesh) L.buf[n].rotation3D = new Vector3(p.type * 37 % 360, p.type * 71 % 360, 90);
                    n++;
                }
                L.ps.SetParticles(L.buf, n);
            }
        }

        // ============================================================ membrane
        Material membraneMat;
        readonly List<Transform> channelsNa = new List<Transform>(), channelsK = new List<Transform>();
        Layer vesLayer;

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
            if (id == "vesicles")
            {
                // receptors for LDL on the outer face, vesicles budding inwards and fusing outwards
                var recMat = Lit(new Color(0.55f, 0.8f, 0.35f), 0.7f);
                for (int c = 0; c < 6; c++) Prim(PrimitiveType.Capsule, new Vector3(-1.3f + c * 0.18f, 0.45f, -0.2f + (c % 2) * 0.3f), new Vector3(0.08f, 0.14f, 0.08f), recMat);
                vesLayer = NewLayer(SphereMesh(), Glass(new Color(0.95f, 0.85f, 0.55f), 0.25f, 0.85f));
                return;
            }
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

        float accNa, accK, accPump, accIn, accOut, accLdl;
        void TickMembrane(Dictionary<string, object> now, float dt)
        {
            if (id == "vesicles")
            {
                // endocytosis: vesicles pinch off into the cytosol; exocytosis: vesicles rise and fuse; LDL binds above
                float rin = F(now, "rin", 0, 38), rel = F(now, "rel", 0, 66);
                membraneMat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.4f) * (0.1f + F(now, "ca10", 1, 35) * 0.8f));
                for (int i = Due(ref accIn, 0.4f + rin * 5f, dt); i > 0; i--) Spawn(vesLayer, new Vector3(Random.Range(-1.4f, -0.2f), -0.35f, Random.Range(-0.4f, 0.4f)), Vector3.down * 0.45f, Color.white, 0.26f, 2.6f);
                for (int i = Due(ref accLdl, 0.5f + rin * 8f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.5f, -0.1f), 1.6f, Random.Range(-0.4f, 0.4f)), Vector3.down * 0.6f, new Color(1f, 0.8f, 0.2f, 0.95f), 0.11f, 1.8f);
                for (int i = Due(ref accOut, 0.4f + rel * 5f, dt); i > 0; i--) Spawn(vesLayer, new Vector3(Random.Range(0.2f, 1.4f), -1.6f, Random.Range(-0.4f, 0.4f)), Vector3.up * 0.5f, Color.white, 0.26f, 2.6f);
                return;
            }
            float v = Raw(now, "v", "vm");
            float depol = float.IsNaN(v) ? 0.3f : Mathf.InverseLerp(-90f, 30f, v);
            membraneMat.SetColor("_EmissionColor", Potential(depol));
            // currents: inward Na (red, from top/outside down), outward K (blue, upward)
            float nNa = N(now, now != null && now.ContainsKey("iNa") ? "iNa" : now != null && now.ContainsKey("gNa") ? "gNa" : "v");
            float nK = N(now, now != null && now.ContainsKey("iK") ? "iK" : now != null && now.ContainsKey("gK") ? "gK" : "v");
            if (nNa < 0) nNa = depol; if (nK < 0) nK = 1 - depol;
            if (id == "potassium") nK = F(now, "k", 2f, 8f, 0.4f);   // more K+ outside: the outward current changes with plasma K+
            if (id == "signal") nNa = Mathf.Max(F(now, "camp", 0, 10, 0), F(now, "ca", 0, 1, 0));
            foreach (var ch in channelsNa) ch.localScale = new Vector3(0.2f + nNa * 0.08f, 0.42f, 0.2f + nNa * 0.08f);
            foreach (var ch in channelsK) ch.localScale = new Vector3(0.2f + nK * 0.08f, 0.42f, 0.2f + nK * 0.08f);
            for (int i = Due(ref accNa, 2f + nNa * 60f, dt); i > 0; i--) { var ch = channelsNa[Random.Range(0, channelsNa.Count)]; Spawn(dots, ch.localPosition + Vector3.up * 1.4f + Random.insideUnitSphere * 0.15f, Vector3.down * 1.4f, id == "signal" ? CaCol : NaCol, 0.09f, 2f); }
            for (int i = Due(ref accK, 2f + nK * 40f, dt); i > 0; i--) { var ch = channelsK[Random.Range(0, channelsK.Count)]; Spawn(dots, ch.localPosition + Vector3.down * 1.4f + Random.insideUnitSphere * 0.15f, Vector3.up * 1.4f, KCol, 0.09f, 2f); }
            for (int i = Due(ref accPump, 6f, dt); i > 0; i--) Spawn(dots, new Vector3(1.3f, -0.3f, 0.9f), new Vector3(0, 0.9f, 0), new Color(1f, 0.4f, 0.3f, 0.8f), 0.07f, 1.4f);
        }

        // ============================================================ axon
        Material[] segMats;
        float apPos = -1f, lastV = -999f, lastSince = 1e9f;
        void BuildAxon()
        {
            int segs = 24; segMats = new Material[segs];
            var myelin = Lit(new Color(0.95f, 0.93f, 0.88f), 0.5f);
            bool bare = id == "electrotonic";   // the cable experiment is on an unmyelinated fibre
            for (int i = 0; i < segs; i++)
            {
                segMats[i] = Lit(new Color(0.85f, 0.55f, 0.45f), 0.6f);
                var g = Prim(PrimitiveType.Cylinder, new Vector3(-2.4f + i * 0.2f, 0, 0), new Vector3(0.22f, 0.1f, 0.22f), segMats[i]);
                g.transform.localRotation = Quaternion.Euler(0, 0, 90);
                if (!bare && i % 4 != 0) { var s = Prim(PrimitiveType.Cylinder, new Vector3(-2.4f + i * 0.2f, 0, 0), new Vector3(0.36f, 0.095f, 0.36f), myelin); s.transform.localRotation = Quaternion.Euler(0, 0, 90); }
            }
            var soma = Prim(PrimitiveType.Sphere, new Vector3(-2.85f, 0, 0), Vector3.one * 0.7f, Lit(new Color(0.8f, 0.5f, 0.45f), 0.6f));
            for (int d = 0; d < 6; d++)
            {
                var den = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.06f, 0.35f, 0.06f), soma.GetComponent<Renderer>().sharedMaterial, soma.transform);
                den.transform.localPosition = Quaternion.Euler(0, d * 60, 50) * Vector3.up * 0.55f; den.transform.localRotation = Quaternion.Euler(0, d * 60, 50);
            }
            if (id == "electrotonic" || id == "nerve-fibres")
            {
                // stimulating electrode near the soma end, recording electrode further along
                var el = Lit(new Color(0.75f, 0.75f, 0.8f), 0.9f);
                Rod(new Vector3(-2.2f, 0.18f, 0), new Vector3(-2.2f, 1.2f, 0), 0.025f, el);
                Rod(new Vector3(1.9f, 0.18f, 0), new Vector3(1.9f, 1.2f, 0), 0.025f, el);
            }
        }

        void TickAxon(Dictionary<string, object> now, float dt)
        {
            int n = segMats.Length;
            if (id == "electrotonic")
            {
                // the local potential spreads and fades with distance (v0 at the electrode, v1 and v2 further along)
                float v0 = Raw(now, "v0"), v1 = Raw(now, "v1"), v2 = Raw(now, "v2");
                if (float.IsNaN(v0)) v0 = -70; if (float.IsNaN(v1)) v1 = -70; if (float.IsNaN(v2)) v2 = -70;
                for (int i = 0; i < n; i++)
                {
                    float x = i / (float)(n - 1);
                    float v = x < 0.5f ? Mathf.Lerp(v0, v1, x * 2) : Mathf.Lerp(v1, v2, (x - 0.5f) * 2);
                    float dep = Mathf.InverseLerp(-70f, -40f, v), hyp = Mathf.InverseLerp(-70f, -80f, v);
                    segMats[i].SetColor("_EmissionColor", Excited * dep * 2.4f + new Color(0.2f, 0.4f, 1f) * (0.2f + hyp * 1.8f) * (1 - dep));
                    if (dep > 0.6f && Random.value < dt * 20f) Spawn(dots, new Vector3(-2.4f + i * 0.2f, 0.45f, 0), Vector3.down * 0.6f, NaCol, 0.07f, 0.7f);
                }
                return;
            }
            float vv = Raw(now, "v", "vm");
            if (!float.IsNaN(vv))
            {
                if (lastV < -20 && vv >= -20 && apPos < 0) apPos = 0; // a spike starts at the axon hillock
                lastV = vv;
            }
            float since = Raw(now, "since");
            if (id == "nerve-fibres" && !float.IsNaN(since)) { if (since < lastSince && apPos < 0) apPos = 0; lastSince = since; }
            if (apPos >= 0) apPos += dt * (id == "nerve-fibres" ? 1.2f + F(now, "cap", 0, 0.3f) * 4f : 6f);  // shown slowed down
            if (apPos > 1.2f) apPos = -1;
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)n;
                float k = apPos < 0 ? 0 : Mathf.Exp(-Mathf.Pow((x - apPos) * 9f, 2));
                segMats[i].SetColor("_EmissionColor", Excited * k * 2.5f + new Color(0.15f, 0.3f, 0.8f) * (1 - k) * 0.25f);
                if (k > 0.6f && Random.value < dt * 30f)
                    Spawn(dots, new Vector3(-2.4f + i * 0.2f, 0.45f, 0), Vector3.down * 0.6f, NaCol, 0.07f, 0.7f);
            }
        }

        // ============================================================ vessel
        Transform clot; float clotSize;
        Layer wbcs, lipo;
        void BuildVessel()
        {
            var wall = Glass(new Color(1f, 0.45f, 0.42f, 1f));
            RodX(Vector3.zero, 5.6f, 0.65f, wall);
            rbcs = NewLayer(DiscMesh(), Lit(new Color(0.75f, 0.08f, 0.08f), 0.7f));
            if (id == "haemostasis")
                clot = Prim(PrimitiveType.Sphere, new Vector3(0, 0.62f, 0), Vector3.zero, Lit(new Color(0.55f, 0.05f, 0.08f), 0.4f)).transform;
            if (id == "leukocytes" || id == "haemopoiesis" || id == "immune")
                wbcs = NewLayer(SphereMesh(), Lit(new Color(0.92f, 0.9f, 0.95f), 0.5f));
            if (id == "lipids")
                lipo = NewLayer(SphereMesh(), Lit(new Color(1f, 0.85f, 0.35f), 0.8f));
            if (id == "leukocytes")
            {
                // the infected tissue above the vessel
                Prim(PrimitiveType.Sphere, new Vector3(0.6f, 1.75f, 0), new Vector3(1.8f, 0.8f, 1.2f), Glass(new Color(0.6f, 0.9f, 0.5f), 0.04f, 0.35f));
            }
            if (id == "capillary" || id == "lymph")
            {
                // tissue around and a blind-ended lymph capillary below
                Prim(PrimitiveType.Cube, new Vector3(0, -0.2f, 0), new Vector3(5.4f, 3.2f, 2.2f), Glass(new Color(0.95f, 0.75f, 0.6f), 0.02f, 0.18f));
                RodX(new Vector3(0, -1.35f, 0), 5.0f, 0.32f, Glass(new Color(0.8f, 0.95f, 0.6f), 0.08f, 0.6f));
            }
        }

        float accRbc, accLeak, accWbc, accPlt, accProt, accBact, accLipo, accLymph;
        void TickVessel(Dictionary<string, object> now, float dt)
        {
            float q = N(now, now != null && now.ContainsKey("q") ? "q" : now != null && now.ContainsKey("flow") ? "flow" : "map");
            float speed = 0.6f + (q < 0 ? 0.5f : q) * 2.6f;
            float density = 1f;
            switch (id)
            {
                case "blood": { float hb = Raw(now, "hb"); if (!float.IsNaN(hb)) density = Mathf.Clamp(hb / 15f, 0.2f, 1.5f); break; }
                case "haemopoiesis": density = Mathf.Clamp(F(now, "hb", 0, 100, 1) , 0.2f, 1.3f); speed = 1.6f; break;
                case "plasma":
                    {
                        density = Mathf.Clamp(Raw(now, "hct") / 45f, 0.3f, 1.5f); if (float.IsNaN(density)) density = 1;
                        float visc = Raw(now, "visc"); speed = float.IsNaN(visc) ? 1.6f : 1.6f * 5f / Mathf.Max(2f, visc);
                        break;
                    }
                case "leukocytes": case "lipids": case "lymph": speed = 1.6f; break;
            }
            for (int i = Due(ref accRbc, 22f * density * speed, dt); i > 0; i--)
            {
                var r = Random.insideUnitCircle * 0.5f;
                Spawn(rbcs, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), Color.white, 0.18f, 5.5f / speed, Random.Range(0, 1000));
            }
            if (id == "capillary")
            {
                float f = N(now, "filt");
                for (int i = Due(ref accLeak, 1 + Mathf.Max(0, f) * 25f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.8f, 1.8f), 0.62f, Random.Range(-0.3f, 0.3f)), new Vector3(0, 0.5f, 0), new Color(0.6f, 0.85f, 1f, 0.85f), 0.07f, 1.5f);
            }
            if (id == "capillary" || id == "lymph")
            {
                // tissue fluid drains into the lymph capillary and flows along it
                float lf = id == "lymph" ? F(now, "flow", 50, 1600, 0.2f) : 0.3f;
                for (int i = Due(ref accLymph, 3f + lf * 40f, dt); i > 0; i--)
                {
                    var r = Random.insideUnitCircle * 0.22f;
                    Spawn(dots, new Vector3(-2.4f, -1.35f + r.x, r.y), new Vector3(0.5f + lf * 2.2f, 0, 0), new Color(0.85f, 1f, 0.6f, 0.9f), 0.09f, 4.8f / (0.5f + lf * 2.2f));
                }
                for (int i = Due(ref accLeak, 2f + lf * 12f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.8f, 1.8f), -0.62f, Random.Range(-0.3f, 0.3f)), new Vector3(0, -0.5f, 0), new Color(0.6f, 0.85f, 1f, 0.85f), 0.07f, 0.9f);
            }
            if (id == "leukocytes")
            {
                float tot = F(now, "tot", 0, 20, 0.35f), bact = F(now, "bact", 0, 50);
                for (int i = Due(ref accWbc, 1f + tot * 14f, dt); i > 0; i--)
                {
                    var r = Random.insideUnitCircle * 0.45f;
                    // with an infection some neutrophils leave the vessel (diapedesis) towards the bacteria
                    bool out_ = bact > 0.02f && Random.value < 0.25f + bact * 0.5f;
                    Spawn(wbcs, new Vector3(out_ ? Random.Range(-0.6f, 1.6f) : -2.75f, out_ ? 0.6f : r.x, r.y), out_ ? new Vector3(Random.Range(-0.2f, 0.2f), 0.45f, 0) : new Vector3(speed * 0.8f, 0, 0), Color.white, 0.3f, out_ ? 2.4f : 5.5f / speed, 0);
                }
                for (int i = Due(ref accBact, bact * 30f, dt); i > 0; i--)
                    Spawn(dots, new Vector3(0.6f, 1.75f, 0) + Vector3.Scale(Random.insideUnitSphere, new Vector3(0.8f, 0.3f, 0.5f)), Random.insideUnitSphere * 0.08f, new Color(0.45f, 0.95f, 0.3f, 0.95f), 0.1f, 2f);
            }
            if (id == "haemopoiesis")
            {
                float neut = F(now, "neut", 0, 180, 0.55f), plt = F(now, "plt", 0, 110, 0.9f);
                for (int i = Due(ref accWbc, 1f + neut * 6f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.45f; Spawn(wbcs, new Vector3(-2.75f, r.x, r.y), new Vector3(speed * 0.8f, 0, 0), Color.white, 0.3f, 5.5f / speed); }
                for (int i = Due(ref accPlt, plt * 40f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.5f; Spawn(dots, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), new Color(0.85f, 0.6f, 1f, 0.95f), 0.07f, 5.5f / speed); }
            }
            if (id == "plasma")
            {
                float alb = F(now, "alb", 10, 55, 0.6f);
                for (int i = Due(ref accProt, alb * 60f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.55f; Spawn(dots, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), new Color(1f, 0.95f, 0.5f, 0.85f), 0.05f, 5.5f / speed); }
            }
            if (id == "lipids")
            {
                // chylomicrons (large), VLDL (medium), free fatty acids and ketone bodies (dots)
                float chylo = F(now, "chylo", 0, 0.4f), vldl = F(now, "vldl", 0, 10, 0.1f), ffa = F(now, "ffa", 0, 2.6f, 0.2f), ket = F(now, "ket", 0, 14);
                for (int i = Due(ref accLipo, chylo * 8f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.4f; Spawn(lipo, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), Color.white, 0.42f, 5.5f / speed); }
                for (int i = Due(ref accWbc, 2f + vldl * 25f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.45f; Spawn(lipo, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), Color.white, 0.18f, 5.5f / speed); }
                for (int i = Due(ref accProt, ffa * 40f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.55f; Spawn(dots, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), new Color(1f, 0.95f, 0.6f, 0.9f), 0.06f, 5.5f / speed); }
                for (int i = Due(ref accPlt, ket * 40f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.55f; Spawn(dots, new Vector3(-2.75f, r.x, r.y), new Vector3(speed, 0, 0), new Color(0.6f, 1f, 0.6f, 0.9f), 0.06f, 5.5f / speed); }
            }
            if (clot != null)
            {
                float s = N(now, "seal"); if (s < 0) s = N(now, "agg");
                clotSize = Mathf.Lerp(clotSize, Mathf.Max(0, s) * 0.55f, dt * 2f);
                clot.localScale = new Vector3(clotSize * 1.6f, clotSize, clotSize);
            }
        }

        // ============================================================ synapse
        Material boutonMat, postMat, astroMat;
        readonly List<GameObject> vesicles = new List<GameObject>();
        readonly List<Material> caCh = new List<Material>();
        readonly List<(Material m, int type)> receptors = new List<(Material, int)>();
        Transform astro, microglia;
        float lastVpre = -65f;

        void BuildSynapse()
        {
            boutonMat = Glass(new Color(1f, 0.7f, 0.5f), 0.08f, 0.6f);
            // axon and its ending (bouton) above the cleft
            Prim(PrimitiveType.Sphere, new Vector3(0, 1.5f, 0), new Vector3(2.6f, 1.8f, 1.8f), boutonMat);
            Rod(new Vector3(-0.4f, 2.2f, 0), new Vector3(-1.6f, 3.6f, 0), 0.28f, boutonMat);
            var vesMat = Lit(new Color(0.95f, 0.9f, 0.7f), 0.7f);
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(0.85f, 1.7f), Random.Range(-0.5f, 0.5f));
                vesicles.Add(Prim(PrimitiveType.Sphere, p, Vector3.one * 0.18f, vesMat));
            }
            // the active zone: voltage-gated Ca2+ channels
            for (int c = 0; c < 4; c++) { var m = Lit(new Color(0.9f, 0.75f, 0.2f), 0.7f); caCh.Add(m); Prim(PrimitiveType.Cylinder, new Vector3(-0.75f + c * 0.5f, 0.66f, 0.2f), new Vector3(0.12f, 0.08f, 0.12f), m); }
            // postsynaptic membrane (dendritic spine) and its receptors: AMPA, NMDA, GABA-A
            postMat = Lit(new Color(0.85f, 0.6f, 0.65f), 0.55f);
            Prim(PrimitiveType.Sphere, new Vector3(0, -0.85f, 0), new Vector3(3.0f, 1.2f, 1.9f), postMat);
            Rod(new Vector3(0.2f, -1.2f, 0), new Vector3(1.2f, -2.8f, 0), 0.35f, postMat);
            var rc = new[] { new Color(1f, 0.4f, 0.3f), new Color(0.35f, 0.5f, 1f), new Color(0.35f, 0.9f, 0.45f) };
            for (int r = 0; r < 6; r++)
            {
                int t = r % 3; var m = Lit(rc[t], 0.7f); receptors.Add((m, t));
                Prim(PrimitiveType.Cylinder, new Vector3(-1.0f + r * 0.4f, -0.27f, -0.15f + (r % 2) * 0.3f), new Vector3(0.13f, 0.09f, 0.13f), m);
            }
            if (id == "neuron-glia")
            {
                // astrocyte wrapping the synapse, with an end-foot on a capillary; a microglial cell when inflamed
                astroMat = Lit(new Color(0.55f, 0.75f, 0.95f), 0.5f);
                astro = Prim(PrimitiveType.Sphere, new Vector3(2.6f, 0.2f, 0), Vector3.one * 1.1f, astroMat).transform;
                Rod(new Vector3(2.6f, 0.2f, 0), new Vector3(1.25f, 0.25f, 0), 0.16f, astroMat);
                Rod(new Vector3(2.6f, 0.2f, 0), new Vector3(2.9f, -1.6f, 0), 0.2f, astroMat);
                Rod(new Vector3(2.6f, 0.2f, 0), new Vector3(3.4f, 1.8f, 0.3f), 0.12f, astroMat);
                Rod(new Vector3(2.6f, 0.2f, 0), new Vector3(3.8f, 0.6f, -0.4f), 0.12f, astroMat);
                var cap = Prim(PrimitiveType.Cylinder, new Vector3(2.9f, -2.1f, 0), new Vector3(0.8f, 1.4f, 0.8f), Glass(new Color(1f, 0.45f, 0.42f)));
                cap.transform.localRotation = Quaternion.Euler(90, 0, 0);
                microglia = Prim(PrimitiveType.Sphere, new Vector3(-2.4f, -0.4f, 0.4f), Vector3.zero, Lit(new Color(0.75f, 0.6f, 0.4f), 0.4f)).transform;
            }
        }

        float accTx, accCa, accPostNa, accPostCl, accKglia, accLac, accGlc;
        void TickSynapse(Dictionary<string, object> now, float dt)
        {
            bool glia = id == "neuron-glia";
            float vpre = Raw(now, "vpre"); if (float.IsNaN(vpre)) vpre = -65;
            float v = Raw(now, "v"); if (float.IsNaN(v)) v = -65;
            boutonMat.SetColor("_Color", Color.Lerp(new Color(1f, 0.7f, 0.5f), new Color(1f, 0.5f, 0.15f), Mathf.InverseLerp(-65f, 10f, vpre)));
            postMat.SetColor("_EmissionColor", Potential(Mathf.InverseLerp(-75f, 0f, v)));
            float ready = F(now, "ready", 0, 100, glia ? 0.8f : 0.9f);
            int shown = Mathf.RoundToInt(ready * vesicles.Count);
            for (int i = 0; i < vesicles.Count; i++) vesicles[i].SetActive(i < shown);
            float ca = glia ? 0 : F(now, "ca", 0.1f, 20f);
            foreach (var m in caCh) m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.2f) * (0.1f + ca * 2.5f));
            float ampa = F(now, "ampa", 0, 10), nmda = F(now, "nmda", 0, 8), inh = F(now, "inh", 0, 30);
            foreach (var (m, t) in receptors) m.SetColor("_EmissionColor", (t == 0 ? new Color(1f, 0.4f, 0.3f) * ampa : t == 1 ? new Color(0.35f, 0.5f, 1f) * nmda : new Color(0.35f, 0.9f, 0.45f) * inh) * 2.5f);
            // a presynaptic spike releases a burst of transmitter
            if (lastVpre < -20 && vpre >= -20) for (int i = 0; i < 25; i++) Spawn(dots, new Vector3(Random.Range(-0.8f, 0.8f), 0.6f, Random.Range(-0.4f, 0.4f)), new Vector3(Random.Range(-0.15f, 0.15f), -0.8f, 0), new Color(1f, 0.95f, 0.4f, 0.95f), 0.08f, 0.9f);
            lastVpre = vpre;
            float cleft = glia ? F(now, "glu", 0, 12) : F(now, "cleft", 0, 60);
            for (int i = Due(ref accTx, cleft * 60f + (glia ? 3f : 0f), dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-0.9f, 0.9f), 0.55f, Random.Range(-0.4f, 0.4f)), new Vector3(Random.Range(-0.1f, 0.1f), -0.8f, 0), new Color(1f, 0.95f, 0.4f, 0.95f), 0.08f, 0.9f);
            for (int i = Due(ref accCa, ca * 40f, dt); i > 0; i--) Spawn(dots, new Vector3(-0.75f + Random.Range(0, 4) * 0.5f, 0.3f, 0.2f), Vector3.up * 0.9f, CaCol, 0.07f, 0.6f);
            for (int i = Due(ref accPostNa, ampa * 40f + nmda * 20f, dt); i > 0; i--) Spawn(dots, new Vector3(-1.0f + Random.Range(0, 6) * 0.4f, 0.05f, 0), Vector3.down * 0.9f, NaCol, 0.07f, 0.9f);
            for (int i = Due(ref accPostCl, inh * 40f, dt); i > 0; i--) Spawn(dots, new Vector3(-0.2f + Random.Range(0, 2) * 1.2f, 0.05f, 0), Vector3.down * 0.9f, ClCol, 0.07f, 0.9f);
            if (glia)
            {
                // K+ and glutamate taken up by the astrocyte, lactate shuttled to the neuron, glucose from the blood
                float ke = F(now, "ke", 3f, 7f), lac = F(now, "lactate", 0.3f, 1.6f), cbf = F(now, "cbf", 10f, 75f, 0.6f);
                astroMat.SetColor("_EmissionColor", new Color(0.4f, 0.6f, 1f) * (0.15f + ke * 1.6f));
                float oed = F(now, "oedema", 0, 6); astro.localScale = Vector3.one * (1.1f + oed * 0.5f);
                microglia.localScale = Vector3.one * (0.9f * F(now, "microglia", 0, 100));
                for (int i = Due(ref accKglia, 2f + ke * 30f, dt); i > 0; i--) Spawn(dots, new Vector3(0.9f, Random.Range(-0.1f, 0.5f), Random.Range(-0.3f, 0.3f)), new Vector3(1.2f, 0, 0), KCol, 0.08f, 1.2f);
                for (int i = Due(ref accLac, 1f + lac * 15f, dt); i > 0; i--) Spawn(dots, new Vector3(2.0f, 0.7f, Random.Range(-0.3f, 0.3f)), new Vector3(-1.0f, 0.55f, 0), new Color(1f, 0.6f, 0.2f, 0.95f), 0.08f, 1.3f);
                for (int i = Due(ref accGlc, 1f + cbf * 12f, dt); i > 0; i--) Spawn(dots, new Vector3(2.9f + Random.Range(-0.2f, 0.2f), -2.0f, Random.Range(-0.8f, 0.8f)), new Vector3(-0.15f, 1.0f, 0), new Color(0.6f, 1f, 0.6f, 0.9f), 0.07f, 1.6f);
            }
        }

        // ============================================================ sarcomere
        readonly List<Transform> zLeft = new List<Transform>(), zRight = new List<Transform>();   // Z discs with the thin filaments fixed to them
        Material zMat, headMat; readonly List<Transform> heads = new List<Transform>();
        float half = 1.8f;

        void BuildSarcomere()
        {
            zMat = Lit(new Color(0.45f, 0.4f, 0.6f), 0.6f);
            var thick = Lit(new Color(0.7f, 0.25f, 0.3f), 0.6f); var thin = Lit(new Color(0.95f, 0.85f, 0.7f), 0.5f);
            headMat = Lit(new Color(0.9f, 0.35f, 0.4f), 0.6f);
            var mline = Glass(new Color(0.8f, 0.6f, 0.9f), 0.05f, 0.4f);
            Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.04f, 2.1f, 2.1f), mline);
            foreach (int side in new[] { -1, 1 })
            {
                var z = new GameObject(side < 0 ? "Z left" : "Z right").transform; z.SetParent(transform, false);
                Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 2.4f, 2.4f), zMat, z);
                // thin filaments: from the Z disc towards the centre, between the thick ones
                for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
                    {
                        var g = RodX(new Vector3(-side * 0.55f, -0.9f + a * 0.6f, -0.9f + b * 0.6f), 1.1f, 0.035f, thin, z);
                        g.name = "actin";
                    }
                (side < 0 ? zLeft : zRight).Add(z);
            }
            // thick filaments (myosin) with their heads (cross-bridges) on both halves, bare zone in the middle
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
                {
                    var c = new Vector3(0, -0.6f + a * 0.6f, -0.6f + b * 0.6f);
                    RodX(c, 1.45f, 0.08f, thick);
                    for (int k = 0; k < 12; k++)
                    {
                        float x = (k < 6 ? -1 : 1) * (0.18f + (k % 6) * 0.1f);
                        float ang = k * 2.1f;
                        var h = Prim(PrimitiveType.Sphere, c + new Vector3(x, Mathf.Cos(ang) * 0.12f, Mathf.Sin(ang) * 0.12f), Vector3.one * 0.07f, headMat);
                        heads.Add(h.transform);
                    }
                }
        }

        float accSr, strokeT;
        void TickSarcomere(Dictionary<string, object> now, float dt)
        {
            float len = Raw(now, "len"); if (float.IsNaN(len)) len = 2.1f;
            float f = F(now, "f", 0, 100), xb = F(now, "xb", 0, 100), ca = F(now, "ca", -7f, -4.7f), dep = Mathf.InverseLerp(-90f, 20f, Raw(now, "v"));
            // sarcomere length in µm drawn at ~0.85 units per µm; force pulls the Z discs in a little
            float target = len * 0.85f * (1f - 0.12f * f);
            half = Mathf.Lerp(half, target, Mathf.Clamp01(dt * 8f));
            foreach (var z in zLeft) z.localPosition = new Vector3(-half, 0, 0);
            foreach (var z in zRight) z.localPosition = new Vector3(half, 0, 0);
            zMat.SetColor("_EmissionColor", Potential(dep));
            headMat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.3f) * xb * 2.2f);
            // cross-bridges cycle: the heads tilt back and forth faster with more bound bridges
            strokeT += dt * (2f + xb * 12f);
            for (int i = 0; i < heads.Count; i++)
            {
                var p = heads[i].localPosition; float baseX = (i % 12 < 6 ? -1 : 1) * (0.18f + (i % 6) * 0.1f);
                p.x = baseX + Mathf.Sin(strokeT + i) * 0.035f * xb * (i % 12 < 6 ? -1 : 1); heads[i].localPosition = p;
            }
            // Ca2+ from the sarcoplasmic reticulum around the filaments
            for (int i = Due(ref accSr, ca * 60f, dt); i > 0; i--)
            {
                var p = new Vector3(Random.Range(-half, half), Random.Range(-1.1f, 1.1f), Random.Range(-1.1f, 1.1f));
                Spawn(dots, p, Random.insideUnitSphere * 0.2f, CaCol, 0.06f, 0.8f);
            }
        }

        // ============================================================ smooth muscle cell
        Transform cell; Material cellMat, bundleMat;
        void BuildSmooth()
        {
            cell = new GameObject("Cell").transform; cell.SetParent(transform, false);
            cellMat = Glass(new Color(0.95f, 0.55f, 0.55f), 0.08f, 0.6f);
            Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(5.2f, 1.3f, 1.3f), cellMat, cell);
            Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(1.3f, 0.45f, 0.45f), Lit(new Color(0.55f, 0.35f, 0.75f), 0.5f), cell);
            var dense = Lit(new Color(0.3f, 0.2f, 0.25f), 0.4f); bundleMat = Lit(new Color(0.85f, 0.5f, 0.45f), 0.6f);
            var pts = new List<Vector3>();
            for (int i = 0; i < 22; i++)
            {
                float x = Random.Range(-2.2f, 2.2f); float rmax = 0.55f * Mathf.Sqrt(Mathf.Max(0.05f, 1 - (x / 2.6f) * (x / 2.6f)));
                var r = Random.insideUnitCircle.normalized * rmax * Random.Range(0.6f, 0.95f);
                var p = new Vector3(x, r.x, r.y); pts.Add(p);
                Prim(PrimitiveType.Sphere, p, Vector3.one * 0.1f, dense, cell);
            }
            // actin–myosin bundles run obliquely between dense bodies
            for (int i = 0; i < pts.Count; i++)
            {
                int best = -1; float bd = 9;
                for (int j = 0; j < pts.Count; j++) { float d = pts[j].x - pts[i].x; if (d > 0.4f && d < bd && Vector3.Distance(pts[i], pts[j]) < 1.4f) { bd = d; best = j; } }
                if (best >= 0) Rod(pts[i], pts[best], 0.025f, bundleMat, cell);
            }
        }

        float accCaS;
        void TickSmooth(Dictionary<string, object> now, float dt)
        {
            float f = F(now, "f", 0, 90), ca = F(now, "ca", 0.1f, 0.5f), latch = F(now, "latch", 0, 36), dep = Mathf.InverseLerp(-65f, -30f, Raw(now, "v"));
            var s = cell.localScale; float tx = 1f - 0.32f * f, ty = 1f / Mathf.Sqrt(tx);
            cell.localScale = Vector3.Lerp(s, new Vector3(tx, ty, ty), Mathf.Clamp01(dt * 4f));
            cellMat.SetColor("_Color", Color.Lerp(new Color(0.95f, 0.55f, 0.55f), new Color(1f, 0.55f, 0.2f), dep));
            // bundles: warm while cross-bridges cycle, blue-ish when held in the latch state
            bundleMat.SetColor("_EmissionColor", Color.Lerp(new Color(1f, 0.45f, 0.3f), new Color(0.4f, 0.6f, 1f), latch / Mathf.Max(0.05f, latch + f)) * (0.1f + f * 2f));
            for (int i = Due(ref accCaS, ca * 50f, dt); i > 0; i--)
            {
                float x = Random.Range(-2f, 2f); var r = Random.insideUnitCircle * 0.45f;
                Spawn(dots, Vector3.Scale(new Vector3(x, r.x, r.y), cell.localScale), Random.insideUnitSphere * 0.15f, CaCol, 0.07f, 0.9f);
            }
        }

        // ============================================================ nephron
        Material cellsMat, tuftMat, capMat;
        void BuildNephron()
        {
            // Bowman's capsule with the glomerular tuft, afferent and efferent arterioles
            Prim(PrimitiveType.Sphere, new Vector3(-2.4f, 0, 0), Vector3.one * 1.7f, Glass(new Color(0.6f, 0.8f, 1f), 0.05f, 0.45f));
            tuftMat = Lit(new Color(0.8f, 0.15f, 0.15f), 0.6f);
            for (int i = 0; i < 26; i++) Prim(PrimitiveType.Sphere, new Vector3(-2.4f, 0.05f, 0) + Random.insideUnitSphere * 0.45f, Vector3.one * 0.24f, tuftMat);
            var art = Lit(new Color(0.8f, 0.15f, 0.15f), 0.6f);
            Rod(new Vector3(-2.55f, 0.4f, 0.1f), new Vector3(-2.9f, 2.0f, 0.1f), 0.12f, art);
            Rod(new Vector3(-2.25f, 0.4f, -0.1f), new Vector3(-1.8f, 1.6f, -0.1f), 0.09f, art);
            // the tubule leaving the capsule, its epithelium and the peritubular capillary above it
            RodX(new Vector3(0.7f, 0, 0), 4.2f, 0.5f, Glass(new Color(1f, 0.9f, 0.55f), 0.06f, 0.5f));
            cellsMat = Lit(new Color(0.85f, 0.7f, 0.6f), 0.5f);
            for (int i = 0; i < 9; i++)
            {
                Prim(PrimitiveType.Cube, new Vector3(-1.05f + i * 0.42f, 0.62f, 0), new Vector3(0.38f, 0.3f, 0.7f), cellsMat);
                Prim(PrimitiveType.Cube, new Vector3(-1.05f + i * 0.42f, -0.62f, 0), new Vector3(0.38f, 0.3f, 0.7f), cellsMat);
            }
            capMat = Glass(new Color(1f, 0.4f, 0.4f), 0.08f, 0.6f);
            RodX(new Vector3(0.9f, 1.3f, 0), 4.0f, 0.3f, capMat);
            rbcs = NewLayer(DiscMesh(), Lit(new Color(0.75f, 0.08f, 0.08f), 0.7f));
        }

        float accF, accR, accS, accU, accRbcN, accPara, accW;
        void TickNephron(Dictionary<string, object> now, float dt)
        {
            float filt, reab, sec = 0, urine, cellNa = 0.4f, flow = 0.6f;
            Color fCol = new Color(0.6f, 0.85f, 1f, 0.9f), rCol = new Color(0.6f, 0.85f, 1f, 0.9f);
            switch (id)
            {
                case "clearance":
                    {
                        // glucose (green): filtered, taken back by SGLT up to Tm, the rest in the urine; PAH (violet) secreted from the capillary
                        float gF = Raw(now, "gF"), gR = Raw(now, "gR"), gE = Raw(now, "gE");
                        filt = float.IsNaN(gF) ? 0.3f : Mathf.Clamp01(gF / 3.8f); reab = float.IsNaN(gR) ? 0.3f : Mathf.Clamp01(gR / 3.8f);
                        urine = float.IsNaN(gE) ? 0 : Mathf.Clamp01(gE / 3.8f); sec = F(now, "cPah", 150, 800, 0.5f);
                        fCol = rCol = new Color(0.45f, 1f, 0.45f, 0.95f); break;
                    }
                case "epithelium":
                    filt = 0.4f; reab = F(now, "trans", 0, 7.6f); urine = 0.1f; cellNa = F(now, "c", 0, 140);
                    sec = 0; break;
                default:   // kidney-filtration
                    filt = F(now, "gfr", 0, 200, 0.6f); urine = F(now, "urine", 0, 7); reab = Mathf.Max(0, filt - urine * 0.3f);
                    flow = F(now, "rbf", 200, 1200, 0.8f); break;
            }
            cellsMat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.3f) * cellNa * 0.8f);
            tuftMat.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.2f) * filt * 0.8f);
            // filtrate leaves the tuft into the capsule and runs down the tubule
            for (int i = Due(ref accF, 3f + filt * 45f, dt); i > 0; i--)
            {
                if (Random.value < 0.4f) Spawn(dots, new Vector3(-2.4f, 0.05f, 0) + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 0.5f, fCol, 0.08f, 0.6f);
                else { var r = Random.insideUnitCircle * 0.32f; Spawn(dots, new Vector3(-1.4f, r.x, r.y), new Vector3(1.3f, 0, 0), fCol, 0.08f, Random.Range(1.0f, 3.2f)); }
            }
            // reabsorption: through the cells up into the capillary
            for (int i = Due(ref accR, reab * 45f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.1f, 2.4f), 0.35f, Random.Range(-0.2f, 0.2f)), new Vector3(0, 0.9f, 0), rCol, 0.08f, 1.0f);
            if (id == "epithelium")
            {
                // paracellular route between the cells and water following the salt
                float para = F(now, "para", 0, 4.5f), water = F(now, "water", 0, 32);
                for (int i = Due(ref accPara, para * 30f, dt); i > 0; i--) Spawn(dots, new Vector3(-0.84f + Random.Range(0, 8) * 0.42f, 0.35f, 0), new Vector3(0, 0.9f, 0), ClCol, 0.07f, 1.0f);
                for (int i = Due(ref accW, water * 1.2f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.1f, 2.4f), 0.35f, Random.Range(-0.2f, 0.2f)), new Vector3(0, 0.8f, 0), new Color(0.85f, 0.95f, 1f, 0.6f), 0.06f, 1.1f);
            }
            // secretion from the capillary into the tubule (PAH)
            for (int i = Due(ref accS, sec * 30f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-0.6f, 2.6f), 1.05f, Random.Range(-0.2f, 0.2f)), new Vector3(0, -0.9f, 0), new Color(0.75f, 0.45f, 1f, 0.95f), 0.08f, 1.0f);
            // what stays in the tubule leaves as urine
            for (int i = Due(ref accU, urine * 30f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.3f; Spawn(dots, new Vector3(2.2f, r.x, r.y), new Vector3(1.0f, 0, 0), id == "clearance" ? fCol : new Color(1f, 0.9f, 0.3f, 0.95f), 0.09f, 1.0f); }
            for (int i = Due(ref accRbcN, 6f + flow * 20f, dt); i > 0; i--) { var r = Random.insideUnitCircle * 0.22f; Spawn(rbcs, new Vector3(-1.1f, 1.3f + r.x, r.y), new Vector3(1.4f, 0, 0), Color.white, 0.14f, 4f / 1.4f, Random.Range(0, 1000)); }
        }

        // ============================================================ skin
        Material epiMat, vesselSkinMat, merkelMat, meissMat, pacMat, ruffMat, coldMat, warmMat, painMat, probeMat;
        Transform probe;
        void BuildSkin()
        {
            float w = 4.6f, d = 2.2f;
            epiMat = Lit(new Color(0.93f, 0.76f, 0.65f), 0.35f);
            Prim(PrimitiveType.Cube, new Vector3(0, 0.88f, 0), new Vector3(w, 0.24f, d), epiMat);
            Prim(PrimitiveType.Cube, new Vector3(0, 0.25f, 0), new Vector3(w, 1.02f, d), Glass(new Color(1f, 0.6f, 0.6f), 0.04f, 0.4f));
            Prim(PrimitiveType.Cube, new Vector3(0, -0.75f, 0), new Vector3(w, 0.98f, d), Glass(new Color(1f, 0.88f, 0.5f), 0.04f, 0.4f));
            // receptors at their depths: Merkel discs at the base of the epidermis, Meissner corpuscles in the papillae,
            // Ruffini endings in the dermis, Pacinian corpuscles deep, free nerve endings (cold, warm, pain) up into the epidermis
            merkelMat = Lit(new Color(0.95f, 0.8f, 0.3f), 0.6f); meissMat = Lit(new Color(0.9f, 0.5f, 0.7f), 0.6f);
            pacMat = Lit(new Color(0.75f, 0.85f, 0.95f), 0.6f); ruffMat = Lit(new Color(0.6f, 0.9f, 0.6f), 0.6f);
            coldMat = Lit(new Color(0.4f, 0.6f, 1f), 0.6f); warmMat = Lit(new Color(1f, 0.55f, 0.3f), 0.6f); painMat = Lit(new Color(1f, 0.85f, 0.85f), 0.6f);
            var nerve = Lit(new Color(0.95f, 0.9f, 0.6f), 0.5f);
            RodX(new Vector3(0, -0.25f, 0.3f), w * 0.95f, 0.05f, nerve);
            void Ending(Vector3 top, Material m) { Rod(new Vector3(top.x, -0.25f, 0.3f), top, 0.025f, m); }
            foreach (var x in new[] { -1.9f, -1.6f }) { Prim(PrimitiveType.Sphere, new Vector3(x, 0.74f, 0.3f), new Vector3(0.16f, 0.05f, 0.16f), merkelMat); Ending(new Vector3(x, 0.72f, 0.3f), merkelMat); }
            foreach (var x in new[] { -0.9f, 0.6f }) { Prim(PrimitiveType.Sphere, new Vector3(x, 0.56f, 0.3f), new Vector3(0.14f, 0.3f, 0.14f), meissMat); Ending(new Vector3(x, 0.42f, 0.3f), meissMat); }
            Prim(PrimitiveType.Sphere, new Vector3(-0.2f, 0.15f, 0.3f), new Vector3(0.42f, 0.1f, 0.1f), ruffMat); Ending(new Vector3(-0.2f, 0.12f, 0.3f), ruffMat);
            Prim(PrimitiveType.Sphere, new Vector3(1.6f, -0.75f, 0.3f), new Vector3(0.36f, 0.55f, 0.36f), pacMat); Rod(new Vector3(1.6f, -0.25f, 0.3f), new Vector3(1.6f, -0.5f, 0.3f), 0.025f, pacMat);
            Ending(new Vector3(1.1f, 0.92f, 0.3f), coldMat); Ending(new Vector3(1.3f, 0.92f, 0.3f), warmMat); Ending(new Vector3(2.0f, 0.95f, 0.3f), painMat); Ending(new Vector3(-1.25f, 0.95f, 0.3f), painMat);
            // dermal vessel (blood flow) and a sweat gland with its duct
            vesselSkinMat = Lit(new Color(0.75f, 0.12f, 0.12f), 0.6f);
            RodX(new Vector3(0, 0.05f, -0.45f), w * 0.95f, 0.09f, vesselSkinMat);
            var gland = Lit(new Color(0.75f, 0.85f, 0.95f), 0.6f);
            for (int i = 0; i < 10; i++) Prim(PrimitiveType.Sphere, new Vector3(-0.9f, -0.45f, -0.5f) + Random.insideUnitSphere * 0.18f, Vector3.one * 0.13f, gland);
            Rod(new Vector3(-0.9f, -0.4f, -0.5f), new Vector3(-0.9f, 1.0f, -0.5f), 0.035f, gland);
            if (id == "touch")
            {
                probeMat = Lit(new Color(0.8f, 0.8f, 0.85f), 0.8f);
                probe = Prim(PrimitiveType.Capsule, new Vector3(-1.0f, 1.9f, 0.3f), new Vector3(0.25f, 0.5f, 0.25f), probeMat).transform;
            }
        }

        float accSpike, accSweat, accUv;
        void TickSkin(Dictionary<string, object> now, float dt)
        {
            if (id == "touch")
            {
                float merkel = F(now, "merkel", 5, 68), meiss = F(now, "meissner", 0, 94), pac = F(now, "pacinian", 0, 50), ruff = F(now, "ruffini", 5, 39);
                float cold = F(now, "cold", 0, 8.3f), warm = F(now, "warm", 0, 7.5f), pain = Mathf.Max(F(now, "adelta", 0, 58), F(now, "cfib", 0, 53));
                merkelMat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * merkel * 2.5f);
                meissMat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.75f) * meiss * 2.5f);
                pacMat.SetColor("_EmissionColor", new Color(0.6f, 0.8f, 1f) * pac * 2.5f);
                ruffMat.SetColor("_EmissionColor", new Color(0.5f, 1f, 0.5f) * ruff * 2.5f);
                coldMat.SetColor("_EmissionColor", new Color(0.4f, 0.6f, 1f) * cold * 3f);
                warmMat.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.3f) * warm * 3f);
                painMat.SetColor("_EmissionColor", new Color(1f, 0.2f, 0.2f) * pain * 3f);
                // the probe presses into the skin with the mechanical stimulus; it is tinted by a thermal stimulus
                float press = Mathf.Max(merkel, Mathf.Max(meiss, pac)), vib = Mathf.Max(meiss, pac);
                var p = probe.localPosition; p.y = Mathf.Lerp(p.y, 1.62f - press * 0.32f + Mathf.Sin(Time.time * 40f) * 0.03f * vib, Mathf.Clamp01(dt * 10f)); probe.localPosition = p;
                probeMat.SetColor("_EmissionColor", new Color(0.4f, 0.6f, 1f) * cold * 1.2f + new Color(1f, 0.4f, 0.2f) * warm * 1.2f);
                // impulses run along the nerve towards the spinal cord
                float total = merkel + meiss + pac + ruff + cold + warm + pain;
                for (int i = Due(ref accSpike, total * 18f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-1.9f, 2.0f), -0.25f, 0.3f), new Vector3(-1.6f, 0, 0), Excited, 0.08f, 1.2f);
                return;
            }
            // skin: blood flow, sweating, UV and pigment
            float flow = F(now, "flow", 0, 7.5f, 0.1f), sweat = F(now, "sweat", 0, 2), uv = F(now, "uv", 0, 8), ery = F(now, "ery", 0, 150), mel = F(now, "mel", 0, 6, 0.13f);
            vesselSkinMat.SetColor("_EmissionColor", new Color(1f, 0.2f, 0.15f) * (0.1f + flow * 1.5f));
            var light = new Color(0.95f, 0.8f, 0.7f); var dark = new Color(0.32f, 0.2f, 0.13f);
            var skin = Color.Lerp(light, dark, Mathf.Sqrt(mel)); skin = Color.Lerp(skin, new Color(0.95f, 0.35f, 0.3f), ery * 0.6f);
            epiMat.SetColor("_BaseColor", skin);
            for (int i = Due(ref accSweat, sweat * 25f, dt); i > 0; i--)
            {
                if (Random.value < 0.4f) Spawn(dots, new Vector3(-0.9f, Random.Range(-0.3f, 0.9f), -0.5f), new Vector3(0, 0.7f, 0), new Color(0.7f, 0.9f, 1f, 0.9f), 0.08f, 0.6f);
                else Spawn(dots, new Vector3(Random.Range(-2.1f, 2.1f), 1.03f, Random.Range(-0.9f, 0.9f)), new Vector3(0, 0.05f, 0), new Color(0.7f, 0.9f, 1f, 0.9f), 0.11f, 1.4f);
            }
            for (int i = Due(ref accUv, uv * 30f, dt); i > 0; i--) Spawn(dots, new Vector3(Random.Range(-2.1f, 2.1f), 2.6f, Random.Range(-0.9f, 0.9f)), new Vector3(0, -2.2f, 0), new Color(0.7f, 0.4f, 1f, 0.9f), 0.07f, Random.Range(0.7f, 0.95f));
        }
    }
}
