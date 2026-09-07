using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Changelog
    {
        private const float PanelW = 640f;
        private const float PanelH = 560f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Text _state;
        private static Button _action;
        private static Text _actionText;
        private static string _shownState = "";

        private sealed class Entry
        {
            internal string Version;
            internal string[] Lines;
        }

        private static readonly Entry[] Entries =
        {
            new Entry
            {
                Version = "1.7.9",
                Lines = new[]
                {
                    "Мод стал тише в журнале: убраны отладочные записи, которые нужны были только при доработке. В обычной работе он пишет одну строку при загрузке и только настоящие ошибки, если что-то пошло не так.",
                    "Подробный режим по-прежнему включается галкой в файле настроек, разделом Log. Он нужен, только если просят прислать журнал.",
                },
            },
            new Entry
            {
                Version = "1.7.8",
                Lines = new[]
                {
                    "Подсказку по бойцу стало легко поймать мышкой. Раньше боец искался строго лучом из курсора, а капсулы у моделей узкие, у лежачих ещё и втрое ниже. Теперь поиск идёт в три захода: точный луч, толстый луч и ближайший к курсору боец на экране.",
                    "Подсказка больше не едет за мышкой: встаёт один раз и стоит на месте, пока наведён тот же боец.",
                },
            },
            new Entry
            {
                Version = "1.7.7",
                Lines = new[]
                {
                    "Подсказка по бойцу в бою: наведи мышку на бойца или монстра, через мгновение появится окошко с уровнем, рейтингом, полосами и списком эффектов с тем, кто их наложил и на сколько ходов.",
                    "Кнопка «Эффекты» в бою рядом с боевыми иконками. Показывает эффекты выделенного бойца, своего или чужого, монстра тоже. Гаснет вместе с остальными боевыми кнопками, когда ход занят.",
                    "Личные и командные сообщения всплывают карточкой в углу экрана. Нажатие на личное открывает чат на вкладке «Приватно» с подставленным ником, на командное открывает вкладку команды.",
                    "У карточек настраиваются сторона экрана, время показа, сколько держать в стопке и прозрачность. Прозрачность выбирается ползунком, рядом живой пример карточки.",
                    "Звуки как в старом клиенте: личное сообщение, начало боя, конец боевой фазы. Личный звук играет и когда пишут тебе, и когда пишешь ты. Родные звуки лички и смены раунда при этом глушатся, чтобы не звучало дважды. Громкость берётся из настроек игры.",
                    "Окно «Кто в игре» можно двигать за шапку и тянуть за нижний край, положение запоминается. Окно открывается горячей клавишей, работает в бою и переживает смену локации.",
                    "Горячая клавиша задаётся нажатием прямо в настройках мода. Клавишу, занятую в настройках игры, назначить нельзя.",
                    "Пока идёт запрос списка игроков, окно честно пишет «обновляю…» и приглушает старые строки.",
                    "Окна мода больше не глушат горячие клавиши игры, в том числе боевые.",
                    "Обход зависания анимации в бою: если боец не возвращается в покой дольше двух с половиной секунд, ход продолжается.",
                    "Это самое окно: мод сам проверяет, вышла ли новая версия, и умеет обновиться одной кнопкой. Настройки при этом остаются, они хранятся в отдельном файле.",
                },
            },
            new Entry
            {
                Version = "1.7.6",
                Lines = new[]
                {
                    "В списке игроков локации больше не двоятся ники.",
                    "Список «Кто в игре» перестал показывать пустые строки.",
                },
            },
            new Entry
            {
                Version = "1.7.0 – 1.7.5",
                Lines = new[]
                {
                    "Окно «Кто в игре»: список всех игроков онлайн со значками класса, клана, вип и прав.",
                    "Клик по строке открывает переписку, правая кнопка открывает меню игрока как в чате.",
                    "На экране выбора персонажа сразу выбран тот, кем заходил в прошлый раз.",
                },
            },
            new Entry
            {
                Version = "1.5.0 – 1.6.2",
                Lines = new[]
                {
                    "В цене лота запятая сама превращается в точку.",
                    "В списке лотов рядом с ценой видно цену за одну штуку, только для золота.",
                },
            },
            new Entry
            {
                Version = "1.3.0 – 1.4.4",
                Lines = new[]
                {
                    "На рынок можно выставить несколько лотов за раз и снять сразу несколько.",
                    "Сообщения о выставлении и снятии лотов показываются по центру экрана.",
                    "Банки выбираются картинками в настройках, без ввода номеров предметов.",
                },
            },
        };

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            try { Build(); }
            catch (Exception e) { Plugin.Log?.LogError("[что нового] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.MessageBox, _canvasGo);
            }
            catch { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); }
            _canvasGo = null;
            _panelGo = null;
            _state = null;
            _action = null;
            _actionText = null;
            _shownState = "";
        }

        internal static void Tick()
        {
            if (_state == null) return;
            string now = Updater.State + "|" + Updater.Message;
            if (now == _shownState) return;
            _shownState = now;
            bool done = Updater.State == Updater.Stage.Done;
            _state.text = done ? Updater.Message : "Установлена версия " + Plugin.Version + " · " + Updater.Message;
            _state.color = done ? new Color32(150, 230, 140, 255) : new Color32(226, 212, 180, 255);
            if (_action == null || _actionText == null) return;
            bool offer = Updater.CanUpdate;
            bool busy = Updater.State == Updater.Stage.Checking || Updater.State == Updater.Stage.Downloading;
            _action.gameObject.SetActive(!done);
            _action.interactable = !busy;
            _actionText.text = offer ? "Обновить" : "Проверить";
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.MessageBox);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLChangelog", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            var area = canvas.transform as RectTransform;
            if (area != null) prt.position = area.TransformPoint(area.rect.center);
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = new Color(0.14f, 0.10f, 0.07f, 0.98f);
            pimg.sprite = OnlineWindow.Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.42f, 0.22f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), Vector2.zero);
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;

            var title = OnlineWindow.Label(_panelGo.transform, "Что нового в моде", 24, FontStyle.Bold, new Color32(255, 224, 130, 255));
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(18f, -50f), new Vector2(-70f, -8f));
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;

            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -104f), new Vector2(-16f, -58f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _state = OnlineWindow.Label(barGo.transform, "Установлена версия " + Plugin.Version, 15, FontStyle.Normal, new Color32(226, 212, 180, 255));
            _state.alignment = TextAnchor.MiddleLeft;
            _state.horizontalOverflow = HorizontalWrapMode.Wrap;
            _state.verticalOverflow = VerticalWrapMode.Truncate;
            _state.resizeTextForBestFit = true;
            _state.resizeTextMinSize = 10;
            _state.resizeTextMaxSize = 15;
            var stateLe = _state.gameObject.AddComponent<LayoutElement>();
            stateLe.flexibleWidth = 1f;
            stateLe.minWidth = 120f;

            _action = MakeButton(barGo.transform, "Проверить", 132f, () =>
            {
                if (Updater.CanUpdate) Updater.Update(); else Updater.Check();
            });
            _actionText = _action.GetComponentInChildren<Text>(true);
            _shownState = "";
            if (Updater.State == Updater.Stage.Idle) Updater.Check();
            Tick();

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 16f), new Vector2(-26f, -110f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.25f);
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = new Vector2(0f, 0f); cont.offsetMax = new Vector2(0f, 0f);
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 16);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cont;
            scroll.viewport = srt;

            bool first = true;
            foreach (var entry in Entries)
            {
                var head = OnlineWindow.Label(contentGo.transform, first ? "Версия " + entry.Version + " · стоит у тебя" : "Версия " + entry.Version,
                                              19, FontStyle.Bold, new Color32(255, 214, 110, 255));
                head.alignment = TextAnchor.MiddleLeft;
                head.horizontalOverflow = HorizontalWrapMode.Wrap;
                var hle = head.gameObject.AddComponent<LayoutElement>();
                hle.minHeight = 30f;
                hle.preferredHeight = 30f;
                if (!first) hle.preferredHeight = 34f;
                first = false;

                foreach (var line in entry.Lines)
                {
                    var text = OnlineWindow.Label(contentGo.transform, "•  " + line, 15, FontStyle.Normal, new Color32(238, 230, 210, 255));
                    text.alignment = TextAnchor.UpperLeft;
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    var tle = text.gameObject.AddComponent<LayoutElement>();
                    tle.flexibleHeight = 0f;
                    tle.minHeight = 22f;
                }

                var gap = new GameObject("gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(contentGo.transform, false);
                gap.GetComponent<LayoutElement>().minHeight = 10f;
            }

            MakeScrollbar(_panelGo.transform, scroll);
            scroll.verticalNormalizedPosition = 1f;
        }

        private static Button MakeButton(Transform host, string text, float width, Action onClick)
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
            catch (Exception e) { Plugin.Trace("[что нового] спрайт кнопки: " + e.Message); }

            var go = new GameObject("QoLUpdateButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
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
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = 42f; le.minHeight = 42f;

            var label = OnlineWindow.Label(go.transform, text, 16, FontStyle.Bold, Color.white);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, 9f), new Vector2(-14f, -9f));
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 16;
            if (proto != null)
            {
                if (proto.font != null) label.font = proto.font;
                label.color = proto.color;
                label.fontStyle = proto.fontStyle;
                var outline = proto.GetComponent<Outline>();
                if (outline != null)
                {
                    var own = label.gameObject.AddComponent<Outline>();
                    own.effectColor = outline.effectColor;
                    own.effectDistance = outline.effectDistance;
                }
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private static void MakeScrollbar(Transform host, ScrollRect scroll)
        {
            var go = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            OnlineWindow.Place(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-22f, 16f), new Vector2(-10f, -110f));
            var track = go.GetComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.35f);
            track.sprite = OnlineWindow.Rounded(6);
            track.type = Image.Type.Sliced;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            OnlineWindow.Place((RectTransform)handle.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var himg = handle.GetComponent<Image>();
            himg.color = new Color(0.62f, 0.5f, 0.3f, 0.9f);
            himg.sprite = OnlineWindow.Rounded(6);
            himg.type = Image.Type.Sliced;

            var bar = go.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = (RectTransform)handle.transform;
            bar.targetGraphic = himg;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }
    }
}
