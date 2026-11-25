using System;
using System.Runtime.InteropServices;

namespace MedAtlas.Sims
{
    /// <summary>Runs the medAtlas web physiology models (bundled JavaScript, medphysio.js) from Unity.
    /// Available in the WebGL build (the browser runs the original model code).</summary>
    public static class PhysioBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int MedPhysio_Ready();
        [DllImport("__Internal")] static extern IntPtr MedPhysio_List();
        [DllImport("__Internal")] static extern IntPtr MedPhysio_Def(string id);
        [DllImport("__Internal")] static extern int MedPhysio_Create(string id, string values);
        [DllImport("__Internal")] static extern void MedPhysio_Set(int h, string values);
        [DllImport("__Internal")] static extern void MedPhysio_Act(int h, string id);
        [DllImport("__Internal")] static extern IntPtr MedPhysio_Step(int h, double dt, int maxSamples);
        [DllImport("__Internal")] static extern IntPtr MedPhysio_Curve(int h, int i);
        [DllImport("__Internal")] static extern void MedPhysio_Free(IntPtr p);

        [DllImport("__Internal")] static extern IntPtr MedPhysio_SceneBuild(int h);
        [DllImport("__Internal")] static extern void MedPhysio_SceneDispose(int h);
        [DllImport("__Internal")] static extern int MedPhysio_SceneFrame(int h, float dt);
        [DllImport("__Internal")] static extern int MedPhysio_SceneCopyFrame(int h, float[] dst, int cap);
        [DllImport("__Internal")] static extern IntPtr MedPhysio_SceneStructure(int h);
        [DllImport("__Internal")] static extern int MedPhysio_SceneGeom(int h, int gid, float[] dst, int cap);
        [DllImport("__Internal")] static extern int MedPhysio_SceneIndex(int h, int gid, int tri, int[] dst, int cap);
        [DllImport("__Internal")] static extern int MedPhysio_SceneTex(int h, int tid, byte[] dst, int cap);
        [DllImport("__Internal")] static extern IntPtr MedPhysio_SceneFit(int h, float aspect, float fov);

        [DllImport("__Internal")] static extern int MedPhysio_MuscleOpen(string tab, int phone);
        [DllImport("__Internal")] static extern int MedPhysio_MuscleAct(string cmd, string arg, int phone);
        [DllImport("__Internal")] static extern IntPtr MedPhysio_MuscleView();
        [DllImport("__Internal")] static extern IntPtr MedPhysio_MusclePart(string id);
        [DllImport("__Internal")] static extern int MedPhysio_MuscleChart(string kind, int w, int h, byte[] dst, int cap);
        [DllImport("__Internal")] static extern float MedPhysio_MuscleChartLen(float x, int w);

        static string Take(IntPtr p) { var s = Marshal.PtrToStringUTF8(p); MedPhysio_Free(p); return s; }

        public static bool Available => MedPhysio_Ready() == 1;
        public static string List() => Take(MedPhysio_List());
        public static string Def(string id) => Take(MedPhysio_Def(id));
        public static int Create(string id, string values) => MedPhysio_Create(id, values);
        public static void Set(int h, string values) => MedPhysio_Set(h, values);
        public static void Act(int h, string id) => MedPhysio_Act(h, id);
        public static string Step(int h, double dt, int maxSamples) => Take(MedPhysio_Step(h, dt, maxSamples));
        public static string Curve(int h, int i) => Take(MedPhysio_Curve(h, i));
        // scene mirror (SceneMirror.cs): the web version's own 3D scene for instance h
        public static string SceneBuild(int h) => Take(MedPhysio_SceneBuild(h));
        public static void SceneDispose(int h) => MedPhysio_SceneDispose(h);
        public static int SceneFrame(int h, float dt) => MedPhysio_SceneFrame(h, dt);
        public static int SceneCopyFrame(int h, float[] dst) => MedPhysio_SceneCopyFrame(h, dst, dst.Length);
        public static string SceneStructure(int h) => Take(MedPhysio_SceneStructure(h));
        public static int SceneGeom(int h, int gid, float[] dst) => MedPhysio_SceneGeom(h, gid, dst, dst.Length);
        public static int SceneIndex(int h, int gid, bool tri, int[] dst) => MedPhysio_SceneIndex(h, gid, tri ? 1 : 0, dst, dst.Length);
        public static int SceneTex(int h, int tid, byte[] dst) => MedPhysio_SceneTex(h, tid, dst, dst.Length);
        public static string SceneFit(int h, float aspect, float fov) => Take(MedPhysio_SceneFit(h, aspect, fov));
        // muscle section (MuscleApp.cs)
        public static int MuscleOpen(string tab, bool phone) => MedPhysio_MuscleOpen(tab ?? "", phone ? 1 : 0);
        public static int MuscleAct(string cmd, string arg, bool phone) => MedPhysio_MuscleAct(cmd, arg ?? "", phone ? 1 : 0);
        public static string MuscleView() => Take(MedPhysio_MuscleView());
        public static string MusclePart(string id) => Take(MedPhysio_MusclePart(id));
        public static int MuscleChart(string kind, int w, int h, byte[] dst) => MedPhysio_MuscleChart(kind, w, h, dst, dst.Length);
        public static float MuscleChartLen(float x, int w) => MedPhysio_MuscleChartLen(x, w);
#else
        public static bool Available => false;
        public static string List() => "[]";
        public static string Def(string id) => null;
        public static int Create(string id, string values) => -1;
        public static void Set(int h, string values) { }
        public static void Act(int h, string id) { }
        public static string Step(int h, double dt, int maxSamples) => null;
        public static string Curve(int h, int i) => "[]";
        public static string SceneBuild(int h) => null;
        public static void SceneDispose(int h) { }
        public static int SceneFrame(int h, float dt) => 0;
        public static int SceneCopyFrame(int h, float[] dst) => 0;
        public static string SceneStructure(int h) => "{}";
        public static int SceneGeom(int h, int gid, float[] dst) => 0;
        public static int SceneIndex(int h, int gid, bool tri, int[] dst) => 0;
        public static int SceneTex(int h, int tid, byte[] dst) => 0;
        public static string SceneFit(int h, float aspect, float fov) => "[]";
        public static int MuscleOpen(string tab, bool phone) => -1;
        public static int MuscleAct(string cmd, string arg, bool phone) => -1;
        public static string MuscleView() => "{}";
        public static string MusclePart(string id) => "null";
        public static int MuscleChart(string kind, int w, int h, byte[] dst) => 0;
        public static float MuscleChartLen(float x, int w) => 2.1f;
#endif
    }
}
