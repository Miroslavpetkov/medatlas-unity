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
            {"bone", new Color(0.90f, 0.87f, 0.80f)}, {"muscle", new Color(0.62f, 0.13f, 0.13f)},
        };

        readonly List<(SkinnedMeshRenderer r, int key)> contract = new List<(SkinnedMeshRenderer, int)>();
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        public Bounds Bounds; public bool Loaded;

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
                        smr.sharedMaterial = Mat(cls);
                        if (k >= 0) contract.Add((smr, k));
                    }
                    else
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = Mat(cls);
                    }
                    if (first) { Bounds = mesh.bounds; first = false; } else Bounds.Encapsulate(mesh.bounds);
                }
            }
            Loaded = true;
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

        public void SetContraction(float a01)
        {
            foreach (var c in contract) c.r.SetBlendShapeWeight(c.key, Mathf.Clamp01(a01) * 100f);
        }
    }
}
