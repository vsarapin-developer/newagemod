using System.Collections.Generic;

namespace NewAgeQoL
{
    internal static class Store
    {
        private class Row
        {
            internal string Image = "";
            internal string Text = "";
        }

        private static readonly Dictionary<int, Row> Rows = new Dictionary<int, Row>();

        internal static string Image(int thingId)
        {
            lock (Rows)
                return Rows.TryGetValue(thingId, out var r) && r.Image.Length > 0 ? r.Image : null;
        }

        internal static string Text(int thingId)
        {
            lock (Rows)
                return Rows.TryGetValue(thingId, out var r) && r.Text.Length > 0 ? r.Text : null;
        }

        internal static void SetImage(int thingId, string image)
        {
            if (string.IsNullOrEmpty(image)) return;
            lock (Rows) Get(thingId).Image = image;
        }

        internal static void SetText(int thingId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (Rows)
            {
                var row = Get(thingId);
                if (row.Text.Length < text.Length) row.Text = text;
            }
        }

        private static Row Get(int thingId)
        {
            if (!Rows.TryGetValue(thingId, out var row)) Rows[thingId] = row = new Row();
            return row;
        }
    }
}
