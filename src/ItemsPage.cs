using System;
using System.Collections.Generic;
using UnityEngine;

namespace PocketRoguesCheats
{
    /// <summary>
    /// Вкладка «Предметы»: что надето (9 слотов), что в сумке, выдача и замена вещей. Выбор вещи —
    /// двумя окнами справа от основного:
    /// сперва вид вещи, потом сами вещи этого вида от слабых к сильным.
    /// </summary>
    internal sealed partial class CheatsRunner
    {
        private const int PickNone = 0, PickAdd = 1, PickSlot = 2, PickBag = 3;

        /// <summary>Что выбираем в боковых окнах: ничего, новую вещь, замену в слот, замену в сумке.</summary>
        private int _pick;
        private int _pickSlot = -1;
        private SO_Item _pickBagItem;
        private ItemKind _pickKind;

        /// <summary>Раскрыт ли список «Оружие других классов» — по умолчанию свёрнут: не убран, но и не мешает.</summary>
        private bool _pickOthers;

        private Vector2 _kindsScroll;
        private Vector2 _listScroll;

        /// <summary>Итог последней выдачи или замены — висит вверху вкладки до следующей.</summary>
        private string _itemsMsg = "";

        /// <summary>Подсказка из боковых окон: они рисуются отдельно от основного, см. DrawFooter.</summary>
        private string _hoveredSide = "";

        /// <summary>Удалённое за то время, пока окно открыто, — для «Вернуть удалённое».</summary>
        private readonly List<SO_Item> _deleted = new List<SO_Item>();

        private GUIStyle _row;
        private GUIStyle _msg;

        private const float KindsWidth = 250f;
        private const float ListWidth = 350f;
        private const int KindsWindowId = 0x5053;
        private const int ListWindowId = 0x5054;

        private void MakeItemStyles()
        {
            if (_row != null) return;
            // строка-кнопка: выбранная остаётся вдавленной, текст слева, как в списке
            _row = new GUIStyle(GUI.skin.button);
            _row.fontSize = 14;
            _row.alignment = TextAnchor.MiddleLeft;
            _row.padding = new RectOffset(8, 6, 4, 4);
            _row.onNormal = _row.active;
            _row.onHover = _row.active;
            _msg = new GUIStyle(GUI.skin.box);
            _msg.fontSize = 13;
            _msg.wordWrap = true;
            _msg.alignment = TextAnchor.UpperLeft;
            _msg.padding = new RectOffset(6, 6, 4, 4);
            _msg.normal.textColor = new Color(1f, 0.92f, 0.6f);
        }

        private void ForgetDeleted()
        {
            _deleted.Clear();
        }

        private void ClosePick()
        {
            _pick = PickNone;
            _pickSlot = -1;
            _pickBagItem = null;
            _pickKind = null;
        }

        private void OpenPick(int mode, int slot, SO_Item bagItem, Character hero)
        {
            _pick = mode;
            _pickSlot = slot;
            _pickBagItem = bagItem;
            _pickKind = null;
            _kindsScroll = Vector2.zero;
            _listScroll = Vector2.zero;
            // сразу открываем вид, который напрашивается: у слота — если он один подходящий, у вещи
            // в сумке — её собственный
            if (mode == PickBag) _pickKind = ItemsGive.KindOf(bagItem);
            if (mode == PickSlot)
            {
                ItemKind only = null;
                int n = 0;
                List<ItemKind> kinds = ItemsGive.Kinds();
                for (int i = 0; i < kinds.Count; i++)
                {
                    ItemKind k = kinds[i];
                    if (!ItemsGive.Fits(k, slot, hero)) continue;
                    if (k.Hero >= 0 && k.Hero != (int)hero.charClass) continue;
                    only = k;
                    n++;
                }
                if (n == 1) _pickKind = only;
            }
        }

        // --- основная вкладка ----------------------------------------------------------------

