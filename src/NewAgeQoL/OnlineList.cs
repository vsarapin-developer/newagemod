using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NewAgeQoL
{
    internal sealed class OnlinePlayer
    {
        internal int Id;
        internal string Login = "";
        internal int Level;
        internal string Class = "";
        internal string Clan = "";
        internal int Rank;
        internal bool Vip;
        internal bool Admin;
    }

    internal static class OnlineList
    {
        private const string Host = "nura.biz";
        private const int Port = 2000;

        private static readonly object Gate = new object();
        private static List<OnlinePlayer> _players = new List<OnlinePlayer>();
        private static string _status = "";
        private static bool _busy;
        private static int _version;
        private static DateTime _at = DateTime.MinValue;

        internal static bool Busy { get { lock (Gate) return _busy; } }
        internal static string Status { get { lock (Gate) return _status; } }
        internal static int Version { get { lock (Gate) return _version; } }
        internal static DateTime At { get { lock (Gate) return _at; } }
        internal static List<OnlinePlayer> Players { get { lock (Gate) return new List<OnlinePlayer>(_players); } }

        internal static bool Configured =>
            !string.IsNullOrEmpty(Plugin.CfgOnlineLogin?.Value?.Trim()) && !string.IsNullOrEmpty(Plugin.CfgOnlinePassword?.Value);

        internal static void Refresh()
        {
            string login = (Plugin.CfgOnlineLogin?.Value ?? "").Trim();
            string pass = Plugin.CfgOnlinePassword?.Value ?? "";
            string ver = (Plugin.CfgOnlineVersion?.Value ?? "").Trim();
            if (ver.Length == 0) ver = "11073";
            if (login.Length == 0 || pass.Length == 0)
            {
                Set("Укажи логин и пароль запасного аккаунта в настройках мода");
                return;
            }
            lock (Gate)
            {
                if (_busy) return;
                _busy = true;
                _status = "Запрашиваю список…";
                _version++;
            }
            var t = new Thread(() => Work(login, pass, ver)) { IsBackground = true, Name = "QoLOnlineList" };
            t.Start();
        }

        private static void Set(string status)
        {
            lock (Gate) { _status = status; _version++; }
        }

        private static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static void Work(string login, string pass, string ver)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.NoDelay = true;
                    var ar = client.BeginConnect(Host, Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(8000)) throw new Exception("нет соединения с " + Host);
                    client.EndConnect(ar);
                    var stream = client.GetStream();
                    var acc = new List<byte>();

                    Pump(stream, acc, 1.5, null);

                    Send(stream, "<Message type=\"315\"><auth account=\"" + Esc(login) + "\" password=\"" + Esc(pass) + "\" ver=\"" + Esc(ver) + "\" site=\"1\" /></Message>");
                    string lr = null;
                    foreach (var m in Pump(stream, acc, 8, m => m.Contains("LoginResponce")))
                        if (m.Contains("LoginResponce")) lr = m;
                    if (lr == null) throw new Exception("сервер не ответил на вход");
                    if (!lr.Contains("LoggedIn=\"1\""))
                    {
                        var mm = Regex.Match(lr, "Msg=\"([^\"]*)\"");
                        throw new Exception("вход отклонён" + (mm.Success && mm.Groups[1].Value.Length > 0 ? ": " + System.Net.WebUtility.HtmlDecode(mm.Groups[1].Value) : ""));
                    }

                    Send(stream, "<Message type=\"49\" />");
                    string m50 = null;
                    foreach (var m in Pump(stream, acc, 12, m => m.Contains("type=\"50\"")))
                        if (m.Contains("type=\"50\"")) m50 = m;
                    if (m50 == null) throw new Exception("список не пришёл");

                    var list = Parse(m50);
                    lock (Gate)
                    {
                        _players = list;
                        _at = DateTime.Now;
                        _status = "В игре: " + list.Count + " · " + _at.ToString("HH:mm:ss");
                    }
                    Plugin.Trace("[онлайн] получено " + list.Count + " игроков");
                }
            }
            catch (Exception e)
            {
                Set("Ошибка: " + e.Message);
                Plugin.Log?.LogWarning("[онлайн] " + e.Message);
            }
            finally
            {
                lock (Gate) { _busy = false; _version++; }
            }
        }

        private static void Send(NetworkStream s, string xml)
        {
            var b = Encoding.UTF8.GetBytes(xml + "\0");
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        private static List<string> Pump(NetworkStream s, List<byte> acc, double seconds, Func<string, bool> stopWhen)
        {
            var got = new List<string>();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var tmp = new byte[65536];
            while (DateTime.UtcNow < end)
            {
                if (!s.DataAvailable) { Thread.Sleep(40); continue; }
                int n = s.Read(tmp, 0, tmp.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (tmp[i] != 0) { acc.Add(tmp[i]); continue; }
                    if (acc.Count == 0) continue;
                    string m = Encoding.UTF8.GetString(acc.ToArray());
                    acc.Clear();
                    if (m.Trim().Length == 0) continue;
                    got.Add(m);
                    if (stopWhen != null && stopWhen(m)) return got;
                }
            }
            return got;
        }

        private static List<OnlinePlayer> Parse(string xml)
        {
            var list = new List<OnlinePlayer>();
            foreach (Match u in Regex.Matches(xml, "<user\\b([^>]*)/>"))
            {
                string a = u.Groups[1].Value;
                var p = new OnlinePlayer
                {
                    Id = Int(Attr(a, "id")),
                    Login = Attr(a, "login"),
                    Level = Int(Attr(a, "level")),
                    Class = Attr(a, "race"),
                    Clan = Attr(a, "icon"),
                    Rank = Int(Attr(a, "rank")),
                    Vip = Attr(a, "vip") == "1",
                    Admin = Attr(a, "admin").Length > 0,
                };
                if (p.Login.Length > 0) list.Add(p);
            }
            return list;
        }

        private static string Attr(string a, string key)
        {
            var m = Regex.Match(a, "\\b" + key + "=\"([^\"]*)\"");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }

        private static int Int(string s)
        {
            int n;
            return int.TryParse(s, out n) ? n : 0;
        }
    }
}
