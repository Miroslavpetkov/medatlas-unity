using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedAtlas.Lungs
{
    /// <summary>Parameters (same set and meaning as the web version medatlas-3d, src/physio/models/resp.ts).</summary>
    [Serializable]
    public class RespParams
    {
        public float rr = 12;            // breaths/min
        public float effort = 5.2f;      // peak inspiratory muscle pressure, cmH2O
        public float resistance = 2;     // cmH2O/(l/s)
        public float compliance = 100;   // ml/cmH2O
        public float deadSpace = 150;    // ml
        public float fio2 = 0.21f;
        public float altitude = 0;       // m
        public float shunt = 0.02f;      // 0..0.6
        public float diffusion = 0;      // 0 normal .. 4
        public float vco2 = 200;         // ml/min
        public float hb = 14;            // g/dl
        public float hco3 = 24;          // mmol/l
        public float temp = 37;          // °C
        public float coHb = 0;           // fraction
        public float dpg = 0;            // P50 shift, mmHg
        public float chemo = 1;          // chemoreflex sensitivity
        public RespParams Clone() => (RespParams)MemberwiseClone();
    }

    /// <summary>
    /// Breathing and gas exchange, ported from the web version (resp.ts): one-compartment lung (R, C)
    /// driven by the inspiratory muscles, passive expiration (air trapping when R×C is long), alveolar
    /// gas equation, shunt and diffusion limitation, Severinghaus/Kelman O2 dissociation curve and the
    /// chemoreflex. Units: ml, cmH2O, mmHg, s.
    /// </summary>
    public class LungModel
    {
        public RespParams P = new RespParams();
        public double T;
        public float V, Flow, Pmus, Palv, Ppl = -5;
        public float PaCO2 = 40, PaO2 = 95, PAO2 = 100, SaO2 = 0.97f;
        public float Eev, Vt = 500, Drive = 1;
        public float CycleTime, CyclePeriod = 5;
        double cycleStart;
        float vMin, vMax;
        readonly List<float> breathVol = new List<float>();

        public LungModel() { Reset(); }

        public void SetParams(RespParams p, bool reset) { P = p.Clone(); if (reset) Reset(); }

        public void Reset()
        {
            T = 0; cycleStart = 0; V = 0; Flow = 0; Eev = 0; Vt = 500; Drive = 1; breathVol.Clear(); vMin = vMax = 0;
            var g = GasTargets(P.rr * (500 - P.deadSpace));
            PaCO2 = g.paco2; PaO2 = g.pao2; SaO2 = g.sao2; PAO2 = g.pAo2;
            // settle: 90 s of breathing so the gases and the chemoreflex reach their steady state
            Advance(90f);
        }

        public float Patm => 760f * Mathf.Exp(-P.altitude / 8400f);
        public float pH => 6.1f + Mathf.Log10(P.hco3 / (0.03f * PaCO2));
        public float Rate => Mathf.Min(45f, P.rr * Mathf.Sqrt(Drive));
        public float EffortNow => P.effort * Mathf.Sqrt(Drive);
        public float MinuteVent => Vt * Rate / 1000f;
        public float AlvVent => Mathf.Max(0, Vt - P.deadSpace) * Rate / 1000f;
        public float CaO2 => 1.34f * P.hb * SaO2 + 0.003f * PaO2;
        public float SpO2 => Mathf.Min(1f, SaO2 + P.coHb * 0.95f);
        public float TimeConstant => P.resistance * P.compliance / 1000f;
        public float P50 => Po2ForSat(0.5f * (1 - P.coHb), pH, P.temp, PaCO2, P.dpg, P.coHb);
        public bool Inspiration => Flow > 20f;
        public bool Expiration => Flow < -20f;

        // ---- O2 dissociation curve
        public static float SatStd(float p) { if (p <= 0) return 0; return 1f / (23400f / (p * p * p + 150f * p) + 1f); }

        public static float VirtualPO2(float po2, float ph, float temp, float pco2, float dpg)
        {
            float f = Mathf.Pow(10f, 0.024f * (37f - temp) + 0.4f * (ph - 7.4f) + 0.06f * Mathf.Log10(40f / Mathf.Max(5f, pco2)));
            return po2 * f * 26.8f / (26.8f + dpg);
        }

        public static float Saturation(float po2, float ph, float temp, float pco2, float dpg, float coHb)
        {
            float shift = 1 + 1.6f * coHb;
            return SatStd(VirtualPO2(po2, ph, temp, pco2, dpg) * shift) * (1 - coHb);
        }

        static float Po2ForSat(float s, float ph, float temp, float pco2, float dpg, float coHb)
        {
            float lo = 0, hi = 700;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2;
                if (Saturation(mid, ph, temp, pco2, dpg, coHb) < s) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        public struct Gas { public float paco2, pAo2, pao2, sao2; }

        public Gas GasTargets(float va)
        {
            var p = P;
            float paco2 = Mathf.Min(140f, 0.863f * p.vco2 / Mathf.Max(0.15f, va / 1000f));
            float pAo2 = Mathf.Max(0, p.fio2 * (Patm - 47f) - paco2 / 0.8f);
            float ph = 6.1f + Mathf.Log10(p.hco3 / (0.03f * paco2));
            float vo2 = p.vco2 / 0.8f;
            float pc = Mathf.Max(0, pAo2 - 2 - p.diffusion * 9f * (vo2 / 250f) * (pAo2 / 100f));
            float sc = Saturation(pc, ph, p.temp, paco2, p.dpg, p.coHb);
            float cap = 1.34f * p.hb * 10f;
            float sv = Mathf.Max(0.15f, sc - vo2 / (5f * cap));
            float sa = (1 - p.shunt) * sc + p.shunt * sv;
            float pao2 = Po2ForSat(sa, ph, p.temp, paco2, p.dpg, p.coHb);
            return new Gas { paco2 = paco2, pAo2 = pAo2, pao2 = Mathf.Min(pao2, pAo2), sao2 = sa };
        }

        public void Advance(float dt)
        {
            const float h = 0.002f;
            int n = Mathf.Max(1, Mathf.RoundToInt(dt / h));
            for (int i = 0; i < n; i++) Tick(dt / n);
        }

        int gasCounter;
        Gas lastGas;

        void Tick(float h)
        {
            var p = P;
            float rr = Rate;
            float period = 60f / Mathf.Max(2f, rr);
            float ti = Mathf.Min(period * 0.42f, 1.6f);
            float tc = (float)(T - cycleStart);
            if (tc >= period)
            {
                cycleStart = T; tc = 0;
                Eev = V;
                Vt = Mathf.Max(0, vMax - vMin);
                breathVol.Add(Vt); if (breathVol.Count > 4) breathVol.RemoveAt(0);
                vMin = V; vMax = V;
            }
            CycleTime = tc; CyclePeriod = period;
            float eff = EffortNow;
            Pmus = tc < ti ? eff * Mathf.Pow(Mathf.Sin(Mathf.PI * tc / (2 * ti)), 1.3f) : eff * Mathf.Exp(-(tc - ti) / 0.12f);
            float e = 1000f / p.compliance;
            Flow = (Pmus - e * V / 1000f) / p.resistance * 1000f;
            V += Flow * h;
            Palv = -Flow / 1000f * p.resistance;
            Ppl = Palv - 5 - e * V / 1000f;
            vMin = Mathf.Min(vMin, V); vMax = Mathf.Max(vMax, V);
            T += h;

            // gas targets change slowly: recompute every 25 steps (50 ms)
            if (gasCounter-- <= 0)
            {
                gasCounter = 25;
                float vt = Vt;
                if (breathVol.Count > 0) { vt = 0; foreach (var b in breathVol) vt += b; vt /= breathVol.Count; }
                lastGas = GasTargets(Mathf.Max(0, vt - p.deadSpace) * rr);
            }
            var g = lastGas;
            float want = Mathf.Clamp(1 + p.chemo * (0.11f * (PaCO2 - 40) + 0.05f * Mathf.Max(0, 60 - PaO2)), 0.4f, 4.5f);
            Drive += (want - Drive) * (h / 8f);
            float k = h / 18f;
            PaCO2 += (g.paco2 - PaCO2) * k;
            PAO2 += (g.pAo2 - PAO2) * h / 6f;
            PaO2 += (g.pao2 - PaO2) * h / 8f;
            SaO2 = Saturation(PaO2, pH, p.temp, PaCO2, p.dpg, p.coHb);
            if (p.shunt > 0 || p.coHb > 0) SaO2 += (g.sao2 - SaO2) * 0.5f;
        }
    }
}
