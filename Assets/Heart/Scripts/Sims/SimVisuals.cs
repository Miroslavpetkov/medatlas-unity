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

        /// <summary>The model's current slider values (they change with the scenario), for models whose channels mean different things per setting.</summary>
        public System.Func<Dictionary<string, object>> Params;
        float P(string key, float def)
        {
            var v = Params?.Invoke();
            if (v == null || !v.TryGetValue(key, out var o)) return def;
            if (o is double d) return (float)d;
            if (o is bool b) return b ? 1 : 0;
            return def;
        }
        float Raw(Dictionary<string, object> now, string ch) => now != null && now.TryGetValue(ch, out var o) && o is double d ? (float)d : float.NaN;
        int lastEnv = -1;

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
                case "vision": G(new[] { "retina" }, Warm, Mathf.Max(N(now, "cone"), N(now, "rod"))); G(new[] { "iris" }, Cool, N(now, "pupil")); G(new[] { "lens" }, Warm, N(now, "accom")); break;
                case "hearing": G(new[] { "eardrum", "ossicle" }, Cool, N(now, "disp")); G(new[] { "cochlea", "Cochlear nerve" }, Warm, N(now, "rate")); break;
                case "touch": G(new[] { "Postcentral", "Supramarginal" }, Hot, Mathf.Max(N(now, "pain"), N(now, "fast"))); break;
                case "speech": G(new[] { "inferior frontal" }, Warm, N(now, "flu")); G(new[] { "Superior temporal", "Supramarginal" }, Cool, N(now, "comp")); break;
                case "memory": G(new[] { "Medial occipitotemporal", "Parahippocampal" }, Warm, N(now, "stored")); G(new[] { "Superior frontal", "Middle frontal" }, Cool, N(now, "held")); break;
                case "sleep":
                    {
                        // hypnogram: 5 awake, 4 REM, 3..0 NREM 1..4 -> depth 0 (awake) .. 1 (deep NREM)
                        float hyp = Raw(now, "hyp"); float depth = float.IsNaN(hyp) ? 0 : (hyp >= 4.5f ? 0 : hyp >= 3.5f ? 0.15f : Mathf.Clamp01((4f - hyp) / 4f));
                        float alert = N(now, "alert");
                        // the cortex is active awake (more with circadian alertness) and in REM, quiet in deep NREM
                        float cx = float.IsNaN(hyp) ? Mathf.Max(0, alert) : hyp >= 4.5f ? 0.35f + 0.5f * Mathf.Max(0, alert) : hyp >= 3.5f ? 0.6f : 0.25f * (1f - depth);
                        G(new[] { "cortex" }, Cool, cx);
                        G(new[] { "Thalamus" }, Warm, Mathf.Max(depth, Mathf.Max(0, N(now, "mel")) * 0.6f)); // spindles / slow waves start in the thalamus; melatonin at night
                        G(new[] { "Adenohypophysis" }, Warm, N(now, "gh"));                                      // the growth hormone pulse of deep sleep
                        break;
                    }
                case "chemo": G(new[] { "tongue" }, Warm, N(now, "gust")); G(new[] { "olfactory", "nasal" }, Cool, N(now, "smell")); break;
                case "ans": G(new[] { "sympathetic" }, Hot, N(now, "symp")); G(new[] { "vagus" }, Cool, N(now, "para")); G(new[] { "heart" }, Warm, N(now, "hr")); G(new[] { "Stomach" }, Cool, N(now, "gut")); break;
                case "vestibular": G(new[] { "vestibule" }, Cool, N(now, "felt")); G(new[] { "Vestibular nerve" }, Warm, N(now, "omega")); break;
                case "reflex": G(new[] { "Rectus femoris", "Vastus" }, Hot, N(now, "fExt")); G(new[] { "biceps femoris" }, Cool, N(now, "fFlex")); G(new[] { "Femoral nerve" }, Warm, N(now, "ia")); break;
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
                // female genital organs (organ_uterus pack): the follicle in the ovary grows with oestradiol, the endometrium with the cycle
                case "cycle": G(new[] { "ovary" }, Warm, Mathf.Max(N(now, "e2"), N(now, "fol"))); G(new[] { "uterus" }, Hot, N(now, "endo")); break;
                case "pregnancy-birth": G(new[] { "uterus" }, Hot, N(now, "uterus")); G(new[] { "cervix" }, Warm, N(now, "dil")); break;
                // male genital organs (organ_male pack): Leydig testosterone lights the testes, sperm output the epididymis and ducts, inhibin (Sertoli) the glands
                case "testis": G(new[] { "testis" }, Warm, N(now, "tt")); G(new[] { "duct" }, Cool, N(now, "sp")); G(new[] { "gland" }, Warm, N(now, "inh") * 0.6f); break;
                // whole-body sims (organ_torso pack)
                case "human": G(new[] { "heart" }, Hot, N(now, "co")); G(new[] { "lung" }, Cool, N(now, "rr")); G(new[] { "artery" }, Warm, N(now, "map")); break;
                case "exercise": G(new[] { "heart" }, Hot, N(now, "hr")); G(new[] { "lung" }, Cool, N(now, "ve")); G(new[] { "Liver" }, Warm, N(now, "lac")); break;
                case "thermo": G(new[] { "Liver" }, Warm, N(now, "m")); G(new[] { "heart", "artery" }, Hot, N(now, "tc")); G(new[] { "lung" }, Cool, N(now, "evap")); break;
                case "acid-base": G(new[] { "lung" }, Cool, N(now, "ve")); G(new[] { "artery" }, Hot, N(now, "pco2")); G(new[] { "kidney" }, Warm, N(now, "nae")); break;
                case "immune": G(new[] { "Spleen" }, Warm, Mathf.Max(N(now, "igm"), N(now, "igg"))); G(new[] { "artery", "vein" }, Hot, N(now, "p")); break;
                case "blood": G(new[] { "kidney" }, Warm, N(now, "epo")); G(new[] { "Liver" }, Cool, N(now, "fer")); G(new[] { "Spleen" }, Hot, N(now, "ret")); break;
                // leg veins and the calf muscle pump (organ_leg pack)
                case "venous-return": G(new[] { "vein" }, Cool, N(now, "pool")); G(new[] { "gastrocnemius", "Soleus" }, Warm, N(now, "co")); break;
                case "liver": G(new[] { "Liver" }, Warm, N(now, "glycogen")); G(new[] { "Pancreas" }, Cool, N(now, "glucose")); break;
                case "enteric": G(new[] { "Stomach" }, Warm, N(now, "stomach")); G(new[] { "Duodenum", "Jejunum" }, Warm, N(now, "small")); G(new[] { "colon", "appendix" }, Cool, N(now, "distal")); G(new[] { "artery" }, Hot, N(now, "flow")); break;
                case "growth-prolactin": G(new[] { "Adenohypophysis" }, Warm, Mathf.Max(N(now, "gh"), N(now, "prl"))); G(new[] { "cortex" }, Cool, N(now, "sleep") * 0.5f); break;
                case "feedback": G(new[] { "artery" }, Hot, N(now, "val")); G(new[] { "heart" }, Warm, N(now, "u")); break;
                // regional blood flow (torso pack)
                case "regional":
                    G(new[] { "heart" }, Hot, N(now, "heart"));
                    G(new[] { "Superior lobe" }, Cool, N(now, "apex"));                       // the apex is the least perfused upright
                    G(new[] { "Middle lobe", "Inferior lobe" }, Cool, N(now, "base"));
                    G(new[] { "kidney" }, Warm, N(now, "kidney"));
                    G(new[] { "Liver", "Spleen", "Stomach" }, Warm, N(now, "splanchnic"));
                    G(new[] { "artery" }, Hot, N(now, "map"));
                    break;
                // special environments (torso pack): the same four channels mean different things per environment
                case "special":
                    {
                        int env = Mathf.RoundToInt(P("env", 0));
                        if (env != lastEnv) { norms.Clear(); lastEnv = env; }
                        G(new[] { "heart" }, Hot, N(now, "hr"));
                        switch (env)
                        {
                            case 0:   // altitude: a SaO2, b PaO2, c ventilation, d haemoglobin
                                { float sa = Raw(now, "a"); G(new[] { "lung" }, Cool, float.IsNaN(sa) ? -1 : Mathf.Clamp01((100f - sa) / 35f) * 0.6f + Mathf.Max(0, N(now, "c")) * 0.4f); }
                                G(new[] { "kidney" }, Warm, N(now, "d"));                    // EPO -> more haemoglobin
                                G(new[] { "Spleen" }, Warm, N(now, "d") * 0.5f);
                                break;
                            case 1:   // diving: a pressure x10, b nitrogen x10, c bubbles, d narcosis
                                G(new[] { "lung" }, Cool, N(now, "a"));                     // Boyle: the gas in the lungs is compressed with depth
                                G(new[] { "vein" }, Hot, N(now, "c"));                      // bubbles return in the venous blood
                                G(new[] { "Liver", "Spleen" }, Warm, N(now, "b") * 0.5f);  // tissues load with nitrogen
                                break;
                            case 2:   // acceleration: a pressure in the head, b at the heart, c retina, d in the feet /4
                                G(new[] { "artery" }, Hot, N(now, "b"));
                                G(new[] { "vein" }, Cool, N(now, "d"));                     // blood pools in the legs
                                G(new[] { "lung" }, Cool, N(now, "c") * 0.5f);
                                break;
                            default:  // weightlessness: a fluid shift, b plasma, c lost muscle, d lost bone
                                G(new[] { "vein" }, Cool, N(now, "a"));                     // fluid moves up to the chest
                                G(new[] { "kidney" }, Warm, N(now, "d"));                   // calcium from the bones leaves in the urine
                                G(new[] { "lung" }, Cool, N(now, "b") * 0.4f);
                                break;
                        }
                        break;
                    }
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
