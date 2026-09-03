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
                Hint = () => Artifacts.Busy ? "Идёт работа с хранилищем" : "Вернуться в Иллениум",
                Usable = () => !Artifacts.Busy,
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
                    if (Artifacts.HasStash) Artifacts.Restore();
                    else Artifacts.Stash();
                },
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
            UpdateStatus(world);
            if (!world) HideHint();
        }

        private static RectTransform Root(RectTransform any)
        {
            var canvas = any.GetComponentInParent<Canvas>();
            if (canvas == null) return any;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return (RectTransform)root.transform;
        }

        private static RectTransform _bag;
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
                return (RectTransform)bag.transform;
            }
            catch { return null; }
        }

        private static Vector2 Base(RectTransform bag, RectTransform parent)
        {
            Vector3 world = bag.TransformPoint(bag.rect.center);
            Vector2 local = parent.InverseTransformPoint(world);
            return local - parent.rect.center;
        }

        private const int PerColumn = 2;

        private static readonly int[] Counted = new int[4];
        private static readonly int[] Placed = new int[4];
        private static readonly int[] FirstColumn = new int[4];
        private static float _sideAt;

        private static void Layout()
        {
            var bag = Bag();
            if (bag == null) return;
            var parent = Root(bag);

            float side = _cellSide;
            float step = side + 8f;
            float lift = bag.rect.height * 0.6f + side * 0.7f + 10f;
            Vector2 baseAt = Base(bag, parent);

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

            int last = 0;
            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Enabled()) continue;
                int group = Group(b);
                int n = Placed[group]++;
                int col = FirstColumn[group] + n / PerColumn;
                int row = n % PerColumn;
                if (col > last) last = col;
                var rt = (RectTransform)b.Go.transform;
                rt.anchoredPosition = baseAt + new Vector2(step * col, lift + step * row);
            }
            _sideAt = baseAt.x + step * last + side * 0.65f;
        }

        private static int Group(Entry entry) => Mathf.Clamp(entry.Col, 0, Counted.Length - 1);

        private static Entry Anchor()
        {
            foreach (var b in Buttons) if (b.Go != null && b.Enabled()) return b;
            return null;
        }

        private static Entry Named(string name)
        {
            foreach (var b in Buttons) if (b.Name == name) return b;
            return null;
        }

        private static Font GameFont()
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

                string line = null;
                Entry owner = null;
                if (!string.IsNullOrEmpty(Artifacts.Status))
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
                if (!world || line == null || host == null)
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
                srt.anchoredPosition = new Vector2(RightOf(hostRt), hostRt.anchoredPosition.y);
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
                || Flasks.AnyBusy || !string.IsNullOrEmpty(Flasks.Status);
        }

        private static float RightOf(RectTransform near)
        {
            float own = near.anchoredPosition.x + near.rect.width * 0.65f;
            return _sideAt > own ? _sideAt : own;
        }

        private static void ShowHint(RectTransform near, string text)
        {
            try
            {
                if (Busy()) return;
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
                hrt.anchoredPosition = new Vector2(RightOf(near), near.anchoredPosition.y);
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

        private static void Strip(GameObject go)
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

        private static void Create(Entry entry)
        {
            try
            {
                var bag = Bag();
                if (bag == null) return;
                var parent = Root(bag);
                if (parent == null) return;

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
                go.name = entry.Name;

                float side = _cellSide;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.sizeDelta = new Vector2(side, side);
                rt.SetAsLastSibling();

                Image background = null, frame = null, icon = null;
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
                    var fs = AtlasUtils.GetStateHighlightingSprite(EHighlightingType.Positive);
                    if (fs != null) frame.sprite = fs;
                }

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
                Plugin.Trace("[buttons] " + entry.Name + (cell != null ? " из клетки" : " своя") + " сторона " + side
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
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.SendRequest(new ReturnToIlleniumRequest(true));
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
