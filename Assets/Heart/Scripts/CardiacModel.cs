using System;
using UnityEngine;

namespace MedAtlas.Heart
{
    public enum CardiacPhase
    {
        AtrialSystole = 0,          // Systole atriorum
        IsovolumetricContraction,   // Contractio isovolumetrica
        RapidEjection,              // Ejectio rapida
        ReducedEjection,            // Ejectio reducta
        IsovolumetricRelaxation,    // Relaxatio isovolumetrica
        RapidFilling,               // Repletio rapida
        Diastasis                   // Diastasis (repletio lenta)
    }

    public enum CardiacEvent { MitralClose, TricuspidClose, AorticOpen, PulmonaryOpen, AorticClose, PulmonaryClose, MitralOpen, TricuspidOpen, QRS, PWave }

    /// <summary>Parameters (same set and meaning as the web version medatlas-3d, src/physio/models/cardio.ts).</summary>
    [Serializable]
    public class CardioParams
    {
        public float hr = 72;               // beats/min (pacemaker)
        public float contractility = 1;     // × Emax
        public float svr = 1;               // × systemic resistance
        public float venousTone = 1;        // × (lower unstressed venous volume)
        public float volume = 0;            // ml added (+) / lost (−)
        public float aorticStenosis = 0;    // 0..1
        public float mitralRegurg = 0;      // 0..1
        public float aorticRegurg = 0;      // 0..1
        public float stiffness = 1;         // × Emin
        public float pericardial = 0;       // mmHg (tamponade)
        public bool af = false;             // atrial fibrillation
        public bool baroreflex = true;
        public float pulmonaryResistance = 1;
        public float wall = 1;              // LV wall thickness ×
        public bool homeometric = true;     // Bowditch + Anrep
        public float mapSet = 93;           // baroreflex set point
        public CardioParams Clone() => (CardioParams)MemberwiseClone();
    }

    /// <summary>
    /// Lumped-parameter model of the whole circulation, ported from the web version (cardio.ts):
    /// four time-varying-elastance chambers, systemic and pulmonary arteries/veins, baroreflex,
    /// homeometric autoregulation (Bowditch, Anrep), L/R asynchrony, valve disease, AF and tamponade.
    /// Added for the 3D heart: valve inertance with a small closing back-flow (dicrotic notch),
    /// aortic/pulmonary characteristic impedance, smoothed leaflet positions and the 7 Wiggers phases.
    /// Units: mmHg, ml, s. Time zero of every beat = onset of QRS.
    /// </summary>
    public class CardiacModel
    {
        public CardioParams P = new CardioParams();
        public float HeartRate { get => P.hr; set => P.hr = value; }

        // ---- base constants (cardio.ts BASE)
        const float LvEmax = 2.9f, LvEmin = 0.07f, LvV0 = 10f;
        const float RvEmax = 0.6f, RvEmin = 0.035f, RvV0 = 10f;
        const float LaEmax = 0.25f, LaEmin = 0.15f, LaV0 = 5f;
        const float RaEmax = 0.25f, RaEmin = 0.12f, RaV0 = 5f;
        const float SaC = 1.15f, SaVu = 700f, SvC = 100f, SvVu = 2050f, PaC = 4.3f, PaVu = 120f, PvC = 9f, PvVu = 330f;
        const float RSys = 1.0f, RVen = 0.05f, RPul = 0.09f, RPv = 0.01f;
        const float RMitral = 0.005f, RAortic = 0.008f, RTric = 0.004f, RPulm = 0.004f;
        const float Zao = 0.03f, Zpa = 0.01f;   // characteristic impedances (3D version)