        private void DrawPageItems()
        {
            MakeItemStyles();
            if (Players.Online())
            {
                GUILayout.Label(L.T("Сетевая игра — выдача вещей выключена: там чужая игра.",
                                    "Online game — giving items is off: it's someone else's game too."), _muted);
                ClosePick();
                return;
            }
            Character hero = GearEdit.Hero();
            if (hero == null)
            {
                GUILayout.Label(NoHeroText(), _muted);
                ClosePick();
                return;
            }

            if (_itemsMsg.Length > 0) GUILayout.Label(_itemsMsg, _msg);

            GUILayout.Label(L.T("Надето", "Equipped"), _head);
            for (int s = 0; s < ItemsGive.SlotCount; s++)
            {
                SO_Item it = ItemsGive.SlotItem(hero, s);
                string locked = ItemsGive.SlotLock(s);
                string what = locked.Length > 0 ? L.T("закрыт", "locked") : it == null ? L.T("пусто", "empty") : ItemsGive.Title(it);
                bool on = _pick == PickSlot && _pickSlot == s;

                GUILayout.BeginHorizontal();
                GUI.enabled = locked.Length == 0;
                bool click = GUILayout.Toggle(on, ItemsGive.SlotTitle(s) + ":  " + what, _row,
                                              GUILayout.Width(320));
                GUI.enabled = true;
                string q = it == null ? "" : ItemsGive.Quality(it).Trim().Trim('(', ')');
                GUILayout.Label(q, _value, GUILayout.Width(110));
                GUILayout.EndHorizontal();

                if (locked.Length > 0)
                    Hover(ItemsGive.SlotTitle(s) + L.T(" — слот закрыт, его открывает ", " — the slot is locked; it is opened by ")
                          + locked + ".");
                else if (it == null)
                    Hover(ItemsGive.SlotTitle(s) + L.T(" — пусто. Нажми, чтобы выбрать вещь: справа откроется список.",
                                                       " — empty. Click to pick an item: a list opens on the right."));
                else
                    Hover(ItemDetails(it) + L.T("  Нажми, чтобы заменить: старая уйдёт в сумку, а нет места —"
                                                + " упадёт рядом с героем.",
                                                "  Click to replace: the old one goes to the bag, or to the floor"
                                                + " next to the hero if there is no room."));

                if (click && !on) OpenPick(PickSlot, s, null, hero);
                else if (!click && on) ClosePick();
            }

            int used = ItemsGive.Used(hero);
            int cap = ItemsGive.Capacity(hero);
            GUILayout.Label(L.T("Сумка — ", "Bag — ") + used + L.T(" из ", " of ") + cap, _head);
            Hover(L.T("Занято и всего мест. Размер сумки дают Склады в крепости (от 15 до 35) и прибавки"
                      + " от вещей; ползунок ниже делает её больше.",
                      "Used and total slots. The bag size comes from the Warehouse in the fortress (15 to 35)"
                      + " plus bonuses from items; the slider below makes it bigger."));
            DrawBagSize();
            if (used > cap)
                GUILayout.Label(L.T("Вещей больше, чем мест: лишние окно сумки игры не показывает. Они не"
                                    + " пропали — верни размер сумки, и они появятся.",
                                    "More items than slots: the game's bag window doesn't show the extra ones."
                                    + " They are not lost — restore the bag size and they will reappear."), _muted);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Добавить предмет", "Add an item"), _text, GUILayout.Width(150));
            bool adding = _pick == PickAdd;
            bool add = GUILayout.Toggle(adding, "+", _tab, GUILayout.Width(44), GUILayout.Height(24));
            GUILayout.EndHorizontal();
            Hover(L.T("Выдать любую вещь игры: справа откроется список видов, потом сами вещи. Снаряжение"
                      + " выдаётся легендарным, эффекты накидывает сама игра, как выпавшей вещи.",
                      "Give any item in the game: a list of kinds opens on the right, then the items themselves."
                      + " Gear is given as legendary; the game rolls its effects itself, as for a dropped item."));
            if (add && !adding) OpenPick(PickAdd, -1, null, hero);
            else if (!add && adding) ClosePick();

            if (_deleted.Count > 0)
            {
                if (GUILayout.Button(L.T("Вернуть удалённое (", "Restore deleted (") + _deleted.Count + ")", _button))
                    RestoreDeleted(hero);
                Hover(L.T("Вернуть в сумку всё, что удалено, пока окно открыто, — те же самые вещи. Закроешь"
                          + " окно — удалённое уже не вернуть.",
                          "Put back into the bag everything deleted while the window is open — the very same"
                          + " items. Once you close the window, deleted items are gone."));
            }

            DrawBag(hero);
            GUILayout.Space(8);
        }

