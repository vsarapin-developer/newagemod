using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Notice
    {
        private static GameObject _canvasGo;
        private static CanvasGroup _veil;
        private static Text _text;
        private static float _until;

        internal static void Show(string text, float seconds)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                Build();
                if (_text == null) return;
                _text.text = text;
                _until = Time.unscaledTime + Mathf.Max(1f, seconds);
                if (_veil != null) _veil.alpha = 1f;
                _canvasGo.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[сообщение] " + e.Message); }
        }

        internal static void Tick()
        {
            if (_canvasGo == null || !_canvasGo.activeSelf) return;
            float left = _until - Time.unscaledTime;
            if (left <= 0f) { _canvasGo.SetActive(false); return; }
            if (_veil != null) _veil.alpha = left < 0.6f ? left / 0.6f : 1f;
        }

        private static void Build()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject("QoLNotice", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _veil = _canvasGo.GetComponent<CanvasGroup>();
            _veil.blocksRaycasts = false;
            _veil.interactable = false;

            var panelGo = new GameObject("plate", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(_canvasGo.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -150f);

            var back = panelGo.GetComponent<Image>();
            back.color = new Color(0.24f, 0.06f, 0.05f, 0.95f);
            back.sprite = OnlineWindow.Rounded(12);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = panelGo.GetComponent<Outline>();
            edge.effectColor = new Color(1f, 0.6f, 0.45f, 0.9f);
            edge.effectDistance = new Vector2(2f, -2f);

            var box = panelGo.GetComponent<HorizontalLayoutGroup>();
            box.padding = new RectOffset(18, 18, 10, 12);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = false;
            box.childForceExpandHeight = false;

            var fit = panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _text = OnlineWindow.Label(panelGo.transform, "", 18, FontStyle.Bold, new Color32(255, 226, 214, 255));
            _text.alignment = TextAnchor.MiddleCenter;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = _text.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 620f;

            _canvasGo.SetActive(false);
        }
    }
}
