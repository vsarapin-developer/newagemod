using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Search
    {
        internal static string Query = "";

        private static InputField _field;
        private static float _pending = -1f;

        internal static bool Enabled => Plugin.CfgSearch == null || Plugin.CfgSearch.Value;

        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Queue<int> Waiting = new Queue<int>();
        private static bool _listener, _draining;

        internal static string TextOf(ThingItemMessage t)
        {
            if (t == null) return null;
            return string.IsNullOrEmpty(t.Name) ? Store.Text(t.ThingId) : t.Name;
        }

        internal static bool Match(ThingItemMessage t)
        {
            if (string.IsNullOrEmpty(Query)) return true;
            var text = TextOf(t);
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Flat(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }

        private static void Remember(int thingId, string name)
        {
            string text = Flat(name).Trim();
            Store.SetText(thingId, text);
            ContractNumbers.Learn(thingId, text);
        }

        internal static void Learn(IEnumerable<ThingItemMessage> things)
        {
            if (things == null) return;
            int asked = 0;
            foreach (var t in things)
                if (t != null && Ask(t.ThingId)) asked++;
            if (asked > 0) Drain();
        }

        internal static bool Ask(int thingId)
        {
            if (thingId <= 0 || Store.Text(thingId) != null) return false;
            lock (Asked)
            {
                if (Asked.Contains(thingId)) return false;
                Asked.Add(thingId);
                Waiting.Enqueue(thingId);
            }
            return true;
        }

        internal static void AskNow(int thingId)
        {
            if (Ask(thingId)) Drain();
        }

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
                        while (Waiting.Count > 0 && batch.Count < 8) batch.Add(Waiting.Dequeue());
                    if (batch.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(Query)) _pending = Time.unscaledTime;
                        yield break;
                    }
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
                nc.AddMessageListener(385, OnThingInfo);
                nc.AddMessageListener(388, OnRecipeInfo);
                _listener = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnThingInfo(object msg)
        {
            var info = msg as GeneralThingInfoMessage;
            if (info == null || info.ThingId <= 0) return;
            Remember(info.ThingId, info.Name);
        }

        private static void OnRecipeInfo(object msg)
        {
            var info = msg as GeneralPrescriptionInfoMessage;
            if (info == null || info.ThingId <= 0) return;
            Remember(info.ThingId, info.Name);
        }

        internal static void Clear()
        {
            Query = "";
            _pending = -1f;
            if (_field != null && !string.IsNullOrEmpty(_field.text)) _field.SetTextWithoutNotify("");
        }

        internal static void Tick()
        {
            if (!Enabled) return;
            EnsureBuilt();
            Place();
            if (_pending < 0f || Time.unscaledTime < _pending) return;
            _pending = -1f;
            Request();
        }

        private static RectTransform _panel;
        private static RectTransform _window;
        private const float Height = 46f;
        private static float _placeAt;

        private static void Place()
        {
            if (_field == null || _panel == null || _window == null) return;
            bool show = _panel.gameObject.activeInHierarchy;
            if (_field.gameObject.activeSelf != show) _field.gameObject.SetActive(show);
            if (!show || Time.unscaledTime < _placeAt) return;
            _placeAt = Time.unscaledTime + 0.2f;

            var rt = _field.transform as RectTransform;
            var host = rt != null ? rt.parent as RectTransform : null;
            if (host == null) return;

            Vector2 left = host.InverseTransformPoint(
                _window.TransformPoint(new Vector3(_window.rect.xMin, _window.rect.yMin, 0f)));
            Vector2 right = host.InverseTransformPoint(
                _window.TransformPoint(new Vector3(_window.rect.xMax, _window.rect.yMin, 0f)));

            rt.sizeDelta = new Vector2(right.x - left.x, Height);
            rt.anchoredPosition = new Vector2((left.x + right.x) * 0.5f, left.y)
                                - host.rect.center + new Vector2(0f, -6f);
        }

        private static void Request()
        {
            try
            {
                int tab = RecipeIcons.CurrentTab;
                if (tab < 0) return;
                int ask = Contracts.Enabled && tab == Contracts.TabId ? Contracts.AllTab : tab;
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.SendRequest(new GetTabContentRequest(ask, (int)EThingContextWindow.WINDOW_INVENTORY));
            }
            catch (Exception e) { Plugin.Log?.LogError("[search] " + e.Message); }
        }

        private static void Changed(string value)
        {
            Query = value ?? "";
            _pending = Time.unscaledTime + 0.25f;
        }

        private static InputField Template()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<InputField>())
            {
                if (f == null || f.name.StartsWith("QoL")) continue;
                if (f.textComponent == null) continue;
                var img = f.GetComponent<Image>();
                if (img == null || img.sprite == null) continue;
                return f;
            }
            return null;
        }

        private static void Strip(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb is Graphic || mb is Selectable || mb is Mask || mb is RectMask2D || mb is Shadow
                    || mb is LayoutGroup || mb is ContentSizeFitter || mb is LayoutElement) continue;
                UnityEngine.Object.DestroyImmediate(mb);
            }
        }

        private static float _lookAt;

        internal static StandardContentWindowPanel Owner;

        private static void Drop()
        {
            if (_field != null) UnityEngine.Object.Destroy(_field.gameObject);
            _field = null;
            _panel = null;
            _window = null;
            Query = "";
            _pending = -1f;
        }

        private static void EnsureBuilt()
        {
            if (Owner == null)
            {
                if (_field != null) Drop();
                return;
            }
            if (_field != null && _panel != null) return;
            if (Time.unscaledTime < _lookAt) return;
            _lookAt = Time.unscaledTime + 1f;

            if (_field != null) Drop();
            var panel = Owner.GetComponentInChildren<InventoryPanelContent>(true);
            if (panel == null) return;
            Build(panel.transform as RectTransform);
        }

        internal static void Build(RectTransform panel)
        {
            if (!Enabled || panel == null) return;

            var host = panel;
            while (host.parent is RectTransform up && up.GetComponent<Canvas>() == null) host = up;
            if (!(host.parent is RectTransform)) return;

            RectTransform window = null;
            var box = panel.GetComponentInParent<StandardContentWindowPanel>();
            if (box != null) window = box.transform as RectTransform;
            if (window == null)
            {
                window = panel;
                while (window.parent is RectTransform step && step != host) window = step;
            }
            if (window == null || window == host) return;

            var stale = host.Find("QoLSearch");
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);

            var template = Template();
            GameObject go;
            if (template != null)
            {
                go = UnityEngine.Object.Instantiate(template.gameObject, host);
                Clones.StripHotkeys(go, template.gameObject);
                Strip(go);
            }
            else
            {
                go = new GameObject("field", typeof(RectTransform), typeof(Image), typeof(InputField));
                go.transform.SetParent(host, false);
                var back = go.GetComponent<Image>();
                back.color = new Color(0.16f, 0.12f, 0.08f, 0.85f);
            }
            go.name = "QoLSearch";
            go.SetActive(true);

            var ignore = go.GetComponent<LayoutElement>();
            if (ignore == null) ignore = go.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(window.rect.width, Height);
            rt.SetAsLastSibling();

            var field = go.GetComponent<InputField>();
            if (field == null) field = go.AddComponent<InputField>();

            if (field.textComponent == null)
            {
                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(rt, false);
                var trt = (RectTransform)textGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10f, 2f);
                trt.offsetMax = new Vector2(-10f, -2f);
                var text = textGo.GetComponent<Text>();
                text.font = VisualPrefabsHolder.Instance != null ? VisualPrefabsHolder.Instance.StandardFont : null;
                text.fontSize = 18;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = new Color(0.25f, 0.16f, 0.08f);
                text.supportRichText = false;
                field.textComponent = text;
            }

            if (field.placeholder is Text hint)
            {
                hint.text = "Поиск по названию";
                hint.alignment = TextAnchor.MiddleLeft;
                hint.gameObject.SetActive(true);
            }
            else
            {
                var hintGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
                hintGo.transform.SetParent(rt, false);
                var prt = (RectTransform)hintGo.transform;
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.offsetMin = new Vector2(10f, 2f);
                prt.offsetMax = new Vector2(-10f, -2f);
                var text = hintGo.GetComponent<Text>();
                text.font = field.textComponent != null ? field.textComponent.font : null;
                text.fontSize = 18;
                text.fontStyle = FontStyle.Italic;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = new Color(0.45f, 0.36f, 0.26f, 0.85f);
                text.text = "Поиск по названию";
                field.placeholder = text;
            }

            if (field.textComponent != null)
            {
                field.textComponent.alignment = TextAnchor.MiddleLeft;
                field.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.alignment = TextAnchor.MiddleLeft;
                var trt = t.transform as RectTransform;
                if (trt == null || trt == rt) continue;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.pivot = new Vector2(0.5f, 0.5f);
                trt.offsetMin = new Vector2(14f, 3f);
                trt.offsetMax = new Vector2(-14f, -3f);
            }

            field.characterLimit = 40;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;
            field.interactable = true;
            field.onValueChanged = new InputField.OnChangeEvent();
            field.onValueChanged.AddListener(Changed);
            field.SetTextWithoutNotify("");

            _field = field;
            _panel = panel;
            _window = window;
            _placeAt = 0f;
            Query = "";
            Place();
            Plugin.Trace("[search] строка поиска добавлена, образец «"
                                 + (template != null ? template.name : "свой") + "»");
        }
    }

    [HarmonyPatch(typeof(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs>), "InternalActivatePanel")]
    public static class SearchPanelOpenPatch
    {
        private static void Postfix(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs> __instance, bool __result)
        {
            if (__result) Search.Owner = __instance.CurrentPanel;
        }
    }

    [HarmonyPatch(typeof(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs>), "InternalDeactivatePanel")]
    public static class SearchPanelClosePatch
    {
        private static void Postfix(bool __result)
        {
            if (__result) Search.Owner = null;
        }
    }

    [HarmonyPatch(typeof(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs>), "OnReceiveTabContent")]
    public static class SearchTabContentPatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                if (!Search.Enabled) return;
                var content = msg as ThingTabInventoryResponseMessage;
                if (content == null || content.Things == null) return;
                if (content.WindowId == (int)EThingContextWindow.WINDOW_INVENTORY) Search.Learn(content.Things);
                if (string.IsNullOrEmpty(Search.Query)) return;
                if (content.WindowId != (int)EThingContextWindow.WINDOW_INVENTORY) return;
                content.Things = content.Things.Where(Search.Match).ToList();
            }
            catch (Exception e) { Plugin.Log?.LogError("[search] содержимое: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnTabSelected")]
    public static class SearchTabResetPatch
    {
        private static void Postfix() => Search.Clear();
    }
}
