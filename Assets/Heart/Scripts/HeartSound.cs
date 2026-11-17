using UnityEngine;

namespace MedAtlas.Heart
{
    /// <summary>Procedurally synthesised heart sounds: S1 ("лъб", AV valve closure) and S2 ("дъп", semilunar closure).</summary>
    public class HeartSound : MonoBehaviour
    {
        AudioSource src;
        AudioClip s1, s2;
        public bool Enabled = true;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.9f;
            s1 = Make("S1", 0.14f, new[] { (0.000f, 48f, 1.0f, 0.030f), (0.025f, 62f, 0.7f, 0.028f) }, 0.20f);
            s2 = Make("S2", 0.10f, new[] { (0.000f, 72f, 0.9f, 0.020f), (0.030f, 64f, 0.55f, 0.018f) }, 0.15f);
        }

        static AudioClip Make(string name, float len, (float t0, float f, float a, float tau)[] comps, float noise)
        {
            int sr = 44100; int n = (int)(len * sr);
            var d = new float[n];
            var rnd = new System.Random(name.GetHashCode());
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, v = 0;
                foreach (var c in comps)
                {
                    float tt = t - c.t0; if (tt < 0) continue;
                    float env = (1f - Mathf.Exp(-tt / 0.004f)) * Mathf.Exp(-tt / c.tau);
                    v += c.a * env * Mathf.Sin(2 * Mathf.PI * c.f * tt * (1f + 0.6f * Mathf.Exp(-tt / 0.02f)));
                }
                // low-passed noise (valve "thud" texture)
                lp += ((float)rnd.NextDouble() * 2 - 1 - lp) * 0.05f;
                float envN = Mathf.Exp(-t / 0.03f);
                v += lp * noise * envN * 4f;
                d[i] = Mathf.Clamp(v * 0.8f, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(d, 0);
            return clip;
        }

        public void PlayS1() { if (Enabled) src.PlayOneShot(s1, 1f); }
        public void PlayS2() { if (Enabled) src.PlayOneShot(s2, 0.85f); }
    }
}
