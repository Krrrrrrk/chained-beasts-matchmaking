using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal sealed class PingVisual
{
    internal PingPacket Packet;
    internal Vector3 World;
    internal string Label;
    internal double Until;
}

/// <summary>Plain managed drawing helper, separate from IL2CPP-injected Unity callbacks.</summary>
internal sealed class PingView : IDisposable
{
    private readonly BrowserTheme _theme = new();
    private readonly UiIcons _icons = new();
    private readonly Texture2D[] _sectors = new Texture2D[3];
    private Texture2D _disc;
    internal static float Scale => Math.Clamp(Math.Min(Screen.width / 1600f, Screen.height / 900f), .8f, 1.8f);

    internal void Draw(IReadOnlyList<PingVisual> visuals, Camera camera, double now, bool open,
        Vector2 center, PingKind selected, bool hasTarget, string status, string controlsHint = null)
    {
        var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldDepth = GUI.depth; var oldEnabled = GUI.enabled;
        try
        {
            GUI.depth = -1000; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
            var scale = Scale;
            _theme.Prepare(scale);
            if (open || visuals.Count > 0) EnsureDisc();
            if (camera != null && camera.enabled && camera.gameObject.activeInHierarchy && camera.targetDisplay == 0)
                foreach (var v in visuals)
                {
                    var projected = camera.WorldToScreenPoint(v.World);
                    if (!float.IsFinite(projected.x) || !float.IsFinite(projected.y) || !float.IsFinite(projected.z) ||
                        projected.z <= 0 || projected.x < 0 || projected.x > Screen.width || projected.y < 0 || projected.y > Screen.height) continue;
                    var x = projected.x; var y = Screen.height - projected.y;
                    var opacity = (float)Math.Clamp((v.Until - now) / .6, 0, 1);
                    var accent = v.Packet.Kind == PingKind.Sorry ? BrowserTheme.Gold : BrowserTheme.Teal;
                    GUI.color = new Color(1, 1, 1, opacity);
                    if (v.Packet.Kind == PingKind.Location)
                    {
                        GroundRing(camera, v.World, PingProtocol.Lifetime - (v.Until - now), accent, scale);
                        Disc(x, y, 2 * scale, accent);
                        Fill(new(x - scale, y - 33 * scale, 2 * scale, 18 * scale), accent);
                        y -= 50 * scale;
                    }
                    var width = 199 * scale; var height = 34 * scale;
                    Fill(new(x - width / 2 - 2, y - height - 2, width + 4, height + 4), accent);
                    Fill(new(x - width / 2, y - height, width, height), BrowserTheme.Ink);
                    _icons.Draw(Icon(v.Packet.Kind), new Rect(x - width / 2 + 8 * scale, y - height + 6 * scale, 22 * scale, 22 * scale), accent);
                    GUI.Label(new Rect(x - width / 2 + 34 * scale, y - height, width - 38 * scale, height), v.Label, _theme.Button);
                }
            if (!open) return;
            GUI.color = Color.white;
            EnsureTextures();
            var radius = 155 * scale;
            for (var i = 0; i < 3; i++)
            {
                var kind = (PingKind)(i + 1);
                var color = selected == kind ? BrowserTheme.Teal : BrowserTheme.Ink;
                if (kind == PingKind.Location && !hasTarget) color = BrowserTheme.Disabled;
                GUI.color = color;
                GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _sectors[i], ScaleMode.StretchToFill, true);
            }
            GUI.color = Color.white;
            Disc(center.x, center.y, 47 * scale, BrowserTheme.Header);
            WheelLabel(center, 0, -94, Icon(PingKind.Location), "PING HERE", selected == PingKind.Location, scale, hasTarget);
            WheelLabel(center, 82, 53, Icon(PingKind.Thanks), "THANKS", selected == PingKind.Thanks, scale);
            WheelLabel(center, -82, 53, Icon(PingKind.Sorry), "SORRY", selected == PingKind.Sorry, scale);
            GUI.Label(new Rect(center.x - 50 * scale, center.y - 17 * scale, 100 * scale, 34 * scale),
                selected == PingKind.None ? "CANCEL" : "RELEASE", _theme.Button);
            Fill(new(center.x - 230 * scale, center.y + 166 * scale, 460 * scale, 55 * scale), BrowserTheme.Ink);
            GUI.Label(new Rect(center.x - 225 * scale, center.y + 169 * scale, 450 * scale, 24 * scale),
                controlsHint ?? "Move mouse / release " + Plugin.PingKey.Value + " / Esc cancels", _theme.Button);
            GUI.Label(new Rect(center.x - 225 * scale, center.y + 193 * scale, 450 * scale, 24 * scale),
                selected == PingKind.Location && !hasTarget ? "Aim at a nearby surface before opening the wheel" : status, _theme.CenteredMuted);
        }
        finally { GUI.color = oldColor; GUI.contentColor = oldContent; GUI.depth = oldDepth; GUI.enabled = oldEnabled; }
    }

    private void WheelLabel(Vector2 center, float x, float y, UiIcon icon, string text, bool selected, float scale, bool enabled = true)
    {
        _icons.Draw(icon, new Rect(center.x + (x - 18) * scale, center.y + (y - 31) * scale, 36 * scale, 36 * scale),
            !enabled ? BrowserTheme.Border : selected ? BrowserTheme.Ink : BrowserTheme.Gold);
        GUI.Label(new Rect(center.x + (x - 64) * scale, center.y + (y + 8) * scale, 128 * scale, 26 * scale),
            text, !enabled ? _theme.DisabledButton : selected ? _theme.ButtonDark : _theme.Button);
    }

    private static UiIcon Icon(PingKind kind) => kind == PingKind.Location ? UiIcon.Target : kind == PingKind.Thanks ? UiIcon.Heart : UiIcon.Sorry;

    private void GroundRing(Camera camera, Vector3 world, double age, Color color, float scale)
    {
        // Project an XZ circle around the stored map point every draw. Zoom and pan affect
        // only its projection; no screen coordinate is ever stored back into the marker.
        const int segments = 32;
        var radius = .65 + .06 * Math.Sin(age * 7);
        Vector3 first = default, previous = default;
        for (var i = 0; i <= segments; i++)
        {
            Vector3 projected;
            if (i == segments) projected = first;
            else
            {
                var angle = i * Math.PI * 2 / segments;
                projected = camera.WorldToScreenPoint(new Vector3(world.x + (float)(radius * Math.Cos(angle)),
                    world.y + .03f, world.z + (float)(radius * Math.Sin(angle))));
                if (i == 0) first = projected;
            }
            if (i > 0 && Visible(previous) && Visible(projected))
            {
                var dx = projected.x - previous.x; var dy = projected.y - previous.y;
                var steps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / (2 * scale)), 1, 8);
                for (var step = 0; step < steps; step++)
                {
                    var fraction = (float)step / steps;
                    Disc(previous.x + dx * fraction, Screen.height - previous.y - dy * fraction, 1.3f * scale, color);
                }
            }
            previous = projected;
        }
    }

    private static bool Visible(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) &&
        float.IsFinite(point.z) && point.z > 0 && point.x >= 0 && point.x <= Screen.width && point.y >= 0 && point.y <= Screen.height;

    private void EnsureDisc()
    {
        if (_disc != null) return;
        const int size = 64;
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + .5 - size / 2) / (size / 2);
                var dy = (y + .5 - size / 2) / (size / 2);
                var alpha = (byte)Math.Round(Math.Clamp((1 - Math.Sqrt(dx * dx + dy * dy)) * size / 2, 0, 1) * 255);
                pixels[y * size + x] = new Color32 { r = 255, g = 255, b = 255, a = alpha };
            }
        _disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        _disc.SetPixels32((Il2CppStructArray<Color32>)pixels); _disc.Apply(false, true);
    }

    private void Disc(float x, float y, float radius, Color color)
    {
        var old = GUI.color;
        try
        {
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(new Rect(x - radius, y - radius, radius * 2, radius * 2), _disc, ScaleMode.StretchToFill, true);
        }
        finally { GUI.color = old; }
    }

    private void EnsureTextures()
    {
        if (_sectors[0] != null) return;
        const int size = 256;
        for (var i = 0; i < 3; i++)
        {
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + .5 - size / 2) / (size / 2);
                    var dy = (size / 2 - y - .5) / (size / 2); // Texture bottom-up to GUI top-down.
                    var radius = Math.Sqrt(dx * dx + dy * dy);
                    var angle = Math.Atan2(dx, -dy);
                    var boundary = Math.Min(Math.Abs(Math.Abs(angle) - Math.PI / 3), Math.PI - Math.Abs(angle));
                    if (radius is > .32 and < .98 && boundary > .018 && PingProtocol.Sector(dx, dy, 0) == (PingKind)(i + 1))
                        pixels[y * size + x] = new Color32 { r = 255, g = 255, b = 255, a = 255 };
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            _sectors[i] = texture;
            texture.SetPixels32((Il2CppStructArray<Color32>)pixels); texture.Apply(false, true);
        }
    }

    private static void Fill(Rect rect, Color color)
    {
        var old = GUI.color;
        try { GUI.color = new Color(color.r, color.g, color.b, color.a * old.a); GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true); }
        finally { GUI.color = old; }
    }
    public void Dispose()
    {
        _icons.Dispose();
        foreach (var texture in _sectors) if (texture != null) UnityEngine.Object.Destroy(texture);
        Array.Clear(_sectors, 0, _sectors.Length);
        if (_disc != null) UnityEngine.Object.Destroy(_disc);
        _disc = null;
    }
}
