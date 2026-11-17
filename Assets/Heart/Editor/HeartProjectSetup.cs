using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedAtlas.Heart.EditorTools
{
    /// <summary>Creates the Heart scene and material assets, and opens the scene the first time the project is loaded.</summary>
    [InitializeOnLoad]
    public static class HeartProjectSetup
    {
        public const string ScenePath = "Assets/Heart/Scenes/Heart.unity";
        const string MatDir = "Assets/Heart/Resources/Materials";
        const string PrefKey = "MedAtlas.Heart.SceneOpened.v1";

        static HeartProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                EnsureAssets();
                if (!SessionState.GetBool(PrefKey, false) && !EditorPrefs.GetBool(PrefKey + Application.dataPath, false))
                {
                    SessionState.SetBool(PrefKey, true);
                    EditorPrefs.SetBool(PrefKey + Application.dataPath, true);
                    if (SceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
                        EditorSceneManager.OpenScene(ScenePath);
                }
            };
        }

        [MenuItem("MedAtlas/Отвори сцената със сърцето")]
        public static void OpenHeartScene()
        {
            EnsureAssets();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void EnsureAssets()
        {
            EnsureMaterials();
            if (!File.Exists(ScenePath)) CreateScene();
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != ScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir))
            {
                Directory.CreateDirectory(MatDir);
                AssetDatabase.Refresh();
            }
            Make("Lit", "Universal Render Pipeline/Lit", m => { m.SetFloat("_Smoothness", 0.65f); });
            Make("Highlight", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.75f, 0.3f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.05f));
            });
            Make("XRay", "MedAtlas/XRay", m => { });
            Make("Blood", "Universal Render Pipeline/Particles/Unlit", m =>
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_BLENDMODE_ADD");
                m.renderQueue = 3100;
            });
        }

        static void Make(string name, string shader, System.Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            if (File.Exists(path)) return;
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogWarning("Shader not found: " + shader); return; }
            var m = new Material(sh);
            setup(m);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("HeartApp");
            go.AddComponent<HeartApp>();
            RenderSettings.skybox = null;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        // -------- batch entry points (used from the command line)
        public static void BatchSetup()
        {
            EnsureAssets();
            Debug.Log("HEART_SETUP_OK");
        }

        public static void TestScenarios()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var sc in Scenarios.All)
            {
                var p = new CardioParams(); sc.Apply(p);
                var m = new CardiacModel(); m.SetParams(p, true);
                m.Advance(20f);
                int[] phaseCount = new int[7];
                for (int i = 0; i < 2000; i++) { m.Advance(0.001f); phaseCount[(int)m.Phase]++; }
                Debug.Log(string.Format(ci, "SCN {0,-12} HR {1:0} BP {2:0}/{3:0} MAP {4:0} PA {5:0}/{6:0} EDV {7:0} ESV {8:0} EF {9:0}% CO {10:0.0} LAP {11:0.0} CVP {12:0.0} LVEDP {13:0.0} grad {14:0} RF {15:0}% dPdt {16:0} phases {17}",
                    sc.Id, m.ActualHr, m.AoSys, m.AoDia, m.Map, m.PaSys, m.PaDia, m.EDV, m.ESV, m.EF * 100, m.CO, m.Lap, m.Cvp, m.Lvedp, m.Gradient, m.RegFraction * 100, m.DpDt, string.Join(",", phaseCount)));
            }
        }

        public static void BuildWindowsTest()
        {
            EnsureAssets();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "D:/tmp/heartbuild/Heart.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var rep = BuildPipeline.BuildPlayer(opts);
            Debug.Log("HEART_BUILD_RESULT " + rep.summary.result + " errors=" + rep.summary.totalErrors);
        }
    }
}
