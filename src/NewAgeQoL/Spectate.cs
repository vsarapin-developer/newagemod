using System;
using System.Collections.Generic;
using Transport.Messages.Responses.Combat;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Spectate
    {
        private const float Keep = 3f * 3600f;
        private static readonly Color Cross = new Color(0.62f, 0.09f, 0.09f, 0.8f);

        private sealed class Fight
        {
            internal int Id;
            internal string Desc;
            internal int MinLevel, MaxLevel, Count;
            internal int Room;
            internal float Seen;
        }

        private static readonly Dictionary<int, Fight> Live = new Dictionary<int, Fight>();
        private static readonly Dictionary<int, GameObject> Rows = new Dictionary<int, GameObject>();
        private static readonly HashSet<int> Fresh = new HashSet<int>();
        private static object _on;
        private static float _listenAt, _viewAt;
        private static int _children;
        private static BaseEnterfightView _view;
        private static int _watched;
        private static int _backTo;
        private static float _leaveAt;
        private static float _watchedAt;
        private static bool _inFight;
        private static int _map = -1;
        private static float _syncUntil;
        private static bool _syncing;

        internal static bool Enabled => Plugin.CfgSpectate == null || Plugin.CfgSpectate.Value;

        internal static void Tick()
        {
            try
            {
                if (Time.unscaledTime >= _listenAt)
                {
                    _listenAt = Time.unscaledTime + 1f;
                    Listen();
                }
                if (Time.unscaledTime < _viewAt) return;
                _viewAt = Time.unscaledTime + 0.5f;
                Watching();
                Leave();
                Sync();
                Prune();
                Show();
            }
            catch (Exception e) { Plugin.Trace("[бои] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(161, OnCombat);
            nc.AddMessageListener(161, OnCombat);
            nc.RemoveMessageListener(351, OnResult);
            nc.AddMessageListener(351, OnResult);
            _on = nc;
        }

        private static void OnCombat(object message)
        {
            var info = message as CombatInfoResponseMessage;
            if (info == null || info.Id <= 0) return;
            bool over = string.IsNullOrEmpty(info.Desc) && !info.FighterCount.HasValue
                        && !info.MinLevel.HasValue && !info.MaxLevel.HasValue;
            if (over)
            {
                if (Live.Remove(info.Id)) Plugin.Trace("[бои] бой " + info.Id + " закончился, строка снята");
                Drop(info.Id);
                return;
            }
            Fight fight;
            if (!Live.TryGetValue(info.Id, out fight))
            {
                fight = new Fight { Id = info.Id };
                Live[info.Id] = fight;
            }
            fight.Desc = string.IsNullOrEmpty(info.Desc) ? "бой идёт" : info.Desc;
            fight.MinLevel = info.MinLevel ?? 0;
            fight.MaxLevel = info.MaxLevel ?? 0;
            fight.Count = info.FighterCount ?? 0;
            fight.Seen = Time.unscaledTime;
            fight.Room = Map();
            if (_syncing) Fresh.Add(info.Id);
            Plugin.Trace("[бои] идёт бой " + info.Id + " «" + fight.Desc + "»");
        }

        private static void Watching()
        {
            bool inFight = SideButtons.InCombat();
            if (inFight) { _inFight = true; return; }
            if (_inFight) { _inFight = false; _watched = 0; return; }
            if (_watched <= 0 || Time.unscaledTime - _watchedAt < 15f) return;
            int gone = _watched;
            _watched = 0;
            if (Live.Remove(gone)) Plugin.Trace("[бои] бой " + gone + " не открылся, строка снята");
            Drop(gone);
        }

        private static void OnResult(object message)
        {
            if (_watched <= 0) return;
            int id = _watched;
            _watched = 0;
            if (Live.Remove(id)) Plugin.Trace("[бои] бой " + id + " закончился при мне, строка снята");
            Drop(id);
            if (_backTo > 0) _leaveAt = Time.unscaledTime + 2.5f;
        }

        private static void Leave()
        {
            if (_leaveAt <= 0f || Time.unscaledTime < _leaveAt) return;
            _leaveAt = 0f;
            if (_backTo <= 0 || !SideButtons.InCombat()) { _backTo = 0; return; }
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.SendRequest(new ChangeMapRequest(_backTo));
                Plugin.Trace("[бои] бой досмотрен, возвращаюсь на карту " + _backTo);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[бои] выход из просмотра: " + e.Message); }
            _backTo = 0;
        }

        private static void Sync()
        {
            int map = Map();
            if (map != _map)
            {
                _map = map;
                _syncing = true;
                _syncUntil = Time.unscaledTime + 6f;
                Fresh.Clear();
                return;
            }
            if (!_syncing || Time.unscaledTime < _syncUntil) return;
            _syncing = false;
            if (Fresh.Count == 0) { Fresh.Clear(); return; }

            var gone = new List<int>();
            foreach (var pair in Live)
                if (!Fresh.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (int id in gone)
            {
                Live.Remove(id);
                Drop(id);
            }
            Plugin.Trace("[бои] при входе сервер прислал боёв: " + Fresh.Count + ", снято старых строк: " + gone.Count);
            Fresh.Clear();
        }

        private static int Map()
        {
            try
            {
                var ud = DependencyContainer.GetContainer()?.Resolve<IUserData>();
                return ud == null ? -1 : ud.MapId;
            }
            catch { return -1; }
        }

        private static void Prune()
        {
            if (Live.Count == 0) return;
            var stale = new List<int>();
            foreach (var pair in Live)
                if (Time.unscaledTime - pair.Value.Seen > Keep) stale.Add(pair.Key);
            foreach (int id in stale)
            {
                Live.Remove(id);
                Drop(id);
            }
        }

        private static void Drop(int id)
        {
            GameObject row;
            if (!Rows.TryGetValue(id, out row)) return;
            Rows.Remove(id);
            if (row != null) UnityEngine.Object.Destroy(row);
        }

        private static void Show()
        {
            var view = _view;
            if (view == null)
            {
                view = UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
                if (view == null)
                {
                    if (Rows.Count > 0) Rows.Clear();
                    _view = null;
                    return;
                }
                _view = view;
                Rows.Clear();
                _children = 0;
            }
            if (!Enabled) return;

            int room = Map();
            bool added = false;
            foreach (var pair in Live)
            {
                GameObject row;
                bool here = pair.Value.Room == 0 || pair.Value.Room == room;
                if (!here) { if (Rows.TryGetValue(pair.Key, out row)) Drop(pair.Key); continue; }
                if (Rows.TryGetValue(pair.Key, out row) && row != null) continue;
                var made = Make(view, pair.Value);
                if (made == null) continue;
                Rows[pair.Key] = made;
                added = true;
            }

            foreach (var row in Rows.Values) Fit(row);

            int children = 0;
            foreach (var row in Rows.Values)
                if (row != null && row.transform.parent != null) { children = row.transform.parent.childCount; break; }
            if (added || children != _children)
            {
                _children = children;
                foreach (var row in Rows.Values)
                    if (row != null) row.transform.SetAsLastSibling();
            }
        }

        private static GameObject Make(BaseEnterfightView view, Fight fight)
        {
            var announce = new FightAnnounce
            {
                Id = fight.Id,
                Type = 1,
                MaxCount = fight.Count,
                LeaderLogin = fight.Desc,
                MinLevel = fight.MinLevel,
                MaxLevel = fight.MaxLevel,
                Timeout = 600000,
                RoundTimeout = 0,
                ClaimType = 1,
            };
            var row = view.Add(announce);
            if (row == null) return null;

            var timer = row.GetComponent<CountdownTimer>();
            if (timer != null) timer.enabled = false;

            Mark(row);

            var button = Unity3DHelper.FindInChild(row, "EnterButton");
            var enter = button != null ? button.GetComponent<Button>() : null;
            if (enter != null)
            {
                int id = fight.Id;
                enter.onClick.RemoveAllListeners();
                enter.onClick.AddListener(() => Watch(id));
            }
            Plugin.Trace("[бои] строка идущего боя " + fight.Id + " добавлена в список заявок");
            return row;
        }

        private static RectTransform Card(GameObject row)
        {
            RectTransform card = null;
            float best = 0f;
            foreach (var image in row.GetComponentsInChildren<Image>(true))
            {
                if (image == null || !image.enabled || !image.gameObject.activeInHierarchy) continue;
                var rt = image.rectTransform;
                float area = rt.rect.width * rt.rect.height;
                if (area <= best) continue;
                best = area;
                card = rt;
            }
            return card ?? (RectTransform)row.transform;
        }

        private static void Mark(GameObject row)
        {
            var markGo = new GameObject("QoLStarted", typeof(RectTransform), typeof(CanvasGroup), typeof(RectMask2D));
            markGo.transform.SetParent(Card(row), false);
            var mrt = (RectTransform)markGo.transform;
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;
            var group = markGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            markGo.GetComponent<RectMask2D>().padding = new Vector4(1f, 1f, 1f, 1f);
            Bar(mrt);
            Bar(mrt);
            markGo.transform.SetAsLastSibling();
            group.alpha = 0f;
            Fit(row);
        }

        private static void Bar(RectTransform host)
        {
            var go = new GameObject("bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var image = go.GetComponent<Image>();
            image.color = Cross;
            image.raycastTarget = false;
        }

        private static void Fit(GameObject row)
        {
            if (row == null) return;
            RectTransform mark = null;
            foreach (var candidate in row.GetComponentsInChildren<RectTransform>(true))
                if (candidate != null && candidate.name == "QoLStarted") { mark = candidate; break; }
            if (mark == null || mark.childCount < 2) return;
            var box = mark.parent as RectTransform;
            if (box == null) return;
            float width = box.rect.width, height = box.rect.height;
            var veil = mark.GetComponent<CanvasGroup>();
            if (width < 4f || height < 4f)
            {
                if (veil != null) veil.alpha = 0f;
                return;
            }
            float length = Mathf.Sqrt(width * width + height * height);
            float angle = Mathf.Atan2(height, width) * Mathf.Rad2Deg;
            for (int i = 0; i < 2; i++)
            {
                var bar = mark.GetChild(i) as RectTransform;
                if (bar == null) continue;
                bar.sizeDelta = new Vector2(length, 11f);
                bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? angle : -angle);
            }
            if (veil != null && veil.alpha < 1f) veil.alpha = 1f;
        }

        private static void Watch(int id)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                _backTo = Map();
                nc.SendRequest(new ChangeMapRequest(id));
                _watched = id;
                _watchedAt = Time.unscaledTime;
                Plugin.Trace("[бои] иду смотреть бой " + id);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[бои] переход в бой: " + e.Message); }
        }
    }
}
