using System.Collections.Generic;
using System.Globalization;
using MedAtlas.Heart;
using MedAtlas.Lungs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using D = System.Collections.Generic.Dictionary<string, object>;

namespace MedAtlas.Sims
{
    /// <summary>Generic simulation screen for any medAtlas physiology model (scene "Sim"):
    /// phase card, scenarios, parameters, actions, values, strip charts, XY plots, key ideas, and a 3D organ
    /// for the heart / circulation and lung models.</summary>
    public class SimApp : MonoBehaviour
    {
        public static string SelectedId = "blood-pressure";
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        D def; int handle = -1; Dictionary<string, object> values;
        string presetId;
        float speedK = 1; bool paused;
        Camera cam; OrbitCamera orbit;
        // 3D
        HeartModel heart; CardiacModel heartSim; LungAnatomy lungs; LungModel lungSim; OrganView organ; float actMax = 1e-6f; SimVisuals visuals; JourneyView journey; ResponsiveLayout layout; bool focus; MicroScene micro;
        // UI
        RectTransform card, scenPanel, parPanel, learnPanel;
        Text phaseTitle, phaseText, scenTitle, scenText, statsLabels, statsValues;
        Image phaseDot;
        UIKit.Btn pauseBtn, scenBtn, parBtn, learnBtn; readonly List<UIKit.Btn> speedBtns = new List<UIKit.Btn>();
        readonly List<System.Action> sync = new List<System.Action>();
        class Strip { public TraceGraph g; public List<string> ch = new List<string>(); public List<float[]> buf = new List<float[]>(); public float window, acc; public Text legend; }
        class XY { public TraceGraph g; public string x, y; public float trail; public float[] bx, by; public int n; public TraceGraph.Series curve; public bool hasCurve; public int index; }
        readonly List<Strip> strips = new List<Strip>(); readonly List<XY> xys = new List<XY>();
        const int N = 400;
        float sampleDt = 0.004f; float curveTimer;
        float statsH; float opacity = 1f; bool journeyPack; string packName; float viewShiftX;
        void ApplyOpacity(float o)
        {
            opacity = o;
            if (heart != null) heart.SetOpacity(o);
            if (lungs != null) lungs.SetOpacity(o);
            if (journey != null) journey.Opacity = o;
            else if (organ != null && organ.Loaded && !journeyPack) organ.SetOpacity(o);
        } float GraphW => MiniJson.L(def.ContainsKey("xy") ? def["xy"] : null).Count > 0 ? 720 : 620;
        bool ready;

        void Start()
        {
            Application.targetFrameRate = 60;
            HeartApp.BuildStudio(out cam, out orbit);
            UIKit.Init();
            var cl = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(cl, "-organ");
            if (oi >= 0 && oi + 1 < cl.Length)
            {
                Build3D(cl[oi + 1] == "brain" ? "Nerve" : "Kidney", "debug");
                StartCoroutine(Shot(cl[System.Array.IndexOf(cl, "-autoshot") + 1]));
                return;
            }
            if (!PhysioBridge.Available) { BuildMessage("Тази симулация работи в уеб версията (линка). В Unity редактора са достъпни „Сърце“ и „Бели дробове“."); return; }
            def = MiniJson.O(MiniJson.Parse(PhysioBridge.Def(SelectedId)));
            if (def == null) { BuildMessage("Няма такава симулация: " + SelectedId); return; }
            sampleDt = (float)MiniJson.D(def, "sampleDt", 0.004);
            values = new D(MiniJson.O(def["defaults"]));
            var presets = MiniJson.L(def["presets"]);
            if (presets.Count > 0) { var p0 = MiniJson.O(presets[0]); presetId = MiniJson.S(p0, "id"); foreach (var kv in MiniJson.O(p0["values"])) values[kv.Key] = kv.Value; }
            handle = PhysioBridge.Create(SelectedId, MiniJson.Write(values));
            var speeds = MiniJson.L(def["speeds"]);
            if (speeds.Count > 0) speedK = (float)MiniJson.D(MiniJson.O(speeds[0]), "k", 1);
            Build3D(MiniJson.S(def, "area"), SelectedId);
            BuildUI();
            ready = true;
        }

        System.Collections.IEnumerator Shot(string dir)
        {
            yield return new WaitForSeconds(4f);
            System.IO.Directory.CreateDirectory(dir);
            Debug.Log("ORGAN loaded=" + (organ != null && organ.Loaded) + " bounds=" + (organ != null ? organ.Bounds.ToString() : "-") + " children=" + (organ != null ? organ.transform.childCount : -1));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "organ.png"));
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        // ---------------------------------------------------------------- 3D
        public static string PackFor(string area, string id)
        {
            switch (id)
            {
                case "food-journey": case "gas": case "swallow": case "water-journey": case "defecation": case "vomit": return "body";
                case "muscle": return "arm";
                case "smooth": case "adrenal": case "glucose-insulin": case "digestion": case "bilirubin": case "metabolism": case "nutrition": return "abdomen";
                case "thyroid": case "calcium": return "neck";
                case "cycle": case "pregnancy-birth": return "uterus";   // HRA uterus + ovaries (CC BY 4.0), Tools/build_uterus.py
                case "testis": return "male";   // Z-Anatomy male genital organs, Tools/spec_male.json
                // whole-body sims: heart, lungs, great vessels, liver, kidneys, spleen (Tools/spec_torso.json)
                case "human": case "exercise": case "thermo": case "acid-base": case "immune": case "blood": return "torso";
                case "venous-return": case "reflex": return "leg";      // thigh and calf muscles, bones, nerves, leg veins (spec_leg.json)
                case "vision": return "eye";
                case "hearing": case "vestibular": return "ear";
                case "chemo": return "senses_head";                     // tongue, nasal mucosa, olfactory nerves
                case "ans": return "ans";                               // spinal cord, sympathetic trunks, vagus and target organs
                case "liver": case "enteric": return "abdomen";
                case "growth-prolactin": return "brain";                // the pituitary
                case "feedback": return "torso";
                case "regional": case "special": return "torso";   // regional blood flow; altitude, diving, acceleration, weightlessness
                case "skin": return null;
            }
            if (area == "Nerve") return "brain";
            if (area == "Kidney") return "kidney";
            return null;
        }