        private void RestoreDeleted(Character hero)
        {
            int back = 0;
            for (int i = 0; i < _deleted.Count; )
            {
                if (ItemsGive.Restore(hero, _deleted[i]))
                {
                    _deleted.RemoveAt(i);
                    back++;
                }
                else i++;
            }
            _itemsMsg = _deleted.Count == 0
                ? L.T("Возвращено в сумку: ", "Restored to the bag: ") + back + "."
                : L.T("Возвращено: ", "Restored: ") + back + L.T(", не влезло: ", ", did not fit: ") + _deleted.Count
                  + L.T(" — освободи место и нажми ещё раз.", " — free some room and click again.");
        }

        /// <summary>
        /// Ползунок размера сумки: от того, что дают Склады («как в игре»), до 100, шагом 5.
        /// Держит размер BagSizeCheat.
        /// </summary>
        private void DrawBagSize()
        {
            int game = BagSizeCheat.GameSize();
            int now = CheatsPlugin.BagSize.Value > game ? CheatsPlugin.BagSize.Value : game;
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Размер сумки", "Bag size"), _text, GUILayout.Width(150));
            float v = GUILayout.HorizontalSlider(now, game, BagSizeCheat.Max, GUILayout.Width(190));
            int nv = Mathf.Clamp(Mathf.RoundToInt(v / 5f) * 5, game, BagSizeCheat.Max);
            GUILayout.Label(nv <= game ? L.T("как в игре", "as in game") : nv + L.T(" мест", " slots"), _value, GUILayout.Width(95));
            GUILayout.EndHorizontal();
            Hover(Note(CheatsPlugin.BagSize) + L.T(" Сейчас Склады дают ", " The Warehouse gives ") + game + ".");
            int store = nv <= game ? 0 : nv;
            if (store != CheatsPlugin.BagSize.Value)
            {
                CheatsPlugin.BagSize.Value = store;
                BagSizeCheat.Changed = true;
            }
        }

        /// <summary>Сумка по группам — в том порядке, как игра сортирует её «по типу».</summary>
        private void DrawBag(Character hero)
        {
            List<SO_Item> bag = GearEdit.Bag(hero);
            if (bag.Count == 0)
            {
                GUILayout.Label(L.T("   сумка пуста", "   the bag is empty"), _muted);
                return;
            }
            List<SO_Item> sorted = new List<SO_Item>(bag);
            sorted.Sort(new BagOrder());
            int lastGroup = -100;
            for (int i = 0; i < sorted.Count; i++)
            {
                SO_Item it = sorted[i];
                if (it == null) continue;
                int g = BagGroup(it);
                if (g != lastGroup)
                {
                    lastGroup = g;
                    GUILayout.Label("   " + L.T(BagGroupRu[g], BagGroupEn[g]), _muted);
                }
                SO_ItemUsable u = it as SO_ItemUsable;
                string count = u != null && u.count > 1 ? "  ×" + u.count : "";
                bool on = _pick == PickBag && _pickBagItem == it;

                GUILayout.BeginHorizontal();
                bool click = GUILayout.Toggle(on, ItemsGive.Title(it) + count, _row, GUILayout.Width(265));
                string q = ItemsGive.Quality(it).Trim().Trim('(', ')');
                GUILayout.Label(q, _value, GUILayout.Width(95));
                bool passive = u != null && u.passive;
                GUI.enabled = !it.isLocked && !passive;
                bool del = GUILayout.Button(L.T("удалить", "delete"), _small, GUILayout.Width(70));
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                Hover(ItemDetails(it) + L.T("  Нажми на название, чтобы заменить: эта вещь упадёт рядом с"
                                            + " героем, новая ляжет в сумку. «Удалить» убирает её совсем, сразу; пока"
                                            + " окно открыто, её можно вернуть.",
                                            "  Click the name to replace: this item drops next to the hero and the"
                                            + " new one goes to the bag. Delete removes it completely, right away;"
                                            + " while the window is open you can restore it.")
                      + (it.isLocked ? L.T(" Сейчас она под замком в сумке игры — удалить нельзя.",
                                           " It is locked in the game's bag now — it can't be deleted.") : "")
                      + (passive ? L.T(" Пассивную вещь удалить нельзя — её действие снимает только игра.",
                                       " A passive item can't be deleted — only the game removes its effect.") : ""));

                if (del)
                {
                    string res = ItemsGive.Delete(hero, it);
                    if (!hero.inventory.Contains(it))
                    {
                        _deleted.Add(it);
                        if (_pickBagItem == it) ClosePick();
                    }
                    _itemsMsg = res;
                    CheatsPlugin.Log.LogInfo("Items: " + res);
                    continue;
                }
                if (click && !on) OpenPick(PickBag, -1, it, hero);
                else if (!click && on) ClosePick();
            }
        }

