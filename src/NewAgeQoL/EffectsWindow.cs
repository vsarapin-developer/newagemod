using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Combat.States;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class EffectsWindow
    {
        private const float PanelW = 420f;
        private const float PanelH = 360f;
        private const float TopH = 38f;
        private const float RowH = 17f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Text _title, _bars;
        private static Transform _rows;
        private static ScrollRect _scroll;
        private static string _sig = "";
        private static int _shownId;
        private static float _pollAt;
        private static GameObject _btnGo;
        private static float _btnAt;
        private static BaseStateButton _btnProto;
        private static Image _btnProtoImage;
        private static int _btnLook = -1;
        private static Button _btnButton;
        private static Image _btnDisc;
        private static Sprite _btnIconSprite;
        private static Sprite _btnOffSprite;
        private static float _btnStateAt;
        private static int _rowIndex;

        internal static bool Enabled => Plugin.CfgEffectsButton == null || Plugin.CfgEffectsButton.Value;

        internal static void Tick()
        {
            try
            {
                bool combat = SideButtons.InCombat();
                if (!combat)
                {
                    _btnGo = null;
                    Column(false);
                    if (_canvasGo != null) Close();
                    return;
                }
                if (Enabled) { EnsureButton(); ButtonState(); }
                Column(Enabled);
                if (_canvasGo == null) return;
                if (_panelGo == null) { Close(); return; }
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.25f;
                Refresh();
            }
            catch (Exception e) { Plugin.Trace("[эффекты] " + e.Message); }
        }

        private static GameObject _theirs;

        private static void Column(bool hide)
        {
            try
            {
                if (_theirs == null)
                {
                    if (!hide) return;
                    var ctrl = DependencyContainer.ResolveController<EnchantmentPanelsController>();
                    if (ctrl == null) return;
                    var panel = AccessTools.Property(typeof(EnchantmentPanelsController), "SelectedCharacterPanel")?.GetValue(ctrl) as AbstractCharacterPanel;
                    if (panel == null) return;
                    var grid = AccessTools.Field(typeof(AbstractCharacterPanel), "enchantmentsPanel")?.GetValue(panel) as MonoBehaviour;
                    if (grid == null) return;
                    _theirs = grid.gameObject;
                }
                if (_theirs.activeSelf == !hide) return;
                _theirs.SetActive(!hide);
            }
            catch (Exception e) { Plugin.Trace("[эффекты] колонка состояний: " + e.Message); }
        }

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

        private static void Open()
        {
            try
            {
                Build();
                _sig = "";
                _shownId = 0;
                Refresh();
            }
            catch (Exception e) { Plugin.Log?.LogError("[эффекты] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); }
            _canvasGo = null;
            _panelGo = null;
            _rows = null;
            _scroll = null;
            _title = null;
            _bars = null;
            _shownId = 0;
        }

        private static AbstractCharacter Target(ICombatData cd)
        {
            if (cd == null) return null;
            var sel = cd.SelectedCharacter;
            if (sel != null && sel.UserId != 0) return sel;
            return cd.MyCharacter;
        }

        private static void Refresh()
        {
            var cd = FighterHint.Cd();
            var ch = Target(cd);
            if (ch == null)
            {
                if (_title != null) _title.text = "Эффекты";
                return;
            }
            if (ch.UserId != _shownId)
            {
                _shownId = ch.UserId;
                _sig = "";
                FighterHint.Ask(ch.UserId);
            }
            else
            {
                float at;
                if (!FighterHint.AskedAt.TryGetValue(ch.UserId, out at) || Time.unscaledTime - at > 3f) FighterHint.Ask(ch.UserId);
            }

            bool me = cd.MyCharacter != null && cd.MyCharacter.UserId == ch.UserId;
            bool friend = me || (cd.MyCharacter != null && ch.Team == cd.MyCharacter.Team);
            _title.text = "Эффекты: " + (ch.Login ?? "?") + (me ? " (ты)" : "") + (ch.Level > 0 ? "   " + ch.Level + " ур." : "");
            var ind = ch.Indicators;
            _bars.text = ind == null ? "" :
                "<color=#ff6a5a>" + ind.CurrentLife + " / " + ind.MaxLife + "</color>   "
                + "<color=#6db3ff>" + ind.CurrentMana + " / " + ind.MaxMana + "</color>   "
                + "<color=#ffd257>" + (friend ? ind.CurrentStamina + " / " + ind.MaxStamina : "?") + "</color>";

            List<UserEnchantmentsResponseItem> items;
            bool known = FighterHint.States.TryGetValue(ch.UserId, out items);
            var sig = new StringBuilder();
            sig.Append(ch.UserId).Append('|').Append(FighterHint.NamesVersion).Append('|');
            if (known)
                foreach (var it in items)
                {
                    sig.Append(it.StateType).Append(':').Append(it.StateId).Append(':').Append(it.Duration).Append(':').Append(it.Highlighting).Append(':').Append(FighterHint.Power(it)).Append(':');
                    if (it.Sources != null) foreach (var s in it.Sources) sig.Append(s.SourceUserId).Append('/');
                    sig.Append(';');
                }
            else sig.Append(FighterHint.Silent(ch.UserId) ? "?!" : "?");
            string now = sig.ToString();
            if (now == _sig) return;
            _sig = now;

            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _rowIndex = 0;
            if (!known) { AddRow(FighterHint.Silent(ch.UserId) ? "эффекты не пришли" : "загружаю…", "", "", "", new Color32(230, 215, 180, 255)); return; }
            if (items.Count == 0) { AddRow("нет эффектов", "", "", "", new Color32(230, 215, 180, 255)); return; }
            foreach (var it in items)
            {
                string key = "states.state_" + it.StateType + "_" + it.StateId;
                string name = ResourceStrings.GetString(key + ".name");
                if (name == key + ".name") name = "состояние " + it.StateType + "/" + it.StateId;
                string src = FighterHint.Sources(it, cd, ch.UserId);
                int power = FighterHint.Power(it);
                string dur = it.Duration > 1000 ? "до конца боя" : it.Duration > 0 ? it.Duration + " " + FighterHint.Turns(it.Duration) : "";
                Color32 col = it.Highlighting == (int)EHighlightingType.Positive ? new Color32(120, 230, 120, 255)
                            : it.Highlighting == (int)EHighlightingType.Negative ? new Color32(255, 110, 100, 255)
                            : new Color32(235, 225, 200, 255);
                AddRow(name, src, power != 0 ? power.ToString() : "", dur, col);
            }
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLEffectsWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = LoadPos();
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = new Color(0.14f, 0.10f, 0.07f, 0.97f);
            pimg.sprite = OnlineWindow.Rounded(16);
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
            mover.OnDone = SavePos;

            _title = Label(_panelGo.transform, "Эффекты", 15, FontStyle.Bold, new Color32(255, 224, 130, 255));
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -34f), new Vector2(-52f, -6f));
            _title.alignment = TextAnchor.MiddleLeft;
            _title.raycastTarget = false;

            MakeCloseButton();

            _bars = Label(_panelGo.transform, "", 10, FontStyle.Bold, Color.white);
            _bars.supportRichText = true;
            Place(_bars.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -56f), new Vector2(-12f, -38f));

            var headGo = new GameObject("head", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)headGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -76f), new Vector2(-26f, -58f));
            FillRow(headGo, "Название", "Источник", "Эффект", "Длительность", new Color32(255, 224, 130, 255), FontStyle.Bold);
            var ruleGo = new GameObject("rule", typeof(RectTransform), typeof(Image));
            ruleGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)ruleGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -78f), new Vector2(-26f, -77f));
            ruleGo.GetComponent<Image>().color = new Color(0.55f, 0.42f, 0.22f, 0.7f);

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 10f), new Vector2(-26f, -80f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.25f);
            simg.sprite = OnlineWindow.Rounded(8);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 30f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
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

            var sbGo = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)sbGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-22f, 10f), new Vector2(-12f, -80f));
            sbGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var sb = sbGo.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(sbGo.transform, false);
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

        private static void AddRow(string name, string src, string power, string dur, Color32 color)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(_rows, false);
            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bg.color = (_rowIndex++ & 1) == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowH; le.minHeight = RowH;
            FillRow(go, name, src, power, dur, color, FontStyle.Normal);
        }

        private static void FillRow(GameObject go, string name, string src, string power, string dur, Color32 color, FontStyle style)
        {
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 6f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            Cell(go.transform, name, 132f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, src, 110f, style, color, TextAnchor.MiddleLeft, true);
            Cell(go.transform, power, 38f, style, color, TextAnchor.MiddleRight, false);
            Cell(go.transform, dur, 84f, style, color, TextAnchor.MiddleLeft, false);
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color, TextAnchor align, bool shrink)
        {
            var t = Label(row, text, 10, style, color);
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            if (shrink) { t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = 10; }
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = RowH - 2f; le.minHeight = RowH - 2f;
        }

        private static Sprite Info()
        {
            if (_btnIconSprite != null) return _btnIconSprite;
            try
            {
                var holder = VisualPrefabsHolder.Instance;
                if (holder != null && holder.UserContextMenuInfoSprite != null) _btnIconSprite = holder.UserContextMenuInfoSprite;
            }
            catch (Exception e) { Plugin.Trace("[эффекты] значок кнопки: " + e.Message); }
            return _btnIconSprite;
        }

        private static SimpleSectorButtonSelector FindProto()
        {
            SimpleSectorButtonSelector found = null;
            float best = float.NegativeInfinity;
            foreach (var one in UnityEngine.Object.FindObjectsOfType<SimpleSectorButtonSelector>())
            {
                if (one == null || !one.gameObject.activeInHierarchy) continue;
                if (_btnGo != null && one.transform.IsChildOf(_btnGo.transform)) continue;
                float x = one.transform.position.x;
                if (x > best) { best = x; found = one; }
            }
            if (found == null) return null;
            _btnProto = found;
            _btnProtoImage = AccessTools.Field(typeof(BaseStateButton), "ButtonImage")?.GetValue(found) as Image;
            if (found.DisableSprite != null) _btnOffSprite = found.DisableSprite;
            return found;
        }

        private static void ButtonState()
        {
            if (_btnGo == null || Time.unscaledTime < _btnStateAt) return;
            _btnStateAt = Time.unscaledTime + 0.2f;
            if (_btnButton != null && !_btnButton.interactable) _btnButton.interactable = true;
            if (_btnDisc == null) return;
            Info();
            if (_btnProtoImage == null) FindProto();
            string near = _btnProtoImage != null && _btnProtoImage.sprite != null ? _btnProtoImage.sprite.name : "";
            bool lost = _btnProtoImage == null;
            bool shown = lost || (_btnProtoImage.enabled && _btnProtoImage.gameObject.activeInHierarchy);
            bool grey = !shown || (!lost && (near.Length == 0 || near.EndsWith("_disabled", StringComparison.OrdinalIgnoreCase)));
            int look = grey ? 1 : 0;
            if (look == _btnLook) return;
            _btnLook = look;
            if (grey) _btnDisc.enabled = false;
            else
            {
                if (_btnIconSprite != null) _btnDisc.sprite = _btnIconSprite;
                _btnDisc.color = Color.white;
                _btnDisc.enabled = _btnIconSprite != null;
            }
            Plugin.Trace("[эффекты] сосед: " + (near.Length > 0 ? near : "нет картинки") + (shown ? "" : ", слой скрыт") + " -> " + (grey ? "серый" : "значок"));
        }

        private static void EnsureButton()
        {
            if (_btnGo != null || Time.unscaledTime < _btnAt) return;
            _btnAt = Time.unscaledTime + 1f;
            var proto = FindProto();
            if (proto == null) return;

            var button = AccessTools.Field(typeof(BaseStateButton), "Button")?.GetValue(proto) as Button;
            var image = AccessTools.Field(typeof(BaseStateButton), "ButtonImage")?.GetValue(proto) as Image;
            if (button == null || image == null) return;

            var go = UnityEngine.Object.Instantiate(proto.gameObject, proto.transform.parent);
            go.name = "QoLEffectsButton";
            SkillList.Veil(go, false);
            go.transform.SetSiblingIndex(proto.transform.GetSiblingIndex() + 1);
            var cBottomText = AccessTools.Field(typeof(BaseCommandButton), "BottomText")?.GetValue(go.GetComponent<BaseCommandButton>()) as Text;
            var cButton = AccessTools.Field(typeof(BaseStateButton), "Button")?.GetValue(go.GetComponent<BaseStateButton>()) as Button;
            var cImage = AccessTools.Field(typeof(BaseStateButton), "ButtonImage")?.GetValue(go.GetComponent<BaseStateButton>()) as Image;
            Clones.StripHotkeys(go, proto.gameObject);
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c is BaseStateButton || c is HintHolder) UnityEngine.Object.Destroy(c);
            }
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.Destroy(a);
            if (cBottomText != null)
            {
                cBottomText.text = "ЭФФЕКТЫ";
                cBottomText.resizeTextForBestFit = true;
                cBottomText.resizeTextMinSize = 8;
                cBottomText.resizeTextMaxSize = cBottomText.fontSize;
            }
            var cRecharge = AccessTools.Field(typeof(SimpleSectorButtonSelector), "RechargeImage")?.GetValue(go.GetComponent<SimpleSectorButtonSelector>()) as Image;
            if (cRecharge != null) cRecharge.gameObject.SetActive(false);
            _btnOffSprite = proto.DisableSprite;
            _btnProtoImage = image;
            if (cImage == null && image != null)
            {
                foreach (var candidate in go.GetComponentsInChildren<Image>(true))
                {
                    if (candidate == null || candidate.gameObject.name != image.gameObject.name) continue;
                    cImage = candidate;
                    break;
                }
            }
            if (cImage != null)
            {
                cImage.enabled = true;
                cImage.color = Color.white;
                cImage.preserveAspect = true;
                var icon = Info();
                if (icon != null) cImage.sprite = icon;
            }
            if (cImage != null)
            {
                var chain = cImage.transform;
                while (chain != null && chain != go.transform)
                {
                    if (!chain.gameObject.activeSelf) chain.gameObject.SetActive(true);
                    chain = chain.parent;
                }
                foreach (var lid in cImage.GetComponentsInChildren<Transform>(true))
                {
                    if (lid == null || lid == cImage.transform) continue;
                    lid.gameObject.SetActive(false);
                }
            }
            Plugin.Trace("[эффекты] кнопка: диск " + (cImage != null ? cImage.gameObject.name : "не найден")
                         + ", значок " + (Info() != null ? Info().name : "не найден")
                         + ", серый круг " + (proto.DisableSprite != null ? proto.DisableSprite.name : "нет")
                         + ", сосед показывает " + (image != null && image.sprite != null ? image.sprite.name : "ничего"));
            _btnProto = proto;
            _btnButton = cButton;
            _btnDisc = cImage;
            _btnLook = -1;
            _btnStateAt = 0f;

            var parentLayout = proto.transform.parent != null ? proto.transform.parent.GetComponent<LayoutGroup>() : null;
            if (parentLayout == null)
            {
                var prt = (RectTransform)proto.transform;
                var grt = (RectTransform)go.transform;
                grt.anchoredPosition = prt.anchoredPosition + new Vector2(prt.rect.width + 6f, 0f);
            }

            if (cButton != null)
            {
                cButton.onClick = new Button.ButtonClickedEvent();
                cButton.onClick.AddListener(Toggle);
                cButton.interactable = true;
            }
            go.SetActive(true);
            _btnGo = go;
            Plugin.Trace("[эффекты] кнопка добавлена рядом с " + proto.gameObject.name);
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
                    rt.localScale = Vector3.one * 0.75f;
                    var b = go.GetComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(Close);
                    go.SetActive(true);
                    return;
                }
            }
            catch { if (go != null) UnityEngine.Object.Destroy(go); }
            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(24f, 24f); crt.anchoredPosition = new Vector2(-6f, -6f);
            closeGo.GetComponent<Image>().color = new Color(0.6f, 0.15f, 0.1f, 1f);
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 14, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static Vector2 LoadPos()
        {
            var pos = new Vector2(0f, PanelH * 0.5f + 60f);
            try
            {
                var parts = (Plugin.CfgEffectsWindow?.Value ?? "").Split(';');
                float x, y;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out y)) pos = new Vector2(x, y);
            }
            catch { }
            return pos;
        }

        private static void SavePos()
        {
            try
            {
                if (_panelGo == null || Plugin.CfgEffectsWindow == null) return;
                var rt = (RectTransform)_panelGo.transform;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Plugin.CfgEffectsWindow.Value = rt.anchoredPosition.x.ToString("0", ci) + ";" + rt.anchoredPosition.y.ToString("0", ci);
            }
            catch { }
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
