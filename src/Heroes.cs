namespace PocketRoguesCheats
{
    /// <summary>
    /// Герой: номер в перечислении игры (CharacterClasses: 0 воин, 1 лучник, 2 маг, 3 охотник,
    /// 4 берсерк, 5 некромант), начало ключей его записей в хранилище и название.
    /// </summary>
    internal sealed class HeroInfo
    {
        public readonly int Index;
        public readonly string ClassKey;
        private readonly string _term;
        private readonly string _ru;
        private readonly string _en;

        public HeroInfo(int index, string classKey, string term, string ru, string en)
        {
            Index = index;
            ClassKey = classKey;
            _term = term;
            _ru = ru;
            _en = en;
        }

        /// <summary>Название — из перевода игры, на её языке; своё — только если термина нет.</summary>
        public string Title { get { return L.Game(_term, _ru, _en); } }
    }

    /// <summary>
    /// Что мод знает о героях: ключи хранилища игры и пределы. Названия героев и характеристик
    /// берутся у самой игры (термины её перевода, Characters/Classes/…, Characters/Stats/…).
    /// </summary>
    internal static class HeroesTable
    {
        /// <summary>Предел характеристики: выше не пускает экран прокачки самой игры.</summary>
        internal const int AttributeMax = 50;

        /// <summary>Разумный потолок против опечатки лишним нулём.</summary>
        internal const int PointsMax = 999999;
        internal const int GoldMax = 999999999;

        internal const string GoldKey = "money";

        /// <summary>«curPointsN» — в этой игре это же число и есть уровень героя (Character.Awake кладёт его в lvl).</summary>
        internal const string PointsKey = "curPoints";

        /// <summary>Ключ характеристики — «КЛАСС_endurance» и подобные; «intelegence» — с опечаткой самой игры.</summary>
        internal static readonly string[] AttributeKeys = new string[]
        {
            "_endurance", "_strength", "_agility", "_intelegence"
        };

        private static readonly string[] AttributeTerms = new string[]
        {
            "Characters/Stats/Stat_End", "Characters/Stats/Stat_Str", "Characters/Stats/Stat_Agl", "Characters/Stats/Stat_Int"
        };

        private static readonly string[] AttributeRu = new string[] { "Выносливость", "Сила", "Ловкость", "Разум" };
        private static readonly string[] AttributeEn = new string[] { "Endurance", "Strength", "Agility", "Intelligence" };

        internal static string AttributeTitle(int a)
        {
            return L.Game(AttributeTerms[a], AttributeRu[a], AttributeEn[a]);
        }

        internal static readonly HeroInfo[] All = new HeroInfo[]
        {
            new HeroInfo(0, "WARRIOR",   "Characters/Classes/Warrior",     "Воин",      "Warrior"),
            new HeroInfo(1, "ARCHER",    "Characters/Classes/Archer",      "Лучник",    "Archer"),
            new HeroInfo(2, "WIZARD",    "Characters/Classes/Wizard",      "Маг",       "Wizard"),
            new HeroInfo(3, "HUNTER",    "Characters/Classes/Hunter",      "Охотник",   "Hunter"),
            new HeroInfo(4, "BERSERKER", "Characters/Classes/Berserker",   "Берсерк",   "Berserker"),
            new HeroInfo(5, "NECROM",    "Characters/Classes/Necromancer", "Некромант", "Necromancer"),
        };
    }
}
