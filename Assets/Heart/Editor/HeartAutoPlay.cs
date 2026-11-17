using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MedAtlas.Heart.EditorTools
{
    /// <summary>If the file Temp/heart_autoplay.flag exists after a script reload, open the Heart scene and press Play once.</summary>
    [InitializeOnLoad]
    public static class HeartAutoPlay
    {
        const string Flag = "Temp/heart_autoplay.flag";
        static HeartAutoPlay()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(Flag);
                HeartProjectSetup.EnsureAssets();
                if (EditorSceneManager.GetActiveScene().path != HeartProjectSetup.ScenePath)
                    EditorSceneManager.OpenScene(HeartProjectSetup.ScenePath);
                EditorApplication.EnterPlaymode();
            };
        }
    }
}