        /// <summary>The "path through the body" journeys: they use the body pack with the journey view (paths, glass organs).</summary>
        public static bool IsJourney(string id) =>
            id == "food-journey" || id == "gas" || id == "swallow" || id == "water-journey" || id == "defecation" || id == "vomit";

        /// <summary>Camera distance so the whole pack is in view (every pack is ~3 units across its longest side).</summary>
        static float PackDistance(string pack)
        {
            switch (pack)
            {
                case "male": case "ans": return 7.6f;   // tall packs
                case "torso": case "eye": case "ear": return 7.0f;
                case "leg": case "senses_head": return 6.6f;
                case "abdomen": return 6.8f;   // the liver must stay below the info card
                default: return 5.6f;
            }
        }

        /// <summary>Start opacity (1-100): glass where outer parts hide what the simulation is about.</summary>
        static float StartOpacity(string id, string pack)
        {
            if (IsJourney(id)) return 35;
            switch (pack) { case "eye": return 45; case "torso": case "ans": return 60; default: return 100; }   // special and regional use the torso at 60 %
        }

        /// <summary>Camera distance per cell scene, so the whole scene sits between the info card and the charts.</summary>
        static float MicroDistance(MicroScene.Kind k)
        {
            switch (k)
            {
                case MicroScene.Kind.Synapse: return 14f;
                case MicroScene.Kind.Sarcomere: return 15f;
                case MicroScene.Kind.Nephron: return 15.5f;
                case MicroScene.Kind.Skin: return 17f;
                case MicroScene.Kind.Smooth: return 11.5f;
                case MicroScene.Kind.Membrane: return 10f;
                case MicroScene.Kind.Vessel: return 13.5f;
                default: return 9.2f;   // axon
            }
        }

