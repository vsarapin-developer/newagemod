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
            Settings.Tick();
            Search.Tick();
        }
    }
}
