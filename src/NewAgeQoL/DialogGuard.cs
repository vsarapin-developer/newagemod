using System.Reflection;
using HarmonyLib;

namespace NewAgeQoL
{

    [HarmonyPatch(typeof(PriceConfirmMessageBoxRefreshByNetwork), "UpdateByPriceMessage")]
    public static class PriceBoxGuardPatch
    {
        private static FieldInfo _okButton;
        private static bool _looked;

        private static bool Prefix(PriceConfirmMessageBoxRefreshByNetwork __instance)
        {
            try
            {
                if (__instance == null)
                {
                    Plugin.Trace("[dialog] цена пришла в закрытое окно докупки — пропускаю");
                    return false;
                }

                if (!_looked)
                {
                    _looked = true;
                    _okButton = AccessTools.Field(typeof(PriceConfirmMessageBoxRefreshByNetwork), "MbOkButton");
                }
                if (_okButton == null) return true;

                var button = _okButton.GetValue(__instance) as UnityEngine.Object;
                if (button == null)
                {
                    Plugin.Trace("[dialog] кнопка окна докупки уже уничтожена — пропускаю обновление цены");
                    return false;
                }
                return true;
            }
            catch { return true; }
        }
    }
}
