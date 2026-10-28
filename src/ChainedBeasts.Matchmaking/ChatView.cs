using ChainedBeasts.Matchmaking.Core;
using System.Text;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal sealed class ChatView
{
    private sealed class Row
    {
        internal readonly string Prefix;
        internal readonly float PrefixWidth, Height;
        internal readonly string[] Lines;
        internal Row(string prefix, float prefixWidth, string[] lines, float height)
        { Prefix = prefix; PrefixWidth = prefixWidth; Lines = lines; Height = height; }
    }
    private readonly BrowserTheme _theme = new();
    private readonly Dictionary<string, string[]> _wraps = new();
    private float _wrapWidth;
    internal int Page;
    private float _scale, _left, _top;
    internal void Draw(IReadOnlyList<ChatPacket> messages, bool open, string draft, string status, string key)
    {
        if (!open && messages.Count == 0) return;
        var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldDepth = GUI.depth; var oldEnabled = GUI.enabled;
        try
        {
            GUI.depth = -1250; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
            _scale = Math.Max(.25f, Math.Min(1.25f, Math.Min(Screen.width / 1100f, Screen.height / 760f)));
            _left = 16 * _scale;
            var width = open ? 480f : 400f;
            _theme.Prepare(_scale);
            if (_wrapWidth != (width - 40) * _scale) { _wrapWidth = (width - 40) * _scale; _wraps.Clear(); }
            var lineHeight = Math.Max(18, _theme.ChatBody.fontSize * 1.2f / _scale + 3);
            var draftText = string.Join("\n", Wrap(draft + "_", _wrapWidth));
            var inputHeight = Math.Max(32, (draftText.Count(c => c == '\n') + 1) * lineHeight) + 12;
            // Five short messages fit the feed. Long messages remain complete in the
            // editor, with fewer rows per history page when the available height requires it.
            var rowBudget = Screen.height / _scale - 32 - 30 - (open ? 50 + inputHeight + 32 : 4);
            var pages = new List<List<Row>>();
            var end = messages.Count;
            var oldest = Math.Max(0, messages.Count - (open ? 50 : 5));
            while (end > oldest)
            {
                var page = new List<Row>(); var used = 0f;
                while (end > oldest && page.Count < 5)
                {
                    var packet = messages[end - 1];
                    var prefix = Name(packet, Math.Min(160, width * .43f));
                    var prefixWidth = _theme.ChatSender.CalcSize(new GUIContent { text = prefix }).x / _scale + 5;
                    var firstWidth = _wrapWidth - prefixWidth * _scale;
                    var lines = open ? Wrap(packet.Text, firstWidth) : new[] { Elide(packet.Text, firstWidth, _theme.ChatBody) };
                    var row = new Row(prefix, prefixWidth, lines, lineHeight * lines.Length);
                    if (page.Count > 0 && used + row.Height + 4 > rowBudget) break;
                    page.Insert(0, row); used += row.Height + 4; end--;
                }
                pages.Add(page);
                if (!open) break;
            }
            if (pages.Count == 0) pages.Add(new());
            Page = Math.Clamp(Page, 0, pages.Count - 1);
            var rows = pages[open ? Page : 0];
            var rowBottom = 30 + rows.Sum(row => row.Height + 4);
            if (open && rows.Count == 0) rowBottom += 46;
            var heightTotal = open ? rowBottom + 50 + inputHeight + 32 : rowBottom + 4;
            _top = Screen.height - (heightTotal + 16) * _scale;
            Fill(0, 0, width, heightTotal, new Color(.051f, .043f, .094f, open ? .86f : .68f));
            Fill(0, 0, 2, heightTotal, new Color(.333f, .890f, .800f, .85f));
            Fill(2, 0, width - 2, 1, new Color(.333f, .890f, .800f, .25f));
            var solo = status.Contains("Solo preview", StringComparison.Ordinal);
            Label(12, 4, width - 128, 20, solo ? "SOLO CHAT PREVIEW" : "PARTY CHAT", _theme.Eyebrow);
            Label(width - 112, 4, 100, 20, open ? "ESC CLOSES" : key + " TO CHAT", _theme.SmallRight);
            var y = 30f;
            foreach (var row in rows)
            {
                Label(12, y, row.PrefixWidth, lineHeight, row.Prefix, _theme.ChatSender);
                Label(12 + row.PrefixWidth, y, width - 24 - row.PrefixWidth, lineHeight, row.Lines[0], _theme.ChatBody);
                if (row.Lines.Length > 1)
                    Label(12, y + lineHeight, width - 24, row.Height - lineHeight, string.Join("\n", row.Lines.Skip(1)), _theme.ChatBody);
                y += row.Height + 4;
            }
            if (!open) return;
            if (rows.Count == 0) Label(12, 31, width - 24, 42, solo ? "Local typing and filter preview. Nothing is sent to other players." : "Messages stay in this session. Everyone chatting needs the update.", _theme.ChatBody);
            Label(12, rowBottom + 2, width - 24, 20, "PgUp / PgDn: history   " + (Page + 1) + "/" + pages.Count, _theme.Small);
            Label(12, rowBottom + 22, width - 24, 24, status, _theme.Small);
            var inputY = rowBottom + 50;
            Fill(12, inputY, width - 24, inputHeight, new Color(.075f, .067f, .125f, .72f));
            Fill(12, inputY, width - 24, 1, new Color(.333f, .890f, .800f, .30f));
            Label(20, inputY + 6, width - 40, inputHeight - 12, draftText, _theme.ChatBody);
            Label(12, inputY + inputHeight + 4, width - 132, 24, "Enter: send / Esc: cancel", _theme.Small);
            Label(width - 108, inputY + inputHeight + 4, 96, 24, draft.EnumerateRunes().Count() + " / 200", _theme.SmallRight);
        }
        finally { GUI.color = oldColor; GUI.contentColor = oldContent; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
    }
    private string Name(ChatPacket packet, float width)
    {
        var name = "P" + (packet.PlayerIndex + 1) + "/" + packet.Name;
        return Elide(name, width * _scale, _theme.ChatSender, ":");
    }
    private static string Elide(string text, float pixels, GUIStyle style, string suffix = "")
    {
        if (style.CalcSize(new GUIContent { text = text + suffix }).x <= pixels) return text + suffix;
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (style.CalcSize(new GUIContent { text = result.ToString() + rune + "..." + suffix }).x > pixels) break;
            result.Append(rune.ToString());
        }
        return result + "..." + suffix;
    }
    private string[] Wrap(string text, float firstWidth)
    {
        var cacheKey = firstWidth + ":" + text;
        if (_wraps.TryGetValue(cacheKey, out var cached)) return cached;
        var lines = new List<string>(); var line = "";
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            var available = lines.Count == 0 ? firstWidth : _wrapWidth;
            if (line.Length > 0 && _theme.ChatBody.CalcSize(new GUIContent { text = line + value }).x * 1.08f > available)
            { lines.Add(line); line = ""; }
            line += value;
        }
        lines.Add(line);
        if (_wraps.Count >= 128) _wraps.Clear();
        return _wraps[cacheKey] = lines.ToArray();
    }
    private Rect R(float x, float y, float w, float h) => new(_left + x * _scale, _top + y * _scale, w * _scale, h * _scale);
    private void Label(float x, float y, float w, float h, string text, GUIStyle style) => GUI.Label(R(x, y, w, h), text, style);
    private void Fill(float x, float y, float w, float h, Color color)
    {
        var old = GUI.color;
        try { GUI.color = color; GUI.DrawTexture(R(x, y, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = old; }
    }
}
