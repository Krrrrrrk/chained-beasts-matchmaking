using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal sealed class BrowserTheme
{
    internal static readonly Color Ink = new(.051f, .043f, .094f, 1);
    internal static readonly Color Header = new(.078f, .063f, .133f, 1);
    internal static readonly Color Raised = new(.106f, .094f, .165f, 1);
    internal static readonly Color Detail = new(.075f, .067f, .125f, 1);
    internal static readonly Color Border = new(.235f, .208f, .302f, 1);
    internal static readonly Color Hover = new(.157f, .141f, .220f, 1);
    internal static readonly Color Selected = new(.094f, .180f, .196f, 1);
    internal static readonly Color StatusFill = new(.078f, .086f, .137f, 1);
    internal static readonly Color Gold = new(.949f, .788f, .365f, 1);
    internal static readonly Color GoldHover = new(1, .871f, .506f, 1);
    internal static readonly Color Teal = new(.333f, .890f, .800f, 1);
    internal static readonly Color TealHover = new(.490f, .980f, .894f, 1);
    internal static readonly Color Coral = new(1, .537f, .529f, 1);
    internal static readonly Color Disabled = new(.129f, .122f, .176f, 1);
    private static readonly Color Cream = new(.953f, .929f, .886f, 1);
    private static readonly Color Faded = new(.678f, .663f, .753f, 1);
    internal GUIStyle Title, Eyebrow, Muted, Small, SmallRight, Button, ButtonDark, DisabledButton, BadgeDark,
        RoomTitle, DetailsTitle, Body, ChatBody, ChatSender, Count, CountRight, EmptyTitle, CenteredMuted, Transparent, Status;
    private float _preparedScale = -1;

    internal void Prepare(float scale)
    {
        if (Math.Abs(_preparedScale - scale) < .015f) return;
        _preparedScale = scale;
        Title = Style(34, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, scale);
        Eyebrow = Style(11, Gold, FontStyle.Bold, TextAnchor.MiddleLeft, scale);
        Muted = Style(15, Faded, FontStyle.Normal, TextAnchor.MiddleLeft, scale);
        Small = Style(11, Faded, FontStyle.Normal, TextAnchor.MiddleLeft, scale);
        SmallRight = Style(11, Faded, FontStyle.Normal, TextAnchor.MiddleRight, scale);
        Button = Style(14, Cream, FontStyle.Bold, TextAnchor.MiddleCenter, scale);
        ButtonDark = Style(14, Ink, FontStyle.Bold, TextAnchor.MiddleCenter, scale);
        DisabledButton = Style(13, Faded, FontStyle.Bold, TextAnchor.MiddleCenter, scale);
        BadgeDark = Style(10, Ink, FontStyle.Bold, TextAnchor.MiddleCenter, scale);
        RoomTitle = Style(18, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, scale);
        DetailsTitle = Style(22, Cream, FontStyle.Bold, TextAnchor.UpperLeft, scale, true);
        Body = Style(13, Cream, FontStyle.Normal, TextAnchor.MiddleLeft, scale, true);
        ChatBody = Style(13, Cream, FontStyle.Normal, TextAnchor.UpperLeft, scale, true);
        ChatSender = Style(13, Gold, FontStyle.Bold, TextAnchor.UpperLeft, scale);
        Count = Style(26, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, scale);
        CountRight = Style(24, Cream, FontStyle.Bold, TextAnchor.MiddleRight, scale);
        EmptyTitle = Style(24, Cream, FontStyle.Bold, TextAnchor.MiddleCenter, scale, true);
        CenteredMuted = Style(15, Faded, FontStyle.Normal, TextAnchor.MiddleCenter, scale, true);
        Status = Style(13, Faded, FontStyle.Normal, TextAnchor.MiddleLeft, scale, true);
        Transparent = Style(14, Cream, FontStyle.Normal, TextAnchor.MiddleCenter, scale);
    }

    private static GUIStyle Style(int size, Color color, FontStyle weight, TextAnchor anchor, float scale, bool wrap = false)
    {
        var style = new GUIStyle()
        {
            font = GUI.skin.label.font ?? GUI.skin.font,
            fontSize = Math.Max(8, (int)Math.Round(size * scale)), fontStyle = weight,
            alignment = anchor, wordWrap = wrap, richText = false,
            padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip
        };
        foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
        { state.background = null; state.textColor = color; }
        return style;
    }
}
