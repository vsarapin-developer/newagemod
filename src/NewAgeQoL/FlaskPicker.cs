using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FlaskPicker
    {
        private const float PanelW = 580f;
        private const float PanelH = 640f;
        private const float Cell = 104f;
        private const float Icon = 64f;
        private const int Columns = 4;

        private static Canvas _canvas;
        private static Transform _grid;
        private static ConfigEntry<string> _byName;
        private static ConfigEntry<int> _byId;
        private static Action _onPicked;
        private static string _keyword = "";
        private static readonly List<KeyValuePair<int, Image>> _cells = new List<KeyValuePair<int, Image>>();
        private static readonly List<int> _shown = new List<int>();
        private static float _refreshAt;

        internal static void Open(ConfigEntry<string> byName, ConfigEntry<int> byId, string title, Action onPicked)
        {
            _byName = byName;
            _byId = byId;
            _onPicked = onPicked;
            _keyword = KeywordFor(title);
            try
            {
                Flasks.RequestScan();
                Flasks.RequestNames();
                Build(title);
                Rebuild();
            }
            catch (Exception e) { Plugin.Log?.LogError("[банки] выбор: " + e.Message); Close(); }
        }

        private static string KeywordFor(string title)
        {
            title = (title ?? "").ToLowerInvariant();
            if (title.Contains("жизн") || title.Contains("здоров")) return "здоров";
            if (title.Contains("ман")) return "ман";
            if (title.Contains("энерг")) return "энерг";
            if (title.Contains("гриб")) return "гриб";
            return "";
        }

        internal static void Close()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _grid = null;
            _cells.Clear();
            _shown.Clear();
        }

        internal static void Tick()
        {
            if (_canvas == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 0.3f;

            var things = Filtered();
            if (things.Count != _shown.Count) { Rebuild(); return; }
            foreach (var pair in _cells)
                if (pair.Value != null) pair.Value.sprite = Flasks.IconFor(pair.Key);
        }

        private static List<int> Filtered()
        {
            var list = new List<int>();
            foreach (int tid in Flasks.ConsumableThings())
            {
                if (_keyword.Length == 0) { list.Add(tid); continue; }
                string name = Flasks.DisplayName(tid);
                if (name != null && name.ToLowerInvariant().Contains(_keyword)) list.Add(tid);
            }
            list.Sort();
            return list;
        }

        private static void Build(string title)
        {
            Close();
            var go = new GameObject("QoLFlaskPicker", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(0.16f, 0.12f, 0.08f, 0.98f);

            var titleT = Line(panelGo.transform, (title ?? "") + " — выбери банку", 22, FontStyle.Bold, new Color32(255, 224, 130, 255));
            var trt = (RectTransform)titleT.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(12f, -46f); trt.offsetMax = new Vector2(-12f, -8f);
            titleT.alignment = TextAnchor.MiddleCenter;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-6f, -6f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var xT = Line(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            var xrt = (RectTransform)xT.transform; xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.offsetMin = Vector2.zero; xrt.offsetMax = Vector2.zero;
            xT.alignment = TextAnchor.MiddleCenter;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(12f, 12f); srt.offsetMax = new Vector2(-12f, -52f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = new Vector2(0f, 0f); cont.offsetMax = new Vector2(0f, 0f);
            var glg = contentGo.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(Cell, Cell + 26f);
            glg.spacing = new Vector2(8f, 8f);
            glg.padding = new RectOffset(6, 6, 6, 6);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = Columns;
            glg.childAlignment = TextAnchor.UpperLeft;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = cont;
            scroll.viewport = srt;
            _grid = contentGo.transform;
        }

        private static void Rebuild()
        {
            if (_grid == null) return;
            for (int i = _grid.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_grid.GetChild(i).gameObject);
            _cells.Clear();
            _shown.Clear();

            var things = Filtered();
            foreach (int tid in things)
            {
                _shown.Add(tid);
                int thingId = tid;

                var cellGo = new GameObject("cell", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup));
                cellGo.transform.SetParent(_grid, false);
                cellGo.GetComponent<Image>().color = new Color(0.28f, 0.22f, 0.14f, 0.9f);
                cellGo.GetComponent<Button>().onClick.AddListener(() => Pick(thingId));
                var clg = cellGo.GetComponent<VerticalLayoutGroup>();
                clg.padding = new RectOffset(4, 4, 4, 2);
                clg.spacing = 1f;
                clg.childAlignment = TextAnchor.UpperCenter;
                clg.childForceExpandWidth = true;
                clg.childForceExpandHeight = false;

                var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                iconGo.transform.SetParent(cellGo.transform, false);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.sprite = Flasks.IconFor(thingId);
                var isz = iconGo.GetComponent<LayoutElement>();
                isz.minHeight = Icon; isz.preferredHeight = Icon;

                string name = Flasks.DisplayName(thingId) ?? ("id " + thingId);
                int qty = Flasks.QtyOf(thingId);
                var cap = Line(cellGo.transform, name + (qty > 0 ? "  x" + qty : ""), 12, FontStyle.Normal, new Color32(240, 232, 210, 255));
                cap.alignment = TextAnchor.UpperCenter;
                cap.horizontalOverflow = HorizontalWrapMode.Wrap;
                cap.verticalOverflow = VerticalWrapMode.Truncate;
                var csz = cap.gameObject.GetComponent<LayoutElement>() ?? cap.gameObject.AddComponent<LayoutElement>();
                csz.minHeight = 30f; csz.preferredHeight = 30f;

                _cells.Add(new KeyValuePair<int, Image>(thingId, iconImg));
            }

            if (things.Count == 0)
            {
                var t = Line(_grid, "Пока пусто — открой сумку разок, банки подтянутся", 13, FontStyle.Normal, new Color32(220, 200, 170, 255));
                var le = t.gameObject.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
                le.minWidth = PanelW - 40f; le.minHeight = 40f;
            }
        }

        private static void Pick(int thingId)
        {
            try
            {
                if (_byId != null) _byId.Value = thingId;
                if (_byName != null) _byName.Value = "";
            }
            catch (Exception e) { Plugin.Log?.LogError("[банки] выбор: " + e.Message); }
            var cb = _onPicked;
            Close();
            try { cb?.Invoke(); } catch { }
        }

        private static Text Line(Transform host, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = text;
            return t;
        }

        private static Font Font()
        {
            try
            {
                var holder = VisualPrefabsHolder.Instance;
                if (holder != null && holder.StandardFont != null) return holder.StandardFont;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
