using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MedAtlas.Heart
{
    /// <summary>Loads the anatomical heart (BodyParts3D derived, see Credits) from Resources/heart_model.bytes,
    /// creates materials and drives the physiological blend shapes from the CardiacModel.</summary>
    public class HeartModel : MonoBehaviour
    {
        public class Part
        {
            public string Name, Cls, Bg, Latin;
            public Mesh Mesh;
            public SkinnedMeshRenderer Renderer;
            public MeshCollider Collider;
            public int KVent = -1, KAtr = -1, KDist = -1, KOpen = -1;
            public Material Solid, Glass;
            public bool InsideOnly;   // only visible in the internal view
            public bool HideInside;   // hidden in the internal view
        }

        public readonly List<Part> Parts = new List<Part>();
        public Dictionary<string, Vector3> Landmarks = new Dictionary<string, Vector3>();
        public bool InternalView { get; private set; }
        public Bounds Bounds;

        static readonly Dictionary<string, Color> SolidColors = new Dictionary<string, Color>
        {
            {"myocardium", new Color(0.34f, 0.050f, 0.055f)}, {"atrium", new Color(0.42f, 0.095f, 0.10f)},
            {"aorta", new Color(0.52f, 0.075f, 0.07f)}, {"pulmArtery", new Color(0.10f, 0.17f, 0.50f)},
            {"pulmVein", new Color(0.50f, 0.10f, 0.10f)}, {"cava", new Color(0.11f, 0.16f, 0.48f)},
            {"coronaryArtery", new Color(0.66f, 0.10f, 0.07f)}, {"coronaryVein", new Color(0.14f, 0.20f, 0.60f)},
            {"mitral", new Color(0.95f, 0.84f, 0.76f)}, {"tricuspid", new Color(0.95f, 0.84f, 0.76f)},
            {"aorticValve", new Color(0.96f, 0.86f, 0.78f)}, {"pulmValve", new Color(0.96f, 0.86f, 0.78f)},
            {"papLV", new Color(0.62f, 0.14f, 0.14f)}, {"papRV", new Color(0.62f, 0.14f, 0.14f)},
            {"cavity", new Color(0.7f, 0.05f, 0.08f)},
        };

        Shader litShader, xrayShader;
        readonly Dictionary<string, Material> solidMats = new Dictionary<string, Material>();
        Part highlighted;
        Material highlightMat;

        public void Build()
        {
            litShader = Shader.Find("Universal Render Pipeline/Lit");
            xrayShader = Shader.Find("MedAtlas/XRay");
            var ta = Resources.Load<TextAsset>("heart_model");
            var lm = Resources.Load<TextAsset>("heart_landmarks");
            if (ta == null) { Debug.LogError("heart_model.bytes missing"); return; }
            ParseLandmarks(lm.text);
            using (var br = new BinaryReader(new MemoryStream(ta.bytes)))
            {
                var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (magic != "HRT1") { Debug.LogError("bad heart model"); return; }
                int n = br.ReadInt32();
                bool first = true;
                for (int i = 0; i < n; i++)
                {
                    var p = ReadPart(br);
                    Parts.Add(p);
                    if (first) { Bounds = p.Mesh.bounds; first = false; }
                    else if (p.Cls == "myocardium" || p.Cls == "atrium" || p.Cls == "aorta" || p.Cls == "pulmArtery" || p.Cls == "cava") Bounds.Encapsulate(p.Mesh.bounds);
                }
            }
            var hl = Resources.Load<Material>("Materials/Highlight");
            highlightMat = hl != null ? new Material(hl) : NewLit(new Color(0.95f, 0.75f, 0.3f), 0.6f);
            highlightMat.EnableKeyword("_EMISSION");
            highlightMat.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.05f));
            SetInternalView(false);
        }

        void ParseLandmarks(string json)
        {
            // tiny parser for {"key": [x, y, z], ...}
            int i = 0;
            while (true)
            {
                int q1 = json.IndexOf('"', i); if (q1 < 0) break;
                int q2 = json.IndexOf('"', q1 + 1);
                string key = json.Substring(q1 + 1, q2 - q1 - 1);
                int b1 = json.IndexOf('[', q2), b2 = json.IndexOf(']', b1);
                var parts = json.Substring(b1 + 1, b2 - b1 - 1).Split(',');
                Landmarks[key] = new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                                             float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                                             float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                i = b2 + 1;
            }
        }

        static string ReadStr(BinaryReader br) { int n = br.ReadInt32(); return Encoding.UTF8.GetString(br.ReadBytes(n)); }

        static Vector3[] ReadV3(BinaryReader br, int n)
        {
            var a = new Vector3[n];
            var bytes = br.ReadBytes(n * 12);
            var f = new float[n * 3];
            System.Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
            for (int i = 0; i < n; i++) a[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return a;
        }

        Part ReadPart(BinaryReader br)
        {
            var p = new Part { Name = ReadStr(br), Cls = ReadStr(br) };
            int nv = br.ReadInt32(), nt = br.ReadInt32();
            var pos = ReadV3(br, nv);
            var nrm = ReadV3(br, nv);
            var idxBytes = br.ReadBytes(nt * 3 * 4);
            var idx = new int[nt * 3];
            System.Buffer.BlockCopy(idxBytes, 0, idx, 0, idxBytes.Length);
            var mesh = new Mesh { name = p.Name };
            if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = pos; mesh.normals = nrm; mesh.triangles = idx;
            int nk = br.ReadInt32();
            for (int k = 0; k < nk; k++)
            {
                string kn = ReadStr(br);
                var dp = ReadV3(br, nv); var dn = ReadV3(br, nv);
                mesh.AddBlendShapeFrame(kn, 100f, dp, dn, null);
            }
            mesh.RecalculateBounds();
            p.Mesh = mesh;
            p.KVent = mesh.GetBlendShapeIndex("VentSystole");
            p.KAtr = mesh.GetBlendShapeIndex("AtrialSystole");
            p.KDist = mesh.GetBlendShapeIndex("Distend");
            p.KOpen = mesh.GetBlendShapeIndex("Open");

            var go = new GameObject(p.Name);
            go.transform.SetParent(transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.updateWhenOffscreen = false;
            var b = mesh.bounds; b.Expand(0.2f); smr.localBounds = b;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            p.Renderer = smr;
            if (p.Cls != "cavity")
            {
                p.Collider = go.AddComponent<MeshCollider>();
                p.Collider.sharedMesh = mesh;
            }
            PartNames.Describe(p);

            p.Solid = SolidFor(p);
            p.Glass = GlassFor(p);
            p.InsideOnly = p.Cls == "cavity" || p.Cls == "mitral" || p.Cls == "tricuspid" || p.Cls == "aorticValve" || p.Cls == "pulmValve" || p.Cls == "papLV" || p.Cls == "papRV";
            p.HideInside = p.Cls == "coronaryArtery" || p.Cls == "coronaryVein";
            return p;
        }

        Material NewLit(Color c, float smooth)
        {
            var tpl = Resources.Load<Material>("Materials/Lit");
            var m = tpl != null ? new Material(tpl) : new Material(litShader);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 1f);
            return m;
        }

        Material SolidFor(Part p)
        {
            string key = p.Cls;
            if (solidMats.TryGetValue(key, out var m)) return m;
            var c = SolidColors.TryGetValue(key, out var cc) ? cc : Color.gray;
            float smooth = key.Contains("Valve") || key == "mitral" || key == "tricuspid" ? 0.42f : 0.50f;
            if (key == "coronaryArtery" || key == "coronaryVein") smooth = 0.58f;
            m = NewLit(c, smooth);
            solidMats[key] = m;
            return m;
        }

        Material GlassFor(Part p)
        {
            var tpl = Resources.Load<Material>("Materials/XRay");
            var m = tpl != null ? new Material(tpl) : new Material(xrayShader);
            Color c;
            float minA = 0.03f, maxA = 0.42f;
            switch (p.Cls)
            {
                case "cavity":
                    bool right = p.Name.EndsWith("RV") || p.Name.EndsWith("RA");
                    c = right ? new Color(0.35f, 0.45f, 1f, 1f) : new Color(1f, 0.18f, 0.2f, 1f);
                    minA = 0.02f; maxA = 0.16f;
                    break;
                case "pulmArtery": case "cava": c = new Color(0.45f, 0.55f, 1f, 1f); minA = 0.012f; maxA = 0.26f; break;
                case "myocardium": c = new Color(1f, 0.42f, 0.42f, 1f); minA = 0.01f; maxA = 0.30f; break;
                default: c = new Color(1f, 0.5f, 0.48f, 1f); minA = 0.012f; maxA = 0.26f; break;
            }
            m.SetColor("_Color", c);
            m.SetFloat("_MinAlpha", minA);
            m.SetFloat("_MaxAlpha", maxA);
            // draw order: blood volumes first, shells after
            m.renderQueue = p.Cls == "cavity" ? 3000 : 3010;
            return m;
        }

        public void SetInternalView(bool on)
        {
            InternalView = on;
            foreach (var p in Parts)
            {
                bool solidInside = p.Cls == "mitral" || p.Cls == "tricuspid" || p.Cls == "aorticValve" || p.Cls == "pulmValve" || p.Cls == "papLV" || p.Cls == "papRV";
                if (!on)
                {
                    p.Renderer.enabled = !p.InsideOnly;
                    p.Renderer.sharedMaterial = p == highlighted ? highlightMat : p.Solid;
                    p.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                else
                {
                    p.Renderer.enabled = !p.HideInside;
                    p.Renderer.sharedMaterial = p == highlighted ? highlightMat : (solidInside ? p.Solid : p.Glass);
                    p.Renderer.shadowCastingMode = solidInside ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (p.Collider) p.Collider.enabled = p.Renderer.enabled;
            }
        }

        public void Highlight(Part p)
        {
            highlighted = p;
            SetInternalView(InternalView);
        }

        // ------------- physiology -> geometry
        const float EDVref = 133f, ESVref = 52f, RvEDVref = 123f, RvESVref = 49f, LAmax = 69f, LAmin = 30f;

        public void Apply(CardiacModel m)
        {
            float wl = (EDVref - m.Vlv) / (EDVref - ESVref);
            float wr = (RvEDVref - m.Vrv) / (RvEDVref - RvESVref);
            float wv = Mathf.Clamp((wl * 0.6f + wr * 0.4f), -0.3f, 1.3f);
            float wa = Mathf.Clamp((LAmax - m.Vla) / (LAmax - LAmin), -0.2f, 1.2f);
            float wao = Mathf.Clamp((m.Pao - 80f) / 40f, -0.3f, 1.4f);
            float wpa = Mathf.Clamp((m.Ppa - 12f) / 14f, -0.3f, 1.4f);
            foreach (var p in Parts)
            {
                var r = p.Renderer;
                if (!r.enabled) continue;
                if (p.KVent >= 0) r.SetBlendShapeWeight(p.KVent, wv * 100f);
                if (p.KAtr >= 0) r.SetBlendShapeWeight(p.KAtr, wa * 100f);
                if (p.KDist >= 0) r.SetBlendShapeWeight(p.KDist, (p.Cls == "pulmArtery" ? wpa : wao) * 100f);
                if (p.KOpen >= 0)
                {
                    float o = 0f;
                    switch (p.Cls)
                    {
                        case "mitral": o = m.Mitral.Leaflet; break;
                        case "tricuspid": o = m.Tricuspid.Leaflet; break;
                        case "aorticValve": o = m.Aortic.Leaflet; break;
                        case "pulmValve": o = m.Pulmonary.Leaflet; break;
                    }
                    r.SetBlendShapeWeight(p.KOpen, Mathf.Clamp01(o) * 100f);
                }
            }
        }

        public Part Pick(Ray worldRay)
        {
            if (Physics.Raycast(worldRay, out var hit, 100f))
                foreach (var p in Parts) if (p.Collider == hit.collider) return p;
            return null;
        }

        public Vector3 LM(string k) => Landmarks.TryGetValue(k, out var v) ? v : Vector3.zero;
    }
}
