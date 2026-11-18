using System;

namespace MedAtlas.Lungs
{
    public static class LungScenarios
    {
        public class Scenario { public string Id, Name, Text; public Action<RespParams> Apply; }

        public static readonly Scenario[] All =
        {
            new Scenario { Id = "normal", Name = "Нормално спокойно дишане", Text = "12 вдишвания/мин × 500 ml. Вдишването е активно, издишването пасивно. PaCO₂ 40, PaO₂ ~95, SpO₂ 97%.", Apply = p => { } },
            new Scenario { Id = "exercise", Name = "Физическо натоварване", Text = "Продукцията на CO₂ се увеличава шесткратно; вентилацията расте заедно с нея, затова PaCO₂ остава ~40. Дишането е по-дълбоко и по-често.", Apply = p => { p.rr = 22; p.effort = 12; p.vco2 = 1200; p.temp = 38; } },
            new Scenario { Id = "asthma", Name = "Астматичен пристъп", Text = "Стеснени бронхи: голямо съпротивление и дълга времева константа. Издишването не успява да завърши, въздухът се задържа (потокът не стига нула, дробовете остават раздути).", Apply = p => { p.resistance = 8; p.rr = 20; } },
            new Scenario { Id = "asthma2", Name = "Животозастрашаваща астма", Text = "Изтощение: дихателният обем пада, CO₂ расте (нормален или висок PaCO₂ при астма е тревожен знак), хипоксемия и ацидоза.", Apply = p => { p.resistance = 12; p.rr = 20; p.effort = 4.5f; } },
            new Scenario { Id = "copd", Name = "ХОББ / емфизем", Text = "Разрушени алвеоларни стени (големи були, висока разтегливост, по-малка повърхност) плюс стеснени бронхи: свръхраздуване, задръжка на CO₂ и нисък O₂.", Apply = p => { p.compliance = 220; p.resistance = 9; p.diffusion = 1.5f; p.rr = 18; p.deadSpace = 220; } },
            new Scenario { Id = "fibrosis", Name = "Белодробна фиброза", Text = "Сковани бели дробове (ниска разтегливост) и дебела бариера: малки чести вдишвания и хипоксемия, която се влошава при усилие; CO₂ обикновено е нормален или нисък.", Apply = p => { p.compliance = 35; p.diffusion = 3; p.rr = 24; } },
            new Scenario { Id = "ards", Name = "Пневмония / ARDS (шънт)", Text = "Пълни с течност алвеоли: кръвта минава през тях без O₂. Пробвай FiO₂ 1,0: PaO₂ почти не се подобрява, което е белегът на шънта.", Apply = p => { p.shunt = 0.32f; p.compliance = 45; p.rr = 26; } },
            new Scenario { Id = "opioid", Name = "Предозиране с опиоиди", Text = "Дихателният център вече не реагира на CO₂: бавно повърхностно дишане, хиперкапния, респираторна ацидоза и хипоксемия. Налоксонът го обръща.", Apply = p => { p.rr = 6; p.effort = 3.5f; p.chemo = 0.15f; } },
            new Scenario { Id = "panic", Name = "Хипервентилация (паника)", Text = "Дишането много повече от нужното „издухва“ CO₂: респираторна алкалоза (pH ↑), O₂ кривата се измества наляво, изтръпване и крампи от нисък йонизиран калций.", Apply = p => { p.rr = 28; p.effort = 6.5f; p.chemo = 0; } },
            new Scenario { Id = "altitude", Name = "Висока планина 5 000 m", Text = "Атмосферното налягане е наполовина: PAO₂ пада, каротидните телца предизвикват хипервентилация, PaCO₂ спада и кръвта става алкална.", Apply = p => { p.altitude = 5000; } },
            new Scenario { Id = "everest", Name = "Връх Еверест", Text = "PaO₂ около 30 mmHg дори при максимална хипервентилация. Повечето алпинисти се нуждаят от допълнителен кислород.", Apply = p => { p.altitude = 8848; p.rr = 18; p.effort = 8; } },
            new Scenario { Id = "anemia", Name = "Анемия", Text = "SpO₂ и PaO₂ са нормални, но хемоглобинът е наполовина, затова кислородното съдържание на кръвта е наполовина. Сатурацията не е същото като снабдяването с кислород!", Apply = p => { p.hb = 7; } },
            new Scenario { Id = "co", Name = "Отравяне с въглероден оксид", Text = "CO се свързва с Hb 240 пъти по-силно от O₂ и измества кривата наляво. Пулсоксиметърът показва фалшиво нормална SpO₂, а истинският O₂Hb и кислородното съдържание са ниски. Лечение: 100% O₂.", Apply = p => { p.coHb = 0.3f; } },
        };
    }

