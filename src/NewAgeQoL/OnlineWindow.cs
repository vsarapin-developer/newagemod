using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class OnlineWindow
    {
        private const float PanelW = 760f;
        private const float PanelH = 680f;
        private const float RowH = 26f;

        private static Canvas _canvas;
        private static Transform _list;
        private static Text _status;
        private static Text _title;
        private static Button _refresh;
        private static InputField _filter;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;

        internal static bool IsOpen => _canvas != null;

        internal static void Toggle()
        {
            if (_canvas != null) { Close(); return; }
            Open();
        }

        internal static void Open()
        {
            try
            {
                Build();
                _seenVersion = -1;
                if (!OnlineList.Busy) OnlineList.Refresh();
                Rebuild();
            }
            catch (Exception e) { Plugin.Log?.LogError("[онлайн] окно: " + e.Message); Close(); }
        }

        internal static void Close()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _list = null;
            _status = null;
            _title = null;
            _refresh = null;
            _filter = null;
        }

        internal static void Tick()
        {
            if (_canvas == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime < _pollAt) return;
            _pollAt = Time.unscaledTime + 0.2f;
            int v = OnlineList.Version;
            if (v == _seenVersion) return;
            _seenVersion = v;
            Rebuild();
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLOnlineWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 790;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(0.16f, 0.12f, 0.08f, 0.98f);

            _title = Label(panelGo.transform, "Кто в игре", 22, FontStyle.Bold, new Color32(255, 224, 130, 255));
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -44f), new Vector2(-60f, -8f));
            _title.alignment = TextAnchor.MiddleLeft;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-6f, -6f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(panelGo.transform, false);
            Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -84f), new Vector2(-16f, -50f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _filter = MakeInput(barGo.transform, 300f, "поиск: ник, клан, класс");
            _filter.onValueChanged.AddListener(v => { _query = (v ?? "").Trim().ToLowerInvariant(); Rebuild(); });

            _refresh = MakeButton(barGo.transform, "Обновить", 120f, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });

            _status = Label(barGo.transform, "", 14, FontStyle.Normal, new Color32(220, 205, 170, 255));
            _status.alignment = TextAnchor.MiddleLeft;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            var headGo = new GameObject("head", typeof(RectTransform), typeof(Image));
            headGo.transform.SetParent(panelGo.transform, false);
            Place((RectTransform)headGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -116f), new Vector2(-16f, -90f));
            headGo.GetComponent<Image>().color = new Color(0.3f, 0.22f, 0.12f, 1f);
            FillRow(headGo.transform, "Игрок", "Ур.", "Класс", "Клан", "Ранг", "", true);

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 14f), new Vector2(-16f, -118f));
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 1f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cont;
            scroll.viewport = srt;
            _list = contentGo.transform;
        }

        private static void Rebuild()
        {
            if (_list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_list.GetChild(i).gameObject);

            var players = OnlineList.Players;
            players.Sort((a, b) =>
            {
                int c = b.Level.CompareTo(a.Level);
                return c != 0 ? c : string.Compare(a.Login, b.Login, StringComparison.OrdinalIgnoreCase);
            });

            int shown = 0;
            foreach (var p in players)
            {
                if (_query.Length > 0
                    && p.Login.ToLowerInvariant().IndexOf(_query, StringComparison.Ordinal) < 0
                    && p.Clan.ToLowerInvariant().IndexOf(_query, StringComparison.Ordinal) < 0
                    && p.Class.ToLowerInvariant().IndexOf(_query, StringComparison.Ordinal) < 0) continue;
                var rowGo = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                rowGo.transform.SetParent(_list, false);
                rowGo.GetComponent<Image>().color = (shown & 1) == 0 ? new Color(1f, 1f, 1f, 0.04f) : new Color(1f, 1f, 1f, 0f);
                var le = rowGo.GetComponent<LayoutElement>();
                le.minHeight = RowH; le.preferredHeight = RowH;
                string mark = (p.Admin ? "модер " : "") + (p.Vip ? "vip" : "");
                FillRow(rowGo.transform, p.Login, p.Level > 0 ? p.Level.ToString() : "", p.Class, Clan(p.Clan), p.Rank > 0 ? p.Rank.ToString() : "", mark, false);
                shown++;
            }

            if (_title != null) _title.text = "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "") + (_query.Length > 0 ? " · показано " + shown : "");
            if (_status != null) _status.text = OnlineList.Status;
            if (_refresh != null) _refresh.interactable = !OnlineList.Busy;
            if (players.Count == 0 && !OnlineList.Busy && !OnlineList.Configured)
            {
                var t = Label(_list, "Укажи логин и пароль запасного аккаунта в настройках мода, раздел «Кто в игре».", 15, FontStyle.Normal, new Color32(240, 200, 160, 255));
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 60f;
            }
        }

        private static string Clan(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return "";
            string s = icon;
            if (s.EndsWith("_Small", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 6);
            else if (s.EndsWith("_s", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2);
            return s;
        }

        private static void FillRow(Transform row, string login, string level, string cls, string clan, string rank, string mark, bool head)
        {
            var hlg = row.gameObject.GetComponent<HorizontalLayoutGroup>() ?? row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 2, 2);
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            var style = head ? FontStyle.Bold : FontStyle.Normal;
            var color = head ? new Color32(255, 224, 130, 255) : new Color32(240, 232, 210, 255);
            Cell(row, login, 250f, style, color, TextAnchor.MiddleLeft);
            Cell(row, level, 44f, style, color, TextAnchor.MiddleCenter);
            Cell(row, cls, 130f, style, color, TextAnchor.MiddleLeft);
            Cell(row, clan, 140f, style, color, TextAnchor.MiddleLeft);
            Cell(row, rank, 70f, style, color, TextAnchor.MiddleRight);
            Cell(row, mark, 70f, style, head ? color : new Color32(255, 200, 90, 255), TextAnchor.MiddleLeft);
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color color, TextAnchor align)
        {
            var t = Label(row, text, 15, style, color);
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width; le.preferredWidth = width;
        }

        private static InputField MakeInput(Transform host, float width, string placeholder)
        {
            var go = new GameObject("filter", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.5f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 32f;

            var txt = Label(go.transform, "", 16, FontStyle.Normal, Color.white);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 3f), new Vector2(-8f, -3f));

            var ph = Label(go.transform, placeholder, 16, FontStyle.Italic, new Color(1f, 1f, 1f, 0.35f));
            ph.alignment = TextAnchor.MiddleLeft;
            Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 3f), new Vector2(-8f, -3f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = Color.white;
            input.customCaretColor = true;
            return input;
        }

        private static Button MakeButton(Transform host, string text, float width, Action onClick)
        {
            var go = new GameObject("btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.2f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 32f;
            var t = Label(go.transform, text, 16, FontStyle.Bold, Color.white);
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var b = go.GetComponent<Button>();
            b.onClick.AddListener(() => onClick());
            return b;
        }

        private static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        private static Text Label(Transform host, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = text;
            return t;
        }
    }
}
