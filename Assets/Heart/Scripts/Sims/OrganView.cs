using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>A static organ group (Z-Anatomy, CC BY-SA 4.0) for the simulations: brain, kidneys, abdomen, neck, arm.
    /// Loads a packed model (HRT2) and colours it by tissue; an optional "Contract" shape follows muscle activity.</summary>
    public class OrganView : MonoBehaviour
    {
        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            {"cortex", new Color(0.86f, 0.70f, 0.68f)}, {"cerebellum", new Color(0.80f, 0.60f, 0.58f)}, {"brainstem", new Color(0.88f, 0.76f, 0.68f)},
            {"deep", new Color(0.92f, 0.82f, 0.72f)}, {"kidney", new Color(0.55f, 0.17f, 0.14f)}, {"ureter", new Color(0.86f, 0.76f, 0.62f)},
            {"bladder", new Color(0.82f, 0.60f, 0.55f)}, {"artery", new Color(0.72f, 0.11f, 0.10f)}, {"vein", new Color(0.16f, 0.24f, 0.62f)},
            {"gland", new Color(0.86f, 0.66f, 0.32f)}, {"liver", new Color(0.46f, 0.14f, 0.10f)}, {"gallbladder", new Color(0.32f, 0.50f, 0.22f)},
            {"stomach", new Color(0.82f, 0.52f, 0.46f)}, {"pancreas", new Color(0.90f, 0.76f, 0.56f)}, {"spleen", new Color(0.46f, 0.12f, 0.20f)},
            {"parathyroid", new Color(0.82f, 0.60f, 0.30f)}, {"cartilage", new Color(0.86f, 0.86f, 0.80f)}, {"airway", new Color(0.88f, 0.82f, 0.72f)},
            {"bone", new Color(0.90f, 0.87f, 0.80f)}, {"muscle", new Color(0.62f, 0.13f, 0.13f)}, {"muscle2", new Color(0.55f, 0.12f, 0.12f)}, {"intestine", new Color(0.84f, 0.60f, 0.55f)},
        };

        readonly List<(SkinnedMeshRenderer r, int key)> contract = new List<(SkinnedMeshRenderer, int)>();
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        public Bounds Bounds; public bool Loaded;
        public class Part { public string Name, Cls; public Mesh Mesh; public Material Mat, Glass; public Color Base; }
        public readonly List<Part> Parts = new List<Part>();

        public void Build(byte[] data)
        {
            if (data == null) return;
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "HRT2") return;
                int n = br.ReadInt32(); bool first = true;
                for (int i = 0; i < n; i++)
                {
                    var (mesh, cls) = ReadPart(br);
                    var go = new GameObject(mesh.name); go.transform.SetParent(transform, false);
                    int k = mesh.GetBlendShapeIndex("Contract");
                    if (mesh.blendShapeCount > 0)
                    {
                        var smr = go.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = mesh;
                        var b = mesh.bounds; b.Expand(0.3f); smr.localBounds = b;
                        smr.sharedMaterial = PartMat(mesh, cls);
                        if (k >= 0) contract.Add((smr, k));
                    }
                    else
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = PartMat(mesh, cls);
                    }
                    if (first) { Bounds = mesh.bounds; first = false; } else Bounds.Encapsulate(mesh.bounds);
                }
            }
            Loaded = true;
        }

        /// <summary>Each part gets its own emission-capable material so it can light up with the physiology.</summary>
        Material PartMat(Mesh mesh, string cls)
        {
            var m = new Material(Resources.Load<Material>("Materials/Highlight"));
            var c = Colors.TryGetValue(cls, out var cc) ? cc : Color.gray;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", cls == "bone" || cls == "cartilage" ? 0.35f : 0.55f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            Parts.Add(new Part { Name = mesh.name, Cls = cls, Mesh = mesh, Mat = m, Base = c });
            return m;
        }

        /// <summary>Light up the parts whose name contains any of the keys (intensity 0..1).</summary>
        public void Glow(string[] keys, Color color, float intensity)
        {
            foreach (var p in Parts)
            {
                bool hit = false;
                foreach (var k in keys) if (p.Name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0 || p.Cls == k) { hit = true; break; }
                if (hit) p.Mat.SetColor("_EmissionColor", color * Mathf.Clamp01(intensity) * 3.2f);
            }
        }

        public void Tint(string[] keys, Color target, float k)
        {
            foreach (var p in Parts)
                foreach (var key in keys)
                    if (p.Name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0 || p.Cls == key) { p.Mat.SetColor("_BaseColor", Color.Lerp(p.Base, target, Mathf.Clamp01(k))); break; }
        }

        // ---- flow of droplets along tube-like parts (urine in the ureters)
        readonly List<List<Vector3>> flowPaths = new List<List<Vector3>>();
        ParticleSystem flowPs; ParticleSystem.Particle[] flowBuf = new ParticleSystem.Particle[400];
        readonly List<(int path, float s)> drops = new List<(int, float)>();

        public void AddFlow(string key, Color color)
        {
            foreach (var p in Parts)
            {
                if (p.Name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var v = new List<Vector3>(p.Mesh.vertices);
                v.Sort((a, b) => b.y.CompareTo(a.y));
                var path = new List<Vector3>();
                int bins = 24; float top = v[0].y, bot = v[v.Count - 1].y;
                for (int i = 0; i < bins; i++)
                {
                    float y0 = Mathf.Lerp(top, bot, i / (float)bins), y1 = Mathf.Lerp(top, bot, (i + 1) / (float)bins);
                    Vector3 sum = Vector3.zero; int n = 0;
                    foreach (var q in v) if (q.y <= y0 && q.y >= y1) { sum += q; n++; }
                    if (n > 0) path.Add(sum / n);
                }
                if (path.Count > 1) flowPaths.Add(path);
            }
            if (flowPaths.Count == 0) return;
            var go = new GameObject("Flow"); go.transform.SetParent(transform, false);
            flowPs = go.AddComponent<ParticleSystem>(); flowPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flowPs.main; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 400; main.playOnAwake = false; main.startLifetime = 1000f;
            var em = flowPs.emission; em.enabled = false; var sh = flowPs.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = new Material(Resources.Load<Material>("Materials/Blood"));
            var tex = new Texture2D(16, 16); for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { float dx = (x - 7.5f) / 8f, dy = (y - 7.5f) / 8f; tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)))); }
            tex.Apply(); r.sharedMaterial.SetTexture("_BaseMap", tex);
            flowColor = color; flowPs.Play();
        }
        Color flowColor;

        /// <param name="rate01">0..1 flow</param>
        public void TickFlow(float dt, float rate01)
        {
            if (flowPs == null) return;
            if (Random.value < rate01 * dt * 25f * flowPaths.Count && drops.Count < 380) drops.Add((Random.Range(0, flowPaths.Count), 0f));
            int n = 0;
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i]; d.s += dt * (0.25f + rate01 * 0.6f);
                if (d.s >= 1f) { drops.RemoveAt(i); continue; }
                drops[i] = d;
            }
            foreach (var d in drops)
            {
                var path = flowPaths[d.path]; float f = d.s * (path.Count - 1); int a = Mathf.Min((int)f, path.Count - 2);
                flowBuf[n].position = Vector3.Lerp(path[a], path[a + 1], f - a);
                flowBuf[n].startSize = 0.085f; flowBuf[n].startColor = flowColor; flowBuf[n].remainingLifetime = 100; flowBuf[n].startLifetime = 1000; n++;
            }
            flowPs.SetParticles(flowBuf, n);
        }

        Material Mat(string cls)
        {
            if (mats.TryGetValue(cls, out var m)) return m;
            m = new Material(Resources.Load<Material>("Materials/Lit"));
            m.SetColor("_BaseColor", Colors.TryGetValue(cls, out var c) ? c : Color.gray);
            m.SetFloat("_Smoothness", cls == "bone" || cls == "cartilage" ? 0.35f : 0.55f);
            m.SetFloat("_Metallic", 0f);
            mats[cls] = m; return m;
        }

        static string Str(BinaryReader br) { int n = br.ReadInt32(); return Encoding.UTF8.GetString(br.ReadBytes(n)); }
        static short[] I16(BinaryReader br, int n) { var b = br.ReadBytes(n * 2); var a = new short[n]; System.Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }

        static (Mesh, string) ReadPart(BinaryReader br)
        {
            string name = Str(br), cls = Str(br);
            int nv = br.ReadInt32(), nt = br.ReadInt32();
            var mn = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var mx = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var r = mx - mn; var qp = I16(br, nv * 3); var pos = new Vector3[nv];
            for (int i = 0; i < nv; i++) pos[i] = new Vector3(mn.x + (qp[i * 3] + 32768) / 65535f * r.x, mn.y + (qp[i * 3 + 1] + 32768) / 65535f * r.y, mn.z + (qp[i * 3 + 2] + 32768) / 65535f * r.z);
            var qn = br.ReadBytes(nv * 3); var nrm = new Vector3[nv];
            for (int i = 0; i < nv; i++) nrm[i] = new Vector3((sbyte)qn[i * 3], (sbyte)qn[i * 3 + 1], (sbyte)qn[i * 3 + 2]).normalized;
            int isz = br.ReadByte(); var idx = new int[nt * 3];
            if (isz == 2) { var ib = br.ReadBytes(nt * 6); for (int i = 0; i < idx.Length; i++) idx[i] = ib[i * 2] | (ib[i * 2 + 1] << 8); }
            else { var ib = br.ReadBytes(nt * 12); System.Buffer.BlockCopy(ib, 0, idx, 0, ib.Length); }
            var mesh = new Mesh { name = name };
            if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = pos; mesh.normals = nrm; mesh.triangles = idx;
            int nk = br.ReadInt32();
            for (int k = 0; k < nk; k++)
            {
                string kn = Str(br); float sp = br.ReadSingle() / 32767f, sn = br.ReadSingle() / 32767f;
                var qa = I16(br, nv * 3); var qb = I16(br, nv * 3);
                var dp = new Vector3[nv]; var dn = new Vector3[nv];
                for (int i = 0; i < nv; i++) { dp[i] = new Vector3(qa[i * 3], qa[i * 3 + 1], qa[i * 3 + 2]) * sp; dn[i] = new Vector3(qb[i * 3], qb[i * 3 + 1], qb[i * 3 + 2]) * sn; }
                mesh.AddBlendShapeFrame(kn, 100f, dp, dn, null);
            }
            mesh.RecalculateBounds();
            return (mesh, cls);
        }

        /// <summary>Opacity 0.01–1 (slider 1–100): 1 = solid organs, lower = X-ray glass.</summary>
        public void SetOpacity(float o)
        {
            o = Mathf.Clamp(o, 0.01f, 1f);
            foreach (Transform ch in transform)
            {
                var r = ch.GetComponent<Renderer>(); var p = Parts.Find(x => x.Name == ch.name);
                if (r == null || p == null) continue;
                if (o >= 0.995f) { r.sharedMaterial = p.Mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; continue; }
                if (p.Glass == null) p.Glass = MedAtlas.Heart.Opacity.Glass(Color.Lerp(p.Base, Color.white, 0.25f));
                MedAtlas.Heart.Opacity.Apply(p.Glass, o);
                r.sharedMaterial = p.Glass; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public void SetContraction(float a01)
        {
            foreach (var c in contract) c.r.SetBlendShapeWeight(c.key, Mathf.Clamp01(a01) * 100f);
        }
    }
}
