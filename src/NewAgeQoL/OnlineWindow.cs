using System;
using System.Collections.Generic;
using Transport.Messages.Common.User;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class OnlineWindow
    {
        private const float PanelW = 640f;
        private const float PanelH = 700f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static ChatUserListPanelContent _panel;
        private static ListWrapper<UserRowInfoMessage> _wrapper;
        private static Text _status;
        private static Text _title;
        private static Button _refresh;
        private static InputField _filter;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;

        private static readonly Dictionary<string, int> ClassByName = BuildClasses();

        internal static bool IsOpen => _canvasGo != null;

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
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
            catch (Exception e) { Plugin.Log?.LogError("[онлайн] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch (Exception e)
            {
                Plugin.Trace("[онлайн] закрытие: " + e.Message);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
            _canvasGo = null;
            _panelGo = null;
            _panel = null;
            _wrapper = null;
            _status = null;
            _title = null;
            _refresh = null;
            _filter = null;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_panelGo == null) { Close(); return; }
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
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLOnlineWindow", typeof(RectTransform), typeof(Image));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            _panelGo.GetComponent<Image>().color = new Color(0.16f, 0.12f, 0.08f, 0.98f);

            _title = Label(_panelGo.transform, "Кто в игре", 22, FontStyle.Bold, new Color32(255, 224, 130, 255));
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -44f), new Vector2(-60f, -8f));
            _title.alignment = TextAnchor.MiddleLeft;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-6f, -6f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -84f), new Vector2(-16f, -50f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _filter = MakeInput(barGo.transform, 260f, "поиск: ник, клан, класс");
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeButton(barGo.transform, "Обновить", 120f, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 14, FontStyle.Normal, new Color32(220, 205, 170, 255));
            _status.alignment = TextAnchor.MiddleLeft;
            _status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var areaGo = new GameObject("list", typeof(RectTransform), typeof(Image));
            areaGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)areaGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(12f, 12f), new Vector2(-12f, -92f));
            areaGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var prefab = VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab;
            var go = UnityEngine.Object.Instantiate(prefab, areaGo.transform, false);
            go.name = "QoLOnlineUserList";
            _panel = go.GetComponent<ChatUserListPanelContent>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            _wrapper = new ListWrapper<UserRowInfoMessage>(new List<UserRowInfoMessage>());
            var resolver = DependencyContainer.ResolveController<UserContextMenuController>().UserContextMenuResolver;
            _panel.Initialize(_wrapper, resolver);
            go.SetActive(true);
        }

        private static void Rebuild()
        {
            if (_wrapper == null) return;
            var players = OnlineList.Players;
            players.Sort((a, b) =>
            {
                int c = b.Level.CompareTo(a.Level);
                return c != 0 ? c : string.Compare(a.Login, b.Login, StringComparison.OrdinalIgnoreCase);
            });

            var rows = new List<UserRowInfoMessage>();
            foreach (var p in players)
            {
                if (_query.Length > 0
                    && Norm(p.Login).IndexOf(_query, StringComparison.Ordinal) < 0
                    && Norm(p.Clan).IndexOf(_query, StringComparison.Ordinal) < 0
                    && Norm(p.Class).IndexOf(_query, StringComparison.Ordinal) < 0) continue;
                rows.Add(Row(p));
            }

            try
            {
                _wrapper.BeginUpdate();
                _wrapper.Clear();
                _wrapper.Content.AddRange(rows);
                _wrapper.EndUpdate();
            }
            catch (Exception e) { Plugin.Trace("[онлайн] список: " + e.Message); }

            if (_title != null) _title.text = "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "") + (_query.Length > 0 ? " · показано " + rows.Count : "");
            if (_status != null)
                _status.text = players.Count == 0 && !OnlineList.Busy && !OnlineList.Configured
                    ? "Укажи запасной аккаунт в настройках мода, раздел «Кто в игре»"
                    : OnlineList.Status;
            if (_refresh != null) _refresh.interactable = !OnlineList.Busy;
        }

        private static UserRowInfoMessage Row(OnlinePlayer p)
        {
            var m = new UserRowInfoMessage(p.Id, p.Login, p.Level);
            m.ClanIcon = string.IsNullOrEmpty(p.Clan) ? null : p.Clan;
            m.RightsIcon = string.IsNullOrEmpty(p.Rights) ? null : p.Rights;
            if (p.Vip) m.Vip = true;
            if (p.Dealer) m.Dealer = true;
            if (p.Rank > 0) m.Rank = p.Rank;
            int cid;
            if (ClassByName.TryGetValue(Norm(p.Class), out cid)) m.ClassId = cid;
            return m;
        }

        private static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');

        private static Dictionary<string, int> BuildClasses()
        {
            var d = new Dictionary<string, int>();
            string[][] names =
            {
                new[] { "рейнджер", "стрелок", "лучник", "егерь", "снайпер", "древний", "завоеватель" },
                new[] { "варвар", "рубака", "гладиатор", "берсерк", "вождь", "жнец", "атаман" },
                new[] { "мастер щита", "щитоносец", "легионер", "центурион", "чемпион", "монолит", "полководец" },
                new[] { "джаггернаут", "защитник", "гвардеец", "рыцарь", "кавалер", "титан", "исполин" },
                new[] { "жрец", "священник", "клирик", "крестоносец", "паладин", "епископ", "кардинал" },
                new[] { "маг", "посвященный", "аколит", "адепт", "магистр", "патриарх", "властелин" },
            };
            for (int i = 0; i < names.Length; i++)
                foreach (var n in names[i]) d[n] = i + 1;
            return d;
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
