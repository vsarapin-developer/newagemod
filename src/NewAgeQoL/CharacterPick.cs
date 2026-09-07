using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Authentication;

namespace NewAgeQoL
{
    internal static class CharacterPick
    {
        internal static void Remember(int userId)
        {
            if (userId <= 0 || Plugin.CfgLastCharacter == null) return;
            Plugin.CfgLastCharacter.Value = userId;
            Plugin.Trace("[персонаж] запомнен id " + userId);
        }

        internal static void Preselect(CharacterSelectorComponent selector, AvailableCharactersResponseMessage message)
        {
            try
            {
                int want = Plugin.CfgLastCharacter?.Value ?? 0;
                if (want <= 0 || selector == null || message?.Items == null) return;
                int index = -1;
                for (int i = 0; i < message.Items.Count; i++)
                    if (message.Items[i] != null && message.Items[i].UserId == want) { index = i; break; }
                if (index <= 0) return;
                AccessTools.Field(typeof(CharacterSelectorComponent), "_selectedIndex")?.SetValue(selector, index);
                AccessTools.Method(typeof(CharacterSelectorComponent), "UpdateCurrentUserWidget")?.Invoke(selector, null);
                Plugin.Trace("[персонаж] на экране выбора показан " + message.Items[index].Login);
            }
            catch (Exception e) { Plugin.Trace("[персонаж] выбор: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CharacterSelectorComponent), "UpdateCharacters")]
    public static class CharacterPickListPatch
    {
        private static void Postfix(CharacterSelectorComponent __instance, AvailableCharactersResponseMessage message) =>
            CharacterPick.Preselect(__instance, message);
    }

    [HarmonyPatch(typeof(CharacterSelectorComponent), "FireCharacterSelected")]
    public static class CharacterPickEnterPatch
    {
        private static void Prefix(int __0) => CharacterPick.Remember(__0);
    }
}
