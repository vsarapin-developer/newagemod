using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Transport.Messages.Common.User;
using Transport.Messages.Requests.Things.Actions;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Shop;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Market
    {
        private const int PutOnMarket = 0x1000;
        private const int RemoveFromSale = 0x40;
        private const int WinPutOnMarket = -103;
        private const int WinSellerOffers = -107;
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

        private static readonly Dictionary<int, int> _offerThing = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<int>> _thingOffers = new Dictionary<int, List<int>>();
        private static int _removeLots = 1;
        private static int _removeCap = 1;
        private static bool _rDone = true;
        private static int _rTab;
        private static readonly List<int> _rQueue = new List<int>();

        internal static void Tick()
        {
            try
            {
                Listen();
                if (MarketAirCenterPatch.Pending && Time.unscaledTime - MarketAirCenterPatch.PendingAt > 1f)
                    MarketAirCenterPatch.Pending = false;
            }
            catch (Exception e) { Plugin.Log?.LogError("[рынок] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(416, OnResult);
            nc.RemoveMessageListener(102, OnOffers);
            nc.AddMessageListener(416, OnResult);
            nc.AddMessageListener(102, OnOffers);
            _on = nc;
        }

        private static void OnOffers(object m)
        {
            var msg = m as ShopTabContentResponseMessage;
            if (msg == null || msg.WindowId != WinSellerOffers || msg.Items == null) return;
            _offerThing.Clear();
            _thingOffers.Clear();
            foreach (var it in msg.Items)
            {
                if (it == null || it.ThingInfo == null) continue;
                int oid = it.ShopItemId.GetValueOrDefault();
                if (oid == 0) continue;
                int tid = it.ThingInfo.ThingId;
                _offerThing[oid] = tid;
                List<int> list;
                if (!_thingOffers.TryGetValue(tid, out list)) { list = new List<int>(); _thingOffers[tid] = list; }
                list.Add(oid);
            }
        }

        internal static void FixDecimal(PutOnMarketConfirmDialog dialog)
        {
            if (dialog == null) return;
            try
            {
                foreach (var fname in new[] { "TallPriceInputField", "GoldPriceInputField" })
                {
                    var cur = AccessTools.Field(typeof(PutOnMarketConfirmDialog), fname)?.GetValue(dialog) as PutOnMarketCurrencyInputField;
                    if (cur == null) continue;
                    var input = AccessTools.Field(typeof(PutOnMarketCurrencyInputField), "InputField")?.GetValue(cur) as InputField;
                    if (input == null) continue;
                    input.onValidateInput = (text, index, ch) =>
                    {
                        if (ch == ',') ch = '.';
                        return char.IsDigit(ch) || ch == '.' ? ch : '\0';
                    };
                    var field = input;
                    field.onValueChanged.AddListener(v =>
                    {
                        if (v != null && v.IndexOf(',') >= 0) field.SetTextWithoutNotify(v.Replace(',', '.'));
                    });
                }
            }
            catch (Exception e) { Plugin.Trace("[рынок] запятая в цене: " + e.Message); }
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
            if (!Enabled) return;
            try
            {
                var msg = request.GenerateMessage() as ThingContextActionRequestMessage;
                if (msg == null) return;

                if (_done && msg.ButtonId == PutOnMarket && msg.WindowId == WinPutOnMarket && _lots > 1)
                {
                    _id = msg.Id;
                    _tab = msg.TabId;
                    _quantity = msg.Quantity.GetValueOrDefault(1);
                    _talls = msg.Cash != null ? msg.Cash.Talls.GetValueOrDefault() : 0f;
                    _gold = msg.Cash != null ? msg.Cash.Gold.GetValueOrDefault() : 0f;
                    _single = msg.SingleLot.GetValueOrDefault();
                    _remaining = _lots - 1;
                    _done = false;
                    Air("Рынок: выставляю лотов: " + _lots);
                    Plugin.Trace("[рынок] лот 1/" + _lots + " ушёл, в очереди ещё " + _remaining);
                    return;
                }

                if (_rDone && msg.ButtonId == RemoveFromSale && msg.WindowId == WinSellerOffers && _removeLots > 1)
                {
                    _rTab = msg.TabId;
                    int tid;
                    if (!_offerThing.TryGetValue(msg.Id, out tid)) return;
                    List<int> all;
                    if (!_thingOffers.TryGetValue(tid, out all)) return;
                    _rQueue.Clear();
                    foreach (int oid in all)
                    {
                        if (oid == msg.Id) continue;
                        _rQueue.Add(oid);
                        if (_rQueue.Count >= _removeLots - 1) break;
                    }
                    _rDone = false;
                    Air("Рынок: снимаю лотов: " + _removeLots);
                    Plugin.Trace("[рынок] снятие 1/" + _removeLots + ", в очереди ещё " + _rQueue.Count);
                }
            }
            catch (Exception e) { Plugin.Trace("[рынок] исходящий: " + e.Message); }
        }

        private static void OnResult(object m)
        {
            var resp = m as ThingContextActionResponseMessage;
            if (resp == null) return;

            if (resp.WindowId == WinSellerOffers && resp.ButtonId == RemoveFromSale && resp.Success)
                DropOffer(resp.Id);

            if (!_done && resp.WindowId == WinPutOnMarket)
            {
                if (!resp.Success)
                {
                    Plugin.Trace("[рынок] сервер отказал, остановка: " + resp.ErrorMessage);
                    _done = true;
                    _remaining = 0;
                    Refresh(WinPutOnMarket, _tab);
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
                    catch (Exception e) { _ours = false; _done = true; _remaining = 0; Plugin.Log?.LogError("[рынок] отправка лота: " + e.Message); Refresh(WinPutOnMarket, _tab); }
                    return;
                }
                _done = true;
                Air("Рынок: выставлено лотов: " + _lots);
                Plugin.Trace("[рынок] все " + _lots + " лотов выставлены");
                Refresh(WinPutOnMarket, _tab);
                return;
            }

            if (!_rDone && resp.WindowId == WinSellerOffers)
            {
                if (!resp.Success)
                {
                    Plugin.Trace("[рынок] снятие: сервер отказал, остановка: " + resp.ErrorMessage);
                    _rDone = true;
                    _rQueue.Clear();
                    Refresh(WinSellerOffers, _rTab);
                    return;
                }
                if (_rQueue.Count > 0)
                {
                    int nextId = _rQueue[0];
                    _rQueue.RemoveAt(0);
                    try
                    {
                        _ours = true;
                        NetworkConnection.Instance.SendRequest(new ContextActionRequest(nextId, RemoveFromSale, WinSellerOffers, _rTab, null));
                        Plugin.Trace("[рынок] снят лот, в очереди ещё " + _rQueue.Count);
                    }
                    catch (Exception e) { _ours = false; _rDone = true; _rQueue.Clear(); Plugin.Log?.LogError("[рынок] снятие лота: " + e.Message); Refresh(WinSellerOffers, _rTab); }
                    return;
                }
                _rDone = true;
                Air("Рынок: снято лотов: " + _removeLots);
                Plugin.Trace("[рынок] снятие завершено");
                Refresh(WinSellerOffers, _rTab);
            }
        }

        private static void Air(string text)
        {
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<AirMessageScript>();
                MarketAirCenterPatch.Pending = existing == null;
                MarketAirCenterPatch.PendingAt = Time.unscaledTime;
                AirMessageScript.ShowInformationNotification(text);
                if (existing != null) Center(existing);
            }
            catch (Exception e) { Plugin.Trace("[рынок] сообщение: " + e.Message); }
        }

        internal static void Center(AirMessageScript air)
        {
            if (air == null) return;
            var rt = air.transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void DropOffer(int offerId)
        {
            int tid;
            if (!_offerThing.TryGetValue(offerId, out tid)) return;
            _offerThing.Remove(offerId);
            List<int> list;
            if (_thingOffers.TryGetValue(tid, out list))
            {
                list.Remove(offerId);
                if (list.Count == 0) _thingOffers.Remove(tid);
            }
        }

        private static void Refresh(int window, int tab)
        {
            try { NetworkConnection.Instance.SendRequest(new GetTabContentRequest(tab, window)); }
            catch (Exception e) { Plugin.Trace("[рынок] обновление вкладки: " + e.Message); }
        }

        internal static void SetupRemove(ThingHintDialog dialog)
        {
            _removeLots = 1;
            _removeCap = 1;
            _rDone = true;
            if (!Enabled || dialog == null) return;
            try
            {
                if (dialog.ContextWindow != (EThingContextWindow)WinSellerOffers) return;

                int offerId = (int)(AccessTools.Field(typeof(ThingHintDialog), "Id")?.GetValue(dialog) ?? 0);
                int tid;
                if (offerId == 0 || !_offerThing.TryGetValue(offerId, out tid)) return;
                List<int> all;
                int cap = _thingOffers.TryGetValue(tid, out all) ? all.Count : 1;
                if (cap <= 1) return;
                _removeCap = cap;

                var qty = AccessTools.Field(typeof(ThingHintDialog), "quantityInputField")?.GetValue(dialog) as IntegerInputField;
                if (qty == null) return;

                var go = UnityEngine.Object.Instantiate(qty.gameObject, qty.transform.parent);
                go.name = "QoLRemoveLotsField";
                go.transform.SetSiblingIndex(qty.transform.GetSiblingIndex() + 1);
                go.SetActive(true);
                var wrap = qty.transform.parent as RectTransform;
                if (wrap != null && !wrap.gameObject.activeSelf) wrap.gameObject.SetActive(true);

                var field = go.GetComponent<IntegerInputField>();
                field.OnValueChanged = new IntegerInputField.OnValueChangedEvent();
                field.OnValueChanged.AddListener(v => _removeLots = v < 1 ? 1 : (v > _removeCap ? _removeCap : v));
                field.Initialize("Снять лотов", 1, cap, 1);
                LayoutRebuilder.MarkLayoutForRebuild(qty.transform.parent as RectTransform);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[рынок] поле снятия не добавлено: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PutOnMarketConfirmDialog), "InitializeDialog")]
    public static class MarketDialogPatch
    {
        private static void Postfix(PutOnMarketConfirmDialog __instance)
        {
            Market.FixDecimal(__instance);
            Market.Setup(__instance);
        }
    }

    [HarmonyPatch(typeof(MarketProposalListItemRowItemRenderer), "UpdateView")]
    public static class MarketUnitPricePatch
    {
        private static void Postfix(MarketProposalListItemRowItemRenderer __instance)
        {
            try
            {
                var data = __instance.Data;
                if (data == null || data.Price == null || data.Quantity <= 1 || !data.SingleSlot) return;
                Unit(__instance, "TallPrice", data.Price.Talls, data.Quantity, 2);
                Unit(__instance, "GoldPrice", data.Price.Gold, data.Quantity, 4);
            }
            catch (Exception e) { Plugin.Trace("[рынок] цена за штуку: " + e.Message); }
        }

        private static void Unit(MarketProposalListItemRowItemRenderer r, string field, float? price, int qty, int digits)
        {
            if (!price.HasValue || price.Value <= 0f) return;
            var dp = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), field)?.GetValue(r) as DialogPrice;
            if (dp == null || dp.Text == null || !dp.gameObject.activeSelf) return;
            string per = (price.Value / qty).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
            var t = dp.Text;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.text = ResourceStrings.FloatToString(price.Value) + " <size=" + Mathf.Max(10, t.fontSize - 4) + ">(" + per + ")</size>";
        }
    }

    [HarmonyPatch(typeof(ThingHintDialog), "FillInventoryThingFields")]
    public static class MarketRemoveDialogPatch
    {
        private static void Postfix(ThingHintDialog __instance) => Market.SetupRemove(__instance);
    }

    [HarmonyPatch(typeof(AirMessageScript), "Start")]
    public static class MarketAirCenterPatch
    {
        internal static bool Pending;
        internal static float PendingAt;

        private static void Postfix(AirMessageScript __instance)
        {
            if (!Pending) return;
            Pending = false;
            Market.Center(__instance);
        }
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
