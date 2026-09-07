using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Settings
    {
        private static GameObject _win;
        private static readonly List<Action> _refresh = new List<Action>();
        private static readonly List<Action> _reset = new List<Action>();

        private static readonly List<Action> _undo = new List<Action>();

        private abstract class RowDef { internal string Title; }

        private sealed class Header : RowDef { }

        private sealed class BoolRow : RowDef { internal ConfigEntry<bool> Cfg; }

        private sealed class PickRow : RowDef { internal ConfigEntry<string> ByName; internal ConfigEntry<int> ById; internal System.Func<string> Display; }

        private sealed class ValueRow : RowDef
        {
            internal Func<string> Get;
            internal Action<string> Set;
            internal Action Reset;
            internal Action Remember;
        }

        private static List<RowDef> Rows()
        {
            var rows = new List<RowDef>
            {
                new Header { Title = "Кнопки" },
                B("Кнопка возврата в Иллениум", Plugin.CfgTownButton),
                B("Возврат в Иллениум ведёт на арену, к турнирам", Plugin.CfgTownTournament),
                B("Кнопка артефактов", Plugin.CfgArtifactButtons),

                new Header { Title = "Банки" },
                B("Кнопки банок", Plugin.CfgFlaskButtons),
                B("Пить до полного", Plugin.CfgFlaskFillToMax),
                Pick("Жизнь", Plugin.CfgFlaskHpName, Plugin.CfgFlaskHpId),
                Pick("Мана", Plugin.CfgFlaskManaName, Plugin.CfgFlaskManaId),
                Pick("Энергия", Plugin.CfgFlaskEnergyName, Plugin.CfgFlaskEnergyId),
                Pick("Грибы", Plugin.CfgFlaskMushroomName, Plugin.CfgFlaskMushroomId),

                new Header { Title = "Бой" },
                B("Пополнение без ожидания анимаций", Plugin.CfgInstantRestore),
                B("Контрприём на себя, когда в бою игроки", Plugin.CfgCounterAuto),
                B("Обновлять контрприём на себе", Plugin.CfgCounterRefresh),
                I("Раундов между контрприёмами", Plugin.CfgCounterRefreshRounds),

                new Header { Title = "Карта" },
                B("Номера точек внешнего мира", Plugin.CfgMapLabels),
                B("Подписывать, что это за точка", Plugin.CfgMapLabelType),
                F("Размер номера точки", Plugin.CfgMapLabelSize),

                new Header { Title = "Инвентарь" },
                B("Иконка вещи у рецепта", Plugin.CfgRecipeIcons),
                B("id предмета в названии", Plugin.CfgItemIds),
                B("Кнопки смены комплекта в снаряжении", Plugin.CfgSlotSwap),
                B("Вкладка «Контракты»", Plugin.CfgContractsTab),
                B("Номер на контракте и договоре", Plugin.CfgContractNumbers),
                B("Поиск в сумке", Plugin.CfgSearch),

                new Header { Title = "Рынок" },
                B("Несколько лотов на рынок за раз", Plugin.CfgMarketMultiLot),

                new Header { Title = "Чат" },
                B("Свои строки в логе зелёным", Plugin.CfgChatHighlight),
                B("Прятать окно «Системное сообщение»", Plugin.CfgHideSystemBoxes),
            };
            rows.RemoveAll(r => r == null);
            return rows;
        }

        private static RowDef Pick(string title, ConfigEntry<string> byName, ConfigEntry<int> byId)
        {
            if (byName == null || byId == null) return null;
            return new PickRow
            {
                Title = title,
                ByName = byName,
                ById = byId,
                Display = () =>
                {
                    if (!string.IsNullOrEmpty(byName.Value)) return byName.Value;
                    if (byId.Value > 0) { var n = Flasks.DisplayName(byId.Value); return n ?? byId.Value.ToString(); }
                    return "— выбрать —";
                },
            };
        }

        private static RowDef B(string title, ConfigEntry<bool> cfg) =>
            cfg == null ? null : new BoolRow { Title = title, Cfg = cfg };

        private static RowDef I(string title, ConfigEntry<int> cfg) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value.ToString(),
                Set = v => { if (int.TryParse(v.Trim(), out int n)) cfg.Value = n; },
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (int)cfg.DefaultValue,
            };

        private static RowDef F(string title, ConfigEntry<float> cfg) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Set = v =>
                {
                    if (float.TryParse(v.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float n)) cfg.Value = n;
                },
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (float)cfg.DefaultValue,
            };


        internal static void Toggle()
        {
            if (_win != null) { Close(); return; }
            try { Open(); }
            catch (Exception e) { Plugin.Log?.LogError("[settings] окно не открылось: " + e); Close(); }
        }

        internal static void Close() => Close(revert: false);

        internal static void Close(bool revert)
        {
            if (revert) foreach (var u in _undo) { try { u(); } catch { } }
            if (_win != null) UnityEngine.Object.Destroy(_win);
            _win = null;
            _frame = null;
            _refresh.Clear();
            _reset.Clear();
            _undo.Clear();
        }

        private static void Remember<T>(ConfigEntry<T> cfg)
        {
            if (cfg == null) return;
            T old = cfg.Value;
            _undo.Add(() => cfg.Value = old);
        }

        private static void Open()
        {
            var prefab = VisualPrefabsHolder.Instance != null ? VisualPrefabsHolder.Instance.HotkeytsDialog : null;
            if (prefab == null) { Plugin.Log?.LogWarning("[settings] окно горячих клавиш не найдено."); return; }

            _win = UnityEngine.Object.Instantiate(prefab);
            var dlg = _win.GetComponent<HotkeysDialog>();
            if (dlg == null) { Plugin.Log?.LogWarning("[settings] в окне нет HotkeysDialog."); Close(); return; }

            dlg.ShowDialog(null, ECanvasType.ModalWindow);

            var caption = Field<Text>(dlg, "CaptionText");
            var okButton = Field<Button>(dlg, "OkButton");
            var resetButton = Field<Button>(dlg, "ResetToDefaultButton");
            var resetText = Field<Text>(dlg, "ResetToDefaultButtonText");
            var manager = Field<MonoBehaviour>(dlg, "widgetManager");
            if (manager == null) { Plugin.Log?.LogWarning("[settings] не нашёл список строк окна."); Close(); return; }

            var rowPrefab = Field<GameObject>(manager, "WidgetPrefab");
            var container = manager.transform;
            if (rowPrefab == null) { Plugin.Log?.LogWarning("[settings] не нашёл шаблон строки."); Close(); return; }

            manager.enabled = false;
            for (int i = container.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);

            if (caption != null) caption.text = "Настройки мода";

            Widen(dlg, 380f);
            Indent(container as RectTransform, 20f);

            if (okButton != null)
            {
                okButton.onClick.RemoveAllListeners();
                okButton.onClick.AddListener(() => Close(revert: false));
            }
            if (resetButton != null) resetButton.gameObject.SetActive(false);
            if (dlg.CloseButton != null)
            {
                dlg.CloseButton.onClick.RemoveAllListeners();
                dlg.CloseButton.onClick.AddListener(() => Close(revert: true));
            }

            _refresh.Clear();
            _reset.Clear();
            _undo.Clear();
            foreach (var row in Rows())
            {
                try { AddRow(rowPrefab, container, row); }
                catch (Exception e) { Plugin.Log?.LogError("[settings] строка «" + (row.Title ?? "?") + "»: " + e); }
            }

            var scroll = Field<ScrollRect>(manager, "ParentScrollRect");
            ScrollTop(scroll);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ScrollTopNextFrame(scroll));
        }

        private static void Indent(RectTransform list, float pad)
        {
            try
            {
                if (list == null) return;
                var layout = list.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.padding.left += Mathf.RoundToInt(pad);
                    Plugin.Trace("[settings] отступ списка слева: " + layout.padding.left);
                    return;
                }
                list.offsetMin = new Vector2(list.offsetMin.x + pad, list.offsetMin.y);
                Plugin.Trace("[settings] отступ списка сдвигом: " + list.offsetMin.x);
            }
            catch (Exception e) { Plugin.Log?.LogError("[settings] отступ списка: " + e.Message); }
        }

        private static RectTransform _frame;
        private static float _extra, _wantWidth;

        private static void Widen(HotkeysDialog dlg, float extra)
        {
            _frame = dlg.transform as RectTransform;
            _extra = extra;
            _wantWidth = 0f;
            Stretch();
        }

        private static void Stretch()
        {
            try
            {
                if (_frame == null) return;
                float now = _frame.rect.width;
                if (now < 50f) return;
                if (_wantWidth <= 0f) _wantWidth = now + _extra;

                float need = _wantWidth - now;
                if (Mathf.Abs(need) < 1f) return;

                var fitter = _frame.GetComponent<ContentSizeFitter>();
                if (fitter != null) fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                var element = _frame.GetComponent<LayoutElement>();
                if (element != null) { element.preferredWidth = _wantWidth; element.minWidth = _wantWidth; }

                Grow(_frame, need, self: true);
                foreach (RectTransform child in _frame) Grow(child, need, self: false);
                Plugin.Trace("[settings] ширина окна " + now + " → " + _frame.rect.width
                             + " (хотим " + _wantWidth + ")");
            }
            catch (Exception e) { Plugin.Log?.LogError("[settings] ширина окна: " + e.Message); }
        }

        private static void Grow(RectTransform rt, float extra, bool self)
        {
            bool stretched = Mathf.Abs(rt.anchorMax.x - rt.anchorMin.x) > 0.01f;
            if (stretched)
            {
                if (!self) return;
                rt.offsetMin = new Vector2(rt.offsetMin.x - extra * 0.5f, rt.offsetMin.y);
                rt.offsetMax = new Vector2(rt.offsetMax.x + extra * 0.5f, rt.offsetMax.y);
                return;
            }
            rt.sizeDelta = new Vector2(rt.sizeDelta.x + extra, rt.sizeDelta.y);
        }

        private static void AddRow(GameObject rowPrefab, Transform container, RowDef def)
        {
            var go = UnityEngine.Object.Instantiate(rowPrefab, container, worldPositionStays: false);
            go.name = "MvlRow";
            var widget = go.GetComponent<HotkeyWidget>();

            Text label = null, value = null;
            Image background = null;
            Button button = null;
            if (widget != null)
            {
                label = Field<Text>(widget, "LabelText");
                value = Field<Text>(widget, "KeyText");
                background = Field<Image>(widget, "KeyBackground");
                button = Field<Button>(widget, "Button");
                UnityEngine.Object.Destroy(widget);
            }
            if (label == null) label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = def.Title;
                if (!(def is Header))
                {
                    label.resizeTextForBestFit = true;
                    label.resizeTextMaxSize = label.fontSize;
                    label.resizeTextMinSize = 8;

                    label.alignment = TextAnchor.MiddleLeft;
                    var lrt = label.rectTransform;
                    const float pad = 34f;
                    if (Mathf.Abs(lrt.anchorMax.x - lrt.anchorMin.x) > 0.01f)
                    {
                        lrt.offsetMin = new Vector2(lrt.offsetMin.x + pad, lrt.offsetMin.y);
                        lrt.offsetMax = new Vector2(lrt.offsetMax.x + pad * 0.25f, lrt.offsetMax.y);
                    }
                    else
                    {
                        lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x + pad, lrt.anchoredPosition.y);
                    }
                    Plugin.Trace("[settings] подпись «" + def.Title + "» якоря "
                                 + lrt.anchorMin.x + ".." + lrt.anchorMax.x
                                 + " отступы " + lrt.offsetMin.x + ".." + lrt.offsetMax.x
                                 + " позиция " + lrt.anchoredPosition.x);
                }
            }

            if (def is Header)
            {
                if (background != null) background.gameObject.SetActive(false);
                else if (value != null) value.gameObject.SetActive(false);
                if (label != null)
                {
                    label.text = def.Title.ToUpperInvariant();
                    label.fontStyle = FontStyle.Bold;
                    label.fontSize = Mathf.RoundToInt(label.fontSize * 1.25f);
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = new Color(0.45f, 0.12f, 0.06f);
                    var rt = label.rectTransform;
                    rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
                    rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
                    rt.offsetMin = new Vector2(6f, rt.offsetMin.y);
                    rt.offsetMax = new Vector2(-6f, rt.offsetMax.y);
                }
                return;
            }

            if (def is BoolRow b)
            {
                Remember(b.Cfg);
                SetSwitch(value, b.Cfg.Value);
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        b.Cfg.Value = !b.Cfg.Value;
                        SetSwitch(value, b.Cfg.Value);
                    });
                }
                _refresh.Add(() => SetSwitch(value, b.Cfg.Value));
                _reset.Add(() => b.Cfg.Value = (bool)b.Cfg.DefaultValue);
                return;
            }

            if (def is PickRow pick)
            {
                Remember(pick.ByName);
                Remember(pick.ById);
                if (value != null) value.text = pick.Display();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    var val = value;
                    button.onClick.AddListener(() => FlaskPicker.Open(pick.ByName, pick.ById, pick.Title,
                        () => { if (val != null) val.text = pick.Display(); }));
                }
                _refresh.Add(() => { if (value != null) value.text = pick.Display(); });
                _reset.Add(() => { pick.ByName.Value = (string)pick.ByName.DefaultValue; pick.ById.Value = (int)pick.ById.DefaultValue; });
                return;
            }

            var row = (ValueRow)def;
            if (row.Remember != null) row.Remember();
            if (value != null) value.text = row.Get();
            _refresh.Add(() => { if (value != null) value.text = row.Get(); });
            if (row.Reset != null) _reset.Add(row.Reset);

            if (background == null || value == null) return;
            if (button != null) UnityEngine.Object.DestroyImmediate(button);

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = value;
            input.lineType = InputField.LineType.SingleLine;
            value.text = row.Get();
            input.SetTextWithoutNotify(row.Get());
            input.onEndEdit.AddListener(v =>
            {
                row.Set(v);
                string now = row.Get();
                input.SetTextWithoutNotify(now);
                value.text = now;
            });
        }

        internal static void Tick()
        {
            if (_win != null) Stretch();
        }

        private static void ScrollTop(ScrollRect scroll)
        {
            if (scroll == null) return;
            try
            {
                Canvas.ForceUpdateCanvases();
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
            }
            catch { }
        }

        private static System.Collections.IEnumerator ScrollTopNextFrame(ScrollRect scroll)
        {
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                if (scroll == null) yield break;
                var content = scroll.content;
                if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                ScrollTop(scroll);
            }
        }

        private static void SetSwitch(Text value, bool on)
        {
            if (value == null) return;
            value.text = on ? "ВКЛ" : "ВЫКЛ";
            value.color = on ? new Color(0.05f, 0.42f, 0.08f) : new Color(0.62f, 0.08f, 0.06f);
        }

        private static T Field<T>(object obj, string name) where T : class
        {
            try { return AccessTools.Field(obj.GetType(), name)?.GetValue(obj) as T; }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "Start")]
    public static class ModSettingsButtonPatch
    {
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                var reset = AccessTools.Field(typeof(SetupDialog), "ResetChatButton")?.GetValue(__instance) as Button;
                if (reset == null) { Plugin.Log?.LogWarning("[settings] кнопку «Сбросить положение чата» не нашёл."); return; }

                var src = (RectTransform)reset.transform;
                var go = UnityEngine.Object.Instantiate(reset.gameObject, src.parent);
                go.name = "MvlSettingsButton";
                var rt = (RectTransform)go.transform;
                rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
                rt.sizeDelta = src.sizeDelta;
                rt.localScale = src.localScale;
                rt.anchoredPosition = src.anchoredPosition - new Vector2(0f, src.rect.height + 10f);

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = "Настройки мода";

                var btn = go.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Settings.Toggle());
            }
            catch (Exception e) { Plugin.Log?.LogError("[settings] кнопка не добавлена: " + e.Message); }
        }
    }
}
