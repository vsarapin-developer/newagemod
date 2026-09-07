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
        private static SummonPlayerDialog _dlg;
        private static UserRowWidgetManager _grid;
        private static ListWrapper<UserRowInfoMessage> _wrapper;
        private static Text _caption;
        private static Text _status;
        private static Text _empty;
        private static GameObject _loading;
        private static GameObject _scroller;
        private static Button _refresh;
        private static InputField _filter;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;
        private static readonly Dictionary<string, string> RightsNames = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> ClassByName = BuildClasses();

        internal static bool IsOpen => _dlg != null;

        internal static void Toggle()
        {
            if (_dlg != null) { Close(); return; }
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
            var d = _dlg;
            Forget();
            if (d != null) { try { d.Close(); } catch { } }
        }

        private static void Forget()
        {
            _dlg = null;
            _grid = null;
            _wrapper = null;
            _caption = null;
            _status = null;
            _empty = null;
            _loading = null;
            _scroller = null;
            _refresh = null;
            _filter = null;
        }

        internal static void Tick()
        {
            if (_dlg == null) return;
            if (Time.unscaledTime < _pollAt) return;
            _pollAt = Time.unscaledTime + 0.2f;
            int v = OnlineList.Version;
            if (v == _seenVersion) return;
            _seenVersion = v;
            Rebuild();
        }

        private static T Field<T>(object host, string name) where T : class =>
            AccessTools.Field(host.GetType(), name)?.GetValue(host) as T
            ?? AccessTools.Field(typeof(SummonPlayerDialog), name)?.GetValue(host) as T;

        private static void Build()
        {
            Close();
            var dlg = DialogFactory.ShowSummonPlayerDialog();
            if (dlg == null) throw new Exception("диалог не создан");
            _dlg = dlg;
            dlg.OnDialogDestroy += () => { if (ReferenceEquals(_dlg, dlg)) Forget(); };

            _caption = Field<Text>(dlg, "Caption");
            _grid = Field<UserRowWidgetManager>(dlg, "WidgetManager");
            _empty = Field<Text>(dlg, "NoAviablePlayersText");
            var loading = Field<Transform>(dlg, "LoadingIndicator");
            _loading = loading != null ? loading.gameObject : null;
            var scroller = Field<Transform>(dlg, "ScrollerAreaTransform");
            _scroller = scroller != null ? scroller.gameObject : null;
            var button = Field<Button>(dlg, "Button");
            if (_grid == null) throw new Exception("в диалоге нет списка");

            var resolver = DependencyContainer.ResolveController<UserContextMenuController>().UserContextMenuResolver;
            _grid.ContextMenuResolver = resolver;
            _grid.OnItemClick += delegate { Close(); };
            _wrapper = new ListWrapper<UserRowInfoMessage>(new List<UserRowInfoMessage>());
            _grid.DataProvider = _wrapper;

            Transform barHost = dlg.transform;
            RectTransform slot = null;
            if (button != null)
            {
                slot = button.transform as RectTransform;
                barHost = button.transform.parent;
                button.gameObject.SetActive(false);
            }

            var barGo = new GameObject("QoLOnlineBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(barHost, false);
            var brt = (RectTransform)barGo.transform;
            if (slot != null)
            {
                brt.anchorMin = slot.anchorMin; brt.anchorMax = slot.anchorMax; brt.pivot = slot.pivot;
                brt.anchoredPosition = slot.anchoredPosition;
                brt.sizeDelta = new Vector2(Mathf.Max(slot.sizeDelta.x, 420f), Mathf.Max(slot.sizeDelta.y, 36f));
                var sle = slot.GetComponent<LayoutElement>();
                if (sle != null)
                {
                    var ble = barGo.AddComponent<LayoutElement>();
                    ble.preferredHeight = Mathf.Max(sle.preferredHeight, 36f);
                    ble.minHeight = ble.preferredHeight;
                    ble.flexibleWidth = 1f;
                }
                brt.SetSiblingIndex(slot.GetSiblingIndex());
            }
            else
            {
                brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0.5f, 0f);
                brt.offsetMin = new Vector2(16f, 12f); brt.offsetMax = new Vector2(-16f, 48f);
            }
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _filter = MakeInput(barGo.transform, 190f, "поиск по нику");
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeButton(barGo.transform, "Обновить", 110f, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 13, FontStyle.Normal, new Color(0.3f, 0.18f, 0.06f, 1f));
            _status.alignment = TextAnchor.MiddleLeft;
            var stle = _status.gameObject.AddComponent<LayoutElement>();
            stle.flexibleWidth = 1f; stle.minWidth = 120f;
        }

        private static void Rebuild()
        {
            if (_dlg == null || _wrapper == null) return;
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

            bool busy = OnlineList.Busy;
            bool nothing = players.Count == 0;
            if (_caption != null) _caption.text = "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "") + (_query.Length > 0 ? " (" + rows.Count + ")" : "");
            if (_status != null) _status.text = OnlineList.Status;
            if (_refresh != null) _refresh.interactable = !busy;
            if (_loading != null) _loading.SetActive(busy && nothing);
            if (_scroller != null) _scroller.SetActive(!nothing);
            if (_empty != null)
            {
                _empty.gameObject.SetActive(nothing && !busy);
                _empty.text = OnlineList.Configured ? OnlineList.Status : "Укажи логин и пароль запасного аккаунта в настройках мода, раздел «Кто в игре»";
            }
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
            img.color = new Color(0f, 0f, 0f, 0.55f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 32f;

            var txt = Label(go.transform, "", 16, FontStyle.Normal, Color.white);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 3f), new Vector2(-8f, -3f));

            var ph = Label(go.transform, placeholder, 16, FontStyle.Italic, new Color(1f, 1f, 1f, 0.4f));
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
            go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.15f, 1f);
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
