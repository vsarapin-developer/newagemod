using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Common.User;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class OnlineWindow
    {
        private const float BarH = 42f;
        private const float PanelW = 420f;
        private const float PanelH = 480f;
        private const float MinH = 260f;
        private const float TopH = 56f;
        private const float GripH = 14f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Transform _rows;
        private static ScrollRect _scroll;
        private static GameObject _rowPrefab;
        private static UserContextMenuResolver _resolver;
        private static Text _title;
        private static Text _status;
        private static CanvasGroup _rowsFade;
        private static Button _refresh;
        private static InputField _filter;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;
        private static float _iconAt;
        private static bool _wantOpen;
        private static float _reopenAt;
        private static Sprite _round16;
        private static Sprite _round8;
        private static readonly List<KeyValuePair<UserRowWidget, OnlinePlayer>> Pending = new List<KeyValuePair<UserRowWidget, OnlinePlayer>>();
        private static readonly Dictionary<string, string> RightsNames = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> ClassByName = BuildClasses();

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Open();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        internal static void Open()
        {
            try
            {
                Build();
                _wantOpen = true;
                _seenVersion = -1;
                if (!OnlineList.Busy) OnlineList.Refresh();
                Rebuild();
            }
            catch (Exception e) { Plugin.Log?.LogError("[онлайн] окно: " + e); Close(); }
        }

        private static void Reopen()
        {
            try
            {
                Build();
                _seenVersion = -1;
                Rebuild();
                Plugin.Trace("[онлайн] окно пересоздано после смены сцены");
            }
            catch (Exception e) { Plugin.Trace("[онлайн] пересоздание: " + e.Message); Teardown(); }
        }

        internal static void Close()
        {
            _wantOpen = false;
            Teardown();
        }

        private static void Teardown()
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
            _rows = null;
            _rowsFade = null;
            _scroll = null;
            _title = null;
            _status = null;
            _refresh = null;
            _filter = null;
            Pending.Clear();
        }

        private static string _keySpec;
        private static KeyCode _key = KeyCode.None;

        private static void HotkeyTick()
        {
            if (Settings.Capturing) return;
            string spec = (Plugin.CfgOnlineHotkey?.Value ?? "").Trim();
            if (spec != _keySpec)
            {
                _keySpec = spec;
                KeyCode k;
                _key = spec.Length > 0 && Enum.TryParse(spec, true, out k) ? k : KeyCode.None;
                if (spec.Length > 0 && _key == KeyCode.None) Plugin.Log?.LogWarning("[онлайн] клавиша «" + spec + "» не распознана, окно только по кнопке");
            }
            if (_key == KeyCode.None || !Input.GetKeyDown(_key)) return;
            try
            {
                var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (sel != null && sel.GetComponent<InputField>() != null) return;
            }
            catch { }
            Toggle();
        }

        internal static string KeyHint()
        {
            string spec = (Plugin.CfgOnlineHotkey?.Value ?? "").Trim();
            return spec.Length > 0 ? " (" + spec + ")" : "";
        }

        internal static void Tick()
        {
            HotkeyTick();
            if (_wantOpen && _panelGo == null)
            {
                if (Time.unscaledTime < _reopenAt) return;
                _reopenAt = Time.unscaledTime + 0.5f;
                Teardown();
                if (SideButtons.InWorld()) Reopen();
                return;
            }
            if (_canvasGo == null) return;
            if (Time.unscaledTime >= _iconAt)
            {
                _iconAt = Time.unscaledTime + 1f;
                RetryIcons();
            }
            if (Time.unscaledTime < _pollAt) return;
            _pollAt = Time.unscaledTime + 0.2f;
            int v = OnlineList.Version;
            if (v == _seenVersion) return;
            _seenVersion = v;
            Rebuild();
        }

        private static void Build()
        {
            Teardown();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            var holder = VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab?.GetComponent<ChatUserListPanelContent>();
            _rowPrefab = holder != null ? AccessTools.Field(typeof(ChatUserListPanelContent), "SmallUserRowPrefab")?.GetValue(holder) as GameObject : null;
            if (_rowPrefab == null) throw new Exception("не нашёл префаб строки игрока");
            _resolver = DependencyContainer.ResolveController<UserContextMenuController>().UserContextMenuResolver;

            _panelGo = new GameObject("QoLOnlineWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 1f);
            float h; Vector2 pos;
            LoadRect(out pos, out h);
            prt.sizeDelta = new Vector2(PanelW, h);
            prt.anchoredPosition = pos;
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = new Color(0.14f, 0.10f, 0.07f, 0.97f);
            pimg.sprite = Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopH), new Vector2(0f, 0f));
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;
            mover.OnDone = SaveRect;

            var gripGo = new GameObject("grip", typeof(RectTransform), typeof(Image), typeof(DragResize));
            gripGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)gripGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, GripH));
            gripGo.GetComponent<Image>().color = new Color(0.55f, 0.42f, 0.22f, 0.35f);
            var sizer = gripGo.GetComponent<DragResize>();
            sizer.Target = prt;
            sizer.Canvas = canvas;
            sizer.Min = MinH;
            sizer.OnDone = SaveRect;
            var gripMark = Label(gripGo.transform, "• • •", 12, FontStyle.Bold, new Color32(230, 210, 170, 200));
            Place(gripMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            _title = Label(_panelGo.transform, "Кто в игре", 24, FontStyle.Bold, new Color32(255, 224, 130, 255));
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(18f, -50f), new Vector2(-70f, -8f));
            _title.raycastTarget = false;
            _title.alignment = TextAnchor.MiddleLeft;

            MakeCloseButton(_panelGo.transform, Close);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -98f), new Vector2(-16f, -56f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _filter = MakeInput(barGo.transform, 168f, "поиск по нику");
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeGameButton(barGo.transform, "Обновить", 118f, BarH, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 13, FontStyle.Normal, new Color32(220, 205, 170, 255));
            _status.alignment = TextAnchor.MiddleLeft;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 9;
            _status.resizeTextMaxSize = 13;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f; sle.preferredHeight = BarH; sle.minWidth = 70f;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, GripH + 6f), new Vector2(-30f, -106f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.25f);
            simg.sprite = Rounded(8);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 35f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = cont;
            _scroll.viewport = srt;
            _rows = contentGo.transform;
            _rowsFade = contentGo.AddComponent<CanvasGroup>();

            MakeScrollbar(_panelGo.transform);
        }

        private static void LoadRect(out Vector2 pos, out float h)
        {
            pos = new Vector2(0f, PanelH * 0.5f);
            h = PanelH;
            try
            {
                var parts = (Plugin.CfgOnlineWindow?.Value ?? "").Split(';');
                if (parts.Length == 3)
                {
                    float x, y, hh;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out x)
                        && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out y)
                        && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, ci, out hh))
                    {
                        pos = new Vector2(x, y);
                        h = Mathf.Max(MinH, hh);
                    }
                }
            }
            catch { }
        }

        private static void SaveRect()
        {
            try
            {
                if (_panelGo == null || Plugin.CfgOnlineWindow == null) return;
                var rt = (RectTransform)_panelGo.transform;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Plugin.CfgOnlineWindow.Value = rt.anchoredPosition.x.ToString("0", ci) + ";" + rt.anchoredPosition.y.ToString("0", ci) + ";" + rt.sizeDelta.y.ToString("0", ci);
            }
            catch { }
        }

        private static void MakeScrollbar(Transform host)
        {
            var go = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-26f, GripH + 6f), new Vector2(-14f, -106f));
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var sb = go.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            var hrt = (RectTransform)handle.transform;
            Place(hrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handle.GetComponent<Image>().color = new Color(0.62f, 0.5f, 0.3f, 0.9f);
            sb.handleRect = hrt;
            sb.targetGraphic = handle.GetComponent<Image>();
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static void Rebuild()
        {
            if (_rows == null) return;
            var players = OnlineList.Players;
            players.Sort((a, b) =>
            {
                int c = b.Level.CompareTo(a.Level);
                return c != 0 ? c : string.Compare(a.Login, b.Login, StringComparison.OrdinalIgnoreCase);
            });
            OnlineList.AskClanCodes(players);

            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            Pending.Clear();

            int shown = 0;
            foreach (var p in players)
            {
                if (_query.Length > 0 && Norm(p.Login).IndexOf(_query, StringComparison.Ordinal) < 0) continue;
                try { AddRow(p); shown++; }
                catch (Exception e) { Plugin.Log?.LogWarning("[онлайн] строка " + p.Login + ": " + e.Message); }
            }

            bool busy = OnlineList.Busy;
            if (_title != null) _title.text = busy
                ? "Кто в игре: обновляю…"
                : "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "") + (_query.Length > 0 ? " · показано " + shown : "");
            if (_rowsFade != null) _rowsFade.alpha = busy ? 0.4f : 1f;
            if (_status != null)
            {
                string st = OnlineList.Status ?? "";
                int dot = st.IndexOf("· ", StringComparison.Ordinal);
                if (st.StartsWith("В игре:", StringComparison.Ordinal) && dot >= 0) st = st.Substring(dot + 2);
                _status.text = players.Count == 0 && !OnlineList.Busy && !OnlineList.Configured
                    ? "нет запасного аккаунта"
                    : st;
            }
            if (_refresh != null) _refresh.interactable = !busy;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static void AddRow(OnlinePlayer p)
        {
            var go = UnityEngine.Object.Instantiate(_rowPrefab, _rows, false);
            Clones.StripHotkeys(go, _rowPrefab);
            go.name = "QoLRow";
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            float h = rt.rect.height > 4f ? rt.rect.height : (rt.sizeDelta.y > 4f ? rt.sizeDelta.y : 34f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h; le.flexibleWidth = 1f;

            var w = go.GetComponent<UserRowWidget>();
            if (w == null) throw new Exception("в префабе нет UserRowWidget");
            var msg = Row(p);
            w.Data = msg;
            var resolver = _resolver;
            w.OnItemClickDelegate = item => { try { PrivateToasts.WritePrivate(p.Id, p.Login); } catch (Exception e) { Plugin.Trace("[онлайн] клик: " + e.Message); } };
            w.OnRightButtonClickDelegate = item =>
            {
                try { ContextMenu.ShowContextMenu(go, resolver, item.Data, EContextMenuSide.Left); }
                catch (Exception e) { Plugin.Trace("[онлайн] меню: " + e.Message); }
            };
            if (msg.ClanIconCode.HasValue && ClanIconCodeMaker.GetIcon(msg.ClanIconCode.Value) == null)
                Pending.Add(new KeyValuePair<UserRowWidget, OnlinePlayer>(w, p));
        }

        private static void RetryIcons()
        {
            if (Pending.Count == 0) return;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var w = Pending[i].Key;
                var p = Pending[i].Value;
                if (w == null) { Pending.RemoveAt(i); continue; }
                int code;
                if (!OnlineList.ClanCode(p.Clan, out code)) { Pending.RemoveAt(i); continue; }
                Sprite s = null;
                try { s = ClanIconCodeMaker.GetIcon(code); } catch { }
                if (s == null) continue;
                try { w.Data = Row(p); } catch { }
                Pending.RemoveAt(i);
            }
        }

        internal static void MakeCloseButton(Transform host, Action onClose)
        {
            GameObject go = null;
            try
            {
                var proto = VisualPrefabsHolder.Instance.SummonPlayerDialog?.GetComponent<SummonPlayerDialog>()?.CloseButton;
                if (proto != null)
                {
                    go = UnityEngine.Object.Instantiate(proto.gameObject, host, false);
                    Clones.StripHotkeys(go, proto.gameObject);
                    go.name = "QoLClose";
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(14f, 14f);
                    rt.localScale = Vector3.one * 0.8f;
                    var b = go.GetComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => onClose());
                    go.SetActive(true);
                }
            }
            catch (Exception e) { Plugin.Trace("[онлайн] крестик игры не взялся: " + e.Message); if (go != null) UnityEngine.Object.Destroy(go); go = null; }
            if (go != null) return;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(host, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-8f, -8f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(() => onClose());
            var x = Label(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static Button MakeGameButton(Transform host, string text, float width, float height, Action onClick)
        {
            Sprite sprite = null;
            Text proto = null;
            try
            {
                var prefab = VisualPrefabsHolder.Instance.GreenButtonPrefab ?? VisualPrefabsHolder.Instance.DefaultButtonPrefab;
                if (prefab != null)
                {
                    var img = prefab.GetComponent<Image>() ?? prefab.GetComponentInChildren<Image>(true);
                    if (img != null) sprite = img.sprite;
                    proto = prefab.GetComponentInChildren<Text>(true);
                }
            }
            catch (Exception e) { Plugin.Trace("[онлайн] спрайт кнопки не взялся: " + e.Message); }

            var go = new GameObject("QoLRefresh", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var image = go.GetComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.color = Color.white;
            }
            else image.color = new Color(0.2f, 0.45f, 0.15f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = height; le.minHeight = height;

            var t = Label(go.transform, text, 16, FontStyle.Bold, Color.white);
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 9f), new Vector2(-16f, -9f));
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = 16;
            if (proto != null)
            {
                try
                {
                    if (proto.font != null) t.font = proto.font;
                    t.color = proto.color;
                    t.fontStyle = proto.fontStyle;
                    var po = proto.GetComponent<Outline>();
                    if (po != null) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = po.effectColor; o.effectDistance = po.effectDistance; }
                    var ps = proto.GetComponent<Shadow>();
                    if (ps != null && po == null) { var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = ps.effectColor; sh.effectDistance = ps.effectDistance; }
                }
                catch { }
            }

            var b = go.GetComponent<Button>();
            b.targetGraphic = image;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            return b;
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
            img.color = new Color(0.07f, 0.05f, 0.03f, 0.95f);
            img.sprite = Rounded(8);
            img.type = Image.Type.Sliced;
            var ol = go.GetComponent<Outline>();
            ol.effectColor = new Color(0.62f, 0.48f, 0.26f, 0.9f);
            ol.effectDistance = new Vector2(1f, -1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = BarH; le.minHeight = BarH;

            var txt = Label(go.transform, "", 16, FontStyle.Normal, new Color32(245, 235, 210, 255));
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));

            var ph = Label(go.transform, placeholder, 16, FontStyle.Italic, new Color(1f, 1f, 1f, 0.35f));
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

        internal static Sprite Rounded(int radius)
        {
            if (radius >= 12 && _round16 != null) return _round16;
            if (radius < 12 && _round8 != null) return _round8;
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x < radius ? radius - x - 0.5f : (x >= size - radius ? x - (size - radius) + 0.5f : 0f);
                    float dy = y < radius ? radius - y - 0.5f : (y >= size - radius ? y - (size - radius) + 0.5f : 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= radius - 1f ? 1f : (d >= radius ? 0f : radius - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            if (radius >= 12) _round16 = sp; else _round8 = sp;
            return sp;
        }

        internal static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        internal static Text Label(Transform host, string text, int size, FontStyle style, Color color)
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

    internal sealed class DragMove : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        internal RectTransform Target;
        internal Canvas Canvas;
        internal Action OnDone;

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            float s = Canvas != null && Canvas.scaleFactor > 0f ? Canvas.scaleFactor : 1f;
            var p = Target.anchoredPosition + e.delta / s;
            var area = Canvas != null ? (RectTransform)Canvas.transform : null;
            if (area != null)
            {
                float hw = area.rect.width * 0.5f, hh = area.rect.height * 0.5f;
                p.x = Mathf.Clamp(p.x, -hw + Target.rect.width * 0.5f, hw - Target.rect.width * 0.5f);
                p.y = Mathf.Clamp(p.y, -hh + 40f, hh);
            }
            Target.anchoredPosition = p;
        }

        public void OnEndDrag(PointerEventData e) => OnDone?.Invoke();
    }

    internal sealed class DragResize : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        internal RectTransform Target;
        internal Canvas Canvas;
        internal float Min = 200f;
        internal Action OnDone;

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            float s = Canvas != null && Canvas.scaleFactor > 0f ? Canvas.scaleFactor : 1f;
            var area = Canvas != null ? (RectTransform)Canvas.transform : null;
            float max = area != null ? area.rect.height - 20f : 2000f;
            float h = Mathf.Clamp(Target.sizeDelta.y - e.delta.y / s, Min, max);
            Target.sizeDelta = new Vector2(Target.sizeDelta.x, h);
        }

        public void OnEndDrag(PointerEventData e) => OnDone?.Invoke();
    }
}