        private static readonly string[] BagGroupRu = new string[]
        {
            "Кольца", "Шлемы", "Доспехи", "Вторая рука", "Оружие", "Артефакты", "Самоцветы",
            "Расходники", "Прочее"
        };

        private static readonly string[] BagGroupEn = new string[]
        {
            "Rings", "Helmets", "Body armor", "Off-hand", "Weapons", "Artifacts", "Gems",
            "Consumables", "Other"
        };

        private static int BagGroup(SO_Item it)
        {
            if (it is SO_ItemRing) return 0;
            if (it is SO_ItemHead) return 1;
            if (it is SO_ItemBody) return 2;
            if (it is SO_ItemShield) return 3;
            if (it is SO_ItemWeapon) return 4;
            SO_ItemTrash t = it as SO_ItemTrash;
            if (t != null && t.Artifact) return 5;
            if (t != null && t.GemEffects != null && t.GemEffects.Length > 0) return 6;
            if (it is SO_ItemUsable) return 7;
            return 8;
        }

        private sealed class BagOrder : IComparer<SO_Item>
        {
            public int Compare(SO_Item a, SO_Item b)
            {
                if (a == null || b == null) return (a == null ? 1 : 0) - (b == null ? 1 : 0);
                int c = BagGroup(a).CompareTo(BagGroup(b));
                if (c != 0) return c;
                c = ItemsGive.Strength(a).CompareTo(ItemsGive.Strength(b));
                if (c != 0) return c;
                return string.Compare(ItemsGive.Title(a), ItemsGive.Title(b), StringComparison.CurrentCulture);
            }
        }

        /// <summary>Строка о вещи для подсказки: название, качество, сила и описание игры.</summary>
        private static string ItemDetails(SO_Item it)
        {
            string s = ItemsGive.Title(it) + ItemsGive.Quality(it);
            string stat = ItemsGive.StatText(it);
            if (stat.Length > 0) s += ", " + stat;
            SO_ItemEquip eq = it as SO_ItemEquip;
            if (eq != null)
            {
                List<ItemEffect> eff = GearEdit.Effects(eq);
                for (int i = 0; i < eff.Count; i++) s += (i == 0 ? ": " : ", ") + GearEdit.EffectLine(eq, eff[i]);
            }
            s += ".";
            string d = ItemsGive.Description(it);
            if (d.Length > 0) s += " " + d;
            return s;
        }

        // --- боковые окна ----------------------------------------------------------------------

        /// <summary>
        /// Окна выбора — вплотную справа от основного, а не хватит экрана — слева от него. Рисуются
        /// в том же масштабе, что и основное (зовётся из OnGUI при уже выставленной матрице).
        /// </summary>
        private void DrawPickWindows()
        {
            if (_pick == PickNone || _page != 2) return;
            float screenW = Screen.width / _scale;
            float total = KindsWidth + 4f + ListWidth;
            float x = _windowRect.x + _drawW + 4f;
            if (x + total > screenW) x = Mathf.Max(0f, _windowRect.x - total - 4f);
            float y = _windowRect.y;
            float h = _drawH;
            GUILayout.Window(KindsWindowId, new Rect(x, y, KindsWidth, h), new GUI.WindowFunction(DrawKindsWindow),
                             L.T("Что выдать", "What to give"), GUILayout.Width(KindsWidth), GUILayout.Height(h));
            if (_pickKind != null)
                GUILayout.Window(ListWindowId, new Rect(x + KindsWidth + 4f, y, ListWidth, h),
                                 new GUI.WindowFunction(DrawListWindow), _pickKind.Title,
                                 GUILayout.Width(ListWidth), GUILayout.Height(h));
        }

