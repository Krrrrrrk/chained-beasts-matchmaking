using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal sealed class HostToolsView : IDisposable
{
    private readonly BrowserTheme _theme = new();
    internal bool CloseRequested, ConfirmRequested, CancelRequested, KickRequested, UnblockRequested;
    internal ulong KickClient, KickAccount, UnblockAccount;
    private int _page;
    private float _scale, _left, _top;
    private readonly UiIcons _icons = new();

    internal void Draw(HostModeration policy, IReadOnlyList<HostPeer> peers, IReadOnlyList<BlockedAccount> blocked)
    {
        var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldDepth = GUI.depth; var oldEnabled = GUI.enabled;
        try
        {
            GUI.depth = -1200; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
            _scale = Math.Min(1.6f, Math.Min(Screen.width / 1180f, Screen.height / 790f));
            _left = (Screen.width - 1060 * _scale) / 2; _top = (Screen.height - 710 * _scale) / 2;
            _theme.Prepare(_scale);
            Fill(0, 0, 1060, 710, BrowserTheme.Ink);
            Fill(0, 0, 1060, 88, BrowserTheme.Header);
            Label(28, 13, 840, 18, "HOST CONTROLS / " + Plugin.HostToolsKey.Value, _theme.Eyebrow);
            _icons.Draw(UiIcon.Lock, R(28, 37, 36, 36), BrowserTheme.Gold);
            Label(82, 34, 786, 44, "YOUR LOBBY, YOUR CALL", _theme.Title);
            if (Button(912, 24, 120, 40, "Close", true)) CloseRequested = true;
            Label(28, 96, 1004, 26, "The game keeps running. Kick disconnects the account; rejoining stays blocked for this lobby.", _theme.Body);
            if (policy.Pending is { } pending)
            {
                Fill(28, 142, 1004, 232, BrowserTheme.Raised);
                Label(48, 157, 964, 36, "Kick " + pending.Name + "?", _theme.DetailsTitle);
                Label(48, 201, 964, 26, "Steam " + pending.Account, _theme.Body);
                Label(48, 234, 964, 48, "This disconnects their whole account, including any local co-op characters. The game handles character cleanup.", _theme.Body);
                if (Button(48, 300, 330, 48, "Confirm kick + block rejoin", true)) ConfirmRequested = true;
                if (Button(394, 300, 180, 48, "Cancel", true)) CancelRequested = true;
            }
            else
            {
                Label(28, 132, 1004, 22, "CONNECTED ACCOUNTS", _theme.Eyebrow);
                if (peers.Count == 0) Label(28, 165, 1004, 35, "No remote players connected. Your own account is excluded.", _theme.Muted);
                for (var i = 0; i < Math.Min(peers.Count, 4); i++)
                {
                    var peer = peers[i]; var y = 162 + i * 52;
                    Fill(28, y, 1004, 46, BrowserTheme.Raised);
                    var nameLength = peer.Name.Length > 20 && char.IsHighSurrogate(peer.Name[19]) ? 19 : 20;
                    var title = peer.Name.Length > 20 ? peer.Name[..nameLength] + "..." : peer.Name;
                    Label(42, y + 3, 410, 23, title, _theme.RoomTitle);
                    Label(42, y + 25, 740, 18, peer.Account == 0 ? "Steam identity unavailable; Kick disabled" :
                        "Steam " + peer.Account + " / " + peer.Characters + " local character slot(s)", _theme.Small);
                    if (Button(834, y + 6, 182, 34, "Kick + block rejoin", peer.Account != 0))
                    { KickClient = peer.Client; KickAccount = peer.Account; KickRequested = true; }
                }
            }
            Label(28, 387, 700, 24, "BLOCKED FOR THIS LOBBY", _theme.Eyebrow);
            var pages = Math.Max(1, (blocked.Count + 3) / 4); _page = Math.Clamp(_page, 0, pages - 1);
            if (Button(780, 382, 72, 30, "Prev", _page > 0)) _page--;
            Label(860, 382, 80, 30, (_page + 1) + " / " + pages, _theme.Button);
            if (Button(948, 382, 84, 30, "Next", _page + 1 < pages)) _page++;
            if (blocked.Count == 0) Label(28, 426, 1004, 34, "Nobody blocked. Blocks clear when you stop hosting this lobby.", _theme.Muted);
            for (var i = 0; i < 4 && _page * 4 + i < blocked.Count; i++)
            {
                var account = blocked[_page * 4 + i]; var y = 424 + i * 45;
                Fill(28, y, 1004, 39, BrowserTheme.Detail);
                Label(42, y, 760, 39, account.Name + " / Steam " + account.Account, _theme.Body);
                if (Button(834, y + 4, 182, 31, "Allow rejoin", true)) { UnblockAccount = account.Account; UnblockRequested = true; }
            }
            Fill(28, 619, 1004, 63, BrowserTheme.StatusFill);
            Label(42, 625, 976, 50, policy.Status, _theme.Status);
        }
        finally { GUI.color = oldColor; GUI.contentColor = oldContent; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
    }

    public void Dispose() => _icons.Dispose();

    private Rect R(float x, float y, float w, float h) => new(_left + x * _scale, _top + y * _scale, w * _scale, h * _scale);
    private void Label(float x, float y, float w, float h, string text, GUIStyle style) => GUI.Label(R(x, y, w, h), text, style);
    private bool Button(float x, float y, float w, float h, string text, bool enabled)
    {
        Fill(x, y, w, h, enabled ? BrowserTheme.Selected : BrowserTheme.Disabled);
        var old = GUI.enabled; GUI.enabled = enabled;
        try { return GUI.Button(R(x, y, w, h), new GUIContent { text = text }, enabled ? _theme.Button : _theme.DisabledButton); }
        finally { GUI.enabled = old; }
    }
    private void Fill(float x, float y, float w, float h, Color color)
    {
        var old = GUI.color;
        try { GUI.color = color; GUI.DrawTexture(R(x, y, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = old; }
    }
}
