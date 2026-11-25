using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using D = System.Collections.Generic.Dictionary<string, object>;

namespace MedAtlas.Sims
{
    /// <summary>
    /// Draws the web version's own 3D scene of the running model (Tools/physio-bridge/mirror.ts): the page builds the
    /// three.js scene with the same code as the web app, runs its update every frame and hands the result over —
    /// meshes, particles, lines and labels with their transforms, colours and visibility. Unity draws exactly those
    /// objects with its own realistic materials (MirrorLit / MirrorPoints / MirrorSprite), so every simulation shows
    /// the same scene, animation, camera and labels in both versions.
    /// </summary>
    public class SceneMirror : MonoBehaviour
    {
        /// <summary>Web scene units (about a millimetre) to Unity units.</summary>
        public const float S = 0.02f;
        /// <summary>The web camera looks from (0.35, 0.25, 1): as Unity orbit angles.</summary>
        public const float WebYaw = -19.29f, WebPitch = 13.28f;
        /// <summary>This scene's web look direction as orbit angles (set by Fit; flat diagrams are seen straight on).</summary>
        public float Yaw = WebYaw, Pitch = WebPitch;

        const int K_MESH = 0, K_INST = 1, K_POINTS = 2, K_LINES = 3, K_STRIP = 4, K_LOOP = 5, K_SPRITE = 6;
        const int M_LIT = 0, M_BASIC = 1, M_POINTS = 2, M_LINE = 3, M_SPRITE = 4;

        int h = -1;
        float[] frame = new float[16384];
        int ver = -1;
        public bool Ok { get; private set; }
        public string Error { get; private set; }
        /// <summary>„Плътност“ slider: 1 = as authored, lower makes solid surfaces see-through.</summary>
        public float Opacity = 1f;
        float builtAt;
        public System.Action OnStructure;

        class Geo
        {
            public int id, n, idx; public bool nrm, uv, col;
            public Vector3[] pos, normal; public Vector2[] uvs; public Color[] cols; public int[] index;
            public readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();   // by node kind
            public readonly Dictionary<int, Vector2Int> range = new Dictionary<int, Vector2Int>();
        }
        class Mat { public int id, type, tex; public Material m, inst; public float baseOpacity = 1; public int flags; public bool transparent; }
        class Node
        {
            public int id, kind, gid, mid; public string part; public GameObject go; public MeshRenderer mr; public MeshFilter mf;
            public bool visible; public Matrix4x4[] inst; public Vector4[] instCol; public int instN; public bool hasInstCol;
            public MaterialPropertyBlock mpb;
        }
        readonly Dictionary<int, Geo> geos = new Dictionary<int, Geo>();
        readonly Dictionary<int, Mat> mats = new Dictionary<int, Mat>();
        readonly Dictionary<int, Texture2D> texs = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, Node> nodes = new Dictionary<int, Node>();
        readonly List<Node> order = new List<Node>();
        Material tplLit, tplInst, tplPoints, tplSprite;
        Mesh quad;
        Camera cam;

        /// <summary>Mirror the web scene of model instance `handle`; `prebuilt` when the page already built it under that
        /// handle (the muscle section).</summary>
        public static SceneMirror Create(int handle, Camera cam, bool prebuilt = false)
        {
            var go = new GameObject("WebScene");
            var m = go.AddComponent<SceneMirror>();
            m.h = handle; m.cam = cam;
            go.transform.localScale = Vector3.one * S;
            var res = MiniJson.O(MiniJson.Parse(prebuilt ? "{\"ok\":true}" : PhysioBridge.SceneBuild(handle) ?? "{}"));
            m.Ok = res != null && res.TryGetValue("ok", out var ok) && ok is bool b && b;
            m.Error = res != null ? MiniJson.S(res, "err") : "no bridge";
            if (!m.Ok) { Debug.LogWarning("SceneMirror: " + m.Error); Destroy(go); return null; }
            m.Init();
            return m;
        }

        void Init()
        {
            tplLit = Resources.Load<Material>("Materials/MirrorLit");
            tplInst = Resources.Load<Material>("Materials/MirrorLitInst");
            tplPoints = Resources.Load<Material>("Materials/MirrorPoints");
            tplSprite = Resources.Load<Material>("Materials/MirrorSprite");
            quad = new Mesh { name = "SpriteQuad" };
            quad.vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            builtAt = Time.unscaledTime;
            SetLights();
            NeutralPost();
            Tick(0);
        }

