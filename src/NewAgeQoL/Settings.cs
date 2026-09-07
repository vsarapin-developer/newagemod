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

        private sealed class SliderRow : RowDef { internal ConfigEntry<float> Cfg; internal float Min; internal float Max; internal bool Preview; }

        private sealed class KeyRow : RowDef { internal ConfigEntry<string> Cfg; }

        private sealed class ActionRow : RowDef { internal string ButtonText; internal Action Do; }

        private sealed class PickRow : RowDef { internal ConfigEntry<string> ByName; internal ConfigEntry<int> ById; internal System.Func<string> Display; }

        private sealed class ValueRow : RowDef
        {
            internal Func<string> Get;
            internal Action<string> Set;
            internal Action Reset;
            internal Action Remember;
            internal bool Secret;
        }

        private static List<RowDef> Rows()
        {
            var rows = new List<RowDef>
            {
                new Header { Title = "Мод" },
                A("Что нового в версии " + Plugin.Version + (Updater.CanUpdate ? "   ·   вышла " + Updater.LatestVersion : ""), "Открыть", Changelog.Toggle),
                B("Проверять обновления мода", Plugin.CfgUpdateCheck),

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
                B("Подсказка по бойцу при наведении", Plugin.CfgFighterHint),
                B("Кнопка «Эффекты» в бою", Plugin.CfgEffectsButton),

                new Header { Title = "Карта" },
                B("Номера точек внешнего мира", Plugin.CfgMapLabels),
                B("Подписывать, что это за точка", Plugin.CfgMapLabelType),
                F("Размер номера точки", Plugin.CfgMapLabelSize),

                new Header { Title = "Инвентарь" },
                B("Иконка вещи у рецепта", Plugin.CfgRecipeIcons),
                B("Кнопки смены комплекта в снаряжении", Plugin.CfgSlotSwap),
                B("Вкладка «Контракты»", Plugin.CfgContractsTab),
                B("Номер на контракте и договоре", Plugin.CfgContractNumbers),
                B("Поиск в сумке", Plugin.CfgSearch),

                new Header { Title = "Рынок" },
                B("Несколько лотов на рынок за раз", Plugin.CfgMarketMultiLot),

                new Header { Title = "Кто в игре" },
                B("Кнопка «Кто в игре»", Plugin.CfgOnlineButton),
                S("Логин запасного аккаунта", Plugin.CfgOnlineLogin),
                S("Пароль запасного аккаунта", Plugin.CfgOnlinePassword, secret: true),
                K("Клавиша окна «Кто в игре»", Plugin.CfgOnlineHotkey),

                new Header { Title = "Чат" },
                B("Свои строки в логе зелёным", Plugin.CfgChatHighlight),
                B("Прятать окно «Системное сообщение»", Plugin.CfgHideSystemBoxes),
                B("Всплывающие личные сообщения", Plugin.CfgPmToasts),
                B("Всплывающие сообщения командного чата", Plugin.CfgTeamToasts),
                I("Секунд показа личного сообщения", Plugin.CfgPmToastSeconds, 2, 120),
                I("Сколько сообщений в стопке (1–10)", Plugin.CfgPmToastMax, 1, 10),
                B("Личные сообщения слева (иначе справа)", Plugin.CfgPmToastLeft),
                Sl("Непрозрачность карточек", Plugin.CfgPmToastOpacity, 0.15f, 1f, preview: true),

                new Header { Title = "Звуки" },
                B("Звуки как во Flash", Plugin.CfgSounds),
                B("Личное сообщение", Plugin.CfgSoundPm),
                B("Командный чат", Plugin.CfgSoundTeam),
                B("Начало боя", Plugin.CfgSoundFight),
                B("Конец боевой фазы", Plugin.CfgSoundRound),
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

        private static RowDef Sl(string title, ConfigEntry<float> cfg, float min, float max, bool preview = false) =>
            cfg == null ? null : new SliderRow { Title = title, Cfg = cfg, Min = min, Max = max, Preview = preview };

        private static RowDef S(string title, ConfigEntry<string> cfg, bool secret = false) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value ?? "",
                Set = v => cfg.Value = (v ?? "").Trim(),
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (string)cfg.DefaultValue,
                Secret = secret,
            };

        private static RowDef A(string title, string buttonText, Action act) =>
            new ActionRow { Title = title, ButtonText = buttonText, Do = act };

        private static RowDef K(string title, ConfigEntry<string> cfg) =>
            cfg == null ? null : new KeyRow { Title = title, Cfg = cfg };

        private static RowDef I(string title, ConfigEntry<int> cfg, int min = int.MinValue, int max = int.MaxValue) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value.ToString(),
                Set = v => { if (int.TryParse(v.Trim(), out int n)) cfg.Value = Mathf.Clamp(n, min, max); },
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
            if (Capturing) { _capCfg = null; _capBg = null; _capText = null; _capBtn = null; _capMsgUntil = 0f; }
            Changelog.Close();
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
            Clones.StripHotkeys(_win, prefab);
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

            if (def is SliderRow sl)
            {
                Remember(sl.Cfg);
                if (background == null || value == null) return;
                if (button != null) UnityEngine.Object.DestroyImmediate(button);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var slider = MakeSlider(background, sl.Min, sl.Max, sl.Cfg.Value);
                value.gameObject.SetActive(false);
                Image pbg = null; Shadow psh = null;
                if (sl.Preview)
                {
                    try { PreviewRow(rowPrefab, container, go.transform.GetSiblingIndex() + 1, def.Title, sl.Cfg.Value, out pbg, out psh); }
                    catch (Exception e) { Plugin.Trace("[settings] превью карточки: " + e.Message); }
                }
                if (slider != null)
                {
                    slider.onValueChanged.AddListener(v =>
                    {
                        sl.Cfg.Value = v;
                        Plugin.Trace("[settings] " + def.Title + " = " + v.ToString("0.00", ci));
                        if (pbg != null) PrivateToasts.SetOpacity(pbg, psh, v);
                        if (sl.Preview) PrivateToasts.ApplyOpacity(v);
                    });
                    _refresh.Add(() => { slider.SetValueWithoutNotify(sl.Cfg.Value); if (pbg != null) PrivateToasts.SetOpacity(pbg, psh, sl.Cfg.Value); });
                }
                _reset.Add(() => sl.Cfg.Value = (float)sl.Cfg.DefaultValue);
                return;
            }

            if (def is ActionRow act)
            {
                if (value == null) return;
                value.text = act.ButtonText;
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => act.Do());
                }
                return;
            }

            if (def is KeyRow kr)
            {
                Remember(kr.Cfg);
                if (background == null || value == null) return;
                value.text = KeyName(kr.Cfg.Value);
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                value.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => StartCapture(kr.Cfg, background, value, button));
                }
                _refresh.Add(() => { if (!Capturing && value != null) value.text = KeyName(kr.Cfg.Value); });
                _reset.Add(() => kr.Cfg.Value = (string)kr.Cfg.DefaultValue);
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
            Func<string> shown = () => row.Secret ? new string('•', row.Get().Length) : row.Get();
            if (value != null) value.text = shown();
            _refresh.Add(() => { if (value != null) value.text = shown(); });
            if (row.Reset != null) _reset.Add(row.Reset);

            if (background == null || value == null) return;
            if (button != null) UnityEngine.Object.DestroyImmediate(button);

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = value;
            input.lineType = InputField.LineType.SingleLine;
            if (row.Secret) input.contentType = InputField.ContentType.Password;
            value.text = shown();
            input.SetTextWithoutNotify(row.Get());
            input.onEndEdit.AddListener(v =>
            {
                row.Set(v);
                string now = row.Get();
                input.SetTextWithoutNotify(now);
                value.text = shown();
            });
        }

        private static void PreviewRow(GameObject rowPrefab, Transform container, int index, string title, float opacity, out Image pbg, out Shadow psh)
        {
            pbg = null; psh = null;
            var go = UnityEngine.Object.Instantiate(rowPrefab, container, worldPositionStays: false);
            go.name = "MvlPreview";
            go.transform.SetSiblingIndex(index);
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
            if (button != null) UnityEngine.Object.DestroyImmediate(button);
            if (label != null)
            {
                label.text = title;
                label.color = new Color(0f, 0f, 0f, 0f);
                label.raycastTarget = false;
            }
            if (value != null) value.gameObject.SetActive(false);
            if (background == null) { UnityEngine.Object.Destroy(go); return; }
            background.enabled = false;
            background.raycastTarget = false;
            foreach (var g in background.GetComponentsInChildren<Graphic>(true)) if (g != background) g.enabled = false;

            const float rowH = 88f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, rowH);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = rowH; le.preferredHeight = rowH;
            var hl = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (hl != null)
            {
                int col = (int)hl.childAlignment % 3;
                hl.childAlignment = (TextAnchor)(3 + col);
            }
            else
            {
                var brt = background.rectTransform;
                float h = brt.rect.height;
                brt.anchorMin = new Vector2(brt.anchorMin.x, 0.5f);
                brt.anchorMax = new Vector2(brt.anchorMax.x, 0.5f);
                brt.pivot = new Vector2(brt.pivot.x, 0.5f);
                brt.sizeDelta = new Vector2(brt.sizeDelta.x, h);
                brt.anchoredPosition = new Vector2(brt.anchoredPosition.x, 0f);
            }

            var card = PrivateToasts.BuildCard(background.transform, "Никнейм", "текст сообщения", opacity, false, out pbg, out psh);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = new Vector2(0f, 0.5f);
            crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = new Vector2(-120f, 0f);
            crt.offsetMax = new Vector2(0f, 0f);
            var cle = card.GetComponent<LayoutElement>();
            cle.minWidth = 0f; cle.preferredWidth = 0f;
            var cb = card.GetComponent<Button>();
            if (cb != null) cb.interactable = false;
            var cg = card.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = false;
        }

        private static Slider MakeSlider(Image background, float min, float max, float current)
        {
            Slider slider = null;
            try
            {
                Slider proto = null;
                foreach (var one in Resources.FindObjectsOfTypeAll<Slider>())
                {
                    if (one == null || !one.gameObject.scene.IsValid()) continue;
                    if (_win != null && one.transform.IsChildOf(_win.transform)) continue;
                    if (one.direction != Slider.Direction.LeftToRight) continue;
                    proto = one;
                    break;
                }
                if (proto != null)
                {
                    var go = UnityEngine.Object.Instantiate(proto.gameObject, background.transform, false);
                    Clones.StripHotkeys(go, proto.gameObject);
                    go.name = "MvlSlider";
                    foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (c == null || c is Slider) continue;
                        string ns = c.GetType().Namespace ?? "";
                        if (!ns.StartsWith("UnityEngine")) UnityEngine.Object.Destroy(c);
                    }
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.offsetMin = new Vector2(10f, 6f); rt.offsetMax = new Vector2(-10f, -6f);
                    rt.localScale = Vector3.one;
                    slider = go.GetComponent<Slider>();
                    bool hit = false;
                    foreach (var g in go.GetComponentsInChildren<Graphic>(true)) hit |= g.raycastTarget;
                    if (!hit) foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = true;
                    slider.onValueChanged = new Slider.SliderEvent();
                    slider.wholeNumbers = false;
                    slider.minValue = min; slider.maxValue = max;
                    slider.SetValueWithoutNotify(current);
                    slider.interactable = true;
                    go.SetActive(true);
                    return slider;
                }
            }
            catch (Exception e) { Plugin.Trace("[settings] ползунок игры не взялся: " + e.Message); }

            var sgo = new GameObject("MvlSlider", typeof(RectTransform), typeof(Slider));
            sgo.transform.SetParent(background.transform, false);
            var srt = (RectTransform)sgo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(10f, 8f); srt.offsetMax = new Vector2(-10f, -8f);
            var track = new GameObject("track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(sgo.transform, false);
            var trt = (RectTransform)track.transform;
            trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(0.5f, 0.5f);
            trt.offsetMin = new Vector2(0f, -3f); trt.offsetMax = new Vector2(0f, 3f);
            track.GetComponent<Image>().color = new Color(0.25f, 0.17f, 0.08f, 0.9f);
            var fillArea = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fillArea.transform.SetParent(track.transform, false);
            var frt = (RectTransform)fillArea.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fillArea.GetComponent<Image>().color = new Color(0.85f, 0.62f, 0.2f, 1f);
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sgo.transform, false);
            var hrt = (RectTransform)handle.transform;
            hrt.sizeDelta = new Vector2(14f, 0f);
            hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(0f, 1f);
            handle.GetComponent<Image>().color = new Color(0.98f, 0.9f, 0.7f, 1f);
            slider = sgo.GetComponent<Slider>();
            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min; slider.maxValue = max;
            slider.SetValueWithoutNotify(current);
            return slider;
        }

        private static ConfigEntry<string> _capCfg;
        private static Text _capText;
        private static Image _capBg;
        private static Button _capBtn;
        private static Color _capTextColor;
        private static Color _capBgColor;
        private static float _capMsgUntil;

        internal static bool Capturing => _capCfg != null;

        private static string KeyName(string spec)
        {
            spec = (spec ?? "").Trim();
            return spec.Length > 0 ? spec : "нет";
        }

        private static void StartCapture(ConfigEntry<string> cfg, Image bg, Text text, Button btn)
        {
            if (Capturing) return;
            _capCfg = cfg; _capBg = bg; _capText = text; _capBtn = btn;
            _capTextColor = text.color; _capBgColor = bg.color;
            _capMsgUntil = 0f;
            bg.color = Color.black;
            text.color = new Color32(255, 216, 134, 255);
            text.text = "нажмите клавишу";
            if (btn != null) btn.interactable = false;
        }

        private static void StopCapture()
        {
            try
            {
                if (_capBg != null) _capBg.color = _capBgColor;
                if (_capText != null)
                {
                    _capText.color = _capTextColor;
                    _capText.text = KeyName(_capCfg != null ? _capCfg.Value : "");
                }
                if (_capBtn != null) _capBtn.interactable = true;
            }
            catch { }
            _capCfg = null; _capBg = null; _capText = null; _capBtn = null; _capMsgUntil = 0f;
        }

        private static string TakenBy(KeyCode key)
        {
            try
            {
                var dispatcher = HotkeyDispatcher.Instance;
                var entries = dispatcher != null ? dispatcher.HotkeyEntries : null;
                if (entries == null) return null;
                foreach (var entry in entries)
                {
                    if (entry == null || entry.KeyCode != key) continue;
                    string label = "hotkey.action." + (int)entry.ActionType;
                    string name = null;
                    try { name = ResourceStrings.GetString(label); } catch { }
                    if (string.IsNullOrEmpty(name) || name == label) name = entry.ActionType.ToString();
                    return name;
                }
            }
            catch (Exception e) { Plugin.Trace("[settings] клавиши игры: " + e.Message); }
            return null;
        }

        private static void CaptureTick()
        {
            if (!Capturing) return;
            if (_capMsgUntil > 0f)
            {
                if (Time.unscaledTime < _capMsgUntil) return;
                StopCapture();
                return;
            }
            if (!Input.anyKeyDown) return;
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
                if (!Input.GetKeyDown(key)) continue;
                if (key == KeyCode.Escape) { StopCapture(); return; }
                if (key == KeyCode.Delete || key == KeyCode.Backspace)
                {
                    _capCfg.Value = "";
                    StopCapture();
                    return;
                }
                string busy = TakenBy(key);
                if (busy != null)
                {
                    if (_capText != null)
                    {
                        _capText.color = new Color32(255, 120, 100, 255);
                        _capText.text = "занято: " + busy;
                    }
                    _capMsgUntil = Time.unscaledTime + 2f;
                    return;
                }
                _capCfg.Value = key.ToString();
                StopCapture();
                return;
            }
        }

        internal static void Tick()
        {
            CaptureTick();
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

    [HarmonyPatch(typeof(HotkeyDispatcher), "Update")]
    public static class SettingsKeyCapturePatch
    {
        private static bool Prefix()
        {
            if (Settings.Capturing) return false;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (Changelog.EscapeClose()) return false;
                    if (ModalDialogList.IsEmpty())
                    {
                        if (OnlineWindow.EscapeClose()) return false;
                        if (EffectsWindow.EscapeClose()) return false;
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[settings] escape: " + e.Message); }
            return true;
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
                Clones.StripHotkeys(go, reset.gameObject);
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