    public static class LungTexts
    {
        public static readonly string[] PhaseBg = { "Вдишване (активно)", "Издишване (пасивно)", "Пауза след издишване" };
        public static readonly string[] PhaseLatin = { "Inspiratio", "Exspiratio", "Pausa exspiratoria" };
        public static readonly string[] PhaseText =
        {
            "Диафрагмата се съкращава и слиза надолу, ребрата се повдигат: плевралното налягане става по-отрицателно, алвеоларното пада под атмосферното и въздухът влиза.",
            "Мускулите се отпускат и еластичната ретракция на белите дробове изтласква въздуха. При високо съпротивление в дихателните пътища това отнема повече от наличното време и въздухът се задържа.",
            "Няма поток: белите дробове са на ФОК, където вътрешната им ретракция се уравновесява с външната на гръдния кош. Газообменът в алвеолите продължава непрекъснато."
        };

        public static (string bg, string la) Part(string name, string cls)
        {
            switch (name)
            {
                case "Superior lobe of right lung": return ("Горен дял на десния бял дроб", "Lobus superior pulmonis dextri");
                case "Middle lobe of right lung": return ("Среден дял на десния бял дроб", "Lobus medius pulmonis dextri");
                case "Inferior lobe of right lung": return ("Долен дял на десния бял дроб", "Lobus inferior pulmonis dextri");
                case "Superior lobe of left lung": return ("Горен дял на левия бял дроб", "Lobus superior pulmonis sinistri");
                case "Inferior lobe of left lung": return ("Долен дял на левия бял дроб", "Lobus inferior pulmonis sinistri");
                case "Trachea": return ("Трахея", "Trachea");
                case "Right main bronchus": return ("Десен главен бронх", "Bronchus principalis dexter");
                case "Left main bronchus": return ("Ляв главен бронх", "Bronchus principalis sinister");
                case "Diaphragm": return ("Диафрагма", "Diaphragma");
                case "Pulmonary trunk": return ("Белодробен ствол", "Truncus pulmonalis");
                case "Right pulmonary artery": return ("Дясна белодробна артерия", "A. pulmonalis dextra");
                case "Left pulmonary artery": return ("Лява белодробна артерия", "A. pulmonalis sinistra");
                case "Right ventricle": return ("Дясна камера", "Ventriculus dexter");
                case "Left ventricle": return ("Лява камера", "Ventriculus sinister");
                case "Right atrium": return ("Дясно предсърдие", "Atrium dextrum");
                case "Left atrium": return ("Ляво предсърдие", "Atrium sinistrum");
                case "Ascending aorta": return ("Възходяща аорта", "Aorta ascendens");
                case "Aortic arch": return ("Дъга на аортата", "Arcus aortae");
                case "Thoracic aorta": return ("Гръдна аорта", "Aorta thoracica");
                case "Superior vena cava": return ("Горна празна вена", "V. cava superior");
                case "Inferior vena cava (thoracic part)": return ("Долна празна вена", "V. cava inferior");
            }
            string side = name.Contains("right") ? " (десен бял дроб)" : name.Contains("left") ? " (ляв бял дроб)" : "";
            switch (cls)
            {
                case "airway": return (name.Contains("lobar") ? "Делов бронх" + side : "Сегментен бронх" + side, name.Contains("lobar") ? "Bronchus lobaris" : "Bronchus segmentalis");
                case "pulmArtery": return ("Клон на белодробната артерия" + side, "Ramus arteriae pulmonalis");
                case "pulmVein": return ("Белодробна вена" + side, "Vena pulmonalis");
            }
            return (name, name);
        }
    }
}
