using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NewAgeQoL
{
    [BepInPlugin(Guid, "New Age QoL", "1.7.6")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "newage.qol";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> CfgTownButton;
        internal static ConfigEntry<bool> CfgArtifactButtons;
        internal static ConfigEntry<bool> CfgTownTournament;
        internal static ConfigEntry<int> CfgTownArenaId;
        internal static ConfigEntry<int> CfgTownTournamentId;
        internal static ConfigEntry<string> CfgTownArenaWords;
        internal static ConfigEntry<string> CfgTownTournamentWords;
        internal static ConfigEntry<bool> CfgContractsTab;
        internal static ConfigEntry<bool> CfgContractNumbers;
        internal static ConfigEntry<bool> CfgRecipeIcons;
        internal static ConfigEntry<bool> CfgSearch;
        internal static ConfigEntry<bool> CfgChatHighlight;
        internal static ConfigEntry<bool> CfgHideSystemBoxes;
        internal static ConfigEntry<string> CfgStash;
        internal static ConfigEntry<int> CfgContractsTabId;
        internal static ConfigEntry<int> CfgContractsIcon;
        internal static ConfigEntry<string> CfgContractsIconImage;
        internal static ConfigEntry<string> CfgContractCache;
        internal static ConfigEntry<string> CfgRecipeIconTabs;
        internal static ConfigEntry<bool> CfgFlaskButtons;
        internal static ConfigEntry<bool> CfgFlaskFillToMax;
        internal static ConfigEntry<int> CfgFlaskHpId;
        internal static ConfigEntry<int> CfgFlaskManaId;
        internal static ConfigEntry<int> CfgFlaskEnergyId;
        internal static ConfigEntry<int> CfgFlaskMushroomId;
        internal static ConfigEntry<string> CfgFlaskHpName;
        internal static ConfigEntry<string> CfgFlaskManaName;
        internal static ConfigEntry<string> CfgFlaskEnergyName;
        internal static ConfigEntry<string> CfgFlaskMushroomName;
        internal static ConfigEntry<bool> CfgSlotSwap;
        internal static ConfigEntry<bool> CfgInstantRestore;
        internal static ConfigEntry<bool> CfgCounterAuto;
        internal static ConfigEntry<bool> CfgCounterRefresh;
        internal static ConfigEntry<int> CfgCounterRefreshRounds;
        internal static ConfigEntry<int> CfgCounterId;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<bool> CfgMarketMultiLot;
        internal static ConfigEntry<bool> CfgOnlineButton;
        internal static ConfigEntry<string> CfgOnlineLogin;
        internal static ConfigEntry<string> CfgOnlinePassword;
        internal static ConfigEntry<string> CfgOnlineVersion;
        internal static ConfigEntry<string> CfgOnlineClanCache;
        internal static ConfigEntry<int> CfgLastCharacter;
        internal static ConfigEntry<bool> CfgTravelButton;
        internal static ConfigEntry<string> CfgTravelSpots;
        internal static ConfigEntry<string> CfgTravelGates;
        internal static ConfigEntry<int> CfgTravelTown;
        internal static ConfigEntry<int> CfgTravelOuter;
        internal static ConfigEntry<bool> CfgMapLabels;
        internal static ConfigEntry<bool> CfgMapLabelType;
        internal static ConfigEntry<bool> CfgMapLabelBillboard;
        internal static ConfigEntry<float> CfgMapLabelSize;
        internal static ConfigEntry<int> CfgMapLabelFont;
        internal static ConfigEntry<float> CfgMapLabelLift;
        internal static ConfigEntry<string> CfgMapLabelColor;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            CfgTownButton = Config.Bind("Town", "ShowButton", true,
                "Показывать кнопку возврата в Иллениум слева внизу, над кнопкой сумки. В бою кнопка скрыта.");

            CfgTownTournament = Config.Bind("Town", "ButtonGoesToTournament", false,
                "Кнопка возврата ведёт дальше города: сама зайдёт на арену и оттуда к турнирам. Это поведение ТОЛЬКО у кнопки: артефакты и поход, когда им нужен город, возвращаются просто в город.");
            CfgTownArenaId = Config.Bind("Town", "ArenaDoorId", 0,
                "id двери арены в городе. 0 — искать дверь по слову из ArenaDoorWords.");
            CfgTownTournamentId = Config.Bind("Town", "TournamentDoorId", 0,
                "id двери турниров на арене. 0 — искать дверь по слову из TournamentDoorWords.");
            CfgTownArenaWords = Config.Bind("Town", "ArenaDoorWords", "aren,арен",
                "Слова, по которым мод узнаёт дверь арены среди дверей города (через запятую, регистр не важен). Если не нашлась, полный список дверей с их id пишется в лог.");
            CfgTownTournamentWords = Config.Bind("Town", "TournamentDoorWords", "tourn,турнир",
                "Слова, по которым мод узнаёт дверь турниров на арене. Если не нашлась, список дверей с их id пишется в лог.");

            CfgArtifactButtons = Config.Bind("Town", "ArtifactButtons", true,
                "Показывать над кнопкой города кнопку артефактов. Пока вещи на руках, она сдаёт их в хранилище, после этого меняет картинку и забирает обратно. Мод сам доходит до банка и хранилища, пропуская пройденные шаги.");

            CfgContractsTab = Config.Bind("Inventory", "ContractsTab", true,
                "Отдельная вкладка «Контракты» в сумке. Из остальных вкладок контракты убираются, кроме вкладки «Все». Выключение возвращает сумку к обычному виду.");
            CfgContractsTabId = Config.Bind("Inventory", "ContractsTabNumber", 40,
                "Номер вкладки «Контракты». Должен быть свободным: сервер шлёт номера 1…23.");
            CfgContractsIcon = Config.Bind("Inventory", "ContractsTabIcon", 16,
                "Запасная картинка вкладки «Контракты» — номер чужой вкладки, у которой её одолжить, пока мод не увидел ни одного контракта.");
            CfgContractsIconImage = Config.Bind("Inventory", "ContractsTabIconImage", "",
                "Картинка предмета-контракта для вкладки. Заполняется сама при первом увиденном контракте.");
            CfgContractNumbers = Config.Bind("Inventory", "ContractNumbers", true,
                "Писать номер контракта или договора прямо посреди его клетки: «Контракт 217» → 217, «Договор 20» → 20. Работает в любой вкладке, где предмет лежит, и не зависит от того, включена ли отдельная вкладка «Контракты».");
            CfgContractNumbers.SettingChanged += (s, e) => ContractNumbers.SyncAll();
            CfgContractCache = Config.Bind("Inventory", "ContractNumberCache", "",
                "Узнанные номера контрактов в виде «id:номер» через запятую. Заполняется сама и нужна, чтобы вкладка «Контракты» сразу открывалась по порядку, не дожидаясь названий с сервера.");

            CfgFlaskButtons = Config.Bind("Flasks", "ShowButtons", true,
                "Показывать справа от кнопок города и артефактов столбик банок: жизнь, мана, энергия, грибы. Кнопка появляется только у той банки, которой задан предмет — id или название. В бою столбик скрыт: это ВНЕбоевые банки.");
            CfgFlaskFillToMax = Config.Bind("Flasks", "FillToMax", true,
                "ВКЛ: жизнь/мана/энергия — пить до ПОЛНОГО (после каждого глотка мод ждёт обновления полосы и останавливается, как только она заполнилась); грибы — пока сервер даёт, но не больше 20 штук за нажатие. ВЫКЛ: любая кнопка использует ровно ОДНУ штуку за нажатие.");
            CfgFlaskHpId = Config.Bind("Flasks", "HpThingId", 0,
                "thingId внебоевой банки ЖИЗНИ. 0 = смотреть на HpThingName; если пусто и там — кнопки нет.");
            CfgFlaskManaId = Config.Bind("Flasks", "ManaThingId", 0,
                "thingId внебоевой банки МАНЫ. 0 = смотреть на ManaThingName.");
            CfgFlaskEnergyId = Config.Bind("Flasks", "EnergyThingId", 0,
                "thingId внебоевой банки ЭНЕРГИИ. 0 = смотреть на EnergyThingName.");
            CfgFlaskMushroomId = Config.Bind("Flasks", "MushroomThingId", 0,
                "thingId грибов (пополнение зарядов). 0 = смотреть на MushroomThingName.");
            CfgFlaskHpName = Config.Bind("Flasks", "HpThingName", "",
                "Название банки ЖИЗНИ, если id неизвестен: мод сам найдёт её среди расходников в сумке (можно часть названия, регистр не важен). Работает, только когда HpThingId = 0.");
            CfgFlaskManaName = Config.Bind("Flasks", "ManaThingName", "",
                "Название банки МАНЫ, если id неизвестен. Работает, только когда ManaThingId = 0.");
            CfgFlaskEnergyName = Config.Bind("Flasks", "EnergyThingName", "",
                "Название банки ЭНЕРГИИ, если id неизвестен. Работает, только когда EnergyThingId = 0.");
            CfgFlaskMushroomName = Config.Bind("Flasks", "MushroomThingName", "",
                "Название грибов, если id неизвестен. Работает, только когда MushroomThingId = 0.");

            CfgInstantRestore = Config.Bind("Combat", "InstantRestore", true,
                "В бою засчитывать пополнение ОТ РАСХОДНИКОВ (жизнь, мана, энергия, заряды) сразу, как пришёл ответ сервера, не дожидаясь анимации: выпил банку — можно тут же жать умение, приём или каст. Игра держит прибавку внутри очереди анимаций, и до её конца её же проверки считают, что ресурса ещё нет. Прибавки другого происхождения — вампиризм, исцеление, регенерация — идут своим чередом, по анимации, как в обычной игре. Урон и любые списания не трогаются вовсе.");

            CfgCounterAuto = Config.Bind("Combat", "CounterOnPlayers", false,
                "Ставить контрприём на себя, когда среди врагов есть живой ИГРОК (хаотические бои, арена, нападение в мире): мобов это не касается, приёмами бьют только игроки. Заряды проверяются ОДИН раз, в начале боя: не хватало их на старте — в этом бою мод больше не лезет, даже если заряды потом пополнить. Тогда первый контрприём за тобой, руками.");
            CfgCounterRefresh = Config.Bind("Combat", "CounterRefresh", false,
                "Обновлять контрприём на себе в начале раунда. Счёт идёт не «каждый N-й раунд боя», а от ПОСЛЕДНЕГО применения приёма НА СЕБЯ — своего или сделанного модом: применился в 4-м раунде при интервале 2 — следующий в 6-м, нажал сам в 7-м — следующий в 9-м. Контрприёмы, поставленные на союзника, и чужие контрприёмы в счёт не идут: мод следит только за своим персонажем. Первого контрприёма обновление не делает никогда: пока приёма не было, обновлять нечего.");
            CfgCounterRefreshRounds = Config.Bind("Combat", "CounterRefreshRounds", 3,
                "Через сколько раундов после последнего контрприёма ставить следующий. Меньше 1 считается за 1.");
            CfgCounterId = Config.Bind("Combat", "CounterDodgeId", 4,
                "id приёма «Контрприем» среди приёмов. Менять не нужно: 4 — его номер в игре. Если приёма с этим номером в бою нет, мод ищет его по названию.");

            CfgTravelButton = Config.Bind("Travel", "ShowButton", true,
                "Кнопка похода со списком точек: мод сам вернётся в город, выйдет во внешний мир и доведёт до выбранной точки. Дорогу видно строкой рядом с кнопкой, повторное нажатие пункта останавливает поход.");
            CfgTravelSpots = Config.Bind("Travel", "Spots",
                "Скорпионы@1002:19, Баньши@1002:52, Сокрушители@1002:38, Тигры@1002:21, Зомби@1002:51, Ифриты@1002:34, "
                + "Рыцари смерти@1003:47, Наги@1003:63, Сатиры@1003:31, "
                + "Дриады@1005:19, Миносы@1005:57, Личи@1005:41",
                "Пункты списка: «название@участок:вершина» через запятую, в том порядке, в каком они нужны. Номер вершины — тот же, что мод подписывает на карте, а участок нужен потому, что номера у участков свои. Дорогу на другой участок мод прокладывает сам по разделу Gates. Участок можно и не писать («Руины:60») — тогда мод ищет вершину на той карте, где стоишь.");
            CfgTravelGates = Config.Bind("Travel", "Gates",
                "1002>1003:64, 1003>1002:15, 1002>1011:79, 1011>1002:0, 1011>1005:35, 1005>1011:0",
                "Переходы между участками: «откуда>куда:вершина». По ним мод сам прокладывает дорогу к нужному участку, хоть через несколько переходов, и подтверждает каждый за игрока. 1002>1011:79 — с внешнего мира на соседний участок через v79, обратно — через v0.");

            Upgrade(CfgTravelSpots,
                "Скорпионы:19, Баньши:52, Сокрушители:38, Тигры:21, Зомби:51, Ифриты:34",
                "Скорпионы@1002:19, Баньши@1002:52, Сокрушители@1002:38, Тигры@1002:21, Зомби@1002:51, Ифриты@1002:34, "
                + "Рыцари смерти@1003:47, Наги@1003:63, Сатиры@1003:31");
            Upgrade(CfgTravelGates, "1002>1003:64, 1003>1002:15");
            CfgTravelTown = Config.Bind("Travel", "TownLocation", 2,
                "id города, из которого начинается поход. 2 — Иллениум.");
            CfgTravelOuter = Config.Bind("Travel", "OuterWorldLocation", 1002,
                "id участка карты, куда выводит выход из этого города. 1002 — внешний мир вокруг Иллениума.");

            CfgMapLabels = Config.Bind("Map", "VertexLabels", false,
                "Подписывать точки внешнего мира их номером: «v12». По номеру видно, куда ведёт дорога, им удобно объяснять маршрут другим и задавать точки в списке похода.");
            CfgMapLabelType = Config.Bind("Map", "VertexLabelType", false,
                "Дописывать к номеру, что это за точка: «бой», «переход» (сохранение, вход в город или на соседний участок) или «дорога».");
            CfgMapLabelBillboard = Config.Bind("Map", "VertexLabelBillboard", true,
                "Держать метку повёрнутой к камере, чтобы она читалась при любом наклоне карты.");
            CfgMapLabelSize = Config.Bind("Map", "VertexLabelSize", 0.08f,
                "Размер буквы метки в мировых единицах. Больше — крупнее подпись на карте.");
            CfgMapLabelFont = Config.Bind("Map", "VertexLabelSharpness", 48,
                "Разрешение шрифта метки. Влияет на чёткость, а не на размер.");
            CfgMapLabelLift = Config.Bind("Map", "VertexLabelLift", 0.7f,
                "Насколько поднять метку над точкой, чтобы она не легла на саму иконку.");
            CfgMapLabelColor = Config.Bind("Map", "VertexLabelColor", "#FFEE00",
                "Цвет метки в виде #RRGGBB.");

            Config.SettingChanged += (s, e) =>
            {
                if (e.ChangedSetting == null) return;
                if (e.ChangedSetting.Definition.Section == "Map") MapLabels.Refresh();
            };

            CfgChatHighlight = Config.Bind("Chat", "HighlightOwnLines", true,
                "В системном логе целиком красить зелёным те строки, где встречается твой ник. Свои события видно сразу, чужие не отвлекают.");

            CfgHideSystemBoxes = Config.Bind("Chat", "HideSystemMessageBoxes", false,
                "Не показывать всплывающее окно «Системное сообщение» — объявления администрации и разведки посреди экрана. Сам текст никуда не девается: он приходит в чат, вкладка «Общий». Письма, которые ждут при входе в игру, окном показываются по-прежнему.");

            CfgStash = Config.Bind("Town", "StashedArtifacts", "",
                "Что лежит в хранилище после сдачи, в виде «вещь;слот» через запятую. Слот 0 значит, что вещь была в сумке. Заполняется и очищается кнопкой артефактов.");

            CfgSearch = Config.Bind("Inventory", "TabSearch", true,
                "Строка поиска под сеткой предметов. Фильтрует текущую вкладку сумки по названию, при переключении вкладки очищается.");

            CfgRecipeIcons = Config.Bind("Inventory", "RecipeResultIcons", true,
                "В сумке рисовать у рецепта иконку той вещи, которую он создаёт, вместо одинакового свитка. В левом верхнем углу клетки остаётся маленький значок рецепта.");
            CfgRecipeIconTabs = Config.Bind("Inventory", "RecipeIconTabs", "5,15,17,22",
                "Номера вкладок, где рецепт показывается иконкой создаваемой вещи.");

            CfgSlotSwap = Config.Bind("Inventory", "SlotSwapButtons", true,
                "В окне снаряжения кнопки «⇄» у оружия и реликвий: меняют местами надетое с тем, что лежит в запасных слотах. То же, что приёмы «Смена оружия» и «Смена реликвий» в бою, только вне боя и без перетаскивания.");

            CfgMarketMultiLot = Config.Bind("Market", "MultiLot", true,
                "В штатном окне выставления вещи на рынок добавляет поле «Лотов»: сколько одинаковых лотов выставить подряд по заданной цене. 1 — как обычно.");
            CfgOnlineButton = Config.Bind("Online", "Button", true,
                "Кнопка «Кто в игре» в боковой панели: полный список игроков онлайн, как в старом 2D-клиенте. Список сервер отдаёт только старому протоколу, а вход по нему выбивает свою же сессию, поэтому мод заходит ЗАПАСНЫМ аккаунтом: подключается им, забирает список, отключается. Запасной персонаж на пару секунд появляется в мире.");
            CfgOnlineLogin = Config.Bind("Online", "Login", "",
                "Логин запасного аккаунта для списка «Кто в игре». Пусто — кнопка подскажет, что настроить.");
            CfgOnlinePassword = Config.Bind("Online", "Password", "",
                "Пароль запасного аккаунта. Хранится в этом файле открытым текстом.");
            CfgOnlineVersion = Config.Bind("Online", "FlashVersion", "11073",
                "Номер версии старого 2D-клиента, который мод называет серверу при входе запасным аккаунтом. Менять только если сервер отвечает «Обновите версию игры».");
            CfgOnlineClanCache = Config.Bind("Online", "ClanIconCache", "",
                "Узнанные коды значков кланов для окна «Кто в игре» в виде «значок:код» через запятую. Заполняется само, чтобы значки появлялись сразу.");
            CfgLastCharacter = Config.Bind("Launch", "LastCharacter", 0,
                "id персонажа, которым ты в последний раз входил в игру через этот клиент. Заполняется само. На экране выбора персонажа мод сразу показывает его, а не того, кто заходил последним по данным сервера (например, запасного для окна «Кто в игре»). 0 — как в игре.");
            CfgVerbose = Config.Bind("Log", "Verbose", false,
                "Писать в лог подробности работы: что нажато, что найдено в сумке, какие окна перестроены. По умолчанию ВЫКЛ — в логе остаются только ошибки и строчка о загрузке. Включай, если нужно показать, что происходит, при разборе проблемы.");

            try
            {
                var harmony = new Harmony(Guid);
                foreach (var t in typeof(Plugin).Assembly.GetTypes())
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    try { harmony.CreateClassProcessor(t).Patch(); }
                    catch (System.Exception e) { Log.LogError("Harmony: " + t.Name + " — " + e.Message); }
                }
            }
            catch (System.Exception e) { Log.LogError("Harmony: " + e); }

            Log.LogInfo("New Age QoL загружен.");
        }

        private static void Upgrade(ConfigEntry<string> cfg, params string[] wasBefore)
        {
            if (cfg == null) return;
            foreach (var old in wasBefore)
                if (cfg.Value == old) { cfg.Value = (string)cfg.DefaultValue; return; }
        }

        internal static void Trace(string text)
        {
            if (CfgVerbose != null && CfgVerbose.Value) Log?.LogInfo(text);
        }

        private void Update()
        {
            SideButtons.Tick();
            Flasks.Tick();
            Settings.Tick();
            SlotSwap.Tick();
            ContractNumbers.Tick();
            Search.Tick();
            Counter.Tick();
            Market.Tick();
            FlaskPicker.Tick();
            OnlineList.Tick();
            OnlineWindow.Tick();
        }
    }
}
