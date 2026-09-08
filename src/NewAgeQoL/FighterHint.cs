using System;
using System.Collections.Generic;
using System.Text;
using Transport.Messages.Responses.Combat.States;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FighterHint
    {
        private const float Look = 0.08f;
        private const float Wait = 0.45f;
        private const float Fresh = 3f;
        private const float BoardW = 300f;
        private const float NameMax = 146f;
        private const float SrcMax = 92f;
        private const float Gap = 7f;
        private const int CellFont = 9;
        private static int _rowIndex;
        private static float _tableW;
        private static Text _measure;
        private static LayoutElement _ble;

        private static float _lookAt, _hoverAt;
        private static int _hoverId, _shownId;
        private static object _on;
        private static Canvas _canvas;
        private static RectTransform _host;
        private static RectTransform _board;
        private static CanvasGroup _veil;
        private static Text _title, _meta, _fxHead;
        private static Text _hp, _mp, _sp;
        private static GameObject _barsGo;
        private static Transform _rows;
        private static string _rowsSig = "";
        private static bool _reveal;
        internal static readonly Dictionary<int, List<UserEnchantmentsResponseItem>> States = new Dictionary<int, List<UserEnchantmentsResponseItem>>();
        internal static readonly Dictionary<int, float> AskedAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly HashSet<int> AskedNames = new HashSet<int>();
        internal static int NamesVersion;

        internal static bool Enabled => Plugin.CfgFighterHint == null || Plugin.CfgFighterHint.Value;

        internal static void Tick()
        {
            try
            {
                Listen();
                if (!Enabled || !SideButtons.InCombat() || SkillList.Armed) { Hide(); return; }
                if (Time.unscaledTime < _lookAt) return;
                _lookAt = Time.unscaledTime + Look;

                var cd = Cd();
                int id = cd == null ? 0 : Under(cd);
                if (id == 0 || Unity3DHelper.IsOverInterface()) { Hide(); return; }
                var who = cd.GetCharacter(id);
                if (who == null || who.IsBot) { Hide(); return; }
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
                if (_reveal) { _reveal = false; Place(); _veil.alpha = 1f; }
                else Fit();
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

        internal static int Under(ICombatData cd)
        {
            return Under(cd, true);
        }

        internal static int Under(ICombatData cd, bool playersOnly)
        {
            var view = CombatLocationView.Instance;
            var camera = view != null ? view.CombatCamera : null;
            if (camera == null) return 0;

            var all = cd.Characters;
            if (all == null) return 0;
            var mouse = (Vector2)Input.mousePosition;
            float limit = Mathf.Max(22f, Screen.height * 0.03f);

            var inside = new List<AbstractCharacter>();
            AbstractCharacter near = null;
            float best = limit;
            foreach (var pair in all)
            {
                var ch = pair.Value;
                if (ch == null || ch.UserId == 0) continue;
                float gap = Reach(camera, ch, mouse);
                if (gap <= 0.5f) { inside.Add(ch); continue; }
                if (gap >= best) continue;
                best = gap;
                near = ch;
            }

            if (playersOnly && inside.Count > 1)
            {
                var live = new List<AbstractCharacter>();
                foreach (var one in inside) if (!one.IsBot) live.Add(one);
                if (live.Count > 0) inside = live;
            }

            AbstractCharacter pick;
            if (inside.Count == 1) pick = inside[0];
            else if (inside.Count > 1) pick = Ray(cd, camera, inside) ?? Front(camera, inside);
            else pick = Ray(cd, camera, null) ?? near;

            if (pick == null) return 0;
            return playersOnly && pick.IsBot ? 0 : pick.UserId;
        }

        private static AbstractCharacter Ray(ICombatData cd, Camera camera, List<AbstractCharacter> only)
        {
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f);
            Array.Sort(hits, (one, two) => one.distance.CompareTo(two.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var found = cd.FindCharacterByGameObject(hit.collider.gameObject);
                if (found == null || found.UserId == 0) continue;
                if (only != null && !only.Contains(found)) continue;
                return found;
            }
            return null;
        }

        private static AbstractCharacter Front(Camera camera, List<AbstractCharacter> list)
        {
            AbstractCharacter best = null;
            float near = float.MaxValue;
            foreach (var ch in list)
            {
                var spot = ch.CharacterCollider != null ? ch.CharacterCollider.bounds.center : ch.position;
                float far = Vector3.Distance(camera.transform.position, spot);
                if (far >= near) continue;
                near = far;
                best = ch;
            }
            return best;
        }

        private static float Reach(Camera camera, AbstractCharacter ch, Vector2 mouse)
        {
            bool known = false;
            var box = new Bounds();
            try
            {
                var holder = ch.CharacterMeshRendererHolder;
                var skin = holder != null ? holder.MainRenderer : null;
                if (skin != null && skin.gameObject.activeInHierarchy) { box = skin.bounds; known = true; }
            }
            catch { }
            if (!known)
            {
                var body = ch.CharacterCollider;
                if (body == null)
                {
                    var spot = camera.WorldToScreenPoint(ch.position);
                    return spot.z <= 0f ? float.MaxValue : Vector2.Distance(mouse, new Vector2(spot.x, spot.y));
                }
                box = body.bounds;
            }
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            bool seen = false;
            for (int corner = 0; corner < 8; corner++)
            {
                var world = new Vector3((corner & 1) == 0 ? box.min.x : box.max.x,
                                        (corner & 2) == 0 ? box.min.y : box.max.y,
                                        (corner & 4) == 0 ? box.min.z : box.max.z);
                var spot = camera.WorldToScreenPoint(world);
                if (spot.z <= 0f) continue;
                seen = true;
                if (spot.x < left) left = spot.x;
                if (spot.x > right) right = spot.x;
                if (spot.y < bottom) bottom = spot.y;
                if (spot.y > top) top = spot.y;
            }
            if (!seen) return float.MaxValue;

            float thin = (right - left) * 0.14f;
            float flat = (top - bottom) * 0.06f;
            left += thin; right -= thin; bottom += flat; top -= flat;
            if (right < left) { float mid = (left + right) * 0.5f; left = right = mid; }
            if (top < bottom) { float mid = (bottom + top) * 0.5f; bottom = top = mid; }

            float wide = mouse.x < left ? left - mouse.x : mouse.x > right ? mouse.x - right : 0f;
            float high = mouse.y < bottom ? bottom - mouse.y : mouse.y > top ? mouse.y - top : 0f;
            float gap = Mathf.Sqrt(wide * wide + high * high);

            var middle = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
            return gap + Vector2.Distance(mouse, middle) * 0.001f;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(109, OnStates);
            nc.AddMessageListener(109, OnStates);
            nc.RemoveMessageListener(433, OnWho);
            nc.AddMessageListener(433, OnWho);
            _on = nc;
        }

        private static void OnStates(object m)
        {
            var msg = m as UserEnchantmentsResponseMessage;
            if (msg == null) return;
            States[msg.UserId] = msg.Items != null ? new List<UserEnchantmentsResponseItem>(msg.Items) : new List<UserEnchantmentsResponseItem>();
        }

        private static void OnWho(object m)
        {
            var info = m as Unity3DUserInfoResponseMessage;
            if (info == null || info.UserId <= 0 || string.IsNullOrEmpty(info.Login)) return;
            string had;
            if (Names.TryGetValue(info.UserId, out had) && had == info.Login) return;
            Names[info.UserId] = info.Login;
            NamesVersion++;
        }

        private static string NameOf(int userId)
        {
            if (userId <= 0) return "";
            string name;
            if (Names.TryGetValue(userId, out name) && !string.IsNullOrEmpty(name)) return name;
            AskName(userId);
            return "#" + userId;
        }

        private static void AskName(int userId)
        {
            if (userId <= 0 || !AskedNames.Add(userId)) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(new UserInfoRequest(userId, null));
            }
            catch (Exception e) { Plugin.Trace("[боец] имя " + userId + ": " + e.Message); }
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
            bool friend = me || (cd.MyCharacter != null && ch.Team == cd.MyCharacter.Team);
            _title.text = (ch.Login ?? "?") + (me ? "  (ты)" : "");
            var meta = new StringBuilder();
            if (ch.Level > 0) meta.Append("Уровень: ").Append(ch.Level);
            if (ch.Rank > 0) meta.Append(meta.Length > 0 ? "      " : "").Append("Рейтинг: ").Append(ch.Rank);
            _meta.text = meta.ToString();
            _meta.gameObject.SetActive(meta.Length > 0);

            var ind = ch.Indicators;
            if (ind != null)
            {
                _hp.text = ind.CurrentLife + " / " + ind.MaxLife;
                _mp.text = ind.CurrentMana + " / " + ind.MaxMana;
                _sp.text = friend ? ind.CurrentStamina + " / " + ind.MaxStamina : "?";
                _barsGo.SetActive(true);
            }
            else _barsGo.SetActive(false);

            List<UserEnchantmentsResponseItem> items;
            bool known = States.TryGetValue(ch.UserId, out items);
            var sig = new StringBuilder();
            sig.Append(ch.UserId).Append('|').Append(NamesVersion).Append('|');
            if (known)
                foreach (var it in items)
                {
                    sig.Append(it.StateType).Append(':').Append(it.StateId).Append(':').Append(it.Duration).Append(':').Append(it.Highlighting).Append(':').Append(Power(it)).Append(':');
                    if (it.Sources != null) foreach (var s in it.Sources) sig.Append(s.SourceUserId).Append('/');
                    sig.Append(';');
                }
            else sig.Append(Silent(ch.UserId) ? "?!" : "?");
            string s2 = sig.ToString();
            if (s2 != _rowsSig)
            {
                _rowsSig = s2;
                Table(ch, cd, known, items);
            }
            Width();
        }

        private static void Table(AbstractCharacter ch, ICombatData cd, bool known, List<UserEnchantmentsResponseItem> items)
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _rowIndex = 0;

            var list = new List<string[]>();
            var tint = new List<Color32>();
            bool head = known && items.Count > 0;
            if (head)
            {
                list.Add(new[] { "Название", "Источник", "Эффект", "Длительность" });
                tint.Add(new Color32(255, 224, 130, 255));
                foreach (var it in items)
                {
                    string key = "states.state_" + it.StateType + "_" + it.StateId;
                    string name = ResourceStrings.GetString(key + ".name");
                    if (name == key + ".name") name = "состояние " + it.StateType + "/" + it.StateId;
                    int power = Power(it);
                    string dur = it.Duration > 1000 ? "до конца боя" : it.Duration > 0 ? it.Duration + " " + Turns(it.Duration) : "";
                    list.Add(new[] { name, Sources(it, cd, ch.UserId), power != 0 ? power.ToString() : "", dur });
                    tint.Add(it.Highlighting == (int)EHighlightingType.Positive ? new Color32(120, 230, 120, 255)
                           : it.Highlighting == (int)EHighlightingType.Negative ? new Color32(255, 110, 100, 255)
                           : new Color32(235, 225, 200, 255));
                }
            }
            else
            {
                list.Add(new[] { known ? "нет эффектов" : Silent(ch.UserId) ? "эффекты не пришли" : "загружаю…", "", "", "" });
                tint.Add(new Color32(230, 215, 180, 255));
            }

            var wide = new float[4];
            for (int i = 0; i < list.Count; i++)
            {
                var style = head && i == 0 ? FontStyle.Bold : FontStyle.Normal;
                list[i][0] = Clip(list[i][0], NameMax, style);
                list[i][1] = Clip(list[i][1], SrcMax, style);
                for (int c = 0; c < 4; c++) wide[c] = Mathf.Max(wide[c], Measure(list[i][c], CellFont, style));
            }
            float total = 0f;
            int shown = 0;
            for (int c = 0; c < 4; c++)
            {
                if (wide[c] <= 0f) continue;
                wide[c] = Mathf.Ceil(wide[c]) + 2f;
                total += wide[c];
                shown++;
            }
            if (shown > 1) total += Gap * (shown - 1);
            _tableW = total + 8f;

            for (int i = 0; i < list.Count; i++)
            {
                AddRow(list[i], wide, tint[i], head && i == 0);
                if (head && i == 0) Rule(_rows);
            }
        }

        private static void AddRow(string[] cells, float[] wide, Color32 color, bool head)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_rows, false);
            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bool stripe = !head && (_rowIndex++ & 1) == 0;
            bg.color = stripe ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0f);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(3, 3, 0, 0);
            h.spacing = Gap;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var style = head ? FontStyle.Bold : FontStyle.Normal;
            for (int c = 0; c < 4; c++)
            {
                if (wide[c] <= 0f) continue;
                Cell(go.transform, cells[c], wide[c], style, color);
            }
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color)
        {
            var t = Line(row, CellFont, style, color, TextAnchor.MiddleLeft);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 13f; le.minHeight = 13f;
        }

        private static float Measure(string text, int size, FontStyle style, bool rich = false)
        {
            if (_measure == null || string.IsNullOrEmpty(text)) return 0f;
            _measure.fontSize = size;
            _measure.fontStyle = style;
            _measure.supportRichText = rich;
            _measure.text = text;
            return _measure.preferredWidth;
        }

        private static string Clip(string text, float max, FontStyle style)
        {
            if (string.IsNullOrEmpty(text) || Measure(text, CellFont, style) <= max) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string cut = text.Substring(0, n).TrimEnd() + "…";
                if (Measure(cut, CellFont, style) <= max) return cut;
            }
            return "…";
        }

        private static void Width()
        {
            if (_board == null || _ble == null) return;
            float w = _tableW;
            w = Mathf.Max(w, Measure(_title.text, 12, FontStyle.Bold));
            if (_meta.gameObject.activeSelf) w = Mathf.Max(w, Measure(_meta.text, 9, FontStyle.Normal));
            if (_barsGo != null && _barsGo.activeSelf)
            {
                float bars = Measure(_hp.text, 9, FontStyle.Bold) + Measure(_mp.text, 9, FontStyle.Bold) + Measure(_sp.text, 9, FontStyle.Bold);
                w = Mathf.Max(w, bars + 3f * 13f + 2f * 8f);
            }
            w = Mathf.Clamp(Mathf.Ceil(w) + 14f, 150f, 520f);
            if (Mathf.Abs(_board.sizeDelta.x - w) < 0.5f) return;
            _ble.preferredWidth = w; _ble.minWidth = w;
            _board.sizeDelta = new Vector2(w, _board.sizeDelta.y);
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
                    n = c != null && !string.IsNullOrEmpty(c.Login) ? c.Login : NameOf(s.SourceUserId);
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
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = new Color(0.12f, 0.09f, 0.06f, 0.96f);
            back.raycastTarget = false;
            var ol = boardGo.GetComponent<Outline>();
            ol.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            ol.effectDistance = new Vector2(1f, -1f);
            var vlg = boardGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(7, 7, 4, 5);
            vlg.spacing = 1f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = boardGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _ble = boardGo.AddComponent<LayoutElement>();
            _ble.preferredWidth = BoardW; _ble.minWidth = BoardW;
            _board.sizeDelta = new Vector2(BoardW, 100f);

            _title = Line(_board, 12, FontStyle.Bold, new Color32(255, 224, 130, 255), TextAnchor.MiddleCenter);
            _meta = Line(_board, 9, FontStyle.Normal, new Color32(235, 225, 200, 255), TextAnchor.MiddleCenter);
            Bars(_board);
            Rule(_board);
            _fxHead = Line(_board, 9, FontStyle.Bold, new Color32(255, 224, 130, 255), TextAnchor.MiddleCenter);
            _fxHead.text = "";
            _fxHead.gameObject.SetActive(false);

            var rowsGo = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rowsGo.transform.SetParent(_board, false);
            var rl = rowsGo.GetComponent<VerticalLayoutGroup>();
            rl.spacing = 1f;
            rl.childControlWidth = true;
            rl.childControlHeight = true;
            rl.childForceExpandWidth = true;
            rl.childForceExpandHeight = false;
            var rf = rowsGo.GetComponent<ContentSizeFitter>();
            rf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rows = rowsGo.transform;

            _measure = Line(_board, CellFont, FontStyle.Normal, new Color32(255, 255, 255, 0), TextAnchor.MiddleLeft);
            _measure.horizontalOverflow = HorizontalWrapMode.Overflow;
            _measure.enabled = false;
            var mle = _measure.gameObject.AddComponent<LayoutElement>();
            mle.ignoreLayout = true;

            _tableW = 0f;
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
            h.padding = new RectOffset(3, 3, 0, 0);
            h.spacing = 5f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var style = head ? FontStyle.Bold : FontStyle.Normal;
            Cell(go.transform, name, 116f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, src, 96f, style, color, TextAnchor.MiddleLeft, true);
            Cell(go.transform, power, 36f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, dur, 76f, style, color, TextAnchor.MiddleLeft, false);
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color, TextAnchor align, bool shrink)
        {
            var t = Line(row, 10, style, color, align);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (shrink)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 8;
                t.resizeTextMaxSize = 10;
            }
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 13f; le.minHeight = 13f;
        }

        private static void Bars(Transform host)
        {
            _barsGo = new GameObject("bars", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            _barsGo.transform.SetParent(host, false);
            var row = _barsGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var le = _barsGo.GetComponent<LayoutElement>();
            le.preferredHeight = 14f; le.minHeight = 14f;

            _hp = Pair(_barsGo.transform, Heart(), new Color32(255, 106, 90, 255));
            _mp = Pair(_barsGo.transform, Icon(EActionParamIconType.MANA_COST), new Color32(109, 179, 255, 255));
            _sp = Pair(_barsGo.transform, Icon(EActionParamIconType.STAMINA_COST), new Color32(255, 210, 87, 255));
        }

        private static Text Pair(Transform host, Sprite mark, Color32 tint)
        {
            var pairGo = new GameObject("pair", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pairGo.transform.SetParent(host, false);
            var row = pairGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 2f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var markGo = new GameObject("mark", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            markGo.transform.SetParent(pairGo.transform, false);
            var image = markGo.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.sprite = mark != null ? mark : OnlineWindow.Rounded(8);
            if (mark == null || ReferenceEquals(mark, _heart)) image.color = tint;
            var mle = markGo.GetComponent<LayoutElement>();
            mle.preferredWidth = 11f; mle.minWidth = 11f; mle.preferredHeight = 11f; mle.minHeight = 11f;

            var text = Line(pairGo.transform, 9, FontStyle.Bold, tint, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tle = text.gameObject.AddComponent<LayoutElement>();
            tle.preferredHeight = 12f; tle.minHeight = 12f;
            return text;
        }

        private static Sprite _heart;

        private static Sprite Heart()
        {
            if (_heart != null) return _heart;
            try
            {
                const int side = 48;
                var tex = new Texture2D(side, side, TextureFormat.RGBA32, false);
                var clear = new Color(1f, 1f, 1f, 0f);
                for (int py = 0; py < side; py++)
                    for (int px = 0; px < side; px++)
                    {
                        float x = (px - side * 0.5f + 0.5f) / (side * 0.42f);
                        float y = (py - side * 0.5f + 0.5f) / (side * 0.42f) + 0.13f;
                        float sum = x * x + y * y - 1f;
                        bool solid = sum * sum * sum - x * x * y * y * y <= 0f;
                        tex.SetPixel(px, py, solid ? Color.white : clear);
                    }
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                _heart = Sprite.Create(tex, new Rect(0f, 0f, side, side), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Trace("[боец] сердце: " + e.Message); }
            return _heart;
        }

        private static Sprite Icon(EActionParamIconType type)
        {
            try { return AtlasUtils.GetActionParamIcon(type); }
            catch (Exception e) { Plugin.Trace("[боец] значок: " + e.Message); return null; }
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

        private static void Fit()
        {
            if (_board == null || _host == null) return;
            float width = _board.rect.width > 0f ? _board.rect.width : BoardW;
            float height = _board.rect.height;
            float hw = _host.rect.width, hh = _host.rect.height;
            var at = _board.anchoredPosition;
            float x = Mathf.Clamp(at.x, 4f, Mathf.Max(4f, hw - width - 4f));
            float y = Mathf.Clamp(at.y, height + 4f, Mathf.Max(height + 4f, hh - 4f));
            if (Mathf.Abs(x - at.x) > 0.5f || Mathf.Abs(y - at.y) > 0.5f) _board.anchoredPosition = new Vector2(x, y);
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
