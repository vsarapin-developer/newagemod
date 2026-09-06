using System;
using HarmonyLib;
using Transport.Messages.Common.User;
using Transport.Messages.Requests.Things.Actions;
using Transport.Messages.Responses.Things.Actions;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Market
    {
        private const int PutOnMarket = 0x1000;
        private const int WinPutOnMarket = -103;
        private const int MaxLots = 999;

        private static bool Enabled => Plugin.CfgMarketMultiLot == null || Plugin.CfgMarketMultiLot.Value;

        private static object _on;
        private static int _lots = 1;
        private static int _remaining;
        private static bool _ours;
        private static bool _done = true;
        private static int _id, _tab, _quantity;
        private static float _talls, _gold;
        private static bool _single;

        internal static void Tick()
        {
            try { Listen(); }
            catch (Exception e) { Plugin.Log?.LogError("[рынок] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(416, OnResult);
            nc.AddMessageListener(416, OnResult);
            _on = nc;
        }

        internal static void Setup(PutOnMarketConfirmDialog dialog)
        {
            _lots = 1;
            _remaining = 0;
            _done = true;
            if (!Enabled || dialog == null) return;
            try
            {
                var qty = AccessTools.Field(typeof(PutOnMarketConfirmDialog), "QuantityInputField")?.GetValue(dialog) as IntegerInputField;
                if (qty == null) return;

                var row = qty.transform.parent;
                var rowGo = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
                rowGo.name = "QoLLotsRow";
                rowGo.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
                rowGo.SetActive(true);

                var field = rowGo.GetComponentInChildren<IntegerInputField>(true);
                if (field == null) { UnityEngine.Object.Destroy(rowGo); return; }

                foreach (Transform child in rowGo.transform)
                {
                    if (child == field.transform || field.transform.IsChildOf(child)) continue;
                    foreach (var g in child.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
                    foreach (var s in child.GetComponentsInChildren<Selectable>(true)) s.interactable = false;
                }

                _field = field;
                _qty = qty;
                _stock = Stock(qty);
                _field.OnValueChanged = new IntegerInputField.OnValueChangedEvent();
                _field.OnValueChanged.AddListener(OnLotsChanged);
                _field.Initialize("Лотов", 1, MaxByQuantity(), 1);

                var origLabel = AccessTools.Field(typeof(IntegerInputField), "LabelText")?.GetValue(qty) as Text;
                var newLabel = AccessTools.Field(typeof(IntegerInputField), "LabelText")?.GetValue(_field) as Text;
                if (origLabel != null && newLabel != null)
                {
                    float w = origLabel.preferredWidth;
                    if (w > 1f)
                    {
                        var le = newLabel.GetComponent<LayoutElement>() ?? newLabel.gameObject.AddComponent<LayoutElement>();
                        le.minWidth = w;
                        le.preferredWidth = w;
                        newLabel.alignment = origLabel.alignment;
                    }
                    var ort = (RectTransform)origLabel.transform;
                    var nrt = (RectTransform)newLabel.transform;
                    nrt.anchorMin = ort.anchorMin;
                    nrt.anchorMax = ort.anchorMax;
                    nrt.pivot = ort.pivot;
                    nrt.sizeDelta = ort.sizeDelta;
                    LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_field.transform);
                }

                qty.OnValueChanged.AddListener(OnQuantityChanged);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[рынок] поле лотов не добавлено: " + e.Message); }
        }

        private static IntegerInputField _field, _qty;
        private static int _stock;

        private static int Stock(IntegerInputField qty)
        {
            try
            {
                int max = (int)(AccessTools.Field(typeof(IntegerInputField), "_maxValue")?.GetValue(qty) ?? 0);
                return max > 0 ? max : 1;
            }
            catch { return 1; }
        }

        private static int CurrentQuantity()
        {
            try { return _qty != null ? (_qty.GetValue() ?? 1) : 1; }
            catch { return 1; }
        }

        private static int MaxByQuantity()
        {
            int q = CurrentQuantity();
            int max = q > 0 ? _stock / q : _stock;
            return max < 1 ? 1 : (max > MaxLots ? MaxLots : max);
        }

        private static void OnLotsChanged(int v)
        {
            int cap = MaxByQuantity();
            _lots = v < 1 ? 1 : (v > cap ? cap : v);
        }

        private static void OnQuantityChanged(int q)
        {
            if (_field == null) return;
            int cap = MaxByQuantity();
            if (_lots > cap) _lots = cap;
            try { _field.Initialize("Лотов", 1, cap, _lots < 1 ? 1 : _lots); }
            catch { }
        }

        internal static void Outgoing(BaseRequest request)
        {
            if (request == null) return;
            if (_ours) { _ours = false; return; }
            if (!Enabled || !_done) return;
            try
            {
                var msg = request.GenerateMessage() as ThingContextActionRequestMessage;
                if (msg == null || msg.ButtonId != PutOnMarket || msg.WindowId != WinPutOnMarket) return;
                if (_lots <= 1) return;

                _id = msg.Id;
                _tab = msg.TabId;
                _quantity = msg.Quantity.GetValueOrDefault(1);
                _talls = msg.Cash != null ? msg.Cash.Talls.GetValueOrDefault() : 0f;
                _gold = msg.Cash != null ? msg.Cash.Gold.GetValueOrDefault() : 0f;
                _single = msg.SingleLot.GetValueOrDefault();
                _remaining = _lots - 1;
                _done = false;
                Plugin.Trace("[рынок] лот 1/" + _lots + " ушёл, в очереди ещё " + _remaining);
            }
            catch (Exception e) { Plugin.Trace("[рынок] исходящий: " + e.Message); }
        }

        private static void OnResult(object m)
        {
            if (_done) return;
            var resp = m as ThingContextActionResponseMessage;
            if (resp == null || resp.WindowId != WinPutOnMarket) return;
            if (!resp.Success)
            {
                Plugin.Trace("[рынок] сервер отказал, остановка: " + resp.ErrorMessage);
                _done = true;
                _remaining = 0;
                Refresh();
                return;
            }

            if (_remaining > 0)
            {
                _remaining--;
                try
                {
                    _ours = true;
                    var req = new ContextActionRequest(_id, PutOnMarket, WinPutOnMarket, _tab, _quantity, _talls, _gold, _single, 0);
                    NetworkConnection.Instance.SendRequest(req);
                    Plugin.Trace("[рынок] лот " + (_lots - _remaining) + "/" + _lots + " ушёл");
                }
                catch (Exception e) { _ours = false; _done = true; _remaining = 0; Plugin.Log?.LogError("[рынок] отправка лота: " + e.Message); Refresh(); }
                return;
            }

            _done = true;
            Plugin.Trace("[рынок] все " + _lots + " лотов выставлены");
            Refresh();
        }

        private static void Refresh()
        {
            try { NetworkConnection.Instance.SendRequest(new GetTabContentRequest(_tab, WinPutOnMarket)); }
            catch (Exception e) { Plugin.Trace("[рынок] обновление вкладки: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PutOnMarketConfirmDialog), "InitializeDialog")]
    public static class MarketDialogPatch
    {
        private static void Postfix(PutOnMarketConfirmDialog __instance) => Market.Setup(__instance);
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[] { typeof(BaseRequest) })]
    public static class MarketOutPatch
    {
        private static void Prefix(BaseRequest request) => Market.Outgoing(request);
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[]
    {
        typeof(ResponseCallbackContext), typeof(BaseRequest), typeof(short), typeof(int), typeof(MessageHandler)
    })]
    public static class MarketOutCallbackPatch
    {
        private static void Prefix(BaseRequest request) => Market.Outgoing(request);
    }
}
