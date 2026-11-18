using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace MedAtlas.Heart
{
    /// <summary>Loads the large model files from StreamingAssets (separate web files instead of one huge data bundle).</summary>
    public static class ModelFiles
    {
        public const string Version = "18";
        public static IEnumerator Load(string file, System.Action<byte[]> done)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, file);
            if (!path.Contains("://")) path = "file://" + path;
            else path += "?v=" + Version;   // new models reach phones without a stale cache
            using (var req = UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("Cannot load " + path + ": " + req.error); done(null); }
                else done(req.downloadHandler.data);
            }
        }
    }
}
