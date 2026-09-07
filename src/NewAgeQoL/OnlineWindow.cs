using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Common.User;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class OnlineWindow
    {
        private const float WinW = 560f;
        private const float WinH = 720f;
        private const float BarH = 40f;

        private static GameObject _canvasGo;
        private static GameObject _winGo;
        private static ChatUserListPanelContent _panel;
        private static ListWrapper<UserRowInfoMessage> _wrapper;
        private static Text _header;
        private static Text _status;
        private static Button _refresh;
        private static InputField _filter;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;
        private static readonly Action CloseAction = Close;
        private static bool _registered;
        private static readonly Dictionary<string, string> RightsNames = new Dictionary<string, string>();

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
                ModalDialogList.Add(CloseAction);
                _registered = true;
                _seenVersion = -1;
                if (!OnlineList.Busy) OnlineList.Refresh();
                Rebuild();
            }
            catch (Exception e) { Plugin.Log?.LogError("[онлайн] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            if (_registered) { try { ModalDialogList.Remove(CloseAction); } catch { } _registered = false; }
            try
            {
                if (_winGo != null) UnityEngine.Object.Destroy(_winGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch (Exception e)
            {
                Plugin.Trace("[онлайн] закрытие: " + e.Message);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
            _canvasGo = null;
            _winGo = null;
            _panel = null;
            _wrapper = null;
            _header = null;
            _status = null;
            _refresh = null;
            _filter = null;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_winGo == null) { Close(); return; }
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

            var prefab = VisualPrefabsHolder.Instance.DesktopChatUserWindowPrefab;
            _winGo = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            _winGo.name = "QoLOnlineWindow";
            var wrt = (RectTransform)_winGo.transform;
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(WinW, WinH);
            wrt.anchoredPosition = Vector2.zero;
            wrt.localScale = Vector3.one;
            var wle = _winGo.GetComponent<LayoutElement>();
            if (wle != null) wle.ignoreLayout = true;

            var win = _winGo.GetComponent<StandardContentWindowPanel>();
            win.SetWithoutTabsPanelHeader("gui.chat.user_list.header");
            var tab = AccessTools.Field(typeof(StandardContentWindowPanel), "TabPanel")?.GetValue(win) as TabPanel;
            _header = tab != null ? tab.TabHeaderText : null;
            var parent = AccessTools.Field(typeof(StandardContentWindowPanel), "PanelContentParent")?.GetValue(win) as Transform;
            if (parent == null) throw new Exception("у окна нет контейнера содержимого");
            bool layout = parent.GetComponent<LayoutGroup>() != null;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_winGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(30f, 30f); crt.anchoredPosition = new Vector2(-4f, -4f);
            closeGo.GetComponent<Image>().color = new Color(0.65f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 18, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var content = UnityEngine.Object.Instantiate(VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab);
            content.name = "QoLOnlineUserList";
            _panel = content.GetComponent<ChatUserListPanelContent>();
            _wrapper = new ListWrapper<UserRowInfoMessage>(new List<UserRowInfoMessage>());
            var resolver = DependencyContainer.ResolveController<UserContextMenuController>().UserContextMenuResolver;
            _panel.Initialize(_wrapper, resolver);
            win.AddPanelContent(_panel);
            content.SetActive(true);

            var barGo = new GameObject("QoLOnlineBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(parent, false);
            barGo.transform.SetAsFirstSibling();
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var crt2 = (RectTransform)content.transform;
            if (layout)
            {
                var ble = barGo.AddComponent<LayoutElement>();
                ble.preferredHeight = BarH; ble.minHeight = BarH; ble.flexibleWidth = 1f;
                var cle = content.GetComponent<LayoutElement>() ?? content.AddComponent<LayoutElement>();
                cle.flexibleHeight = 1f; cle.flexibleWidth = 1f;
            }
            else
            {
                Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -BarH), new Vector2(0f, 0f));
                crt2.anchorMin = Vector2.zero; crt2.anchorMax = Vector2.one;
                crt2.offsetMin = Vector2.zero; crt2.offsetMax = new Vector2(0f, -BarH);
                crt2.localScale = Vector3.one;
            }

            _filter = MakeInput(barGo.transform, 200f, "поиск по нику");
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeButton(barGo.transform, "Обновить", 110f, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 13, FontStyle.Normal, new Color(0.25f, 0.15f, 0.05f, 1f));
            _status.alignment = TextAnchor.MiddleLeft;
            _status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
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
            OnlineList.AskClanCodes(players);

            var rows = new List<UserRowInfoMessage>();
            foreach (var p in players)
            {
                if (_query.Length > 0 && Norm(p.Login).IndexOf(_query, StringComparison.Ordinal) < 0) continue;
                rows.Add(Row(p));
            }

            try
            {
                _wrapper.BeginUpdate();
                _wrapper.Clear();
                foreach (var r in rows) _wrapper.AddItem(r);
                _wrapper.EndUpdate();
                _wrapper.Refresh();
            }
            catch (Exception e) { Plugin.Trace("[онлайн] список: " + e.Message); }

            if (_header != null) _header.text = "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "") + (_query.Length > 0 ? " (" + rows.Count + ")" : "");
            if (_status != null)
                _status.text = players.Count == 0 && !OnlineList.Busy && !OnlineList.Configured
                    ? "Укажи запасной аккаунт в настройках мода"
                    : OnlineList.Status;
            if (_refresh != null) _refresh.interactable = !OnlineList.Busy;
        }

        private static UserRowInfoMessage Row(OnlinePlayer p)
        {
            var m = new UserRowInfoMessage(p.Id, p.Login, p.Level);
            int code;
            if (OnlineList.ClanCode(p.Clan, out code)) m.ClanIconCode = code;
            m.RightsIcon = RightsSprite(p.Rights);
            if (p.Vip) m.Vip = true;
            if (p.Dealer) m.Dealer = true;
            if (p.Rank > 0) m.Rank = p.Rank;
            int cid;
            if (ClassByName.TryGetValue(Norm(p.Class), out cid)) m.ClassId = cid;
            return m;
        }

        private static string RightsSprite(string rights)
        {
            if (string.IsNullOrEmpty(rights)) return null;
            string found;
            if (RightsNames.TryGetValue(rights, out found)) return found;
            found = null;
            foreach (var name in new[] { rights, "AdminIcon", "ModerIcon", "ModeratorIcon", "OperatorIcon", "OpIcon", "HelperIcon", "InfoIcon", "SupportIcon" })
            {
                try
                {
                    var s = AtlasUtils.GetUserRowIcon(name);
                    if (s != null && s.name != "unknown") { found = name; break; }
                }
                catch { }
            }
            RightsNames[rights] = found;
            Plugin.Trace("[онлайн] значок прав «" + rights + "» → " + (found ?? "нет"));
            return found;
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
            img.color = new Color(1f, 1f, 1f, 0.55f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 30f;

            var txt = Label(go.transform, "", 15, FontStyle.Normal, new Color(0.1f, 0.06f, 0.02f, 1f));
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 3f), new Vector2(-8f, -3f));

            var ph = Label(go.transform, placeholder, 15, FontStyle.Italic, new Color(0.1f, 0.06f, 0.02f, 0.45f));
            ph.alignment = TextAnchor.MiddleLeft;
            Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 3f), new Vector2(-8f, -3f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button MakeButton(Transform host, string text, float width, Action onClick)
        {
            var go = new GameObject("btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.15f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 30f;
            var t = Label(go.transform, text, 15, FontStyle.Bold, Color.white);
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