        static Bounds MeshBounds(Transform root)
        {
            var b = new Bounds(); bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // particles move; frame the fixed structures
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public static MicroScene.Kind? MicroFor(string id)
        {
            switch (id)
            {
                case "membrane": case "cardiac-ap": case "signal": case "vesicles": case "potassium": return MicroScene.Kind.Membrane;
                case "action-potential": case "electrotonic": case "nerve-fibres": return MicroScene.Kind.Axon;
                case "haemodynamics": case "blood-flow": case "capillary": case "haemostasis": case "bloodgroups":
                case "leukocytes": case "haemopoiesis": case "plasma": case "lipids": case "lymph": return MicroScene.Kind.Vessel;
                case "synapse-receptors": case "neuron-glia": return MicroScene.Kind.Synapse;
                case "muscle": return MicroScene.Kind.Sarcomere;
                case "smooth": return MicroScene.Kind.Smooth;
                case "kidney-filtration": case "epithelium": case "clearance": return MicroScene.Kind.Nephron;
                case "skin": case "touch": return MicroScene.Kind.Skin;
            }
            return null;
        }

        void Build3D(string area, string id)
        {
            var mk = MicroFor(id);
            if (mk != null)
            {
                var mroot = new GameObject("Micro"); micro = mroot.AddComponent<MicroScene>(); micro.Build(mk.Value, id);
                orbit.Distance = MicroDistance(mk.Value); orbit.MinDistance = 2f; orbit.MaxDistance = 26f; orbit.Target = Vector3.zero; orbit.Yaw = -20f; orbit.Pitch = 18f;
                // frame what was actually built: aim at the middle of the meshes and back off so all of it fits (30 deg FOV)
                var mb = MeshBounds(mroot.transform);
                if (mb.size.sqrMagnitude > 0.01f)
                {
                    // fit the height (bounding radius) and the width into the ~60 % of the screen left free by the panels
                    float aspect = Mathf.Max(0.5f, cam != null ? cam.aspect : 1.6f);
                    float wide = Mathf.Max(mb.extents.x, mb.extents.z) / (Mathf.Tan(15f * Mathf.Deg2Rad) * aspect * 0.6f);
                    orbit.Target = mb.center; orbit.Distance = Mathf.Clamp(Mathf.Max(Mathf.Max(mb.extents.magnitude * 3.1f, wide), MicroDistance(mk.Value)), 6f, 26f);   // particles (filaments, cells) are not in the bounds: keep a floor per kind
                }
                viewShiftX = mk.Value == MicroScene.Kind.Skin ? 0.08f : 0.11f;   // cell scenes are wide: keep them left of the chart column
                return;
            }
            if (id == "human" || id == "exercise") area = "Heart";
            var pack = PackFor(area, id);
            if (pack != null)
            {
                journeyPack = IsJourney(id); packName = pack;
                var root = new GameObject("Organ"); organ = root.AddComponent<OrganView>(); visuals = new SimVisuals(id, organ) { Params = () => values };
                StartCoroutine(ModelFiles.Load("organ_" + pack + ".wasm", d =>
                {
                    organ.Build(d); root.transform.position = -organ.Bounds.center;
                    if (!journeyPack && opacity < 0.995f) organ.SetOpacity(opacity);
                    if (journeyPack && organ.Loaded)
                    {
                        var fr = JourneyView.Framing(id); var b = organ.Bounds;
                        orbit.Target = new Vector3(0, (fr.yFrac - 0.5f) * b.size.y, 0); orbit.SetView(-18f, 6f, fr.dist);
                        StartCoroutine(ModelFiles.Load("organ_body_paths.json", pj => { if (pj == null) return; journey = root.AddComponent<JourneyView>(); journey.Init(organ, System.Text.Encoding.UTF8.GetString(pj)); journey.Opacity = opacity; }));
                    }
                }));
                orbit.Distance = journeyPack ? 6.4f : PackDistance(pack); orbit.MinDistance = 1.2f; orbit.MaxDistance = 16f; orbit.Target = pack == "ear" ? new Vector3(0.75f, 0, 0) : pack == "abdomen" ? new Vector3(0, 0.45f, 0) : Vector3.zero; orbit.Yaw = pack == "brain" ? -30f : -10f;   // ear: eardrum and ossicles sit at +x, keep them clear of the charts
                return;
            }
            if (area == "Heart" || area == "Circulation")
            {
                var root = new GameObject("Heart"); heart = root.AddComponent<HeartModel>();
                heartSim = new CardiacModel();
                StartCoroutine(ModelFiles.Load("heart_model.wasm", d => { heart.Build(d); root.transform.position = -heart.Bounds.center; }));
                orbit.Distance = 4.3f; orbit.Target = new Vector3(0, 0.05f, 0); orbit.Yaw = -12f;
            }
            else if (area == "Lungs")
            {
                var root = new GameObject("Lungs"); lungs = root.AddComponent<LungAnatomy>();
                lungSim = new LungModel();
                StartCoroutine(ModelFiles.Load("lungs_model.wasm", d => { lungs.Build(d); root.transform.position = -lungs.Bounds.center; }));
                orbit.Distance = 9.5f; orbit.MinDistance = 3f; orbit.MaxDistance = 22f; orbit.Target = new Vector3(0, -0.15f, 0);
            }
        }

        // ---------------------------------------------------------------- UI
        Transform C;

        void BuildCanvas()
        {
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>(); }
            var bgGo = new GameObject("BackdropCanvas"); var bgc = bgGo.AddComponent<Canvas>();
            bgc.renderMode = RenderMode.ScreenSpaceCamera; bgc.worldCamera = cam; bgc.planeDistance = 60f; bgc.sortingOrder = -100;
            var bgr = UIKit.Rect("bg", bgGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bgr.gameObject.AddComponent<RawImage>(); raw.texture = HeartApp.RadialTexture(); raw.raycastTarget = false;
            var cgo = new GameObject("UI"); var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var sc = cgo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.6f;
            cgo.AddComponent<GraphicRaycaster>();
            C = cgo.transform;
        }

        void BuildMessage(string msg)
        {
            BuildCanvas();
            SceneTabs.Add(C, "Sim");
            UIKit.LabelAt(C, msg, 26, UIKit.TextMain, new Vector2(60, -200), new Vector2(1200, 200), UIKit.SemiBold);
        }

        static Color Hex(string h, Color fallback)
        {
            if (!string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString(h, out var c)) return c;
            return fallback;
        }

        string Fmt(double v, int dp) => v.ToString(dp <= 0 ? "0" : "0." + new string('0', dp), CI).Replace('.', ',');

        void BuildUI()
        {
            BuildCanvas();
            string title = MiniJson.S(def, "title");
            // header: back to the catalog, the organ-system chip and the title (like the web app)
            var back = UIKit.Button(C, "←  Всички модели", Vector2.zero, new Vector2(214, 44), () => UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"), 16);
            var brt = back.Button.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 1); brt.anchoredPosition = new Vector2(36, -26);
            brt.gameObject.name = "Back";
            var ai = MenuApp.AreaInfo(MiniJson.S(def, "area"));
            float chipW = ai.bg.Length * 9f + 36;
            var chip = UIKit.Rect("AreaChip", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(262, -32), new Vector2(chipW, 32));
            UIKit.PanelImage(chip, new Color(ai.color.r * 0.2f, ai.color.g * 0.2f, ai.color.b * 0.2f, 0.95f));
            var ct = UIKit.Label(chip, ai.bg.ToUpperInvariant(), 13, ai.color, UIKit.SemiBold, TextAnchor.MiddleCenter);
            ct.rectTransform.anchorMin = Vector2.zero; ct.rectTransform.anchorMax = Vector2.one; ct.rectTransform.offsetMin = ct.rectTransform.offsetMax = Vector2.zero;
            var t = UIKit.LabelAt(C, title, 30, UIKit.TextMain, new Vector2(38, -82), new Vector2(1100, 40), UIKit.SemiBold);
            t.gameObject.name = "Title";
            var hint = UIKit.LabelAt(C, "Плъзни – въртене · колелце / два пръста – мащаб · десен бутон – преместване", 13, new Color(UIKit.TextDim.r, UIKit.TextDim.g, UIKit.TextDim.b, 0.7f), new Vector2(40, -124), new Vector2(900, 20));

            // phase card
            card = UIKit.Rect("PhaseCard", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -160), new Vector2(450, 300));
            UIKit.PanelImage(card, UIKit.Panel);
            scenTitle = UIKit.LabelAt(card, "", 13, UIKit.Accent, Vector2.zero, new Vector2(404, 22), UIKit.SemiBold);
            scenText = UIKit.LabelAt(card, "", 14, UIKit.TextDim, Vector2.zero, new Vector2(404, 60));
            phaseDot = UIKit.Dot(card, Vector2.zero, 12, Color.white);
            phaseTitle = UIKit.LabelAt(card, "", 22, UIKit.TextMain, Vector2.zero, new Vector2(380, 30), UIKit.SemiBold);
            phaseText = UIKit.LabelAt(card, "", 15, new Color(0.84f, 0.85f, 0.88f), Vector2.zero, new Vector2(404, 100));

            // stats
            var statsDef = MiniJson.L(def["stats"]);
            bool dense = statsDef.Count > 12; float rowH = dense ? 16.5f : 22f; int fs = dense ? 12 : 14;
            statsH = 64 + statsDef.Count * rowH;
            var stats = UIKit.Rect("Stats", C, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -26), new Vector2(GraphW, statsH));
            UIKit.PanelImage(stats, UIKit.Panel);
            UIKit.LabelAt(stats, "Стойности", 17, UIKit.TextMain, new Vector2(22, -16), new Vector2(300, 24), UIKit.SemiBold);
            var sb = new System.Text.StringBuilder();
            foreach (var o in statsDef) sb.Append(MiniJson.S(MiniJson.O(o), "label")).Append('\n');
            statsLabels = UIKit.LabelAt(stats, sb.ToString(), fs, UIKit.TextDim, new Vector2(24, -52), new Vector2(GraphW * 0.62f, statsDef.Count * rowH + 10));
            statsLabels.lineSpacing = dense ? 1.12f : 1.26f;
            statsValues = UIKit.LabelAt(stats, "", fs, UIKit.TextMain, new Vector2(GraphW * 0.45f, -52), new Vector2(GraphW * 0.55f - 26, statsDef.Count * rowH + 10), UIKit.SemiBold, TextAnchor.UpperRight);
            statsValues.lineSpacing = statsLabels.lineSpacing; statsValues.supportRichText = true;

