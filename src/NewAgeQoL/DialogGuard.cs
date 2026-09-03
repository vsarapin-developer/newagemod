using System.Reflection;
using HarmonyLib;

namespace NewAgeQoL
{
    // Окно докупки за деньги спрашивает цену у сервера и обновляет себя из ответа по захваченной ссылке.
    // Если к приходу ответа окно уже закрыто, обновление падает на уничтоженной кнопке — и падает не само
    // по себе, а внутри NetworkConnection.Update, обрывая разбор входящих сообщений: клиент выглядит
    // зависшим, поверх экрана остаётся мёртвый модальный слой. Пропускаем такое обновление молча.
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
                    Plugin.Log?.LogInfo("[dialog] цена пришла в закрытое окно докупки — пропускаю");
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
                    Plugin.Log?.LogInfo("[dialog] кнопка окна докупки уже уничтожена — пропускаю обновление цены");
                    return false;
                }
                return true;
            }
            catch { return true; }
        }
    }
}
