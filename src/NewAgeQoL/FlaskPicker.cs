using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FlaskPicker
    {
        private const float Cell = 96f;
        private const float Icon = 64f;

        private static Canvas _canvas;
        private static Transform _grid;
        private static Text _title;
        private static ConfigEntry<string> _byName;
        private static ConfigEntry<int> _byId;
        private static Action _onPicked;
        private static readonly List<KeyValuePair<int, Image>> _cells = new List<KeyValuePair<int, Image>>();
        private static readonly List<int> _shown = new List<int>();
        private static float _refreshAt;

        internal static bool IsOpen => _canvas != null;

        internal static void Open(ConfigEntry<string> byName, ConfigEntry<int> byId, string title, Action onPicked)
        {
            _byName = byName;
            _byId = byId;
            _onPicked = onPicked;
            try
            {
                Flasks.RequestScan();
                Build(title);
                Rebuild();
            }
            catch (Exception e) { Plugin.Log?.LogError("[банки] выбор: " + e.Message); Close(); }
        }

        internal static void Close()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _grid = null;
            _title = null;
            _cells.Clear();
            _shown.Clear();
        }

        internal static void Tick()
        {
            if (_canvas == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 0.3f;

            var things = Flasks.ConsumableThings();
            if (things.Count != _shown.Count) { Rebuild(); return; }
            foreach (var pair in _cells)
                if (pair.Value != null) pair.Value.sprite = Flasks.IconFor(pair.Key);
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

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            var pbg = panelGo.GetComponent<Image>();
            pbg.color = new Color(0.16f, 0.12f, 0.08f, 0.98f);
            var vlg = panelGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 16);
            vlg.spacing = 10f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            var fit = panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = Line(panelGo.transform, title + " — выбери банку", 22, FontStyle.Bold, new Color32(255, 224, 130, 255));

            var gridGo = new GameObject("grid", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(panelGo.transform, false);
            var glg = gridGo.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(Cell, Cell + 22f);
            glg.spacing = new Vector2(8f, 8f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 5;
            glg.childAlignment = TextAnchor.UpperLeft;
            _grid = gridGo.transform;
        }

        private static void Rebuild()
        {
            if (_grid == null) return;
            for (int i = _grid.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_grid.GetChild(i).gameObject);
            _cells.Clear();
            _shown.Clear();

            var things = Flasks.ConsumableThings();
            things.Sort();
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
                csz.minHeight = 20f; csz.preferredHeight = 20f;

                _cells.Add(new KeyValuePair<int, Image>(thingId, iconImg));
            }

            if (things.Count == 0)
                Line(_grid, "Банок в сумке не найдено — открой сумку разок", 14, FontStyle.Normal, new Color32(220, 200, 170, 255));
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
