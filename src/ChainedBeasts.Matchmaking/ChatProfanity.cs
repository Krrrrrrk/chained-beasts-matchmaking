using ChainedBeasts.Matchmaking.Core;
using Crosstales.BWF;

namespace ChainedBeasts.Matchmaking;

/// <summary>Reuse the game's username sanitizer without creating/configuring its filter service.</summary>
internal sealed class ChatProfanity
{
    private bool _faulted, _logged;
    internal bool Ready
    {
        get
        {
            if (_faulted) return false;
            try
            {
                // Lowercase instance is a direct field getter. Uppercase Instance may create/search.
                var manager = BWFManager.instance;
                return manager is not null && manager.isReady;
            }
            catch (Exception e) { Fail(e); return false; }
        }
    }

    internal bool TryText(string text, int maximum, out string filtered)
    {
        filtered = "";
        if (!Ready) return false;
        text = ChatText.Clean(text, maximum);
        if (text.Length == 0) return false;
        try
        {
            var result = PlayerNameFilter.Sanitize(text);
            if (result is null) { Fail(new InvalidOperationException("Native filter returned no result.")); return false; }
            filtered = ChatText.Clean(result, maximum);
            if (filtered.Length == 0) filtered = "***";
            return true;
        }
        catch (Exception e) { Fail(e); return false; }
    }

    internal bool TryPacket(ChatPacket packet, out ChatPacket filtered)
    {
        filtered = default;
        if (!TryText(packet.Text, ChatText.MaximumCharacters, out var text) ||
            !TryText(packet.Name, ChatText.MaximumName, out var name)) return false;
        filtered = packet with { Text = text, Name = name };
        return true;
    }

    private void Fail(Exception e)
    {
        _faulted = true;
        if (_logged) return;
        _logged = true;
        // Never put the original message or name in logs, including exception messages.
        Plugin.Logger.LogWarning("[Chat] game profanity filter unavailable (" + e.GetType().Name + "); text withheld until relay/session reset.");
    }
    internal void Reset() => _faulted = false;
}
