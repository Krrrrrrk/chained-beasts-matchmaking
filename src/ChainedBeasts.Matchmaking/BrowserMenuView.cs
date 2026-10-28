using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

// Presentation only: actions are queued for Update. No Steam calls or injected managed-parameter methods.
internal sealed class BrowserMenuView : IDisposable
{
    internal bool Worldwide, OtherBuilds, Acknowledge, RefreshRequested, JoinRequested, OpenRequested, CloseRequested, UseFallback;
    internal ulong Selected;
    internal int Page;
    internal double RefreshAfter;
    private const int PageSize = 5;
    private float _scale, _left, _top;
    private readonly UiIcons _icons = new();
    private BrowserTheme _theme;
    private GUIContent _content;
    private readonly Dictionary<string, string> _clippedTitles = new();

    internal void Draw(SteamBrowser service, bool open)
    {
        var oldColor = GUI.color;
        var oldContent = GUI.contentColor;
        var oldEnabled = GUI.enabled;
        var oldDepth = GUI.depth;
        try
        {
            GUI.depth = -1100; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
            if (UseFallback) { DrawFallback(service, open); return; }
            _scale = Math.Max(.1f, Math.Min(2f, Math.Min(Screen.width / 1440f, Screen.height / 900f)));
            _left = (Screen.width - 1120 * _scale) / 2;
            _top = (Screen.height - 720 * _scale) / 2;
            _theme ??= new BrowserTheme();
            _theme.Prepare(_scale);
            _content ??= new GUIContent();
            if (!open) { DrawLauncher(); return; }

            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.025f, .016f, .055f, .80f));
            Fill(R(10, 12, 1120, 720), new Color(0, 0, 0, .38f));
            Panel(0, 0, 1120, 720, BrowserTheme.Ink, BrowserTheme.Border);
            Fill(R(1, 1, 1118, 116), BrowserTheme.Header);
            Fill(R(0, 0, 5, 118), BrowserTheme.Gold);
            Fill(R(28, 117, 1064, 1), BrowserTheme.Border);
            Label(28, 23, 700, 18, "CHAINED BEASTS  /  PLAY TOGETHER", _theme.Eyebrow);
            _icons.Draw(UiIcon.Party, R(28, 48, 42, 42), BrowserTheme.Gold);
            Label(84, 46, 682, 44, "FIND A PACK", _theme.Title);
            Label(28, 91, 760, 20, "Find a pack using Chained Beasts Matchmaking.", _theme.Muted);
            Badge(920, 28, 94, 26, "STEAM", BrowserTheme.Teal, _theme.BadgeDark);
            if (Button(1044, 24, 48, 36, "X", _theme.Button, BrowserTheme.Raised, BrowserTheme.Hover)) CloseRequested = true;
            Label(900, 82, 192, 18, $"{Plugin.ToggleKey.Value} / ESC TO CLOSE", _theme.SmallRight);
            DrawToolbar(service);
            DrawList(service);
            DrawDetails(service);
            Label(28, 696, 870, 16, "Steam members count accounts, not local characters. The host starts the run.", _theme.Small);
            Label(934, 696, 158, 16, "MATCHMAKING " + Plugin.Version, _theme.SmallRight);
        }
        finally { GUI.color = oldColor; GUI.contentColor = oldContent; GUI.enabled = oldEnabled; GUI.depth = oldDepth; }
    }

    public void Dispose() => _icons.Dispose();

    private void DrawLauncher()
    {
        var rect = new Rect(Screen.width - 312 * _scale - 24, 24, 312 * _scale, 58 * _scale);
        Fill(new Rect(rect.x + 4, rect.y + 5, rect.width, rect.height), new Color(0, 0, 0, .35f));
        Fill(rect, rect.Contains(Event.current.mousePosition) ? BrowserTheme.Hover : BrowserTheme.Ink);
        Fill(new Rect(rect.x, rect.y, 4 * _scale, rect.height), BrowserTheme.Teal);
        _content.text = "";
        if (GUI.Button(rect, _content, _theme.Transparent)) OpenRequested = true;
        GUI.Label(new Rect(rect.x + 20 * _scale, rect.y, 240 * _scale, rect.height), "FIND PUBLIC GAMES", _theme.Button);
        GUI.Label(new Rect(rect.x + 260 * _scale, rect.y, 40 * _scale, rect.height), Plugin.ToggleKey.Value.ToString(), _theme.Eyebrow);
    }

    private void DrawToolbar(SteamBrowser service)
    {
        var idle = service.Browser.Operation == Operation.Idle;
        if (Button(28, 138, 112, 40, "NEARBY", Worldwide ? _theme.Button : _theme.ButtonDark,
                Worldwide ? BrowserTheme.Raised : BrowserTheme.Gold, Worldwide ? BrowserTheme.Hover : BrowserTheme.GoldHover, idle))
        { Worldwide = false; RefreshRequested = true; }
        if (Button(148, 138, 140, 40, "WORLDWIDE", Worldwide ? _theme.ButtonDark : _theme.Button,
                Worldwide ? BrowserTheme.Gold : BrowserTheme.Raised, Worldwide ? BrowserTheme.GoldHover : BrowserTheme.Hover, idle))
        { Worldwide = true; RefreshRequested = true; }
        if (Check(318, 142, 280, 32, OtherBuilds, "Include other game builds", idle))
        { OtherBuilds = !OtherBuilds; RefreshRequested = true; }
        var wait = Math.Max(0, (int)Math.Ceiling(RefreshAfter - Time.realtimeSinceStartupAsDouble));
        var text = service.Browser.Operation == Operation.Searching ? "SEARCHING..." : wait > 0 ? $"REFRESH ({wait}s)" : "REFRESH";
        if (Button(930, 138, 162, 40, text, _theme.ButtonDark, BrowserTheme.Teal, BrowserTheme.TealHover,
                idle && service.Context.Available && wait == 0)) RefreshRequested = true;
        Fill(R(28, 196, 1064, 40), BrowserTheme.StatusFill);
        Fill(R(43, 213, 6, 6), idle ? BrowserTheme.Teal : BrowserTheme.Gold);
        var status = !service.Context.Available ? service.Context.Reason : RefreshRequested && idle ? "Updating search filters..." : service.Browser.Status;
        Label(62, 199, 1008, 34, status, _theme.Status);
    }

    private void DrawList(SteamBrowser service)
    {
        var lobbies = service.Browser.Lobbies;
        Label(28, 254, 460, 18, "OPEN LOBBIES", _theme.Eyebrow);
        Label(580, 254, 148, 18, $"{lobbies.Count} FOUND", _theme.SmallRight);
        var pages = Math.Max(1, (lobbies.Count + PageSize - 1) / PageSize);
        Page = Math.Clamp(Page, 0, pages - 1);
        if (lobbies.Count == 0)
        {
            Panel(28, 282, 700, 352, BrowserTheme.Raised, BrowserTheme.Border);
            var searching = service.Browser.Operation == Operation.Searching || RefreshRequested;
            Label(76, 376, 604, 42, searching ? "LOOKING FOR A PACK..." : "NO OPEN LOBBIES", _theme.EmptyTitle);
            Label(76, 424, 604, 54, searching ? "Checking Steam for available rooms." : "Try a wider search or refresh in a moment.", _theme.CenteredMuted);
            if (!searching && Button(246, 504, 264, 42, "SEARCH WORLDWIDE", _theme.ButtonDark,
                    BrowserTheme.Teal, BrowserTheme.TealHover, service.Context.Available))
            { Worldwide = true; RefreshRequested = true; }
        }
        for (var i = Page * PageSize; i < Math.Min(lobbies.Count, (Page + 1) * PageSize); i++)
        {
            var lobby = lobbies[i];
            var y = 282 + (i % PageSize) * 72;
            var selected = lobby.Id == Selected;
            var idle = service.Browser.Operation == Operation.Idle;
            var hover = R(28, y, 700, 64).Contains(Event.current.mousePosition) && idle;
            Panel(28, y, 700, 64, selected ? BrowserTheme.Selected : hover ? BrowserTheme.Hover : BrowserTheme.Raised,
                selected ? BrowserTheme.Teal : BrowserTheme.Border);
            if (selected) Fill(R(28, y, 4, 64), BrowserTheme.Teal);
            var before = GUI.enabled; GUI.enabled = idle;
            _content.text = "";
            if (GUI.Button(R(28, y, 700, 64), _content, _theme.Transparent)) { Selected = lobby.Id; Acknowledge = false; }
            GUI.enabled = before;
            Label(48, y + 7, 420, 28, ClipTitle(RoomName(lobby), _theme.RoomTitle, 420), _theme.RoomTitle);
            Label(48, y + 36, 440, 18, "STEAM LOBBY " + lobby.Id, _theme.Small);
            var assessment = LobbyPolicy.Assess(lobby, service.Context);
            var label = assessment.Compatibility switch { Compatibility.Blocked => "UNAVAILABLE", Compatibility.FoundationMatched => "MOD INFO", _ => "OPEN" };
            var color = assessment.Compatibility switch { Compatibility.Blocked => BrowserTheme.Coral, Compatibility.FoundationMatched => BrowserTheme.Teal, _ => BrowserTheme.Gold };
            Badge(491, y + 21, 126, 22, label, color, _theme.BadgeDark);
            Label(634, y + 7, 72, 32, $"{lobby.Members}/{lobby.MemberLimit}", _theme.CountRight);
            Label(626, y + 42, 80, 14, "MEMBERS", _theme.SmallRight);
        }
        var first = lobbies.Count == 0 ? 0 : Page * PageSize + 1;
        Label(28, 650, 370, 30, $"{first}–{Math.Min(lobbies.Count, (Page + 1) * PageSize)} OF {lobbies.Count}  /  PAGE {Page + 1}", _theme.Small);
        if (Button(482, 646, 118, 32, "< PREVIOUS", _theme.Button, BrowserTheme.Raised, BrowserTheme.Hover, Page > 0)) Page--;
        if (Button(608, 646, 120, 32, "NEXT >", _theme.Button, BrowserTheme.Raised, BrowserTheme.Hover, Page < pages - 1)) Page++;
    }

    private void DrawDetails(SteamBrowser service)
    {
        Panel(752, 254, 340, 424, BrowserTheme.Detail, BrowserTheme.Border);
        Label(776, 277, 292, 18, "YOUR NEXT RUN", _theme.Eyebrow);
        Lobby selected = null;
        foreach (var lobby in service.Browser.Lobbies) if (lobby.Id == Selected) { selected = lobby; break; }
        if (selected == null)
        {
            Label(776, 350, 292, 64, "PICK YOUR\nPACK", _theme.EmptyTitle);
            Label(776, 438, 292, 76, "Choose a room on the left to see its details and join the lobby.", _theme.Body);
            Button(776, 618, 292, 42, "SELECT A LOBBY", _theme.Button, BrowserTheme.Raised, BrowserTheme.Hover, false);
            return;
        }
        var assessment = LobbyPolicy.Assess(selected, service.Context);
        Label(776, 307, 292, 32, ClipTitle(RoomName(selected), _theme.DetailsTitle, 292), _theme.DetailsTitle);
        Label(776, 346, 292, 18, "STEAM LOBBY " + selected.Id, _theme.Small);
        Fill(R(776, 376, 292, 48), BrowserTheme.Raised);
        Label(792, 381, 80, 34, $"{selected.Members}/{selected.MemberLimit}", _theme.Count);
        Label(880, 386, 172, 24, "STEAM MEMBERS", _theme.SmallRight);
        Label(776, 444, 292, 16, "GAME BUILD", _theme.Small);
        Label(776, 463, 292, 25, LobbyPolicy.Display(selected.Build, 60), _theme.Body);
        Label(776, 500, 292, 16, "MOD INFORMATION", _theme.Small);
        Label(776, 520, 292, 48, assessment.Detail, _theme.Body);
        var needsConfirmation = LobbyPolicy.NeedsModConfirmation(assessment, service.Context);
        if (needsConfirmation && Check(776, 572, 292, 38, Acknowledge, "This host has my Foundation/TC setup.", true)) Acknowledge = !Acknowledge;
        var allowed = service.Context.Available && service.Browser.Operation == Operation.Idle && assessment.Allowed && (!needsConfirmation || Acknowledge) && !RefreshRequested;
        var caption = service.Browser.Operation == Operation.CheckingLobby ? "CHECKING ROOM..." : assessment.Allowed ? "JOIN LOBBY  >" : "ROOM UNAVAILABLE";
        if (Button(776, 618, 292, 42, caption, _theme.ButtonDark, BrowserTheme.Teal, BrowserTheme.TealHover, allowed)) JoinRequested = true;
    }

    private Rect R(float x, float y, float width, float height) => new(_left + x * _scale, _top + y * _scale, width * _scale, height * _scale);
    private static string RoomName(Lobby lobby) => lobby.Title.StartsWith("Lobby ", StringComparison.Ordinal) ?
        "Public lobby " + (lobby.Id % 1_000_000).ToString("D6") : lobby.Title;
    private string ClipTitle(string text, GUIStyle style, float width)
    {
        var key = text + "/" + style.fontSize + "/" + width + "/" + _scale;
        if (_clippedTitles.TryGetValue(key, out var cached)) return cached;
        if (_clippedTitles.Count >= 128) _clippedTitles.Clear();
        var result = text;
        _content.text = text;
        if (style.CalcSize(_content).x > width * _scale - 4)
        {
            var low = 0; var high = text.Length;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                _content.text = text.Substring(0, middle) + "...";
                if (style.CalcSize(_content).x <= width * _scale - 4) low = middle; else high = middle - 1;
            }
            result = text.Substring(0, low) + "...";
        }
        _clippedTitles[key] = result;
        return result;
    }
    private static void Fill(Rect rect, Color color)
    {
        var before = GUI.color;
        try { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = before; }
    }
    private void Panel(float x, float y, float width, float height, Color fill, Color border)
    { Fill(R(x, y, width, height), border); Fill(R(x + 1, y + 1, width - 2, height - 2), fill); }
    private void Label(float x, float y, float width, float height, string text, GUIStyle style) => GUI.Label(R(x, y, width, height), text, style);
    private void Badge(float x, float y, float width, float height, string text, Color color, GUIStyle style)
    { Fill(R(x, y, width, height), color); Label(x, y, width, height, text, style); }
    private bool Button(float x, float y, float width, float height, string text, GUIStyle style, Color fill, Color hover, bool enabled = true)
    {
        var rect = R(x, y, width, height);
        Fill(rect, !enabled ? BrowserTheme.Disabled : rect.Contains(Event.current.mousePosition) ? hover : fill);
        var before = GUI.enabled;
        try { GUI.enabled = enabled; _content.text = text; return GUI.Button(rect, _content, enabled ? style : _theme.DisabledButton); }
        finally { GUI.enabled = before; }
    }
    private bool Check(float x, float y, float width, float height, bool value, string text, bool enabled)
    {
        var before = GUI.enabled;
        try
        {
            GUI.enabled = enabled;
            _content.text = "";
            var clicked = GUI.Button(R(x, y, width, height), _content, _theme.Transparent);
            Panel(x, y + 6, 18, 18, value ? BrowserTheme.Teal : BrowserTheme.Raised, value ? BrowserTheme.Teal : BrowserTheme.Border);
            if (value) Label(x, y + 3, 18, 24, "X", _theme.BadgeDark);
            Label(x + 28, y, width - 28, height, text, _theme.Body);
            return clicked;
        }
        finally { GUI.enabled = before; }
    }

    // Basic retained controls remain available if custom drawing fails on a particular player.
    private void DrawFallback(SteamBrowser service, bool open)
    {
        if (!open)
        {
            GUILayout.BeginArea(new Rect(Math.Max(10, Screen.width - 270), 20, 250, 70));
            try { if (GUILayout.Button($"Find public games ({Plugin.ToggleKey.Value})")) OpenRequested = true; }
            finally { GUILayout.EndArea(); }
            return;
        }
        GUILayout.BeginArea(new Rect(20, 20, Math.Min(850, Screen.width - 40), Math.Min(650, Screen.height - 40)), "Public Steam Games", GUI.skin.window);
        try
        {
            GUILayout.Space(24);
            GUILayout.Label(service.Context.Available ? service.Browser.Status : service.Context.Reason);
            var worldwide = GUILayout.Toggle(Worldwide, "Worldwide");
            var other = GUILayout.Toggle(OtherBuilds, "Include other game builds");
            if (worldwide != Worldwide || other != OtherBuilds) { Worldwide = worldwide; OtherBuilds = other; RefreshRequested = true; }
            if (GUILayout.Button("Refresh")) RefreshRequested = true;
            if (GUILayout.Button("Close")) CloseRequested = true;
            var lobbies = service.Browser.Lobbies;
            var pages = Math.Max(1, (lobbies.Count + PageSize - 1) / PageSize);
            Page = Math.Clamp(Page, 0, pages - 1);
            for (var i = Page * PageSize; i < Math.Min(lobbies.Count, (Page + 1) * PageSize); i++)
                if (GUILayout.Button($"{lobbies[i].Title} — {lobbies[i].Members}/{lobbies[i].MemberLimit}")) { Selected = lobbies[i].Id; Acknowledge = false; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous")) Page = Math.Max(0, Page - 1);
            if (GUILayout.Button("Next")) Page = Math.Min(pages - 1, Page + 1);
            GUILayout.EndHorizontal();
            foreach (var lobby in lobbies)
            {
                if (lobby.Id != Selected) continue;
                var assessment = LobbyPolicy.Assess(lobby, service.Context);
                GUILayout.Label(assessment.Detail);
                if (LobbyPolicy.NeedsModConfirmation(assessment, service.Context)) Acknowledge = GUILayout.Toggle(Acknowledge, "I checked the Foundation/TC setup.");
                GUI.enabled = service.Context.Available && service.Browser.Operation == Operation.Idle && assessment.Allowed &&
                    (!LobbyPolicy.NeedsModConfirmation(assessment, service.Context) || Acknowledge);
                if (GUILayout.Button("Join selected lobby")) JoinRequested = true;
                GUI.enabled = true;
                break;
            }
        }
        finally { GUILayout.EndArea(); }
    }
}
