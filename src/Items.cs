using System;
using System.Collections.Generic;
using I2.Loc;
using UnityEngine;

namespace PocketRoguesCheats
{
    /// <summary>
    /// Вид вещей в окне выбора — «Меч», «Шлемы», «Кольца»… — и сами вещи этого вида, от слабых к
    /// сильным.
    /// </summary>
    internal sealed class ItemKind
    {
        // название: термин перевода игры (у оружия — «Меч», «Жезл») или своя пара «по-русски / по-английски»
        private readonly string _term;
        private readonly string _ru;
        private readonly string _en;

        /// <summary>Название вида — на языке игры и мода; берётся при каждом показе, язык меняется на ходу.</summary>
        public string Title { get { return _term != null ? L.Game(_term, _ru, _en) : L.T(_ru, _en); } }

        /// <summary>Чьё это: класс героя у оружия и второй руки, −1 — вещь общая.</summary>
        public readonly int Hero;

        /// <summary>Раздел окна видов (<see cref="ItemsGive"/>.Sec*).</summary>
        public readonly int Section;

        /// <summary>Куда надевается (<see cref="ItemsGive"/>.Slot*); −1 — только в сумку.</summary>
        public readonly int Fits;

        /// <summary>Порядок внутри раздела: у оружия — по подвиду, как он перечислен в игре.</summary>
        public readonly int Order;

        public readonly List<SO_Item> Items = new List<SO_Item>();

        public ItemKind(string term, string ru, string en, int hero, int section, int fits, int order)
        {
            _term = term;
            _ru = ru;
            _en = en;
            Hero = hero;
            Section = section;
            Fits = fits;
            Order = order;
        }
    }

    /// <summary>
    /// Размер сумки больше, чем дают Склады. Жёсткого предела у игры нет: места — это `invSize +
    /// inv_mod`, и окно сумки само строит столько ячеек, с прокруткой. Но `invSize` игра ставит
    /// заново в `Character.Awake` — при каждом появлении героя, поэтому держим его, как множители. Свой прежний размер каждому герою возвращаем, когда
    /// ползунок вернули «как в игре» или началась сетевая игра.
    /// </summary>
    internal static class BagSizeCheat
    {
        internal const int Max = 100;

        /// <summary>Ползунок сдвинули — применить сразу, не дожидаясь медленного такта.</summary>
        internal static bool Changed;

        // герои, которым размер поставили мы, и их собственный размер
        private static readonly Dictionary<Character, int> _own = new Dictionary<Character, int>();

        /// <summary>
        /// Сколько мест дают Склады — так же, как считает `Character.Awake`: `bld_boat` 1 → 20,
        /// 2 → 25, 3 → 30, 4–6 → 35, иначе 15.
        /// </summary>
        internal static int GameSize()
        {
            int b;
            try { b = FileBasedPrefs.GetInt("bld_boat", 0); }
            catch (Exception) { b = 0; }
            if (b == 1) return 20;
            if (b == 2) return 25;
            if (b == 3) return 30;
            if (b >= 4 && b <= 6) return 35;
            return 15;
        }

        /// <summary>list = null — вернуть всем их размер (сетевая игра).</summary>
        internal static void Keep(List<Character> list)
        {
            // герои прошлых этажей уже уничтожены — забываем
            List<Character> gone = new List<Character>();
            foreach (Character c in _own.Keys) if (c == null) gone.Add(c);
            for (int i = 0; i < gone.Count; i++) _own.Remove(gone[i]);

            int target = CheatsPlugin.BagSize.Value;
            if (list == null || target <= 0)
            {
                foreach (KeyValuePair<Character, int> kv in _own)
                    if (kv.Key != null && (int)kv.Key.invSize != kv.Value)
                    {
                        kv.Key.invSize = (InventorySize)kv.Value;
                        RebuildGrid(kv.Key);
                    }
                _own.Clear();
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Character c = list[i];
                if (c == null) continue;
                int own;
                if (!_own.TryGetValue(c, out own))
                {
                    own = (int)c.invSize;
                    _own[c] = own;
                }
                // меньше, чем даёт игра, не делаем: сумка только растёт
                int want = Mathf.Max(target, own);
                if ((int)c.invSize != want)
                {
                    c.invSize = (InventorySize)want;
                    RebuildGrid(c);
                }
            }
        }