        void OnDestroy()
        {
            if (h >= 0 && Ok) PhysioBridge.SceneDispose(h);
            foreach (var g in geos.Values) foreach (var m in g.meshes.Values) Destroy(m);
            foreach (var m in mats.Values) { Destroy(m.m); if (m.inst) Destroy(m.inst); }
            foreach (var t in texs.Values) Destroy(t);
            if (quad) Destroy(quad);
        }

        // ------------------------------------------------------------------ lights, post

        static Vector4 Dir(float x, float y, float z) { var v = new Vector3(x, y, -z).normalized; return new Vector4(v.x, v.y, v.z, 0); }
        static Vector4 Lin(int hex, float k) { var c = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f).linear; return new Vector4(c.r * k, c.g * k, c.b * k, 1); }

        /// <summary>The web viewer's lights (viewer.ts), as shader globals.</summary>
        static void SetLights()
        {
            Shader.SetGlobalVector("_MirKeyDir", Dir(120, 200, 160)); Shader.SetGlobalVector("_MirKeyCol", Lin(0xfff3e8, 1.7f));
            Shader.SetGlobalVector("_MirFillDir", Dir(-200, -40, -120)); Shader.SetGlobalVector("_MirFillCol", Lin(0x9fc3ff, 0.55f));
            Shader.SetGlobalVector("_MirRimDir", Dir(-80, 60, -200)); Shader.SetGlobalVector("_MirRimCol", Lin(0xbfd4ff, 0.9f));
            Shader.SetGlobalVector("_MirCamCol", Lin(0xffffff, 0.3f));
            Shader.SetGlobalVector("_MirSky", Lin(0xdce9ff, 0.85f)); Shader.SetGlobalVector("_MirGround", Lin(0x2a1d26, 0.85f));
            // x: environment intensity (web 0.55), y: exposure (web 1.15), z: soft tissue rim
            Shader.SetGlobalVector("_MirEnv", new Vector4(0.42f, 0.9f, 0.08f, 0));   // side-by-side QA: web 0.55/1.15 read clearly brighter and warmer here
            var fog = Lin(0x0a1020, 1f); fog.w = 0.0012f / S;   // FogExp2 density per Unity unit
            Shader.SetGlobalVector("_MirFogCol", fog);
        }

