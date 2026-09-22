// Таблица собрана скриптом из файлов игры 1.38.3.1. После обновления игры её пересобирают,
// руками не правят.
//
// Артефакты игры: путь в Resources, название и описание свойства — как в самой игре.
// «Старая карта» и компас сюда не входят: они в разделе «Разведка» (мод 1.1).

namespace PocketRoguesCheats
{
    /// <summary>Артефакт игры: чем его свойство включается и как его назвать, если не загрузился.</summary>
    internal sealed class ArtifactInfo
    {
        /// <summary>Путь в Resources игры, нижний регистр — как в таблице путей Unity.</summary>
        public readonly string Path;

        /// <summary>Название из русского перевода игры — запасное и старое имя настройки (до 1.8).</summary>
        public readonly string TitleRu;

        /// <summary>Название из английского перевода игры — имя настройки в файле настроек.</summary>
        public readonly string TitleEn;

        /// <summary>Описание свойства по-русски, с числами, — запасное: в окне — описание самой игры.</summary>
        public readonly string NoteRu;

        public ArtifactInfo(string path, string titleRu, string titleEn, string noteRu)
        {
            Path = path;
            TitleRu = titleRu;
            TitleEn = titleEn;
            NoteRu = noteRu;
        }
    }

    /// <summary>Все артефакты, которые мод умеет включать.</summary>
    internal static class ArtifactsTable
    {
        /// <summary>Версия игры, по файлам которой собрана таблица.</summary>
        internal const string GameVersion = "1.38.3.1";

