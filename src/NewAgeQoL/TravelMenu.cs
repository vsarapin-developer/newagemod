using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{

    internal static class TravelMenu
    {
        private const float RowHeight = 30f;
        private const float Width = 220f;
        private const float Pad = 6f;

        private static RectTransform _root;
        private static readonly List<Text> _labels = new List<Text>();
        private static readonly List<Spot> _shown = new List<Spot>();
        private static bool _open;
        private static float _leftAt;

        internal static bool Open => _open && _root != null && _root.gameObject.activeSelf;

        internal static void Toggle()
        {
            _open = !_open;
            _leftAt = 0f;
            if (!_open) Hide();
        }

        internal static void Hide()
        {
            _open = false;
            _leftAt = 0f;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        internal static void Hover()
        {
            if (!_open || _root == null || !_root.gameObject.activeSelf) return;

            var cam = Eye();
            if (Inside(_root, cam) || Inside(SideButtons.ButtonRect("QoLTravelButton"), cam))
            {
                _leftAt = 0f;
                return;
            }
            if (_leftAt <= 0f) { _leftAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _leftAt > 0.4f) Hide();
        }

        private static bool Inside(RectTransform rect, Camera eye)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eye);
        }

        private static Camera Eye()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        internal static void Tick(bool ready)
        {
            try
            {
                if (!ready || !_open || !Travel.Enabled) { Hide(); return; }

                var spots = Travel.Spots();
                if (spots.Count == 0) { Hide(); return; }
                if (!Same(spots)) Build(spots);
                if (_root == null) { _open = false; return; }

                for (int i = 0; i < _shown.Count && i < _labels.Count; i++)
                {
                    string text = Travel.IsRunning(_shown[i]) ? "▶ " + _shown[i].Name : _shown[i].Name;
                    if (_labels[i].text != text) _labels[i].text = text;
                }

                Place();
                if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogError("[travel] список точек: " + e.Message);
                _open = false;
            }
        }

        private static bool Same(List<Spot> spots)
        {
            if (_root == null || _shown.Count != spots.Count) return false;
            for (int i = 0; i < spots.Count; i++)
                if (!ReferenceEquals(_shown[i], spots[i])) return false;
            return true;
        }

        private static void Place()
        {
            var panel = SideButtons.PanelRect;
            if (panel == null || _root == null) return;

            bool left = SideButtons.AtRightSide;
            _root.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            float half = panel.sizeDelta.x * 0.5f;

            float wantY = SideButtons.RowOf("QoLTravelButton");
            var canvas = panel.parent as RectTransform;
            if (canvas != null && canvas.rect.height > 1f)
            {

                Vector2 center = canvas.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
                float limit = Mathf.Max(0f, canvas.rect.height * 0.5f - _root.sizeDelta.y * 0.5f);
                wantY = Mathf.Clamp(center.y + wantY, -limit, limit) - center.y;
            }
            _root.anchoredPosition = new Vector2(left ? -(half + 8f) : half + 8f, wantY);
        }

        private static void Build(List<Spot> spots)
        {
            var panel = SideButtons.PanelRect;
            if (panel == null) return;
            if (_root != null) Object.Destroy(_root.gameObject);
            _labels.Clear();
            _shown.Clear();
            _shown.AddRange(spots);

            var go = new GameObject("QoLTravelMenu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.localScale = Vector3.one;
            _root.sizeDelta = new Vector2(Width, spots.Count * RowHeight + Pad * 2f);

            var back = go.GetComponent<Image>();
            back.color = new Color(0.05f, 0.055f, 0.07f, 0.93f);
            back.raycastTarget = true;

            var font = SideButtons.GameFont();
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                var row = new GameObject("row" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                row.transform.SetParent(_root, false);
                var rt = (RectTransform)row.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(Pad, 0f);
                rt.offsetMax = new Vector2(-Pad, 0f);
                rt.sizeDelta = new Vector2(-Pad * 2f, RowHeight);
                rt.anchoredPosition = new Vector2(0f, -Pad - i * RowHeight);

                var fill = row.GetComponent<Image>();
                fill.color = Color.white;
                fill.raycastTarget = true;

                var button = row.GetComponent<Button>();
                button.targetGraphic = fill;
                var colors = button.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
                colors.highlightedColor = new Color(1f, 0.86f, 0.45f, 0.30f);
                colors.pressedColor = new Color(1f, 0.8f, 0.3f, 0.45f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.05f);
                colors.fadeDuration = 0.05f;
                button.colors = colors;
                button.onClick.AddListener(() =>
                {
                    Travel.Click(spot);
                    Hide();
                });

                var textGo = new GameObject("text", typeof(RectTransform), typeof(Text), typeof(Outline));
                textGo.transform.SetParent(rt, false);
                var trt = (RectTransform)textGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(8f, 0f);
                trt.offsetMax = new Vector2(-8f, 0f);

                var label = textGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 19;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = new Color(1f, 0.94f, 0.78f);
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.text = spot.Name;

                var outline = textGo.GetComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
                outline.effectDistance = new Vector2(1.4f, -1.4f);

                _labels.Add(label);
            }
        }
    }
}
