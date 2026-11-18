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

        static string Take(IntPtr p) { var s = Marshal.PtrToStringUTF8(p); MedPhysio_Free(p); return s; }

        public static bool Available => MedPhysio_Ready() == 1;
        public static string List() => Take(MedPhysio_List());
        public static string Def(string id) => Take(MedPhysio_Def(id));
        public static int Create(string id, string values) => MedPhysio_Create(id, values);
        public static void Set(int h, string values) => MedPhysio_Set(h, values);
        public static void Act(int h, string id) => MedPhysio_Act(h, id);
        public static string Step(int h, double dt, int maxSamples) => Take(MedPhysio_Step(h, dt, maxSamples));
        public static string Curve(int h, int i) => Take(MedPhysio_Curve(h, i));
#else
        public static bool Available => false;
        public static string List() => "[]";
        public static string Def(string id) => null;
        public static int Create(string id, string values) => -1;
        public static void Set(int h, string values) { }
        public static void Act(int h, string id) { }
        public static string Step(int h, double dt, int maxSamples) => null;
        public static string Curve(int h, int i) => "[]";
#endif
    }
}
