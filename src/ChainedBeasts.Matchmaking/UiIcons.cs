using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal enum UiIcon { Target, Heart, Sorry, Party, Lock }

/// <summary>Owned CC0 alpha masks, embedded in this DLL; no file or network access at runtime.</summary>
internal sealed class UiIcons : IDisposable
{
    private const int Size = 64;
    private readonly Texture2D[] _textures = new Texture2D[5];

    internal void Draw(UiIcon icon, Rect rect, Color tint)
    {
        var texture = Get(icon);
        var previous = GUI.color;
        try
        {
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * previous.a);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }
        finally { GUI.color = previous; }
    }

    private Texture2D Get(UiIcon icon)
    {
        var index = (int)icon;
        if (index < 0 || index >= _textures.Length) throw new ArgumentOutOfRangeException(nameof(icon));
        if (_textures[index] != null) return _textures[index];
        using var stream = typeof(UiIcons).Assembly.GetManifestResourceStream("Matchmaking.Icons." + icon + ".alpha")
            ?? throw new InvalidDataException("Missing embedded icon: " + icon);
        if (stream.Length != Size * Size) throw new InvalidDataException("Invalid embedded icon size: " + icon);
        var bytes = new byte[Size * Size];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException("Incomplete embedded icon: " + icon);
            offset += read;
        }
        var pixels = new Color32[bytes.Length];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32 { r = 255, g = 255, b = 255, a = bytes[i] };
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        try { texture.SetPixels32((Il2CppStructArray<Color32>)pixels); texture.Apply(false, true); }
        catch { UnityEngine.Object.Destroy(texture); throw; }
        return _textures[index] = texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        Array.Clear(_textures, 0, _textures.Length);
    }
}
