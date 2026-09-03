using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NewAgeQoL
{
    [BepInPlugin(Guid, "New Age QoL", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "newage.qol";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> CfgTownButton;
        internal static ConfigEntry<bool> CfgArtifactButtons;
        internal static ConfigEntry<bool> CfgContractsTab;
        internal static ConfigEntry<bool> CfgRecipeIcons;
        internal static ConfigEntry<bool> CfgSearch;
        internal static ConfigEntry<bool> CfgChatHighlight;
        internal static ConfigEntry<string> CfgStash;
        internal static ConfigEntry<int> CfgContractsTabId;
        internal static ConfigEntry<int> CfgContractsIcon;
        internal static ConfigEntry<string> CfgContractsIconImage;
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
        internal static ConfigEntry<bool> CfgItemIds;
        internal static ConfigEntry<bool> CfgInstantRestore;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            CfgTownButton = Config.Bind("Town", "ShowButton", true,
                "Показывать кнопку возврата в Иллениум слева внизу, над кнопкой сумки. В бою кнопка скрыта.");

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
                "В бою засчитывать любое ПОПОЛНЕНИЕ (жизнь, мана, энергия, заряды) сразу, как пришёл ответ сервера, не дожидаясь анимации: выпил расходник — можно тут же жать умение, приём или каст. Игра держит прибавку внутри очереди анимаций, и до её конца её же проверки считают, что ресурса ещё нет. Урон и любые списания не трогаются — они как были, по анимации.");

            CfgChatHighlight = Config.Bind("Chat", "HighlightOwnLines", true,
                "В системном логе целиком красить зелёным те строки, где встречается твой ник. Свои события видно сразу, чужие не отвлекают.");

            CfgStash = Config.Bind("Town", "StashedArtifacts", "",
                "Что лежит в хранилище после сдачи, в виде «вещь;слот» через запятую. Слот 0 значит, что вещь была в сумке. Заполняется и очищается кнопкой артефактов.");

            CfgSearch = Config.Bind("Inventory", "TabSearch", true,
                "Строка поиска под сеткой предметов. Фильтрует текущую вкладку сумки по названию, при переключении вкладки очищается.");

            CfgRecipeIcons = Config.Bind("Inventory", "RecipeResultIcons", true,
                "В сумке рисовать у рецепта иконку той вещи, которую он создаёт, вместо одинакового свитка. В левом верхнем углу клетки остаётся маленький значок рецепта.");
            CfgRecipeIconTabs = Config.Bind("Inventory", "RecipeIconTabs", "5,15,17,22",
                "Номера вкладок, где рецепт показывается иконкой создаваемой вещи.");

            CfgItemIds = Config.Bind("Inventory", "ShowThingIds", true,
                "У вещей, используемых ВНЕ боя (зелья, прочие внебоевые расходники, руны), дописывать к названию id в скобках: «Зелье здоровья (427)». Этот id вставляется в настройки банок. Боевые расходники, снаряжение и рецепты не трогаются, на рынке id не показывается.");

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

        private void Update()
        {
            SideButtons.Tick();
            Flasks.Tick();
            Settings.Tick();
            Search.Tick();
        }
    }
}
