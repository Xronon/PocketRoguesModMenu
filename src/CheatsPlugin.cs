using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PocketRoguesCheats
{
    /// <summary>
    /// Читы для одиночной игры Pocket Rogues — один мод, одно окно (Insert) с разделами:
    /// выживание (бессмертие, мана, без негативных эффектов), параметры героя (обзор, скорость,
    /// броня, урон), добыча (золото, опыт, запрет отправки рекордов в Steam), разведка (карта,
    /// компас, туман), артефакты (свойства любого артефакта без самого артефакта).
    /// Загружается BepInEx 5, сама игра
    /// и сохранение не меняются: всё действует, пока игра запущена с модом.
    ///
    /// Как устроено (по коду игры, Assembly-CSharp):
    ///   здоровье и мана — поля Creature.HP и Creature.ST (мана в коде зовётся ST), меняются только
    ///   через свойства HP_cur / ST_cur; перехват не даёт им уменьшиться у своих героев, а
    ///   Creature.CanReceiveDamage — единственная проверка перед уроном — отвечает «нет».
    ///   Негативные эффекты приходят не уроном, поэтому бессмертие их не гасило: их накладывает
    ///   Creature.AddEffect — одна дверь и для Buff, и для Debuff; своему герою Debuff не пускаем,
    ///   уже наложенное снимаем так же, как игра по истечении времени (RemoveEffect(эффект, 0)).
    ///   Обзор — Character.FOV_normal / FOV_mod: из них PlayerController.Update ставит размер
    ///   камеры, так что множитель отдаляет камеру. Скорость — Controller.Move задаёт скорость
    ///   тела заново при каждом шаге (если герою можно двигаться), её и умножаем после.
    ///   Броня — Character.ArmorDefence. Урон — Creature.SetDamageRPC у врага, если источник удара
    ///   (контроллер героя, в том числе у стрел) — свой герой.
    ///   Золото — GameManager.AddMoney (одна дверь на монеты, продажу и разборку; траты идут
    ///   через неё же отрицательными суммами — множим только приход). Опыт — Character.AddXP.
    ///   Рекорды Steam — GameManager.SendSteamScore: единственное место, откуда игра выкладывает
    ///   очки за забег в публичные таблицы, и единственное, что делает читы видимыми со стороны.
    ///   Карта — то, что делает артефакт «Старая карта»: Character.art_Minimap, затем
    ///   UI_RightTopBar.UpdMinimapLvl и MinimapFowController.ShowFow(false) — миникарта без тумана.
    ///   Компас — Character.art_compass и HiddenObjectAltSprite.ChangeSpriteType(true) у тайников.
    ///   Артефакты — своими руками ничего не повторяем: артефакт грузится из Resources игры по
    ///   пути из таблицы (Artifacts.cs), и его свойства отдаются самой игре —
    ///   Character.EquipArtifact / UnequipArtifact. Сверено по коду: все 70 родов свойств, какие
    ///   есть в игре, эта пара обрабатывает симметрично, кроме RingEff_HpRegPercent (только у
    ///   колец, ни у одного артефакта его нет), и ни Equip, ни Unequip не пишут в сам ассет.
    ///   В сетевой игре (Controller.IsConnected) всё выключено: там чужая игра.
    ///
    /// ⚠️ Игра при каждой загрузке сцены переносит «незнакомые» корневые объекты из своей сцены в
    ///   загружаемую (LoadingManager.LoadAsynchronously), и на следующей смене сцены они гибнут.
    ///   Поэтому окно и клавиши живут на своём объекте с HideAndDontSave, а в BepInEx.cfg —
    ///   HideManagerGameObject = true (версия 1.0 молчала именно из-за этого).
    /// </summary>
    [BepInPlugin(Guid, "Pocket Rogues Cheats", Version)]
    public sealed class CheatsPlugin : BaseUnityPlugin
    {
        public const string Guid = "pocketrogues.cheats";
        public const string Version = "1.8";

        internal static ConfigEntry<bool> God;
        internal static ConfigEntry<bool> Mana;
        internal static ConfigEntry<bool> NoDebuffs;
        internal static ConfigEntry<float> View;
        internal static ConfigEntry<float> Speed;
        internal static ConfigEntry<float> Armor;
        internal static ConfigEntry<float> Damage;
        internal static ConfigEntry<float> Gold;
        internal static ConfigEntry<float> Xp;
        internal static ConfigEntry<bool> NoLeaderboards;
        internal static ConfigEntry<bool> Map;
        internal static ConfigEntry<bool> Compass;
        internal static ConfigEntry<bool> NoFog;
        internal static ConfigEntry<int> BagSize;

        /// <summary>По галочке на каждый артефакт из ArtifactsTable, в том же порядке.</summary>
        internal static ConfigEntry<bool>[] Arts;
        internal static ConfigEntry<bool> ArtsOpen;
        internal static ConfigEntry<KeyboardShortcut> WindowKey;
        internal static ConfigEntry<KeyboardShortcut> GodKey;
        internal static ConfigEntry<KeyboardShortcut> ManaKey;
        internal static ConfigEntry<bool> Overlay;
        internal static ConfigEntry<bool> PauseInWindow;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<float> WinW;
        internal static ConfigEntry<float> WinH;
        internal static ConfigEntry<string> Language;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigFile Cfg;

        /// <summary>Записать настройки в файл (сохраняем не на каждое изменение — см. Awake).</summary>
        internal static void SaveConfig()
        {
            try { if (Cfg != null) Cfg.Save(); }
            catch (Exception ex) { if (Log != null) Log.LogWarning("Settings were not saved: " + ex.Message); }
        }

        /// <summary>
        /// Пояснения к настройкам для окна — на двух языках: строка-подсказка внизу окна берёт их
        /// отсюда (L.T). В самом файле настроек — английские.
        /// </summary>
        internal static readonly Dictionary<ConfigEntryBase, string[]> Notes = new Dictionary<ConfigEntryBase, string[]>();

        /// <summary>Старые русские имена настроек (до 1.8) → новые: значение переносится один раз.</summary>
        private readonly List<KeyValuePair<ConfigDefinition, ConfigEntryBase>> _renamed =
            new List<KeyValuePair<ConfigDefinition, ConfigEntryBase>>();

        /// <summary>Разделы файла настроек до 1.8 — их хвосты после переноса убираются.</summary>
        private static readonly string[] OldSections = new string[]
        {
            "Читы", "Параметры", "Добыча", "Разведка", "Предметы", "Артефакты", "Клавиши", "Экран"
        };

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string oldSection, string oldKey,
                                       string ru, string en, AcceptableValueBase range)
        {
            ConfigEntry<T> e = Config.Bind(section, key, value, new ConfigDescription(en, range));
            Notes[e] = new string[] { ru, en };
            if (oldSection != null)
                _renamed.Add(new KeyValuePair<ConfigDefinition, ConfigEntryBase>(new ConfigDefinition(oldSection, oldKey), e));
            return e;
        }

        /// <summary>
        /// Имя настройки без знаков, которых загрузчик не принимает (= перенос табуляция \ " ' [ ] и
        /// пробелы по краям): иначе Bind бросит исключение и мод не загрузится. Нынешние названия
        /// артефактов чистые; это на случай обновления игры.
        /// </summary>
        private static string SafeKey(string key)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in key)
                if (c != (char)61 && c != (char)10 && c != (char)9 && c != (char)92 && c != (char)34
                    && c != (char)39 && c != (char)91 && c != (char)93) sb.Append(c);   // = перенос таб \ " ' [ ]
            string k = sb.ToString().Trim();
            return k.Length > 0 ? k : "Artifact";
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string oldSection, string oldKey,
                                       string ru, string en)
        {
            return Bind(section, key, value, oldSection, oldKey, ru, en, null);
        }

        /// <summary>
        /// До 1.8 имена разделов и настроек были русскими. Загрузчик держит строки файла, которые ни
        /// одна настройка не забрала, в OrphanedEntries — оттуда значения переезжают под новые имена,
        /// а старые строки убираются. Так перевод не сбрасывает настройки тем, кто ставил мод до
        /// него. Второй раз переносить нечего: старых строк уже нет.
        /// </summary>
        private void MoveRenamed()
        {
            Dictionary<ConfigDefinition, string> orphans = null;
            try
            {
                PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null) orphans = prop.GetValue(Config, null) as Dictionary<ConfigDefinition, string>;
            }
            catch (Exception ex) { Logger.LogWarning("Old settings are not readable: " + ex.Message); }
            if (orphans == null) return;

            int moved = 0;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> r in _renamed)
            {
                string v;
                if (!orphans.TryGetValue(r.Key, out v)) continue;
                try
                {
                    r.Value.SetSerializedValue(v);
                    moved++;
                }
                catch (Exception ex) { Logger.LogWarning("Old setting " + r.Key.Key + " not moved: " + ex.Message); }
            }
            List<ConfigDefinition> old = new List<ConfigDefinition>();
            foreach (ConfigDefinition d in orphans.Keys)
                if (Array.IndexOf(OldSections, d.Section) >= 0) old.Add(d);
            foreach (ConfigDefinition d in old) orphans.Remove(d);
            if (moved > 0 || old.Count > 0)
                Logger.LogInfo("Settings moved to the new English names: " + moved + ", old lines removed: " + old.Count + ".");
        }

        private void Awake()
        {
            Log = Logger;
            // ползунок двигается плавно — писать файл настроек на каждое движение незачем:
            // сохраняем при закрытии окна, по быстрым клавишам и при выходе из игры
            Config.SaveOnConfigSet = false;

            God = Bind("Cheats", "God mode", false, "Читы", "Бессмертие",
                "Урон по своему герою не проходит, здоровье всегда полное.",
                "Your hero takes no damage; health always stays full.");
            Mana = Bind("Cheats", "Infinite mana", false, "Читы", "Бесконечная мана",
                "Навыки не тратят ману, мана всегда полная.",
                "Skills cost no mana; mana always stays full.");
            NoDebuffs = Bind("Cheats", "No debuffs", false, "Читы", "Без негативных эффектов",
                "Отравление, горение, кровотечение, обморожение, окаменение, оглушение,"
                + " замедление, безумие, печать маны, поломка вещи и прочее на своего героя"
                + " не накладываются; уже наложенное снимается.",
                "Poison, burning, bleeding, freezing, petrification, stun, slow, madness, mana seal,"
                + " item breaking and the like are not applied to your hero; anything already"
                + " applied is removed.");
            View = Bind("Hero", "View", 1f, "Параметры", "Обзор",
                "Во сколько раз дальше камера: видно больше карты.",
                "How many times farther the camera is: you see more of the map.",
                new AcceptableValueRange<float>(1f, 2.5f));
            Speed = Bind("Hero", "Run speed", 1f, "Параметры", "Скорость бега",
                "Во сколько раз быстрее бегает свой герой.",
                "How many times faster your hero runs.",
                new AcceptableValueRange<float>(1f, 3f));
            Armor = Bind("Hero", "Armor", 1f, "Параметры", "Броня",
                "Во сколько раз больше защита своего героя.",
                "How many times stronger your hero's defense is.",
                new AcceptableValueRange<float>(1f, 100f));
            Damage = Bind("Hero", "Damage", 1f, "Параметры", "Урон",
                "Во сколько раз сильнее удары, стрелы и заклинания своего героя по врагам.",
                "How many times stronger your hero's hits, arrows and spells are against enemies.",
                new AcceptableValueRange<float>(1f, 100f));
            Gold = Bind("Loot", "Gold", 1f, "Добыча", "Золото",
                "Во сколько раз больше золота: монеты с пола, продажа и разборка вещей."
                + " Траты не дорожают — множится только приход.",
                "How many times more gold: coins from the floor, selling and dismantling items."
                + " Spending does not get more expensive — only income is multiplied.",
                new AcceptableValueRange<float>(1f, 100f));
            Xp = Bind("Loot", "Experience", 1f, "Добыча", "Опыт",
                "Во сколько раз больше опыта своему герою. В этой игре уровень героя и есть запас"
                + " очков навыков, так что множитель ускоряет именно его.",
                "How many times more experience for your hero. In this game the hero's level is"
                + " the skill point pool, so the multiplier speeds that up.",
                new AcceptableValueRange<float>(1f, 100f));
            NoLeaderboards = Bind("Loot", "Block Steam leaderboards", true, "Добыча", "Не отправлять рекорды в Steam",
                "Игра выкладывает очки за забег в публичные таблицы рекордов Steam по каждой"
                + " локации. Это единственное, что делает читы видимыми посторонним, поэтому"
                + " по умолчанию отправка выключена.",
                "After a run the game posts your score to public Steam leaderboards for each"
                + " location. That is the only thing that makes cheats visible to other people,"
                + " so posting is off by default.");
            Map = Bind("Scouting", "Map", false, "Разведка", "Карта",
                "Как артефакт «Старая карта» с Хижиной картографа: миникарта без тумана, враги на ней.",
                "Like the Old Map artifact with the Cartographer's shack: the minimap has no fog and shows enemies.");
            Compass = Bind("Scouting", "Compass", false, "Разведка", "Компас",
                "Как эффект компаса: тайники видны.",
                "Like the compass effect: hidden caches become visible.");
            NoFog = Bind("Scouting", "No fog", false, "Разведка", "Без тумана вокруг героя",
                "Снять затемнение неразведанного вокруг героя на основном экране.",
                "Removes the darkness over unexplored areas around the hero on the main screen.");
            BagSize = Bind("Items", "Bag size", 0, "Предметы", "Размер сумки",
                "Сколько мест в сумке вместо тех, что дают Склады (от 15 до 35); прибавки от вещей"
                + " идут сверху. 0 — как в игре. Если выключить мод при переполненной сумке, лишние"
                + " вещи не пропадут, но окно сумки игры их не покажет, пока размер не вернёшь.",
                "How many bag slots instead of what the Warehouse gives (15 to 35); bonuses from"
                + " items come on top. 0 means as in the game. If you disable the mod with an"
                + " overfull bag, the extra items are not lost, but the game's bag window won't show"
                + " them until you restore the size.",
                new AcceptableValueRange<int>(0, BagSizeCheat.Max));
            ArtsOpen = Bind("Artifacts", "List open", false, "Артефакты", "Список раскрыт",
                "Раскрывать ли список артефактов при открытии окна.",
                "Whether the artifact list is expanded when the window opens.");
            Arts = new ConfigEntry<bool>[ArtifactsTable.All.Length];
            for (int i = 0; i < Arts.Length; i++)
            {
                ArtifactInfo a = ArtifactsTable.All[i];
                Arts[i] = Bind("Artifacts", SafeKey(a.TitleEn), false, "Артефакты", a.TitleRu,
                    "Свойство артефакта «" + a.TitleRu + "» без самого артефакта.",
                    "The property of the " + a.TitleEn + " artifact without the artifact itself.");
            }
            WindowKey = Bind("Keys", "Window", new KeyboardShortcut(KeyCode.Insert), "Клавиши", "Окно",
                "Открыть и закрыть окно мода.",
                "Open and close the mod window.");
            GodKey = Bind("Keys", "God mode", new KeyboardShortcut(KeyCode.F7), "Клавиши", "Бессмертие",
                "Быстрая клавиша бессмертия. F1–F6 игра использует сама — их не брать.",
                "Hotkey for god mode. The game uses F1–F6 itself — don't pick those.");
            ManaKey = Bind("Keys", "Infinite mana", new KeyboardShortcut(KeyCode.F8), "Клавиши", "Бесконечная мана",
                "Быстрая клавиша бесконечной маны.",
                "Hotkey for infinite mana.");
            Overlay = Bind("Window", "Show overlay", true, "Экран", "Показывать надпись",
                "Надпись в левом верхнем углу экрана со списком включённых читов. Выключишь — её не"
                + " будет; короткие сообщения о быстрых клавишах и выдаче вещей всё равно мелькнут на"
                + " пару секунд.",
                "The line in the top-left corner of the screen listing the cheats that are on. Turn it"
                + " off and it's gone; short messages about hotkeys and given items still flash for a"
                + " couple of seconds.");
            PauseInWindow = Bind("Window", "Pause while open", true, "Экран", "Пауза, пока открыто окно",
                "Пока окно мода открыто, игра стоит на паузе.",
                "While the mod window is open, the game is paused.");
            UiScale = Bind("Window", "Scale", 1f, "Экран", "Масштаб окна",
                "Во сколько раз крупнее окно мода и текст в нём. Меняется ползунком в самом окне,"
                + " внизу первой вкладки.",
                "How many times larger the mod window and its text are. Changed with the slider in"
                + " the window itself, at the bottom of the first tab.",
                new AcceptableValueRange<float>(0.6f, 2f));
            WinW = Bind("Window", "Width", 540f, "Экран", "Ширина окна",
                "Запоминается сама, когда тянешь окно за уголок справа внизу.",
                "Remembered automatically when you drag the window by its bottom-right corner.",
                new AcceptableValueRange<float>(420f, 1600f));
            WinH = Bind("Window", "Height", 640f, "Экран", "Высота окна",
                "Запоминается сама, когда тянешь окно за уголок справа внизу.",
                "Remembered automatically when you drag the window by its bottom-right corner.",
                new AcceptableValueRange<float>(360f, 1400f));
            Language = Bind("Window", "Language", L.Auto, null, null,
                "Язык надписей мода: «как в игре» (по-русски, если игра на русском, иначе по-английски),"
                + " English или Русский. Названия вещей, эффектов и героев всегда идут от самой игры.",
                "Language of the mod's own text: Auto follows the game (Russian if the game is in"
                + " Russian, English otherwise), English or Russian. Names of items, effects and heroes"
                + " always come from the game itself.",
                new AcceptableValueList<string>(L.Auto, L.English, L.Russian));

            MoveRenamed();
            Cfg = Config;
            SaveConfig();

            // каждый перехват ставится отдельно: если после обновления игры какой-то не встанет,
            // остальные работают, а в журнале понятная строка
            Harmony harmony = new Harmony(Guid);
            int ok = 0, failed = 0;
            foreach (Type t in typeof(CheatsPlugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception ex)
                {
                    failed++;
                    Logger.LogError("Patch " + t.Name + " failed (did the game update?): " + ex.Message);
                }
            }
            Logger.LogInfo("Patches applied: " + ok + (failed > 0 ? ", failed: " + failed : ""));
            CheatsRunner.Ensure();
            Logger.LogInfo("Cheats loaded (" + Version + "). " + WindowKey.Value + " — window, "
                           + GodKey.Value + " — god mode, " + ManaKey.Value + " — mana.");
        }

        private void OnDestroy()
        {
            // сам мод работает на своём объекте, но знать, что игра сносит объект загрузчика, полезно
            // при выходе из игры Unity уничтожает всё подряд — это не тревога
            if (!CheatsRunner.Quitting)
                Logger.LogWarning("The loader object was destroyed by the game; the mod runs on its own object.");
        }

        /// <summary>Множитель включён — отличается от единицы.</summary>
        internal static bool On(ConfigEntry<float> k) { return Mathf.Abs(k.Value - 1f) > 0.001f; }

        /// <summary>Множитель для показа: «×2,5», «×100».</summary>
        internal static string Mult(ConfigEntry<float> k)
        {
            return "×" + L.Num(k.Value, "0.#");
        }
    }

    /// <summary>
    /// Окно, клавиши, надпись и то, что держится каждый кадр (долив здоровья и маны, карта,
    /// компас, туман). Живёт на своём спрятанном объекте (см. ⚠️ выше).
    /// </summary>
    internal sealed partial class CheatsRunner : MonoBehaviour
    {
        private static CheatsRunner _instance;
        private bool _ticked;
        private bool _keysFailed;
        private string _flash = "";
        private float _flashUntil;
        private GUIStyle _style;
        private GUIStyle _shadow;

        // Размер окна задаём мы, а не раскладка: содержимое прокручивается внутри. Так окно не
        // дёргается ни от переключения вкладок, ни от раскрытия списков — и заодно не упирается в
        // то, что GUILayout.Window умеет только растить окно, но не сжимать: свою высоту оно
        // берёт за наименьшую и обратно не уменьшает.
        // Размер тянется за уголок и запоминается в настройках.

        /// <summary>Сколько высоты окна съедают заголовок, вкладки, подсказка и «Закрыть».</summary>
        private const float ChromeHeight = 190f;

        private const float MinWidth = 420f;
        private const float MinHeight = 360f;

        private bool _window;
        private Rect _windowRect = new Rect(40, 60, 540f, 640f);

        // размер, с которым окно рисуется прямо сейчас (уже зажатый по экрану)
        private float _drawW = 540f;
        private float _drawH = 640f;

        /// <summary>Какая вкладка открыта: 0 — читы, 1 — редактор, 2 — предметы.</summary>
        private int _page;
        private Vector2 _pageScroll;
        private float _savedTimeScale = 1f;
        private bool _paused;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLock;
        private GUIStyle _head;
        private GUIStyle _text;
        private GUIStyle _muted;
        private GUIStyle _toggle;
        private GUIStyle _button;
        private GUIStyle _tipBox;
        private GUIStyle _tab;
        private GUIStyle _small;
        private GUIStyle _value;

        // что включили мы, чтобы при выключении вернуть как было
        private readonly List<Character> _mapSetByUs = new List<Character>();
        private readonly List<Character> _compassSetByUs = new List<Character>();
        private readonly List<Renderer> _fogHiddenByUs = new List<Renderer>();
        private float _nextSlowTick;
        private float _nextDebuffTick;
        private bool _mapWas, _compassWas, _fogWas;
        private int _artsWas;

        // строка-подсказка внизу окна: что под курсором сейчас и что показывали последним
        private string _hovered = "";
        private string _tip = "";

        internal static void Ensure()
        {
            if (_instance != null) return;
            GameObject go = new GameObject("PocketRoguesCheats");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CheatsRunner>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_paused) Time.timeScale = _savedTimeScale;
            if (!Quitting)
                CheatsPlugin.Log.LogWarning("The mod object was destroyed — mod hotkeys stop working until the game restarts.");
        }

        // --- каждый кадр ---------------------------------------------------------------

        private void Update()
        {
            UpdateScale();   // один раз за кадр, до всякой отрисовки — см. _scale
            if (!_ticked)
            {
                _ticked = true;
                CheatsPlugin.Log.LogInfo("The mod receives game frames — hotkeys are live.");
            }
            try
            {
                if (CheatsPlugin.WindowKey.Value.IsDown()) SetWindow(!_window);
                if (CheatsPlugin.GodKey.Value.IsDown()) Toggle(CheatsPlugin.God, L.T("Бессмертие", "God mode"));
                if (CheatsPlugin.ManaKey.Value.IsDown()) Toggle(CheatsPlugin.Mana, L.T("Бесконечная мана", "Infinite mana"));
            }
            catch (Exception ex)
            {
                if (!_keysFailed) CheatsPlugin.Log.LogError("Hotkeys are not readable: " + ex);
                _keysFailed = true;
            }

            if (_window) KeepWindowState();

            bool online = Players.Online();
            List<Character> list = online ? null : Players.List();
            // уже висящий вред снимаем на медленном такте: включил галочку посреди отравления —
            // через полсекунды чисто. Новое не пускает перехват NoDebuffs, сразу
            if (!online && CheatsPlugin.NoDebuffs.Value && Time.unscaledTime >= _nextDebuffTick)
            {
                _nextDebuffTick = Time.unscaledTime + 0.5f;
                Debuffs.Clear(list);
            }
            if (list != null && (CheatsPlugin.God.Value || CheatsPlugin.Mana.Value))
            {
                // держим полными: при включении посреди боя здоровье и мана сразу доливаются
                for (int i = 0; i < list.Count; i++)
                {
                    Character c = list[i];
                    if (c == null || !c.IsAlivedCreature) continue;
                    if (CheatsPlugin.God.Value && c.HP_cur < c.HP_max) c.HP_cur = c.HP_max;
                    if (CheatsPlugin.Mana.Value && c.ST_cur < c.ST_max) c.ST_cur = c.ST_max;
                }
            }

            // карта, компас, туман: включить — сразу; держать — раз в полсекунды (новый этаж
            // создаёт героя и миникарту заново); выключить — вернуть как было
            bool map = CheatsPlugin.Map.Value && !online;
            bool compass = CheatsPlugin.Compass.Value && !online;
            bool noFog = CheatsPlugin.NoFog.Value && !online;
            bool slow = Time.unscaledTime >= _nextSlowTick;
            if (slow) _nextSlowTick = Time.unscaledTime + 0.5f;
            try
            {
                if (map && (slow || !_mapWas)) Scout.KeepMap(list, _mapSetByUs);
                if (!map && _mapWas) Scout.UndoMap(_mapSetByUs);
                if (compass && (slow || !_compassWas)) Scout.KeepCompass(list, _compassSetByUs);
                if (!compass && _compassWas) Scout.UndoCompass(_compassSetByUs);
                if (noFog && (slow || !_fogWas)) Scout.KeepNoFog(_fogHiddenByUs);
                if (!noFog && _fogWas) Scout.UndoNoFog(_fogHiddenByUs);
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogError("Scouting: " + ex.Message);
            }
            _mapWas = map;
            _compassWas = compass;
            _fogWas = noFog;

            // артефакты: держим на том же медленном такте — новый этаж создаёт героя заново, и
            // свойства надо надеть ему снова. Смена галочки видна сразу, не ждёт полсекунды
            int chosen = online ? 0 : ArtifactCheats.Chosen();
            if (slow || chosen != _artsWas || ArtifactCheats.Changed)
            {
                ArtifactCheats.Changed = false;
                try
                {
                    if (online) ArtifactCheats.UndoAll();
                    else ArtifactCheats.Keep(list);
                }
                catch (Exception ex)
                {
                    CheatsPlugin.Log.LogError("Artifacts: " + ex.Message);
                }
            }
            _artsWas = chosen;

            // размер сумки: игра ставит свой при каждом появлении героя, держим на медленном такте
            if (slow || BagSizeCheat.Changed)
            {
                BagSizeCheat.Changed = false;
                try { BagSizeCheat.Keep(online ? null : list); }
                catch (Exception ex) { CheatsPlugin.Log.LogError("Bag size: " + ex.Message); }
            }
        }

        private void Toggle(ConfigEntry<bool> entry, string title)
        {
            entry.Value = !entry.Value;
            CheatsPlugin.SaveConfig();
            _flash = title + ": " + OnOff(entry.Value);
            _flashUntil = Time.unscaledTime + 2.5f;
            CheatsPlugin.Log.LogInfo(_flash);
        }

        internal static string OnOff(bool on) { return on ? L.T("ВКЛ", "ON") : L.T("выкл", "off"); }

        // --- окно ------------------------------------------------------------------------

        private void SetWindow(bool open)
        {
            if (open == _window) return;
            _window = open;
            if (open)
            {
                _savedCursorVisible = Cursor.visible;
                _savedCursorLock = Cursor.lockState;
                if (CheatsPlugin.PauseInWindow.Value)
                {
                    _savedTimeScale = Time.timeScale;
                    Time.timeScale = 0f;
                    _paused = true;
                }
                KeepWindowState();
            }
            else
            {
                if (_paused)
                {
                    // игра могла сама сменить скорость, пока окно было открыто, — тогда не трогаем
                    if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale;
                    _paused = false;
                }
                Cursor.visible = _savedCursorVisible;
                Cursor.lockState = _savedCursorLock;
                CheatsPlugin.SaveConfig();
                ClosePick();
                ForgetDeleted();
            }
        }

        /// <summary>Игра закрывается: дальше объекты уничтожаются штатно, тревожиться не о чем.</summary>
        internal static bool Quitting;

        private void OnApplicationQuit()
        {
            Quitting = true;
            CheatsPlugin.SaveConfig();
        }

        private void KeepWindowState()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (_paused && Time.timeScale != 0f) Time.timeScale = 0f;
        }

        /// <summary>
        /// Масштаб окна. ⚠️ Берётся один раз за кадр, в Update, и до конца кадра не меняется:
        /// система рисования проходит кадр дважды (раскладка, потом отрисовка), и если между
        /// этими проходами масштаб успевает измениться — а ползунок масштаба меняет его прямо
        /// посреди кадра, — окно мерцает. Второй источник скачков — сам ползунок, см. ScaleSlider.
        /// </summary>
        private float _scale = 1f;

        /// <summary>Масштаб под экран, без личного: окно одного видимого размера, за основу — 1080 строк.</summary>
        private static float ScreenScale()
        {
            return Mathf.Clamp(Screen.height / 1080f * 1.15f, 1f, 3f);
        }

        private void UpdateScale()
        {
            // сверху — личный масштаб, чтобы можно было сделать текст крупнее или мельче
            float old = _scale;
            _scale = ScreenScale() * CheatsPlugin.UiScale.Value;
            // место окна тоже хранится в масштабе, и без поправки окно при смене масштаба ещё и
            // уезжало бы. С ней левый верхний угол стоит на экране на месте, окно растёт от него
            if (Mathf.Abs(_scale - old) > 0.0001f)
            {
                _windowRect.x = _windowRect.x * old / _scale;
                _windowRect.y = _windowRect.y * old / _scale;
            }
        }

        private void MakeStyles()
        {
            if (_head != null) return;
            _head = new GUIStyle(GUI.skin.label);
            _head.fontSize = 15;
            _head.fontStyle = FontStyle.Bold;
            _head.normal.textColor = new Color(1f, 0.85f, 0.3f);
            _head.margin = new RectOffset(4, 4, 10, 2);
            _text = new GUIStyle(GUI.skin.label);
            _text.fontSize = 14;
            _muted = new GUIStyle(_text);
            _muted.fontSize = 12;
            _muted.wordWrap = true;
            // описания игра размечает цветом (<color=…>) — пусть он и рисуется, а не показывается
            _muted.richText = true;
            _muted.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            _toggle = new GUIStyle(GUI.skin.toggle);
            _toggle.fontSize = 14;
            _button = new GUIStyle(GUI.skin.button);
            _button.fontSize = 14;
            // кнопки-шаги в строках правки и само число: мельче и поуже обычных
            _small = new GUIStyle(GUI.skin.button);
            _small.fontSize = 12;
            _small.padding = new RectOffset(2, 2, 3, 3);
            _small.margin = new RectOffset(2, 2, 2, 2);
            _value = new GUIStyle(GUI.skin.label);
            _value.fontSize = 14;
            _value.alignment = TextAnchor.MiddleRight;
            _value.normal.textColor = new Color(1f, 0.92f, 0.6f);
            // вкладка — кнопка, которая остаётся вдавленной, пока её страница открыта
            _tab = new GUIStyle(GUI.skin.button);
            _tab.fontSize = 15;
            _tab.fontStyle = FontStyle.Bold;
            _tab.onNormal = _tab.active;
            _tab.onHover = _tab.active;
            // строка «что делает» внизу окна: высота задана в DrawFooter, чтобы окно не дёргалось
            // от длины описания — самое длинное укладывается в три строки
            _tipBox = new GUIStyle(GUI.skin.box);
            _tipBox.fontSize = 12;
            _tipBox.wordWrap = true;
            _tipBox.richText = true;
            _tipBox.alignment = TextAnchor.UpperLeft;
            _tipBox.padding = new RectOffset(6, 6, 4, 4);
            _tipBox.normal.textColor = new Color(0.9f, 0.9f, 0.8f);
        }

        private void OnGUI()
        {
            DrawOverlay();
            if (!_window) return;
            MakeStyles();
            float s = _scale;

            // больше экрана окно не делаем, и целиком за его край не выпускаем: иначе не поймать
            float maxW = Mathf.Max(MinWidth, Screen.width / s - 20f);
            float maxH = Mathf.Max(MinHeight, Screen.height / s - 20f);
            float w = Mathf.Clamp(CheatsPlugin.WinW.Value, MinWidth, maxW);
            float h = Mathf.Clamp(CheatsPlugin.WinH.Value, MinHeight, maxH);
            _drawW = w;
            _drawH = h;
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width / s - 120f));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height / s - 60f));

            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            Rect r = GUILayout.Window(0x5052, _windowRect, new GUI.WindowFunction(DrawWindow),
                L.T("Pocket Rogues — читы (", "Pocket Rogues — cheats (") + CheatsPlugin.WindowKey.Value + ")",
                GUILayout.Width(w), GUILayout.Height(h));
            // размер держим свой: от окна нам нужно только то, куда его перетащили
            r.width = w;
            r.height = h;
            _windowRect = r;
            DrawPickWindows();
            GUI.matrix = old;
        }

        private void DrawWindow(int id)
        {
            _hovered = "";

            GUILayout.BeginHorizontal();
            Tab(0, L.T("Читы", "Cheats"));
            Tab(1, L.T("Редактор", "Editor"));
            Tab(2, L.T("Предметы", "Items"));
            GUILayout.EndHorizontal();

            float pageHeight = Mathf.Max(80f, _drawH - ChromeHeight);
            _pageScroll = GUILayout.BeginScrollView(_pageScroll, GUILayout.Height(pageHeight));
            if (_page == 0) DrawPageCheats();
            else if (_page == 1) DrawPageEditor();
            else DrawPageItems();
            GUILayout.EndScrollView();

            DrawFooter();
            DrawResizeGrip();
            // за заголовок окно таскается; полосу держим узкой, иначе она заедает верх вкладок
            GUI.DragWindow(new Rect(0, 0, 10000, 18));
        }

        /// <summary>Кнопка-вкладка: нажатая остаётся вдавленной.</summary>
        private void Tab(int page, string title)
        {
            bool on = GUILayout.Toggle(_page == page, title, _tab, GUILayout.Height(26));
            if (on && _page != page)
            {
                _page = page;
                _pageScroll = Vector2.zero;
                ClosePick();
            }
        }

        /// <summary>Низ окна: строка-подсказка и «Закрыть». Высота постоянная — окно не прыгает.</summary>
        private void DrawFooter()
        {
            // помним последнее, над чем был курсор: иначе строка гасла бы от полушага в сторону
            // боковые окна вкладки «Предметы» рисуются отдельно — их строка приходит через _hoveredSide
            if (Event.current.type == EventType.Repaint)
            {
                if (_hovered.Length > 0) _tip = _hovered;
                else if (_hoveredSide.Length > 0 && _page == 2 && _pick != PickNone) _tip = _hoveredSide;
            }
            string text = _tip.Length > 0
                ? _tip
                : CheatsPlugin.WindowKey.Value + L.T(" — открыть и закрыть окно", " opens and closes the window")
                  + (CheatsPlugin.PauseInWindow.Value ? L.T("; пока оно открыто, игра на паузе", "; while it is open, the game is paused") : "")
                  + L.T(". Всё запоминается до следующего запуска игры.", ". Everything is remembered until the next game launch.")
                  + L.T(" Наведи курсор на строку — здесь будет, что она делает.", " Hover over a line to see here what it does.");
            GUILayout.Label(text, _tipBox, GUILayout.Height(52));
            if (GUILayout.Button(L.T("Закрыть", "Close"), _button)) SetWindow(false);
        }

        /// <summary>
        /// Уголок справа внизу: за него окно тянется. Размер меняем сами, потому что своего
        /// растягивания у окон этой системы рисования нет вовсе.
        /// </summary>
        private void DrawResizeGrip()
        {
            // номер берём всегда, на каждом проходе: иначе система рисования собьётся со счёта
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Rect grip = new Rect(_drawW - 20f, _drawH - 20f, 16f, 16f);
            GUI.Box(grip, GUIContent.none);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                // «горячий» захват: пока тянем, мышь слушается даже за краем окна и не срывается
                GUIUtility.hotControl = id;
                e.Use();
            }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag)
            {
                CheatsPlugin.WinW.Value = CheatsPlugin.WinW.Value + e.delta.x;
                CheatsPlugin.WinH.Value = CheatsPlugin.WinH.Value + e.delta.y;
                e.Use();
            }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                CheatsPlugin.SaveConfig();
                e.Use();
            }
        }

        /// <summary>Запомнить подсказку, если курсор стоит над только что нарисованной строкой.</summary>
        private void Hover(string text)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) _hovered = text;
        }

        /// <summary>Какой герой открыт на вкладке правки; −1 — ещё не выбирали.</summary>
        private int _editorHero = -1;

        private void DrawPageEditor()
        {
            if (Players.Online())
            {
                GUILayout.Label(L.T("Сетевая игра — правка выключена: там чужая игра.",
                                    "Online game — editing is off: it's someone else's game too."), _muted);
                return;
            }
            if (_editorHero < 0) _editorHero = HeroEdit.CurrentHero();

            GUILayout.Label(L.T("Золото", "Gold"), _head);
            int gold = HeroEdit.Gold();
            int newGold = NumberRow(L.T("Золото", "Gold"), gold, 0, HeroesTable.GoldMax,
                                    new int[] { -100000, -1000, 1000, 100000 },
                                    L.T("Золото крепости. Меняется сразу, и счётчик в углу экрана тоже.",
                                        "The fortress gold. Changes right away, and so does the counter on screen."));
            if (newGold != gold) HeroEdit.SetGold(newGold);

            GUILayout.Label(L.T("Герой", "Hero"), _head);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < HeroesTable.All.Length; i++)
            {
                bool on = GUILayout.Toggle(_editorHero == i, HeroesTable.All[i].Title, _tab,
                                           GUILayout.Height(24));
                if (on) _editorHero = i;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(L.T("Золото, очки навыков и характеристики меняются сразу — и у героя, который"
                                + " уже бегает по подземелью. Навыки правятся в программе-редакторе: их"
                                + " герой берёт из хранилища только в новой вылазке.",
                                "Gold, skill points and attributes change right away — even for a hero who is"
                                + " already in the dungeon. Skills are edited in the save editor: the hero"
                                + " reads them only when a new run starts."), _muted);

            HeroInfo hero = HeroesTable.All[_editorHero];

            GUILayout.Label(L.Game("Characters/Stats/SkillPoints", "Очки навыков", "Skill points"), _head);
            int pts = HeroEdit.Points(_editorHero);
            int newPts = NumberRow(L.T("Очки (они же уровень)", "Points (= level)"), pts, 0, HeroesTable.PointsMax,
                                   new int[] { -1000, -1, 1, 1000 },
                                   L.T("В этой игре запас очков навыков и уровень героя — одно и то же"
                                       + " число. Трата очков понижает уровень, повышение уровня их добавляет.",
                                       "In this game the skill point pool and the hero's level are the same"
                                       + " number. Spending points lowers the level, leveling up adds points."));
            if (newPts != pts) HeroEdit.SetPoints(_editorHero, newPts);

            GUILayout.Label(L.Game("Characters/Stats/Attributes", "Характеристики", "Attributes"), _head);
            for (int a = 0; a < HeroesTable.AttributeKeys.Length; a++)
            {
                int v = HeroEdit.Attribute(_editorHero, a);
                int nv = NumberRow(HeroesTable.AttributeTitle(a), v, 0, HeroesTable.AttributeMax,
                                   new int[] { -10, -1, 1, 10 },
                                   L.T("Предел 50 — это предел самой игры, выше она не пускает и в"
                                       + " экране прокачки. Очки за это не списываются.",
                                       "The cap of 50 is the game's own: its upgrade screen won't go higher"
                                       + " either. No points are spent on this."));
                if (nv != v) HeroEdit.SetAttribute(_editorHero, a, nv);
            }
            if (GUILayout.Button(L.T("Все характеристики: ", "All attributes: ") + hero.Title
                                 + L.T(" — в максимум", " — to max"), _button))
                for (int a = 0; a < HeroesTable.AttributeKeys.Length; a++)
                    HeroEdit.SetAttribute(_editorHero, a, HeroesTable.AttributeMax);

            DrawGear();
            GUILayout.Space(8);
        }

        /// <summary>Какая вещь раскрыта в разделе снаряжения и у какой открыт список добавления.</summary>
        private int _gearSlot = -1;
        private int _gearAdd = -1;

        private void DrawGear()
        {
            GUILayout.Label(L.T("Снаряжение", "Equipment"), _head);
            Character hero = GearEdit.Hero();
            if (hero == null)
            {
                GUILayout.Label(NoHeroText(), _muted);
                return;
            }
            GUILayout.Label(L.T("Правится снаряжение того героя, кем играешь. Эффекты меняются сразу. Качество"
                                + " тоже, но броня и урон, которые от него зависят, пересчитаются со"
                                + " следующей вылазки. Кольца правятся в программе-редакторе.",
                                "This edits the gear of the hero you are playing. Effects change right away."
                                + " So does quality, but the armor and damage that depend on it are"
                                + " recalculated on the next run. Rings are edited in the save editor."), _muted);

            for (int i = 0; i < GearEdit.SlotCount; i++)
            {
                SO_ItemEquip item = GearEdit.Slot(hero, i);
                string title = item == null ? L.T("пусто", "empty") : GearEdit.Title(item);
                string quality = item == null ? "" : GearEdit.QualityName((int)item.Quality);

                GUILayout.BeginHorizontal();
                bool open = GUILayout.Toggle(_gearSlot == i,
                    " " + GearEdit.SlotTitle(i) + ":  " + title, _toggle, GUILayout.Width(330));
                GUILayout.Label(quality, _value, GUILayout.Width(110));
                GUILayout.EndHorizontal();
                Hover(item == null
                          ? GearEdit.SlotTitle(i) + L.T(" — слот пуст", " — empty slot")
                          : GearEdit.Describe(item));
                if (open && _gearSlot != i) _gearSlot = i;
                else if (!open && _gearSlot == i) _gearSlot = -1;

                if (_gearSlot != i || item == null) continue;

                List<ItemEffect> effects = GearEdit.Effects(item);

                // кольцо — только посмотреть: ступени кольца игра хранит внутри каждого эффекта и
                // накладывает при надевании по-своему, на лету мод их не меняет (21.09.2026)
                if (item is SO_ItemRing)
                {
                    if (effects.Count == 0) GUILayout.Label(L.T("      эффектов нет", "      no effects"), _muted);
                    for (int e = 0; e < effects.Count; e++)
                    {
                        GUILayout.Label("      " + GearEdit.EffectLine(item, effects[e]), _text);
                        string what = GearEdit.Description(effects[e], GearEdit.Tier(item, effects[e]));
                        Hover(GearEdit.EffectLine(item, effects[e]) + (what.Length > 0 ? " — " + what : ""));
                    }
                    GUILayout.Label(L.T("      Кольца правятся в программе-редакторе при закрытой игре.",
                                        "      Rings are edited in the save editor with the game closed."), _muted);
                    Hover(L.T("У кольца игра хранит ступени по-своему и накладывает их при надевании, поэтому"
                              + " на ходу правка кольца не держится. В программе-редакторе оно правится"
                              + " по правилам игры: очки, род эффекта, несовместимые.",
                              "The game stores ring tiers its own way and applies them when the ring is put on,"
                              + " so editing a ring on the fly doesn't hold. The save editor edits rings by the"
                              + " game's rules: points, effect kind, incompatible effects."));
                    continue;
                }

                // раскрытая вещь: её эффекты и выбор качества
                int norm = GearEdit.EffectNorm(item);
                if (effects.Count == 0) GUILayout.Label(L.T("      эффектов нет", "      no effects"), _muted);
                for (int e = 0; e < effects.Count; e++)
                {
                    ItemEffect eff = effects[e];
                    int tier = GearEdit.Tier(item, eff);
                    int tiers = GearEdit.TierCount(eff);
                    bool innate = GearEdit.IsDefault(item, eff);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("      " + GearEdit.EffectLine(item, eff), _text,
                                    GUILayout.Width(320));
                    if (GUILayout.Button("−", _small, GUILayout.Width(28)) && tier > 0)
                        GearEdit.LiveTier(hero, item, eff, tier - 1);
                    if (GUILayout.Button("+", _small, GUILayout.Width(28)) && tier < tiers - 1)
                        GearEdit.LiveTier(hero, item, eff, tier + 1);
                    if (GUILayout.Button(L.T("макс", "max"), _small, GUILayout.Width(46)))
                        GearEdit.LiveTier(hero, item, eff, tiers - 1);
                    if (innate) GUILayout.Label(L.T("врождённый", "innate"), _muted, GUILayout.Width(80));
                    else if (GUILayout.Button(L.T("убрать", "remove"), _small, GUILayout.Width(60)))
                        GearEdit.LiveRemove(hero, item, eff);
                    GUILayout.EndHorizontal();
                    string what = GearEdit.Description(eff, tier);
                    Hover(GearEdit.EffectLine(item, eff)
                          + (what.Length > 0 ? " — " + what : "")
                          + L.T("  (ступеней ", "  (tiers: ") + tiers
                          + (innate ? L.T(", врождённый — убрать нельзя", ", innate — can't be removed") : "") + ")");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(L.T("      эффектов ", "      effects ") + effects.Count + L.T(" из ", " of ") + norm, _muted,
                                GUILayout.Width(200));
                bool full = effects.Count >= norm;
                if (!full && GUILayout.Button(_gearAdd == i ? L.T("скрыть список", "hide list") : L.T("добавить эффект", "add effect"),
                                              _small, GUILayout.Width(150)))
                    _gearAdd = _gearAdd == i ? -1 : i;
                if (full) GUILayout.Label(L.T("больше вещь не удержит", "the item holds no more"), _muted);
                GUILayout.EndHorizontal();
                Hover(L.T("Вещь удерживает столько эффектов, сколько даёт её качество плюс"
                          + " врождённые. Лишнее игра срежет сама при следующей загрузке.",
                          "An item holds as many effects as its quality allows plus its innate ones."
                          + " The game trims any extra on the next load."));

                if (_gearAdd == i)
                {
                    List<ItemEffect> pool = GearEdit.Pool(item);
                    if (pool.Count == 0) GUILayout.Label(L.T("         добавить нечего", "         nothing to add"), _muted);
                    for (int p = 0; p < pool.Count; p++)
                    {
                        string t = pool[p].GetTitle;
                        if (string.IsNullOrEmpty(t)) t = pool[p].name;
                        if (GUILayout.Button("         + " + t, _small))
                        {
                            GearEdit.LiveAdd(hero, item, pool[p]);
                            _gearAdd = -1;
                        }
                        string what = GearEdit.Description(pool[p], GearEdit.TierCount(pool[p]) - 1);
                        Hover(t + (what.Length > 0 ? " — " + what : "")
                              + L.T("  (добавится сразу высшей ступенью; предлагается только то,"
                                    + " что игра сама может выкинуть на эту вещь)",
                                    "  (added at the top tier; only what the game itself can roll on this"
                                    + " item is offered)"));
                    }
                }

                List<string> curses = GearEdit.Curses(item);
                for (int c = 0; c < curses.Count; c++)
                    GUILayout.Label(L.T("      • проклятие: ", "      • curse: ") + curses[c], _muted);
                if (curses.Count > 0)
                {
                    if (GUILayout.Button(L.T("      снять проклятия с этой вещи", "      remove curses from this item"), _small))
                        GearEdit.RemoveCurses(hero, item);
                    Hover(L.T("В самой игре снять проклятие нельзя никак — только носить или выбросить"
                              + " вещь. Здесь оно снимается, и с героя тоже, сразу.",
                              "The game itself has no way to remove a curse — you can only wear or drop"
                              + " the item. Here it comes off, from the hero too, right away."));
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(L.T("      качество:", "      quality:"), _text, GUILayout.Width(100));
                for (int q = 0; q <= 4; q++)
                {
                    bool now = (int)item.Quality == q;
                    if (GUILayout.Toggle(now, GearEdit.QualityName(q), _small, GUILayout.Width(84))
                        && !now)
                        GearEdit.LiveQuality(hero, item, q);
                }
                GUILayout.EndHorizontal();
                Hover(L.T("Качество задаёт, сколько эффектов вещь удерживает, и множит броню или урон."
                          + " Выше легендарного в игре ничего нет. Броня и урон пересчитаются со"
                          + " следующей вылазки.",
                          "Quality sets how many effects an item holds and multiplies its armor or damage."
                          + " Nothing in the game is above legendary. Armor and damage are recalculated"
                          + " on the next run."));
            }

            List<SO_Item> bag = GearEdit.Bag(hero);
            GUILayout.Label(L.T("В сумке предметов: ", "Items in the bag: ") + bag.Count, _muted);
        }

        /// <summary>
        /// Строка числа: название, само число и кнопки-шаги. Возвращает новое значение — вызвавший
        /// сам решает, что с ним делать, поэтому строка ничего не знает про хранилище игры.
        /// </summary>
        private int NumberRow(string title, int value, int min, int max, int[] steps, string note)
        {
            int result = value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _text, GUILayout.Width(155));
            GUILayout.Label(Digits(value), _value, GUILayout.Width(95));
            for (int i = 0; i < steps.Length; i++)
            {
                string label = steps[i] > 0 ? "+" + Short(steps[i]) : "−" + Short(-steps[i]);
                if (GUILayout.Button(label, _small, GUILayout.Width(46)))
                    result = Mathf.Clamp(value + steps[i], min, max);
            }
            if (GUILayout.Button(L.T("макс", "max"), _small, GUILayout.Width(46))) result = max;
            GUILayout.EndHorizontal();
            Hover(title + " — " + note);
            return result;
        }

        /// <summary>Число с пробелами по три знака: 1 193 977.</summary>
        private static string Digits(int v)
        {
            string s = Mathf.Abs(v).ToString();
            string outp = "";
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0) outp += " ";
                outp += s[i];
            }
            return (v < 0 ? "−" : "") + outp;
        }

        /// <summary>Короткая подпись шага: 1000 → «1к», 100000 → «100к».</summary>
        private static string Short(int v)
        {
            if (v >= 1000 && v % 1000 == 0) return (v / 1000) + L.T("к", "k");
            return v.ToString();
        }

        /// <summary>Почему вещей не видно: общая строка вкладок «Редактор» и «Предметы».</summary>
        internal static string NoHeroText()
        {
            return L.T("Вещи видны только у героя, которым играешь прямо сейчас: у"
                       + " остальных они лежат в сохранении и в памяти игры их нет."
                       + " Зайди героем в крепость или в подземелье.",
                       "Items are visible only for the hero you are playing right now: the others'"
                       + " items sit in the save file, not in the game's memory. Enter the fortress"
                       + " or a dungeon with a hero.");
        }

        private void DrawPageCheats()
        {
            if (Players.Online())
                GUILayout.Label(L.T("Сетевая игра — всё выключено: там чужая игра.",
                                    "Online game — everything is off: it's someone else's game too."), _muted);

            GUILayout.Label(L.T("Выживание", "Survival"), _head);
            Check(CheatsPlugin.God, L.T("Бессмертие  (", "God mode  (") + CheatsPlugin.GodKey.Value + ")");
            Check(CheatsPlugin.Mana, L.T("Бесконечная мана  (", "Infinite mana  (") + CheatsPlugin.ManaKey.Value + ")");
            Check(CheatsPlugin.NoDebuffs, L.T("Без негативных эффектов: отравление, горение, оглушение…",
                                              "No debuffs: poison, burning, stun…"));

            GUILayout.Label(L.T("Параметры героя", "Hero stats"), _head);
            Slider(CheatsPlugin.View, L.T("Обзор (камера дальше)", "View (camera farther)"), 1f, 2.5f, 0.1f, 0.1f);
            Slider(CheatsPlugin.Speed, L.T("Скорость бега", "Run speed"), 1f, 3f, 0.1f, 0.1f);
            Slider(CheatsPlugin.Armor, L.T("Броня", "Armor"), 1f, 100f, 0.5f, 5f);
            Slider(CheatsPlugin.Damage, L.T("Урон", "Damage"), 1f, 100f, 0.5f, 5f);
            GUILayout.Label(L.T("Скорость выше ×3 не поднимаем: герой начнёт проскакивать стены.",
                                "Speed stops at ×3: above that the hero starts slipping through walls."), _muted);

            GUILayout.Label(L.T("Добыча", "Loot"), _head);
            Slider(CheatsPlugin.Gold, L.T("Золото", "Gold"), 1f, 100f, 0.5f, 5f);
            Slider(CheatsPlugin.Xp, L.T("Опыт", "Experience"), 1f, 100f, 0.5f, 5f);
            Check(CheatsPlugin.NoLeaderboards, L.T("Не отправлять рекорды в Steam", "Don't post scores to Steam"));
            GUILayout.Label(CheatsPlugin.NoLeaderboards.Value
                                ? L.T("Очки за забег в публичные таблицы Steam не уходят.",
                                      "Run scores are not posted to the public Steam leaderboards.")
                                : L.T("ВНИМАНИЕ: очки за забег уйдут в публичные таблицы Steam.",
                                      "WARNING: run scores will be posted to the public Steam leaderboards."), _muted);
            Check(CheatsPlugin.Overlay, L.T("Показывать надпись с включёнными читами", "Show the list of active cheats"));

            if (GUILayout.Button(L.T("Все множители — обратно ×1", "All multipliers back to ×1"), _button))
            {
                CheatsPlugin.View.Value = 1f;
                CheatsPlugin.Speed.Value = 1f;
                CheatsPlugin.Armor.Value = 1f;
                CheatsPlugin.Damage.Value = 1f;
                CheatsPlugin.Gold.Value = 1f;
                CheatsPlugin.Xp.Value = 1f;
            }

            GUILayout.Label(L.T("Разведка", "Scouting"), _head);
            Check(CheatsPlugin.Map, L.T("Карта: миникарта без тумана, враги на ней", "Map: minimap without fog, enemies shown"));
            Check(CheatsPlugin.Compass, L.T("Компас: тайники видны", "Compass: hidden caches visible"));
            Check(CheatsPlugin.NoFog, L.T("Без тумана вокруг героя", "No fog around the hero"));

            DrawArtifacts();

            GUILayout.Label(L.T("Окно", "Window"), _head);
            ScaleSlider(CheatsPlugin.UiScale, L.T("Масштаб окна и текста", "Window and text scale"), 0.6f, 2f);
            GUILayout.Label(L.T("Размер окна тянется за уголок справа внизу. И размер, и масштаб"
                                + " запоминаются до следующего запуска игры.",
                                "Drag the bottom-right corner to resize the window. Both size and scale"
                                + " are remembered until the next game launch."), _muted);
            if (GUILayout.Button(L.T("Вернуть обычный размер окна", "Reset window size"), _button))
            {
                CheatsPlugin.UiScale.Value = 1f;
                CheatsPlugin.WinW.Value = 540f;
                CheatsPlugin.WinH.Value = 640f;
            }
            DrawLanguage();
            GUILayout.Space(8);
        }

        /// <summary>Язык надписей мода: как в игре, English или Русский.</summary>
        private void DrawLanguage()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Язык мода", "Mod language"), _text, GUILayout.Width(180));
            string now = CheatsPlugin.Language.Value;
            string[] values = new string[] { L.Auto, L.English, L.Russian };
            string[] titles = new string[] { L.T("как в игре", "as the game"), "English", "Русский" };
            for (int i = 0; i < values.Length; i++)
            {
                bool on = GUILayout.Toggle(now == values[i], titles[i], _small, GUILayout.Width(96));
                if (on && now != values[i]) CheatsPlugin.Language.Value = values[i];
            }
            GUILayout.EndHorizontal();
            Hover(Note(CheatsPlugin.Language));
        }

        private void Check(ConfigEntry<bool> entry, string title)
        {
            bool v = GUILayout.Toggle(entry.Value, " " + title, _toggle);
            Hover(Note(entry));
            if (v != entry.Value) entry.Value = v;
        }

        /// <summary>
        /// Пояснение к настройке — из того же места, где она заведена (CheatsPlugin.Bind): там оба
        /// языка рядом, и в файле настроек лежит английский из той же пары, так что окно и файл не
        /// разъедутся при правках.
        /// </summary>
        private static string Note(ConfigEntryBase entry)
        {
            if (entry == null) return "";
            string[] n;
            if (CheatsPlugin.Notes.TryGetValue(entry, out n)) return L.T(n[0], n[1]);
            return entry.Description != null ? entry.Description.Description : "";
        }

        /// <summary>
        /// Раздел «Артефакты»: список галочек в своей прокрутке и под ним строка «что делает» —
        /// описание того артефакта, на котором курсор. Высоты заданы заранее, поэтому окно не
        /// растёт ни от числа артефактов, ни от длины описания.
        /// </summary>
        private void DrawArtifacts()
        {
            int chosen = ArtifactCheats.Chosen();
            GUILayout.Label(L.T("Артефакты", "Artifacts") + (chosen > 0 ? L.T("  (выбрано ", "  (chosen: ") + chosen + ")" : ""), _head);
            string label = (CheatsPlugin.ArtsOpen.Value ? L.T("Скрыть список", "Hide list") : L.T("Показать список", "Show list"))
                           + " — " + CheatsPlugin.Arts.Length + L.T(" шт.", "");
            if (GUILayout.Button(label, _button))
                CheatsPlugin.ArtsOpen.Value = !CheatsPlugin.ArtsOpen.Value;
            if (!CheatsPlugin.ArtsOpen.Value) return;

            GUILayout.Label(L.T("Свойство действует, пока галочка стоит, — сам артефакт не нужен."
                                + " «Старая карта» и компас — выше, в «Разведке».",
                                "The property works while the box is ticked — you don't need the artifact."
                                + " The Old Map and the compass are above, in Scouting."), _muted);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.T("Включить все", "Tick all"), _button)) SetAllArtifacts(true);
            if (GUILayout.Button(L.T("Снять все", "Untick all"), _button)) SetAllArtifacts(false);
            GUILayout.EndHorizontal();

            List<Character> heroes = Players.Online() ? null : Players.List();
            for (int i = 0; i < CheatsPlugin.Arts.Length; i++)
            {
                string title = ArtifactCheats.Title(i);
                bool worn = heroes != null && ArtifactCheats.RealOnAnyHero(heroes, i);
                bool v = GUILayout.Toggle(CheatsPlugin.Arts[i].Value,
                                          " " + title + (worn ? L.T("   — уже надет", "   — already worn") : ""), _toggle);
                if (v != CheatsPlugin.Arts[i].Value)
                {
                    CheatsPlugin.Arts[i].Value = v;
                    ArtifactCheats.Changed = true;
                }
                Hover(title + " — " + ArtifactCheats.Note(i)
                      + (worn ? L.T(" (артефакт уже надет по-настоящему, свойство и так действует)",
                                    " (the real artifact is worn, so the property already works)") : ""));
            }
        }

        private void SetAllArtifacts(bool on)
        {
            for (int i = 0; i < CheatsPlugin.Arts.Length; i++) CheatsPlugin.Arts[i].Value = on;
            ArtifactCheats.Changed = true;
        }

        /// <summary>
        /// Ползунок множителя. Шаг мельче до ×10 и крупнее дальше: иначе на ползунке шириной
        /// в 190 точек сотня значений с шагом 0,5 не выбирается пальцем.
        /// </summary>
        private void Slider(ConfigEntry<float> entry, string title, float min, float max,
                            float step, float bigStep)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _text, GUILayout.Width(180));
            float v = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(190));
            float s = v > 10f ? bigStep : step;
            v = Mathf.Clamp(Mathf.Round(v / s) * s, min, max);
            GUILayout.Label("×" + L.Num(v, "0.#"), _text, GUILayout.Width(55));
            GUILayout.EndHorizontal();
            Hover(Note(entry));
            if (Mathf.Abs(v - entry.Value) > 0.0001f) entry.Value = v;
        }

        // ползунок масштаба: что запомнено при нажатии (см. ScaleSlider)
        private float _scaleDragWinX;    // левый край окна на экране, в точках экрана
        private float _scaleDragOrigin;  // начало хода ручки от левого края окна, в единицах окна
        private float _scaleDragGrab;    // на сколько точек экрана правее середины ручки за неё взялись

        /// <summary>
        /// Ползунок масштаба. Обычный здесь не годится: он лежит в том самом окне, которое
        /// масштабирует. Сдвинул ручку — окно выросло, ползунок уехал из-под курсора, и тот же
        /// курсор теперь показывает на другое значение, окно сжимается, и так по кругу: размер
        /// перескакивал между двумя, как мышь ни держи (исправлено в 1.5.1).
        /// Поэтому значение считается от курсора на экране и величин, запомненных при нажатии, —
        /// во время перетаскивания они не меняются, и петля разорвана. Левый край окна на экране
        /// стоит на месте (UpdateScale), а значение подбирается так, чтобы ручка при новом
        /// масштабе пришлась ровно под курсор: ползунок ведёт себя как обычный.
        /// </summary>
        private void ScaleSlider(ConfigEntry<float> entry, string title, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _text, GUILayout.Width(180));
            Rect track = GUILayoutUtility.GetRect(190f, 190f, 18f, 18f, GUI.skin.horizontalSlider,
                                                  GUILayout.Width(190));
            // номер берём всегда, на каждом проходе: иначе система рисования собьётся со счёта
            int id = GUIUtility.GetControlID(FocusType.Passive);
            GUIStyle thumbStyle = GUI.skin.horizontalSliderThumb;
            float thumb = thumbStyle.fixedWidth > 0f ? thumbStyle.fixedWidth : 12f;
            float travel = track.width - thumb;

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && track.Contains(e.mousePosition))
            {
                float s = _scale;
                float mouseX = Input.mousePosition.x;
                _scaleDragWinX = _windowRect.x * s;
                // начало хода — середина ручки при наименьшем значении: сперва на экране, потом от окна
                float origin = mouseX + (track.x + thumb / 2f - e.mousePosition.x) * s;
                _scaleDragOrigin = (origin - _scaleDragWinX) / s;
                // взялся за саму ручку — она не прыгает серединой под курсор, как у обычного ползунка
                float center = track.x + thumb / 2f + (entry.Value - min) / (max - min) * travel;
                float off = e.mousePosition.x - center;
                _scaleDragGrab = Mathf.Abs(off) <= thumb / 2f ? off * s : 0f;
                GUIUtility.hotControl = id;
                ScaleFromMouse(entry, min, max, travel, mouseX);
                e.Use();
            }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag)
            {
                ScaleFromMouse(entry, min, max, travel, Input.mousePosition.x);
                e.Use();
            }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                CheatsPlugin.SaveConfig();
                e.Use();
            }
            // обычный ползунок только рисует: нажатия и перетаскивание забраны выше
            GUI.HorizontalSlider(track, entry.Value, min, max);
            GUILayout.Label(Mathf.RoundToInt(entry.Value * 100f) + ((char)0xA0).ToString() + "%",
                            _text, GUILayout.Width(55));
            GUILayout.EndHorizontal();
            Hover(Note(entry));
        }

        /// <summary>
        /// Значение масштаба, при котором середина ручки встаёт под курсор. Ручка на экране стоит в
        /// winX + (origin + f·travel)·base·(min + f·(max − min)), где f — доля хода от 0 до 1, а base —
        /// масштаб под экран: приравниваем курсору и решаем квадратное уравнение относительно f.
        /// </summary>
        private void ScaleFromMouse(ConfigEntry<float> entry, float min, float max, float travel,
                                    float mouseX)
        {
            float k = max - min;
            float d = (mouseX - _scaleDragGrab - _scaleDragWinX) / ScreenScale();
            float a = travel * k;
            float b = _scaleDragOrigin * k + travel * min;
            float c = _scaleDragOrigin * min - d;
            float f = (-b + Mathf.Sqrt(Mathf.Max(0f, b * b - 4f * a * c))) / (2f * a);
            float v = min + Mathf.Clamp01(f) * k;
            // шаг в один процент: плавно, и число под ползунком круглое
            v = Mathf.Clamp(Mathf.Round(v * 100f) / 100f, min, max);
            if (Mathf.Abs(v - entry.Value) > 0.0001f) entry.Value = v;
        }

        // --- надпись в углу ----------------------------------------------------------------

        private void DrawOverlay()
        {
            bool flash = Time.unscaledTime < _flashUntil;
            List<string> parts = new List<string>();
            if (CheatsPlugin.God.Value) parts.Add(L.T("Бессмертие", "God mode"));
            if (CheatsPlugin.Mana.Value) parts.Add(L.T("Мана", "Mana"));
            if (CheatsPlugin.NoDebuffs.Value) parts.Add(L.T("Без эффектов", "No debuffs"));
            if (CheatsPlugin.On(CheatsPlugin.View)) parts.Add(L.T("Обзор ", "View ") + CheatsPlugin.Mult(CheatsPlugin.View));
            if (CheatsPlugin.On(CheatsPlugin.Speed)) parts.Add(L.T("Скорость ", "Speed ") + CheatsPlugin.Mult(CheatsPlugin.Speed));
            if (CheatsPlugin.On(CheatsPlugin.Armor)) parts.Add(L.T("Броня ", "Armor ") + CheatsPlugin.Mult(CheatsPlugin.Armor));
            if (CheatsPlugin.On(CheatsPlugin.Damage)) parts.Add(L.T("Урон ", "Damage ") + CheatsPlugin.Mult(CheatsPlugin.Damage));
            if (CheatsPlugin.On(CheatsPlugin.Gold)) parts.Add(L.T("Золото ", "Gold ") + CheatsPlugin.Mult(CheatsPlugin.Gold));
            if (CheatsPlugin.On(CheatsPlugin.Xp)) parts.Add(L.T("Опыт ", "XP ") + CheatsPlugin.Mult(CheatsPlugin.Xp));
            if (CheatsPlugin.Map.Value) parts.Add(L.T("Карта", "Map"));
            if (CheatsPlugin.Compass.Value) parts.Add(L.T("Компас", "Compass"));
            if (CheatsPlugin.NoFog.Value) parts.Add(L.T("Без тумана", "No fog"));
            int arts = ArtifactCheats.Chosen();
            if (arts > 0) parts.Add(L.T("Артефакты: ", "Artifacts: ") + arts);
            if (CheatsPlugin.BagSize.Value > 0) parts.Add(L.T("Сумка ", "Bag ") + CheatsPlugin.BagSize.Value);
            bool any = parts.Count > 0;
            // о единственном, что видно посторонним, предупреждаем, только когда читы включены
            if (any && !CheatsPlugin.NoLeaderboards.Value) parts.Add(L.T("рекорды Steam ОТПРАВЛЯЮТСЯ", "Steam scores ARE POSTED"));
            if (!flash && !(any && CheatsPlugin.Overlay.Value)) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 16;
                _style.normal.textColor = new Color(1f, 0.85f, 0.3f);
                _shadow = new GUIStyle(_style);
                _shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }

            string text;
            if (Players.Online() && any) text = L.T("Читы выключены: сетевая игра", "Cheats are off: online game");
            else text = string.Join(" · ", parts.ToArray());
            // надпись выключена — остаётся только короткое сообщение, без списка читов
            if (!CheatsPlugin.Overlay.Value) text = "";
            if (flash) text = text.Length > 0 ? _flash + "\n" + text : _flash;

            Rect r = new Rect(12, 8, 900, 60);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _shadow);
            GUI.Label(r, text, _style);
        }
    }

    /// <summary>Карта, компас и туман: включить, держать, вернуть как было.</summary>
    internal static class Scout
    {
        internal static void KeepMap(List<Character> list, List<Character> setByUs)
        {
            if (list == null) return;
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                Character c = list[i];
                if (c == null || c.art_Minimap) continue;
                c.art_Minimap = true;
                if (!setByUs.Contains(c)) setByUs.Add(c);
                changed = true;
            }
            GameManager gm = GameManager.Instance;
            if (changed && gm != null && gm.rightBar != null) gm.rightBar.UpdMinimapLvl();
            if (MinimapFowController.Instance != null) MinimapFowController.Instance.ShowFow(false);
        }

        internal static void UndoMap(List<Character> setByUs)
        {
            for (int i = 0; i < setByUs.Count; i++)
                if (setByUs[i] != null) setByUs[i].art_Minimap = false;
            setByUs.Clear();
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.rightBar != null) gm.rightBar.UpdMinimapLvl();
            // как решает сама игра (Character.EquipArtifact): туман снят, только если есть настоящая
            // карта и построена Хижина картографа
            bool realMap = false;
            List<Character> list = Players.List();
            if (list != null) foreach (Character c in list) if (c != null && c.art_Minimap) realMap = true;
            if (MinimapFowController.Instance != null)
                MinimapFowController.Instance.ShowFow(!(realMap && FileBasedPrefs.GetInt("bld_map", 0) != 0));
        }

        internal static void KeepCompass(List<Character> list, List<Character> setByUs)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Character c = list[i];
                if (c == null || c.art_compass) continue;
                c.art_compass = true;
                if (!setByUs.Contains(c)) setByUs.Add(c);
            }
            HiddenSprites(true);
        }

        internal static void UndoCompass(List<Character> setByUs)
        {
            for (int i = 0; i < setByUs.Count; i++)
                if (setByUs[i] != null) setByUs[i].art_compass = false;
            setByUs.Clear();
            bool realCompass = false;
            List<Character> list = Players.List();
            if (list != null) foreach (Character c in list) if (c != null && c.art_compass) realCompass = true;
            if (!realCompass) HiddenSprites(false);
        }

        /// <summary>Как Character.EquipArtifact для компаса: у всех тайников сменить вид.</summary>
        private static void HiddenSprites(bool shown)
        {
            GameObject[] hidden;
            try { hidden = GameObject.FindGameObjectsWithTag("HiddenObject"); }
            catch (UnityException) { return; }   // на сцене без такого тега Unity бросает исключение
            for (int i = 0; i < hidden.Length; i++)
            {
                HiddenObjectAltSprite alt = hidden[i].GetComponent<HiddenObjectAltSprite>();
                if (alt != null) alt.ChangeSpriteType(shown);
            }
        }

        internal static void KeepNoFog(List<Renderer> hiddenByUs)
        {
            FogOfWarPlayer[] fogs = UnityEngine.Object.FindObjectsOfType<FogOfWarPlayer>();
            for (int i = 0; i < fogs.Length; i++)
            {
                Renderer r = fogs[i].FogOfWarRenderer;
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                if (!hiddenByUs.Contains(r)) hiddenByUs.Add(r);
            }
        }

        internal static void UndoNoFog(List<Renderer> hiddenByUs)
        {
            for (int i = 0; i < hiddenByUs.Count; i++)
                if (hiddenByUs[i] != null) hiddenByUs[i].enabled = true;
            hiddenByUs.Clear();
        }
    }

    /// <summary>
    /// Артефакты: свойство любого артефакта без самого артефакта. Ничего своими руками не
    /// повторяем — артефакт грузится из Resources игры, а его свойства отдаются самой игре
    /// (Character.EquipArtifact / UnequipArtifact), поэтому работает ровно как настоящий.
    ///
    /// Два правила, из-за которых нужен учёт:
    ///   • свойства складываются. Надеть одно и то же дважды — получить двойную прибавку,
    ///     поэтому помним каждую пару «герой + артефакт» и второй раз не надеваем;
    ///   • артефакт, который герой носит по-настоящему, мы не трогаем. Иначе наше «снять»
    ///     погасило бы и настоящее свойство: игра при снятии пересчитывает надетое и считает
    ///     единственного носителя последним (Character.UnequipArtifact, проверка на дубли).
    ///     Надел настоящий — наше снимается; снял настоящий — наше возвращается само.
    /// </summary>
    internal static class ArtifactCheats
    {
        /// <summary>Что мы надели: герой и номер артефакта в ArtifactsTable.</summary>
        private sealed class Applied
        {
            public readonly Character Hero;
            public readonly int Index;

            public Applied(Character hero, int index)
            {
                Hero = hero;
                Index = index;
            }
        }

        private static readonly List<Applied> _applied = new List<Applied>();
        private static readonly Dictionary<int, SO_ItemTrash> _loaded = new Dictionary<int, SO_ItemTrash>();
        private static bool[] _complained;

        /// <summary>Галочку только что тронули — не ждать медленного такта.</summary>
        internal static bool Changed;

        /// <summary>Сколько артефактов отмечено галочками.</summary>
        internal static int Chosen()
        {
            if (CheatsPlugin.Arts == null) return 0;
            int n = 0;
            for (int i = 0; i < CheatsPlugin.Arts.Length; i++)
                if (CheatsPlugin.Arts[i].Value) n++;
            return n;
        }

        /// <summary>Привести надетое в соответствие с галочками.</summary>
        internal static void Keep(List<Character> heroes)
        {
            if (CheatsPlugin.Arts == null) return;

            // снять: галочку сняли, герой сменился (новый этаж) или настоящий артефакт надет
            for (int i = _applied.Count - 1; i >= 0; i--)
            {
                Applied a = _applied[i];
                if (a.Hero == null)
                {
                    _applied.RemoveAt(i);            // героя уже нет — снимать не с кого
                    continue;
                }
                bool mine = heroes != null && heroes.Contains(a.Hero);
                if (mine && CheatsPlugin.Arts[a.Index].Value && !RealOnHero(a.Hero, a.Index)) continue;
                Wear(a.Hero, a.Index, false);
                _applied.RemoveAt(i);
            }

            if (heroes == null) return;

            // надеть: отмечено галочкой, нами ещё не надето и настоящего такого на герое нет
            for (int h = 0; h < heroes.Count; h++)
            {
                Character hero = heroes[h];
                if (hero == null) continue;
                for (int i = 0; i < CheatsPlugin.Arts.Length; i++)
                {
                    if (!CheatsPlugin.Arts[i].Value) continue;
                    if (AlreadyOurs(hero, i) || RealOnHero(hero, i)) continue;
                    if (Wear(hero, i, true)) _applied.Add(new Applied(hero, i));
                }
            }
        }

        /// <summary>Снять всё наше: уход в сетевую игру, выход из игры.</summary>
        internal static void UndoAll()
        {
            for (int i = _applied.Count - 1; i >= 0; i--)
            {
                Applied a = _applied[i];
                if (a.Hero != null) Wear(a.Hero, a.Index, false);
            }
            _applied.Clear();
        }

        /// <summary>Носит ли этот артефакт по-настоящему хоть кто-то из своих героев.</summary>
        internal static bool RealOnAnyHero(List<Character> heroes, int index)
        {
            if (heroes == null) return false;
            for (int i = 0; i < heroes.Count; i++)
                if (heroes[i] != null && RealOnHero(heroes[i], index)) return true;
            return false;
        }

        private static bool RealOnHero(Character hero, int index)
        {
            SO_ItemTrash art = Load(index);
            if (art == null) return false;
            return SameArtifact(hero.Art1, art) || SameArtifact(hero.Art2, art)
                   || SameArtifact(hero.Art3, art);
        }

        /// <summary>
        /// Надетая вещь — тот же артефакт. Игра сравнивает по самому объекту свойства: копия вещи
        /// (ItemDrop.ItemCopy) делит свойства с исходным ассетом. Имя — запасная сверка.
        /// </summary>
        private static bool SameArtifact(SO_ItemTrash worn, SO_ItemTrash art)
        {
            if (worn == null) return false;
            if (worn.ArtEffects != null && worn.ArtEffects.Length > 0
                && art.ArtEffects != null && art.ArtEffects.Length > 0
                && worn.ArtEffects[0] == art.ArtEffects[0]) return true;
            return !string.IsNullOrEmpty(worn.Name) && worn.Name == art.Name;
        }

        private static bool AlreadyOurs(Character hero, int index)
        {
            for (int i = 0; i < _applied.Count; i++)
                if (_applied[i].Index == index && _applied[i].Hero == hero) return true;
            return false;
        }

        /// <summary>Отдать свойства артефакта игре: надеть или снять. false — артефакт не нашёлся.</summary>
        private static bool Wear(Character hero, int index, bool on)
        {
            SO_ItemTrash art = Load(index);
            if (art == null || art.ArtEffects == null || art.ArtEffects.Length == 0) return false;
            try
            {
                if (on) hero.EquipArtifact(art.ArtEffects);
                else hero.UnequipArtifact(art.ArtEffects);
            }
            catch (Exception ex)
            {
                // могло примениться наполовину. Всё равно отвечаем «надето»: иначе на следующем
                // такте мод надел бы это свойство вторым слоем, а они складываются
                CheatsPlugin.Log.LogError("Artifact " + ArtifactsTable.All[index].TitleEn
                                          + " (did the game update?): " + ex.Message);
            }
            return true;
        }

        /// <summary>Название артефакта — у самой игры, на её языке; не загрузился — из таблицы.</summary>
        internal static string Title(int index)
        {
            SO_ItemTrash art = Load(index);
            if (art != null)
            {
                try
                {
                    string t = art.InGameTitle;
                    if (!string.IsNullOrEmpty(t)) return t;
                }
                catch (Exception) { }
            }
            ArtifactInfo a = ArtifactsTable.All[index];
            return L.T(a.TitleRu, a.TitleEn);
        }

        /// <summary>
        /// Что артефакт делает — описание его свойств из перевода игры, с числами, как их ставит
        /// сама игра (ItemEffectContainer.Initialize(ItemEffect, string)): {0} — value свойства,
        /// {1}…{3} — числа его ступени из customLvls. Так описание — на любом языке игры.
        /// </summary>
        internal static string Note(int index)
        {
            SO_ItemTrash art = Load(index);
            List<string> parts = new List<string>();
            if (art != null && art.ArtEffects != null)
                foreach (ItemEffectArtifact e in art.ArtEffects)
                {
                    if (e == null) continue;
                    string d = Describe(e);
                    if (d.Length > 0) parts.Add(d);
                }
            if (parts.Count > 0) return string.Join(" ", parts.ToArray());
            return L.Ru ? ArtifactsTable.All[index].NoteRu : "";
        }

        private static string Describe(ItemEffectArtifact e)
        {
            string d;
            try { d = e.descriptionLocalized; }
            catch (Exception) { d = ""; }
            if (string.IsNullOrEmpty(d)) return "";
            d = d.Replace("{0}", e.value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (e.customLvls != null && e.customLvl >= 0 && e.customLvl < e.customLvls.Length
                && e.customLvls[e.customLvl] != null)
            {
                ItemEffectArtifact.ItemEffectLvl lv = e.customLvls[e.customLvl];
                d = d.Replace("{1}", L.Num(lv.value1, "0.##"))
                     .Replace("{2}", L.Num(lv.value2, "0.##"))
                     .Replace("{3}", L.Num(lv.value3, "0.##"));
            }
            return d;
        }

        /// <summary>Артефакт из Resources игры; о ненайденном жалуемся один раз на запуск.</summary>
        private static SO_ItemTrash Load(int index)
        {
            SO_ItemTrash art;
            // из кэша — только если объект жив: Unity могла выгрузить неиспользуемые ассеты
            if (_loaded.TryGetValue(index, out art) && art != null) return art;

            string path = ArtifactsTable.All[index].Path;
            art = Resources.Load(path, typeof(SO_ItemTrash)) as SO_ItemTrash;
            if (art == null)
            {
                // как DungeonSaving.ResourcesLoad_Item в игре: не нашлось по пути — пробуем имя файла
                int slash = path.LastIndexOf('/');
                if (slash >= 0)
                    art = Resources.Load(path.Substring(slash + 1), typeof(SO_ItemTrash)) as SO_ItemTrash;
            }

            if (art == null)
            {
                if (_complained == null) _complained = new bool[ArtifactsTable.All.Length];
                if (!_complained[index])
                {
                    _complained[index] = true;
                    CheatsPlugin.Log.LogWarning("Artifact " + ArtifactsTable.All[index].TitleEn
                        + " not found at " + path + " — did the game update?");
                }
                return null;
            }
            _loaded[index] = art;   // Dictionary: по тому же ключу перезапись, а не вторая запись
            return art;
        }
    }

    /// <summary>
    /// Негативные эффекты: что считать вредом и как снять уже наложенное.
    ///
    /// Вред — любой `Debuff`, кроме трёх видов, которыми игра помечает самого героя по его же
    /// механике, а не по злому умыслу: перезарядка адреналина и две метки некроманта. Остальные
    /// виды из `DebuffTypes` либо вредят герою (отравление, горение, оглушение, окаменение,
    /// замедление, безумие, печать маны, поломка вещи, запрет восстановления…), либо вообще
    /// ложатся не на героя, а на врагов — метки охотника, зловоние, устрашение, воскрешение
    /// монстра, — и до этой проверки не доходят: она смотрит только своих героев.
    /// </summary>
    internal static class Debuffs
    {
        private const int NecroLife = 7;
        private const int NecroRessurection = 8;
        private const int AdrenalineReload = 128;

        /// <summary>Этот эффект — вред, который стоит не пускать на своего героя.</summary>
        internal static bool Harmful(IEffect effect)
        {
            Debuff d = effect as Debuff;
            if (d == null) return false;                      // Buff — это хорошее, не мешаем
            int type = (int)d.debuffType;
            return type != NecroLife && type != NecroRessurection && type != AdrenalineReload;
        }

        /// <summary>
        /// Снять с героев уже наложенный вред. Снимаем так же, как это делает сама игра, когда у
        /// эффекта вышло время (`Creature.UpdateBuffs` → `RemoveEffect(эффект, 0)`), поэтому и
        /// прибавки откатятся, и значок пропадёт. Список копируем: снятие его меняет.
        /// </summary>
        internal static void Clear(List<Character> heroes)
        {
            if (heroes == null) return;
            for (int h = 0; h < heroes.Count; h++)
            {
                Character hero = heroes[h];
                if (hero == null || hero.buffsList == null || hero.buffsList.Count == 0) continue;
                IEffect[] copy = hero.buffsList.ToArray();
                for (int i = 0; i < copy.Length; i++)
                {
                    if (!Harmful(copy[i])) continue;
                    try { hero.RemoveEffect(copy[i], 0f); }
                    catch (Exception ex)
                    {
                        CheatsPlugin.Log.LogError("Removing an effect: " + ex.Message);
                        return;                               // не сыпать одно и то же каждый такт
                    }
                }
            }
        }
    }

    /// <summary>
    /// Правка данных героя из запущенной игры: золото, очки навыков, характеристики, навыки.
    ///
    /// ⚠️ Главное правило: **писать и в хранилище, и в живого героя.** Игра при каждом переходе
    /// (лестница, выход, смена места) зовёт `GameManager.SavePrefs` и перекладывает туда золото из
    /// `curMoney` и очки из `Creature.lvl` — то есть правка только хранилища была бы стёрта своими
    /// же числами игры через минуту. А герой, наоборот, забирает характеристики и навыки из
    /// хранилища один раз, когда создаётся, — поэтому они и подействуют со следующей вылазки.
    /// </summary>
    internal static class HeroEdit
    {
        /// <summary>Герой, которым играют сейчас.</summary>
        internal static int CurrentHero()
        {
            try { return Mathf.Clamp(FileBasedPrefs.GetInt("curChar", 0), 0, HeroesTable.All.Length - 1); }
            catch (Exception) { return 0; }
        }

        /// <summary>Герой этого класса среди живых; null — такого сейчас нет.</summary>
        private static Character Live(int hero)
        {
            List<Character> list = Players.List();
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && (int)list[i].charClass == hero) return list[i];
            return null;
        }

        private static void Flush()
        {
            try { PlayerPrefs.Save(); }
            catch (Exception ex) { CheatsPlugin.Log.LogWarning("The edit was not saved: " + ex.Message); }
        }

        // --- золото ---------------------------------------------------------------------

        internal static int Gold()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null) return gm.curMoney + gm.moneyMod;
            return FileBasedPrefs.GetInt(HeroesTable.GoldKey, 0);
        }

        internal static void SetGold(int value)
        {
            value = Mathf.Clamp(value, 0, HeroesTable.GoldMax);
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                // игра сохраняет сумму curMoney + moneyMod, поэтому копилку обнуляем
                gm.curMoney = value;
                gm.moneyMod = 0;
            }
            FileBasedPrefs.SetInt(HeroesTable.GoldKey, value);
            Flush();
        }

        // --- очки навыков (они же уровень героя) ----------------------------------------

        internal static int Points(int hero)
        {
            return FileBasedPrefs.GetInt(HeroesTable.PointsKey + hero, 0);
        }

        internal static void SetPoints(int hero, int value)
        {
            value = Mathf.Clamp(value, 0, HeroesTable.PointsMax);
            FileBasedPrefs.SetInt(HeroesTable.PointsKey + hero, value);
            Character c = Live(hero);
            if (c != null) c.lvl = value;   // иначе SavePrefs вернёт прежнее число
            Flush();
        }

        // --- характеристики -------------------------------------------------------------

        private static string AttrKey(int hero, int attr)
        {
            return HeroesTable.All[hero].ClassKey + HeroesTable.AttributeKeys[attr];
        }

        internal static int Attribute(int hero, int attr)
        {
            return FileBasedPrefs.GetInt(AttrKey(hero, attr), 0);
        }

        internal static void SetAttribute(int hero, int attr, int value)
        {
            value = Mathf.Clamp(value, 0, HeroesTable.AttributeMax);
            FileBasedPrefs.SetInt(AttrKey(hero, attr), value);
            Character c = Live(hero);
            if (c != null)
            {
                // живому герою меняем и сейчас: сам он перечитает хранилище только в новой вылазке
                if (attr == 0) c.Att_Endurance = value;
                else if (attr == 1) c.Att_Strength = value;
                else if (attr == 2) c.Att_Agility = value;
                else if (attr == 3) c.Att_Intelegence = value;
            }
            Flush();
        }
    }

    /// <summary>
    /// Снаряжение героя, которым играют сейчас. В отличие от программы-редактора, правятся не
    /// записи сохранения, а живые вещи в памяти игры — поэтому и видно только того героя, кто
    /// сейчас в игре: у остальных вещи лежат в сохранении, и в памяти их нет.
    ///
    /// Сохранять ничего не надо: когда игра дойдёт до своего сохранения, она соберёт запись из
    /// этих же объектов (`DungeonSaving.GetEffects`), то есть в своём формате и по своим правилам.
    /// </summary>
    internal static class GearEdit
    {
        private static readonly string[] SlotRu = new string[] { "Шлем", "Доспех", "Оружие", "Щит", "Кольцо 1", "Кольцо 2" };
        private static readonly string[] SlotEn = new string[] { "Helmet", "Armor", "Weapon", "Shield", "Ring 1", "Ring 2" };

        internal static string SlotTitle(int slot) { return L.T(SlotRu[slot], SlotEn[slot]); }

        internal const int SlotCount = 6;

        private static readonly string[] QualityRu = new string[] { "обычное", "особое", "древнее", "эпическое", "легендарное" };
        private static readonly string[] QualityEn = new string[] { "common", "unusual", "ancient", "epic", "legendary" };

        /// <summary>
        /// Название качества — из перевода игры (термины UI/Inventory/Rare0_It … Rare4_It: форма
        /// для «оно»), на её языке; своё — только если термина нет.
        /// </summary>
        internal static string QualityName(int q)
        {
            if (q >= 0 && q < QualityRu.Length)
                return L.Game("UI/Inventory/Rare" + q + "_It", QualityRu[q], QualityEn[q]);
            return L.T("качество ", "quality ") + q;
        }

        /// <summary>Герой, которым играют. null — в игре сейчас никого.</summary>
        internal static Character Hero()
        {
            List<Character> list = Players.List();
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) return list[i];
            return null;
        }

        internal static SO_ItemEquip Slot(Character c, int slot)
        {
            if (c == null) return null;
            SO_ItemEquip item = null;
            if (slot == 0) item = c.Head;
            else if (slot == 1) item = c.Body;
            else if (slot == 2) item = c.Weapon;
            else if (slot == 3) item = c.Shield;
            else if (slot == 4) item = c.Ring1;
            else if (slot == 5) item = c.Ring2;
            // пустой слот игра держит не как null, а как вещь без имени
            if (item == null || string.IsNullOrEmpty(item.Name)) return null;
            return item;
        }

        internal static List<SO_Item> Bag(Character c)
        {
            if (c == null || c.inventory == null) return new List<SO_Item>();
            return c.inventory;
        }

        internal static string Title(SO_Item item)
        {
            try
            {
                string t = item.InGameTitle;
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch (Exception) { }
            return item.Name;
        }

        /// <summary>
        /// Эффекты вещи. У каждого вида снаряжения свой список своего рода, общего у игры нет,
        /// поэтому разбираем по видам и сводим к общему предку.
        /// </summary>
        internal static List<ItemEffect> Effects(SO_ItemEquip item)
        {
            List<ItemEffect> res = new List<ItemEffect>();
            if (item == null) return res;
            SO_ItemHead head = item as SO_ItemHead;
            if (head != null && head.effects != null)
                foreach (ItemEffectArmor e in head.effects) if (e != null) res.Add(e);
            SO_ItemBody body = item as SO_ItemBody;
            if (body != null && body.effects != null)
                foreach (ItemEffectArmor e in body.effects) if (e != null) res.Add(e);
            SO_ItemShield shield = item as SO_ItemShield;
            if (shield != null)
            {
                if (shield.effects != null)
                    foreach (ItemEffectArmor e in shield.effects) if (e != null) res.Add(e);
                if (shield.effectsWeapon != null)
                    foreach (ItemEffectWeapon e in shield.effectsWeapon) if (e != null) res.Add(e);
            }
            SO_ItemWeapon weapon = item as SO_ItemWeapon;
            if (weapon != null && weapon.effects != null)
                foreach (ItemEffectWeapon e in weapon.effects) if (e != null) res.Add(e);
            SO_ItemRing ring = item as SO_ItemRing;
            if (ring != null && ring.RingResultEffects != null)
                foreach (ItemEffectArtifact e in ring.RingResultEffects) if (e != null) res.Add(e);
            return res;
        }

        /// <summary>Строка эффекта: название и ступень, как их понимает сама игра.</summary>
        internal static string EffectLine(SO_ItemEquip item, ItemEffect effect)
        {
            string title;
            try { title = effect.GetTitle; }
            catch (Exception) { title = effect.path; }
            if (string.IsNullOrEmpty(title)) title = effect.path;
            // у кольца ступень лежит в самом эффекте — так её сохраняет и накладывает игра
            ItemEffectArtifact art = effect as ItemEffectArtifact;
            if (item is SO_ItemRing && art != null) return title + L.T(" · ступень ", " · tier ") + (art.customLvl + 1);
            int lvl;
            if (item.TryGetEffectLevel(effect, out lvl)) return title + L.T(" · ступень ", " · tier ") + (lvl + 1);
            return title;
        }

        internal static List<string> Curses(SO_Item item)
        {
            List<string> res = new List<string>();
            if (item == null || item.curseEffects == null) return res;
            foreach (ItemEffectCurse c in item.curseEffects)
            {
                if (c == null) continue;
                string t;
                try { t = c.GetTitle; }
                catch (Exception) { t = c.path; }
                res.Add(string.IsNullOrEmpty(t) ? c.path : t);
            }
            return res;
        }

        /// <summary>Короткое описание вещи для строки-подсказки внизу окна.</summary>
        internal static string Describe(SO_ItemEquip item)
        {
            string s = Title(item) + " — " + QualityName((int)item.Quality);
            List<ItemEffect> eff = Effects(item);
            for (int i = 0; i < eff.Count; i++) s += (i == 0 ? ": " : ", ") + EffectLine(item, eff[i]);
            return s;
        }

        /// <summary>
        /// Сменить качество. ⚠️ Не через `ChangeQuality` игры: тот пересобирает вещь с нуля и
        /// сбрасывает эффекты, да и на уже созданной вещи сразу выходит. Пишем поле — тогда вещь
        /// остаётся своей, а игра сохранит новое качество вместе с ней.
        /// </summary>
        internal static void SetQuality(SO_ItemEquip item, int quality)
        {
            if (item == null) return;
            item.Quality = (Qualities)Mathf.Clamp(quality, 0, 4);
        }

        // --- эффекты ---------------------------------------------------------------------

        /// <summary>
        /// Игра вычищает эти эффекты при загрузке вещи (`SO_ItemEquip.effectsBlacklist`), так что
        /// предлагать их незачем: вещь их не удержит.
        /// </summary>
        private static readonly string[] Blacklist = new string[]
        {
            "AE CustomSkill Spec - Hide", "AE CustomSkill Spec - Summon Nightmare"
        };

        /// <summary>Сколько эффектов вещь удержит: качество плюс врождённые (правило игры).</summary>
        internal static int EffectNorm(SO_ItemEquip item)
        {
            return (int)item.Quality + Defaults(item).Count;
        }

        /// <summary>Врождённые эффекты — их вещь не теряет и убирать их нельзя.</summary>
        internal static List<ItemEffect> Defaults(SO_ItemEquip item)
        {
            List<ItemEffect> res = new List<ItemEffect>();
            SO_ItemHead head = item as SO_ItemHead;
            if (head != null && head.defaultEffects != null)
                foreach (ItemEffectArmor e in head.defaultEffects) if (e != null) res.Add(e);
            SO_ItemBody body = item as SO_ItemBody;
            if (body != null && body.defaultEffects != null)
                foreach (ItemEffectArmor e in body.defaultEffects) if (e != null) res.Add(e);
            SO_ItemShield shield = item as SO_ItemShield;
            if (shield != null && shield.defaultEffects != null)
                foreach (ItemEffectArmor e in shield.defaultEffects) if (e != null) res.Add(e);
            SO_ItemWeapon weapon = item as SO_ItemWeapon;
            if (weapon != null && weapon.defaultEffects != null)
                foreach (ItemEffectWeapon e in weapon.defaultEffects) if (e != null) res.Add(e);
            return res;
        }

        /// <summary>Пул вещи: что игра сама могла бы на неё выкинуть. Кольца сюда не входят.</summary>
        internal static List<ItemEffect> Pool(SO_ItemEquip item)
        {
            List<ItemEffect> res = new List<ItemEffect>();
            SO_ItemHead head = item as SO_ItemHead;
            if (head != null && head.allEffects != null)
                foreach (ItemEffectArmor e in head.allEffects) if (e != null) res.Add(e);
            SO_ItemBody body = item as SO_ItemBody;
            if (body != null && body.allEffects != null)
                foreach (ItemEffectArmor e in body.allEffects) if (e != null) res.Add(e);
            SO_ItemShield shield = item as SO_ItemShield;
            if (shield != null && shield.allEffects != null)
                foreach (ItemEffectArmor e in shield.allEffects) if (e != null) res.Add(e);
            SO_ItemWeapon weapon = item as SO_ItemWeapon;
            if (weapon != null && weapon.allPossibleEffects != null)
                foreach (ItemEffectWeapon e in weapon.allPossibleEffects) if (e != null) res.Add(e);

            // убрать чёрный список и то, что на вещи уже стоит
            List<ItemEffect> have = Effects(item);
            List<ItemEffect> ok = new List<ItemEffect>();
            for (int i = 0; i < res.Count; i++)
            {
                ItemEffect e = res[i];
                bool skip = false;
                for (int b = 0; b < Blacklist.Length; b++)
                    if (e.name == Blacklist[b]) skip = true;
                for (int h = 0; h < have.Count; h++)
                    if (have[h] == e) skip = true;
                for (int o = 0; o < ok.Count; o++)
                    if (ok[o] == e) skip = true;
                if (!skip) ok.Add(e);
            }
            return ok;
        }

        /// <summary>
        /// Сколько у эффекта ступеней. У колец своя таблица (`customLvls`), у брони и оружия —
        /// своя (`customLvlValues`). Нет ни той, ни другой — значит ступень одна.
        /// </summary>
        internal static int TierCount(ItemEffect e)
        {
            if (e == null) return 1;
            ItemEffectArtifact art = e as ItemEffectArtifact;
            if (art != null)
                return art.customLvls == null || art.customLvls.Length == 0 ? 1 : art.customLvls.Length;
            if (e.customLvlValues == null || e.customLvlValues.Length == 0) return 1;
            return e.customLvlValues.Length;
        }

        // --- правка на лету: вещь перенадевается -------------------------------------------

        /// <summary>
        /// Эффекты игра накладывает на героя в момент надевания вещи (`Character.EquipItem` — с той
        /// ступенью, что стоит на вещи) и снимает при снятии (`UnequipItem`). Поэтому надетую вещь
        /// снимаем до правки — игра снимет ровно то, что когда-то наложила, — и надеваем после:
        /// правка действует сразу. В слоты и сумку эти две функции не лезут. Кольца так не
        /// правим: ступень их эффекта игра хранит в самом эффекте (ItemEffectArtifact.customLvl).
        /// </summary>
        private static bool Unwear(Character hero, SO_ItemEquip item)
        {
            if (hero == null || item == null || !Worn(hero, item)) return false;
            try
            {
                hero.UnequipItem(item);
                return true;
            }
            catch (Exception ex)
            {
                CheatsPlugin.Log.LogWarning("Taking the item off for an edit: " + ex.Message);
                return false;
            }
        }

        private static void Wear(Character hero, SO_ItemEquip item, bool wasWorn)
        {
            if (!wasWorn) return;
            try { hero.EquipItem(item); }
            catch (Exception ex) { CheatsPlugin.Log.LogWarning("Putting the item back on after an edit: " + ex.Message); }
        }

        private static bool Worn(Character hero, SO_ItemEquip item)
        {
            for (int i = 0; i < SlotCount; i++) if (Slot(hero, i) == item) return true;
            return false;
        }

        internal static void LiveTier(Character hero, SO_ItemEquip item, ItemEffect e, int tier)
        {
            bool worn = Unwear(hero, item);
            SetTier(item, e, tier);
            Wear(hero, item, worn);
        }

        internal static void LiveRemove(Character hero, SO_ItemEquip item, ItemEffect e)
        {
            bool worn = Unwear(hero, item);
            RemoveEffect(item, e);
            Wear(hero, item, worn);
        }

        internal static void LiveAdd(Character hero, SO_ItemEquip item, ItemEffect e)
        {
            bool worn = Unwear(hero, item);
            AddEffect(item, e);
            Wear(hero, item, worn);
        }

        internal static void LiveQuality(Character hero, SO_ItemEquip item, int quality)
        {
            bool worn = Unwear(hero, item);
            SetQuality(item, quality);
            Wear(hero, item, worn);
        }

        // --- проклятия ---------------------------------------------------------------------

        /// <summary>
        /// Снять с вещи все проклятия. В самой игре способа снять проклятие нет вовсе. Живому
        /// герою их действие снимаем сразу (`Character.RemoveCurses`), иначе оно висело бы до
        /// снятия вещи.
        /// </summary>
        internal static void RemoveCurses(Character hero, SO_Item item)
        {
            if (item == null || item.curseEffects == null || item.curseEffects.Count == 0) return;
            if (hero != null)
            {
                try { hero.RemoveCurses(item.curseEffects); }
                catch (Exception ex) { CheatsPlugin.Log.LogWarning("Removing a curse: " + ex.Message); }
            }
            item.curseEffects.Clear();
            item.isCursed = false;
        }

        internal static int Tier(SO_ItemEquip item, ItemEffect e)
        {
            ItemEffectArtifact art = e as ItemEffectArtifact;
            if (item is SO_ItemRing && art != null) return art.customLvl;
            int lvl;
            if (item.TryGetEffectLevel(e, out lvl)) return lvl;
            return 0;
        }

        /// <summary>
        /// Что эффект делает — словами самой игры и с её же числом для этой ступени
        /// (`ItemEffect.GetDescription`). Свои пересказы не сочиняем: у игры описание уже есть,
        /// на том языке, на котором в неё играют.
        /// </summary>
        internal static string Description(ItemEffect e, int tier)
        {
            if (e == null) return "";
            try
            {
                string d = e.GetDescription(tier);
                return d == null ? "" : d;
            }
            catch (Exception) { return ""; }
        }

        internal static void SetTier(SO_ItemEquip item, ItemEffect e, int tier)
        {
            if (item == null || e == null) return;
            tier = Mathf.Clamp(tier, 0, TierCount(e) - 1);
            if (item.effectsLvl == null) item.effectsLvl = new ItemEffectIntDictionary();
            item.effectsLvl[e] = tier;
        }

        internal static bool IsDefault(SO_ItemEquip item, ItemEffect e)
        {
            List<ItemEffect> def = Defaults(item);
            for (int i = 0; i < def.Count; i++) if (def[i] == e) return true;
            return false;
        }

        internal static void RemoveEffect(SO_ItemEquip item, ItemEffect e)
        {
            if (item == null || e == null || IsDefault(item, e)) return;
            SO_ItemHead head = item as SO_ItemHead;
            if (head != null && head.effects != null) head.effects.Remove(e as ItemEffectArmor);
            SO_ItemBody body = item as SO_ItemBody;
            if (body != null && body.effects != null) body.effects.Remove(e as ItemEffectArmor);
            SO_ItemShield shield = item as SO_ItemShield;
            if (shield != null)
            {
                if (shield.effects != null) shield.effects.Remove(e as ItemEffectArmor);
                if (shield.effectsWeapon != null) shield.effectsWeapon.Remove(e as ItemEffectWeapon);
            }
            SO_ItemWeapon weapon = item as SO_ItemWeapon;
            if (weapon != null && weapon.effects != null) weapon.effects.Remove(e as ItemEffectWeapon);
            if (item.effectsLvl != null && item.effectsLvl.ContainsKey(e)) item.effectsLvl.Remove(e);
            // освободившаяся ячейка — как это считает сама игра
            item.emptySlotsCount = Mathf.Max(0, EffectNorm(item) - Effects(item).Count);
        }

        /// <summary>Добавить эффект из пула вещи, сразу высшей ступени — как делает кузница.</summary>
        internal static void AddEffect(SO_ItemEquip item, ItemEffect e)
        {
            if (item == null || e == null) return;
            if (Effects(item).Count >= EffectNorm(item)) return;
            ItemEffectArmor armor = e as ItemEffectArmor;
            ItemEffectWeapon wep = e as ItemEffectWeapon;
            SO_ItemHead head = item as SO_ItemHead;
            if (head != null && armor != null) head.effects.Add(armor);
            SO_ItemBody body = item as SO_ItemBody;
            if (body != null && armor != null) body.effects.Add(armor);
            SO_ItemShield shield = item as SO_ItemShield;
            if (shield != null && armor != null) shield.effects.Add(armor);
            if (shield != null && wep != null) shield.effectsWeapon.Add(wep);
            SO_ItemWeapon weapon = item as SO_ItemWeapon;
            if (weapon != null && wep != null) weapon.effects.Add(wep);
            SetTier(item, e, TierCount(e) - 1);
            item.emptySlotsCount = Mathf.Max(0, EffectNorm(item) - Effects(item).Count);
        }
    }

    /// <summary>Свои герои: список игроков GameManager; в сетевой игре — никто.</summary>
    internal static class Players
    {
        internal static bool Online()
        {
            try { return Controller.IsConnected; }
            catch (Exception) { return true; }   // не смогли понять — считаем сетевой, читы не трогаем
        }

        internal static List<Character> List()
        {
            GameManager gm = GameManager.Instance;
            return gm != null ? gm.playerList : null;
        }

        internal static bool Mine(Creature c)
        {
            Character ch = c as Character;
            if (ch == null || Online()) return false;
            List<Character> list = List();
            return list != null && list.Contains(ch);
        }

        /// <summary>Источник удара — свой герой: его контроллер (так передают и удары, и стрелы) или он сам.</summary>
        internal static bool FromMine(UnityEngine.Object source)
        {
            Controller ctrl = source as Controller;
            if (ctrl != null) return Mine(ctrl._creature);
            Creature cr = source as Creature;
            return cr != null && Mine(cr);
        }
    }

    // --- выживание ---------------------------------------------------------------------------

    /// <summary>Здоровье своего героя не уменьшается.</summary>
    [HarmonyPatch(typeof(Creature), "HP_cur", MethodType.Setter)]
    internal static class KeepHealth
    {
        private static void Prefix(Creature __instance, ref float value)
        {
            if (!CheatsPlugin.God.Value || value >= __instance.HP_cur) return;
            if (Players.Mine(__instance)) value = __instance.HP_cur;
        }
    }

    /// <summary>Удар по своему герою не доходит вовсе — как во время кувырка, только всегда.</summary>
    [HarmonyPatch(typeof(Creature), "CanReceiveDamage")]
    internal static class NoHits
    {
        private static bool Prefix(Creature __instance, ref bool __result)
        {
            if (!CheatsPlugin.God.Value || !Players.Mine(__instance)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Мана своего героя не уменьшается.</summary>
    [HarmonyPatch(typeof(Creature), "ST_cur", MethodType.Setter)]
    internal static class KeepMana
    {
        private static void Prefix(Creature __instance, ref float value)
        {
            if (!CheatsPlugin.Mana.Value || value >= __instance.ST_cur) return;
            if (Players.Mine(__instance)) value = __instance.ST_cur;
        }
    }

    /// <summary>
    /// Негативный эффект на своего героя не накладывается. `Creature.AddEffect` — единственная
    /// дверь и для хорошего, и для плохого: `Buff` пропускаем, `Debuff` своему герою — нет.
    /// Бессмертие тут не помогало: оно гасит урон, а отравление, замедление или печать маны
    /// приходят не уроном.
    /// </summary>
    [HarmonyPatch(typeof(Creature), "AddEffect",
        new Type[] { typeof(IEffect), typeof(bool), typeof(bool), typeof(float) })]
    internal static class NoDebuffs
    {
        private static bool Prefix(Creature __instance, IEffect __0)
        {
            if (!CheatsPlugin.NoDebuffs.Value || !Debuffs.Harmful(__0)) return true;
            return !Players.Mine(__instance);
        }
    }

    // --- параметры героя ------------------------------------------------------------------------

    /// <summary>Обзор: из него игра ставит размер камеры — множитель отдаляет камеру.</summary>
    [HarmonyPatch(typeof(Character), "FOV_normal", MethodType.Getter)]
    internal static class ViewNormal
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            if (CheatsPlugin.On(CheatsPlugin.View) && Players.Mine(__instance)) __result *= CheatsPlugin.View.Value;
        }
    }

    [HarmonyPatch(typeof(Character), "FOV_mod", MethodType.Getter)]
    internal static class ViewMod
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            if (CheatsPlugin.On(CheatsPlugin.View) && Players.Mine(__instance)) __result *= CheatsPlugin.View.Value;
        }
    }

    /// <summary>
    /// Скорость: Controller.Move ставит скорость тела заново при каждом шаге, если герою можно
    /// двигаться (canMove); иначе выходит, ничего не ставя. Умножаем только в первом случае —
    /// иначе прежняя скорость умножалась бы шаг за шагом.
    /// </summary>
    [HarmonyPatch(typeof(Controller), "Move", new Type[] { typeof(Vector3) })]
    internal static class RunSpeed
    {
        private static readonly FieldInfo Rigid = AccessTools.Field(typeof(Controller), "rigid");

        private static void Postfix(Controller __instance)
        {
            if (!CheatsPlugin.On(CheatsPlugin.Speed) || !__instance.canMove || Rigid == null) return;
            if (!Players.Mine(__instance._creature)) return;
            Rigidbody2D body = Rigid.GetValue(__instance) as Rigidbody2D;
            if (body != null) body.velocity = body.velocity * CheatsPlugin.Speed.Value;
        }
    }

    /// <summary>Броня своего героя.</summary>
    [HarmonyPatch(typeof(Character), "ArmorDefence", MethodType.Getter)]
    internal static class MoreArmor
    {
        private static void Postfix(Character __instance, ref ArmorStruct __result)
        {
            if (CheatsPlugin.On(CheatsPlugin.Armor) && Players.Mine(__instance)) __result = __result * CheatsPlugin.Armor.Value;
        }
    }

    /// <summary>Урон своего героя: на входе у врага, если источник удара — свой герой.</summary>
    [HarmonyPatch(typeof(Creature), "SetDamageRPC")]
    internal static class MoreDamage
    {
        private static void Prefix(Creature __instance, ref DamageStruct __0, UnityEngine.Object __3)
        {
            if (!CheatsPlugin.On(CheatsPlugin.Damage) || Players.Mine(__instance)) return;
            if (Players.FromMine(__3)) __0 = __0 * CheatsPlugin.Damage.Value;
        }
    }

    // --- добыча -----------------------------------------------------------------------------

    /// <summary>
    /// Золото. `GameManager.AddMoney` — одна дверь для всего: монеты с пола, продажа, разборка,
    /// награды. ⚠️ Через неё же идут траты, отрицательными суммами, поэтому множим только приход:
    /// иначе всё в лавке подорожало бы во столько же раз.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "AddMoney")]
    internal static class MoreGold
    {
        private static void Prefix(ref int __0)
        {
            if (!CheatsPlugin.On(CheatsPlugin.Gold) || __0 <= 0 || Players.Online()) return;
            __0 = Mult.Apply(__0, CheatsPlugin.Gold.Value);
        }
    }

    /// <summary>
    /// Опыт. `Character.AddXP` — одна дверь, и все свои множители (кольца, перки, башня,
    /// сложность) игра применяет уже внутри, так что наш ложится поверх них.
    /// </summary>
    [HarmonyPatch(typeof(Character), "AddXP")]
    internal static class MoreXp
    {
        private static void Prefix(Character __instance, ref int __0)
        {
            if (!CheatsPlugin.On(CheatsPlugin.Xp) || __0 <= 0) return;
            if (Players.Mine(__instance)) __0 = Mult.Apply(__0, CheatsPlugin.Xp.Value);
        }
    }

    /// <summary>
    /// Таблицы рекордов Steam. `GameManager.SendSteamScore` — единственное место, откуда игра
    /// выкладывает очки за забег в публичные таблицы (по таблице на локацию: башня, склеп,
    /// катакомбы, тюрьма…). Пока галочка стоит, отправки не происходит вовсе.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "SendSteamScore")]
    internal static class NoSteamScores
    {
        private static bool Prefix()
        {
            return !CheatsPlugin.NoLeaderboards.Value;
        }
    }

    /// <summary>Умножение с запасом: при ×100 и без того больших числах не уйти за предел int.</summary>
    internal static class Mult
    {
        internal static int Apply(int value, float factor)
        {
            double result = (double)value * factor;
            if (result > int.MaxValue) return int.MaxValue;
            return (int)result;
        }
    }
}
