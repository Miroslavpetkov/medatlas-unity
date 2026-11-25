using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MedAtlas.Heart
{
    /// <summary>
    /// Quality profiles Low / Medium / High, chosen automatically from the device (Auto, the default) or by hand.
    /// Low (weak phones): 30 FPS lock, no MSAA, no bloom, lower render scale, lighter levels of detail sooner.
    /// Medium (other phones): 60 FPS, slightly lower render scale. High (computers): the full picture.
    /// In Auto a governor watches the frame rate and steps down one profile when the device cannot keep up
    /// (the same idea as the web version's "auto" quality). The choice is kept in PlayerPrefs.
    /// </summary>
    public class QualityProfile : MonoBehaviour
    {
        public enum Level { Auto = 0, Low = 1, Medium = 2, High = 3 }

        const string Key = "medatlas-quality";
        static QualityProfile inst;
        static bool saved; static float baseScale = 1; static int baseMsaa = 1;

        public static Level Choice => (Level)Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, 3);
        public static Level Active { get; private set; } = Level.High;
        public static event System.Action Changed;

        public static string Label(Level l) => l == Level.Auto ? "Авто" : l == Level.Low ? "Ниско" : l == Level.Medium ? "Средно" : "Високо";

        /// <summary>Apply the saved choice (call once the scene's camera and post volume exist).</summary>
        public static void Boot()
        {
            if (!inst)
            {
                var go = new GameObject("QualityProfile");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<QualityProfile>();
            }
            Apply(Choice == Level.Auto ? (Active != Level.High && inst.stepped ? Active : Detect()) : Choice);
        }

        /// <summary>Next choice in the cycle Auto → High → Medium → Low → Auto.</summary>
        public static void Cycle()
        {
            var c = Choice;
            var next = c == Level.Auto ? Level.High : c == Level.High ? Level.Medium : c == Level.Medium ? Level.Low : Level.Auto;
            PlayerPrefs.SetInt(Key, (int)next); PlayerPrefs.Save();
            if (inst) inst.stepped = false;
            Apply(next == Level.Auto ? Detect() : next);
        }

        /// <summary>Profile for this device: computers High; phones Medium, weak phones (few cores / little memory) Low.</summary>
        public static Level Detect()
        {
            if (!Application.isMobilePlatform) return Level.High;
            int cores = SystemInfo.processorCount, mem = SystemInfo.systemMemorySize;
            if ((cores > 0 && cores <= 4) || (mem > 0 && mem < 3000)) return Level.Low;
            return Level.Medium;
        }

        public static void Apply(Level l)
        {
            if (l == Level.Auto) l = Detect();
            Active = l;
            Application.targetFrameRate = l == Level.Low ? 30 : 60;
            // (not in the editor: there a change to the pipeline asset would be saved into the project)
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset a)
            {
                // never above what the project was built with; only lower it
                if (!saved) { saved = true; baseScale = a.renderScale; baseMsaa = a.msaaSampleCount; }
                a.renderScale = l == Level.Low ? Mathf.Min(baseScale, 0.7f) : l == Level.Medium ? Mathf.Min(baseScale, 0.9f) : baseScale;
                a.msaaSampleCount = l == Level.Low ? 1 : baseMsaa;
            }
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.sharedProfile != null && v.sharedProfile.TryGet<Bloom>(out var bl)) bl.active = l != Level.Low;
            MedAtlas.Sims.PhysioBridge.LodBias(l == Level.Low ? 0.6f : l == Level.Medium ? 0.8f : 1f);
            if (inst) inst.Restart();
            Changed?.Invoke();
        }

        // ---- governor (Auto only): about 2 s of frames under the profile's floor steps down one profile
        bool stepped; float acc; int frames; float wait = 3;

        void Restart() { acc = 0; frames = 0; wait = 3; }

        void Update()
        {
            if (Choice != Level.Auto || Active == Level.Low) return;
            float dt = Time.unscaledDeltaTime;
            if (wait > 0) { wait -= dt; return; }   // let a new scene settle (loading spikes)
            acc += dt; frames++;
            if (acc < 2f) return;
            float fps = frames / acc;
            acc = 0; frames = 0;
            if (fps < (Active == Level.High ? 40f : 24f))
            {
                stepped = true;
                Apply(Active == Level.High ? Level.Medium : Level.Low);
            }
        }
    }
}