            // charts
            BuildCharts();

            // controls
            // control bar: centred, in groups (play + speed | panels | view), like the web toolbar
            var bar = UIKit.Rect("Controls", C, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1200, 64));
            UIKit.PanelImage(bar, UIKit.Panel);
            float x0 = 10;
            const float H = 44, G = 6, GG = 22;
            pauseBtn = UIKit.Button(bar, "Пауза", new Vector2(x0, 0), new Vector2(110, H), () => { paused = !paused; pauseBtn.Text.text = paused ? "Пусни" : "Пауза"; pauseBtn.On = paused; }); x0 += 110 + G + 4;
            foreach (var o in MiniJson.L(def["speeds"]))
            {
                var sp = MiniJson.O(o); float k = (float)MiniJson.D(sp, "k", 1); string lbl = MiniJson.S(sp, "label");
                UIKit.Btn b = null; float bw = Mathf.Max(66, lbl.Length * 11 + 24);
                b = UIKit.Button(bar, lbl, new Vector2(x0, 0), new Vector2(bw, H), () => { speedK = k; foreach (var s2 in speedBtns) s2.On = s2 == b; }, 15);
                b.On = Mathf.Approximately(k, speedK); speedBtns.Add(b);
                x0 += bw + G;
            }
            x0 += GG;
            scenBtn = UIKit.Button(bar, "Сценарии", new Vector2(x0, 0), new Vector2(136, H), () => ShowPanel(scenPanel.gameObject.activeSelf ? null : scenPanel)); x0 += 136 + G;
            parBtn = UIKit.Button(bar, "Параметри", new Vector2(x0, 0), new Vector2(140, H), () => ShowPanel(parPanel.gameObject.activeSelf ? null : parPanel)); x0 += 140 + G;
            learnBtn = UIKit.Button(bar, "Учи", new Vector2(x0, 0), new Vector2(92, H), () => ShowPanel(learnPanel.gameObject.activeSelf ? null : learnPanel)); x0 += 92 + G + GG;
            UIKit.Button(bar, "Център", new Vector2(x0, 0), new Vector2(110, H), () => orbit.ResetView()); x0 += 110 + G;
            UIKit.Btn fb = null;
            fb = UIKit.Button(bar, "Само 3D", new Vector2(x0, 0), new Vector2(122, H), () => { focus = !focus; fb.On = focus; ApplyFocus(); }); x0 += 122 + G;
            if (heart != null || lungs != null || organ != null)
            {
                // opacity of the organs, 1–100 (100 = solid); the body journeys start as glass so the food stays visible
                float start = organ != null ? StartOpacity(SelectedId, packName) : 100; opacity = start / 100f;
                var box = UIKit.Rect("Opacity", bar, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x0, 0), new Vector2(300, H));
                UIKit.PanelImage(box, UIKit.ButtonOff);
                UIKit.LabelAt(box, "Плътност", 15, UIKit.TextMain, new Vector2(14, -12), new Vector2(90, 22), UIKit.SemiBold);
                var val = UIKit.LabelAt(box, Mathf.RoundToInt(start).ToString(), 15, UIKit.Accent, new Vector2(244, -12), new Vector2(46, 22), UIKit.SemiBold, TextAnchor.UpperRight);
                UIKit.Slider(box, new Vector2(104, 0), new Vector2(140, 20), 1, 100, start, v => { val.text = Mathf.RoundToInt(v).ToString(); ApplyOpacity(v / 100f); }, true);
                x0 += 300 + G;
            }
            bar.sizeDelta = new Vector2(x0 + 4, 64);

            BuildScenarioPanel(); BuildParamPanel(); BuildLearnPanel();
            if (heart == null && lungs == null && organ == null && micro == null)
                UIKit.LabelAt(C, "3D визуализацията за тази тема предстои", 18, new Color(1, 1, 1, 0.22f), new Vector2(560, -520), new Vector2(700, 30), UIKit.Italic, TextAnchor.UpperCenter);
            string creditText = "Физиологичен модел: Med Simulation 3D. Само за обучение.";
            if (PackFor("", SelectedId) == "uterus") creditText += " Матка и яйчници: Human Reference Atlas, NIH 3D, по Visible Human (CC BY 4.0); маточните тръби са схематични.";
            var credit = UIKit.Label(C, creditText, 11, new Color(1, 1, 1, 0.28f), null, TextAnchor.LowerCenter);
            var crt = credit.rectTransform; crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0); crt.pivot = new Vector2(0.5f, 0); crt.anchoredPosition = new Vector2(0, 4); crt.sizeDelta = new Vector2(1400, 18);
            layout = ResponsiveLayout.Attach(C, orbit, 1.75f);
            layout.LandscapeOffsetX = 0.03f + viewShiftX;
            LayoutCard();
        }

        void BuildCharts()
        {
            var charts = MiniJson.L(def["charts"]); var xyDefs = MiniJson.L(def.ContainsKey("xy") ? def["xy"] : null);
            int nStrip = Mathf.Min(charts.Count, 4);
            bool hasXY = xyDefs.Count > 0;
            float w = GraphW, h = Mathf.Clamp(1080 - 26 - statsH - 16 - 108, 360, 780);
            var gp = UIKit.Rect("Graphs", C, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 108), new Vector2(w, h));
            UIKit.PanelImage(gp, UIKit.Panel);
            float colW = hasXY ? w - 270 : w - 40;
            float each = (h - 20) / Mathf.Max(1, nStrip);
            for (int i = 0; i < nStrip; i++)
            {
                var cd = MiniJson.O(charts[i]);
                float top = -12 - i * each;
                UIKit.LabelAt(gp, MiniJson.S(cd, "title"), 14, UIKit.TextMain, new Vector2(18, top), new Vector2(colW, 20), UIKit.SemiBold);
                var st = new Strip { window = (float)MiniJson.D(cd, "window", 10) };
                var leg = UIKit.LabelAt(gp, "", 12, UIKit.TextDim, new Vector2(18, top - 19), new Vector2(colW + 20, 18)); leg.supportRichText = true; st.legend = leg;
                var rt = UIKit.Rect("graph", gp, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(54, top - 40), new Vector2(colW - 40, each - 52));
                var g = rt.gameObject.AddComponent<TraceGraph>(); g.raycastTarget = false;
                g.Min = (float)MiniJson.D(cd, "min"); g.Max = (float)MiniJson.D(cd, "max");
                var guides = new List<float>(); foreach (var gg in MiniJson.L(cd["guides"])) guides.Add((float)MiniJson.D(MiniJson.O(gg), "y"));
                if (g.Min < 0 && g.Max > 0) guides.Add(0);
                g.GridValues = guides.ToArray();
                UIKit.LabelAt(gp, Fmt(g.Max, g.Max < 10 ? 1 : 0), 10, new Color(1, 1, 1, 0.4f), new Vector2(6, top - 40), new Vector2(44, 14), null, TextAnchor.UpperRight);
                UIKit.LabelAt(gp, Fmt(g.Min, Mathf.Abs(g.Min) < 10 && g.Min != 0 ? 1 : 0), 10, new Color(1, 1, 1, 0.4f), new Vector2(6, top - 40 - (each - 52) + 12), new Vector2(44, 14), null, TextAnchor.UpperRight);
                var lg = new System.Text.StringBuilder();
                foreach (var co in MiniJson.L(cd["channels"]))
                {
                    var ch = MiniJson.O(co);
                    var buf = new float[N]; for (int k = 0; k < N; k++) buf[k] = float.NaN;
                    st.ch.Add(MiniJson.S(ch, "id")); st.buf.Add(buf);
                    var col = Hex(MiniJson.S(ch, "color"), Color.white);
                    g.Lines.Add(new TraceGraph.Series { Data = buf, Color = col, Width = 2f });
                    string unit = MiniJson.S(ch, "unit");
                    lg.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(col)).Append(">━ ").Append(MiniJson.S(ch, "label")).Append(unit != "" ? " (" + unit + ")" : "").Append("</color>   ");
                }
                leg.text = lg.ToString();
                st.g = g; strips.Add(st);
            }
            if (hasXY)
            {
                var xd = MiniJson.O(xyDefs[0]);
                float xx = w - 246;
                UIKit.LabelAt(gp, MiniJson.S(xd, "title"), 13, UIKit.TextMain, new Vector2(xx - 8, -12), new Vector2(240, 36), UIKit.SemiBold);
                var rt = UIKit.Rect("xy", gp, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(xx, -56), new Vector2(226, 226));
                var g = rt.gameObject.AddComponent<TraceGraph>(); g.raycastTarget = false;
                var xr = MiniJson.L(xd["xr"]); var yr = MiniJson.L(xd["yr"]);
                g.XMin = (float)MiniJson.Num(xr[0]); g.XMax = (float)MiniJson.Num(xr[1]); g.Min = (float)MiniJson.Num(yr[0]); g.Max = (float)MiniJson.Num(yr[1]);
                UIKit.LabelAt(gp, MiniJson.S(xd, "xl") + " →   ↑ " + MiniJson.S(xd, "yl"), 11, UIKit.TextDim, new Vector2(xx, -286), new Vector2(226, 16));
                var xy = new XY { g = g, x = MiniJson.S(xd, "x"), y = MiniJson.S(xd, "y"), trail = (float)MiniJson.D(xd, "trail", 2), bx = new float[N], by = new float[N], hasCurve = xd.ContainsKey("hasCurve") && xd["hasCurve"] is bool hb && hb };
                var normal = MiniJson.L(xd.ContainsKey("normal") ? xd["normal"] : null);
                if (normal.Count > 1)
                {
                    var nx = new float[normal.Count]; var ny = new float[normal.Count];
                    for (int i = 0; i < normal.Count; i++) { var pt = MiniJson.L(normal[i]); nx[i] = (float)MiniJson.Num(pt[0]); ny[i] = (float)MiniJson.Num(pt[1]); }
                    g.Lines.Add(new TraceGraph.Series { Data = ny, X = nx, Color = new Color(1, 1, 1, 0.25f), Width = 1.5f });
                }
                if (xy.hasCurve) { xy.curve = new TraceGraph.Series { Data = new float[2], X = new float[2], Color = new Color(1, 1, 1, 0.5f), Width = 1.8f, Count = 0 }; g.Lines.Add(xy.curve); }
                g.Lines.Add(new TraceGraph.Series { Data = xy.by, X = xy.bx, Color = Hex(MiniJson.S(xd, "color"), Color.yellow), Width = 2.4f, Count = 0 });
                g.MarkerColor = Hex(MiniJson.S(xd, "color"), Color.yellow);
                xys.Add(xy);
                if (xyDefs.Count > 1)
                {
                    var x2 = MiniJson.O(xyDefs[1]);
                    UIKit.LabelAt(gp, "Още: " + MiniJson.S(x2, "title"), 11, UIKit.TextDim, new Vector2(xx, -310), new Vector2(226, 40));
                }
            }
        }

        void ApplyFocus()
        {
            foreach (var n in new[] { "PhaseCard", "Stats", "Graphs" })
            {
                var t = C.Find(n); if (t != null) t.gameObject.SetActive(!focus);
            }
            orbit.ViewOffsetX = focus ? 0f : 0.03f + viewShiftX;
            orbit.DistanceScale = focus ? 0.8f : 1f;
        }

        void ShowPanel(RectTransform which)
        {
            scenPanel.gameObject.SetActive(which == scenPanel); parPanel.gameObject.SetActive(which == parPanel); learnPanel.gameObject.SetActive(which == learnPanel);
            card.gameObject.SetActive(which == null);
            scenBtn.On = which == scenPanel; parBtn.On = which == parPanel; learnBtn.On = which == learnPanel;
        }

        RectTransform Panel(string name, string title)
        {
            var p = UIKit.Rect(name, C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -160), new Vector2(470, 600));
            UIKit.PanelImage(p, UIKit.Panel);
            UIKit.LabelAt(p, title, 22, UIKit.TextMain, new Vector2(22, -16), new Vector2(420, 30), UIKit.SemiBold);
            return p;
        }

        void BuildScenarioPanel()
        {
            scenPanel = Panel("Scenarios", "Сценарии");
            var presets = MiniJson.L(def["presets"]);
            float y = -56;
            foreach (var o in presets)
            {
                var p = MiniJson.O(o); string id = MiniJson.S(p, "id");
                var b = UIKit.Button(scenPanel, MiniJson.S(p, "label"), Vector2.zero, new Vector2(424, 40), () => ApplyPreset(id), 15);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(22, y);
                b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.offsetMin = new Vector2(12, 0);
                sync.Add(() => b.On = presetId == id);
                y -= 46;
            }
            scenPanel.sizeDelta = new Vector2(470, -y + 16);
            scenPanel.gameObject.SetActive(false);
        }

        void BuildParamPanel()
        {
            parPanel = Panel("Params", "Параметри");
            float y = -56;
            var acts = MiniJson.L(def["actions"]);
            if (acts.Count > 0)
            {
                UIKit.LabelAt(parPanel, "ДЕЙСТВИЯ", 12, new Color(1, 1, 1, 0.4f), new Vector2(22, y), new Vector2(300, 16), UIKit.SemiBold); y -= 20;
                float x = 22;
                foreach (var o in acts)
                {
                    var a = MiniJson.O(o); string id = MiniJson.S(a, "id"); string lbl = MiniJson.S(a, "label");
                    float bw = Mathf.Min(424, lbl.Length * 9 + 30);
                    if (x + bw > 446) { x = 22; y -= 38; }
                    var b = UIKit.Button(parPanel, lbl, Vector2.zero, new Vector2(bw, 32), () => PhysioBridge.Act(handle, id), 13);
                    var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, y);
                    x += bw + 6;
                }
                y -= 46;
            }
            foreach (var o in MiniJson.L(def["sliders"]))
            {
                var s = MiniJson.O(o); string id = MiniJson.S(s, "id"); string unit = MiniJson.S(s, "unit");
                float min = (float)MiniJson.D(s, "min"), max = (float)MiniJson.D(s, "max"), step = (float)MiniJson.D(s, "step", 0.01);
                int dp = (int)MiniJson.D(s, "dp", -1); if (dp < 0) dp = step >= 1 ? 0 : step >= 0.1f ? 1 : step >= 0.01f ? 2 : 3;
                UIKit.LabelAt(parPanel, MiniJson.S(s, "label"), 13, UIKit.TextDim, new Vector2(22, y), new Vector2(310, 18));
                var val = UIKit.LabelAt(parPanel, "", 13, UIKit.TextMain, new Vector2(300, y), new Vector2(148, 18), UIKit.SemiBold, TextAnchor.UpperRight);
                float Get() => values.TryGetValue(id, out var v) ? (float)MiniJson.Num(v, 0) : min;
                var sl = UIKit.Slider(parPanel, Vector2.zero, new Vector2(420, 20), min, max, Get(), v =>
                {
                    v = Mathf.Round(v / step) * step; values[id] = (double)v; PhysioBridge.Set(handle, MiniJson.Write(values));
                }, false);
                var srt = sl.GetComponent<RectTransform>(); srt.anchorMin = srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 1); srt.anchoredPosition = new Vector2(26, y - 19);
                string F() => Fmt(Get(), dp) + (unit != "" ? " " + unit : "");
                sl.onValueChanged.AddListener(_ => val.text = F()); val.text = F();
                sync.Add(() => { sl.SetValueWithoutNotify(Get()); val.text = F(); });
                y -= 44;
            }
            float tx = 22;
            foreach (var o in MiniJson.L(def["toggles"]))
            {
                var tg = MiniJson.O(o); string id = MiniJson.S(tg, "id"); string lbl = MiniJson.S(tg, "label");
                float bw = Mathf.Min(424, lbl.Length * 8.5f + 30);
                if (tx + bw > 446) { tx = 22; y -= 38; }
                bool Get() => values.TryGetValue(id, out var v) && v is bool b0 && b0;
                UIKit.Btn b = null;
                b = UIKit.Button(parPanel, lbl, Vector2.zero, new Vector2(bw, 32), () => { values[id] = !Get(); b.On = Get(); PhysioBridge.Set(handle, MiniJson.Write(values)); }, 13);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(tx, y);
                b.On = Get(); sync.Add(() => b.On = Get());
                tx += bw + 6;
            }
            if (tx > 22) y -= 40;
            parPanel.sizeDelta = new Vector2(470, -y + 16);
            parPanel.gameObject.SetActive(false);
        }

        void BuildLearnPanel()
        {
            learnPanel = Panel("Learn", "Ключови идеи");
            var sb = new System.Text.StringBuilder();
            foreach (var o in MiniJson.L(def["learn"])) sb.Append("• ").Append(o as string).Append("\n\n");
            var t = UIKit.LabelAt(learnPanel, sb.ToString().TrimEnd(), 14, new Color(0.86f, 0.87f, 0.9f), new Vector2(22, -56), new Vector2(426, 100));
            float h = t.preferredHeight; t.rectTransform.sizeDelta = new Vector2(426, h);
            learnPanel.sizeDelta = new Vector2(470, h + 76);
            learnPanel.gameObject.SetActive(false);
        }

        void ApplyPreset(string id)
        {
            var p = MiniJson.O(MiniJson.L(def["presets"]).Find(o => MiniJson.S(MiniJson.O(o), "id") == id));
            values = new D(MiniJson.O(def["defaults"]));
            foreach (var kv in MiniJson.O(p["values"])) values[kv.Key] = kv.Value;
            PhysioBridge.Set(handle, MiniJson.Write(values));
            presetId = id;
            foreach (var a in sync) a();
            LayoutCard();
            ShowPanel(null);
        }

        string lastPhaseTitle;

        void LayoutCard()
        {
            float y = -18;
            var presets = MiniJson.L(def["presets"]);
            var p = MiniJson.O(presets.Find(o => MiniJson.S(MiniJson.O(o), "id") == presetId));
            bool has = p != null;
            scenTitle.gameObject.SetActive(has); scenText.gameObject.SetActive(has);
            if (has)
            {
                scenTitle.text = "СЪСТОЯНИЕ: " + MiniJson.S(p, "label").ToUpperInvariant(); scenText.text = MiniJson.S(p, "text");
                scenTitle.rectTransform.anchoredPosition = new Vector2(24, y); y -= 22;
                scenText.rectTransform.anchoredPosition = new Vector2(24, y);
                float hh = scenText.preferredHeight; scenText.rectTransform.sizeDelta = new Vector2(404, hh); y -= hh + 14;
            }
            ((RectTransform)phaseDot.transform).anchoredPosition = new Vector2(24, y - 9);
            phaseTitle.rectTransform.anchoredPosition = new Vector2(44, y);
            float th = Mathf.Max(28, phaseTitle.preferredHeight); phaseTitle.rectTransform.sizeDelta = new Vector2(384, th); y -= th + 6;
            phaseText.rectTransform.anchoredPosition = new Vector2(24, y);
            float ph = phaseText.preferredHeight; phaseText.rectTransform.sizeDelta = new Vector2(404, ph); y -= ph + 16;
            card.sizeDelta = new Vector2(450, -y + 6);
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (!ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float modelDt = paused ? 0 : dt * speedK;
            int maxSamples = 200;
            var res = MiniJson.O(MiniJson.Parse(PhysioBridge.Step(handle, modelDt, maxSamples)));
            if (res == null) return;
            var samples = MiniJson.L(res["samples"]);
            int taken = samples.Count;
            float perSample = taken > 0 ? modelDt / taken : 0;
            foreach (var so in samples)
            {
                var smp = MiniJson.O(so);
                foreach (var st in strips)
                {
                    st.acc += perSample;
                    float every = st.window / N;
                    while (st.acc >= every)
                    {
                        st.acc -= every;
                        for (int c = 0; c < st.ch.Count; c++)
                        {
                            var buf = st.buf[c];
                            System.Array.Copy(buf, 1, buf, 0, N - 1);
                            buf[N - 1] = smp.TryGetValue(st.ch[c], out var v) ? (float)MiniJson.Num(v) : float.NaN;
                        }
                    }
                }
                foreach (var xy in xys)
                {
                    float xv = smp.TryGetValue(xy.x, out var a) ? (float)MiniJson.Num(a) : float.NaN;
                    float yv = smp.TryGetValue(xy.y, out var b) ? (float)MiniJson.Num(b) : float.NaN;
                    int keep = Mathf.Clamp(Mathf.RoundToInt(xy.trail / Mathf.Max(sampleDt * (perSample > 0 ? perSample / sampleDt : 1), 1e-4f)), 2, N);
                    if (xy.n < keep) xy.n++;
                    if (xy.n > keep) xy.n = keep;
                    System.Array.Copy(xy.bx, 1, xy.bx, 0, N - 1); System.Array.Copy(xy.by, 1, xy.by, 0, N - 1);
                    xy.bx[N - 1] = xv; xy.by[N - 1] = yv;
                }
            }
            foreach (var xy in xys)
            {
                var s = xy.g.Lines[xy.g.Lines.Count - 1];
                // the series draws from index 0: point it at the newest n samples
                int n = xy.n; if (s.Data.Length != n) { s.Data = new float[n]; s.X = new float[n]; }
                System.Array.Copy(xy.by, N - n, s.Data, 0, n); System.Array.Copy(xy.bx, N - n, s.X, 0, n);
                s.Count = n;
                if (n > 0) { xy.g.MarkerX = Mathf.InverseLerp(xy.g.XMin, xy.g.XMax, s.X[n - 1]); xy.g.MarkerY = Mathf.InverseLerp(xy.g.Min, xy.g.Max, s.Data[n - 1]); }
            }
            curveTimer -= dt;
            if (curveTimer <= 0)
            {
                curveTimer = 0.5f;
                foreach (var xy in xys)
                {
                    if (!xy.hasCurve) continue;
                    var pts = MiniJson.L(MiniJson.Parse(PhysioBridge.Curve(handle, 0)));
                    var cx = new float[pts.Count]; var cy = new float[pts.Count];
                    for (int i = 0; i < pts.Count; i++) { var pt = MiniJson.L(pts[i]); cx[i] = (float)MiniJson.Num(pt[0]); cy[i] = (float)MiniJson.Num(pt[1]); }
                    xy.curve.X = cx; xy.curve.Data = cy; xy.curve.Count = pts.Count;
                }
            }
            foreach (var st in strips) st.g.Refresh();
            foreach (var xy in xys) xy.g.Refresh();

            // phase
            var ph = MiniJson.O(res["phase"]);
            string ptitle = MiniJson.S(ph, "title");
            phaseTitle.text = ptitle; phaseText.text = MiniJson.S(ph, "text");
            phaseDot.color = Hex(MiniJson.S(ph, "color"), UIKit.Accent);
            if (ptitle != lastPhaseTitle) { lastPhaseTitle = ptitle; LayoutCard(); }

            // stats
            var stv = MiniJson.O(res["stats"]);
            var sb = new System.Text.StringBuilder();
            foreach (var o in MiniJson.L(def["stats"]))
            {
                var s = MiniJson.O(o); string id = MiniJson.S(s, "id"); int dp = (int)MiniJson.D(s, "dp", 0); string unit = MiniJson.S(s, "unit");
                object v = stv != null && stv.TryGetValue(id, out var vv) ? vv : null;
                string txt; bool bad = false;
                if (v is double d)
                {
                    txt = Fmt(d, dp);
                    var nr = MiniJson.L(s.ContainsKey("normal") ? s["normal"] : null);
                    if (nr.Count == 2 && (d < MiniJson.Num(nr[0]) || d > MiniJson.Num(nr[1]))) bad = true;
                }
                else txt = v == null ? "—" : v.ToString();
                if (unit != "" && v is double) txt += " " + unit;
                sb.Append(bad ? "<color=#FFB347>" + txt + "</color>" : txt).Append('\n');
            }
            statsValues.text = sb.ToString();

            Drive3D(MiniJson.O(res["now"]), stv, dt);
        }

        static float Pick(Dictionary<string, object> a, Dictionary<string, object> b, float def, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (a != null && a.TryGetValue(k, out var v) && v is double d) return (float)d;
                if (b != null && b.TryGetValue(k, out var w) && w is double e) return (float)e;
            }
            return def;
        }

        void Drive3D(Dictionary<string, object> now, Dictionary<string, object> stats, float dt)
        {
            if (visuals != null) visuals.Tick(now, paused ? 0 : dt);
            if (micro != null) micro.Tick(now, paused ? 0 : dt);
            if (journey != null) journey.Tick(now, paused ? 0 : dt);
            if (organ != null && organ.Loaded)
            {
                float a = Pick(now, null, float.NaN, "force", "F", "tension", "f");
                if (!float.IsNaN(a)) { actMax = Mathf.Max(actMax * 0.999f, Mathf.Abs(a)); organ.SetContraction(Mathf.Abs(a) / actMax); }
            }
            float phys = paused ? 0 : dt;
            if (heart != null && heart.Parts.Count > 0)
            {
                if (now != null && now.ContainsKey("vLV"))
                {
                    // the original model drives the 3D heart: volumes, pressures and valve states
                    float pLV = Pick(now, null, 0, "pLV"), pAo = Pick(now, null, 80, "pAo"), pLA = Pick(now, null, 8, "pLA");
                    float pRV = Pick(now, null, 0, "pRV"), pPA = Pick(now, null, 15, "pPA"), pRA = Pick(now, null, 4, "pRA");
                    heartSim.Vlv = Pick(now, null, 120, "vLV"); heartSim.Vrv = Pick(now, null, 120, "vRV");
                    heartSim.Vla = 69f - Mathf.Clamp01((pLA - 4f) / 10f) * 30f;
                    heartSim.Pao = pAo; heartSim.Ppa = pPA;
                    float k = 1f - Mathf.Exp(-dt / 0.03f);
                    heartSim.Mitral.Leaflet += ((pLA > pLV ? 1f : 0f) - heartSim.Mitral.Leaflet) * k;
                    heartSim.Tricuspid.Leaflet += ((pRA > pRV ? 1f : 0f) - heartSim.Tricuspid.Leaflet) * k;
                    heartSim.Aortic.Leaflet += ((Pick(now, null, 0, "qAo") > 1f ? 1f : 0f) - heartSim.Aortic.Leaflet) * k;
                    heartSim.Pulmonary.Leaflet += ((pRV > pPA ? 1f : 0f) - heartSim.Pulmonary.Leaflet) * k;
                }
                else
                {
                    float hr = Pick(now, stats, 72f, "hr", "rate");
                    heartSim.HeartRate = Mathf.Clamp(hr, 25f, 220f);
                    if (phys > 0) heartSim.Advance(phys);
                }
                heart.Apply(heartSim);
            }
            if (lungs != null && lungs.Parts.Count > 0)
            {
                float rr = Pick(now, stats, 12f, "rr", "rate");
                lungSim.P.rr = Mathf.Clamp(rr, 2f, 40f);
                if (phys > 0) lungSim.Advance(phys);
                float vol = Pick(now, null, float.NaN, "vol", "v");
                float sat = Pick(now, stats, 97f, "spo2", "sao2"); if (sat > 1.5f) sat /= 100f;
                lungs.Apply(float.IsNaN(vol) ? lungSim.V : vol, 0, sat);
            }
        }
    }
}