        private void HoverSide(string text)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) _hoveredSide = text;
        }

        private string PickPurpose()
        {
            if (_pick == PickAdd) return L.T("Новая вещь — в сумку.", "New item — into the bag.");
            if (_pick == PickSlot && _pickSlot >= 0) return L.T("Замена: ", "Replace: ") + ItemsGive.SlotTitle(_pickSlot) + ".";
            if (_pick == PickBag && _pickBagItem != null)
                return L.T("Замена в сумке: ", "Replace in the bag: ") + ItemsGive.Title(_pickBagItem) + ".";
            return "";
        }

        private void DrawKindsWindow(int id)
        {
            MakeItemStyles();
            // сбрасываем только на отрисовке: на других проходах подсказка ещё нужна основному окну
            if (Event.current.type == EventType.Repaint) _hoveredSide = "";
            Character hero = GearEdit.Hero();
            if (hero == null)
            {
                GUILayout.Label(L.T("Героя в игре сейчас нет.", "There is no hero in the game right now."), _muted);
                if (GUILayout.Button(L.T("Закрыть", "Close"), _button)) ClosePick();
                return;
            }
            int heroClass = (int)hero.charClass;
            GUILayout.Label(PickPurpose(), _muted);

            List<ItemKind> kinds = ItemsGive.Kinds();
            _kindsScroll = GUILayout.BeginScrollView(_kindsScroll);
            int shown = DrawKinds(kinds, hero, heroClass);

            // оружие и вторая рука чужих классов — свёрнуто, но не убрано
            bool others = false;
            for (int i = 0; i < kinds.Count && !others; i++)
                if (kinds[i].Hero >= 0 && kinds[i].Hero != heroClass && PickFits(kinds[i], hero)) others = true;
            if (others)
            {
                GUILayout.Space(6);
                _pickOthers = GUILayout.Toggle(_pickOthers, (_pickOthers ? "▾ " : "▸ ") + L.T("Оружие других классов", "Other classes' weapons"),
                                               _toggle);
                HoverSide(L.T("Игра выдаёт герою только оружие его класса. Чужое надеть можно: маг с вещами"
                              + " некроманта атакует нормально (проверено 21.09.2026).",
                              "The game gives a hero only weapons of their own class. Others can be equipped:"
                              + " a wizard with necromancer items attacks normally (tested 21.09.2026)."));
                if (_pickOthers)
                    for (int c = 0; c < HeroesTable.All.Length; c++)
                    {
                        if (c == heroClass) continue;
                        bool any = false;
                        for (int i = 0; i < kinds.Count && !any; i++)
                            if (kinds[i].Hero == c && PickFits(kinds[i], hero)) any = true;
                        if (!any) continue;
                        GUILayout.Label(HeroesTable.All[c].Title, _head);
                        shown += DrawKindsOf(kinds, hero, c);
                    }
            }
            if (shown == 0 && !others) GUILayout.Label(L.T("Для этого слота вещей нет.", "No items for this slot."), _muted);
            GUILayout.EndScrollView();

            if (GUILayout.Button(L.T("Закрыть", "Close"), _button)) ClosePick();
        }

        private bool PickFits(ItemKind k, Character hero)
        {
            if (_pick == PickSlot) return ItemsGive.Fits(k, _pickSlot, hero);
            return true;
        }

        /// <summary>Виды своего класса и общие, по разделам. Возвращает, сколько показано.</summary>
        private int DrawKinds(List<ItemKind> kinds, Character hero, int heroClass)
        {
            int shown = 0;
            int lastSection = -1;
            for (int i = 0; i < kinds.Count; i++)
            {
                ItemKind k = kinds[i];
                if (k.Hero >= 0 && k.Hero != heroClass) continue;
                if (!PickFits(k, hero)) continue;
                if (k.Section != lastSection)
                {
                    lastSection = k.Section;
                    GUILayout.Label(ItemsGive.SectionTitle(k.Section), _head);
                }
                KindRow(k);
                shown++;
            }
            return shown;
        }

        /// <summary>Виды одного чужого класса: подразделы помельче, чем у своего.</summary>
        private int DrawKindsOf(List<ItemKind> kinds, Character hero, int cls)
        {
            int shown = 0;
            int lastSection = -1;
            for (int i = 0; i < kinds.Count; i++)
            {
                ItemKind k = kinds[i];
                if (k.Hero != cls || !PickFits(k, hero)) continue;
                if (k.Section != lastSection)
                {
                    lastSection = k.Section;
                    GUILayout.Label("   " + ItemsGive.SectionTitle(k.Section), _muted);
                }
                KindRow(k);
                shown++;
            }
            return shown;
        }

        private void KindRow(ItemKind k)
        {
            bool on = _pickKind == k;
            bool click = GUILayout.Toggle(on, k.Title + "   (" + k.Items.Count + ")", _row);
            if (click && !on)
            {
                _pickKind = k;
                _listScroll = Vector2.zero;
            }
        }

        private void DrawListWindow(int id)
        {
            MakeItemStyles();
            ItemKind k = _pickKind;
            Character hero = GearEdit.Hero();
            if (k == null || hero == null) return;

            string note;
            if (k.Fits == -1)
                note = L.T("От дешёвых к дорогим. Выдаются по одному, одинаковые складываются стопкой.",
                           "Cheapest to priciest. Given one at a time; identical ones stack.");
            else if (k.Fits == ItemsGive.SlotArt1)
                note = L.T("От дешёвых к дорогим. Качества у артефакта нет.", "Cheapest to priciest. Artifacts have no quality.");
            else note = L.T("От слабых к сильным. Снаряжение выдаётся легендарным.", "Weakest to strongest. Gear is given as legendary.");
            GUILayout.Label(note, _muted);
            if (k.Hero >= 0 && k.Hero != (int)hero.charClass)
                GUILayout.Label(L.T("Это вещи класса «", "These are items of the ") + HeroesTable.All[k.Hero].Title
                                + L.T("». Игра такие этому герою не выдаёт, но надеть можно — атаки не ломаются.",
                                      " class. The game doesn't give them to this hero, but they can be equipped — attacks"
                                      + " still work."), _muted);

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < k.Items.Count; i++)
            {
                SO_Item it = k.Items[i];
                GUILayout.BeginHorizontal();
                bool click = GUILayout.Button(ItemsGive.Title(it), _row, GUILayout.Width(ListWidth - 130f));
                GUILayout.Label(ItemsGive.StatText(it), _value, GUILayout.Width(90));
                GUILayout.EndHorizontal();
                string d = ItemsGive.Description(it);
                string stat = ItemsGive.StatText(it);
                HoverSide(ItemsGive.Title(it) + (stat.Length > 0 ? ", " + stat : "")
                          + (it.unique ? L.T(", уникальная — у героя может быть одна", ", unique — a hero can have only one") : "") + "."
                          + (d.Length > 0 ? " " + d : ""));
                if (click) Give(hero, it);
            }
            GUILayout.EndScrollView();
        }

        /// <summary>Выдать или заменить — смотря что выбрано. Итог — вверху вкладки и в углу экрана.</summary>
        private void Give(Character hero, SO_Item template)
        {
            string res;
            if (_pick == PickSlot) res = ItemsGive.Replace(hero, _pickSlot, template);
            else if (_pick == PickBag) res = ItemsGive.ReplaceInBag(hero, _pickBagItem, template);
            else res = ItemsGive.Add(hero, template);
            CheatsPlugin.Log.LogInfo("Items: " + res);
            _itemsMsg = res;
            _flash = res;
            _flashUntil = Time.unscaledTime + 4f;
            // новую вещь в сумку можно выдавать дальше подряд; замена — дело разовое
            if (_pick != PickAdd) ClosePick();
        }
    }
}
