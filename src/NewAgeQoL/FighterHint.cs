using System;
using System.Collections.Generic;
using System.Text;
using Transport.Messages.Responses.Combat.States;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FighterHint
    {
        private const float Look = 0.08f;
        private const float Wait = 0.45f;
        private const float Fresh = 3f;
        private const float BoardW = 510f;
        private static int _rowIndex;

        private static float _lookAt, _hoverAt;
        private static int _hoverId, _shownId;
        private static object _on;
        private static Canvas _canvas;
        private static RectTransform _host;
        private static RectTransform _board;
        private static CanvasGroup _veil;
        private static Text _title, _meta, _bars, _fxHead;
        private static Transform _rows;
        private static string _rowsSig = "";
        private static bool _reveal;
        internal static readonly Dictionary<int, List<UserEnchantmentsResponseItem>> States = new Dictionary<int, List<UserEnchantmentsResponseItem>>();
        internal static readonly Dictionary<int, float> AskedAt = new Dictionary<int, float>();

        internal static bool Enabled => Plugin.CfgFighterHint == null || Plugin.CfgFighterHint.Value;

        internal static void Tick()
        {
            try
            {
                Listen();
                if (!Enabled || !SideButtons.InCombat()) { Hide(); return; }
                if (Time.unscaledTime < _lookAt) return;
                _lookAt = Time.unscaledTime + Look;

                var cd = Cd();
                int id = cd == null ? 0 : Under(cd);
                if (id == 0 || Unity3DHelper.IsOverInterface()) { Hide(); return; }
                if (id != _hoverId)
                {
                    Hide();
                    _hoverId = id;
                    _hoverAt = Time.unscaledTime + Wait;
                    Ask(id);
                    return;
                }
                if (Time.unscaledTime < _hoverAt) return;
                var ch = cd.GetCharacter(id);
                if (ch == null) { Hide(); return; }

                Build();
                bool fresh = _shownId != id || !_board.gameObject.activeSelf;
                Fill(ch, cd);
                _shownId = id;
                if (fresh)
                {
                    _veil.alpha = 0f;
                    _reveal = true;
                    _board.gameObject.SetActive(true);
                    return;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(_board);
                Place();
                if (_reveal) { _reveal = false; _veil.alpha = 1f; }
                float at;
                if (!AskedAt.TryGetValue(id, out at) || Time.unscaledTime - at > Fresh) Ask(id);
            }
            catch (Exception e) { Plugin.Trace("[боец] подсказка: " + e.Message); }
        }

        private static void Hide()
        {
            _hoverId = 0;
            _shownId = 0;
            if (_board != null && _board.gameObject.activeSelf) _board.gameObject.SetActive(false);
        }

        internal static ICombatData Cd()
        {
            try { return DependencyContainer.GetContainer()?.Resolve<IUserData>()?.CombatData; }
            catch { return null; }
        }

        private static int Under(ICombatData cd)
        {
            var view = CombatLocationView.Instance;
            var camera = view != null ? view.CombatCamera : null;
            if (camera == null) return 0;
            var hits = Physics.RaycastAll(camera.ScreenPointToRay(Input.mousePosition), 1000f);
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var found = cd.FindCharacterByGameObject(hit.collider.gameObject);
                if (found != null && found.UserId != 0) return found.UserId;
            }
            return 0;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(109, OnStates);
            nc.AddMessageListener(109, OnStates);
            _on = nc;
        }

        private static void OnStates(object m)
        {
            var msg = m as UserEnchantmentsResponseMessage;
            if (msg == null) return;
            States[msg.UserId] = msg.Items != null ? new List<UserEnchantmentsResponseItem>(msg.Items) : new List<UserEnchantmentsResponseItem>();
        }

        internal static bool Silent(int userId)
        {
            float at;
            return AskedAt.TryGetValue(userId, out at) && Time.unscaledTime - at > 2.5f;
        }

        internal static void Ask(int userId)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                AskedAt[userId] = Time.unscaledTime;
                nc.SendRequest(new UserEnchantmentsRequest(userId));
            }
            catch (Exception e) { Plugin.Trace("[боец] запрос состояний: " + e.Message); }
        }

        private static void Fill(AbstractCharacter ch, ICombatData cd)
        {
            bool me = cd.MyCharacter != null && cd.MyCharacter.UserId == ch.UserId;
            _title.text = (ch.Login ?? "?") + (me ? "  (ты)" : "");
            var meta = new StringBuilder();
            if (ch.Level > 0) meta.Append("Уровень: ").Append(ch.Level);
            if (ch.Rank > 0) meta.Append(meta.Length > 0 ? "      " : "").Append("Рейтинг: ").Append(ch.Rank);
            _meta.text = meta.ToString();
            _meta.gameObject.SetActive(meta.Length > 0);

            var ind = ch.Indicators;
            if (ind != null)
            {
                _bars.text = "<color=#ff6a5a>Жизнь " + ind.CurrentLife + " / " + ind.MaxLife + "</color>     "
                           + "<color=#6db3ff>Мана " + ind.CurrentMana + " / " + ind.MaxMana + "</color>     "
                           + "<color=#ffd257>Энергия " + (me ? ind.CurrentStamina + " / " + ind.MaxStamina : "?") + "</color>";
                _bars.gameObject.SetActive(true);
            }
            else _bars.gameObject.SetActive(false);

            List<UserEnchantmentsResponseItem> items;
            bool known = States.TryGetValue(ch.UserId, out items);
            var sig = new StringBuilder();
            sig.Append(ch.UserId).Append('|');
            if (known)
                foreach (var it in items)
                {
                    sig.Append(it.StateType).Append(':').Append(it.StateId).Append(':').Append(it.Duration).Append(':').Append(it.Highlighting).Append(':').Append(Power(it)).Append(':');
                    if (it.Sources != null) foreach (var s in it.Sources) sig.Append(s.SourceUserId).Append('/');
                    sig.Append(';');
                }
            else sig.Append(Silent(ch.UserId) ? "?!" : "?");
            string s2 = sig.ToString();
            if (s2 == _rowsSig) return;
            _rowsSig = s2;

            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _rowIndex = 0;
            if (!known) { AddRow(Silent(ch.UserId) ? "эффекты не пришли" : "загружаю…", "", "", "", new Color32(230, 215, 180, 255)); return; }
            if (items.Count == 0) { AddRow("нет эффектов", "", "", "", new Color32(230, 215, 180, 255)); return; }
            AddRow("Название", "Источник", "Эффект", "Длительность", new Color32(255, 224, 130, 255), true);
            Rule(_rows);
            foreach (var it in items)
            {
                string key = "states.state_" + it.StateType + "_" + it.StateId;
                string name = ResourceStrings.GetString(key + ".name");
                if (name == key + ".name") name = "состояние " + it.StateType + "/" + it.StateId;
                string src = Sources(it, cd, ch.UserId);
                int power = Power(it);
                string dur = it.Duration > 1000 ? "до конца боя" : it.Duration > 0 ? it.Duration + " " + Turns(it.Duration) : "";
                Color32 col = it.Highlighting == (int)EHighlightingType.Positive ? new Color32(120, 230, 120, 255)
                            : it.Highlighting == (int)EHighlightingType.Negative ? new Color32(255, 110, 100, 255)
                            : new Color32(235, 225, 200, 255);
                AddRow(name, src, power != 0 ? power.ToString() : "", dur, col);
            }
        }

        internal static string Turns(int n)
        {
            int a = n % 10, b = n % 100;
            if (b >= 11 && b <= 14) return "ходов";
            if (a == 1) return "ход";
            if (a >= 2 && a <= 4) return "хода";
            return "ходов";
        }

        internal static int Power(UserEnchantmentsResponseItem it)
        {
            int sum = 0;
            if (it.Sources != null) foreach (var s in it.Sources) sum += s.Power;
            return sum;
        }

        internal static string Sources(UserEnchantmentsResponseItem it, ICombatData cd, int self)
        {
            if (it.Sources == null || it.Sources.Count == 0) return "";
            var names = new List<string>();
            int myId = cd.MyCharacter != null ? cd.MyCharacter.UserId : 0;
            foreach (var s in it.Sources)
            {
                string n;
                if (myId > 0 && s.SourceUserId == myId) n = "я";
                else
                {
                    var c = cd.GetCharacter(s.SourceUserId);
                    n = c != null && !string.IsNullOrEmpty(c.Login) ? c.Login : (s.SourceUserId > 0 ? "#" + s.SourceUserId : "");
                }
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            return string.Join(", ", names.ToArray());
        }

        private static void Build()
        {
            if (_board != null) return;
            var canvasGo = new GameObject("QoLFighterHint", typeof(Canvas), typeof(CanvasScaler));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 690;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            CanvasScaler sample = null;
            foreach (var one in UnityEngine.Object.FindObjectsOfType<CanvasScaler>())
            {
                if (one == null || one == scaler) continue;
                var owner = one.GetComponent<Canvas>();
                if (owner == null || owner.renderMode == RenderMode.WorldSpace) continue;
                if (sample == null || one.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) sample = one;
                if (one.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) break;
            }
            if (sample != null)
            {
                scaler.uiScaleMode = sample.uiScaleMode;
                scaler.referenceResolution = sample.referenceResolution;
                scaler.screenMatchMode = sample.screenMatchMode;
                scaler.matchWidthOrHeight = sample.matchWidthOrHeight;
                scaler.scaleFactor = sample.scaleFactor;
            }
            _host = (RectTransform)canvasGo.transform;

            var boardGo = new GameObject("board", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            boardGo.transform.SetParent(canvasGo.transform, false);
            _board = (RectTransform)boardGo.transform;
            _board.anchorMin = _board.anchorMax = new Vector2(0f, 0f);
            _board.pivot = new Vector2(0f, 1f);
            _veil = boardGo.GetComponent<CanvasGroup>();
            _veil.blocksRaycasts = false;
            _veil.interactable = false;
            var back = boardGo.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(12);
            back.type = Image.Type.Sliced;
            back.color = new Color(0.12f, 0.09f, 0.06f, 0.96f);
            back.raycastTarget = false;
            var ol = boardGo.GetComponent<Outline>();
            ol.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            var vlg = boardGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 8, 10);
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = boardGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ble = boardGo.AddComponent<LayoutElement>();
            ble.preferredWidth = BoardW; ble.minWidth = BoardW;
            _board.sizeDelta = new Vector2(BoardW, 100f);

            _title = Line(_board, 19, FontStyle.Bold, new Color32(255, 224, 130, 255), TextAnchor.MiddleCenter);
            _meta = Line(_board, 14, FontStyle.Normal, new Color32(235, 225, 200, 255), TextAnchor.MiddleCenter);
            _bars = Line(_board, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            _bars.supportRichText = true;
            Rule(_board);
            _fxHead = Line(_board, 15, FontStyle.Bold, new Color32(255, 224, 130, 255), TextAnchor.MiddleCenter);
            _fxHead.text = "Эффекты";

            var rowsGo = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rowsGo.transform.SetParent(_board, false);
            var rl = rowsGo.GetComponent<VerticalLayoutGroup>();
            rl.spacing = 2f;
            rl.childControlWidth = true;
            rl.childControlHeight = true;
            rl.childForceExpandWidth = true;
            rl.childForceExpandHeight = false;
            var rf = rowsGo.GetComponent<ContentSizeFitter>();
            rf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rows = rowsGo.transform;
            _rowsSig = "";
            _board.gameObject.SetActive(false);
        }

        private static void AddRow(string name, string src, string power, string dur, Color32 color, bool head = false)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_rows, false);
            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bool stripe = !head && (_rowIndex++ & 1) == 0;
            bg.color = stripe ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0f);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 2, 2);
            h.spacing = 8f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var style = head ? FontStyle.Bold : FontStyle.Normal;
            Cell(go.transform, name, 160f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, src, 132f, style, color, TextAnchor.MiddleLeft, true);
            Cell(go.transform, power, 60f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, dur, 112f, style, color, TextAnchor.MiddleLeft, false);
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color, TextAnchor align, bool shrink)
        {
            var t = Line(row, 13, style, color, align);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (shrink)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 9;
                t.resizeTextMaxSize = 13;
            }
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 20f; le.minHeight = 20f;
        }

        private static void Rule(Transform host)
        {
            var go = new GameObject("rule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.55f, 0.42f, 0.22f, 0.7f);
            img.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 1f; le.minHeight = 1f; le.flexibleWidth = 1f;
        }

        private static Text Line(Transform host, int size, FontStyle style, Color32 color, TextAnchor align)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private static void Place()
        {
            if (_board == null || _host == null) return;
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float width = _board.rect.width > 0f ? _board.rect.width : BoardW;
            float height = _board.rect.height;
            var mouse = (Vector2)Input.mousePosition;
            float mx = mouse.x / scale, my = mouse.y / scale;
            float hw = _host.rect.width, hh = _host.rect.height;
            float x = mx - width - 22f;
            float y = my + height * 0.5f;
            if (x < 4f)
            {
                x = Mathf.Clamp(mx - width * 0.5f, 4f, hw - width - 4f);
                y = my + 26f + height;
                if (y > hh - 4f) y = my - 26f;
            }
            if (y > hh - 4f) y = hh - 4f;
            if (y - height < 4f) y = height + 4f;
            _board.anchoredPosition = new Vector2(x, y);
        }
    }
}
