using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(TabPanel), "SetTabs")]
    public static class TabPanelRebuildPatch
    {
        private static AccessTools.FieldRef<TabPanel, IList<TabButton>> _buttons;
        private static AccessTools.FieldRef<TabPanel, int> _scroll;
        private static MethodInfo _unhook;
        private static bool _looked;

        private static void Prefix(TabPanel __instance)
        {
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    _buttons = AccessTools.FieldRefAccess<TabPanel, IList<TabButton>>("_tabButtons");
                    _scroll = AccessTools.FieldRefAccess<TabPanel, int>("_scroll");
                    _unhook = AccessTools.Method(typeof(TabPanel), "RemoveEventHandlers");
                }
                if (_buttons == null) return;

                var old = _buttons(__instance);
                if (old == null || old.Count == 0) return;

                _unhook?.Invoke(__instance, null);
                foreach (var button in old)
                    if (button != null) Object.Destroy(button.gameObject);
                foreach (var arrow in __instance.GetComponentsInChildren<TabScrollButton>(true))
                    if (arrow != null) Object.Destroy(arrow.gameObject);
                old.Clear();
                if (_scroll != null) _scroll(__instance) = 0;
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[tabs] " + e.Message); }
        }
    }
}
