namespace MedAtlas.Heart
{
    /// <summary>Bulgarian / Latin texts.</summary>
    public static class PartNames
    {
        public static void Describe(HeartModel.Part p)
        {
            string n = p.Name;
            (string bg, string la) r;
            switch (n)
            {
                case "myocardium": r = ("Миокард на камерите", "Myocardium ventriculorum"); break;
                case "paries_LA": r = ("Ляво предсърдие", "Atrium sinistrum"); break;
                case "paries_RA": r = ("Дясно предсърдие", "Atrium dextrum"); break;
                case "cavum_LV": r = ("Кухина на лявата камера", "Cavitas ventriculi sinistri"); break;
                case "cavum_RV": r = ("Кухина на дясната камера", "Cavitas ventriculi dextri"); break;
                case "cavum_LA": r = ("Кухина на лявото предсърдие", "Cavitas atrii sinistri"); break;
                case "cavum_RA": r = ("Кухина на дясното предсърдие", "Cavitas atrii dextri"); break;
                case "mitral_ant": r = ("Предно платно на митралната клапа", "Cuspis anterior valvae mitralis"); break;
                case "mitral_post": r = ("Задно платно на митралната клапа", "Cuspis posterior valvae mitralis"); break;
                case "tricusp_ant": r = ("Предно платно на трикуспидалната клапа", "Cuspis anterior valvae tricuspidalis"); break;
                case "tricusp_post": r = ("Задно платно на трикуспидалната клапа", "Cuspis posterior valvae tricuspidalis"); break;
                case "tricusp_sept": r = ("Септално платно на трикуспидалната клапа", "Cuspis septalis valvae tricuspidalis"); break;
                case "papLV_antlat": r = ("Преден папиларен мускул (лява камера)", "M. papillaris anterior ventriculi sinistri"); break;
                case "papLV_lat": r = ("Заден папиларен мускул (лява камера)", "M. papillaris posterior ventriculi sinistri"); break;
                case "papRV_ant": r = ("Преден папиларен мускул (дясна камера)", "M. papillaris anterior ventriculi dextri"); break;
                case "papRV_post": r = ("Заден папиларен мускул (дясна камера)", "M. papillaris posterior ventriculi dextri"); break;
                case "papRV_sept": r = ("Септален папиларен мускул", "M. papillaris septalis"); break;
                case "aortic_ant": r = ("Дясно (коронарно) платно на аортната клапа", "Valvula semilunaris dextra valvae aortae"); break;
                case "aortic_lp": r = ("Ляво (коронарно) платно на аортната клапа", "Valvula semilunaris sinistra valvae aortae"); break;
                case "aortic_rp": r = ("Задно (некоронарно) платно на аортната клапа", "Valvula semilunaris posterior valvae aortae"); break;
                case "pulm_la": case "pulm_ra": case "pulm_post": r = ("Платно на белодробната клапа", "Valvula semilunaris valvae trunci pulmonalis"); break;
                case "aorta_asc": r = ("Възходяща аорта", "Aorta ascendens"); break;
                case "aorta_arch": r = ("Дъга на аортата", "Arcus aortae"); break;
                case "aorta_desc": r = ("Низходяща (гръдна) аорта", "Aorta descendens, pars thoracica"); break;
                case "truncus_brachiocephalicus": r = ("Брахиоцефален ствол", "Truncus brachiocephalicus"); break;
                case "a_carotis_sin": r = ("Лява обща сънна артерия", "A. carotis communis sinistra"); break;
                case "a_carotis_dx": r = ("Дясна обща сънна артерия", "A. carotis communis dextra"); break;
                case "a_subclavia_sin": r = ("Лява подключична артерия", "A. subclavia sinistra"); break;
                case "truncus_pulm": r = ("Белодробен ствол", "Truncus pulmonalis"); break;
                case "a_pulm_sin": r = ("Лява белодробна артерия", "A. pulmonalis sinistra"); break;
                case "a_pulm_dx": r = ("Дясна белодробна артерия", "A. pulmonalis dextra"); break;
                case "vv_pulm_1": case "vv_pulm_2": r = ("Лява горна белодробна вена", "V. pulmonalis superior sinistra"); break;
                case "vv_pulm_3": case "vv_pulm_4": case "vv_pulm_5": r = ("Лява долна белодробна вена", "V. pulmonalis inferior sinistra"); break;
                case "vv_pulm_6": r = ("Дясна горна белодробна вена", "V. pulmonalis superior dextra"); break;
                case "vv_pulm_7": r = ("Дясна долна белодробна вена", "V. pulmonalis inferior dextra"); break;
                case "v_cava_sup": r = ("Горна празна вена", "V. cava superior"); break;
                case "v_cava_inf": r = ("Долна празна вена", "V. cava inferior"); break;
                default: r = Coronary(n); break;
            }
            p.Bg = r.bg; p.Latin = r.la;
        }

