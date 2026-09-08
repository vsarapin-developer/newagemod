using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Hints;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class SkillList
    {
        private const float Side = 46f;
        private const float Gap = 4f;
        private const int Columns = 6;
        private const float TopGap = 104f;
        private const float SideGap = 14f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Transform _cells;
        private static string _sig = "";
        private static float _pollAt;
        private static float _sourceAt;
        private static GameObject _hidden;
        private static bool _found;
        private static CombatButtonsController _ctrl;
        private static IQuickButton _armed;
        private sealed class Cell
        {
            internal Image Icon;
            internal Image Dim;
            internal Outline Edge;
            internal Button Press;
            internal Image Mark;
            internal Image Cool;
            internal Text Left;
            internal Text Count;
            internal string Block;
        }

        private static readonly Dictionary<int, Cell> Live = new Dictionary<int, Cell>();
        private static readonly Dictionary<int, float> Used = new Dictionary<int, float>();
        private static readonly Dictionary<int, string> Told = new Dictionary<int, string>();
        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static GameObject _tipGo;
        private static Text _tipText;
        private static int _tipFor;
        private static bool _listening;
        private static readonly Vector2 Spot = new Vector2(-600f, -30f);
        private static Texture2D _cursor;
        private static int _hexPick;
        private static int _hexFor;
        private static bool _hexOk = true;
        private static AbstractCharacter _lit;
        private static Light _lamp;
        private static MaterialPropertyBlock _paint;
        private static Renderer _painted;
        private static GameObject _stopGo;

        internal static bool Enabled => Plugin.CfgSkillList != null && Plugin.CfgSkillList.Value;

        internal static bool Armed => _armed != null;

        internal static void Tick()
        {
            try
            {
                if (!Enabled || !SideButtons.InCombat()) { Off(); return; }
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.15f;
                Source();
                if (!_found) { Close(); return; }
                Veil(_hidden, true);
                if (_canvasGo == null) Build();
                Refresh();
                Stop();
                Place();
            }
            catch (Exception e) { Plugin.Trace("[умения] " + e.Message); }
        }

        internal static void Aim()
        {
            try
            {
                if (_armed == null || !Enabled || !SideButtons.InCombat()) { if (_armed != null) Disarm(); return; }
                if (Input.GetMouseButtonDown(1)) { Disarm(); return; }
                if (Why(_armed) != null) { Disarm(); return; }

                var cd = FighterHint.Cd();
                if (cd == null) return;
                bool over = Unity3DHelper.IsOverInterface();
                int id = over ? 0 : FighterHint.Under(cd, false);
                var target = id != 0 ? cd.GetCharacter(id) : null;
                Frame(target);
                Pulse();

                if (!Input.GetMouseButtonDown(0) || target == null) return;

                var skill = _armed;
                cd.SelectedCharacter = target;
                var verdict = Check(skill);
                if (verdict != EQuickButtonValidationResult.Success)
                {
                    Refuse(skill, verdict);
                    return;
                }
                Disarm();
                Fire(skill);
            }
            catch (Exception e) { Plugin.Trace("[умения] цель: " + e.Message); }
        }

        private static void Frame(AbstractCharacter target)
        {
            var cd = FighterHint.Cd();
            if (cd == null || target == null) { ClearHex(); return; }
            bool fits = Check(_armed) == EQuickButtonValidationResult.Success;
            if (_hexFor == target.UserId && _hexPick != 0 && _hexOk == fits) return;

            ClearHex();
            _hexFor = target.UserId;
            try { if (cd.SelectedCharacter != target) cd.SelectedCharacter = target; }
            catch (Exception e) { Plugin.Trace("[умения] выделение бойца: " + e.Message); }

            _hexOk = Check(_armed) == EQuickButtonValidationResult.Success;
            var paint = _hexOk ? new Color(0.2f, 2.4f, 0.25f, 1f) : new Color(2.2f, 0.15f, 0.1f, 1f);
            try { _hexPick = cd.SetSelection(target.HexGridPosition, 0, paint, true, true); }
            catch (Exception e) { Plugin.Trace("[умения] клетка цели: " + e.Message); }
            Glow(target);
        }

        private static void Glow(AbstractCharacter target)
        {
            if (ReferenceEquals(_lit, target)) return;
            Unglow();
            _lit = target;
        }

        private static void Pulse()
        {
            if (_lit == null) return;
            try
            {
                if (_lamp == null)
                {
                    var go = new GameObject("QoLGlow", typeof(Light));
                    _lamp = go.GetComponent<Light>();
                    _lamp.type = LightType.Point;
                    _lamp.color = new Color(0.4f, 1f, 0.45f, 1f);
                    _lamp.shadows = LightShadows.None;
                    _lamp.renderMode = LightRenderMode.ForcePixel;
                }

                var body = _lit.CharacterCollider;
                var holder = _lit.CharacterMeshRendererHolder;
                var skin = holder != null ? holder.MainRenderer : null;
                var box = skin != null ? skin.bounds : body != null ? body.bounds : new Bounds(_lit.position, Vector3.one);
                _lamp.transform.position = box.center;
                _lamp.range = Mathf.Clamp(box.size.magnitude * 1.2f, 1.6f, 4f);

                int mask = 0;
                if (body != null) mask |= 1 << body.gameObject.layer;
                if (skin != null) mask |= 1 << skin.gameObject.layer;
                _lamp.cullingMask = mask != 0 ? mask : ~0;

                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                _lamp.intensity = 8f + 10f * wave;
                if (!_lamp.enabled) _lamp.enabled = true;

                Tint(wave);
            }
            catch (Exception e) { Plugin.Trace("[умения] подсветка модели: " + e.Message); }
        }

        private static void Tint(float wave)
        {
            try
            {
                var holder = _lit.CharacterMeshRendererHolder;
                var skin = holder != null ? holder.MainRenderer : null;
                if (skin == null) return;
                if (_paint == null) _paint = new MaterialPropertyBlock();
                skin.GetPropertyBlock(_paint);
                var glow = new Color(0.25f + 0.25f * wave, 1f, 0.3f + 0.25f * wave, 1f);
                _paint.SetColor("_Color", glow);
                _paint.SetColor("_BaseColor", glow);
                _paint.SetColor("_TintColor", glow);
                _paint.SetColor("_EmissionColor", new Color(0.1f, 0.35f + 0.5f * wave, 0.15f, 1f));
                _paint.SetColor("_RimColor", glow);
                _paint.SetColor("_OutlineColor", glow);
                skin.SetPropertyBlock(_paint);
                _painted = skin;
            }
            catch (Exception e) { Plugin.Trace("[умения] окраска модели: " + e.Message); }
        }

        private static void Unglow()
        {
            if (_lamp != null) _lamp.enabled = false;
            if (_painted != null)
            {
                try { _painted.SetPropertyBlock(null); } catch { }
                _painted = null;
            }
            _lit = null;
        }

        private static EQuickButtonValidationResult Check(IQuickButton skill)
        {
            if (skill == null) return EQuickButtonValidationResult.Success;
            try
            {
                var data = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                var holder = Holder();
                var judge = holder != null ? holder.Validator : null;
                if (data == null || judge == null) return EQuickButtonValidationResult.Success;
                return judge.Validate(data, skill, null);
            }
            catch (Exception e)
            {
                Plugin.Trace("[умения] проверка цели: " + e.Message);
                return EQuickButtonValidationResult.Success;
            }
        }

        private static void Refuse(IQuickButton skill, EQuickButtonValidationResult verdict)
        {
            try
            {
                string key = QuickButtonValidationResultExtension.GetValidationMessage(skill, verdict);
                if (string.IsNullOrEmpty(key)) key = "combat.gui.combatbutton.disablecause.target_not_selected";
                AirMessageScript.ShowErrorNotification(key);
                Plugin.Trace("[умения] цель не годится: " + verdict);
            }
            catch (Exception e) { Plugin.Trace("[умения] отказ по цели: " + e.Message); }
        }

        private static void ClearHex()
        {
            Unglow();
            if (_hexPick == 0) { _hexFor = 0; return; }
            try
            {
                var cd = FighterHint.Cd();
                if (cd != null) cd.ClearSelection(_hexPick);
            }
            catch (Exception e) { Plugin.Trace("[умения] снять клетку: " + e.Message); }
            _hexPick = 0;
            _hexFor = 0;
        }

        private static void Point(bool on)
        {
            try
            {
                if (!on) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
                if (_cursor == null) _cursor = Crosshair();
                if (_cursor != null) Cursor.SetCursor(_cursor, new Vector2(15f, 15f), CursorMode.Auto);
            }
            catch (Exception e) { Plugin.Trace("[умения] курсор: " + e.Message); }
        }

        private static Texture2D Crosshair()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var gold = new Color(1f, 0.82f, 0.3f, 1f);
            var dark = new Color(0.15f, 0.09f, 0.02f, 0.95f);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 15.5f, dy = y - 15.5f;
                    float far = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = far > 8.5f && far < 11.5f;
                    bool edge = far > 7.5f && far <= 8.5f || far >= 11.5f && far < 12.5f;
                    bool cross = far < 6f && (Mathf.Abs(dx) < 1.2f || Mathf.Abs(dy) < 1.2f);
                    tex.SetPixel(x, y, ring || cross ? gold : edge ? dark : clear);
                }
            tex.Apply();
            return tex;
        }

        private static void Disarm()
        {
            _armed = null;
            Point(false);
            ClearHex();
            if (_stopGo != null) _stopGo.SetActive(false);
            foreach (var pair in Live)
                if (pair.Value.Mark != null) pair.Value.Mark.enabled = false;
        }

        private static void Arm(IQuickButton skill)
        {
            _armed = skill;
            Point(true);
            foreach (var pair in Live)
                if (pair.Value.Mark != null) pair.Value.Mark.enabled = pair.Key == skill.Id;
        }

        private static void Fire(IQuickButton skill)
        {
            try
            {
                Used[skill.Id] = Time.unscaledTime;
                _pollAt = 0f;
                Repaint();
                var ctrl = _ctrl ?? DependencyContainer.ResolveController<CombatButtonsController>();
                if (ctrl == null) return;
                var confirm = AccessTools.Method(typeof(CombatButtonsController), "OnActionConfirmed");
                if (confirm == null) return;
                confirm.Invoke(ctrl, new object[] { skill, null });
                Plugin.Trace("[умения] применяю " + skill.Id);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[умения] применение " + skill.Id + ": " + e.Message); }
        }

        private static IQuickButton Skill(int id)
        {
            try
            {
                var data = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                var manager = data?.CombatData?.ButtonManager;
                return manager == null ? null : manager.Skills.GetButton(id);
            }
            catch (Exception e) { Plugin.Trace("[умения] умение " + id + ": " + e.Message); return null; }
        }

        private static void Place()
        {
            if (_panelGo == null) return;
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            var spot = new Vector2(280f, -8f);
            if (prt.anchoredPosition != spot) prt.anchoredPosition = spot;

            if (_stopGo == null) return;
            var srt = (RectTransform)_stopGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(spot.x, spot.y - prt.rect.height - 4f);
        }

        private static void Stop()
        {
            if (_canvasGo == null) return;
            if (_stopGo == null)
            {
                _stopGo = new GameObject("cancel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
                _stopGo.transform.SetParent(_canvasGo.transform, false);
                var back = _stopGo.GetComponent<Image>();
                back.color = new Color(0.34f, 0.07f, 0.05f, 0.95f);
                back.sprite = OnlineWindow.Rounded(6);
                back.type = Image.Type.Sliced;
                var edge = _stopGo.GetComponent<Outline>();
                edge.effectColor = new Color(1f, 0.55f, 0.4f, 0.95f);
                edge.effectDistance = new Vector2(2f, -2f);
                var srt = (RectTransform)_stopGo.transform;
                srt.sizeDelta = new Vector2(Side, Side);
                var text = OnlineWindow.Label(_stopGo.transform, "✕", 30, FontStyle.Bold, new Color32(255, 220, 205, 255));
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var stop = _stopGo.GetComponent<Button>();
                stop.targetGraphic = back;
                var colors = stop.colors;
                colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
                stop.colors = colors;
                stop.onClick.AddListener(Disarm);
            }
            bool want = _armed != null;
            if (_stopGo.activeSelf != want) _stopGo.SetActive(want);
        }

        internal static bool EscapeClose()
        {
            if (_armed == null) return false;
            Disarm();
            return true;
        }

        internal static void Off()
        {
            if (_armed != null) Disarm();
            Point(false);
            Veil(_hidden, false);
            _hidden = null;
            _found = false;
            _ctrl = null;
            _armed = null;
            _sourceAt = 0f;
            Close();
        }

        private static void Source()
        {
            if (_found && Time.unscaledTime < _sourceAt) return;
            _sourceAt = Time.unscaledTime + 2f;
            try
            {
                if (Holder() == null) { _found = false; return; }
                _found = true;
                if (_hidden != null) return;
                _ctrl = null;
                var button = FromController() ?? FromScene();
                if (button == null) { Plugin.Trace("[умения] кнопка умений не найдена"); return; }
                _hidden = button.gameObject;
                Plugin.Trace("[умения] кнопка спрятана: " + button.name);
            }
            catch (Exception e) { Plugin.Trace("[умения] источник: " + e.Message); }
        }

        private static QuickButtonStateHolder Holder()
        {
            try
            {
                var data = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                var manager = data?.CombatData?.ButtonManager;
                return manager != null ? manager.Skills : null;
            }
            catch { return null; }
        }

        private static List<IQuickButton> All()
        {
            var list = new List<IQuickButton>();
            var holder = Holder();
            if (holder == null) return list;
            var seen = new HashSet<int>();
            foreach (var round in new[] { RoundType.COMBAT_ROUND, RoundType.WALK_ROUND })
            {
                var part = holder.GetButtonsByRoundType(round);
                if (part == null) continue;
                foreach (var one in part)
                    if (one != null && seen.Add(one.Id)) list.Add(one);
            }
            return list;
        }

        private static SimpleSectorButtonSelector FromController()
        {
            try
            {
                var ctrl = DependencyContainer.ResolveController<CombatButtonsController>();
                if (ctrl == null) return null;
                _ctrl = ctrl;
                return AccessTools.Property(typeof(CombatButtonsController), "MasteriesButton")?.GetValue(ctrl) as SimpleSectorButtonSelector;
            }
            catch (Exception e) { Plugin.Trace("[умения] контроллер боя: " + e.Message); return null; }
        }

        private static SimpleSectorButtonSelector FromScene()
        {
            foreach (var one in UnityEngine.Object.FindObjectsOfType<SimpleSectorButtonSelector>())
            {
                if (one == null || !one.gameObject.activeInHierarchy) continue;
                if (one.gameObject.name.StartsWith("QoL", StringComparison.Ordinal)) continue;
                var text = AccessTools.Field(typeof(BaseCommandButton), "BottomText")?.GetValue(one) as Text;
                string caption = text != null ? (text.text ?? "") : "";
                if (caption.IndexOf("умен", StringComparison.OrdinalIgnoreCase) >= 0) return one;
            }
            return null;
        }

        internal static void Veil(GameObject go, bool hide)
        {
            if (go == null) return;
            var veil = go.GetComponent<CanvasGroup>();
            if (veil != null) { veil.alpha = 1f; veil.blocksRaycasts = true; veil.interactable = true; }
            if (go.activeSelf == !hide) return;
            go.SetActive(!hide);
            var parent = go.transform.parent as RectTransform;
            if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        private static void Close()
        {
            _tipGo = null;
            _tipText = null;
            _tipFor = 0;
            _stopGo = null;
            Live.Clear();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panelGo = null;
            _cells = null;
            _sig = "";
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLSkillList", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _panelGo = new GameObject("skills", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            _panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-SideGap, -TopGap);

            var grid = _panelGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Side, Side);
            grid.spacing = new Vector2(Gap, Gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            var fit = _panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _cells = _panelGo.transform;
        }

        private static void Repaint()
        {
            try { if (_cells != null) Paint(All()); }
            catch (Exception e) { Plugin.Trace("[умения] перерисовка: " + e.Message); }
        }

        private static void Refresh()
        {
            if (_cells == null) return;
            var all = All();

            var sig = new StringBuilder();
            foreach (var one in all) sig.Append(one.Id).Append(',');
            string now = sig.ToString();
            if (now != _sig)
            {
                _sig = now;
                Rebuild(all);
            }
            Paint(all);
        }

        private static void Rebuild(List<IQuickButton> all)
        {
            HideTip();
            for (int i = _cells.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_cells.GetChild(i).gameObject);
            Live.Clear();
            foreach (var one in all) Slot(one);
        }

        private static void Paint(List<IQuickButton> all)
        {
            foreach (var skill in all)
            {
                Cell cell;
                if (!Live.TryGetValue(skill.Id, out cell)) continue;
                string why = Why(skill);
                bool ready = why == null;
                bool cooling = Cooling(skill);
                int rounds = skill.Recharge - skill.Turn;
                cell.Block = why;
                if (cell.Icon != null) cell.Icon.color = ready ? Color.white : cooling ? new Color(1f, 1f, 1f, 0.75f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
                if (cell.Dim != null) cell.Dim.enabled = !ready && !cooling;
                if (cell.Cool != null)
                {
                    cell.Cool.enabled = cooling;
                    if (cooling) cell.Cool.fillAmount = Mathf.Clamp01(1f - (float)skill.Turn / skill.Recharge);
                }
                if (cell.Left != null)
                {
                    bool show = cooling && rounds > 0;
                    cell.Left.enabled = show;
                    if (show) cell.Left.text = rounds.ToString();
                }
                if (cell.Count != null)
                {
                    bool show = skill.Count > 0;
                    cell.Count.enabled = show;
                    if (show) cell.Count.text = skill.Count.ToString();
                }
                if (cell.Edge != null) cell.Edge.effectColor = ready ? new Color(0.62f, 0.5f, 0.3f, 0.95f) : new Color(0.3f, 0.26f, 0.18f, 0.7f);
                if (cell.Press != null) cell.Press.interactable = ready;
                if (!ready && _armed != null && _armed.Id == skill.Id) Disarm();
            }
        }

        private static bool Cooling(IQuickButton skill)
        {
            return !skill.CanActivate && skill.Recharge > 0 && skill.Turn < skill.Recharge;
        }

        internal static string Why(IQuickButton skill)
        {
            if (skill == null) return null;
            float used;
            if (Used.TryGetValue(skill.Id, out used) && Time.unscaledTime - used < 0.6f) return "применяю…";
            var cd = FighterHint.Cd();
            if (cd != null && cd.RoundType != RoundType.WALK_ROUND && cd.RoundType != RoundType.COMBAT_ROUND) return "идёт расчёт раунда";
            if (cd != null && !QuickButtonHelper.CheckRoundType(skill, cd.RoundType)) return "не в этой фазе";
            if (!skill.Enabled) return string.IsNullOrEmpty(skill.DisableCause) ? "недоступно" : skill.DisableCause;
            if (Cooling(skill)) return "перезарядка";
            if (!skill.CanActivate) return "перезарядка";
            var me = cd != null ? cd.MyCharacter : null;
            var ind = me != null ? me.Indicators : null;
            if (ind == null) return null;
            if (skill.StaminaCost > ind.CurrentStamina) return "не хватает энергии";
            if (skill.ManaCost > ind.CurrentMana) return "не хватает маны";
            if (skill.ExpowerCost > ind.CurrentExpower) return "не хватает силы";
            return null;
        }

        private static void Listen()
        {
            if (_listening) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.RemoveMessageListener(419, OnHint);
                nc.AddMessageListener(419, OnHint);
                _listening = true;
            }
            catch (Exception e) { Plugin.Trace("[умения] слушатель подсказок: " + e.Message); }
        }

        private static void OnHint(object message)
        {
            try
            {
                var answer = message as DynamicHintResponseMessage;
                if (answer == null || answer.Request == null || answer.Request.HintType != (int)EHintType.SKILL) return;
                int id = answer.Request.Id;
                var skill = Skill(id);
                string raw = skill != null ? skill.Description : null;
                if (string.IsNullOrEmpty(raw)) return;
                Told[id] = DynamicHintHelper.PrepareActionDescription(raw, answer.ActionEffectMessage);
                if (_tipFor == id) Fill(id);
            }
            catch (Exception e) { Plugin.Trace("[умения] описание: " + e.Message); }
        }

        private static void AskTold(int id)
        {
            if (Told.ContainsKey(id) || !Asked.Add(id)) return;
            try
            {
                Listen();
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(new DynamicHintRequest(EHintType.SKILL, id, 0));
            }
            catch (Exception e) { Plugin.Trace("[умения] запрос описания " + id + ": " + e.Message); }
        }

        private static string Phase(int phase)
        {
            if (phase == 1) return "фаза перемещения";
            if (phase == 2) return "фаза боя";
            if (phase == 3) return "любая фаза";
            return "";
        }

        private static string Costs(IQuickButton skill)
        {
            var parts = new List<string>();
            if (skill.ManaCost > 0) parts.Add("мана " + skill.ManaCost);
            if (skill.StaminaCost > 0) parts.Add("энергия " + skill.StaminaCost);
            if (skill.ExpowerCost > 0) parts.Add("сила " + skill.ExpowerCost);
            if (skill.Count > 0) parts.Add("зарядов " + skill.Count);
            if (skill.Recharge > 0) parts.Add("перезарядка " + skill.Recharge);
            return string.Join(", ", parts.ToArray());
        }

        private static void ShowTip(int id, RectTransform near)
        {
            try
            {
                _tipFor = id;
                AskTold(id);
                if (_tipGo == null) BuildTip();
                if (_tipGo == null) return;
                Fill(id);
                _tipGo.SetActive(true);

                var corners = new Vector3[4];
                near.GetWorldCorners(corners);
                var point = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                var root = (RectTransform)_canvasGo.transform;
                Vector2 spot;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, point, null, out spot))
                {
                    var trt = (RectTransform)_tipGo.transform;
                    trt.pivot = new Vector2(1f, 1f);
                    trt.anchoredPosition = new Vector2(spot.x + near.rect.width, spot.y - 6f);
                }
            }
            catch (Exception e) { Plugin.Trace("[умения] подсказка " + id + ": " + e.Message); }
        }

        private static void Fill(int id)
        {
            if (_tipText == null) return;
            var skill = Skill(id);
            var text = new StringBuilder();
            string name = skill != null && !string.IsNullOrEmpty(skill.Name) ? skill.Name : "";
            text.Append("<b><color=#ffe082>").Append(name).Append("</color></b>");
            string block = Reason(id);
            if (block != null) text.Append("\n<color=#ff8f82>").Append(block).Append("</color>");
            if (skill != null)
            {
                string phase = Phase(skill.Phase);
                if (phase.Length > 0) text.Append("\n<color=#d3c1a0>").Append(phase).Append("</color>");
                string cost = Costs(skill);
                if (cost.Length > 0) text.Append("\n<color=#ffc85a>").Append(cost).Append("</color>");
                string told;
                string body = Told.TryGetValue(id, out told) ? told : skill.Description;
                if (!string.IsNullOrEmpty(body)) text.Append("\n\n").Append(body.Trim());
            }
            _tipText.text = text.ToString();
        }

        private static string Reason(int id)
        {
            Cell cell;
            return Live.TryGetValue(id, out cell) ? cell.Block : null;
        }

        private static void HideTip()
        {
            _tipFor = 0;
            if (_tipGo != null) _tipGo.SetActive(false);
        }

        private static void BuildTip()
        {
            if (_canvasGo == null) return;
            _tipGo = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            _tipGo.transform.SetParent(_canvasGo.transform, false);
            var trt = (RectTransform)_tipGo.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(1f, 1f);

            var back = _tipGo.GetComponent<Image>();
            back.color = new Color(0.12f, 0.09f, 0.06f, 0.97f);
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            edge.effectDistance = new Vector2(1f, -1f);

            var veil = _tipGo.GetComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;

            var box = _tipGo.GetComponent<VerticalLayoutGroup>();
            box.padding = new RectOffset(10, 10, 8, 8);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = true;
            box.childForceExpandHeight = false;

            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            trt.sizeDelta = new Vector2(320f, 60f);

            _tipText = OnlineWindow.Label(_tipGo.transform, "", 14, FontStyle.Normal, new Color32(240, 232, 214, 255));
            _tipText.alignment = TextAnchor.UpperLeft;
            _tipText.supportRichText = true;
            _tipText.raycastTarget = false;
            _tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            var le = _tipText.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 300f;
            _tipGo.SetActive(false);
        }

        private static void Pick(IQuickButton skill)
        {
            try
            {
                if (skill == null) return;
                if (TargetTypeExtension.IsActionHasCellTarget(skill)) { Dialog(skill.Id); return; }
                if (skill.Target == ETargetType.TARGET_SOURCE) { Disarm(); Fire(skill); return; }
                if (_armed != null && _armed.Id == skill.Id) { Disarm(); return; }
                Arm(skill);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[умения] выбор " + skill.Id + ": " + e.Message); }
        }

        private static void Dialog(int id)
        {
            try
            {
                var holder = Holder();
                var dialog = DependencyContainer.ResolveController<ConfirmActionDialogController>();
                if (holder == null || dialog == null) return;
                dialog.OpenDialog(holder, id);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[умения] окно выбора " + id + ": " + e.Message); }
        }

        private static void Slot(IQuickButton skill)
        {
            var go = new GameObject("skill", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_cells, false);

            var frame = go.GetComponent<Image>();
            frame.color = new Color(0.09f, 0.07f, 0.05f, 0.92f);
            frame.sprite = OnlineWindow.Rounded(6);
            frame.type = Image.Type.Sliced;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = new Color(0.62f, 0.5f, 0.3f, 0.95f);
            edge.effectDistance = new Vector2(2f, -2f);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = AtlasUtils.GetQuickButtonSprite(skill);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var dimGo = new GameObject("dim", typeof(RectTransform), typeof(Image));
            dimGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)dimGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var dim = dimGo.GetComponent<Image>();
            dim.color = new Color(0.05f, 0.04f, 0.03f, 0.55f);
            dim.sprite = OnlineWindow.Rounded(6);
            dim.type = Image.Type.Sliced;
            dim.raycastTarget = false;

            var coolGo = new GameObject("cool", typeof(RectTransform), typeof(Image));
            coolGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)coolGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var cool = coolGo.GetComponent<Image>();
            cool.sprite = OnlineWindow.Rounded(6);
            cool.color = new Color(0f, 0f, 0f, 0.62f);
            cool.type = Image.Type.Filled;
            cool.fillMethod = Image.FillMethod.Radial360;
            cool.fillOrigin = (int)Image.Origin360.Top;
            cool.fillClockwise = false;
            cool.fillAmount = 0f;
            cool.raycastTarget = false;
            cool.enabled = false;

            var count = OnlineWindow.Label(go.transform, "", 13, FontStyle.Bold, new Color32(255, 232, 160, 255));
            count.alignment = TextAnchor.LowerRight;
            count.raycastTarget = false;
            var shadow = count.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-3f, -2f));
            count.enabled = false;

            var watch = go.AddComponent<HoverWatch>();
            int who = skill.Id;
            var where = (RectTransform)go.transform;
            watch.OnEnter = () => ShowTip(who, where);
            watch.OnExit = HideTip;

            var markGo = new GameObject("armed", typeof(RectTransform), typeof(Image), typeof(Outline));
            markGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)markGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-2f, -2f), new Vector2(2f, 2f));
            var mark = markGo.GetComponent<Image>();
            mark.color = new Color(1f, 0.85f, 0.35f, 0.22f);
            mark.sprite = OnlineWindow.Rounded(6);
            mark.type = Image.Type.Sliced;
            mark.raycastTarget = false;
            var markEdge = markGo.GetComponent<Outline>();
            markEdge.effectColor = new Color(1f, 0.85f, 0.35f, 1f);
            markEdge.effectDistance = new Vector2(2f, -2f);
            mark.enabled = false;

            var button = go.GetComponent<Button>();
            button.targetGraphic = frame;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            var shot = skill;
            button.onClick.AddListener(() => Pick(shot));

            var left = OnlineWindow.Label(go.transform, "", 16, FontStyle.Bold, new Color32(255, 240, 200, 255));
            left.alignment = TextAnchor.MiddleCenter;
            left.raycastTarget = false;
            var leftEdge = left.gameObject.AddComponent<Outline>();
            leftEdge.effectColor = new Color(0f, 0f, 0f, 0.95f);
            leftEdge.effectDistance = new Vector2(1.5f, -1.5f);
            OnlineWindow.Place(left.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            left.enabled = false;

            Live[skill.Id] = new Cell { Icon = icon, Dim = dim, Cool = cool, Left = left, Count = count, Edge = edge, Press = button, Mark = mark };
        }
    }
}

namespace NewAgeQoL
{
    internal sealed class HoverWatch : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action OnEnter;
        internal Action OnExit;

        public void OnPointerEnter(PointerEventData e)
        {
            if (OnEnter != null) OnEnter();
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (OnExit != null) OnExit();
        }

        private void OnDisable()
        {
            if (OnExit != null) OnExit();
        }
    }
}
