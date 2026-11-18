using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MedAtlas.Heart
{
    /// <summary>Entry point: builds the studio scene, the anatomical heart, the physiology model and the UI.</summary>
    public class HeartApp : MonoBehaviour
    {
        CardiacModel model;
        HeartModel heart;
        BloodFlow flow;
        HeartSound sound;
        OrbitCamera orbit;
        Camera cam;

        float speed = 0.25f;
        bool paused, stepping;
        CardiacPhase stepFrom;

        // UI refs
        RectTransform phaseCard, scenPanel, parPanel; Text valveHeader, scenTitle, scenText; int laidOutPhase = -1; string currentScenario = "normal";
        UIKit.Btn scenBtn, parBtn; Slider hrSlider;
        readonly System.Collections.Generic.List<System.Action> paramSync = new System.Collections.Generic.List<System.Action>();
        Text phaseIdx, phaseName, phaseLatin, phaseText, eventText, statsText, partText, legendText, hrLabel;
        Image[] valveDots = new Image[4]; Text[] valveTexts = new Text[4];
        TraceGraph gP, gV, gE;
        UIKit.Btn pauseBtn, internalBtn, flowBtn, soundBtn, sideBtn;
        UIKit.Btn[] speedBtns;
        static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f };
        bool rightSide;
        float eventTimer, partTimer;

        public static readonly Color[] PhaseColors =
        {
            new Color(0.95f, 0.75f, 0.30f, 0.9f), new Color(0.95f, 0.40f, 0.35f, 0.9f), new Color(0.90f, 0.20f, 0.30f, 0.9f),
            new Color(0.75f, 0.30f, 0.55f, 0.9f), new Color(0.50f, 0.45f, 0.85f, 0.9f), new Color(0.30f, 0.65f, 0.90f, 0.9f),
            new Color(0.35f, 0.75f, 0.65f, 0.9f)
        };

        bool ready;

        System.Collections.IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            BuildStudio(out cam, out orbit);
            model = new CardiacModel();

            var root = new GameObject("Heart");
            heart = root.AddComponent<HeartModel>();
            byte[] data = null;
            yield return ModelFiles.Load("heart_model.wasm", d => data = d);
            heart.Build(data);
            // the model is in anatomical position: +x = patient's left, +y = up, +z = posterior
            root.transform.position = -heart.Bounds.center;

            var flowGo = new GameObject("BloodFlow");
            flowGo.transform.SetParent(root.transform, false);
            flow = flowGo.AddComponent<BloodFlow>();
            flow.Build(heart, model);

            sound = gameObject.AddComponent<HeartSound>();
            model.OnEvent += OnCardiacEvent;

            BuildUI();
            SetInternal(false);
            orbit.OnTap = OnTap;
            ready = true;
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-lungs") >= 0) { UnityEngine.SceneManagement.SceneManager.LoadScene("Lungs"); yield break; }
            if (System.Array.IndexOf(args, "-organ") >= 0) { UnityEngine.SceneManagement.SceneManager.LoadScene("Sim"); yield break; }
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-autoshot") StartCoroutine(AutoShot(args[i + 1]));
        }

        System.Collections.IEnumerator AutoShot(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            SetSpeed(0.5f);
            yield return new WaitForSeconds(4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_external.png"));
            yield return new WaitForSeconds(1.0f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_external2.png"));
            yield return new WaitForSeconds(0.5f);
            SetInternal(true);
            yield return new WaitForSeconds(3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_internal.png"));
            yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_internal2.png"));
            orbit.Yaw = orbit.Yaw + 0; 
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        // ---------------------------------------------------------------- studio
        public static void BuildStudio(out Camera cam, out OrbitCamera orbit)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.028f, 0.03f, 0.038f);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();
            orbit = camGo.AddComponent<OrbitCamera>();
            orbit.Target = new Vector3(0.0f, 0.05f, 0f); orbit.Distance = 4.9f; orbit.Yaw = -12f; orbit.Pitch = 6f;
            orbit.MinDistance = 1.4f; orbit.MaxDistance = 10f;

            Light MakeLight(string n, Color c, float i, Vector3 euler, bool shadows)
            {
                var go = new GameObject(n); var l = go.AddComponent<Light>();
                l.type = LightType.Directional; l.color = c; l.intensity = i;
                go.transform.rotation = Quaternion.Euler(euler);
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                return l;
            }
            var key = MakeLight("Key Light", new Color(1f, 0.95f, 0.9f), 2.1f, new Vector3(38f, 28f, 0f), true);
            key.shadowStrength = 0.35f; key.shadowBias = 0.02f; key.shadowNormalBias = 0.3f;
            MakeLight("Fill Light", new Color(0.72f, 0.80f, 1f), 0.85f, new Vector3(12f, -130f, 0f), false);
            MakeLight("Rim Light", new Color(1f, 0.88f, 0.86f), 1.6f, new Vector3(28f, 200f, 0f), false);
            RenderSettings.sun = key;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.32f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.18f, 0.19f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.045f);
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = StudioCubemap();
            RenderSettings.reflectionIntensity = 0.3f;

            // post processing
            var volGo = new GameObject("Post Volume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = prof.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.35f); bloom.scatter.Override(0.65f);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.32f); vg.smoothness.Override(0.55f);
            var ca = prof.Add<ColorAdjustments>(true); ca.contrast.Override(8f); ca.saturation.Override(6f); ca.postExposure.Override(0.15f);
            vol.sharedProfile = prof;
        }

        public static Cubemap StudioCubemap()
        {
            int n = 64;
            var cube = new Cubemap(n, TextureFormat.RGBAHalf, true);
            for (int f = 0; f < 6; f++)
            {
                var face = (CubemapFace)f;
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                        Vector3 d;
                        switch (face)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                            case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                            case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                            case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                            default: d = new Vector3(-u, -v, -1); break;
                        }
                        d.Normalize();
                        float c = 0.03f + 0.05f * Mathf.Max(d.y, 0);
                        // soft boxes: big one up-left-front, strip on the right, rim behind
                        c += 1.1f * Mathf.Pow(Mathf.Max(Vector3.Dot(d, new Vector3(-0.5f, 0.75f, -0.45f).normalized), 0), 10);
                        c += 0.45f * Mathf.Pow(Mathf.Max(Vector3.Dot(d, new Vector3(0.9f, 0.15f, -0.2f).normalized), 0), 16);
                        c += 0.4f * Mathf.Pow(Mathf.Max(Vector3.Dot(d, new Vector3(0.1f, 0.4f, 1f).normalized), 0), 8);
                        px[y * n + x] = new Color(c, c * 0.98f, c * 0.97f, 1);
                    }
                cube.SetPixels(px, face);
            }
            cube.Apply(true);
            return cube;
        }

        // ---------------------------------------------------------------- UI
        void BuildUI()
        {
            UIKit.Init();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            // background gradient behind the heart
            var bgCanvasGo = new GameObject("BackdropCanvas");
            var bgc = bgCanvasGo.AddComponent<Canvas>();
            bgc.renderMode = RenderMode.ScreenSpaceCamera; bgc.worldCamera = cam; bgc.planeDistance = 60f; bgc.sortingOrder = -100;
            var bgImg = UIKit.Rect("bg", bgCanvasGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bgImg.gameObject.AddComponent<RawImage>(); raw.texture = RadialTexture(); raw.raycastTarget = false;

            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.6f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var C = canvasGo.transform;

            // title
            UIKit.LabelAt(C, "Сърце", 38, UIKit.TextMain, new Vector2(44, -30), new Vector2(600, 50), UIKit.SemiBold);
            SceneTabs.Add(C, "Heart");
            UIKit.LabelAt(C, "Cor  ·  Сърдечен цикъл (Cyclus cardiacus)", 18, UIKit.TextDim, new Vector2(46, -80), new Vector2(700, 30), UIKit.Italic);

            // phase card (left)
            var card = UIKit.Rect("PhaseCard", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -128), new Vector2(450, 560));
            UIKit.PanelImage(card, UIKit.Panel);
            phaseIdx = UIKit.LabelAt(card, "", 15, UIKit.Accent, new Vector2(24, -20), new Vector2(410, 24), UIKit.SemiBold);
            phaseName = UIKit.LabelAt(card, "", 28, UIKit.TextMain, new Vector2(24, -46), new Vector2(410, 80), UIKit.SemiBold);
            phaseLatin = UIKit.LabelAt(card, "", 19, UIKit.TextDim, new Vector2(24, -86), new Vector2(410, 30), UIKit.Italic);
            phaseText = UIKit.LabelAt(card, "", 16, new Color(0.84f, 0.85f, 0.88f), new Vector2(24, -124), new Vector2(404, 260));
            valveHeader = UIKit.LabelAt(card, "КЛАПИ", 13, new Color(1, 1, 1, 0.38f), new Vector2(24, -382), new Vector2(200, 20), UIKit.SemiBold);
            for (int i = 0; i < 4; i++)
            {
                valveDots[i] = UIKit.Dot(card, Vector2.zero, 11, Color.gray);
                valveTexts[i] = UIKit.LabelAt(card, "", 15, UIKit.TextDim, Vector2.zero, new Vector2(380, 22));
            }
            eventText = UIKit.LabelAt(card, "", 16, new Color(1f, 0.86f, 0.5f), new Vector2(24, -476), new Vector2(404, 70), UIKit.SemiBold);
            scenTitle = UIKit.LabelAt(card, "", 14, new Color(0.55f, 0.85f, 1f), new Vector2(24, -20), new Vector2(404, 22), UIKit.SemiBold);
            scenText = UIKit.LabelAt(card, "", 14, new Color(0.75f, 0.85f, 0.95f), new Vector2(24, -44), new Vector2(404, 80), UIKit.Italic);
            phaseCard = card;

            // stats (top right)
            var stats = UIKit.Rect("Stats", C, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -30), new Vector2(340, 318));
            UIKit.PanelImage(stats, UIKit.Panel);
            var statLabels = UIKit.LabelAt(stats, "Пулс\nАртериално налягане (СрАН)\nБелодробна артерия\nКДО / КСО (ЛК)\nУдарен обем · ФИ\nМинутен обем\nЛП / ЦВН\nКДН на ЛК\nГрадиент ЛК–аорта\nРегургитантна фракция\ndP/dt max\nBowditch · Anrep", 14, UIKit.TextDim, new Vector2(20, -16), new Vector2(220, 300));
            statLabels.lineSpacing = 1.32f;
            statsText = UIKit.LabelAt(stats, "", 14, UIKit.TextMain, new Vector2(170, -16), new Vector2(150, 300), UIKit.SemiBold, TextAnchor.UpperRight);
            statsText.lineSpacing = 1.32f;

            // graph panel (bottom right)
            var gp = UIKit.Rect("Graphs", C, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 112), new Vector2(660, 520));
            UIKit.PanelImage(gp, UIKit.Panel);
            UIKit.LabelAt(gp, "Диаграма на Wiggers", 17, UIKit.TextMain, new Vector2(20, -14), new Vector2(300, 26), UIKit.SemiBold);
            sideBtn = UIKit.Button(gp, "Ляво сърце", new Vector2(490, 0), new Vector2(150, 34), () => { rightSide = !rightSide; UpdateSide(); }, 15);
            var sbrt = sideBtn.Button.GetComponent<RectTransform>(); sbrt.anchorMin = sbrt.anchorMax = new Vector2(0, 1); sbrt.pivot = new Vector2(0, 1); sbrt.anchoredPosition = new Vector2(490, -10);
            legendText = UIKit.LabelAt(gp, "", 14, UIKit.TextDim, new Vector2(20, -44), new Vector2(620, 24));
            legendText.supportRichText = true;
            gP = MakeGraph(gp, new Vector2(56, -74), new Vector2(586, 230), 16f);
            gV = MakeGraph(gp, new Vector2(56, -318), new Vector2(586, 92), 0f);
            gE = MakeGraph(gp, new Vector2(56, -420), new Vector2(586, 86), 0f);
            AxisLabels(gp, new Vector2(10, -90), "mmHg", new[] { "120", "80", "40", "0" }, 230 - 16, -90);
            UIKit.LabelAt(gp, "обем\nml", 12, UIKit.TextDim, new Vector2(10, -330), new Vector2(46, 60));
            UIKit.LabelAt(gp, "ЕКГ", 12, UIKit.TextDim, new Vector2(10, -446), new Vector2(46, 30));
            gP.Min = 0; gP.Max = 140; gP.GridValues = new[] { 0f, 40f, 80f, 120f };
            gP.PhaseColors = PhaseColors; gP.Phases = model.TrPhase; gP.Marks = model.TrMark;
            gV.PhaseColors = PhaseColors; gV.Phases = model.TrPhase;
            gE.Min = -0.45f; gE.Max = 1.35f; gE.GridValues = new[] { 0f };
            gE.Lines.Add(new TraceGraph.Series { Data = model.TrEcg, Color = new Color(0.45f, 1f, 0.6f), Width = 2f });
            UpdateSide();

            // control bar (bottom)
            var bar = UIKit.Rect("Controls", C, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 30), new Vector2(1848, 64));
            UIKit.PanelImage(bar, UIKit.Panel);
            float x0 = 14;
            pauseBtn = UIKit.Button(bar, "Пауза", new Vector2(x0, 0), new Vector2(110, 44), TogglePause); x0 += 118;
            UIKit.Button(bar, "Следваща фаза ›", new Vector2(x0, 0), new Vector2(170, 44), StepPhase); x0 += 186;
            var spLbl = UIKit.Label(bar, "Скорост", 15, UIKit.TextDim, null, TextAnchor.MiddleLeft);
            Place(spLbl.rectTransform, x0, 80); x0 += 78;
            speedBtns = new UIKit.Btn[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
            {
                int k = i;
                string lbl = Speeds[i] < 1 ? Speeds[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + "×" : "1×";
                speedBtns[i] = UIKit.Button(bar, lbl, new Vector2(x0, 0), new Vector2(66, 44), () => SetSpeed(Speeds[k]));
                x0 += 72;
            }
            x0 += 18;
            hrLabel = UIKit.Label(bar, "", 15, UIKit.TextDim, null, TextAnchor.MiddleLeft);
            Place(hrLabel.rectTransform, x0, 130); x0 += 130;
            hrSlider = UIKit.Slider(bar, new Vector2(x0, 0), new Vector2(180, 30), 30, 200, model.HeartRate, v => { model.HeartRate = v; });
            x0 += 200;
            internalBtn = UIKit.Button(bar, "Вътрешен изглед", new Vector2(x0, 0), new Vector2(190, 44), () => SetInternal(!heart.InternalView)); x0 += 198;
            flowBtn = UIKit.Button(bar, "Кръвоток", new Vector2(x0, 0), new Vector2(120, 44), () => { flow.Visible = !flow.Visible; flowBtn.On = flow.Visible; }); x0 += 128;
            soundBtn = UIKit.Button(bar, "Звук", new Vector2(x0, 0), new Vector2(90, 44), () => { sound.Enabled = !sound.Enabled; soundBtn.On = sound.Enabled; }); x0 += 98;
            UIKit.Button(bar, "Център", new Vector2(x0, 0), new Vector2(100, 44), () => orbit.ResetView()); x0 += 108;
            scenBtn = UIKit.Button(bar, "Сценарии", new Vector2(x0, 0), new Vector2(132, 44), () => ShowPanel(scenPanel.gameObject.activeSelf ? null : scenPanel)); x0 += 140;
            parBtn = UIKit.Button(bar, "Параметри", new Vector2(x0, 0), new Vector2(132, 44), () => ShowPanel(parPanel.gameObject.activeSelf ? null : parPanel));
            BuildScenarioPanel(C);
            BuildParamPanel(C);
            flowBtn.On = true; soundBtn.On = true;
            SetSpeed(speed);

            // picked part label
            partText = UIKit.Label(C, "", 20, UIKit.TextMain, UIKit.SemiBold, TextAnchor.MiddleCenter);
            var prt = partText.rectTransform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0); prt.pivot = new Vector2(0.5f, 0);
            prt.anchoredPosition = new Vector2(-180, 110); prt.sizeDelta = new Vector2(900, 70);
            partText.supportRichText = true;

            var credit = UIKit.Label(C, "Анатомични модели: BodyParts3D © DBCLS (CC BY-SA 2.1 JP), обработени", 11, new Color(1, 1, 1, 0.3f), null, TextAnchor.LowerLeft);
            var crt = credit.rectTransform; crt.anchorMin = crt.anchorMax = Vector2.zero; crt.pivot = Vector2.zero; crt.anchoredPosition = new Vector2(40, 6); crt.sizeDelta = new Vector2(700, 20);
            var hint = UIKit.Label(C, "Плъзни – въртене · колелце / два пръста – мащаб · десен бутон – преместване · клик върху част – име", 13, new Color(1, 1, 1, 0.38f), null, TextAnchor.UpperLeft);
            var hrt = hint.rectTransform; hrt.anchorMin = hrt.anchorMax = new Vector2(0, 1); hrt.pivot = new Vector2(0, 1); hrt.anchoredPosition = new Vector2(46, -104); hrt.sizeDelta = new Vector2(900, 20);
            ResponsiveLayout.Attach(C, orbit);
        }

        void LayoutCard()
        {
            float y = -18;
            bool hasScen = currentScenario != "normal";
            scenTitle.gameObject.SetActive(hasScen); scenText.gameObject.SetActive(hasScen);
            if (hasScen)
            {
                var sc = System.Array.Find(Scenarios.All, x => x.Id == currentScenario);
                scenTitle.text = "СЦЕНАРИЙ: " + sc.Name.ToUpperInvariant();
                scenText.text = sc.Text;
                scenTitle.rectTransform.anchoredPosition = new Vector2(24, y); y -= 22;
                scenText.rectTransform.anchoredPosition = new Vector2(24, y);
                float hh = scenText.preferredHeight; scenText.rectTransform.sizeDelta = new Vector2(404, hh); y -= hh + 14;
            }
            phaseIdx.rectTransform.anchoredPosition = new Vector2(24, y); y -= 26;
            void Put(Text t, float gap)
            {
                var rt = t.rectTransform; rt.anchoredPosition = new Vector2(24, y);
                float h = t.preferredHeight; rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
                y -= h + gap;
            }
            Put(phaseName, 4); Put(phaseLatin, 14); Put(phaseText, 18);
            valveHeader.rectTransform.anchoredPosition = new Vector2(24, y); y -= 24;
            for (int i = 0; i < 4; i++)
            {
                ((RectTransform)valveDots[i].transform).anchoredPosition = new Vector2(24, y - 5);
                valveTexts[i].rectTransform.anchoredPosition = new Vector2(44, y);
                y -= 25;
            }
            y -= 12;
            eventText.rectTransform.anchoredPosition = new Vector2(24, y);
            y -= 64;
            phaseCard.sizeDelta = new Vector2(phaseCard.sizeDelta.x, -y + 10);
        }

        void ShowPanel(RectTransform which)
        {
            scenPanel.gameObject.SetActive(which == scenPanel);
            parPanel.gameObject.SetActive(which == parPanel);
            phaseCard.gameObject.SetActive(which == null);
            scenBtn.On = which == scenPanel; parBtn.On = which == parPanel;
        }

        void BuildScenarioPanel(Transform C)
        {
            scenPanel = UIKit.Rect("Scenarios", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -128), new Vector2(450, 680));
            UIKit.PanelImage(scenPanel, UIKit.Panel);
            UIKit.LabelAt(scenPanel, "Сценарии", 22, UIKit.TextMain, new Vector2(22, -16), new Vector2(400, 30), UIKit.SemiBold);
            UIKit.LabelAt(scenPanel, "Избери състояние – моделът се пренастройва веднага.", 13, UIKit.TextDim, new Vector2(22, -48), new Vector2(400, 20));
            for (int i = 0; i < Scenarios.All.Length; i++)
            {
                var sc = Scenarios.All[i];
                string id = sc.Id;
                var b = UIKit.Button(scenPanel, sc.Name, Vector2.zero, new Vector2(404, 36), () => ApplyScenario(id), 15);
                var rt = b.Button.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(22, -80 - i * 41);
                b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.offsetMin = new Vector2(14, 0);
                paramSync.Add(() => b.On = currentScenario == id);
            }
            scenPanel.gameObject.SetActive(false);
        }

        void BuildParamPanel(Transform C)
        {
            parPanel = UIKit.Rect("Params", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -128), new Vector2(450, 800));
            UIKit.PanelImage(parPanel, UIKit.Panel);
            UIKit.LabelAt(parPanel, "Параметри", 22, UIKit.TextMain, new Vector2(22, -16), new Vector2(300, 30), UIKit.SemiBold);
            var reset = UIKit.Button(parPanel, "Нормално", Vector2.zero, new Vector2(120, 32), () => ApplyScenario("normal"), 14);
            var rrt = reset.Button.GetComponent<RectTransform>(); rrt.anchorMin = rrt.anchorMax = new Vector2(0, 1); rrt.pivot = new Vector2(0, 1); rrt.anchoredPosition = new Vector2(306, -16);
            float y = -62;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void Row(string label, float min, float max, System.Func<float> get, System.Action<float> set, string fmt, string unit)
            {
                UIKit.LabelAt(parPanel, label, 14, UIKit.TextDim, new Vector2(22, y), new Vector2(300, 20));
                var val = UIKit.LabelAt(parPanel, "", 14, UIKit.TextMain, new Vector2(300, y), new Vector2(128, 20), UIKit.SemiBold, TextAnchor.UpperRight);
                var sl = UIKit.Slider(parPanel, Vector2.zero, new Vector2(400, 24), min, max, get(), v => { set(v); }, false);
                var srt = sl.GetComponent<RectTransform>(); srt.anchorMin = srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 1); srt.anchoredPosition = new Vector2(26, y - 22);
                sl.onValueChanged.AddListener(v => val.text = get().ToString(fmt, ci).Replace('.', ',') + unit);
                val.text = get().ToString(fmt, ci).Replace('.', ',') + unit;
                paramSync.Add(() => { sl.SetValueWithoutNotify(get()); val.text = get().ToString(fmt, ci).Replace('.', ',') + unit; });
                y -= 54;
            }
            Row("Контрактилитет", 0.2f, 2.2f, () => model.P.contractility, v => model.P.contractility = v, "0.00", "×");
            Row("Периферно съдово съпротивление", 0.3f, 2.5f, () => model.P.svr, v => model.P.svr = v, "0.00", "×");
            Row("Венозен тонус (преднатоварване)", 0.7f, 1.4f, () => model.P.venousTone, v => model.P.venousTone = v, "0.00", "×");
            Row("Промяна на кръвния обем", -2000f, 1500f, () => model.P.volume, v => model.P.volume = Mathf.Round(v / 50f) * 50f, "0", " ml");
            Row("Диастолна скованост", 0.6f, 4f, () => model.P.stiffness, v => model.P.stiffness = v, "0.0", "×");
            Row("Аортна стеноза", 0f, 1f, () => model.P.aorticStenosis, v => model.P.aorticStenosis = v, "0.00", "");
            Row("Аортна регургитация", 0f, 1f, () => model.P.aorticRegurg, v => model.P.aorticRegurg = v, "0.00", "");
            Row("Митрална регургитация", 0f, 1f, () => model.P.mitralRegurg, v => model.P.mitralRegurg = v, "0.00", "");
            Row("Перикардно налягане (тампонада)", 0f, 20f, () => model.P.pericardial, v => model.P.pericardial = v, "0.0", " mmHg");
            Row("Белодробно съдово съпротивление", 0.5f, 5f, () => model.P.pulmonaryResistance, v => model.P.pulmonaryResistance = v, "0.0", "×");
            Row("Дебелина на камерната стена", 0.5f, 2.2f, () => model.P.wall, v => model.P.wall = v, "0.00", "×");
            y -= 4;
            void Toggle(string label, System.Func<bool> get, System.Action<bool> set, float x, float w)
            {
                UIKit.Btn b = null;
                b = UIKit.Button(parPanel, label, Vector2.zero, new Vector2(w, 34), () => { set(!get()); b.On = get(); }, 13);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, y);
                b.On = get();
                paramSync.Add(() => b.On = get());
            }
            Toggle("Барорефлекс", () => model.P.baroreflex, v => model.P.baroreflex = v, 22, 128);
            Toggle("Предсърдно мъждене", () => model.P.af, v => model.P.af = v, 156, 160);
            Toggle("Bowditch/Anrep", () => model.P.homeometric, v => model.P.homeometric = v, 322, 106);
            parPanel.sizeDelta = new Vector2(450, -y + 50);
            parPanel.gameObject.SetActive(false);
        }

        void ApplyScenario(string id)
        {
            var sc = System.Array.Find(Scenarios.All, x => x.Id == id);
            var p = new CardioParams();
            sc.Apply(p);
            model.SetParams(p, true);
            currentScenario = id;
            hrSlider.SetValueWithoutNotify(model.HeartRate);
            foreach (var a in paramSync) a();
            laidOutPhase = -1;
            ShowPanel(null);
        }

        static void Place(RectTransform rt, float x, float w)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(w, 40);
        }

        TraceGraph MakeGraph(RectTransform parent, Vector2 pos, Vector2 size, float band)
        {
            var rt = UIKit.Rect("graph", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            var g = rt.gameObject.AddComponent<TraceGraph>();
            g.raycastTarget = false; g.BandHeight = band;
            return g;
        }

        void AxisLabels(RectTransform parent, Vector2 pos, string unit, string[] vals, float h, float top)
        {
            UIKit.LabelAt(parent, unit, 12, UIKit.TextDim, new Vector2(10, top + 6), new Vector2(46, 20));
            // positions of 120/80/40/0 on a 0..140 axis in a plot of height h starting 16px below top
            float[] v = { 120, 80, 40, 0 };
            for (int i = 0; i < vals.Length; i++)
            {
                float y = top - h * (1 - v[i] / 140f) + 8;
                UIKit.LabelAt(parent, vals[i], 12, new Color(1, 1, 1, 0.4f), new Vector2(10, y), new Vector2(40, 18), null, TextAnchor.UpperRight);
            }
        }

        void UpdateSide()
        {
            gP.Lines.Clear(); gV.Lines.Clear();
            if (!rightSide)
            {
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPao, Color = new Color(1f, 0.35f, 0.3f), Width = 2.5f });
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPlv, Color = new Color(1f, 0.85f, 0.85f), Width = 2f });
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPla, Color = new Color(1f, 0.7f, 0.3f), Width = 2f });
                gV.Lines.Add(new TraceGraph.Series { Data = model.TrVlv, Color = new Color(0.55f, 0.8f, 1f), Width = 2f });
                gP.Max = 140; gV.Min = 40; gV.Max = 150; gV.GridValues = new[] { 60f, 100f, 140f };
                legendText.text = "<color=#FF594D>━ аорта</color>   <color=#FFD9D9>━ лява камера</color>   <color=#FFB34D>━ ляво предсърдие</color>   <color=#8CCCFF>━ обем ЛК</color>";
                sideBtn.Text.text = "Ляво сърце";
            }
            else
            {
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPpa, Color = new Color(0.45f, 0.6f, 1f), Width = 2.5f });
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPrv, Color = new Color(0.85f, 0.88f, 1f), Width = 2f });
                gP.Lines.Add(new TraceGraph.Series { Data = model.TrPra, Color = new Color(0.4f, 0.9f, 0.9f), Width = 2f });
                gV.Lines.Add(new TraceGraph.Series { Data = model.TrVrv, Color = new Color(0.55f, 0.8f, 1f), Width = 2f });
                gP.Max = 140; gV.Min = 30; gV.Max = 140; gV.GridValues = new[] { 40f, 80f, 120f };
                legendText.text = "<color=#7399FF>━ белодробна артерия</color>   <color=#D9E0FF>━ дясна камера</color>   <color=#66E6E6>━ дясно предсърдие</color>   <color=#8CCCFF>━ обем ДК</color>";
                sideBtn.Text.text = "Дясно сърце";
            }
        }

        public static Texture2D RadialTexture()
        {
            int w = 256, h = 144;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - w * 0.45f) / w, dy = (y - h * 0.55f) / h * 0.8f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 1.6f;
                    float k = Mathf.Clamp01(1 - d); k = k * k;
                    var c = Color.Lerp(new Color(0.018f, 0.019f, 0.025f), new Color(0.115f, 0.11f, 0.13f), k);
                    t.SetPixel(x, y, c);
                }
            t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        // ---------------------------------------------------------------- interaction
        void TogglePause() { paused = !paused; stepping = false; pauseBtn.Text.text = paused ? "Пусни" : "Пауза"; pauseBtn.On = paused; }

        void StepPhase()
        {
            stepping = true; stepFrom = model.Phase; paused = false;
            pauseBtn.Text.text = "Пауза"; pauseBtn.On = false;
        }

        void SetSpeed(float s)
        {
            speed = s;
            for (int i = 0; i < Speeds.Length; i++) speedBtns[i].On = Mathf.Approximately(Speeds[i], s);
        }

        void SetInternal(bool on)
        {
            heart.SetInternalView(on);
            internalBtn.On = on;
            flow.gameObject.SetActive(on);
        }

        void OnTap(Vector2 screen)
        {
            var p = heart.Pick(cam.ScreenPointToRay(screen));
            heart.Highlight(p);
            if (p != null)
            {
                partText.text = p.Bg + "\n<size=17><i><color=#B0B4BD>" + p.Latin + "</color></i></size>";
                partTimer = 5f;
            }
            else { partText.text = ""; }
        }

        void OnCardiacEvent(CardiacEvent e)
        {
            string msg = null;
            switch (e)
            {
                case CardiacEvent.MitralClose: msg = "Затваряне на митралната и трикуспидалната клапа → I сърдечен тон"; sound.PlayS1(); break;
                case CardiacEvent.AorticOpen: msg = "Отваряне на аортната клапа (налягане в ЛК > аортното)"; break;
                case CardiacEvent.AorticClose: msg = "Затваряне на аортната и белодробната клапа → II сърдечен тон, дикротична инцизура"; sound.PlayS2(); break;
                case CardiacEvent.MitralOpen: msg = "Отваряне на митралната клапа (налягане в ЛК < ЛП)"; break;
                case CardiacEvent.PWave: msg = "P вълна – деполяризация на предсърдията"; break;
                case CardiacEvent.QRS: msg = "QRS комплекс – деполяризация на камерите"; break;
            }
            if (msg != null) { eventText.text = msg; eventTimer = 2.5f; }
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (model == null || !ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float phys = paused ? 0f : dt * speed;
            if (phys > 0f) model.Advance(phys);
            if (stepping && model.Phase != stepFrom)
            {
                stepping = false; paused = true; pauseBtn.Text.text = "Пусни"; pauseBtn.On = true;
            }
            heart.Apply(model);
            if (flow.gameObject.activeSelf) flow.Tick(phys);
            UpdateUI(dt);
        }

        void UpdateUI(float dt)
        {
            int ph = (int)model.Phase;
            phaseIdx.text = $"ФАЗА {ph + 1} / 7  ·  {PhaseTexts.Group[ph].ToUpperInvariant()}";
            phaseIdx.color = PhaseColors[ph];
            phaseName.text = PhaseTexts.Bg[ph];
            phaseLatin.text = PhaseTexts.Latin[ph];
            phaseText.text = PhaseTexts.Text[ph];
            if (laidOutPhase != ph) { laidOutPhase = ph; LayoutCard(); }
            var vs = new[] { model.Mitral, model.Tricuspid, model.Aortic, model.Pulmonary };
            string[] vnames = { "Митрална (valva mitralis)", "Трикуспидална (valva tricuspidalis)", "Аортна (valva aortae)", "Белодробна (valva trunci pulmonalis)" };
            for (int i = 0; i < 4; i++)
            {
                bool open = vs[i].Leaflet > 0.5f;
                valveDots[i].color = open ? new Color(0.35f, 0.9f, 0.55f) : new Color(0.45f, 0.47f, 0.52f);
                valveTexts[i].text = vnames[i] + " – " + PhaseTexts.ValveState(open);
                valveTexts[i].color = open ? UIKit.TextMain : UIKit.TextDim;
            }
            if (eventTimer > 0) { eventTimer -= dt; if (eventTimer <= 0) eventText.text = ""; }
            if (partTimer > 0) { partTimer -= dt; if (partTimer <= 0) { partText.text = ""; heart.Highlight(null); } }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F1(float v) => v.ToString("0.0", ci).Replace('.', ',');
            statsText.text =
                $"{model.ActualHr:0} уд/мин\n" +
                $"{model.AoSys:0}/{model.AoDia:0} ({model.Map:0})\n" +
                $"{model.PaSys:0}/{model.PaDia:0} mmHg\n" +
                $"{model.EDV:0} / {model.ESV:0} ml\n" +
                $"{model.SV:0} ml · {model.EF * 100:0}%\n" +
                $"{F1(model.CO)} l/min\n" +
                $"{F1(model.Lap)} / {F1(model.Cvp)} mmHg\n" +
                $"{F1(model.Lvedp)} mmHg\n" +
                $"{model.Gradient:0} mmHg\n" +
                $"{model.RegFraction * 100:0} %\n" +
                $"{model.DpDt:0} mmHg/s\n" +
                $"{model.Bowditch * 100:0}% · +{model.Anrep * 100:0}%";
            statsText.supportRichText = true;
            hrLabel.text = $"Водач {model.HeartRate:0}/мин";

            float cur = model.SweepX;
            gP.Cursor = gV.Cursor = gE.Cursor = cur;
            gP.Refresh(); gV.Refresh(); gE.Refresh();
        }
    }
}
