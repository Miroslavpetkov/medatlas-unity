using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Sims
{
    /// <summary>Maps the live values of each original model to what moves or lights up on the organ
    /// (which brain area works, which gland secretes, urine flow, colour of the liver in jaundice ...).</summary>
    public class SimVisuals
    {
        class Norm { public float lo = float.MaxValue, hi = float.MinValue; public float N(float v) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); return hi - lo < 1e-6f ? 0.5f : (v - lo) / (hi - lo); } }
        readonly Dictionary<string, Norm> norms = new Dictionary<string, Norm>();
        readonly string id;
        readonly OrganView organ;
        bool flowAdded;

        public SimVisuals(string id, OrganView organ) { this.id = id; this.organ = organ; }

        float N(Dictionary<string, object> now, string ch)
        {
            if (now == null || !now.TryGetValue(ch, out var o) || !(o is double d)) return -1;
            if (!norms.TryGetValue(ch, out var n)) norms[ch] = n = new Norm();
            return n.N((float)d);
        }

        static readonly Color Warm = new Color(1f, 0.75f, 0.35f), Cool = new Color(0.4f, 0.75f, 1f), Hot = new Color(1f, 0.35f, 0.25f);

        void G(string[] keys, Color c, float v) { if (v >= 0) organ.Glow(keys, c, v); }

        public void Tick(Dictionary<string, object> now, float dt)
        {
            if (organ == null || !organ.Loaded) return;
            switch (id)
            {
                // nervous system: the cortical area that does the work lights up with the model's activity
                case "motor": G(new[] { "Precentral", "Paracentral" }, Warm, N(now, "direct")); G(new[] { "cerebellum" }, Cool, N(now, "indirect")); break;
                case "vision": G(new[] { "occipital", "Lingual", "Cuneus" }, Warm, Mathf.Max(N(now, "cone"), N(now, "rod"))); break;
                case "hearing": G(new[] { "Superior temporal", "Temporal pole" }, Warm, N(now, "rate")); break;
                case "touch": G(new[] { "Postcentral", "Supramarginal" }, Hot, Mathf.Max(N(now, "pain"), N(now, "fast"))); break;
                case "speech": G(new[] { "inferior frontal" }, Warm, N(now, "flu")); G(new[] { "Superior temporal", "Supramarginal" }, Cool, N(now, "comp")); break;
                case "memory": G(new[] { "Medial occipitotemporal", "Parahippocampal" }, Warm, N(now, "stored")); G(new[] { "Superior frontal", "Middle frontal" }, Cool, N(now, "held")); break;
                case "sleep": G(new[] { "cortex" }, Cool, N(now, "alert")); G(new[] { "hypophysis", "Thalamus" }, Warm, N(now, "mel")); break;
                case "chemo": G(new[] { "Insula", "Orbital" }, Warm, Mathf.Max(N(now, "gust"), N(now, "smell"))); break;
                case "ans": G(new[] { "hypophysis", "brainstem" }, Hot, N(now, "symp")); G(new[] { "Medulla" }, Cool, N(now, "para")); break;
                case "vestibular": G(new[] { "cerebellum", "brainstem" }, Cool, N(now, "felt")); break;
                case "reflex": G(new[] { "brainstem", "Precentral" }, Warm, N(now, "fExt")); break;
                case "action-potential": case "synapse-receptors": G(new[] { "cortex" }, Warm, N(now, "v")); break;
                case "membrane": G(new[] { "cortex" }, Warm, N(now, "vm")); break;
                case "neuron-glia": G(new[] { "cortex" }, Hot, N(now, "ke")); break;
                case "signal": G(new[] { "cortex" }, Warm, N(now, "camp")); break;
                // kidneys: urine runs down the ureters, the kidneys glow with filtration
                case "kidney-filtration": Urine(now, dt, "urine"); G(new[] { "kidney" }, Warm, N(now, "gfr")); break;
                case "water-salt": Urine(now, dt, "urine"); G(new[] { "kidney" }, Cool, N(now, "adh")); break;
                case "potassium": Urine(now, dt, "urine"); G(new[] { "Suprarenal" }, Warm, N(now, "k")); break;
                // glands and digestive organs
                case "glucose-insulin": G(new[] { "Pancreas" }, Warm, N(now, "insulin")); G(new[] { "Liver" }, Cool, N(now, "glucagon")); break;
                case "adrenal": G(new[] { "Suprarenal" }, Warm, N(now, "cort")); break;
                case "digestion": G(new[] { "Stomach" }, Hot, N(now, "acid")); G(new[] { "Pancreas" }, Cool, N(now, "sec")); G(new[] { "Gallbladder" }, Warm, N(now, "cck")); break;
                case "bilirubin": { float t = N(now, "tot"); if (t >= 0) organ.Tint(new[] { "Liver", "Gallbladder" }, new Color(0.85f, 0.7f, 0.15f), t * 0.7f); break; }
                case "metabolism": G(new[] { "Liver" }, Warm, N(now, "glyc")); break;
                case "nutrition": G(new[] { "Liver" }, Warm, N(now, "alb")); break;
                case "smooth": G(new[] { "Stomach" }, Hot, N(now, "f")); break;
                case "cycle": G(new[] { "hypophysis" }, Warm, N(now, "lh")); G(new[] { "Thalamus" }, Cool, N(now, "fsh")); break;
                case "testis": G(new[] { "hypophysis" }, Warm, N(now, "lh")); G(new[] { "Thalamus" }, Cool, N(now, "fsh")); break;
                case "thyroid": G(new[] { "Thyroid gland" }, Warm, N(now, "ft4")); break;
                case "calcium": G(new[] { "parathyroid" }, Warm, N(now, "pth")); G(new[] { "Thyroid gland" }, Cool, 0.15f); break;
            }
        }

        void Urine(Dictionary<string, object> now, float dt, string ch)
        {
            if (!flowAdded) { organ.AddFlow("Ureter", new Color(1f, 0.9f, 0.4f, 0.9f)); flowAdded = true; }
            float u = N(now, ch);
            organ.TickFlow(dt, u < 0 ? 0.3f : 0.15f + u * 0.85f);
        }
    }
}
