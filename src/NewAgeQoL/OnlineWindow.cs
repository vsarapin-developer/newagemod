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
        private const float PanelW = 640f;
        private const float PanelH = 700f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static ChatUserListPanelContent _panel;
        private static ListWrapper<UserRowInfoMessage> _wrapper;
        private static Text _title;
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
            _title = null;
            _status = null;
            _refresh = null;
            _filter = null;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_panelGo == null) { Close(); return; }
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

            _panelGo = new GameObject("QoLOnlineWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            _panelGo.GetComponent<Image>().color = new Color(0.14f, 0.10f, 0.07f, 0.97f);
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            _title = Label(_panelGo.transform, "Кто в игре", 24, FontStyle.Bold, new Color32(255, 224, 130, 255));
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(18f, -50f), new Vector2(-70f, -8f));
            _title.alignment = TextAnchor.MiddleLeft;

            MakeCloseButton();

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -98f), new Vector2(-16f, -56f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            _filter = MakeInput(barGo.transform, 250f, "поиск по нику");
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeGameButton(barGo.transform, "Обновить", 140f, 40f, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 14, FontStyle.Normal, new Color32(220, 205, 170, 255));
            _status.alignment = TextAnchor.MiddleLeft;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f; sle.preferredHeight = 40f;

            var areaGo = new GameObject("list", typeof(RectTransform), typeof(Image));
            areaGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)areaGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, 14f), new Vector2(-14f, -106f));
            areaGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var content = UnityEngine.Object.Instantiate(VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab, areaGo.transform, false);
            content.name = "QoLOnlineUserList";
            _panel = content.GetComponent<ChatUserListPanelContent>();
            var rt = (RectTransform)content.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            _wrapper = new ListWrapper<UserRowInfoMessage>(new List<UserRowInfoMessage>());
            var resolver = DependencyContainer.ResolveController<UserContextMenuController>().UserContextMenuResolver;
            _panel.Initialize(_wrapper, resolver);
            content.SetActive(true);
        }

        private static void MakeCloseButton()
        {
            GameObject go = null;
            try
            {
                var proto = VisualPrefabsHolder.Instance.SummonPlayerDialog?.GetComponent<SummonPlayerDialog>()?.CloseButton;
                if (proto != null)
                {
                    go = UnityEngine.Object.Instantiate(proto.gameObject, _panelGo.transform, false);
                    go.name = "QoLClose";
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(14f, 14f);
                    rt.localScale = Vector3.one * 0.8f;
                    var b = go.GetComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(Close);
                    go.SetActive(true);
                }
            }
            catch (Exception e) { Plugin.Trace("[онлайн] крестик игры не взялся: " + e.Message); if (go != null) UnityEngine.Object.Destroy(go); go = null; }
            if (go != null) return;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-8f, -8f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static Button MakeGameButton(Transform host, string text, float width, float height, Action onClick)
        {
            GameObject go = null;
            try
            {
                var prefab = VisualPrefabsHolder.Instance.GreenButtonPrefab ?? VisualPrefabsHolder.Instance.DefaultButtonPrefab;
                if (prefab != null)
                {
                    go = UnityEngine.Object.Instantiate(prefab, host, false);
                    go.name = "QoLRefresh";
                    var t = go.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = text;
                    var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                    le.preferredWidth = width; le.minWidth = width; le.preferredHeight = height; le.minHeight = height;
                    var b = go.GetComponent<Button>();
                    if (b == null) throw new Exception("у префаба нет Button");
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => onClick());
                    go.SetActive(true);
                    return b;
                }
            }
            catch (Exception e) { Plugin.Trace("[онлайн] кнопка игры не взялась: " + e.Message); if (go != null) UnityEngine.Object.Destroy(go); }

            var fb = new GameObject("btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            fb.transform.SetParent(host, false);
            fb.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.15f, 1f);
            var fle = fb.GetComponent<LayoutElement>();
            fle.preferredWidth = width; fle.minWidth = width; fle.preferredHeight = height;
            var ft = Label(fb.transform, text, 16, FontStyle.Bold, Color.white);
            Place(ft.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var btn = fb.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            return btn;
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
                var sr = _panel != null ? AccessTools.Field(typeof(ChatUserListPanelContent), "ScrollRect")?.GetValue(_panel) as LoopScrollRect : null;
                if (sr != null) sr.ClearCells();
                _wrapper.BeginUpdate();
                _wrapper.Clear();
                foreach (var r in rows) _wrapper.AddItem(r);
                _wrapper.EndUpdate();
                _wrapper.Refresh();
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[онлайн] список: " + e.Message); }

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
            var go = new GameObject("filter", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
            var ol = go.GetComponent<Outline>();
            ol.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            ol.effectDistance = new Vector2(1f, -1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 40f; le.minHeight = 40f;

            var txt = Label(go.transform, "", 17, FontStyle.Normal, new Color32(245, 235, 210, 255));
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));

            var ph = Label(go.transform, placeholder, 17, FontStyle.Italic, new Color(1f, 1f, 1f, 0.35f));
            ph.alignment = TextAnchor.MiddleLeft;
            Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = new Color32(245, 235, 210, 255);
            input.customCaretColor = true;
            return input;
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