        static (string, string) Coronary(string n)
        {
            int id = 0;
            int k = n.IndexOf("FJ");
            if (k >= 0) int.TryParse(n.Substring(k + 2), out id);
            if (id == 2631) return ("Предна междукамерна артерия (LAD)", "R. interventricularis anterior a. coronariae sinistrae");
            if (id >= 2632 && id <= 2648) return ("Клонове на предната междукамерна артерия", "Rami diagonales et septales r. interventricularis anterioris");
            if (id >= 2649 && id <= 2654) return ("Огъваща (циркумфлексна) артерия", "R. circumflexus a. coronariae sinistrae");
            if (id == 2655) return ("Коронарен синус", "Sinus coronarius");
            if (id == 2656) return ("Голяма сърдечна вена", "V. cardiaca magna");
            if (id >= 2667 && id <= 2677) return ("Клонове на дясната коронарна артерия", "Rami a. coronariae dextrae (r. marginalis dexter, r. coni arteriosi)");
            if (id >= 2692 && id <= 2700) return ("Задна междукамерна артерия", "R. interventricularis posterior");
            if (id >= 2714 && id <= 2722) return ("Задни камерни клонове", "Rami posterolaterales");
            if (id == 2723) return ("Дясна коронарна артерия", "A. coronaria dextra");
            if (id == 2737) return ("Лява коронарна артерия (ствол)", "A. coronaria sinistra");
            if (id == 2724 || id == 2731) return ("Малка сърдечна вена", "V. cardiaca parva");
            if (id >= 2727 && id <= 2729) return ("Дясна маргинална вена", "V. marginalis dextra");
            if (id >= 2678 && id <= 2691) return ("Средна сърдечна вена", "V. cardiaca media (v. interventricularis posterior)");
            if (id >= 2701 && id <= 2713) return ("Задна вена на лявата камера", "V. ventriculi sinistri posterior");
            return ("Коронарен съд", "Vas coronarium");
        }
    }

    public static class PhaseTexts
    {
        public static readonly string[] Bg =
        {
            "Предсърдна систола", "Изоволуметрично съкращение", "Бързо изтласкване", "Бавно изтласкване",
            "Изоволуметрично отпускане", "Бързо пълнене", "Бавно пълнене (диастаза)"
        };
        public static readonly string[] Latin =
        {
            "Systole atriorum", "Contractio isovolumetrica", "Ejectio rapida", "Ejectio reducta",
            "Relaxatio isovolumetrica", "Repletio rapida", "Diastasis"
        };
        public static readonly string[] Group =
        {
            "Камерна диастола (край)", "Камерна систола", "Камерна систола", "Камерна систола",
            "Камерна диастола", "Камерна диастола", "Камерна диастола"
        };
        public static readonly string[] Text =
        {
            "Деполяризацията на предсърдията (P вълна) предизвиква тяхното съкращение. Предсърдията изтласкват последните ~20–25% от кръвта в камерите („предсърдно допълване“). Атриовентрикуларните клапи са отворени, полулунните – затворени. В предсърдното налягане се вижда a-вълната.",
            "След QRS комплекса камерите започват да се съкращават. Налягането в тях надвишава предсърдното и митралната и трикуспидалната клапа се затварят – I сърдечен тон. Всички клапи са затворени, обемът на камерите е постоянен (≈ ТДО), налягането рязко расте.",
            "Когато налягането в лявата камера надвиши аортното (~80 mmHg), аортната клапа се отваря (белодробната – при ~10 mmHg). Около 2/3 от ударния обем се изтласква бързо; налягането в камерата и аортата достига максимум (~120 mmHg).",
            "Реполяризацията на камерите (T вълна) започва, силата на съкращението намалява. Изтласкването продължава по-бавно, налягането в камерата и аортата леко спада. Предсърдията се пълнят при затворени AV клапи (v-вълна).",
            "Налягането в камерите пада под аортното и белодробното – полулунните клапи се затварят: II сърдечен тон и дикротична инцизура на аортната крива. Всички клапи са затворени, обемът е постоянен (≈ КСО), налягането рязко пада.",
            "Камерното налягане пада под предсърдното – AV клапите се отварят и натрупаната в предсърдията кръв нахлува бързо в камерите (E вълна на трансмитралния поток). Възможен е физиологичен III тон.",
            "Наляганията в предсърдията и камерите почти се изравняват, пълненето е бавно (кръвта преминава пасивно от вените през предсърдията). При тахикардия тази фаза се скъсява първа."
        };

        public static string ValveState(bool open) => open ? "отворена" : "затворена";
    }
}
