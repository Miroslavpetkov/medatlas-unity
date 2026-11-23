# MedAtlas – физиологични симулации (Unity)

Пробен Unity 6 (URP) проект: **сърдечен цикъл** и **дишане / газообмен**, 3D анатомия, движеща се физиология, интерфейс на български с латински термини.

- Уеб версия: папката `docs/` (GitHub Pages).
- Unity: отвори проекта, меню **MedAtlas → Отвори сцената със сърцето**, натисни Play. Превключване между „Сърце“ и „Бели дробове“ с бутоните горе.

Физиологията е пренесена от уеб версията medAtlas-3D (модел на кръвообращението и на дишането).

Анатомични модели: BodyParts3D © The Database Center for Life Science (CC BY-SA 2.1 JP) и Z-Anatomy (CC BY-SA 4.0), обработени. Матка и яйчници (`organ_uterus.wasm`): 3D Reference Organs на Human Reference Atlas (HuBMAP), публикувани в NIH 3D (3DPX-020996, 3DPX-020979, 3DPX-020980), направени от NLM Visible Human Female, CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/); обработени с `Tools/build_uterus.py`, маточните тръби са добавени схематично. Шрифт Inter (SIL OFL).
