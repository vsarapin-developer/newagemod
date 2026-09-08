using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class PrivateToasts
    {
        private const float ToastW = 340f;
        private const float Fade = 0.6f;

        private sealed class Toast
        {
            internal GameObject Go;
            internal CanvasGroup Group;
            internal Image Bg;
            internal Shadow Sh;
            internal float Until;
        }

        private static object _on;
        private static Canvas _canvas;
        private static RectTransform _stack;
        private static readonly List<Toast> Live = new List<Toast>();
        private static string _lastKey = "";
        private static float _lastAt;
        private static bool _leftNow;

        internal static bool Enabled => Plugin.CfgPmToasts == null || Plugin.CfgPmToasts.Value;
        private static bool TeamEnabled => Plugin.CfgTeamToasts == null || Plugin.CfgTeamToasts.Value;

        private static int Seconds => Mathf.Clamp(Plugin.CfgPmToastSeconds != null ? Plugin.CfgPmToastSeconds.Value : 8, 2, 120);
        private static int Max => Mathf.Clamp(Plugin.CfgPmToastMax != null ? Plugin.CfgPmToastMax.Value : 5, 1, 10);
        private static bool Left => Plugin.CfgPmToastLeft != null && Plugin.CfgPmToastLeft.Value;
        private static float Opacity => Mathf.Clamp(Plugin.CfgPmToastOpacity != null ? Plugin.CfgPmToastOpacity.Value : 0.72f, 0.15f, 1f);

        internal static void Tick()
        {
            try
            {
                Listen();
                if (_stack != null && Left != _leftNow) PlaceStack();
                if (Live.Count == 0) return;
                float now = Time.unscaledTime;
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    var t = Live[i];
                    if (t.Go == null) { Live.RemoveAt(i); continue; }
                    float left = t.Until - now;
                    if (left <= 0f) { UnityEngine.Object.Destroy(t.Go); Live.RemoveAt(i); continue; }
                    if (t.Group != null) t.Group.alpha = left < Fade ? left / Fade : 1f;
                }
            }
            catch (Exception e) { Plugin.Trace("[личка] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(4, OnChat);
            nc.AddMessageListener(4, OnChat);
            _on = nc;
        }

        private static int MyId()
        {
            try { return DependencyContainer.GetContainer().Resolve<IUserData>().UserInfo.UserId; }
            catch { return 0; }
        }

        private static void OnChat(object m)
        {
            try
            {
                var msg = m as ChatResponseMessage;
                if (msg == null) return;
                bool team = msg.Type == 9;
                if (msg.Type != 2 && !team) return;
                if (string.IsNullOrEmpty(msg.Sender) || string.IsNullOrEmpty(msg.Text)) return;
                int me = MyId();
                bool mine = me > 0 && msg.SenderId == me;
                bool toMe = team ? !mine
                    : (msg.ReceiverId.HasValue ? (me <= 0 || msg.ReceiverId.Value == me) : (me <= 0 || !mine));
                Plugin.Trace("[личка] " + (team ? "команда" : "приват") + " от " + msg.Sender + " (" + msg.SenderId + ") кому " + (msg.ReceiverId.HasValue ? msg.ReceiverId.Value.ToString() : "?") + ", я " + me + (mine ? " — моё" : "") + (toMe || mine ? "" : " — мимо, пропуск"));
                if (!toMe) return;
                try { if (GameSettings.Instance != null && GameSettings.Instance.IsIgnored(msg.Sender)) return; } catch { }
                string key = msg.Type + "|" + msg.SenderId + "|" + msg.Text;
                if (key == _lastKey && Time.unscaledTime - _lastAt < 1.5f) return;
                _lastKey = key;
                _lastAt = Time.unscaledTime;
                bool named = me > 0 && msg.ReceiverId.HasValue && msg.ReceiverId.Value == me;
                bool mention = team && !mine && (named || Mentioned(msg.Text));
                string to = team ? (msg.Receiver ?? "").Trim() : "";
                if (team) Sounds.Team(); else Sounds.Pm();
                if (team ? TeamEnabled : Enabled) Show(msg.SenderId, msg.Sender, msg.Text, team, mention, to);
            }
            catch (Exception e) { Plugin.Trace("[личка] сообщение: " + e.Message); }
        }

        private static bool Mentioned(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string me = ChatHighlight.Login();
            return !string.IsNullOrEmpty(me) && me.Length >= 2
                   && text.IndexOf(me, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Mark(string text)
        {
            string me = ChatHighlight.Login();
            if (string.IsNullOrEmpty(me) || string.IsNullOrEmpty(text)) return text;
            var built = new System.Text.StringBuilder();
            int from = 0;
            while (true)
            {
                int at = text.IndexOf(me, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { built.Append(text, from, text.Length - from); break; }
                built.Append(text, from, at - from);
                built.Append("<color=#9bf08f><b>").Append(text, at, me.Length).Append("</b></color>");
                from = at + me.Length;
            }
            return built.ToString();
        }

        private static void Show(int senderId, string sender, string text, bool team, bool mention, string to)
        {
            Build();
            while (Live.Count >= Max)
            {
                var old = Live[0];
                Live.RemoveAt(0);
                if (old.Go != null) UnityEngine.Object.Destroy(old.Go);
            }
            if (text.Length > 220) text = text.Substring(0, 220) + "…";
            Image bg; Shadow sh;
            var go = BuildCard(_stack, sender, text, Opacity, team, out bg, out sh, mention, to);
            var toast = new Toast { Go = go, Group = go.GetComponent<CanvasGroup>(), Bg = bg, Sh = sh, Until = Time.unscaledTime + Seconds };
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                if (team) OpenTeam(); else Reply(senderId, sender);
                Live.Remove(toast);
                UnityEngine.Object.Destroy(go);
            });
            Live.Add(toast);
        }

        internal static GameObject BuildCard(Transform parent, string sender, string text, float opacity, bool team, out Image bg, out Shadow sh, bool mention = false, string to = null)
        {
            var go = new GameObject("QoLToast", typeof(RectTransform), typeof(Image), typeof(Shadow), typeof(Button), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            bg = go.GetComponent<Image>();
            bg.sprite = OnlineWindow.Rounded(12);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.06f, 0.05f, 0.04f, opacity);
            sh = go.GetComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.45f * opacity);
            sh.effectDistance = new Vector2(2f, -3f);
            var bar = new GameObject("accent", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 0.5f);
            brt.offsetMin = new Vector2(7f, 10f); brt.offsetMax = new Vector2(mention ? 13f : 11f, -10f);
            var bimg = bar.GetComponent<Image>();
            bimg.sprite = OnlineWindow.Rounded(4);
            bimg.type = Image.Type.Sliced;
            bimg.color = mention ? new Color32(120, 235, 110, 240)
                       : team ? new Color32(110, 180, 255, 230)
                       : new Color32(255, 200, 90, 230);
            bimg.raycastTarget = false;
            bar.AddComponent<LayoutElement>().ignoreLayout = true;
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 12, 8, 8);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = ToastW; le.minWidth = ToastW;

            string who = string.IsNullOrEmpty(to) ? sender : sender + " → " + to;
            string title = team ? who + "  ·  команда" + (mention ? "  ·  тебе" : "") : sender;
            var head = Label(go.transform, title, 15, FontStyle.Bold,
                             mention ? new Color32(155, 240, 143, 255)
                             : team ? new Color32(150, 200, 255, 255)
                             : new Color32(255, 214, 110, 255));
            head.alignment = TextAnchor.MiddleLeft;
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            var body = Label(go.transform, mention ? Mark(text) : text, 14, FontStyle.Normal, new Color32(245, 240, 228, 255));
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0f;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            return go;
        }

        internal static void ApplyOpacity(float opacity)
        {
            foreach (var t in Live) if (t.Go != null) SetOpacity(t.Bg, t.Sh, opacity);
        }

        internal static void SetOpacity(Image bg, Shadow sh, float opacity)
        {
            opacity = Mathf.Clamp(opacity, 0.15f, 1f);
            if (bg != null) bg.color = new Color(0.06f, 0.05f, 0.04f, opacity);
            if (sh != null) sh.effectColor = new Color(0f, 0f, 0f, 0.45f * opacity);
        }

        private static ChatWindowController OpenChat(EChatTab tab)
        {
            var cw = DependencyContainer.ResolveController<ChatWindowController>();
            if (cw == null) return null;
            if (!cw.IsWindowOpened) cw.Open(null);
            try { cw.ActiveTab = tab; }
            catch (Exception e) { Plugin.Trace("[личка] вкладка " + tab + ": " + e.Message); }
            return cw;
        }

        private static void OpenTeam()
        {
            try { OpenChat(EChatTab.TEAM); }
            catch (Exception e) { Plugin.Log?.LogWarning("[личка] открыть чат команды: " + e.Message); }
        }

        internal static void WritePrivate(int userId, string login)
        {
            try
            {
                var cw = OpenChat(EChatTab.PRIVATE);
                if (cw == null) return;
                cw.SetMessageRecipient(new UserContextMenuData(userId, login));
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[личка] написать игроку: " + e.Message); }
        }

        private static void Reply(int senderId, string sender)
        {
            try
            {
                var cw = OpenChat(EChatTab.PRIVATE);
                if (cw == null) return;
                cw.SetMessageRecipient(new UserContextMenuData(senderId, sender));
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[личка] открыть чат: " + e.Message); }
        }

        private static void Build()
        {
            if (_canvas != null && _stack != null) return;
            var go = new GameObject("QoLPrivateToasts", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 850;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var stackGo = new GameObject("stack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            stackGo.transform.SetParent(go.transform, false);
            _stack = (RectTransform)stackGo.transform;
            _stack.sizeDelta = new Vector2(ToastW, 10f);
            PlaceStack();
            var vlg = stackGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.LowerLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            var fit = stackGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static void PlaceStack()
        {
            if (_stack == null) return;
            _leftNow = Left;
            if (_leftNow)
            {
                _stack.anchorMin = _stack.anchorMax = new Vector2(0f, 0f);
                _stack.pivot = new Vector2(0f, 0f);
                _stack.anchoredPosition = new Vector2(16f, 178f);
            }
            else
            {
                _stack.anchorMin = _stack.anchorMax = new Vector2(1f, 0f);
                _stack.pivot = new Vector2(1f, 0f);
                _stack.anchoredPosition = new Vector2(-16f, 178f);
            }
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
            t.text = text;
            t.raycastTarget = false;
            return t;
        }
    }

}
