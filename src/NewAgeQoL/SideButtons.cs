using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class SideButtons
    {
        private class Entry
        {
            internal string Name;
            internal System.Func<string> Hint;
            internal GameObject Go;
            internal Image Icon;
            internal Text Count;
            internal System.Func<bool> Enabled;
            internal System.Func<Sprite> Sprite;
            internal System.Func<string> Badge;
            internal System.Action Click;
            internal System.Func<bool> Usable;
            internal int Col;
        }

        private static readonly List<Entry> Buttons = new List<Entry>
        {
            new Entry
            {
                Name = "QoLTownButton",
                Col = 0,
                Hint = () => Artifacts.Busy ? "Идёт работа с хранилищем"
                           : TownWalk.Busy ? "Уже иду"
                           : Plugin.CfgTownTournament != null && Plugin.CfgTownTournament.Value
                             ? "В город, потом на арену и к турнирам"
                             : "Вернуться в Иллениум",
                Usable = () => !Artifacts.Busy && !TownWalk.Busy,
                Enabled = () => Plugin.CfgTownButton == null || Plugin.CfgTownButton.Value,
                Sprite = () => Pick("toTheCity", 4),
                Click = GoHome,
            },
            new Entry
            {
                Name = "QoLArtifactButton",
                Col = 0,
                Hint = () => Artifacts.HasStash
                    ? "Забрать артефакты и надеть"
                    : "Сдать артефакты в хранилище",
                Enabled = () => Plugin.CfgArtifactButtons == null || Plugin.CfgArtifactButtons.Value,
                Sprite = () => Artifacts.HasStash ? Pick("storage_get", 13) : Pick("storage_put", 14),
                Click = () =>
                {
                    if (Artifacts.Busy) return;
                    Travel.Cancel("занялся артефактами");
                    if (Artifacts.HasStash) Artifacts.Restore();
                    else Artifacts.Stash();
                },
            },
            new Entry
            {
                Name = "QoLTravelButton",
                Col = 2,
                Hint = () => Travel.Busy ? "Идёт поход — можно выбрать другую точку" : "Куда идти: список точек",
                Enabled = () => Travel.Enabled && Travel.Spots().Count > 0,
                Sprite = () => Pick(1, "move5", "move3", "mines_attack", "assassinate"),
                Badge = () => Travel.Busy ? "▶" : "",
                Click = TravelMenu.Toggle,
            },
        };

        static SideButtons()
        {
            for (int i = 0; i < Flasks.Rows; i++)
            {
                int row = i;
                Buttons.Add(new Entry
                {
                    Name = "QoLFlaskButton" + row,
                    Col = 1,
                    Hint = () => Flasks.Hint(row),
                    Badge = () => Flasks.Badge(row),
                    Usable = () => !Flasks.Busy(row) && !Artifacts.Busy,
                    Enabled = () => Flasks.Shown(row),
                    Sprite = () => Flasks.Icon(row),
                    Click = () => Flasks.Use(row),
                });
            }
        }

        private static float _next;

        internal static void Tick()
        {
            TravelMenu.Hover();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;

            bool world = InWorld() && !InCombat();
            var anchor = Bag();
            bool ready = world && anchor != null && anchor.rect.height > 2f
                         && Root(anchor).rect.width > 2f;

            foreach (var b in Buttons)
            {
                bool want = ready && b.Enabled();
                if (!want)
                {
                    if (b.Go != null) b.Go.SetActive(false);
                    continue;
                }
                if (b.Go == null) Create(b);
                if (b.Go == null) continue;
                var sprite = b.Sprite();
                if (b.Icon != null)
                {
                    if (sprite != null && b.Icon.sprite != sprite) b.Icon.sprite = sprite;
                    bool drawn = b.Icon.sprite != null && b.Icon.sprite.texture != null;
                    if (b.Icon.enabled != drawn) b.Icon.enabled = drawn;
                }

                bool usable = b.Usable == null || b.Usable();
                var btn = b.Go.GetComponent<Button>();
                if (btn != null && btn.interactable != usable) btn.interactable = usable;
                if (b.Icon != null)
                {
                    var tint = usable ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.65f);
                    if (b.Icon.color != tint) b.Icon.color = tint;
                }
                if (b.Count != null && b.Badge != null)
                {
                    string badge = b.Badge();
                    if (b.Count.text != badge) b.Count.text = badge;
                }
            }
            Layout();
            foreach (var b in Buttons)
            {
                if (b.Go == null) continue;
                bool show = ready && b.Enabled();
                if (show && !b.Go.activeSelf) b.Go.SetActive(true);
            }
            if (_panel != null && _panel.gameObject.activeSelf != ready) _panel.gameObject.SetActive(ready);
            TravelMenu.Tick(ready);
            UpdateStatus(world);
            if (!world) HideHint();
        }

        private static RectTransform _panel;

        private static RectTransform Panel(RectTransform parent)
        {
            if (_panel != null) return _panel;
            if (parent == null) return null;

            var go = new GameObject("QoLPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(_cellSide, _cellSide);

            var back = go.GetComponent<Image>();
            back.color = new Color(0.04f, 0.05f, 0.07f, 0.42f);
            back.raycastTarget = false;

            _panel = rt;
            return _panel;
        }

        private static void Place()
        {
            if (_panel == null) return;
            var bag = Bag();
            if (bag == null) return;
            var parent = Root(bag);
            if (parent == null) return;

            Rect menu = Around(parent);
            if (menu.width <= 0f) return;

            Vector2 size = _panel.sizeDelta;
            Rect area = parent.rect;
            float x = Mathf.Min(menu.xMax + Gap, area.xMax - size.x);
            float y = Mathf.Clamp(menu.yMin, area.yMin, area.yMax - size.y);

            var corner = new Vector2(0f, 0f);
            if (_panel.pivot != corner) _panel.pivot = corner;
            var middle = new Vector2(0.5f, 0.5f);
            if (_panel.anchorMin != middle) _panel.anchorMin = middle;
            if (_panel.anchorMax != middle) _panel.anchorMax = middle;

            var want = new Vector2(x, y);
            if ((_panel.anchoredPosition - want).sqrMagnitude <= 4f) return;
            Plugin.Trace("[buttons] панель " + _panel.anchoredPosition + " → " + want);
            _panel.anchoredPosition = want;
        }

        private static Rect Around(RectTransform parent)
        {
            var group = _menu != null ? _menu : (Bag() != null ? Bag().parent as RectTransform : null);
            if (group == null) return new Rect();

            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            var corners = new Vector3[4];
            foreach (var button in group.GetComponentsInChildren<Button>(false))
            {
                var rt = button.transform as RectTransform;
                if (rt == null || rt.rect.width < 4f) continue;
                var host = rt.parent as RectTransform;
                if (host == null) continue;
                rt.GetLocalCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 local = parent.InverseTransformPoint(host.TransformPoint(rt.localPosition + corners[i]));
                    if (local.x < xMin) xMin = local.x;
                    if (local.x > xMax) xMax = local.x;
                    if (local.y < yMin) yMin = local.y;
                    if (local.y > yMax) yMax = local.y;
                }
            }
            if (xMax < xMin) return new Rect();
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        internal static bool AtRightSide
        {
            get
            {
                if (_panel == null) return false;
                var parent = _panel.parent as RectTransform;
                if (parent == null || parent.rect.width < 1f) return false;
                Vector2 center = parent.InverseTransformPoint(_panel.TransformPoint(_panel.rect.center));
                return center.x > parent.rect.width * 0.25f;
            }
        }

        internal static RectTransform PanelRect => _panel;

        private static RectTransform Root(RectTransform any)
        {
            var canvas = any.GetComponentInParent<Canvas>();
            if (canvas == null) return any;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return (RectTransform)root.transform;
        }

        private static RectTransform _bag, _menu;
        private static float _bagAt = -1f;

        private static RectTransform Bag()
        {
            if (_bagAt == Time.unscaledTime) return _bag;
            _bagAt = Time.unscaledTime;
            _bag = FindBag();
            return _bag;
        }

        private static RectTransform FindBag()
        {
            try
            {
                LeftBottomMenuScript menu = null;
                var view = BaseLocationView.GetInstance();
                if (view != null) menu = view.LeftBottomMenuScript;
                if (menu == null || !menu.gameObject.activeInHierarchy)
                    menu = Object.FindObjectOfType<LeftBottomMenuScript>();

                var bag = menu != null ? menu.InventoryMenuButton : null;
                if (bag == null || !bag.gameObject.activeInHierarchy) return null;
                _menu = menu.transform as RectTransform;
                return (RectTransform)bag.transform;
            }
            catch { return null; }
        }

        private const int PerColumn = 2;
        private const float Gap = 8f;
        private const float Pad = 7f;

        private static readonly int[] Counted = new int[4];
        private static readonly int[] Placed = new int[4];
        private static readonly int[] FirstColumn = new int[4];

        private static void Layout()
        {
            if (_panel == null) return;

            float side = _cellSide;
            float step = side + Gap;

            for (int i = 0; i < Counted.Length; i++) { Counted[i] = 0; Placed[i] = 0; }
            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Enabled()) continue;
                Counted[Group(b)]++;
            }
            int next = 0;
            for (int g = 0; g < Counted.Length; g++)
            {
                FirstColumn[g] = next;
                next += (Counted[g] + PerColumn - 1) / PerColumn;
            }
            if (next == 0) return;

            int lastCol = 0, lastRow = 0;
            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Enabled()) continue;
                int n = Placed[Group(b)]++;
                int col = FirstColumn[Group(b)] + n / PerColumn;
                int row = n % PerColumn;
                if (col > lastCol) lastCol = col;
                if (row > lastRow) lastRow = row;
            }

            float width = (lastCol + 1) * side + lastCol * Gap + Pad * 2f;
            float height = (lastRow + 1) * side + lastRow * Gap + Pad * 2f;
            _panel.sizeDelta = new Vector2(width, height);

            float left = -width * 0.5f + Pad + side * 0.5f;
            float bottom = -height * 0.5f + Pad + side * 0.5f;

            for (int i = 0; i < Placed.Length; i++) Placed[i] = 0;
            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Enabled()) continue;
                int n = Placed[Group(b)]++;
                int col = FirstColumn[Group(b)] + n / PerColumn;
                int row = n % PerColumn;
                var rt = (RectTransform)b.Go.transform;
                rt.anchoredPosition = new Vector2(left + step * col, bottom + step * row);
            }

            Place();
        }

        private static int Group(Entry entry) => Mathf.Clamp(entry.Col, 0, Counted.Length - 1);

        private static Entry Anchor()
        {
            foreach (var b in Buttons) if (b.Go != null && b.Enabled()) return b;
            return null;
        }

        internal static RectTransform ButtonRect(string name)
        {
            var entry = Named(name);
            return entry != null && entry.Go != null ? (RectTransform)entry.Go.transform : null;
        }

        internal static float RowOf(string name)
        {
            var entry = Named(name);
            if (entry == null || entry.Go == null) return 0f;
            return ((RectTransform)entry.Go.transform).anchoredPosition.y;
        }

        private static Entry Named(string name)
        {
            foreach (var b in Buttons) if (b.Name == name) return b;
            return null;
        }

        internal static Font GameFont()
        {
            var holder = VisualPrefabsHolder.Instance;
            if (holder != null && holder.BoldStandardFont != null) return holder.BoldStandardFont;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) return f;
            return null;
        }

        private static Text _status;

        private static void UpdateStatus(bool world)
        {
            try
            {
                if (!Artifacts.Busy && !string.IsNullOrEmpty(Artifacts.Status)
                    && Time.unscaledTime - Artifacts.StatusAt > 4f) Artifacts.Status = "";
                if (!Flasks.AnyBusy && !string.IsNullOrEmpty(Flasks.Status)
                    && Time.unscaledTime - Flasks.StatusAt > 5f) Flasks.Status = "";

                if (!Travel.Busy && !string.IsNullOrEmpty(Travel.Status)
                    && Time.unscaledTime - Travel.StatusAt > 6f) Travel.Status = "";

                string line = null;
                Entry owner = null;
                if (!string.IsNullOrEmpty(Travel.Status))
                {
                    line = Travel.Status;
                    owner = Named("QoLTravelButton");
                }
                else if (!string.IsNullOrEmpty(Artifacts.Status))
                {
                    line = "Артефакты: " + Artifacts.Status;
                    owner = Named("QoLArtifactButton");
                }
                else if (!string.IsNullOrEmpty(Flasks.Status))
                {
                    line = Flasks.Status;
                    owner = Named("QoLFlaskButton" + Flasks.StatusRow);
                }
                if (owner == null || owner.Go == null || !owner.Enabled()) owner = Anchor();
                var host = owner != null ? owner.Go : null;
                if (!world || line == null || host == null || TravelMenu.Open)
                {
                    if (_status != null) _status.gameObject.SetActive(false);
                    return;
                }
                var hostRt = (RectTransform)host.transform;
                if (_status == null)
                {
                    var go = new GameObject("QoLStatus", typeof(RectTransform), typeof(Text), typeof(Outline));
                    go.transform.SetParent(hostRt.parent, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(460f, 32f);
                    var t = go.GetComponent<Text>();
                    t.font = GameFont();
                    t.fontSize = 20;
                    t.alignment = TextAnchor.MiddleLeft;
                    t.color = new Color(1f, 0.86f, 0.4f);
                    t.raycastTarget = false;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                    var o = go.GetComponent<Outline>();
                    o.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    o.effectDistance = new Vector2(1.6f, -1.6f);
                    _status = t;
                }
                var srt = (RectTransform)_status.transform;
                srt.anchoredPosition = new Vector2(SideOf(_status), hostRt.anchoredPosition.y);
                srt.SetAsLastSibling();
                if (!_status.gameObject.activeSelf) _status.gameObject.SetActive(true);
                _status.text = line;
                HideHint();
            }
            catch { }
        }

        private static Text _hint;

        private static bool Busy()
        {
            return Artifacts.Busy || !string.IsNullOrEmpty(Artifacts.Status)
                || Flasks.AnyBusy || !string.IsNullOrEmpty(Flasks.Status)
                || Travel.Busy || !string.IsNullOrEmpty(Travel.Status);
        }

        private static float SideOf(Text label)
        {
            bool left = AtRightSide;
            var rt = (RectTransform)label.transform;
            rt.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            label.alignment = left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            float half = _panel != null ? _panel.sizeDelta.x * 0.5f : 0f;
            return left ? -(half + 10f) : half + 10f;
        }

        private static void ShowHint(RectTransform near, string text)
        {
            try
            {
                if (Busy() || TravelMenu.Open) return;
                if (_hint == null)
                {
                    var go = new GameObject("QoLHint", typeof(RectTransform), typeof(Text), typeof(Outline));
                    go.transform.SetParent(near.parent, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(460f, 30f);
                    _hint = go.GetComponent<Text>();
                    _hint.font = GameFont();
                    _hint.fontSize = 19;
                    _hint.alignment = TextAnchor.MiddleLeft;
                    _hint.color = new Color(1f, 0.94f, 0.78f);
                    _hint.raycastTarget = false;
                    _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
                    _hint.verticalOverflow = VerticalWrapMode.Overflow;
                    var o = go.GetComponent<Outline>();
                    o.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    o.effectDistance = new Vector2(1.6f, -1.6f);
                }
                if (Busy()) { HideHint(); return; }
                _hint.text = text;
                var hrt = (RectTransform)_hint.transform;
                hrt.anchoredPosition = new Vector2(SideOf(_hint), near.anchoredPosition.y);
                hrt.SetAsLastSibling();
                _hint.gameObject.SetActive(true);
            }
            catch { }
        }

        private static void HideHint()
        {
            if (_hint != null && _hint.gameObject.activeSelf) _hint.gameObject.SetActive(false);
        }

        private static GameObject _cell;
        private static float _cellSide = 64f;
        private static float _cellAt = -100f;

        internal static float CellSide => _cellSide;

        internal static GameObject CellPrefab() => Cell();

        private static GameObject Cell()
        {
            if (_cell != null) return _cell;
            if (Time.unscaledTime - _cellAt < 3f) return null;
            _cellAt = Time.unscaledTime;
            try
            {
                EnchantmentsPanel panel = null;
                foreach (var p in Resources.FindObjectsOfTypeAll<EnchantmentsPanel>()) { panel = p; break; }
                if (panel == null) return null;

                for (var t = panel.GetType(); t != null; t = t.BaseType)
                {
                    var f = t.GetField("ItemRendererPrefab",
                                       BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f == null) continue;
                    _cell = f.GetValue(panel) as GameObject;
                    break;
                }
                if (_cell == null) return null;

                var prefabRt = _cell.transform as RectTransform;
                float fromPrefab = prefabRt != null ? prefabRt.rect.height : 0f;
                float fromGrid = panel.ItemSize.y;
                _cellSide = fromPrefab > 16f && fromPrefab < 220f ? fromPrefab
                          : (fromGrid > 16f && fromGrid < 220f ? fromGrid : 64f);
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[buttons] " + e.Message); }
            return _cell;
        }

        internal static void Strip(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                if (mb is Graphic || mb is Button || mb is Mask || mb is Shadow
                    || mb is LayoutGroup || mb is ContentSizeFitter || mb is LayoutElement) continue;
                Object.DestroyImmediate(mb);
            }
            foreach (var an in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> call)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(call);
            trigger.triggers.Add(e);
        }

        internal static GameObject BuildCell(RectTransform parent, string name, float side,
                                             out Image icon, out Image frame, out Image background)
        {
            var cell = Cell();
            GameObject go;
            if (cell != null)
            {
                go = Object.Instantiate(cell, parent);
                go.SetActive(false);
                Strip(go);
                foreach (var tx in go.GetComponentsInChildren<Text>(true)) tx.gameObject.SetActive(false);
            }
            else
            {
                go = new GameObject("cell", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.SetActive(false);
                var edge = new GameObject("frame", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(go.transform, false);
                edge.GetComponent<Image>().sprite =
                    AtlasUtils.GetStateHighlightingSprite(EHighlightingType.Positive);
            }
            go.name = name;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(side, side);
            rt.SetAsLastSibling();

            background = null;
            frame = null;
            icon = null;
            foreach (var im in go.GetComponentsInChildren<Image>(true))
            {
                if (im.transform == rt) { background = im; continue; }
                var crt = (RectTransform)im.transform;
                crt.anchorMin = Vector2.zero;
                crt.anchorMax = Vector2.one;
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.offsetMin = Vector2.zero;
                crt.offsetMax = Vector2.zero;
                crt.localScale = Vector3.one;
                crt.localRotation = Quaternion.identity;
                im.raycastTarget = false;

                bool looksFrame = im.name.IndexOf("frame", System.StringComparison.OrdinalIgnoreCase) >= 0
                           || im.name.IndexOf("border", System.StringComparison.OrdinalIgnoreCase) >= 0
                           || im.name.IndexOf("highlight", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (looksFrame) frame = im;
                else if (icon == null) icon = im;
            }

            if (icon == null)
            {
                var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(rt, false);
                icon = iconGo.GetComponent<Image>();
                icon.raycastTarget = false;
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                if (frame != null) ((RectTransform)frame.transform).SetAsLastSibling();
            }

            float inset = side * 0.1f;
            var iconRt = (RectTransform)icon.transform;
            iconRt.offsetMin = new Vector2(inset, inset);
            iconRt.offsetMax = new Vector2(-inset, -inset);

            if (background != null)
            {
                background.enabled = true;
                background.raycastTarget = true;
                background.color = new Color(1f, 1f, 1f, 0f);
            }

            var backGo = new GameObject("back", typeof(RectTransform), typeof(Image));
            backGo.transform.SetParent(rt, false);
            backGo.transform.SetAsFirstSibling();
            var backRt = (RectTransform)backGo.transform;
            backRt.anchorMin = Vector2.zero;
            backRt.anchorMax = Vector2.one;
            float pad = side * 0.04f;
            backRt.offsetMin = new Vector2(pad, pad);
            backRt.offsetMax = new Vector2(-pad, -pad);
            var back = backGo.GetComponent<Image>();
            back.color = new Color(0.05f, 0.05f, 0.06f, 0.6f);
            back.raycastTarget = false;

            if (frame != null)
            {
                frame.gameObject.SetActive(true);
                frame.enabled = true;
                frame.color = Color.white;
                var edge = AtlasUtils.GetStateHighlightingSprite(EHighlightingType.Positive);
                if (edge != null) frame.sprite = edge;
            }
            return go;
        }

        private static void Create(Entry entry)
        {
            try
            {
                var bag = Bag();
                if (bag == null) return;
                var parent = Panel(Root(bag));
                if (parent == null) return;

                var go = BuildCell(parent, entry.Name, _cellSide, out var icon, out var frame, out var background);
                if (go == null) return;
                go.SetActive(false);
                float side = _cellSide;
                var rt = (RectTransform)go.transform;

                var sprite = entry.Sprite();
                icon.gameObject.SetActive(true);
                icon.enabled = true;
                icon.preserveAspect = true;
                icon.color = Color.white;
                if (sprite != null) icon.sprite = sprite;
                entry.Icon = icon;

                if (entry.Badge != null) entry.Count = Badge(rt, side, entry.Badge());

                var button = go.GetComponent<Button>();
                if (button == null) button = go.AddComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.targetGraphic = background;
                button.interactable = true;
                var clickAction = entry.Click;
                button.onClick.AddListener(() => clickAction());

                var trigger = go.GetComponent<EventTrigger>();
                if (trigger == null) trigger = go.AddComponent<EventTrigger>();
                trigger.triggers = new List<EventTrigger.Entry>();
                var hint = entry.Hint;
                AddTrigger(trigger, EventTriggerType.PointerEnter, _ => ShowHint(rt, hint()));
                AddTrigger(trigger, EventTriggerType.PointerExit, _ => HideHint());

                entry.Go = go;
                Layout();
                Plugin.Trace("[buttons] " + entry.Name + " сторона " + side
                                    + ", рамка " + (frame != null && frame.sprite != null ? frame.sprite.name : "нет")
                                    + ", иконка " + (sprite != null ? sprite.name : "нет"));
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[buttons] " + e); }
        }

        private static Text Badge(RectTransform host, float side, string text)
        {
            var go = new GameObject("count", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(side * 0.1f, side * 0.06f);
            rt.offsetMax = new Vector2(-side * 0.1f, side * 0.4f);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            var label = go.GetComponent<Text>();
            label.font = GameFont();
            label.fontSize = Mathf.Max(11, Mathf.RoundToInt(side * 0.27f));
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.LowerRight;
            label.color = new Color(1f, 0.93f, 0.62f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text ?? "";

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.4f, -1.4f);
            return label;
        }

        private static Sprite Pick(int tabFallback, params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var s = AtlasUtils.GetQuickButtonSprite(name);
                    if (Ok(s)) return s;
                    s = AtlasUtils.GetBottomPanelSprite(EBottomPanelAtlas.BottomPanel, name);
                    if (Ok(s)) return s;
                    s = AtlasUtils.GetBottomPanelSpriteByName(name);
                    if (Ok(s)) return s;
                }
                catch { }
            }
            try
            {
                var s = AtlasUtils.GetThingTabImage((EThingTabType)tabFallback);
                if (Ok(s)) return s;
            }
            catch { }
            return null;
        }

        private static Sprite Pick(string bottomPanelName, int tabFallback)
        {
            try
            {
                var s = AtlasUtils.GetBottomPanelSprite(EBottomPanelAtlas.BottomPanel, bottomPanelName);
                if (Ok(s)) return s;
                s = AtlasUtils.GetBottomPanelSpriteByName(bottomPanelName);
                if (Ok(s)) return s;
                s = AtlasUtils.GetThingTabImage((EThingTabType)tabFallback);
                if (Ok(s)) return s;
            }
            catch { }
            return null;
        }

        private static bool Ok(Sprite s) => s != null && s.name != "unknown";

        private static void GoHome()
        {
            try
            {
                Travel.Cancel("ты вернулся в город");
                TownWalk.Go();
            }
            catch (System.Exception e) { Plugin.Log?.LogError("[buttons] " + e.Message); }
        }

        internal static string Scene()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
        }

        internal static bool InCombat()
        {
            try { return SceneWorkFlow.IsCurrentScene(EUnityScene.CombatLocation); }
            catch { return false; }
        }

        internal static bool InWorld()
        {
            try
            {
                var ud = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                if (ud == null || !ud.LoggedIn) return false;
                return !SceneWorkFlow.IsCurrentScene(EUnityScene.Launch)
                    && !SceneWorkFlow.IsCurrentScene(EUnityScene.CreateCharacter);
            }
            catch { return false; }
        }
    }
}