        internal static readonly ArtifactInfo[] All = new ArtifactInfo[]
        {
            new ArtifactInfo("items/trash/126/itemart_bondages",           "Бинты",                 "Bandages",            "+0.30 к восстановлению ОЗ/с"),
            new ArtifactInfo("items/trash/126/itemart_bonebeads",          "Костяные чётки",        "Bone beads",          "Все спутники (питомцы и наёмники) получают эффект \"Гнев Предков\": их физ. защита увеличена на 50 единиц, а урон - на 3 единицы"),
            new ArtifactInfo("items/trash/126/itemart_intcrystall",        "Магический кристалл",   "Magic crystal",       "+4 к Разуму"),
            new ArtifactInfo("items/trash/126/itemart_monocle",            "Монокль",               "Monocle",             "Полностью убирает влияние надетого головного убора на поле зрения"),
            new ArtifactInfo("items/trash/126/itemart_monsterblood",       "Кровь чудовища",        "Monster blood",       "+2 к Выносливости"),
            new ArtifactInfo("items/trash/126/itemart_monsterhand",        "Когти чудовища",        "Beast claws",         "+3 к наносимому урону"),
            new ArtifactInfo("items/trash/126/itemart_mpstone",            "Сияющий камень",        "Shining stone",       "+0.20 к восстановлению ОМ/с"),
            new ArtifactInfo("items/trash/126/itemart_shell1",             "Панцирь броненосца",    "Armadillo carapace",  "Физ. защита увеличена на 40 ед."),
            new ArtifactInfo("items/trash/126/itemart_shell2",             "Окаменевший панцирь",   "Petrified carapace",  "Физ. защита увеличена на 60 ед."),
            new ArtifactInfo("items/trash/126/itemart_tentacle",           "Щупальце",              "Tentacle",            "+8 к макс. ОМ"),
            new ArtifactInfo("items/trash/126/itemart_wolffur",            "Волчья шерсть",         "Wolf fur",            "+8 к макс. ОЗ"),
            new ArtifactInfo("items/trash/126/itemart_wolffur2",           "Шерсть оборотня",       "Werewolf fur",        "+14 к макс. ОЗ"),
            new ArtifactInfo("items/trash/135/itemart_coals",              "Остывшие угли",         "Extinguished embers", "Костры и факелы на 50% эффективнее восст. ОЗ. Факел в руке ускоряет восст. ОЗ на 0.5 ед./с."),
            new ArtifactInfo("items/trash/135/itemart_dmgmod1",            "Окаменевший клык",      "Petrified fang",      "+1 к наносимому урону"),
            new ArtifactInfo("items/trash/135/itemart_dmgmod2",            "Сломанный коготь",      "Broken claw",         "+2 к наносимому урону"),
            new ArtifactInfo("items/trash/135/itemart_rustynail",          "Ржавый гвоздь",         "Rusty nail",          "Весь наносимый и получаемый урон ув. на 7%"),
            new ArtifactInfo("items/trash/135/itemart_shellsmall",         "Осколок чешуи",         "Scale shard",         "Физ. защита увеличена на 20 ед."),
            new ArtifactInfo("items/trash/135/itemart_shinyscales",        "Блестящая чешуя",       "Shiny scales",        "Любая полученная атака с шансом в 8% восстановит ОЗ, а не отнимет"),
            new ArtifactInfo("items/trash/135/itemart_strongscales",       "Крепкая чешуя",         "Strong scales",       "Ловушки наносят персонажу на 20% меньше урона"),
            new ArtifactInfo("items/trash/135/itemart_tentacle2",          "Извивающееся щупальце", "Living tentacle",     "+14 к макс. ОМ"),
            new ArtifactInfo("items/trash/137/itemart_adddef_elem_1",      "Согревающий камень",    "Warming stone",       "Стих. защита персонажа увеличена на 40 ед."),
            new ArtifactInfo("items/trash/137/itemart_adddef_magic_1",     "Колдовской талисман",   "Witch's talisman",    "Магич. защита персонажа увеличена на 40 ед."),
            new ArtifactInfo("items/trash/137/itemart_attackrange",        "Моток веревки",         "Coil of rope",        "Область действия атакующих навыков ув. на 7%"),
            new ArtifactInfo("items/trash/137/itemart_autocharge",         "Засушенное ухо",        "Dried ear",           "Заряжаемые навыки срабатывают сразу, как только заполняется шкала"),
            new ArtifactInfo("items/trash/137/itemart_chargespeed",        "Перо гарпии",           "Harpy feather",       "Шкала заряда навыков накапливается на +15% быстрее"),
            new ArtifactInfo("items/trash/137/itemart_fightspeed",         "Больное перо",          "Diseased feather",    "Скорость атаки ув. на 7%"),
            new ArtifactInfo("items/trash/itemtrashaxehead",               "Порванное крыло",       "Torn wing",           "+2 к Ловкости"),
            new ArtifactInfo("items/trash/itemtrasheye",                   "Глаз",                  "Eye",                 "+2 к Разуму"),
            new ArtifactInfo("items/trash/itemtrashfeather",               "Перышко",               "Feather",             "Штраф к скорости передвижения от ношения доспехов снижен на 20%"),
            new ArtifactInfo("items/trash/itemtrashfrogleg",               "Сушеная лапка",         "Dried frog leg",      "Каждая Красная и Синяя сфера эффективнее на 1 ед."),
            new ArtifactInfo("items/trash/itemtrashfrogleg_2",             "Лягушачья лапка",       "Frog leg",            "Каждая Красная и Синяя сфера эффективнее на 2 ед."),
            new ArtifactInfo("items/trash/itemtrashmirror",                "Зеркало",               "Mirror",              "Отражает обратно 50% урона, полученного в ближнем бою"),
            new ArtifactInfo("items/trash/itemtrashrabbitpaw",             "Кроличья лапка",        "Rabbit paw",          "На 1% увеличивает шанс выпадения предметов"),
            new ArtifactInfo("items/trash/itemtrashscorpion",              "Клешня",                "Claw",                "+2 к Силе"),
            new ArtifactInfo("items/trash/itemtrashscorpion_2",            "Могучая клешня",        "Mighty claw",         "+4 к Силе"),
            new ArtifactInfo("items/trash/itemtrashsilvercoin",            "Ржавая монета",         "Rusty coin",          "Повышает стоимость каждой подобранной монеты на +1"),
            new ArtifactInfo("items/trash/itemtrashtail",                  "Рыжий хвост",           "Red tail",            "Увеличивает скорость передвижения на 10%"),
            new ArtifactInfo("items/trash/itemtrashtelescope",             "Подзорная труба",       "Spyglass",            "Увеличивает обзор"),
            new ArtifactInfo("items/trash/secret/itemartsecret_emptybowl", "Опустевшая чаша",       "Drained bowl",        "Копит кровь своего владельца. Когда чаша будет заполнена, она выплеснется и разорвет плоть ближайших врагов."),
        };
    }
}