        public class Valve
        {
            public readonly string Name;
            public float R; readonly float L, closeVol;
            public float Q;            // forward flow ml/s
            public bool Open;
            float reg;
            public float Leaflet;      // 0 closed .. 1 fully open (visual, smoothed)
            public float MinLeaflet, MaxLeaflet = 1f;   // regurgitation / stenosis limits
            public Valve(string n, float r, float l, float cv) { Name = n; R = r; L = l; closeVol = cv; }
            public float Step(float dp, float dt, float z)
            {
                if (!Open)
                {
                    if (dp > 0f) { Open = true; reg = 0f; }
                    else { Q = 0f; return 0f; }
                }
                Q += dt * (dp - (R + z) * Q) / L;
                if (Q < 0f)
                {
                    reg += -Q * dt;
                    if (reg > closeVol) { Open = false; Q = 0f; }
                }
                else reg = 0f;
                return Q;
            }
            public void Visual(float dt)
            {
                float target = Open ? MaxLeaflet : MinLeaflet;
                float tau = Open ? 0.030f : 0.022f;
                Leaflet += (target - Leaflet) * (1f - Mathf.Exp(-dt / tau));
            }
        }

        public readonly Valve Mitral = new Valve("mitral", RMitral, 0.0004f, 3.0f);
        public readonly Valve Aortic = new Valve("aortic", RAortic, 0.0005f, 1.2f);
        public readonly Valve Tricuspid = new Valve("tricuspid", RTric, 0.0004f, 3.0f);
        public readonly Valve Pulmonary = new Valve("pulmonary", RPulm, 0.0005f, 1.2f);

        // ---- state (volumes)
        public float Vla, Vlv, Vsa, Vsv, Vra, Vrv, Vpa, Vpv;
        float lastVolume;

        // ---- outputs
        public float Plv, Pla, Prv, Pra, Pao, Ppa, Psa, Psv, Ppv, PpaC;
        public float Qsys, Qven, Qpul, Qpv, Qmr, Qar;   // Qmr: LV→LA, Qar: Ao→LV (regurgitant)
        public float En, Ea;
        public float Ecg;
        public double Time;
        public float CycleTime;            // time since QRS onset
        public int Cycle;
        float beatLen = 60f / 72f;
        public float Period => beatLen;
        public CardiacPhase Phase = CardiacPhase.Diastasis;
        public float PhaseTime;

        // baroreflex and homeometric autoregulation
        public float RefHr = 1, RefContr = 1, RefSvr = 1, RefVu = 0;
        public float Bowditch = 1, Anrep = 0;
        float mapFilt = 93;
        public float MapFilt => mapFilt;
        uint rng = 1;

        // ---- per-beat statistics (last completed beat)
        public float EDV = 120, ESV = 50, AoSys = 120, AoDia = 80, Map = 93, PaSys = 25, PaDia = 10, LvMax = 120, RvMax = 25;
        public float RvEDV = 120, RvESV = 50, Lap = 8, Cvp = 4, Lvedp = 8, Gradient = 0, RegFraction = 0, DpDt = 1200, ActualHr = 72, ForwardSV = 70;
        public float SV => EDV - ESV;
        public float EF => EDV > 1 ? SV / EDV : 0;
        public float CO = 5f;  // l/min, smoothed over beats

        // running per-beat trackers
        float cMaxV, cMinV = 1e9f, cAoMax, cAoMin = 1e9f, cPaMax, cPaMin = 1e9f, cLvMax, cRvMax, cRvMaxV, cRvMinV = 1e9f;
        float cMapSum, cLapSum, cCvpSum, cGrad, cFwd, cBackAr, cBackMr, cDpdt, cLvedp; int cN;
        float peakFill, prevPlv;
        bool lvPeakPassed, captureEd;

        public event Action<CardiacEvent> OnEvent;
        public event Action<CardiacPhase> OnPhase;

        // ---- sweep traces for the Wiggers diagram
        public const int TraceN = 720;
        public readonly float[] TrPao = new float[TraceN], TrPlv = new float[TraceN], TrPla = new float[TraceN];
        public readonly float[] TrPpa = new float[TraceN], TrPrv = new float[TraceN], TrPra = new float[TraceN];
        public readonly float[] TrVlv = new float[TraceN], TrVrv = new float[TraceN], TrEcg = new float[TraceN];
        public readonly byte[] TrPhase = new byte[TraceN];
        public readonly byte[] TrMark = new byte[TraceN];   // 1 = S1, 2 = S2
        int lastIdx = -1;
        public const float SweepLead = 0.22f;
        public float SweepX => Mathf.Repeat((CycleTime + SweepLead) / Period, 1f);

