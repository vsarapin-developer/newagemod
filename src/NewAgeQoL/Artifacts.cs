using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Shop;
using Transport.Messages.Responses.Things.Thingtabs;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(LocationMapBuilder), "Build")]
    public static class ArtifactsMapPatch
    {
        private static void Postfix(LocationMap __result)
        {
            if (__result != null) Artifacts.LastMap = __result;
        }
    }

    internal static class Artifacts
    {
        private const int IlleniumLoc = 2, BankLoc = 21;
        private const int WinInventory = -104, WinBox = -100, WinByLocation = -105;
        private const int BtnDress = 1, BtnDressExtra = 2, BtnTakeOff = 4, BtnGetFromBox = 0x4000, BtnPutToBox = 0x8000;
        private const int ArtRarity = 4;

        internal static bool Busy;
        internal static bool Internal;
        internal static string Status = "";
        internal static float StatusAt;

        private struct Item
        {
            internal int Inv, Tab, ThingId, Qty, Rarity;
            internal string Name;
        }

        private static readonly List<Item> Scanned = new List<Item>();
        private static readonly List<InventoryWearResponseMessageItem> Worn = new List<InventoryWearResponseMessageItem>();
        private static readonly Dictionary<int, (bool Ok, string Err)> Ctx = new Dictionary<int, (bool, string)>();
        private static readonly HashSet<int> GotTabs = new HashSet<int>();
        private static List<int> _tabs;
        private static int _tabsWindow = int.MinValue;
        private static int _tabsAltWindow = int.MinValue;
        private static bool _gotWear;
        private static bool _listeners;

        private static string Saved
        {
            get { return Plugin.CfgStash != null ? Plugin.CfgStash.Value ?? "" : ""; }
            set { if (Plugin.CfgStash != null) Plugin.CfgStash.Value = value ?? ""; }
        }

        internal static bool HasStash => !string.IsNullOrEmpty(Saved.Trim());

        private static void Forget() => Saved = "";

        internal static void Stash()
        {
            if (Busy || Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(StashRoutine());
        }

        internal static void Restore()
        {
            if (Busy || Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(RestoreRoutine());
        }

        private static IEnumerator StashRoutine()
        {
            Busy = true;
            try
            {
                yield return Plugin.Instance.StartCoroutine(EnsureStorage());
                if (!AtStorage()) { Say("не дошёл до хранилища (см. лог)"); yield break; }

                Say("смотрю, что надето");
                yield return Plugin.Instance.StartCoroutine(ScanWear());
                var worn = Worn.Where(w => w.Rarity == ArtRarity).ToList();

                var memory = worn.Select(w => new Slot { ThingId = w.ThingId, SlotId = w.SlotId }).ToList();

                if (worn.Count > 0)
                {
                    Say("снимаю надетое: " + worn.Count);
                    yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                    int off = 0;
                    foreach (var w in worn)
                    {
                        yield return Plugin.Instance.StartCoroutine(
                            Act(w.InventoryId, BtnTakeOff, WinInventory, TabOf(w.InventoryId, 1), 1));
                        off++;
                        Step("снимаю надетое: " + off + " из " + worn.Count);
                    }
                    yield return Wait(0.6f);
                }

                Say("собираю артефакты из сумки");
                yield return Plugin.Instance.StartCoroutine(Scan(WinBox, 380));
                var art = Scanned.Where(i => i.Rarity == ArtRarity).ToList();
                if (art.Count == 0)
                {
                    if (memory.Count > 0) Remember(memory);
                    else Say("артефактных вещей не нашёл, старый список цел");
                    yield break;
                }

                Say("кладу в хранилище: " + art.Count);
                int done = 0;
                foreach (var it in art)
                {
                    if (!memory.Any(m => m.ThingId == it.ThingId))
                        memory.Add(new Slot { ThingId = it.ThingId, SlotId = 0 });
                    yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnPutToBox, WinBox, it.Tab, it.Qty));
                    done++;
                    Step("кладу в хранилище: " + done + " из " + art.Count);
                }

                foreach (var o in Recall())
                    if (!memory.Any(m => m.ThingId == o.ThingId && m.SlotId == o.SlotId))
                        memory.Add(o);

                Remember(memory);
                Say("сдал в хранилище: " + done);
            }
            finally { Busy = false; }
        }

        private static IEnumerator RestoreRoutine()
        {
            Busy = true;
            try
            {
                var memory = Recall();
                if (memory.Count == 0) { Say("список пуст — сначала сдай вещи"); yield break; }

                yield return Plugin.Instance.StartCoroutine(EnsureStorage());
                if (!AtStorage()) { Say("не дошёл до хранилища (см. лог)"); yield break; }

                Say("смотрю хранилище");
                yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                Plugin.Trace("[art] в хранилище видно " + Scanned.Count + ": "
                                    + string.Join(", ", Scanned.Select(i => i.ThingId + "/стак" + i.Inv + "/вкл" + i.Tab + "/шт" + i.Qty).ToArray()));

                var need = new Dictionary<int, int>();
                foreach (var m in memory)
                {
                    need.TryGetValue(m.ThingId, out int n);
                    need[m.ThingId] = n + 1;
                }

                var take = new List<Item>();
                foreach (var it in Scanned)
                {
                    if (!need.TryGetValue(it.ThingId, out int left) || left <= 0) continue;
                    int qty = it.Qty < left ? it.Qty : left;
                    need[it.ThingId] = left - qty;
                    var copy = it;
                    copy.Qty = qty;
                    take.Add(copy);
                }

                int done = 0;
                if (take.Count > 0)
                {
                    Say("забираю из хранилища: " + take.Count);
                    foreach (var it in take)
                    {
                        yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnGetFromBox, WinByLocation, it.Tab, it.Qty));
                        done++;
                        Step("забираю из хранилища: " + done + " из " + take.Count);
                    }
                    yield return Wait(0.8f);
                }
                else Say("в хранилище нет вещей из списка, смотрю сумку");

                yield return Plugin.Instance.StartCoroutine(ScanWear());
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));

                var dress = memory
                    .Where(m => m.SlotId > 0)
                    .Where(m => !Worn.Any(w => w.SlotId == m.SlotId && w.ThingId == m.ThingId))
                    .ToList();

                int back = 0;
                if (dress.Count > 0)
                {
                    var busySlots = Worn
                        .Where(w => dress.Any(d => d.SlotId == w.SlotId && d.ThingId != w.ThingId))
                        .ToList();

                    if (busySlots.Count > 0)
                    {
                        Say("освобождаю слоты: " + busySlots.Count);
                        foreach (var w in busySlots)
                            yield return Plugin.Instance.StartCoroutine(
                                Act(w.InventoryId, BtnTakeOff, WinInventory, TabOf(w.InventoryId, 1), 1));
                        yield return Wait(0.6f);
                        yield return Plugin.Instance.StartCoroutine(ScanWear());
                        yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                    }

                    Say("надеваю: " + dress.Count);
                    var used = new HashSet<int>();
                    foreach (var d in dress)
                    {
                        var found = Scanned.FirstOrDefault(i => i.ThingId == d.ThingId && !used.Contains(i.Inv));
                        if (found.Inv == 0)
                        {
                            Plugin.Log?.LogWarning("[art] в сумке нет вещи " + d.ThingId + " для слота " + d.SlotId);
                            continue;
                        }
                        used.Add(found.Inv);
                        int button = ExtraSlot(d.SlotId) ? BtnDressExtra : BtnDress;
                        yield return Plugin.Instance.StartCoroutine(Act(found.Inv, button, WinInventory, found.Tab, 1));
                        back++;
                        Step("надеваю: " + back + " из " + dress.Count);
                    }
                }

                yield return Wait(0.5f);
                yield return Plugin.Instance.StartCoroutine(ScanWear());
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));

                var wornLeft = Worn.Select(w => w.ThingId).ToList();
                var bagLeft = Scanned.Select(i => i.ThingId).ToList();
                var missed = new List<Slot>();
                foreach (var m in memory)
                {
                    bool ok = m.SlotId > 0 ? wornLeft.Remove(m.ThingId) : bagLeft.Remove(m.ThingId);
                    if (!ok) missed.Add(m);
                }

                if (missed.Count == 0)
                {
                    Forget();
                    Say("вернул " + memory.Count + ", надел " + back);
                }
                else
                {
                    Remember(missed);
                    Say("вернул " + (memory.Count - missed.Count) + ", осталось " + missed.Count
                        + " — нажми ещё раз");
                }
            }
            finally { Busy = false; }
        }

        private static IEnumerator EnsureStorage()
        {
            if (AtStorage()) yield break;

            if (Loc() != IlleniumLoc && Loc() != BankLoc)
            {
                Say("возвращаюсь в город");
                Send(new ReturnToIlleniumRequest(true));
                float t = 0f;
                while (t < 60f && Loc() != IlleniumLoc) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }

            if (Loc() == IlleniumLoc)
            {
                Say("иду в банк");
                Send(new ChangeMapRequest(BankLoc));
                float t = 0f;
                while (t < 60f && Loc() != BankLoc) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }

            if (Loc() == BankLoc)
            {
                int door = StorageDoor();
                if (door <= 0) { Say("в банке не нашёл дверь хранилища"); yield break; }
                Say("иду в хранилище");
                Send(new ChangeMapRequest(door));
                float t = 0f;
                while (t < 60f && Loc() != door) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }
        }

        private static bool AtStorage()
        {
            var map = LastMap;
            if (map == null || map.SceneObjects == null) return false;
            foreach (var so in map.SceneObjects)
                if (so != null && (so.ObjectType == EObjectType.PersonalStoragePut || so.ObjectType == EObjectType.PersonalStorageGet))
                    return true;
            return false;
        }

        internal static LocationMap LastMap;

        private static int StorageDoor()
        {
            var map = LastMap;
            if (map == null || map.SceneObjects == null) { Plugin.Log?.LogWarning("[art] карта банка не разобрана"); return 0; }
            int found = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var so in map.SceneObjects)
            {
                if (so == null || so.ObjectType != EObjectType.Link) continue;
                sb.Append("id=").Append(so.Id).Append(" sn=").Append(so.SceneObjectName).Append("; ");
                string n = (so.SceneObjectName ?? "") + " " + (so.Text ?? "");
                if (found == 0 && (Has(n, "safe") || Has(n, "storage") || Has(n, "vault") || Has(n, "box") || Has(n, "хран")))
                    found = so.Id;
            }
            Plugin.Trace("[art] двери локации " + Loc() + ": " + sb + "-> хранилище: " + (found > 0 ? found.ToString() : "не найдено"));
            return found;
        }

        private static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static IEnumerator Scan(int window, short response)
        {
            EnsureListeners();
            Scanned.Clear();
            GotTabs.Clear();
            _tabs = null;
            _tabsWindow = window;
            _tabsAltWindow = window == WinByLocation ? Loc() : window;

            Send(new GetThingsTabsRequest(window));
            float t = 0f;
            while (t < 5f && _tabs == null) { yield return null; t += Time.unscaledDeltaTime; }
            if (_tabs == null) yield break;

            var tabs = new List<int>(_tabs);
            foreach (int tab in tabs)
            {
                Send(new GetTabContentRequest(tab, window));
                yield return Wait(0.05f);
            }

            t = 0f;
            while (t < 5f && GotTabs.Count < tabs.Count) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator ScanWear()
        {
            EnsureListeners();
            Worn.Clear();
            _gotWear = false;
            Send(new InventoryWearRequest());
            float t = 0f;
            while (t < 5f && !_gotWear) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator Act(int inv, int button, int window, int tab, int qty)
        {
            lock (Ctx) Ctx.Remove(inv);
            Send(new ContextActionRequest(inv, button, window, tab, qty));
            float t = 0f;
            while (t < 5f)
            {
                bool has;
                lock (Ctx) has = Ctx.ContainsKey(inv);
                if (has) break;
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            lock (Ctx)
            {
                if (Ctx.TryGetValue(inv, out var r))
                    Plugin.Trace("[art] кнопка " + button + " стак " + inv + " окно " + window + " вкладка " + tab
                                        + " -> " + (r.Ok ? "ок" : "отказ: " + r.Err));
                else
                    Plugin.Log?.LogWarning("[art] кнопка " + button + " стак " + inv + " окно " + window + " вкладка " + tab
                                           + " -> сервер не ответил");
                Ctx.Remove(inv);
            }
            yield return Wait(0.12f);
        }

        private static int TabOf(int inv, int fallback)
        {
            foreach (var it in Scanned) if (it.Inv == inv) return it.Tab;
            return fallback;
        }

        private static void EnsureListeners()
        {
            if (_listeners) return;
            var nc = NetworkConnection.Instance;
            if (nc == null) return;
            nc.AddMessageListener(359, OnTabs);
            nc.AddMessageListener(380, OnBagTab);
            nc.AddMessageListener(381, OnShopTab);
            nc.AddMessageListener(412, OnWear);
            nc.AddMessageListener(416, OnAction);
            _listeners = true;
        }

        private static void OnTabs(object m)
        {
            if (m is ThingTypeTabsResponseMessage t && t.TabIds != null && (t.WindowId == _tabsWindow || t.WindowId == _tabsAltWindow))
                _tabs = t.TabIds.Distinct().ToList();
        }

        private static void OnBagTab(object m)
        {
            if (!(m is ThingTabInventoryResponseMessage inv) || inv.WindowId != _tabsWindow && inv.WindowId != _tabsAltWindow) return;
            GotTabs.Add(inv.TabNumber);
            if (inv.Things == null) return;
            foreach (var th in inv.Things)
            {
                if (th.Inventories == null) continue;
                foreach (var ii in th.Inventories)
                    Add(new Item { Inv = ii.InventoryId, Tab = inv.TabNumber, ThingId = th.ThingId, Qty = ii.Quantity, Rarity = th.Rarity, Name = th.Name ?? "" });
            }
        }

        private static void OnShopTab(object m)
        {
            if (!(m is ShopTabContentResponseMessage shop) || shop.WindowId != _tabsWindow && shop.WindowId != _tabsAltWindow) return;
            int tab = shop.TabId.GetValueOrDefault();
            GotTabs.Add(tab);
            if (shop.Items == null) return;
            foreach (var it in shop.Items)
            {
                if (it == null || it.ThingInfo == null) continue;
                Add(new Item
                {
                    Inv = it.ShopItemId.GetValueOrDefault(),
                    Tab = tab,
                    ThingId = it.ThingInfo.ThingId,
                    Qty = it.Quantity.GetValueOrDefault(1),
                    Rarity = it.ThingInfo.Rarity,
                    Name = it.ThingInfo.Name ?? "",
                });
            }
        }

        private static void Add(Item item)
        {
            if (item.Inv == 0 || item.Qty <= 0) return;
            foreach (var s in Scanned) if (s.Inv == item.Inv) return;
            Scanned.Add(item);
        }

        private static void OnWear(object m)
        {
            if (!(m is InventoryWearResponseMessage w)) return;
            Worn.Clear();
            if (w.Items != null) Worn.AddRange(w.Items);
            _gotWear = true;
        }

        private static void OnAction(object m)
        {
            if (m is ThingContextActionResponseMessage r)
                lock (Ctx) Ctx[r.Id] = (r.Success, r.ErrorMessage);
        }

        private struct Slot
        {
            internal int ThingId, SlotId;
        }

        private static void Remember(List<Slot> slots)
        {
            var parts = slots.Select(x => x.ThingId + ";" + x.SlotId).ToArray();
            Saved = string.Join(",", parts);
            Plugin.Trace("[art] запомнил " + parts.Length + ": " + Saved);
        }

        private static List<Slot> Recall()
        {
            var list = new List<Slot>();
            foreach (var pair in Saved.Split(','))
            {
                var parts = pair.Split(';');
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0].Trim(), out int id) || !int.TryParse(parts[1].Trim(), out int slot)) continue;
                list.Add(new Slot { ThingId = id, SlotId = slot });
            }
            return list;
        }

        private static bool ExtraSlot(int slot) => slot >= 19 && slot <= 24;

        private static void Send(BaseRequest request)
        {
            Internal = true;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(request);
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[art] " + e.Message); }
            finally { Internal = false; }
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static int Loc()
        {
            try
            {
                var ud = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                return ud != null ? ud.CurrentLocationId : -1;
            }
            catch { return -1; }
        }

        private static void Say(string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
            Plugin.Trace("[art] " + text + " (loc=" + Loc() + ")");
            try { AirMessageScript.ShowInformationNotification("Артефакты: " + text); } catch { }
        }

        internal static void Step(string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
        }
    }
}
