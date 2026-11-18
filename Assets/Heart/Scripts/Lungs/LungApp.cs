using System.Collections.Generic;
using MedAtlas.Heart;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MedAtlas.Lungs
{
    /// <summary>Entry point of the breathing simulation (scene "Lungs").</summary>
    public class LungApp : MonoBehaviour
    {
        LungModel model;
        LungAnatomy anatomy;
        AirFlow air;
        OrbitCamera orbit;
        Camera cam;
        float speed = 1f; bool paused;
        string currentScenario = "normal";
        double beatClock;

        RectTransform card, scenPanel, parPanel;
        Text phaseIdx, phaseName, phaseLatin, phaseText, scenTitle, scenText, pressText, statsText, partText;
        TraceGraph gVol, gFlow, gGas, gO2;
        UIKit.Btn pauseBtn, internalBtn, airBtn, scenBtn, parBtn;
        UIKit.Btn[] speedBtns;
        static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
        readonly List<System.Action> paramSync = new List<System.Action>();
        int laidOut = -1; float partTimer;

        const int N = 600;
        readonly float[] trVol = new float[N], trFlow = new float[N], trPaO2 = new float[N], trPaCO2 = new float[N], trSpO2 = new float[N];
        readonly float[] o2Curve = new float[151];
        float sampleAcc, gasAcc;

        bool ready;

        System.Collections.IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            HeartApp.BuildStudio(out cam, out orbit);
            orbit.Distance = 11f; orbit.MinDistance = 3f; orbit.MaxDistance = 22f; orbit.Yaw = -10f; orbit.Pitch = 4f;
            orbit.Target = new Vector3(0, -0.15f, 0);
            model = new LungModel();
            var root = new GameObject("Lungs");
            anatomy = root.AddComponent<LungAnatomy>();
            byte[] data = null;
            yield return ModelFiles.Load("lungs_model.wasm", d => data = d);
            anatomy.Build(data);
            root.transform.position = -anatomy.Bounds.center;
            var airGo = new GameObject("Air"); airGo.transform.SetParent(root.transform, false);
            air = airGo.AddComponent<AirFlow>(); air.Build(anatomy);
            for (int i = 0; i < N; i++) { trPaO2[i] = model.PaO2; trPaCO2[i] = model.PaCO2; trSpO2[i] = model.SpO2 * 100; }
            BuildUI();
            SetInternal(false);
            orbit.OnTap = OnTap;
            ready = true;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-autoshot") StartCoroutine(AutoShot(args[i + 1]));
        }

        System.Collections.IEnumerator AutoShot(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "lungs_external.png"));
            yield return new WaitForSeconds(1.7f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "lungs_external2.png"));
            yield return new WaitForSeconds(0.5f);
            SetInternal(true);
            yield return new WaitForSeconds(2.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "lungs_internal.png"));
            yield return new WaitForSeconds(0.5f);
            ApplyScenario("asthma");
            yield return new WaitForSeconds(4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "lungs_asthma.png"));
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // ------------------------------------------------------------ UI
        void BuildUI()
        {
            UIKit.Init();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>();
            }
            var bgGo = new GameObject("BackdropCanvas"); var bgc = bgGo.AddComponent<Canvas>();
            bgc.renderMode = RenderMode.ScreenSpaceCamera; bgc.worldCamera = cam; bgc.planeDistance = 60f; bgc.sortingOrder = -100;
            var bgr = UIKit.Rect("bg", bgGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var raw = bgr.gameObject.AddComponent<RawImage>(); raw.texture = HeartApp.RadialTexture(); raw.raycastTarget = false;

            var cgo = new GameObject("UI"); var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.6f;
            cgo.AddComponent<GraphicRaycaster>();
            var C = cgo.transform;

            UIKit.LabelAt(C, "Бели дробове", 38, UIKit.TextMain, new Vector2(44, -30), new Vector2(600, 50), UIKit.SemiBold);
            UIKit.LabelAt(C, "Pulmones  ·  Дишане и газообмен (Respiratio)", 18, UIKit.TextDim, new Vector2(46, -80), new Vector2(700, 30), UIKit.Italic);
            SceneTabs.Add(C, "Lungs");
            var hint = UIKit.LabelAt(C, "Плъзни – въртене · колелце / два пръста – мащаб · десен бутон – преместване · клик върху част – име", 13, new Color(1, 1, 1, 0.38f), new Vector2(46, -104), new Vector2(900, 20));

            // phase card
            card = UIKit.Rect("PhaseCard", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -128), new Vector2(450, 420));
            UIKit.PanelImage(card, UIKit.Panel);
            scenTitle = UIKit.LabelAt(card, "", 14, new Color(0.55f, 0.85f, 1f), Vector2.zero, new Vector2(404, 22), UIKit.SemiBold);
            scenText = UIKit.LabelAt(card, "", 14, new Color(0.75f, 0.85f, 0.95f), Vector2.zero, new Vector2(404, 80), UIKit.Italic);
            phaseIdx = UIKit.LabelAt(card, "", 15, UIKit.Accent, Vector2.zero, new Vector2(410, 24), UIKit.SemiBold);
            phaseName = UIKit.LabelAt(card, "", 28, UIKit.TextMain, Vector2.zero, new Vector2(410, 40), UIKit.SemiBold);
            phaseLatin = UIKit.LabelAt(card, "", 19, UIKit.TextDim, Vector2.zero, new Vector2(410, 30), UIKit.Italic);
            phaseText = UIKit.LabelAt(card, "", 16, new Color(0.84f, 0.85f, 0.88f), Vector2.zero, new Vector2(404, 200));
            pressText = UIKit.LabelAt(card, "", 15, new Color(1f, 0.86f, 0.5f), Vector2.zero, new Vector2(404, 70), UIKit.SemiBold);

            // stats
            var stats = UIKit.Rect("Stats", C, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -30), new Vector2(340, 372));
            UIKit.PanelImage(stats, UIKit.Panel);
            var lbl = UIKit.LabelAt(stats, "Дихателна честота\nДихателен обем\nМинутен дихателен обем\nАлвеоларна вентилация\nPaO₂ / PaCO₂\npH\nSpO₂ (пулсоксиметър)\nИстинска O₂Hb сатурация\nКислородно съдържание\nАлвеоларно PO₂ (PAO₂)\nA–a градиент\nЗадържан въздух (авто-PEEP)\nВремева константа R×C\nP50 на кривата", 14, UIKit.TextDim, new Vector2(20, -16), new Vector2(230, 360));
            lbl.lineSpacing = 1.32f;
            statsText = UIKit.LabelAt(stats, "", 14, UIKit.TextMain, new Vector2(170, -16), new Vector2(150, 360), UIKit.SemiBold, TextAnchor.UpperRight);
            statsText.lineSpacing = 1.32f;

            // graphs
            var gp = UIKit.Rect("Graphs", C, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 112), new Vector2(660, 520));
            UIKit.PanelImage(gp, UIKit.Panel);
            UIKit.LabelAt(gp, "Обем над ФОК (ml) · поток (l/s) · 15 s", 15, UIKit.TextMain, new Vector2(20, -14), new Vector2(400, 22), UIKit.SemiBold);
            gVol = Graph(gp, new Vector2(56, -42), new Vector2(360, 120)); gVol.Min = -50; gVol.Max = 1300; gVol.GridValues = new[] { 0f, 500f, 1000f };
            gVol.Lines.Add(new TraceGraph.Series { Data = trVol, Color = new Color(0.48f, 0.77f, 1f), Width = 2.2f });
            gFlow = Graph(gp, new Vector2(56, -172), new Vector2(360, 110)); gFlow.Min = -1.6f; gFlow.Max = 1.6f; gFlow.GridValues = new[] { 0f };
            gFlow.Lines.Add(new TraceGraph.Series { Data = trFlow, Color = new Color(1f, 0.82f, 0.4f), Width = 2.2f });
            UIKit.LabelAt(gp, "1000", 11, new Color(1, 1, 1, 0.4f), new Vector2(10, -64), new Vector2(42, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "0", 11, new Color(1, 1, 1, 0.4f), new Vector2(10, -150), new Vector2(42, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "вдиш.", 11, new Color(1, 1, 1, 0.4f), new Vector2(6, -180), new Vector2(46, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "издиш.", 11, new Color(1, 1, 1, 0.4f), new Vector2(6, -262), new Vector2(46, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "Кръвни газове · тренд 2 min", 15, UIKit.TextMain, new Vector2(20, -294), new Vector2(400, 22), UIKit.SemiBold);
            var leg = UIKit.LabelAt(gp, "<color=#FF6B6B>━ PaO₂</color>  <color=#C39BFF>━ PaCO₂</color>  <color=#4FD8FF>━ SpO₂ %</color>", 13, UIKit.TextDim, new Vector2(20, -316), new Vector2(400, 20));
            leg.supportRichText = true;
            gGas = Graph(gp, new Vector2(56, -340), new Vector2(360, 160)); gGas.Min = 0; gGas.Max = 150; gGas.GridValues = new[] { 40f, 100f };
            gGas.Lines.Add(new TraceGraph.Series { Data = trPaO2, Color = new Color(1f, 0.42f, 0.42f), Width = 2f });
            gGas.Lines.Add(new TraceGraph.Series { Data = trPaCO2, Color = new Color(0.76f, 0.61f, 1f), Width = 2f });
            gGas.Lines.Add(new TraceGraph.Series { Data = trSpO2, Color = new Color(0.31f, 0.85f, 1f), Width = 2f });
            UIKit.LabelAt(gp, "40", 11, new Color(1, 1, 1, 0.4f), new Vector2(10, -450), new Vector2(42, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "100", 11, new Color(1, 1, 1, 0.4f), new Vector2(10, -390), new Vector2(42, 16), null, TextAnchor.UpperRight);
            UIKit.LabelAt(gp, "Крива на O₂-дисоциация", 14, UIKit.TextMain, new Vector2(440, -14), new Vector2(210, 22), UIKit.SemiBold);
            gO2 = Graph(gp, new Vector2(446, -42), new Vector2(194, 194)); gO2.Min = 0; gO2.Max = 1; gO2.GridValues = new[] { 0.5f, 0.9f };
            gO2.Lines.Add(new TraceGraph.Series { Data = o2Curve, Color = new Color(1f, 0.45f, 0.45f), Width = 2.4f });
            gO2.MarkerColor = new Color(1f, 0.95f, 0.5f);
            UIKit.LabelAt(gp, "PO₂ 0–150 mmHg →", 11, UIKit.TextDim, new Vector2(446, -240), new Vector2(194, 16));
            UIKit.LabelAt(gp, "↑ SO₂", 11, UIKit.TextDim, new Vector2(446, -256), new Vector2(194, 16));
            var info = UIKit.LabelAt(gp, "Точката е артериалната кръв. Ацидоза, ↑CO₂, ↑t° и 2,3-БФГ изместват кривата надясно; CO – наляво.", 12, UIKit.TextDim, new Vector2(446, -280), new Vector2(200, 120));

            // controls
            var bar = UIKit.Rect("Controls", C, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(36, 30), new Vector2(1848, 64));
            UIKit.PanelImage(bar, UIKit.Panel);
            float x0 = 14;
            pauseBtn = UIKit.Button(bar, "Пауза", new Vector2(x0, 0), new Vector2(110, 44), () => { paused = !paused; pauseBtn.Text.text = paused ? "Пусни" : "Пауза"; pauseBtn.On = paused; }); x0 += 126;
            var sl = UIKit.Label(bar, "Скорост", 15, UIKit.TextDim, null, TextAnchor.MiddleLeft);
            var srt = sl.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0, 0.5f); srt.pivot = new Vector2(0, 0.5f); srt.anchoredPosition = new Vector2(x0, 0); srt.sizeDelta = new Vector2(80, 40); x0 += 78;
            speedBtns = new UIKit.Btn[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
            {
                int k = i; string l = Speeds[i].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + "×";
                speedBtns[i] = UIKit.Button(bar, l, new Vector2(x0, 0), new Vector2(66, 44), () => SetSpeed(Speeds[k])); x0 += 72;
            }
            x0 += 24;
            internalBtn = UIKit.Button(bar, "Вътрешен изглед", new Vector2(x0, 0), new Vector2(190, 44), () => SetInternal(!anatomy.InternalView)); x0 += 198;
            airBtn = UIKit.Button(bar, "Въздушен поток", new Vector2(x0, 0), new Vector2(180, 44), () => { air.Visible = !air.Visible; airBtn.On = air.Visible; }); x0 += 188;
            UIKit.Button(bar, "Център", new Vector2(x0, 0), new Vector2(100, 44), () => orbit.ResetView()); x0 += 108;
            scenBtn = UIKit.Button(bar, "Сценарии", new Vector2(x0, 0), new Vector2(132, 44), () => ShowPanel(scenPanel.gameObject.activeSelf ? null : scenPanel)); x0 += 140;
            parBtn = UIKit.Button(bar, "Параметри", new Vector2(x0, 0), new Vector2(132, 44), () => ShowPanel(parPanel.gameObject.activeSelf ? null : parPanel));
            airBtn.On = true;
            SetSpeed(1f);
            BuildScenarioPanel(C);
            BuildParamPanel(C);

            partText = UIKit.Label(C, "", 20, UIKit.TextMain, UIKit.SemiBold, TextAnchor.MiddleCenter);
            var prt = partText.rectTransform; prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0); prt.pivot = new Vector2(0.5f, 0);
            prt.anchoredPosition = new Vector2(-180, 110); prt.sizeDelta = new Vector2(900, 70); partText.supportRichText = true;
            var credit = UIKit.Label(C, "Анатомични модели: Z-Anatomy (CC BY-SA 4.0), по BodyParts3D © DBCLS; физиология по medAtlas", 11, new Color(1, 1, 1, 0.3f), null, TextAnchor.LowerLeft);
            var crt = credit.rectTransform; crt.anchorMin = crt.anchorMax = Vector2.zero; crt.pivot = Vector2.zero; crt.anchoredPosition = new Vector2(40, 6); crt.sizeDelta = new Vector2(900, 20);

            for (int i = 0; i <= 150; i++) o2Curve[i] = LungModel.Saturation(i, 7.4f, 37f, 40f, 0f, 0f);
            PortraitHint.Add(C);
        }

        TraceGraph Graph(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var rt = UIKit.Rect("graph", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            var g = rt.gameObject.AddComponent<TraceGraph>(); g.raycastTarget = false; return g;
        }

        void SetSpeed(float s) { speed = s; for (int i = 0; i < Speeds.Length; i++) speedBtns[i].On = Mathf.Approximately(Speeds[i], s); }

        void SetInternal(bool on) { anatomy.SetInternalView(on); internalBtn.On = on; }

        void ShowPanel(RectTransform which)
        {
            scenPanel.gameObject.SetActive(which == scenPanel); parPanel.gameObject.SetActive(which == parPanel);
            card.gameObject.SetActive(which == null);
            scenBtn.On = which == scenPanel; parBtn.On = which == parPanel;
        }

        void BuildScenarioPanel(Transform C)
        {
            scenPanel = UIKit.Rect("Scenarios", C, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -128), new Vector2(450, 640));
            UIKit.PanelImage(scenPanel, UIKit.Panel);
            UIKit.LabelAt(scenPanel, "Сценарии", 22, UIKit.TextMain, new Vector2(22, -16), new Vector2(400, 30), UIKit.SemiBold);
            UIKit.LabelAt(scenPanel, "Избери състояние – моделът се пренастройва веднага.", 13, UIKit.TextDim, new Vector2(22, -48), new Vector2(400, 20));
            for (int i = 0; i < LungScenarios.All.Length; i++)
            {
                var sc = LungScenarios.All[i]; string id = sc.Id;
                var b = UIKit.Button(scenPanel, sc.Name, Vector2.zero, new Vector2(404, 36), () => ApplyScenario(id), 15);
                var rt = b.Button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
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
            float y = -58; var ci = System.Globalization.CultureInfo.InvariantCulture;
            void Row(string label, float min, float max, System.Func<float> get, System.Action<float> set, string fmt, string unit)
            {
                UIKit.LabelAt(parPanel, label, 13, UIKit.TextDim, new Vector2(22, y), new Vector2(300, 18));
                var val = UIKit.LabelAt(parPanel, "", 13, UIKit.TextMain, new Vector2(300, y), new Vector2(128, 18), UIKit.SemiBold, TextAnchor.UpperRight);
                var s = UIKit.Slider(parPanel, Vector2.zero, new Vector2(400, 20), min, max, get(), v => set(v), false);
                var r = s.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(26, y - 19);
                string F() => get().ToString(fmt, ci).Replace('.', ',') + unit;
                s.onValueChanged.AddListener(v => val.text = F()); val.text = F();
                paramSync.Add(() => { s.SetValueWithoutNotify(get()); val.text = F(); });
                y -= 44;
            }
            var P = model.P;
            Row("Честота на дишане (зададена)", 4, 40, () => model.P.rr, v => model.P.rr = Mathf.Round(v), "0", " /min");
            Row("Вдишвателно усилие", 1, 20, () => model.P.effort, v => model.P.effort = v, "0.0", " cmH₂O");
            Row("Съпротивление на дихателните пътища", 1, 20, () => model.P.resistance, v => model.P.resistance = v, "0.0", "");
            Row("Разтегливост на белия дроб", 20, 300, () => model.P.compliance, v => model.P.compliance = Mathf.Round(v / 5) * 5, "0", " ml/cmH₂O");
            Row("Мъртво пространство", 100, 400, () => model.P.deadSpace, v => model.P.deadSpace = Mathf.Round(v / 10) * 10, "0", " ml");
            Row("Вдишван O₂ (FiO₂)", 0.12f, 1f, () => model.P.fio2, v => model.P.fio2 = v, "0.00", "");
            Row("Надморска височина", 0, 8848, () => model.P.altitude, v => model.P.altitude = Mathf.Round(v / 50) * 50, "0", " m");
            Row("Шънт (невентилирани алвеоли)", 0, 0.6f, () => model.P.shunt, v => model.P.shunt = v, "0.00", "");
            Row("Дифузионна бариера", 0, 4, () => model.P.diffusion, v => model.P.diffusion = v, "0.0", "");
            Row("Метаболизъм (продукция на CO₂)", 100, 2000, () => model.P.vco2, v => model.P.vco2 = Mathf.Round(v / 10) * 10, "0", " ml/min");
            Row("Чувствителност на дихателния център", 0, 2, () => model.P.chemo, v => model.P.chemo = v, "0.00", "");
            Row("Хемоглобин", 4, 20, () => model.P.hb, v => model.P.hb = v, "0.0", " g/dl");
            Row("Въглероден оксид (COHb)", 0, 0.6f, () => model.P.coHb, v => model.P.coHb = v, "0.00", "");
            Row("Телесна температура", 32, 42, () => model.P.temp, v => model.P.temp = v, "0.0", " °C");
            Row("Изместване от 2,3-БФГ", -6, 8, () => model.P.dpg, v => model.P.dpg = v, "0.0", " mmHg");
            parPanel.sizeDelta = new Vector2(450, -y + 20);
            parPanel.gameObject.SetActive(false);
        }

        void ApplyScenario(string id)
        {
            var sc = System.Array.Find(LungScenarios.All, s => s.Id == id);
            var p = new RespParams(); sc.Apply(p);
            model.SetParams(p, true);
            currentScenario = id;
            for (int i = 0; i < N; i++) { trPaO2[i] = model.PaO2; trPaCO2[i] = model.PaCO2; trSpO2[i] = model.SpO2 * 100; }
            foreach (var a in paramSync) a();
            laidOut = -1;
            ShowPanel(null);
        }

        void OnTap(Vector2 screen)
        {
            var p = anatomy.Pick(cam.ScreenPointToRay(screen));
            anatomy.Highlight(p);
            if (p != null) { partText.text = p.Bg + "\n<size=17><i><color=#B0B4BD>" + p.Latin + "</color></i></size>"; partTimer = 5f; }
            else partText.text = "";
        }

        int PhaseIndex => model.Inspiration ? 0 : model.Expiration ? 1 : 2;

        void LayoutCard()
        {
            float y = -18;
            bool has = currentScenario != "normal";
            scenTitle.gameObject.SetActive(has); scenText.gameObject.SetActive(has);
            if (has)
            {
                var sc = System.Array.Find(LungScenarios.All, s => s.Id == currentScenario);
                scenTitle.text = "СЦЕНАРИЙ: " + sc.Name.ToUpperInvariant(); scenText.text = sc.Text;
                scenTitle.rectTransform.anchoredPosition = new Vector2(24, y); y -= 22;
                scenText.rectTransform.anchoredPosition = new Vector2(24, y);
                float hh = scenText.preferredHeight; scenText.rectTransform.sizeDelta = new Vector2(404, hh); y -= hh + 14;
            }
            void Put(Text t, float gap)
            {
                var rt = t.rectTransform; rt.anchoredPosition = new Vector2(24, y);
                float h = t.preferredHeight; rt.sizeDelta = new Vector2(rt.sizeDelta.x, h); y -= h + gap;
            }
            Put(phaseIdx, 4); Put(phaseName, 4); Put(phaseLatin, 14); Put(phaseText, 16);
            pressText.rectTransform.anchoredPosition = new Vector2(24, y); y -= 66;
            card.sizeDelta = new Vector2(450, -y + 10);
        }

        // ------------------------------------------------------------ loop
        void Update()
        {
            if (model == null || !ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float phys = paused ? 0 : dt * speed;
            if (phys > 0) model.Advance(phys);
            beatClock += phys;
            float hb = (float)(beatClock * 72.0 / 60.0); hb -= Mathf.Floor(hb);
            float beat = hb < 0.35f ? Mathf.Sin(hb / 0.35f * Mathf.PI) : 0f;
            anatomy.Apply(model.V, beat, model.SaO2);
            air.Tick(phys, model.Flow);

            // traces
            sampleAcc += phys;
            while (sampleAcc >= 15f / N) { sampleAcc -= 15f / N; Shift(trVol, model.V); Shift(trFlow, model.Flow / 1000f); }
            gasAcc += phys;
            while (gasAcc >= 120f / N) { gasAcc -= 120f / N; Shift(trPaO2, model.PaO2); Shift(trPaCO2, model.PaCO2); Shift(trSpO2, model.SpO2 * 100f); }
            UpdateUI(dt);
        }

        static void Shift(float[] a, float v) { System.Array.Copy(a, 1, a, 0, a.Length - 1); a[a.Length - 1] = v; }

        void UpdateUI(float dt)
        {
            int ph = PhaseIndex;
            phaseIdx.text = ph == 0 ? "ВДИШВАНЕ · INSPIRATIO" : ph == 1 ? "ИЗДИШВАНЕ · EXSPIRATIO" : "ПАУЗА";
            phaseIdx.color = ph == 0 ? new Color(0.49f, 0.77f, 1f) : ph == 1 ? new Color(1f, 0.82f, 0.4f) : new Color(0.24f, 1f, 0.54f);
            phaseName.text = LungTexts.PhaseBg[ph]; phaseLatin.text = LungTexts.PhaseLatin[ph]; phaseText.text = LungTexts.PhaseText[ph];
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F1(float v) => v.ToString("0.0", ci).Replace('.', ',');
            pressText.text = $"Pmus {F1(model.Pmus)} · Palv {F1(model.Palv)} · Pпл {F1(model.Ppl)} cmH₂O\nпоток {F1(model.Flow / 1000f)} l/s · обем {model.V:0} ml над ФОК";
            if (laidOut != ph) { laidOut = ph; LayoutCard(); }
            statsText.text =
                $"{model.Rate:0} /min\n{model.Vt:0} ml\n{F1(model.MinuteVent)} l/min\n{F1(model.AlvVent)} l/min\n" +
                $"{model.PaO2:0} / {model.PaCO2:0} mmHg\n{model.pH.ToString("0.00", ci).Replace('.', ',')}\n{model.SpO2 * 100:0} %\n{model.SaO2 * 100:0} %\n" +
                $"{F1(model.CaO2)} ml/dl\n{model.PAO2:0} mmHg\n{model.PAO2 - model.PaO2:0} mmHg\n{model.Eev:0} ml\n" +
                $"{model.TimeConstant.ToString("0.00", ci).Replace('.', ',')} s\n{F1(model.P50)} mmHg";
            if (partTimer > 0) { partTimer -= dt; if (partTimer <= 0) { partText.text = ""; anatomy.Highlight(null); } }

            // O2 curve shifted for the current blood
            var p = model.P;
            for (int i = 0; i <= 150; i++) o2Curve[i] = LungModel.Saturation(i, model.pH, p.temp, model.PaCO2, p.dpg, p.coHb);
            gO2.MarkerX = model.PaO2 / 150f; gO2.MarkerY = model.SaO2;
            gVol.Refresh(); gFlow.Refresh(); gGas.Refresh(); gO2.Refresh();
        }
    }
}