        public CardiacModel() { Reset(); }

        public void SetParams(CardioParams p, bool reset)
        {
            P = p.Clone();
            if (reset) Reset();
        }

        public void Reset()
        {
            Vla = 60; Vlv = 120; Vsa = SaVu + 95 * SaC; Vsv = SvVu + 8 * SvC; Vra = 55; Vrv = 125; Vpa = PaVu + 17 * PaC; Vpv = PvVu + 9 * PvC;
            lastVolume = 0;
            RefHr = RefContr = RefSvr = 1; RefVu = 0; Bowditch = 1; Anrep = 0; mapFilt = P.mapSet;
            Time = 0; CycleTime = 0; Cycle = 0; beatLen = 60f / Mathf.Max(20, P.hr);
            ResetTrackers();
            Phase = CardiacPhase.IsovolumetricContraction;
            int steps = Mathf.CeilToInt(10f / DT);  // settle ~10 s (reflexes)
            for (int i = 0; i < steps; i++) Integrate(DT, false);
            Array.Clear(TrMark, 0, TraceN);
        }

        void ResetTrackers()
        {
            cMaxV = 0; cMinV = 1e9f; cAoMax = 0; cAoMin = 1e9f; cPaMax = 0; cPaMin = 1e9f; cLvMax = 0; cRvMax = 0;
            cRvMaxV = 0; cRvMinV = 1e9f; cMapSum = cLapSum = cCvpSum = 0; cGrad = 0; cFwd = cBackAr = cBackMr = 0; cDpdt = 0; cN = 0;
        }

        float Rand() { rng = rng * 1664525u + 1013904223u; return (rng >> 8) / 16777216f; }

        public float EffectiveHr => Mathf.Clamp(P.hr * (P.baroreflex ? RefHr : 1f), 20f, 220f);

        // half-cosine activation used by the web model: rise over ts, relax over ts/2
        static float Activation(float t, float ts)
        {
            if (t < 0) return 0;
            if (t < ts) return 0.5f * (1 - Mathf.Cos(Mathf.PI * t / ts));
            if (t < 1.5f * ts) return 0.5f * (1 + Mathf.Cos(Mathf.PI * (t - ts) / (0.5f * ts)));
            return 0;
        }

        public float Ts => 0.12f + 0.2f * Mathf.Sqrt(60f / EffectiveHr);
        public float PR => Mathf.Min(0.16f, (60f / EffectiveHr) * 0.2f);

        float EcgAt(float tb, float tA, float ts)
        {
            float G(float x, float c, float w, float a) => a * Mathf.Exp(-((x - c) * (x - c)) / (2 * w * w));
            float e = 0;
            if (P.af) e += 0.05f * Mathf.Sin((float)Time * 41f) + 0.04f * Mathf.Sin((float)Time * 29f + 1f);
            else e += G(tA, 0.04f, 0.022f, 0.15f);
            e += G(tb, -0.005f, 0.008f, -0.1f) + G(tb, 0.012f, 0.009f, 1.1f) + G(tb, 0.03f, 0.009f, -0.3f);
            e += G(tb, ts * 0.95f, ts * 0.13f, 0.3f);
            return e;
        }

        public const float DT = 0.0002f;

        public void Advance(float dt)
        {
            if (P.volume != lastVolume) { Vsv += P.volume - lastVolume; lastVolume = P.volume; }
            int n = Mathf.Max(1, Mathf.RoundToInt(dt / DT));
            float h = dt / n;
            for (int i = 0; i < n; i++) Integrate(h, true);
        }