        /// <summary>
        /// Перестроить сетку окна сумки под новое число мест. Лишние ячейки окно само не убирает:
        /// `DisplayInventory` только достраивает недостающие, а при уменьшении старые остаются и
        /// закрываются. `UpdateInventorySlots` сносит все и строит заново.
        /// </summary>
        private static void RebuildGrid(Character c)
        {
            try
            {
                GameManager gm = GameManager.Instance;
                if (gm == null || gm.Inventory == null) return;
                gm.Inventory.SetCharacter(c);
                gm.Inventory.UpdateInventorySlots();
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogWarning("The bag grid was not rebuilt: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Выдача и замена вещей. Всё делается функциями самой игры — теми же, что при подборе добычи,
    /// кнопках «надеть» и «снять» в окне сумки и выдаче вещей разработчиком (ItemsGrantApplier).
    /// Поэтому новая вещь получает номер-отпечаток, эффекты и качество так же,
    /// как выпавшая, а снятая уходит в сумку или падает рядом с героем по правилам игры.
    /// </summary>
    internal static class ItemsGive
    {
        // слоты героя в том порядке, как их показывает вкладка
        internal const int SlotHead = 0, SlotBody = 1, SlotWeapon = 2, SlotOff = 3,
                           SlotRing1 = 4, SlotRing2 = 5, SlotArt1 = 6, SlotArt2 = 7, SlotArt3 = 8;

        internal const int SlotCount = 9;

        private static readonly string[] SlotRu = new string[]
        {
            "Шлем", "Доспех", "Оружие", "Вторая рука", "Кольцо 1", "Кольцо 2",
            "Артефакт 1", "Артефакт 2", "Артефакт 3"
        };

        private static readonly string[] SlotEn = new string[]
        {
            "Helmet", "Armor", "Weapon", "Off-hand", "Ring 1", "Ring 2",
            "Artifact 1", "Artifact 2", "Artifact 3"
        };

        internal static string SlotTitle(int slot) { return L.T(SlotRu[slot], SlotEn[slot]); }

        /// <summary>
        /// Номер слота для кнопки «снять» в окне сумки игры (`InventoryManager.selectedIndex` меньше
        /// нуля): так его понимает `EquipThisItemButton`.
        /// </summary>
        private static readonly int[] UnequipCode = new int[] { -2, -5, -6, -4, -1, -3, -7, -8, -9 };

        // разделы окна видов, в этом порядке
        internal const int SecMelee = 0, SecRanged = 1, SecMagic = 2, SecOff = 3, SecArmor = 4,
                           SecOther = 5;

        private static readonly string[] SectionRu = new string[]
        {
            "Ближний бой", "Дальний бой", "Магия", "Вторая рука", "Броня", "Прочее"
        };

        private static readonly string[] SectionEn = new string[]
        {
            "Melee", "Ranged", "Magic", "Off-hand", "Armor", "Other"
        };

        internal static string SectionTitle(int section) { return L.T(SectionRu[section], SectionEn[section]); }

        /// <summary>Вторая рука каждого класса — по-своему; у берсерка там второе оружие.</summary>
        private static readonly string[] OffhandRu = new string[] { "Щиты", "Стрелы", "Книги", "Болты", "", "Гримуары" };
        private static readonly string[] OffhandEn = new string[] { "Shields", "Arrows", "Books", "Bolts", "", "Grimoires" };

        // --- слоты героя --------------------------------------------------------------------

        /// <summary>Что надето в слоте; null — пусто (пустой слот игра держит как вещь без имени).</summary>
        internal static SO_Item SlotItem(Character c, int slot)
        {
            if (c == null) return null;
            SO_Item item = null;
            switch (slot)
            {
                case SlotHead: item = c.Head; break;
                case SlotBody: item = c.Body; break;
                case SlotWeapon: item = c.Weapon; break;
                case SlotOff:
                    item = c.Shield;
                    if (item == null || string.IsNullOrEmpty(item.Name)) item = c.ShieldWeapon;
                    break;
                case SlotRing1: item = c.Ring1; break;
                case SlotRing2: item = c.Ring2; break;
                case SlotArt1: item = c.Art1; break;
                case SlotArt2: item = c.Art2; break;
                case SlotArt3: item = c.Art3; break;
            }
            if (item == null || string.IsNullOrEmpty(item.Name)) return null;
            return item;
        }

        /// <summary>
        /// Закрыт ли слот и чем открывается; пустая строка — открыт. Кольца открывает Ювелирная
        /// мастерская, второй и третий артефакт — Мастерская артефактов (`Character.AddItem`:
        /// `bld_jeweler` ≥ 1 и ≥ 2, `bld_arts` ≥ 1 и ≥ 2).
        /// </summary>
        internal static string SlotLock(int slot)
        {
            if (slot == SlotRing1 && Building("bld_jeweler") < 1) return BuildingTitle("Jeweler") + L.T(", уровень 1", ", level 1");
            if (slot == SlotRing2 && Building("bld_jeweler") < 2) return BuildingTitle("Jeweler") + L.T(", уровень 2", ", level 2");
            if (slot == SlotArt2 && Building("bld_arts") < 1) return BuildingTitle("Artifacts") + L.T(", уровень 1", ", level 1");
            if (slot == SlotArt3 && Building("bld_arts") < 2) return BuildingTitle("Artifacts") + L.T(", уровень 2", ", level 2");
            return "";
        }

        private static int Building(string key)
        {
            try { return FileBasedPrefs.GetInt(key, 0); }
            catch (Exception) { return 0; }
        }

        private static string BuildingTitle(string term)
        {
            return term == "Jeweler"
                ? L.Game("Fortress/Buildings/Jeweler", "Ювелирная мастерская", "Jewelry workshop")
                : L.Game("Fortress/Buildings/Artifacts", "Мастерская артефактов", "Artifacts workshop");
        }

        /// <summary>Сколько в сумке мест: размер от Складов плюс прибавка от вещей и эффектов.</summary>
        internal static int Capacity(Character c)
        {
            if (c == null) return 0;
            return (int)c.invSize + c.inv_mod;
        }

        internal static int Used(Character c)
        {
            if (c == null || c.inventory == null) return 0;
            return c.inventory.Count;
        }

        // --- справочник вещей ---------------------------------------------------------------

        private static List<ItemKind> _kinds;

        /// <summary>
        /// Все виды вещей. Вещи берутся у самой игры, из её папки Resources/Items — те же образцы,
        /// по которым она создаёт добычу и загружает сохранения; названия и описания у них на языке
        /// игры. Собирается один раз, при первом открытии списка.
        /// </summary>
        internal static List<ItemKind> Kinds()
        {
            if (_kinds != null) return _kinds;
            List<ItemKind> kinds = new List<ItemKind>();
            Dictionary<string, ItemKind> byKey = new Dictionary<string, ItemKind>();
            SO_Item[] all;
            try { all = Resources.LoadAll<SO_Item>("Items"); }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogError("The item list did not load: " + ex.Message);
                all = new SO_Item[0];
            }

            for (int i = 0; i < all.Length; i++)
            {
                SO_Item it = all[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (!SavesAsItself(it)) continue;

                SO_ItemWeapon w = it as SO_ItemWeapon;
                if (w != null)
                {
                    int hero = (int)w.characterClass;
                    int sub = (int)w.weaponTypeAdditional;
                    Put(kinds, byKey, "w" + hero + ":" + sub, "Items/Types/weapon_" + w.weaponTypeAdditional,
                        WeaponRu[sub], WeaponEn[sub], hero, WeaponSection(w.weaponTypeAdditional), SlotWeapon, sub, it);
                    continue;
                }
                // SO_ItemShield_Alt («Ручной арбалет») — особая вещь, в обычный слот не идёт
                if (it.GetType() == typeof(SO_ItemShield))
                {
                    SO_ItemShield s = (SO_ItemShield)it;
                    List<int> classes = new List<int>();
                    if (s.characterClasses != null)
                        foreach (CharacterClasses cc in s.characterClasses)
                            if (!classes.Contains((int)cc)) classes.Add((int)cc);
                    if (classes.Count == 0) classes.Add((int)s.characterClass);
                    for (int k = 0; k < classes.Count; k++)
                    {
                        int hero = classes[k];
                        bool named = hero >= 0 && hero < OffhandRu.Length && OffhandRu[hero].Length > 0;
                        Put(kinds, byKey, "o" + hero, null, named ? OffhandRu[hero] : "Вторая рука",
                            named ? OffhandEn[hero] : "Off-hand", hero, SecOff, SlotOff, 0, it);
                    }
                    continue;
                }
                if (it is SO_ItemHead) { Put(kinds, byKey, "head", null, "Шлемы", "Helmets", -1, SecArmor, SlotHead, 0, it); continue; }
                if (it is SO_ItemBody) { Put(kinds, byKey, "body", null, "Доспехи", "Body armor", -1, SecArmor, SlotBody, 1, it); continue; }
                if (it is SO_ItemRing) { Put(kinds, byKey, "ring", null, "Кольца", "Rings", -1, SecOther, SlotRing1, 0, it); continue; }
                SO_ItemTrash tr = it as SO_ItemTrash;
                if (tr != null && tr.Artifact)
                {
                    Put(kinds, byKey, "art", null, "Артефакты", "Artifacts", -1, SecOther, SlotArt1, 1, it);
                    continue;
                }
                SO_ItemUsable u = it as SO_ItemUsable;
                if (u != null && !u.passive)
                {
                    Put(kinds, byKey, "use", null, "Расходники", "Consumables", -1, SecOther, -1, 2, it);
                    continue;
                }
                // остальное — сокровища на продажу, самоцветы кузницы, пассивные благословения,
                // ключи — снаряжением не считаем
            }

            StrengthOrder cmp = new StrengthOrder();
            for (int i = 0; i < kinds.Count; i++) kinds[i].Items.Sort(cmp);
            kinds.Sort(new KindOrder());
            CheatsPlugin.Log.LogInfo("Item list: " + all.Length + " templates, " + kinds.Count + " kinds.");
            _kinds = kinds;
            return _kinds;
        }

        /// <summary>
        /// Сохранение держит вещь как её саму. У части вещей второго и третьего уровня в образце
        /// записан путь чужой вещи: «Гладиус» и другие новые мечи сохраняются как «Душегуб», новые
        /// щиты — путём вещи, которой в игре нет. Игра пишет в сохранение ровно этот путь и по нему
        /// грузит (DungeonSaving.GetInventoryString, ResourcesLoad_Item), так что после перезагрузки
        /// такая вещь становится другой или пропадает. Выдавать их незачем (21.09.2026).
        /// </summary>
        private static bool SavesAsItself(SO_Item it)
        {
            if (string.IsNullOrEmpty(it.savingObjectPath)) return false;
            try { return Resources.Load<SO_Item>(it.savingObjectPath) == it; }
            catch (Exception) { return false; }
        }

        private static void Put(List<ItemKind> kinds, Dictionary<string, ItemKind> byKey, string key,
                                string term, string ru, string en, int hero, int section, int fits, int order,
                                SO_Item item)
        {
            ItemKind k;
            if (!byKey.TryGetValue(key, out k))
            {
                k = new ItemKind(term, ru, en, hero, section, fits, order);
                byKey[key] = k;
                kinds.Add(k);
            }
            k.Items.Add(item);
        }

        private static int WeaponSection(WeaponTypesAdditional t)
        {
            switch (t)
            {
                case WeaponTypesAdditional.bow:
                case WeaponTypesAdditional.crossbow:
                    return SecRanged;
                case WeaponTypesAdditional.staff:
                case WeaponTypesAdditional.wand:
                    return SecMagic;
                default:
                    return SecMelee;
            }
        }

        // подвиды оружия по номеру WeaponTypesAdditional (меч … жезл); названия — термины перевода игры
        // Items/Types/weapon_…, свои пары — только на случай, если термина нет
        private static readonly string[] WeaponRu = new string[]
        {
            "Меч", "Кинжал", "Топор", "Молот", "Копьё", "Лук", "Арбалет", "Посох", "Жезл"
        };

        private static readonly string[] WeaponEn = new string[]
        {
            "Sword", "Dagger", "Axe", "Hammer", "Spear", "Bow", "Crossbow", "Staff", "Wand"
        };

        /// <summary>Разделы по порядку, внутри — подвиды оружия как в игре, потом по названию.</summary>
        private sealed class KindOrder : IComparer<ItemKind>
        {
            public int Compare(ItemKind a, ItemKind b)
            {
                if (a.Section != b.Section) return a.Section.CompareTo(b.Section);
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
            }
        }

        /// <summary>
        /// От слабых к сильным — мерой самой игры (сортировка сумки «по эффективности»,
        /// `InventoryManager.OrderOnEfficiency`): броня у шлема, доспеха и щита, средний урон у
        /// оружия, у остального — цена.
        /// </summary>
        private sealed class StrengthOrder : IComparer<SO_Item>
        {
            public int Compare(SO_Item a, SO_Item b)
            {
                int c = Strength(a).CompareTo(Strength(b));
                if (c != 0) return c;
                c = a.PriceNormal.CompareTo(b.PriceNormal);
                if (c != 0) return c;
                return string.Compare(Title(a), Title(b), StringComparison.CurrentCulture);
            }
        }

        internal static float Strength(SO_Item it)
        {
            SO_ItemWeapon w = it as SO_ItemWeapon;
            if (w != null) return (w._damageMin + w._damageMax) / 2f;
            SO_ItemHead h = it as SO_ItemHead;
            if (h != null) return h.Armor;
            SO_ItemBody b = it as SO_ItemBody;
            if (b != null) return b.Armor;
            SO_ItemShield s = it as SO_ItemShield;
            if (s != null && (s.Armor > 0f || s.DamageDefence > 0f)) return s.Armor + s.DamageDefence / 1000f;
            return it.PriceNormal;
        }

        /// <summary>Короткая строка силы рядом с названием: «урон 12–18», «броня 7».</summary>
        internal static string StatText(SO_Item it)
        {
            SO_ItemWeapon w = it as SO_ItemWeapon;
            if (w != null)
                return L.T("урон ", "damage ") + Mathf.RoundToInt(w._damageMin) + "–" + Mathf.RoundToInt(w._damageMax);
            SO_ItemHead h = it as SO_ItemHead;
            if (h != null && h.Armor > 0) return L.T("броня ", "armor ") + h.Armor;
            SO_ItemBody b = it as SO_ItemBody;
            if (b != null && b.Armor > 0) return L.T("броня ", "armor ") + b.Armor;
            SO_ItemShield s = it as SO_ItemShield;
            if (s != null && s.Armor > 0f) return L.T("броня ", "armor ") + Mathf.RoundToInt(s.Armor);
            return "";
        }

        internal static string Title(SO_Item it)
        {
            if (it == null) return "";
            try
            {
                string t = it.InGameTitle;
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch (Exception) { }
            return it.Name;
        }

        /// <summary>Описание вещи словами игры — для строки-подсказки внизу окна.</summary>
        internal static string Description(SO_Item it)
        {
            if (it == null) return "";
            try
            {
                string d = it.descriptionLocalized;
                return string.IsNullOrEmpty(d) ? "" : d;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Вид, к которому относится вещь — чтобы замена в сумке открывала её же вид.</summary>
        internal static ItemKind KindOf(SO_Item item)
        {
            if (item == null) return null;
            List<ItemKind> kinds = Kinds();
            for (int i = 0; i < kinds.Count; i++)
                for (int j = 0; j < kinds[i].Items.Count; j++)
                    if (kinds[i].Items[j].Name == item.Name) return kinds[i];
            return null;
        }

        /// <summary>Подходит ли вид для слота. У берсерка во второй руке — второе оружие.</summary>
        internal static bool Fits(ItemKind k, int slot, Character hero)
        {
            if (k == null) return false;
            if (slot == SlotRing1 || slot == SlotRing2) return k.Fits == SlotRing1;
            if (slot == SlotArt1 || slot == SlotArt2 || slot == SlotArt3) return k.Fits == SlotArt1;
            if (slot == SlotOff && hero != null && hero.charClass == CharacterClasses.BERSERKER)
                return k.Fits == SlotWeapon;
            return k.Fits == slot;
        }

        // --- выдача и замена ----------------------------------------------------------------

        /// <summary>
        /// Новая вещь по образцу — как игра готовит гарантированную добычу сундука
        /// (`PR_ContainerGuaranteedLootUtility.ApplyEntryToRuntimeItem`): копия образца, сброс,
        /// базовые числа из образца и смена качества на легендарное. Эффекты при этом накидывает
        /// сама игра, как выпавшей вещи. Вещи, которые не бывают редкими (уникальные), остаются
        /// какими их задумала игра.
        /// </summary>
        private static SO_Item MakeItem(SO_Item template)
        {
            SO_Item copy = ItemDrop.ItemCopy(template);
            SO_ItemEquip eq = copy as SO_ItemEquip;
            if (eq != null)
            {
                if (eq.canBeRare)
                {
                    eq.useRandomQuality = false;
                    eq.ResetInitialization();
                    eq.RestoreBaseCombatStatsFromResourcesTemplate();
                    eq.ChangeQuality(Qualities.LEGENDARY5, true, true);
                }
                else if (!eq.isInitialized)
                {
                    eq.InitializeEffects();
                }
            }
            return copy;
        }

        /// <summary>
        /// Положить вещь в сумку — `Character.AddItem`, как при подборе с пола: игра сама проверит
        /// уникальность, сложит зелья стопкой, а если места нет — уронит вещь рядом с героем.
        /// Возвращает, что вышло, словами для окна.
        /// </summary>
        internal static string Add(Character hero, SO_Item template)
        {
            if (hero == null || template == null) return NoHero();
            if (!hero.CheckUniqueItem(template))
                return Title(template) + L.T(" — вещь уникальная, и у героя она уже есть.", " is unique, and the hero already has it.");
            try
            {
                int before = BagWeight(hero);
                bool worn = IsWorn(hero, template.Name);
                SO_Item item = MakeItem(template);
                hero.AddItem(item, null, true, false);
                string name = Title(item) + Quality(item);
                if (BagWeight(hero) > before) return L.T("В сумке: ", "In the bag: ") + name + ".";
                if (!worn && IsWorn(hero, template.Name))
                    return L.T("Надето сразу (так настроена игра — «надевать подобранное»): ",
                               "Equipped right away (the game's auto-equip setting is on): ") + name + ".";
                return L.T("Сумка полна — ", "The bag is full — ") + name
                     + L.T(" лежит на полу рядом с героем.", " is on the floor next to the hero.");
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogError("Giving item " + template.Name + ": " + ex);
                return L.T("Не вышло выдать ", "Could not give ") + Title(template)
                     + L.T(" — подробности в LogOutput.log.", " — see LogOutput.log for details.");
            }
        }

        /// <summary>
        /// Надеть в слот новую вещь. Старая снимается кнопкой «снять» окна сумки игры: уходит в
        /// сумку, а нет места — падает рядом с героем. Новая надевается тем же, чем окно сумки
        /// надевает выбранную вещь (`InventoryManager.EquipItem`); артефакт — прямо в свой слот,
        /// как это делает сама игра (`Character.EquipArtifact`).
        /// </summary>
        internal static string Replace(Character hero, int slot, SO_Item template)
        {
            if (hero == null || template == null) return NoHero();
            string locked = SlotLock(slot);
            if (locked.Length > 0) return L.T("Слот закрыт — его открывает ", "The slot is locked — it is opened by ") + locked + ".";
            if (!hero.CheckUniqueItem(template))
                return Title(template) + L.T(" — вещь уникальная, и у героя она уже есть.", " is unique, and the hero already has it.");
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Inventory == null) return NotReady();
            InventoryManager inv = gm.Inventory;
            SO_Item old = SlotItem(hero, slot);
            string oldName = old != null ? Title(old) : "";
            try
            {
                inv.SetCharacter(hero);
                bool hadRoom = hero.IsInventoryHasPlace();
                if (old != null)
                {
                    inv.selectedIndex = UnequipCode[slot];
                    inv.EquipThisItemButton();
                }
                SO_Item item = MakeItem(template);
                SO_ItemTrash art = item as SO_ItemTrash;
                if (art != null)
                {
                    if (slot == SlotArt1) hero.Art1 = art;
                    else if (slot == SlotArt2) hero.Art2 = art;
                    else hero.Art3 = art;
                    hero.EquipArtifact(art);
                    ArtifactCheats.Changed = true;
                    RefreshBuffs(hero);
                }
                else
                {
                    SO_ItemEquip eq = item as SO_ItemEquip;
                    if (eq == null) return Title(template) + L.T(" не надевается.", " can't be equipped.");
                    inv.EquipItem(eq);
                }

                SO_Item now = SlotItem(hero, slot);
                bool ok = now != null && now.Name == template.Name;
                if (!ok && !IsWorn(hero, template.Name))
                    return L.T("Игра не надела ", "The game did not equip ") + Title(template)
                         + L.T(" — вещь, скорее всего, в сумке.", " — the item is most likely in the bag.");
                string res = L.T("Надето: ", "Equipped: ") + Title(template) + Quality(now != null ? now : item) + ".";
                if (oldName.Length > 0)
                    res += hadRoom ? " " + oldName + L.T(" — в сумке.", " is in the bag.")
                                   : L.T(" Сумка была полна — ", " The bag was full — ") + oldName
                                     + L.T(" лежит на полу рядом.", " is on the floor nearby.");
                return res;
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogError("Replacing in slot " + SlotEn[slot] + ": " + ex);
                return CouldNotReplace();
            }
        }

        /// <summary>
        /// Заменить вещь в сумке: старую игра выбрасывает рядом с героем (кнопка «выбросить»,
        /// `InventoryManager.Drop`), новая ложится в сумку.
        /// </summary>
        internal static string ReplaceInBag(Character hero, SO_Item old, SO_Item template)
        {
            if (hero == null || template == null) return NoHero();
            if (old == null || hero.inventory == null || !hero.inventory.Contains(old))
                return L.T("Этой вещи в сумке уже нет.", "This item is no longer in the bag.");
            if (old.isLocked)
                return Title(old) + L.T(" закреплена замком в сумке игры — сними замок, тогда заменю.",
                                        " is locked in the game's bag — unlock it and I'll replace it.");
            if (!hero.CheckUniqueItem(template))
                return Title(template) + L.T(" — вещь уникальная, и у героя она уже есть.", " is unique, and the hero already has it.");
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Inventory == null) return NotReady();
            string oldName = Title(old);
            try
            {
                gm.Inventory.SetCharacter(hero);
                gm.Inventory.Drop(old, 1f);
                if (hero.inventory.Contains(old))
                    return L.T("Игра не выбросила ", "The game did not drop ") + oldName
                         + L.T(" — замена отменена.", " — replacement cancelled.");
                return Add(hero, template) + " " + oldName + L.T(" — на полу рядом с героем.", " is on the floor next to the hero.");
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogError("Replacing in the bag: " + ex);
                return CouldNotReplace();
            }
        }

        /// <summary>
        /// Удалить вещь из сумки совсем: в игре лишнее приходится
        /// выбрасывать по одной вещи, с подтверждением. Убираем так же, как игра, когда вещь
        /// выбрасывают (`InventoryManager.Drop`), только без вещи на полу: из сумки, из быстрых
        /// ячеек (зелье стоит и там, тем же предметом) и из списка надетого. Вещь под замком игры
        /// и пассивные вещи (их действие снимает только сама игра) не трогаем. Стопка — целиком.
        /// </summary>
        internal static string Delete(Character hero, SO_Item item)
        {
            if (hero == null || item == null || hero.inventory == null || !hero.inventory.Contains(item))
                return L.T("Этой вещи в сумке уже нет.", "This item is no longer in the bag.");
            if (item.isLocked)
                return Title(item) + L.T(" закреплена замком в сумке игры — сними замок, тогда удалю.",
                                         " is locked in the game's bag — unlock it and I'll delete it.");
            SO_ItemUsable u = item as SO_ItemUsable;
            if (u != null && u.passive)
                return Title(item) + L.T(" — пассивная вещь, её действие снимает только сама игра: выброси её в игре.",
                                         " is a passive item: only the game removes its effect — drop it in the game.");
            string count = u != null && u.count > 1 ? " ×" + u.count : "";
            hero.inventory.Remove(item);
            if (u != null && hero.usables != null)
            {
                bool slot = false;
                for (int i = 0; i < hero.usables.Length; i++)
                    if (hero.usables[i] == u)
                    {
                        hero.usables[i] = null;
                        slot = true;
                    }
                if (slot) RefreshQuickSlots(hero);
            }
            SO_ItemEquip eq = item as SO_ItemEquip;
            if (eq != null && hero.equiped != null) hero.equiped.Remove(eq);
            return L.T("Удалено: ", "Deleted: ") + Title(item) + Quality(item) + count + ".";
        }

        /// <summary>Вернуть удалённую вещь в сумку — ту же самую, с её номером и эффектами.</summary>
        internal static bool Restore(Character hero, SO_Item item)
        {
            if (hero == null || item == null || hero.inventory == null) return false;
            if (hero.inventory.Contains(item)) return true;
            if (!hero.IsInventoryHasPlace()) return false;
            if (!hero.CheckUniqueItem(item)) return true;   // такую же уникальную уже добыли — вторую не кладём
            hero.inventory.Add(item);
            return true;
        }

        /// <summary>Быстрые ячейки зелий на экране — после того, как из них убрали вещь.</summary>
        private static void RefreshQuickSlots(Character hero)
        {
            try
            {
                if (PlayerInputManager.Instance != null) PlayerInputManager.Instance.UpdateSlots(hero);
            }
            catch (Exception) { }
        }

        private static string NoHero() { return L.T("Героя в игре сейчас нет.", "There is no hero in the game right now."); }

        private static string NotReady()
        {
            return L.T("Окно сумки игры ещё не готово — попробуй через секунду.",
                       "The game's bag window isn't ready yet — try again in a second.");
        }

        private static string CouldNotReplace()
        {
            return L.T("Не вышло заменить — подробности в LogOutput.log.", "Could not replace — see LogOutput.log for details.");
        }

        /// <summary>Сколько «весит» сумка: вещи плюс зелья в стопках — чтобы понять, легла ли вещь.</summary>
        private static int BagWeight(Character hero)
        {
            if (hero.inventory == null) return 0;
            int n = 0;
            foreach (SO_Item it in hero.inventory)
            {
                SO_ItemUsable u = it as SO_ItemUsable;
                n += u != null ? Mathf.Max(1, u.count) : 1;
            }
            return n;
        }

        private static bool IsWorn(Character hero, string name)
        {
            for (int s = 0; s < SlotCount; s++)
            {
                SO_Item it = SlotItem(hero, s);
                if (it != null && it.Name == name) return true;
            }
            return false;
        }

        internal static string Quality(SO_Item it)
        {
            SO_ItemEquip eq = it as SO_ItemEquip;
            if (eq == null) return "";
            return " (" + GearEdit.QualityName((int)eq.Quality) + ")";
        }

        /// <summary>Строка свойств героя на экране — после артефакта, как делает игра.</summary>
        private static void RefreshBuffs(Character hero)
        {
            try
            {
                PlayerController pc = hero.ctrl as PlayerController;
                if (pc != null && PlayerInputManager.Instance != null)
                    PlayerInputManager.Instance.UpdateBuffsList(pc.playerID);
            }
            catch (Exception) { }
        }
    }
}
