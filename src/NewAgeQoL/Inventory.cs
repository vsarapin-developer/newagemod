using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class RecipeIcons
    {
        private const int RecipeAlchemy = 19, RecipeTanner = 20, RecipeBlacksmith = 21;

        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Queue<int> Waiting = new Queue<int>();
        private static readonly List<InventoryThingItemRenderer> Live = new List<InventoryThingItemRenderer>();

        private static bool _listener, _draining;

        internal static bool Enabled => Plugin.CfgRecipeIcons == null || Plugin.CfgRecipeIcons.Value;

        internal static bool IsRecipe(int subType) =>
            subType == RecipeAlchemy || subType == RecipeTanner || subType == RecipeBlacksmith;

        internal static int CurrentTab = -1;

        private static int[] _tabs;
        private static string _tabsRaw;

        internal static bool SwapHere()
        {
            if (!Enabled) return false;
            string raw = Plugin.CfgRecipeIconTabs != null ? Plugin.CfgRecipeIconTabs.Value : "5,15,17,22";
            if (_tabs == null || raw != _tabsRaw)
            {
                _tabsRaw = raw;
                _tabs = raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => int.TryParse(x.Trim(), out int v) ? v : -1).Where(v => v > 0).ToArray();
            }
            return Array.IndexOf(_tabs, CurrentTab) >= 0;
        }

        internal static string ResultImage(int recipeThingId)
        {
            var image = Store.Image(recipeThingId);
            if (image != null) return image;
            lock (Asked)
            {
                if (Asked.Contains(recipeThingId)) return null;
                Asked.Add(recipeThingId);
                Waiting.Enqueue(recipeThingId);
            }
            Drain();
            return null;
        }

        internal static void Sync(InventoryThingItemRenderer r)
        {
            if (r == null) return;
            var d = SwapHere() ? r.Data : null;
            bool on = d != null && IsRecipe((int)d.SubType);
            Mark(r, on, on ? d.Image : null);
            if (!on) { Shrink(r, false); return; }
            Redraw(r);
        }

        private static MethodInfo _updateImage;

        // Клетку с новым рецептом игра не перерисовывает: в её проверке сравнивается картинка предмета,
        // а у всех рецептов она одна и та же (свиток). Нам это ломает подмену — в клетке остаётся
        // результат прошлого рецепта, хотя данные уже новые. Поэтому перерисовываем сами.
        private static void Redraw(InventoryThingItemRenderer r)
        {
            try
            {
                if (_updateImage == null) _updateImage = AccessTools.Method(typeof(InventoryThingItemRenderer), "UpdateImage");
                _updateImage?.Invoke(r, null);
            }
            catch { }
        }

        private static readonly AccessTools.FieldRef<InventoryThingItemRenderer, Image> ThingImageRef =
            AccessTools.FieldRefAccess<InventoryThingItemRenderer, Image>("ThingImage");

        internal static void Shrink(InventoryThingItemRenderer r, bool on)
        {
            try
            {
                var img = ThingImageRef(r);
                if (img == null) return;
                float scale = on ? 0.78f : 1f;
                var t = img.transform;
                if (Mathf.Abs(t.localScale.x - scale) > 0.01f) t.localScale = new Vector3(scale, scale, 1f);
            }
            catch { }
        }

        internal static void Track(InventoryThingItemRenderer r)
        {
            if (r != null && !Live.Contains(r)) Live.Add(r);
        }

        internal static void Forget(InventoryThingItemRenderer r) => Live.Remove(r);

        private static void Drain()
        {
            if (_draining || Plugin.Instance == null) return;
            _draining = true;
            Plugin.Instance.StartCoroutine(DrainRoutine());
        }

        private static IEnumerator DrainRoutine()
        {
            try
            {
                while (true)
                {
                    var batch = new List<int>();
                    lock (Asked)
                    {
                        while (Waiting.Count > 0 && batch.Count < 8) batch.Add(Waiting.Dequeue());
                    }
                    if (batch.Count == 0) yield break;
                    if (!EnsureListener()) yield break;
                    foreach (int id in batch)
                    {
                        try { NetworkConnection.Instance.SendRequest(new GeneralThingHintRequest(id)); }
                        catch { yield break; }
                    }
                    float t = 0f;
                    while (t < 0.05f) { yield return null; t += Time.unscaledDeltaTime; }
                }
            }
            finally { _draining = false; }
        }

        private static bool EnsureListener()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return false;
                if (_listener) return true;
                nc.AddMessageListener(388, OnRecipeInfo);
                _listener = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnRecipeInfo(object msg)
        {
            var rec = msg as GeneralPrescriptionInfoMessage;
            if (rec == null || rec.ThingId <= 0 || string.IsNullOrEmpty(rec.ResultThingImage)) return;
            if (Store.Image(rec.ThingId) != null) return;
            Store.SetImage(rec.ThingId, rec.ResultThingImage);
            RefreshCells(rec.ThingId);
        }

        private static void RefreshCells(int recipeThingId)
        {
            if (_updateImage == null) _updateImage = AccessTools.Method(typeof(InventoryThingItemRenderer), "UpdateImage");
            var update = _updateImage;
            if (update == null) return;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var r = Live[i];
                if (r == null) { Live.RemoveAt(i); continue; }
                try
                {
                    var d = r.Data;
                    if (d != null && d.ThingId == recipeThingId) update.Invoke(r, null);
                }
                catch { }
            }
        }

        private const string MarkName = "MvlRecipeMark";

        private static void Mark(InventoryThingItemRenderer r, bool on, string recipeImage)
        {
            try
            {
                var t = r.transform.Find(MarkName);
                if (t == null)
                {
                    if (!on) return;
                    t = BuildMark(r);
                    if (t == null) return;
                }

                if (on)
                {
                    var sprite = string.IsNullOrEmpty(recipeImage) ? null : AtlasUtils.GetThingSprite(recipeImage);
                    var img = t.GetComponent<Image>();
                    if (img != null) { img.sprite = sprite; img.enabled = sprite != null; }
                    var fallback = t.Find("txt");
                    if (fallback != null) fallback.gameObject.SetActive(sprite == null);
                }
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            }
            catch { }
        }

        private static Transform BuildMark(InventoryThingItemRenderer r)
        {
            var cell = r.transform as RectTransform;
            if (cell == null) return null;

            var go = new GameObject(MarkName, typeof(RectTransform));
            go.transform.SetParent(cell, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(7f, -7f);
            float side = Mathf.Max(10f, cell.rect.width * 0.26f);
            rt.sizeDelta = new Vector2(side, side);
            rt.localScale = Vector3.one;

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;

            var countText = AccessTools.Field(typeof(InventoryThingItemRenderer), "CountText")?.GetValue(r) as Text;
            var font = countText != null ? countText.font : null;
            if (font != null)
            {
                var sub = new GameObject("txt", typeof(RectTransform));
                sub.transform.SetParent(rt, worldPositionStays: false);
                var srt = (RectTransform)sub.transform;
                srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
                srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
                srt.localScale = Vector3.one;
                var txt = sub.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = countText != null ? Mathf.Max(10, countText.fontSize - 2) : 12;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.UpperLeft;
                txt.color = new Color(1f, 0.87f, 0.25f);
                txt.raycastTarget = false;
                txt.text = "Р";
                var outline = sub.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                sub.SetActive(false);
            }

            if (countText != null && countText.transform.parent == cell)
                rt.SetSiblingIndex(countText.transform.GetSiblingIndex());

            return rt;
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "UpdateImage")]
    public static class RecipeIconPatch
    {
        private static readonly AccessTools.FieldRef<InventoryThingItemRenderer, SingleImageLoader> LoaderRef =
            AccessTools.FieldRefAccess<InventoryThingItemRenderer, SingleImageLoader>("_singleImageLoader");

        private static bool Prefix(InventoryThingItemRenderer __instance)
        {
            try
            {
                var data = __instance.Data;
                if (data == null || !RecipeIcons.SwapHere()) { RecipeIcons.Shrink(__instance, false); return true; }
                if (!RecipeIcons.IsRecipe((int)data.SubType)) { RecipeIcons.Shrink(__instance, false); return true; }

                string img = RecipeIcons.ResultImage(data.ThingId);
                if (string.IsNullOrEmpty(img)) return true;

                var loader = LoaderRef(__instance);
                if (loader == null) return true;
                RecipeIcons.Shrink(__instance, true);
                loader.LoadThingImage(img, null);
                return false;
            }
            catch (Exception e) { Plugin.Log?.LogError("[recipes] UpdateImage: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "Data", MethodType.Setter)]
    public static class RecipeMarkSyncPatch
    {
        private static void Postfix(InventoryThingItemRenderer __instance) => RecipeIcons.Sync(__instance);
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "Awake")]
    public static class RecipeCellAwakePatch
    {
        private static void Postfix(InventoryThingItemRenderer __instance) => RecipeIcons.Track(__instance);
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "OnDestroy")]
    public static class RecipeCellDestroyPatch
    {
        private static void Prefix(InventoryThingItemRenderer __instance) => RecipeIcons.Forget(__instance);
    }

    internal static class Contracts
    {
        internal const int AllTab = 18;

        internal static bool Enabled => Plugin.CfgContractsTab == null || Plugin.CfgContractsTab.Value;
        internal static int TabId => Plugin.CfgContractsTabId != null ? Plugin.CfgContractsTabId.Value : 40;
        internal static int IconTabId => Plugin.CfgContractsIcon != null ? Plugin.CfgContractsIcon.Value : 16;

        internal static bool IsContract(ThingItemMessage t)
        {
            if (t == null) return false;
            if (t.ThingId >= 4000 && t.ThingId <= 4999) return true;
            string n = t.Name;
            return n != null && n.TrimStart().StartsWith("Контракт ", StringComparison.OrdinalIgnoreCase);
        }

        internal static void LearnIcon(ThingItemMessage t)
        {
            try
            {
                if (Plugin.CfgContractsIconImage == null || string.IsNullOrEmpty(t?.Image)) return;
                if (!string.IsNullOrEmpty(Plugin.CfgContractsIconImage.Value)) return;
                Plugin.CfgContractsIconImage.Value = t.Image;
                Plugin.Trace("[contracts] картинка вкладки: «" + t.Image + "» (по контракту " + t.ThingId + " «" + t.Name + "»)");
            }
            catch { }
        }

        private static Sprite _remote;
        private static string _remoteFor;

        internal static Sprite Icon()
        {
            try
            {
                string img = Plugin.CfgContractsIconImage != null ? Plugin.CfgContractsIconImage.Value : null;
                if (string.IsNullOrEmpty(img)) return null;

                var s = AtlasUtils.GetThingSprite(img);
                if (s != null && s.name != "unknown") return s;
                if (_remote != null && _remoteFor == img) return _remote;

                if (_remoteFor != img)
                {
                    _remoteFor = img;
                    RemoteImageLoader.Instance.Load(
                        "https://files.nura.biz/site/images/things100x100/" + img + ".png",
                        sprite => _remote = sprite,
                        error => Plugin.Log?.LogWarning("[contracts] картинка вкладки не загрузилась: " + error));
                }
            }
            catch { }
            return null;
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnThingTypeTabsResponse")]
    public static class ContractsTabListPatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                if (!Contracts.Enabled) return;
                var tabs = msg as ThingTypeTabsResponseMessage;
                if (tabs?.TabIds == null || tabs.TabIds.Count == 0) return;
                if (tabs.TabIds.Contains(Contracts.TabId)) return;
                tabs.TabIds.Insert(Math.Max(0, tabs.TabIds.Count - 1), Contracts.TabId);
            }
            catch (Exception e) { Plugin.Log?.LogError("[contracts] список вкладок: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "GetTabCaption")]
    public static class ContractsTabCaptionPatch
    {
        private static bool Prefix(int tabId, ref string __result)
        {
            if (!Contracts.Enabled || tabId != Contracts.TabId) return true;
            __result = "Контракты";
            return false;
        }
    }

    [HarmonyPatch(typeof(AtlasUtils), "GetThingTabImage")]
    public static class ContractsTabIconPatch
    {
        private static bool Prefix(EThingTabType tabType, ref Sprite __result)
        {
            if (!Contracts.Enabled || (int)tabType != Contracts.TabId) return true;
            __result = Contracts.Icon() ?? AtlasUtils.GetThingTabImage((EThingTabType)Contracts.IconTabId);
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnTabSelected")]
    public static class ContractsTabSelectedPatch
    {
        private static readonly AccessTools.FieldRef<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, int> CurrentTabRef =
            AccessTools.FieldRefAccess<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, int>("_currentTab");

        private static bool Prefix(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance, TabView tab)
        {
            try
            {
                if (tab != null) RecipeIcons.CurrentTab = tab.Id;
                if (!Contracts.Enabled || tab == null || tab.Id != Contracts.TabId) return true;
                CurrentTabRef(__instance) = tab.Id;
                NetworkConnection.Instance.SendRequest(
                    new GetTabContentRequest(Contracts.AllTab, (int)EThingContextWindow.WINDOW_INVENTORY));
                return false;
            }
            catch (Exception e) { Plugin.Log?.LogError("[contracts] выбор вкладки: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs>), "OnReceiveTabContent")]
    public static class ContractsTabContentPatch
    {
        private static void Prefix(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs> __instance, object msg)
        {
            try
            {
                var content = msg as ThingTabInventoryResponseMessage;
                if (content?.Things == null) return;
                if (content.WindowId != (int)EThingContextWindow.WINDOW_INVENTORY) return;

                RecipeIcons.CurrentTab = __instance.CurrentTab;
                foreach (var t in content.Things) if (Contracts.IsContract(t)) { Contracts.LearnIcon(t); break; }
                if (!Contracts.Enabled) return;

                if (__instance.CurrentTab == Contracts.TabId && content.TabNumber == Contracts.AllTab)
                {
                    content.Things = content.Things.Where(Contracts.IsContract).ToList();
                    content.TabNumber = Contracts.TabId;
                    return;
                }
                if (content.TabNumber == Contracts.AllTab || content.TabNumber == Contracts.TabId) return;
                content.Things = content.Things.Where(t => !Contracts.IsContract(t)).ToList();
            }
            catch (Exception e) { Plugin.Log?.LogError("[contracts] содержимое вкладки: " + e.Message); }
        }
    }
}