        void Integrate(float h, bool record)
        {
            var p = P;
            bool br = p.baroreflex;
            float rContr = br ? RefContr : 1, rSvr = br ? RefSvr : 1, rVu = br ? RefVu : 0;

            // beat timing (irregular in AF)
            if (CycleTime >= beatLen)
            {
                FinishBeat();
                CycleTime -= beatLen;
                Cycle++;
                float baseLen = 60f / EffectiveHr;
                beatLen = p.af ? baseLen * (0.6f + Rand() * 0.8f) : baseLen;
                Fire(CardiacEvent.QRS);
                captureEd = true;
            }
            float tb = CycleTime;
            float ts = Ts, pr = PR;
            float tA = tb - (beatLen - pr);
            float actV = Activation(tb, ts);
            float actVr = Activation(tb - 0.01f, ts);           // RV starts ~10 ms after LV
            float actA = p.af ? 0 : Activation(tA - 0.02f, 0.1f); // RA ~20 ms before LA
            float actAl = p.af ? 0 : Activation(tA, 0.1f);
            En = actV; Ea = Mathf.Max(actA, actAl);

            // homeometric autoregulation
            if (p.homeometric)
            {
                float target = Mathf.Clamp(1 + 0.35f * (EffectiveHr / 72f - 1), 0.8f, 1.45f);
                Bowditch += (target - Bowditch) * Mathf.Min(1, h / 8);
                float load = Mathf.Clamp(0.3f * (mapFilt / 93f - 1), -0.1f, 0.3f);
                Anrep += (load - Anrep) * Mathf.Min(1, h / 45);
            }
            else
            {
                Bowditch += (1 - Bowditch) * Mathf.Min(1, h / 8);
                Anrep += (0 - Anrep) * Mathf.Min(1, h / 45);
            }
            float contr = p.contractility * rContr * Bowditch * (1 + Anrep);

            float El(float emin, float emax, float a, float k) => emin * p.stiffness + (emax * k - emin * p.stiffness) * a;
            float peri = p.pericardial;
            Plv = El(LvEmin, LvEmax, actV, contr) * (Vlv - LvV0) + peri;
            Prv = El(RvEmin, RvEmax, actVr, contr) * (Vrv - RvV0) + peri;
            Pla = El(LaEmin, LaEmax, actAl, 1) * (Vla - LaV0) + peri;
            Pra = El(RaEmin, RaEmax, actA, 1) * (Vra - RaV0) + peri;
            Psa = (Vsa - SaVu) / SaC;
            Psv = (Vsv - SvVu / p.venousTone + rVu) / SvC;
            PpaC = (Vpa - PaVu) / PaC;
            if (captureEd) { cLvedp = Plv; captureEd = false; }
            Ppv = (Vpv - PvVu) / PvC;

            bool miWas = Mitral.Open, aoWas = Aortic.Open, trWas = Tricuspid.Open, puWas = Pulmonary.Open;
            Aortic.R = RAortic * (1 + 60 * p.aorticStenosis * p.aorticStenosis);
            float qmi = Mitral.Step(Pla - Plv, h, 0f);
            float qao = Aortic.Step(Plv - Psa, h, Zao);
            float qtr = Tricuspid.Step(Pra - Prv, h, 0f);
            float qpu = Pulmonary.Step(Prv - PpaC, h, Zpa);
            // regurgitant jets through incompetent valves: orifice flow ∝ k·√Δp
            Qmr = (!Mitral.Open && p.mitralRegurg > 0 && Plv > Pla) ? p.mitralRegurg * 22f * Mathf.Sqrt(Plv - Pla) : 0f;
            Qar = (!Aortic.Open && p.aorticRegurg > 0 && Psa > Plv) ? p.aorticRegurg * 22f * Mathf.Sqrt(Psa - Plv) : 0f;
            Pao = Psa + Zao * (qao - Qar);
            Ppa = PpaC + Zpa * qpu;

            Qsys = (Psa - Psv) / (RSys * p.svr * rSvr);
            Qven = Mathf.Max(0, (Psv - Pra) / RVen);
            Qpul = (PpaC - Ppv) / (RPul * p.pulmonaryResistance);
            Qpv = Mathf.Max(0, (Ppv - Pla) / RPv);

            Vla += h * (Qpv - qmi + Qmr);
            Vlv += h * (qmi - qao - Qmr + Qar);
            Vsa += h * (qao - Qar - Qsys);
            Vsv += h * (Qsys - Qven);
            Vra += h * (Qven - qtr);
            Vrv += h * (qtr - qpu);
            Vpa += h * (qpu - Qpul);
            Vpv += h * (Qpul - Qpv);
            if (Vla < 1) Vla = 1; if (Vlv < 1) Vlv = 1; if (Vra < 1) Vra = 1; if (Vrv < 1) Vrv = 1;

            // baroreflex: slow correction of the mean arterial pressure towards the set point
            mapFilt += (Psa - mapFilt) * h * 0.6f;
            if (br)
            {
                float err = Mathf.Clamp(p.mapSet - mapFilt, -45, 45) / 93f;
                float k = h / 6f;
                RefHr += (Mathf.Clamp(1 + 1.9f * err, 0.6f, 2.4f) - RefHr) * k;
                RefContr += (Mathf.Clamp(1 + 0.8f * err, 0.7f, 1.6f) - RefContr) * k;
                RefSvr += (Mathf.Clamp(1 + 1.1f * err, 0.6f, 1.8f) - RefSvr) * k;
                RefVu += (Mathf.Clamp(2000 * err, -250, 900) - RefVu) * k;
            }

            Mitral.MinLeaflet = 0.18f * p.mitralRegurg;
            Aortic.MinLeaflet = 0.18f * p.aorticRegurg;
            Aortic.MaxLeaflet = 1f - 0.75f * p.aorticStenosis;
            Mitral.Visual(h); Aortic.Visual(h); Tricuspid.Visual(h); Pulmonary.Visual(h);

            // trackers
            cMaxV = Mathf.Max(cMaxV, Vlv); cMinV = Mathf.Min(cMinV, Vlv);
            cRvMaxV = Mathf.Max(cRvMaxV, Vrv); cRvMinV = Mathf.Min(cRvMinV, Vrv);
            cAoMax = Mathf.Max(cAoMax, Pao); cAoMin = Mathf.Min(cAoMin, Pao);
            cPaMax = Mathf.Max(cPaMax, Ppa); cPaMin = Mathf.Min(cPaMin, Ppa);
            cLvMax = Mathf.Max(cLvMax, Plv); cRvMax = Mathf.Max(cRvMax, Prv);
            cMapSum += Psa; cLapSum += Pla; cCvpSum += Pra; cN++;
            if (Aortic.Open) cGrad = Mathf.Max(cGrad, Plv - Pao);
            cFwd += qao * h; cBackAr += Qar * h; cBackMr += Qmr * h;
            if (cN > 1) cDpdt = Mathf.Max(cDpdt, (Plv - prevPlv) / h);

            // events
            byte mark = 0;
            if (miWas && !Mitral.Open && En > 0.01f) { Fire(CardiacEvent.MitralClose); mark = 1; }
            if (trWas && !Tricuspid.Open && En > 0.01f) Fire(CardiacEvent.TricuspidClose);
            if (!aoWas && Aortic.Open) Fire(CardiacEvent.AorticOpen);
            if (!puWas && Pulmonary.Open) Fire(CardiacEvent.PulmonaryOpen);
            if (aoWas && !Aortic.Open) { Fire(CardiacEvent.AorticClose); mark = 2; }
            if (puWas && !Pulmonary.Open) Fire(CardiacEvent.PulmonaryClose);
            if (!miWas && Mitral.Open && CycleTime > 0.15f) Fire(CardiacEvent.MitralOpen);
            if (!trWas && Tricuspid.Open && CycleTime > 0.15f) Fire(CardiacEvent.TricuspidOpen);

            UpdatePhase(h, ts);

            float prevCycle = CycleTime;
            CycleTime += h;
            Time += h;
            float pStart = beatLen - pr;
            if (!p.af && prevCycle < pStart && CycleTime >= pStart) Fire(CardiacEvent.PWave);
            Ecg = EcgAt(CycleTime, CycleTime - (beatLen - pr), ts);

            if (record) Record(mark);
            prevPlv = Plv;
        }