        /// <summary>The mirror's shaders tone-map like the web (ACES, exposure 1.15): switch off the studio's own
        /// tone mapping and colour grading so the picture is not graded twice.</summary>
        static void NeutralPost()
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var p = v.sharedProfile; if (p == null) continue;
                if (p.TryGet<Tonemapping>(out var tm)) tm.mode.Override(TonemappingMode.None);
                if (p.TryGet<ColorAdjustments>(out var ca)) { ca.contrast.Override(0f); ca.saturation.Override(0f); ca.postExposure.Override(0f); }
                if (p.TryGet<Bloom>(out var bl)) { bl.threshold.Override(0.9f); bl.intensity.Override(0.25f); }
            }
        }

        // ------------------------------------------------------------------ framing

        /// <summary>The web viewer's framing for this view's shape: target (Unity units) and distance.</summary>
        public bool Fit(float aspect, float fovDeg, out Vector3 target, out float dist)
        {
            target = Vector3.zero; dist = 0;
            var a = MiniJson.L(MiniJson.Parse(PhysioBridge.SceneFit(h, aspect, fovDeg)));
            if (a.Count < 4) return false;
            target = new Vector3((float)MiniJson.Num(a[0]), (float)MiniJson.Num(a[1]), (float)MiniJson.Num(a[2])) * S;
            dist = (float)MiniJson.Num(a[3]) * S;
            if (a.Count >= 7)
            {
                // the web look direction (target -> camera) as orbit angles: the camera looks along its opposite
                var f = -new Vector3((float)MiniJson.Num(a[4]), (float)MiniJson.Num(a[5]), (float)MiniJson.Num(a[6])).normalized;
                Yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                Pitch = Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            return dist > 0;
        }

        /// <summary>True for a few seconds after the scene was built (real anatomy is still arriving).</summary>
        public bool Settling => Time.unscaledTime - builtAt < 25f;   // body anatomy can take well over 6 s on a slow machine

        // ------------------------------------------------------------------ frame

        /// <summary>Run the web scene's update (dt = 0 while paused, as on the web) and apply the frame.</summary>
        public void Tick(float dt)
        {
            if (!Ok) return;
            if (cam)
            {
                // the camera in the web scene's coordinates (this object is the web world scaled by S, z mirrored):
                // the heavy meshes (skin, brain) choose their level of detail from it, as in the web viewer
                var lp = transform.InverseTransformPoint(cam.transform.position);
                PhysioBridge.SceneCam(h, lp.x, lp.y, -lp.z);
            }
            int len = PhysioBridge.SceneFrame(h, dt);
            if (len <= 0) return;
            if (len > frame.Length) frame = new float[Mathf.NextPowerOfTwo(len)];
            PhysioBridge.SceneCopyFrame(h, frame);
            var f = frame;
            int sv = (int)f[0], nMat = (int)f[1], nNode = (int)f[2], nDyn = (int)f[3], nTex = (int)f[4], nInst = (int)f[5];
            if (sv != ver)
            {
                Sync();
                ver = sv;
                OnStructure?.Invoke();
            }
            int p = 8;
            for (int i = 0; i < nMat; i++, p += 16) ApplyMat(f, p);
            for (int i = 0; i < nNode; i++, p += 24) ApplyNode(f, p);
            for (int i = 0; i < nDyn; i++) p = ApplyDyn(f, p);
            for (int i = 0; i < nTex; i++, p += 4) LoadTex((int)f[p], (int)f[p + 1], (int)f[p + 2]);
            for (int i = 0; i < nInst; i++) p = ApplyInst(f, p);
            Readable();
            DrawInstanced();
        }

        void ApplyMat(float[] f, int p)
        {
            if (!mats.TryGetValue((int)f[p], out var mt)) return;
            var c = new Color(f[p + 1], f[p + 2], f[p + 3], 1);
            var e = new Color(f[p + 4], f[p + 5], f[p + 6], 1);
            float op = f[p + 7]; int flags = (int)f[p + 11];
            mt.baseOpacity = op;
            bool dirtyState = flags != mt.flags;
            mt.flags = flags;
            for (int mi = 0; mi < 2; mi++)
            {
                var m = mi == 0 ? mt.m : mt.inst;
                if (m == null) continue;
                m.SetColor("_Color", c);
                switch (mt.type)
                {
                    case M_LIT: case M_BASIC: case M_LINE:
                        m.SetColor("_Emission", e);
                        m.SetFloat("_Rough", f[p + 8]); m.SetFloat("_Metal", f[p + 9]);
                        break;
                    case M_POINTS:
                        if ((flags & 128) != 0) { m.SetFloat("_PtPix", f[p + 10] * Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 96f : 1f)); m.SetFloat("_PtSize", 0); }
                        else { m.SetFloat("_PtSize", f[p + 10] * S); m.SetFloat("_PtPix", 0); }
                        break;
                    case M_SPRITE:
                        m.SetFloat("_Rotation", f[p + 13]);
                        break;
                }
                m.SetFloat("_VCol", (flags & 8) != 0 ? 1 : 0);
                m.SetFloat("_ToneMap", (flags & 256) != 0 ? 0 : 1);
                float o = op;
                bool solidDimmed = (mt.type == M_LIT) && (flags & 1) == 0 && Opacity < 0.995f;
                if (solidDimmed) o = op * Opacity;
                m.SetFloat("_Opacity", o);
                if (dirtyState || solidDimmed != mt.transparent) RenderState(m, mt.type, flags, solidDimmed);
            }
            mt.transparent = (mt.type == M_LIT) && (flags & 1) == 0 && Opacity < 0.995f;
        }

        static void RenderState(Material m, int type, int flags, bool dimmed)
        {
            bool transparent = (flags & 1) != 0 || dimmed || type == M_POINTS || type == M_SPRITE;
            bool additive = (flags & 64) != 0;
            bool depthWrite = (flags & 2) != 0 && !dimmed;
            bool depthTest = (flags & 4) != 0;
            if (type == M_SPRITE)
            {
                m.SetFloat("_ZTest", depthTest ? (float)CompareFunction.LessEqual : (float)CompareFunction.Always);
                m.SetFloat("_ZWrite", depthWrite ? 1 : 0);
                m.renderQueue = depthTest ? 3000 : 3100;
                return;
            }
            m.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", !transparent ? (float)UnityEngine.Rendering.BlendMode.Zero : additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", transparent ? (depthWrite ? 1 : 0) : 1);
            m.SetFloat("_ZTest", depthTest ? (float)CompareFunction.LessEqual : (float)CompareFunction.Always);
            if (type != M_POINTS) m.SetFloat("_Cull", (flags & 16) != 0 ? (float)CullMode.Off : (flags & 32) != 0 ? (float)CullMode.Front : (float)CullMode.Back);
            m.renderQueue = transparent ? 3000 : 2000;
        }

        void ApplyNode(float[] f, int p)
        {
            if (!nodes.TryGetValue((int)f[p], out var n)) return;
            n.visible = f[p + 1] > 0.5f;
            if (n.kind == K_INST) { SetRange(n, (int)f[p + 3], (int)f[p + 4]); return; }
            if (n.go == null) return;
            if (n.go.activeSelf != n.visible) n.go.SetActive(n.visible);
            if (!n.visible) return;
            n.mr.sortingOrder = (int)f[p + 2];
            SetTransform(n.go.transform, f, p + 5);
            if (n.kind == K_SPRITE)
            {
                if (mats.TryGetValue(n.mid, out var mt)) mt.m.SetVector("_Center", new Vector4(f[p + 21], f[p + 22], 0, 0));
            }
            else SetRange(n, (int)f[p + 3], (int)f[p + 4]);
        }

        static void SetTransform(Transform t, float[] f, int o)
        {
            var c0 = new Vector3(f[o], f[o + 1], f[o + 2]);
            var c1 = new Vector3(f[o + 4], f[o + 5], f[o + 6]);
            var c2 = new Vector3(f[o + 8], f[o + 9], f[o + 10]);
            t.localPosition = new Vector3(f[o + 12], f[o + 13], f[o + 14]);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sy > 1e-9f && sz > 1e-9f)
            {
                if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0) sx = -sx;   // a mirror image (negative scale on the web)
                t.localRotation = Quaternion.LookRotation(c2 / sz, c1 / sy);
            }
            t.localScale = new Vector3(sx, sy, sz);
        }

        void SetRange(Node n, int start, int count)
        {
            if (!geos.TryGetValue(n.gid, out var g)) return;
            int kind = MeshKind(n.kind);
            if (!g.meshes.TryGetValue(kind, out var mesh)) return;
            var want = new Vector2Int(start, count);
            if (g.range.TryGetValue(kind, out var had) && had == want) return;
            g.range[kind] = want;
            int total = IndexCount(g, n.kind);
            int mul = n.kind == K_POINTS ? 6 : 1;
            int s0 = Mathf.Clamp(start * mul, 0, total);
            int c = count < 0 ? total - s0 : Mathf.Clamp(count * mul, 0, total - s0);
            if (n.kind == K_POINTS) { s0 -= s0 % 6; c -= c % 6; }
            var topo = Topology(n.kind);
            mesh.SetSubMesh(0, new SubMeshDescriptor(s0, c, topo), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        static MeshTopology Topology(int kind) => kind == K_LINES ? MeshTopology.Lines : kind == K_STRIP || kind == K_LOOP ? MeshTopology.LineStrip : MeshTopology.Triangles;
        static int MeshKind(int kind) => kind == K_INST ? K_MESH : kind;
        static int IndexCount(Geo g, int kind)
        {
            if (kind == K_POINTS) return g.n * 6;
            int c = g.idx > 0 ? g.idx : g.n;
            return kind == K_LOOP ? c + 1 : c;
        }

        int ApplyDyn(float[] f, int p)
        {
            int gid = (int)f[p]; int n = (int)f[p + 1]; int fl = (int)f[p + 2]; p += 4;
            geos.TryGetValue(gid, out var g);
            bool rebuild = g != null && n != g.n;
            var pos = new Vector3[n];
            for (int i = 0; i < n; i++, p += 3) pos[i] = new Vector3(f[p], f[p + 1], f[p + 2]);
            Vector3[] nrm = null; Color[] col = null;
            if ((fl & 2) != 0) { nrm = new Vector3[n]; for (int i = 0; i < n; i++, p += 3) nrm[i] = new Vector3(f[p], f[p + 1], f[p + 2]); }
            if ((fl & 4) != 0) { col = new Color[n]; for (int i = 0; i < n; i++, p += 3) col[i] = new Color(f[p], f[p + 1], f[p + 2], 1); }
            if (g == null) return p;
            g.pos = pos; if (nrm != null) g.normal = nrm; if (col != null) g.cols = col;
            if (rebuild)
            {
                g.n = n; if (g.idx == 0) g.index = null;
                if (g.uvs != null && g.uvs.Length != n) g.uvs = null;
                if (g.normal != null && g.normal.Length != n) g.normal = null;
                if (g.cols != null && g.cols.Length != n) g.cols = null;
                foreach (var kv in g.meshes) Fill(kv.Value, g, kv.Key);
                g.range.Clear();
                return p;
            }
            foreach (var kv in g.meshes)
            {
                var mesh = kv.Value;
                if (kv.Key == K_POINTS) { mesh.SetVertices(Quads(pos)); if (col != null) mesh.SetColors(Quads(col)); }
                else
                {
                    mesh.SetVertices(pos);
                    if (kv.Key == K_MESH) { if (nrm != null) mesh.SetNormals(nrm); else if (!g.nrm) mesh.RecalculateNormals(); }
                    if (col != null) mesh.SetColors(col);
                }
                mesh.RecalculateBounds();
                if (kv.Key == K_POINTS) Pad(mesh);
            }
            return p;
        }

        static Vector3[] Quads(Vector3[] p) { var o = new Vector3[p.Length * 4]; for (int i = 0; i < p.Length; i++) o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = o[i * 4 + 3] = p[i]; return o; }
        static Color[] Quads(Color[] p) { var o = new Color[p.Length * 4]; for (int i = 0; i < p.Length; i++) o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = o[i * 4 + 3] = p[i]; return o; }
        static void Pad(Mesh m) { var b = m.bounds; b.Expand(30f); m.bounds = b; }   // the quads grow in the shader

        int ApplyInst(float[] f, int p)
        {
            int nid = (int)f[p]; int n = (int)f[p + 1]; bool col = f[p + 2] > 0.5f; p += 4;
            nodes.TryGetValue(nid, out var nd);
            if (nd != null)
            {
                if (nd.inst == null || nd.inst.Length < n) { nd.inst = new Matrix4x4[Mathf.Max(n, 1)]; nd.instCol = new Vector4[Mathf.Max(n, 1)]; }
                nd.instN = n; nd.hasInstCol = col;
                for (int i = 0; i < n; i++)
                {
                    var m = new Matrix4x4();
                    for (int k = 0; k < 16; k++) m[k % 4, k / 4] = f[p + i * 16 + k];
                    nd.inst[i] = m;
                }
            }
            p += n * 16;
            if (col)
            {
                if (nd != null) for (int i = 0; i < n; i++) { var c = new Color(f[p + i * 3], f[p + i * 3 + 1], f[p + i * 3 + 2], 1).linear; nd.instCol[i] = new Vector4(c.r, c.g, c.b, 1); }
                p += n * 3;
            }
            return p;
        }

        readonly Matrix4x4[] chunk = new Matrix4x4[1023];
        readonly Vector4[] chunkCol = new Vector4[1023];
        void DrawInstanced()
        {
            var root = transform.localToWorldMatrix;
            foreach (var n in order)
            {
                if (n.kind != K_INST || !n.visible || n.instN <= 0) continue;
                if (!geos.TryGetValue(n.gid, out var g) || !g.meshes.TryGetValue(K_MESH, out var mesh)) continue;
                if (!mats.TryGetValue(n.mid, out var mt) || mt.inst == null) continue;
                if (n.mpb == null) n.mpb = new MaterialPropertyBlock();
                var rp = new RenderParams(mt.inst) { matProps = n.mpb, worldBounds = new Bounds(transform.position, Vector3.one * 1000f) };
                for (int s = 0; s < n.instN; s += 1023)
                {
                    int c = Mathf.Min(1023, n.instN - s);
                    for (int i = 0; i < c; i++) { chunk[i] = root * n.inst[s + i]; chunkCol[i] = n.hasInstCol ? n.instCol[s + i] : Vector4.one; }
                    n.mpb.SetVectorArray("_InstColor", chunkCol);
                    Graphics.RenderMeshInstanced(rp, mesh, 0, chunk, c);
                }
            }
        }

        /// <summary>Labels keep a readable size on screen (the web grows them to at least 17 CSS pixels).</summary>
        void Readable()
        {
            if (cam == null) return;
            float minPx = 17f * Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 96f : 1f);
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (var n in order)
            {
                if (n.kind != K_SPRITE || n.go == null || !n.visible) continue;
                var t = n.go.transform;
                float depth = Vector3.Dot(t.position - cam.transform.position, cam.transform.forward);
                if (depth <= 0) continue;
                float pxPerUnit = Screen.height / (2f * depth * tan);
                float px = Mathf.Abs(t.localScale.y) * S * pxPerUnit;
                if (px <= 0 || px >= minPx) continue;
                float k = minPx / px;
                float aspect = Mathf.Abs(t.localScale.x) / Mathf.Max(1e-6f, Mathf.Abs(t.localScale.y));
                float maxK = Screen.width * 0.96f / Mathf.Max(1e-3f, px * aspect);   // a long label stops growing at the view's width
                k = Mathf.Max(1f, Mathf.Min(k, maxK));
                t.localScale = new Vector3(t.localScale.x * k, t.localScale.y * k, t.localScale.z);
            }
        }

        // ------------------------------------------------------------------ structure

        void Sync()
        {
            var s = MiniJson.O(MiniJson.Parse(PhysioBridge.SceneStructure(h)));
            if (s == null) return;
            var liveG = new HashSet<int>(); var liveM = new HashSet<int>(); var liveT = new HashSet<int>(); var liveN = new HashSet<int>();
            foreach (var o in MiniJson.L(s.ContainsKey("texs") ? s["texs"] : null))
            {
                var d = MiniJson.O(o); int id = I(d, "id"); liveT.Add(id);
                texWrap[id] = (B(d, "ws") || I(d, "ws") == 1, B(d, "wt") || I(d, "wt") == 1);
                if (!texs.ContainsKey(id)) LoadTex(id, I(d, "w"), I(d, "h"));
            }
            foreach (var o in MiniJson.L(s.ContainsKey("geoms") ? s["geoms"] : null))
            {
                var d = MiniJson.O(o); int id = I(d, "id"); liveG.Add(id);
                if (!geos.ContainsKey(id)) LoadGeo(d);
            }
            foreach (var o in MiniJson.L(s.ContainsKey("mats") ? s["mats"] : null))
            {
                var d = MiniJson.O(o); int id = I(d, "id"); liveM.Add(id);
                if (!mats.ContainsKey(id)) MakeMat(id, I(d, "t"), I(d, "tex"), d);
            }
            order.Clear();
            foreach (var o in MiniJson.L(s.ContainsKey("nodes") ? s["nodes"] : null))
            {
                var d = MiniJson.O(o); int id = I(d, "id"); liveN.Add(id);
                int k = I(d, "k"), g = I(d, "g"), m = I(d, "m");
                if (nodes.TryGetValue(id, out var n) && (n.gid != g || n.mid != m || n.kind != k)) { if (n.go) Destroy(n.go); nodes.Remove(id); n = null; }
                if (n == null) { n = MakeNode(id, k, g, m, MiniJson.S(d, "part"), MiniJson.S(d, "name")); nodes[id] = n; }
                order.Add(n);
            }
            // forget what the scene no longer has
            foreach (var id in new List<int>(nodes.Keys)) if (!liveN.Contains(id)) { var n = nodes[id]; if (n.go) Destroy(n.go); nodes.Remove(id); }
            foreach (var id in new List<int>(geos.Keys)) if (!liveG.Contains(id)) { foreach (var m in geos[id].meshes.Values) Destroy(m); geos.Remove(id); }
            foreach (var id in new List<int>(mats.Keys)) if (!liveM.Contains(id)) { Destroy(mats[id].m); if (mats[id].inst) Destroy(mats[id].inst); mats.Remove(id); }
            foreach (var id in new List<int>(texs.Keys)) if (!liveT.Contains(id)) { Destroy(texs[id]); texs.Remove(id); }
        }

        static int I(D d, string k) => (int)MiniJson.D(d, k, 0);
        static float F(object o) => o is double v ? (float)v : o is float f ? f : o is int n ? n : 0f;
        static bool B(D d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool b && b;

        readonly Dictionary<int, (bool s, bool t)> texWrap = new Dictionary<int, (bool s, bool t)>();
        byte[] texBuf = new byte[1 << 18];
        void LoadTex(int id, int w, int hgt)
        {
            if (w <= 0 || hgt <= 0) return;
            int need = w * hgt * 4;
            if (texBuf.Length < need) texBuf = new byte[Mathf.NextPowerOfTwo(need)];
            int got = PhysioBridge.SceneTex(h, id, texBuf);
            if (got != need) return;
            if (!texs.TryGetValue(id, out var t) || t.width != w || t.height != hgt)
            {
                if (t) Destroy(t);
                t = new Texture2D(w, hgt, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
                if (texWrap.TryGetValue(id, out var wr)) { t.wrapModeU = wr.s ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; t.wrapModeV = wr.t ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; }
                texs[id] = t;
                foreach (var m in mats.Values) if (m.tex == id) { m.m.SetTexture("_MainTex", t); if (m.type != M_SPRITE) m.m.SetFloat("_HasTex", 1); }
            }
            var data = t.GetPixelData<byte>(0);
            Unity.Collections.NativeArray<byte>.Copy(texBuf, data, need);
            t.Apply(false, false);
        }

        float[] geoBuf = new float[1 << 16];
        int[] idxBuf = new int[1 << 16];
        void LoadGeo(D d)
        {
            var g = new Geo { id = I(d, "id"), n = I(d, "n"), idx = I(d, "idx"), nrm = B(d, "nrm"), uv = B(d, "uv"), col = B(d, "col") };
            int n = g.n;
            int need = n * 3 + (g.nrm ? n * 3 : 0) + (g.uv ? n * 2 : 0) + (g.col ? n * 3 : 0);
            if (geoBuf.Length < need) geoBuf = new float[Mathf.NextPowerOfTwo(need)];
            if (need > 0 && PhysioBridge.SceneGeom(h, g.id, geoBuf) != need) { g.n = 0; geos[g.id] = g; return; }
            var f = geoBuf; int p = 0;
            g.pos = new Vector3[n]; for (int i = 0; i < n; i++, p += 3) g.pos[i] = new Vector3(f[p], f[p + 1], f[p + 2]);
            if (g.nrm) { g.normal = new Vector3[n]; for (int i = 0; i < n; i++, p += 3) g.normal[i] = new Vector3(f[p], f[p + 1], f[p + 2]); }
            if (g.uv) { g.uvs = new Vector2[n]; for (int i = 0; i < n; i++, p += 2) g.uvs[i] = new Vector2(f[p], f[p + 1]); }
            if (g.col) { g.cols = new Color[n]; for (int i = 0; i < n; i++, p += 3) g.cols[i] = new Color(f[p], f[p + 1], f[p + 2], 1); }
            geos[g.id] = g;
        }

        /// <summary>The triangle (winding already reversed) or line indices of a geometry, fetched once per kind.</summary>
        int[] Indices(Geo g, bool tri)
        {
            if (g.idx <= 0) return null;
            if (idxBuf.Length < g.idx) idxBuf = new int[Mathf.NextPowerOfTwo(g.idx)];
            if (PhysioBridge.SceneIndex(h, g.id, tri, idxBuf) != g.idx) return null;
            var a = new int[g.idx]; System.Array.Copy(idxBuf, a, g.idx); return a;
        }

        Mesh MeshFor(Geo g, int kind)
        {
            int mk = MeshKind(kind);
            if (g.meshes.TryGetValue(mk, out var m)) return m;
            m = new Mesh { name = "web" + g.id };
            m.MarkDynamic();
            Fill(m, g, mk);
            g.meshes[mk] = m;
            return m;
        }

        void Fill(Mesh m, Geo g, int kind)
        {
            m.Clear();
            int n = g.n;
            if (kind == K_POINTS)
            {
                m.indexFormat = n * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                m.SetVertices(Quads(g.pos));
                var uv = new Vector2[n * 4]; var tri = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    uv[i * 4] = new Vector2(-1, -1); uv[i * 4 + 1] = new Vector2(1, -1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(-1, 1);
                    int b = i * 4; tri[i * 6] = b; tri[i * 6 + 1] = b + 2; tri[i * 6 + 2] = b + 1; tri[i * 6 + 3] = b; tri[i * 6 + 4] = b + 3; tri[i * 6 + 5] = b + 2;
                }
                m.SetUVs(0, uv);
                if (g.cols != null) m.SetColors(Quads(g.cols));
                m.SetIndices(tri, MeshTopology.Triangles, 0, true);
                Pad(m);
                return;
            }
            m.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(g.pos);
            if (g.uvs != null) m.SetUVs(0, g.uvs);
            if (g.cols != null) m.SetColors(g.cols);
            bool tris = kind == K_MESH;
            if (g.index == null || g.index.Length != (g.idx > 0 ? g.idx : n)) g.index = Indices(g, tris);
            int[] idx = g.index;
            if (idx == null)
            {
                idx = new int[n];
                for (int i = 0; i < n; i++) idx[i] = i;
                if (tris) for (int i = 0; i + 2 < n; i += 3) { idx[i + 1] = i + 2; idx[i + 2] = i + 1; }
            }
            if (kind == K_LOOP && idx.Length > 0) { var l = new int[idx.Length + 1]; System.Array.Copy(idx, l, idx.Length); l[idx.Length] = idx[0]; idx = l; }
            m.SetIndices(idx, Topology(kind), 0, true);
            if (tris) { if (g.normal != null) m.SetNormals(g.normal); else m.RecalculateNormals(); }
        }

        void MakeMat(int id, int type, int tex, D d)
        {
            var mt = new Mat { id = id, type = type, tex = tex, flags = -1 };
            switch (type)
            {
                case M_POINTS: mt.m = new Material(tplPoints); break;
                case M_SPRITE: mt.m = new Material(tplSprite); if (texs.TryGetValue(tex, out var t)) mt.m.SetTexture("_MainTex", t); break;
                default:
                    mt.m = new Material(tplLit);
                    if (type != M_LIT) { mt.m.SetFloat("_Unlit", 1); mt.m.SetFloat("_Coat", 0); }
                    if (tex != 0)
                    {
                        // texture on a mesh (three.js map): uv * repeat + offset, as in the web
                        var tr = MiniJson.L(d != null && d.ContainsKey("tr") ? d["tr"] : null);
                        if (tr != null && tr.Count == 4)
                        {
                            mt.m.SetTextureScale("_MainTex", new Vector2(F(tr[0]), F(tr[1])));
                            mt.m.SetTextureOffset("_MainTex", new Vector2(F(tr[2]), F(tr[3])));
                        }
                        if (texs.TryGetValue(tex, out var lt)) { mt.m.SetTexture("_MainTex", lt); mt.m.SetFloat("_HasTex", 1); }
                    }
                    break;
            }
            mt.m.name = "web" + id;
            mats[id] = mt;
        }

        Node MakeNode(int id, int kind, int gid, int mid, string part, string name)
        {
            var n = new Node { id = id, kind = kind, gid = gid, mid = mid, part = part };
            mats.TryGetValue(mid, out var mt);
            if (kind == K_INST)
            {
                if (mt != null && mt.inst == null)
                {
                    mt.inst = new Material(tplInst) { enableInstancing = true, name = "webInst" + mid };
                    mt.inst.SetFloat("_UseInst", 1);
                    if (mt.type != M_LIT) { mt.inst.SetFloat("_Unlit", 1); mt.inst.SetFloat("_Coat", 0); }
                    mt.flags = -1;   // the instanced copy gets its render state on the next frame
                }
                if (geos.TryGetValue(gid, out var gi)) MeshFor(gi, K_INST);
                return n;
            }
            // tap a part for its card (PartPicker): objects are named by their web part id
            n.go = new GameObject(!string.IsNullOrEmpty(part) ? part : !string.IsNullOrEmpty(name) ? name : "структура");
            n.go.transform.SetParent(transform, false);
            n.mf = n.go.AddComponent<MeshFilter>();
            n.mr = n.go.AddComponent<MeshRenderer>();
            n.mr.shadowCastingMode = ShadowCastingMode.Off; n.mr.receiveShadows = false;
            n.mr.lightProbeUsage = LightProbeUsage.Off; n.mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (kind == K_SPRITE) n.mf.sharedMesh = quad;
            else if (geos.TryGetValue(gid, out var g)) n.mf.sharedMesh = MeshFor(g, kind);
            if (mt != null) n.mr.sharedMaterial = mt.m;
            if (kind != K_MESH) n.go.layer = 2;   // labels, particles and lines are not picked (Ignore Raycast)
            return n;
        }
    }
}
