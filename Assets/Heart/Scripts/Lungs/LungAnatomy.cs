using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MedAtlas.Lungs
{
    /// <summary>Loads the lungs (Z-Anatomy, CC BY-SA 4.0, derived from BodyParts3D) from Resources/lungs_model.bytes
    /// (HRT2 packed format) and drives the breathing blend shapes.</summary>
    public class LungAnatomy : MonoBehaviour
    {
        public class Part
        {
            public string Name, Cls, Bg, Latin;
            public Mesh Mesh; public SkinnedMeshRenderer Renderer; public MeshCollider Collider;
            public int KInspire = -1, KBeat = -1;
            public Material Solid, Glass;
        }

        public readonly List<Part> Parts = new List<Part>();
        public readonly Dictionary<string, Vector3> Landmarks = new Dictionary<string, Vector3>();
        public Bounds Bounds;
        public bool InternalView { get; private set; }
        Part highlighted; Material highlightMat;
        readonly Dictionary<string, Material> solid = new Dictionary<string, Material>();
        Material veinMat, veinGlass;

        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            {"lobe", new Color(0.78f, 0.47f, 0.47f)}, {"airway", new Color(0.88f, 0.82f, 0.72f)},
            {"pulmArtery", new Color(0.16f, 0.26f, 0.68f)}, {"pulmVein", new Color(0.70f, 0.12f, 0.12f)},
            {"diaphragm", new Color(0.62f, 0.2f, 0.18f)}, {"heart", new Color(0.46f, 0.08f, 0.09f)},
        };

        public void Build(byte[] data)
        {

            ParseLandmarks(Resources.Load<TextAsset>("lungs_landmarks").text);
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "HRT2") { Debug.LogError("bad lungs model"); return; }
                int n = br.ReadInt32(); bool first = true;
                for (int i = 0; i < n; i++)
                {
                    var p = ReadPart(br); Parts.Add(p);
                    if (p.Cls != "lobe") continue;
                    if (first) { Bounds = p.Mesh.bounds; first = false; } else Bounds.Encapsulate(p.Mesh.bounds);
                }
            }
            var hl = Resources.Load<Material>("Materials/Highlight");
            highlightMat = new Material(hl);
            SetInternalView(false);
        }

        void ParseLandmarks(string json)
        {
            int i = 0; var ci = System.Globalization.CultureInfo.InvariantCulture;
            while (true)
            {
                int q1 = json.IndexOf('"', i); if (q1 < 0) break;
                int q2 = json.IndexOf('"', q1 + 1);
                string key = json.Substring(q1 + 1, q2 - q1 - 1);
                int b1 = json.IndexOf('[', q2), b2 = json.IndexOf(']', b1);
                var s = json.Substring(b1 + 1, b2 - b1 - 1).Split(',');
                Landmarks[key] = new Vector3(float.Parse(s[0], ci), float.Parse(s[1], ci), float.Parse(s[2], ci));
                i = b2 + 1;
            }
        }

        public Vector3 LM(string k) => Landmarks.TryGetValue(k, out var v) ? v : Vector3.zero;

        static string ReadStr(BinaryReader br) { int n = br.ReadInt32(); return Encoding.UTF8.GetString(br.ReadBytes(n)); }
        static short[] I16(BinaryReader br, int n) { var b = br.ReadBytes(n * 2); var a = new short[n]; System.Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }

        Part ReadPart(BinaryReader br)
        {
            var p = new Part { Name = ReadStr(br), Cls = ReadStr(br) };
            int nv = br.ReadInt32(), nt = br.ReadInt32();
            var mn = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var mx = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var r = mx - mn; var qp = I16(br, nv * 3);
            var pos = new Vector3[nv];
            for (int i = 0; i < nv; i++)
                pos[i] = new Vector3(mn.x + (qp[i * 3] + 32768) / 65535f * r.x, mn.y + (qp[i * 3 + 1] + 32768) / 65535f * r.y, mn.z + (qp[i * 3 + 2] + 32768) / 65535f * r.z);
            var qn = br.ReadBytes(nv * 3); var nrm = new Vector3[nv];
            for (int i = 0; i < nv; i++) nrm[i] = new Vector3((sbyte)qn[i * 3], (sbyte)qn[i * 3 + 1], (sbyte)qn[i * 3 + 2]).normalized;
            int isz = br.ReadByte(); var idx = new int[nt * 3];
            if (isz == 2) { var ib = br.ReadBytes(nt * 6); for (int i = 0; i < idx.Length; i++) idx[i] = ib[i * 2] | (ib[i * 2 + 1] << 8); }
            else { var ib = br.ReadBytes(nt * 12); System.Buffer.BlockCopy(ib, 0, idx, 0, ib.Length); }
            var mesh = new Mesh { name = p.Name };
            if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = pos; mesh.normals = nrm; mesh.triangles = idx;
            int nk = br.ReadInt32();
            for (int k = 0; k < nk; k++)
            {
                string kn = ReadStr(br);
                float sp = br.ReadSingle() / 32767f, sn = br.ReadSingle() / 32767f;
                var qa = I16(br, nv * 3); var qb = I16(br, nv * 3);
                var dp = new Vector3[nv]; var dn = new Vector3[nv];
                for (int i = 0; i < nv; i++) { dp[i] = new Vector3(qa[i * 3], qa[i * 3 + 1], qa[i * 3 + 2]) * sp; dn[i] = new Vector3(qb[i * 3], qb[i * 3 + 1], qb[i * 3 + 2]) * sn; }
                mesh.AddBlendShapeFrame(kn, 100f, dp, dn, null);
            }
            mesh.RecalculateBounds();
            p.Mesh = mesh;
            p.KInspire = mesh.GetBlendShapeIndex("Inspire");
            p.KBeat = mesh.GetBlendShapeIndex("Beat");
            var go = new GameObject(p.Name); go.transform.SetParent(transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = mesh;
            var b = mesh.bounds; b.Expand(0.4f); smr.localBounds = b;
            p.Renderer = smr;
            p.Collider = go.AddComponent<MeshCollider>(); p.Collider.sharedMesh = mesh;
            var names = LungTexts.Part(p.Name, p.Cls); p.Bg = names.bg; p.Latin = names.la;
            p.Solid = SolidFor(p.Cls);
            p.Glass = GlassFor(p.Cls);
            if (p.Cls == "pulmVein") { p.Solid = veinMat; p.Glass = veinGlass; }
            return p;
        }

        Material SolidFor(string cls)
        {
            if (cls == "pulmVein" && veinMat != null) return veinMat;
            if (solid.TryGetValue(cls, out var m)) return m;
            m = new Material(Resources.Load<Material>("Materials/Lit"));
            m.SetColor("_BaseColor", Colors.TryGetValue(cls, out var c) ? c : Color.gray);
            m.SetFloat("_Smoothness", cls == "lobe" ? 0.35f : 0.5f);
            m.SetFloat("_Metallic", 0f);
            solid[cls] = m;
            if (cls == "pulmVein") veinMat = m;
            return m;
        }

        Material GlassFor(string cls)
        {
            if (cls == "pulmVein" && veinGlass != null) return veinGlass;
            if (cls == "airway" || cls == "pulmArtery" || cls == "pulmVein" || cls == "diaphragm")
            {
                if (cls == "pulmVein") { veinGlass = SolidFor(cls); return veinGlass; }
                return SolidFor(cls);
            }
            var m = new Material(Resources.Load<Material>("Materials/XRay"));
            m.SetColor("_Color", cls == "heart" ? new Color(1f, 0.4f, 0.4f, 1f) : new Color(1f, 0.72f, 0.72f, 1f));
            m.SetFloat("_MinAlpha", 0.02f); m.SetFloat("_MaxAlpha", cls == "heart" ? 0.35f : 0.42f);
            m.renderQueue = 3010;
            return m;
        }

        public void SetInternalView(bool on)
        {
            InternalView = on;
            foreach (var p in Parts)
            {
                bool inside = p.Cls == "pulmArtery" || p.Cls == "pulmVein" || (p.Cls == "airway" && p.Name != "Trachea");
                if (!on)
                {
                    p.Renderer.enabled = !inside || p.Name.Contains("main") || p.Name.Contains("Pulmonary trunk") || p.Name.Contains("pulmonary artery") || p.Name.Contains("pulmonary vein");
                    p.Renderer.sharedMaterial = p == highlighted ? highlightMat : p.Solid;
                }
                else
                {
                    p.Renderer.enabled = true;
                    p.Renderer.sharedMaterial = p == highlighted ? highlightMat : p.Glass;
                }
                p.Renderer.receiveShadows = p.Cls != "lobe"; p.Renderer.shadowCastingMode = (p.Cls == "lobe" || (on && p.Cls == "heart")) ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                p.Collider.enabled = p.Renderer.enabled;
            }
        }

        public void Highlight(Part p) { highlighted = p; SetInternalView(InternalView); }

        public Part Pick(Ray ray)
        {
            if (Physics.Raycast(ray, out var hit, 200f))
                foreach (var p in Parts) if (p.Collider == hit.collider) return p;
            return null;
        }

        /// <param name="volumeMl">lung volume above FRC</param>
        public void Apply(float volumeMl, float beat01, float sao2)
        {
            float w = Mathf.Clamp(volumeMl / 750f, -0.2f, 2.2f);   // shown ~1.3× larger than life so it is visible
            foreach (var p in Parts)
            {
                if (!p.Renderer.enabled) continue;
                if (p.KInspire >= 0) p.Renderer.SetBlendShapeWeight(p.KInspire, w * 100f);
                if (p.KBeat >= 0) p.Renderer.SetBlendShapeWeight(p.KBeat, beat01 * 100f);
            }
            // oxygenated blood colour of the pulmonary veins follows SaO2
            float k = Mathf.InverseLerp(0.6f, 0.98f, sao2);
            var c = Color.Lerp(new Color(0.30f, 0.12f, 0.45f), new Color(0.72f, 0.10f, 0.10f), k);
            if (veinMat != null) veinMat.SetColor("_BaseColor", c);
        }
    }
}