        void FinishBeat()
        {
            if (cN < 20) { ResetTrackers(); return; }
            EDV = cMaxV; ESV = cMinV; AoSys = cAoMax; AoDia = cAoMin; PaSys = cPaMax; PaDia = cPaMin;
            LvMax = cLvMax; RvMax = cRvMax; RvEDV = cRvMaxV; RvESV = cRvMinV;
            Map = cMapSum / cN; Lap = cLapSum / cN; Cvp = cCvpSum / cN; Gradient = Mathf.Max(0, cGrad);
            Lvedp = cLvedp; DpDt = cDpdt;
            float sv = Mathf.Max(1, EDV - ESV);
            RegFraction = Mathf.Clamp01((cBackAr + cBackMr) / sv);
            ForwardSV = Mathf.Max(0, cFwd - cBackAr);
            ActualHr = 60f / beatLen;
            CO += (ForwardSV * ActualHr / 1000f - CO) * (P.af ? 0.25f : 0.6f);
            ResetTrackers();
        }

        void Fire(CardiacEvent e) { OnEvent?.Invoke(e); }

        void SetPhase(CardiacPhase ph)
        {
            if (ph == Phase) return;
            Phase = ph; PhaseTime = 0f;
            OnPhase?.Invoke(ph);
        }

        void UpdatePhase(float dt, float ts)
        {
            PhaseTime += dt;
            bool systolicActivation = CycleTime < ts * 1.5f;
            if (Aortic.Open)
            {
                if (Phase != CardiacPhase.RapidEjection && Phase != CardiacPhase.ReducedEjection) { lvPeakPassed = false; SetPhase(CardiacPhase.RapidEjection); }
                else if (Phase == CardiacPhase.RapidEjection && !lvPeakPassed && Plv < prevPlv - 0.0005f && PhaseTime > 0.02f)
                { lvPeakPassed = true; SetPhase(CardiacPhase.ReducedEjection); }
                return;
            }
            if (!Mitral.Open)
            {
                if (Phase == CardiacPhase.RapidEjection || Phase == CardiacPhase.ReducedEjection || Phase == CardiacPhase.IsovolumetricRelaxation)
                    SetPhase(CardiacPhase.IsovolumetricRelaxation);
                else if (systolicActivation && En > 0.005f)
                    SetPhase(CardiacPhase.IsovolumetricContraction);
                return;
            }
            if (Ea > 0.02f && CycleTime > Period * 0.4f) { SetPhase(CardiacPhase.AtrialSystole); return; }
            if (Phase == CardiacPhase.IsovolumetricRelaxation || Phase == CardiacPhase.ReducedEjection || Phase == CardiacPhase.RapidEjection || Phase == CardiacPhase.IsovolumetricContraction)
            { peakFill = 0f; SetPhase(CardiacPhase.RapidFilling); }
            if (Phase == CardiacPhase.RapidFilling)
            {
                peakFill = Mathf.Max(peakFill, Mitral.Q);
                if (PhaseTime > 0.06f && Mitral.Q < 0.3f * peakFill) SetPhase(CardiacPhase.Diastasis);
            }
            else if (Phase != CardiacPhase.AtrialSystole && Phase != CardiacPhase.Diastasis) SetPhase(CardiacPhase.Diastasis);
        }

        void Record(byte mark)
        {
            int idx = Mathf.Clamp((int)(SweepX * TraceN), 0, TraceN - 1);
            if (lastIdx < 0) lastIdx = idx;
            int i = lastIdx, guard = 0;
            while (true)
            {
                TrPao[i] = Pao; TrPlv[i] = Plv; TrPla[i] = Pla;
                TrPpa[i] = Ppa; TrPrv[i] = Prv; TrPra[i] = Pra;
                TrVlv[i] = Vlv; TrVrv[i] = Vrv; TrEcg[i] = Ecg;
                TrPhase[i] = (byte)Phase;
                if (i != lastIdx) TrMark[i] = 0;
                if (i == idx) break;
                i = (i + 1) % TraceN;
                if (++guard > TraceN) break;
            }
            if (mark != 0) TrMark[idx] = mark;
            lastIdx = idx;
        }
    }
}
