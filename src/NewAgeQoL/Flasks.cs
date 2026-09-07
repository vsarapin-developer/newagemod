using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Flasks
    {
        internal const int Rows = 4;

        private const int WinInventory = -104;
        private const int AllTab = 18;
        private const int BtnUse = 0x20;
        private const float CacheAge = 20f;
        private const float RescanEvery = 180f;
        private const float TickEvery = 0.2f;
        private const int FillLimit = 50;
        private const int NameLimit = 60;
        private const int Batch = 20;
        private const float Pause = 0.15f;

        private static readonly string[] Titles = { "Жизнь", "Мана", "Энергия", "Грибы" };

        private struct Stack
        {
            internal int Inv, Tab, ThingId, Qty, SubType;
        }

        private static readonly List<Stack> Scanned = new List<Stack>();
        private static readonly HashSet<int> GotTabs = new HashSet<int>();
        private static readonly Dictionary<int, (bool Ok, string Err, int Left)> Ctx = new Dictionary<int, (bool, string, int)>();
        private static readonly Dictionary<int, string> Images = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static readonly HashSet<int> AskedInfo = new HashSet<int>();
        private static readonly HashSet<string> Loading = new HashSet<string>();

        private static readonly int[] Left = { -1, -1, -1, -1 };
        private static readonly int[] Resolved = new int[Rows];
        private static readonly int[] WatchedId = new int[Rows];
        private static readonly string[] WatchedWish = new string[Rows];
        private static readonly bool[] RowBusy = new bool[Rows];
        private static readonly int[] RowThing = new int[Rows];
        private static readonly string[] RowMsg = new string[Rows];

        private static string _scene = "";
        private static float _nextTick;
        private static List<int> _tabs;
        private static int _seen;
        private static bool _listeners, _scanBusy, _scanOk, _wasCombat;
        private static float _scanAt = -999f, _nextScan;

        internal static string Status = "";
        internal static float StatusAt;
        internal static int StatusRow = -1;

        internal static bool Enabled => Plugin.CfgFlaskButtons == null || Plugin.CfgFlaskButtons.Value;

        internal static bool AnyBusy
        {
            get
            {
                for (int row = 0; row < Rows; row++) if (RowBusy[row]) return true;
                return false;
            }
        }

        private static ConfigEntry<int> Cfg(int row)
        {
            switch (row)
            {
                case 0: return Plugin.CfgFlaskHpId;
                case 1: return Plugin.CfgFlaskManaId;
                case 2: return Plugin.CfgFlaskEnergyId;
                case 3: return Plugin.CfgFlaskMushroomId;
            }
            return null;
        }

        internal static int Id(int row)
        {
            var cfg = row >= 0 && row < Rows ? Cfg(row) : null;
            return cfg != null ? cfg.Value : 0;
        }

        private static ConfigEntry<string> CfgName(int row)
        {
            switch (row)
            {
                case 0: return Plugin.CfgFlaskHpName;
                case 1: return Plugin.CfgFlaskManaName;
                case 2: return Plugin.CfgFlaskEnergyName;
                case 3: return Plugin.CfgFlaskMushroomName;
            }
            return null;
        }

        private static string Wish(int row)
        {
            var cfg = row >= 0 && row < Rows ? CfgName(row) : null;
            return cfg != null ? (cfg.Value ?? "").Trim() : "";
        }

        internal static int Thing(int row)
        {
            if (row < 0 || row >= Rows) return 0;
            int id = Id(row);
            return id > 0 ? id : Resolved[row];
        }

        internal static bool Shown(int row) => Enabled && (Id(row) > 0 || Wish(row).Length > 0);

        internal static bool Busy(int row) => row >= 0 && row < Rows && RowBusy[row];

        private static readonly int[] BadgeOf = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private static readonly string[] BadgeText = new string[Rows];

        internal static string Badge(int row)
        {
            if (!Shown(row)) return "";
            int left = Thing(row) <= 0 ? -1 : Left[row];
            if (BadgeOf[row] != left)
            {
                BadgeOf[row] = left;
                BadgeText[row] = left < 0 ? "?" : left.ToString();
            }
            return BadgeText[row];
        }

        internal static string Hint(int row)
        {
            if (!Shown(row)) return "";
            if (RowBusy[row]) return Titles[row] + ": " + (RowMsg[row] ?? "…");

            int id = Thing(row);
            if (id <= 0) return Titles[row] + ": «" + Wish(row) + "» в сумке не найдено";

            string name = Plain(NameOf(id), id);
            string head = !string.IsNullOrEmpty(name) ? name : Titles[row];
            bool wantFill = Plugin.CfgFlaskFillToMax == null || Plugin.CfgFlaskFillToMax.Value;
            return head + " — " + (wantFill ? "до полного" : "не до полного");
        }

        internal static Sprite Icon(int row) => IconFor(Thing(row));

        internal static Sprite IconFor(int id)
        {
            if (id <= 0) return Fallback();

            string image;
            lock (Images) image = Images.TryGetValue(id, out var img) ? img : null;
            if (string.IsNullOrEmpty(image)) { AskInfo(id); return Fallback(); }

            if (Icons.TryGetValue(image, out var known))
            {
                if (known != null && known.texture != null) return known;
                Icons.Remove(image);
                Loading.Remove(image);
            }
            try
            {
                var s = AtlasUtils.GetThingSprite(image);
                if (s != null && s.name != "unknown") { Icons[image] = s; return s; }
            }
            catch { }
            LoadRemote(image);
            return Fallback();
        }

        internal static List<int> ConsumableThings()
        {
            var seen = new HashSet<int>();
            var list = new List<int>();
            lock (Scanned)
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && s.Qty > 0 && seen.Add(s.ThingId))
                        list.Add(s.ThingId);
            return list;
        }

        internal static int QtyOf(int thingId)
        {
            int q = 0;
            lock (Scanned) foreach (var s in Scanned) if (s.ThingId == thingId) q += s.Qty;
            return q;
        }

        internal static string DisplayName(int thingId)
        {
            string n = NameOf(thingId);
            return string.IsNullOrEmpty(n) ? null : Plain(n, thingId);
        }

        internal static void RequestScan()
        {
            _nextScan = 0f;
        }

        internal static void RequestNames()
        {
            List<int> ids;
            lock (Scanned)
            {
                ids = new List<int>();
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && s.Qty > 0 && NameOf(s.ThingId) == null)
                        ids.Add(s.ThingId);
            }
            foreach (int id in ids) AskInfo(id);
        }

        private static Sprite Fallback()
        {
            try { return AtlasUtils.GetThingTabImage(EThingTabType.NON_COMBAT_USED_TAB); }
            catch { return null; }
        }

        private static void LoadRemote(string image)
        {
            if (string.IsNullOrEmpty(image) || !Loading.Add(image)) return;
            try
            {
                RemoteImageLoader.Instance.Load(
                    "https://files.nura.biz/site/images/things100x100/" + image + ".png",
                    sprite => { if (sprite != null) Icons[image] = sprite; },
                    error => Plugin.Log?.LogWarning("[flasks] картинка «" + image + "» не загрузилась: " + error));
            }
            catch { }
        }

        private static void AskInfo(int thingId)
        {
            if (thingId <= 0) return;
            lock (AskedInfo) if (AskedInfo.Contains(thingId)) return;
            if (!EnsureListeners()) return;
            if (!Send(new GeneralThingHintRequest(thingId))) return;
            lock (AskedInfo) AskedInfo.Add(thingId);
        }

        private static void Learn(int thingId, string image, string name)
        {
            if (thingId <= 0) return;
            if (!string.IsNullOrEmpty(image)) lock (Images) Images[thingId] = image;
            if (!string.IsNullOrEmpty(name)) lock (Names) Names[thingId] = name;
        }

        private static string Plain(string name, int thingId)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string tail = " (" + thingId + ")";
            return name.EndsWith(tail, System.StringComparison.Ordinal)
                ? name.Substring(0, name.Length - tail.Length)
                : name;
        }

        private static string NameOf(int thingId)
        {
            lock (Names) return Names.TryGetValue(thingId, out var n) && !string.IsNullOrEmpty(n) ? n : null;
        }

        private static bool Consumable(int subType) => subType == 16 || subType == 18 || subType == 54;

        private static void ResolveWishes()
        {
            for (int row = 0; row < Rows; row++)
            {
                int id = Id(row);
                if (id > 0) { Resolved[row] = id; continue; }
                string want = Wish(row);
                Resolved[row] = want.Length > 0 ? Find(want) : 0;
            }
        }

        private static int Find(string want)
        {
            int loose = 0;
            lock (Scanned)
                foreach (var s in Scanned)
                {
                    string name = NameOf(s.ThingId);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, want, System.StringComparison.OrdinalIgnoreCase)) return s.ThingId;
                    if (loose == 0 && name.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) loose = s.ThingId;
                }
            return loose;
        }

        private static bool NeedNames()
        {
            for (int row = 0; row < Rows; row++)
                if (Id(row) <= 0 && Wish(row).Length > 0) return true;
            return false;
        }

        private static IEnumerator NameConsumables()
        {
            if (!NeedNames()) yield break;

            var need = new List<int>();
            lock (Scanned)
                foreach (var s in Scanned)
                    if (Consumable(s.SubType) && NameOf(s.ThingId) == null && !need.Contains(s.ThingId))
                        need.Add(s.ThingId);
            if (need.Count == 0) yield break;
            if (!EnsureListeners()) yield break;

            int asked = 0;
            foreach (int id in need)
            {
                if (asked++ >= NameLimit) break;
                if (!Send(new GeneralThingHintRequest(id))) yield break;
                yield return Wait(0.06f);
            }
            yield return Wait(0.4f);
        }

        internal static void DropIcons()
        {
            Icons.Clear();
            Loading.Clear();
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickEvery;

            string scene = SideButtons.Scene();
            if (scene != _scene)
            {
                _scene = scene;
                DropIcons();
            }

            bool combat = SideButtons.InCombat();
            if (_wasCombat && !combat) Forget();
            _wasCombat = combat;

            for (int row = 0; row < Rows; row++)
            {
                int id = Id(row);
                string wish = Wish(row);
                if (WatchedId[row] == id && WatchedWish[row] == wish) continue;
                WatchedId[row] = id;
                WatchedWish[row] = wish;
                Left[row] = -1;
                Resolved[row] = 0;
                Forget();
            }

            if (Plugin.Instance == null || combat || !Enabled || !SideButtons.InWorld()) return;
            if (_scanBusy || AnyBusy || Time.unscaledTime < _nextScan) return;

            bool any = false;
            for (int row = 0; row < Rows; row++) if (Shown(row)) any = true;
            if (!any) return;

            if (BagOpen()) { _nextScan = Time.unscaledTime + 5f; return; }

            _nextScan = Time.unscaledTime + (_scanOk ? RescanEvery : 3f);
            Plugin.Instance.StartCoroutine(RefreshRoutine());
        }

        private static bool BagOpen()
        {
            try { return Object.FindObjectOfType<InventoryPanelContent>() != null; }
            catch { return false; }
        }

        private static void Forget()
        {
            _scanOk = false;
            _scanAt = -999f;
            _nextScan = 0f;
        }

        private static IEnumerator RefreshRoutine()
        {
            if (!Connected()) yield break;
            yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
            yield return Plugin.Instance.StartCoroutine(NameConsumables());
            ResolveWishes();
            UpdateCounts();
        }

        internal static void Use(int row)
        {
            if (Plugin.Instance == null || row < 0 || row >= Rows) return;
            if (RowBusy[row] || !Shown(row)) return;
            Plugin.Instance.StartCoroutine(UseRoutine(row));
        }

        private static IEnumerator UseRoutine(int row)
        {
            int thingId = Thing(row);
            string title = Titles[row];
            if (thingId <= 0 && Wish(row).Length == 0) yield break;
            for (int i = 0; i < Rows; i++)
                if (i != row && RowBusy[i] && thingId > 0 && RowThing[i] == thingId)
                { Say(row, title + ": этот предмет уже использует соседняя кнопка"); yield break; }

            RowBusy[row] = true;
            RowThing[row] = thingId;
            try
            {
                if (SideButtons.InCombat()) { Say(row, title + ": в бою эти банки не пьются"); yield break; }
                if (!Connected()) { Say(row, title + ": нет соединения"); yield break; }

                Step(row, "…");
                bool cached = Time.unscaledTime - _scanAt <= CacheAge;
                float scannedAt = _scanAt;
                yield return Plugin.Instance.StartCoroutine(EnsureScan(CacheAge));
                if (_scanAt != scannedAt) UpdateCounts();
                if (thingId <= 0)
                {
                    yield return Plugin.Instance.StartCoroutine(NameConsumables());
                    ResolveWishes();
                    thingId = Thing(row);
                    RowThing[row] = thingId;
                    if (thingId <= 0)
                    { Say(row, title + ": «" + Wish(row) + "» в сумке не найдено"); yield break; }
                }
                var stacks = Stacks(thingId);

                if (stacks.Count == 0 && cached)
                {
                    cached = false;
                    Step(row, "смотрю сумку");
                    yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
                    stacks = Stacks(thingId);
                    UpdateCounts();
                }
                if (stacks.Count == 0)
                {
                    Say(row, _seen == 0
                        ? title + ": сумка ещё не пришла, попробуй через пару секунд"
                        : title + ": предмета id " + thingId + " в сумке нет");
                    yield break;
                }

                float pause = Pause;
                bool wantFill = Plugin.CfgFlaskFillToMax == null || Plugin.CfgFlaskFillToMax.Value;

                int cur = 0, max = 0;
                bool fill = wantFill && Gauge(row, out cur, out max);
                int count = !wantFill ? 1 : fill ? FillLimit : Batch;
                if (fill && cur >= max) { Say(row, title + ": уже полное — " + cur + " / " + max); yield break; }

                int used = 0, si = 0, q = 0;
                bool stopped = false, rescanned = false;

                while (used < count && !stopped && si < stacks.Count)
                {
                    var st = stacks[si];
                    if (q >= st.Qty) { si++; q = 0; continue; }
                    q++;

                    Take(st.Inv, out _, out _, out _);
                    if (!Send(new ContextActionRequest(st.Inv, BtnUse, WinInventory, st.Tab, 1)))
                    { Say(row, title + ": нет соединения"); yield break; }

                    float waited = 0f;
                    bool ok = false, got = false;
                    string err = null;
                    int left = -1;
                    while (waited < 3f && !(got = Take(st.Inv, out ok, out err, out left)))
                    { yield return null; waited += Time.unscaledDeltaTime; }

                    if (!got || !ok)
                    {
                        if (used == 0 && cached && !rescanned)
                        {
                            rescanned = true;
                            cached = false;
                            Step(row, "смотрю сумку");
                            yield return Plugin.Instance.StartCoroutine(EnsureScan(0f));
                            stacks = Stacks(thingId);
                            UpdateCounts();
                            si = 0; q = 0;
                            continue;
                        }
                        stopped = true;
                        string why = !got ? "сервер не ответил"
                                   : string.IsNullOrEmpty(err) ? "дальше сервер не даёт, похоже уже полное" : err;
                        Say(row, title + ": использовано " + used + ", " + why);
                        break;
                    }

                    used++;
                    if (left >= 0 && stacks.Count == 1) Left[row] = left;
                    else if (Left[row] > 0) Left[row]--;
                    Step(row, fill ? used.ToString() : used + " / " + count);

                    if (fill)
                    {
                        float w = 0f;
                        int c2 = cur, m2 = max;
                        while (w < 1.5f)
                        {
                            if (Gauge(row, out c2, out m2) && c2 != cur) break;
                            yield return null;
                            w += Time.unscaledDeltaTime;
                        }
                        cur = c2; max = m2;
                        if (cur >= max)
                        {
                            Say(row, title + ": до полного — " + used + " шт. (" + cur + " / " + max + ")");
                            yield break;
                        }
                    }

                    if (used < count) yield return Wait(Random.Range(pause * 0.8f, pause * 1.2f));
                }

                if (!stopped)
                    Say(row, fill
                        ? title + ": использовано " + used + " шт., предметы кончились (" + cur + " / " + max + ")"
                        : title + ": использовано " + used + " шт." + (used < count ? ", предметы кончились" : ""));
            }
            finally
            {
                RowBusy[row] = false;
                RowMsg[row] = null;
                _nextScan = Time.unscaledTime + 2f;
            }
        }

        private static bool Gauge(int row, out int cur, out int max)
        {
            cur = 0; max = 0;
            try
            {
                var indicators = DependencyContainer.GetContainer()?.Resolve<IUserData>()?.Indicators;
                if (indicators == null) return false;
                switch (row)
                {
                    case 0: cur = indicators.CurrentLife; max = indicators.MaxLife; break;
                    case 1: cur = indicators.CurrentMana; max = indicators.MaxMana; break;
                    case 2: cur = indicators.CurrentStamina; max = indicators.MaxStamina; break;
                    default: return false;
                }
                return max > 0;
            }
            catch { return false; }
        }

        private static List<Stack> Stacks(int thingId)
        {
            lock (Scanned) return Scanned.Where(s => s.ThingId == thingId && s.Qty > 0).ToList();
        }

        private static void UpdateCounts()
        {
            if (_seen == 0) return;
            for (int row = 0; row < Rows; row++)
            {
                int id = Thing(row);
                Left[row] = id > 0 ? Stacks(id).Sum(s => s.Qty) : -1;
            }
        }

        private static IEnumerator EnsureScan(float maxAge)
        {
            while (_scanBusy) yield return null;
            if (Time.unscaledTime - _scanAt <= maxAge) yield break;
            _scanBusy = true;
            try
            {
                yield return Plugin.Instance.StartCoroutine(ScanRoutine());
                _scanAt = Time.unscaledTime;
                _scanOk = _seen > 0;
            }
            finally { _scanBusy = false; }
        }

        private static IEnumerator ScanRoutine()
        {
            if (!EnsureListeners()) yield break;
            _tabs = null;
            _seen = 0;
            lock (Scanned) { Scanned.Clear(); GotTabs.Clear(); }

            List<int> tabs;
            if (BagOpen())
            {
                if (!string.IsNullOrEmpty(Search.Query)) yield break;
                tabs = new List<int> { AllTab };
            }
            else
            {
                if (!Send(new GetThingsTabsRequest(WinInventory))) yield break;

                float wait = 0f;
                while (_tabs == null && wait < 3f) { yield return null; wait += Time.unscaledDeltaTime; }
                if (_tabs == null) yield break;
                tabs = new List<int>(_tabs);
            }

            foreach (int tab in tabs)
            {
                if (!Send(new GetTabContentRequest(tab, WinInventory))) yield break;
                yield return null;
            }

            float t = 0f;
            while (t < 3f)
            {
                int got;
                lock (Scanned) got = GotTabs.Count;
                if (got >= tabs.Count) break;
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            yield return null;
        }

        private static object _boundTo;

        private static bool EnsureListeners()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null) return false;
                if (_listeners && ReferenceEquals(_boundTo, nc)) return true;
                nc.AddMessageListener(359, OnTabs);
                nc.AddMessageListener(380, OnTabContent);
                nc.AddMessageListener(385, OnThingInfo);
                nc.AddMessageListener(416, OnAction);
                _boundTo = nc;
                _listeners = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnTabs(object m)
        {
            if (!_scanBusy) return;
            if (m is ThingTypeTabsResponseMessage t && t.TabIds != null && t.WindowId == WinInventory)
                _tabs = t.TabIds.Distinct().ToList();
        }

        private static void OnTabContent(object m)
        {
            if (!(m is ThingTabInventoryResponseMessage inv) || inv.WindowId != WinInventory || inv.Things == null) return;

            foreach (var th in inv.Things)
                if (th != null) Learn(th.ThingId, th.Image, th.Name);

            if (!_scanBusy) { Overhear(inv); return; }
            lock (Scanned)
            {
                GotTabs.Add(inv.TabNumber);
                _seen += inv.Things.Count;
                foreach (var th in inv.Things)
                {
                    if (th == null || th.Inventories == null) continue;
                    foreach (var ii in th.Inventories)
                    {
                        if (ii == null || ii.Quantity <= 0 || Scanned.Any(s => s.Inv == ii.InventoryId)) continue;
                        Scanned.Add(new Stack
                        {
                            Inv = ii.InventoryId,
                            Tab = inv.TabNumber,
                            ThingId = th.ThingId,
                            Qty = ii.Quantity,
                            SubType = th.SubType,
                        });
                    }
                }
            }
        }

        private static void Overhear(ThingTabInventoryResponseMessage inv)
        {
            try
            {
                if (AnyBusy || !string.IsNullOrEmpty(Search.Query)) return;
                for (int row = 0; row < Rows; row++)
                {
                    int id = Thing(row);
                    if (id <= 0) continue;
                    int sum = 0;
                    bool seen = false;
                    foreach (var th in inv.Things)
                    {
                        if (th == null || th.ThingId != id || th.Inventories == null) continue;
                        seen = true;
                        foreach (var ii in th.Inventories)
                            if (ii != null && ii.Quantity > 0) sum += ii.Quantity;
                    }
                    if (seen) Left[row] = sum;
                }
            }
            catch { }
        }

        private static void OnThingInfo(object m)
        {
            if (m is GeneralThingInfoMessage info) Learn(info.ThingId, info.Image, info.Name);
        }

        private static void OnAction(object m)
        {
            if (!(m is ThingContextActionResponseMessage r)) return;
            int left = -1;
            if (r.ChangesInTab != null)
                foreach (var ch in r.ChangesInTab)
                    if (ch.Id == r.Id) { left = ch.Quantity; break; }
            lock (Ctx) Ctx[r.Id] = (r.Success, r.Success ? null : r.ErrorMessage, left);
        }

        private static bool Take(int inv, out bool ok, out string err, out int left)
        {
            lock (Ctx)
            {
                if (Ctx.TryGetValue(inv, out var v))
                {
                    Ctx.Remove(inv);
                    ok = v.Ok; err = v.Err; left = v.Left;
                    return true;
                }
            }
            ok = false; err = null; left = -1;
            return false;
        }

        private static bool Connected()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                return nc != null && nc.IsConnected();
            }
            catch { return false; }
        }

        private static bool Send(BaseRequest request)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return false;
                nc.SendRequest(request);
                return true;
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[flasks] " + e.Message); return false; }
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static void Say(int row, string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
            StatusRow = row;
            Plugin.Trace("[flasks] " + text);
        }

        private static void Step(int row, string text)
        {
            RowMsg[row] = text;
            Status = Titles[row] + ": " + text;
            StatusAt = Time.unscaledTime;
            StatusRow = row;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(AtlasUtils), "ClearUnusedAtlases")]
    public static class AtlasDropPatch
    {
        private static void Postfix() => Flasks.DropIcons();
    }
}
