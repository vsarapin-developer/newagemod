using System.Collections.Generic;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class ItemIds
    {
        internal static bool Market;

        internal static bool Enabled => Plugin.CfgItemIds == null || Plugin.CfgItemIds.Value;

        private static bool OutOfCombat(int subType) => subType == 16 || subType == 18 || subType == 22;

        private static readonly Dictionary<int, string> Sources = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> Ready = new Dictionary<int, string>();

        internal static string Decorate(string name, int thingId, int subType)
        {
            if (!Enabled || Market || thingId <= 0 || !OutOfCombat(subType)) return name;

            if (Sources.TryGetValue(thingId, out var was) && was == name
                && Ready.TryGetValue(thingId, out var done)) return done;

            string tail = " (" + thingId + ")";
            string result = string.IsNullOrEmpty(name) ? tail.Substring(1)
                          : name.EndsWith(tail, System.StringComparison.Ordinal) ? name
                          : name + tail;
            Sources[thingId] = name;
            Ready[thingId] = result;
            return result;
        }
    }

    [HarmonyPatch(typeof(GeneralThingInfoDescription), "Name", MethodType.Getter)]
    public static class ThingNameIdPatch
    {
        private static void Postfix(GeneralThingInfoDescription __instance, ref string __result)
        {
            __result = ItemIds.Decorate(__result, __instance.ThingId, (int)__instance.ThingSubType);
        }
    }

    [HarmonyPatch(typeof(ShopPanelContentResolver), "InternalActivatePanel")]
    public static class MarketPanelOpenPatch
    {
        private static void Postfix(ShopPanelContentResolver __instance, bool __result)
        {
            if (__result) ItemIds.Market = __instance is MarketPanelContentResolver;
        }
    }

    [HarmonyPatch(typeof(ShopPanelContentResolver), "InternalDeactivatePanel")]
    public static class MarketPanelClosePatch
    {
        private static void Postfix(bool __result)
        {
            if (__result) ItemIds.Market = false;
        }
    }
}
