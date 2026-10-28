using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ChainedBeasts.Matchmaking.Core;

public enum ChatMessage : byte { Hello = 1, Welcome, Request, Show }
public readonly record struct ChatPacket(ChatMessage Message, string Text = "", ulong Epoch = 0, ulong Sequence = 0,
    ulong Author = 0, ulong PlayerIndex = 0, double Created = 0, ulong Nonce = 0, string Name = "");

public static class ChatText
{
    public const int MaximumCharacters = 200, MaximumName = 32;
    public static bool Allowed(Rune rune) => Rune.GetUnicodeCategory(rune) is not
        (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse) &&
        rune.Value is not ('<' or '>');
    public static string Clean(string text, int maximum = MaximumCharacters)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = new StringBuilder(); var count = 0; var space = true;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count >= maximum) break;
            if (Rune.IsWhiteSpace(rune)) { if (!space) { result.Append(' '); count++; space = true; } continue; }
            if (!Allowed(rune)) continue;
            result.Append(rune.ToString()); count++; space = false;
        }
        return result.ToString().TrimEnd();
    }
}

public static class ChatProtocol
{
    public const int MaximumSenders = 8;
    public const int HeaderBytes = 64, MaximumBytes = HeaderBytes + 800 + 128;
    public const double DeliveryLifetime = 10;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static double Remaining(ChatPacket p, double now) => !double.IsFinite(now) || !double.IsFinite(p.Created) ||
        now < 0 || p.Created < 0 || p.Created > now + 1 ? 0 : Math.Clamp(p.Created + DeliveryLifetime - now, 0, DeliveryLifetime);
    public static bool Valid(ChatPacket p)
    {
        if (p.Text == null || p.Name == null || p.Sequence == 0 || !double.IsFinite(p.Created) || p.Created < 0 || p.PlayerIndex >= 8) return false;
        if (p.Message is ChatMessage.Hello or ChatMessage.Welcome)
            return p.Text.Length == 0 && p.Name.Length == 0 && p.Author == 0 && p.PlayerIndex == 0 && p.Created == 0 && p.Nonce != 0 &&
                (p.Message == ChatMessage.Hello ? p.Epoch == 0 : p.Epoch != 0);
        if (p.Message is not (ChatMessage.Request or ChatMessage.Show) || p.Epoch == 0 || p.Text.Length == 0 ||
            p.Text.Length > 400 || p.Name.Length > 64 || ChatText.Clean(p.Text) != p.Text || ChatText.Clean(p.Name, ChatText.MaximumName) != p.Name) return false;
        return p.Message == ChatMessage.Request ? p.Author == 0 && p.Name.Length == 0 && p.Nonce != 0 : p.Nonce == 0 && p.Name.Length != 0;
    }
    public static byte[] Encode(ChatPacket p)
    {
        if (!Valid(p)) throw new ArgumentException("Invalid chat packet.");
        var text = Utf8.GetBytes(p.Text); var name = Utf8.GetBytes(p.Name);
        if (text.Length > 800 || name.Length > 128) throw new ArgumentException("Oversized chat text.");
        var bytes = new byte[HeaderBytes + text.Length + name.Length];
        bytes[0] = (byte)'C'; bytes[1] = (byte)'B'; bytes[2] = (byte)'C'; bytes[3] = (byte)'H'; bytes[4] = 1; bytes[5] = (byte)p.Message;
        Write(bytes, 8, p.Epoch); Write(bytes, 16, p.Sequence); Write(bytes, 24, p.Nonce); Write(bytes, 32, p.Author); Write(bytes, 40, p.PlayerIndex);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(48), p.Created);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), (ushort)text.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), (ushort)name.Length);
        text.CopyTo(bytes, HeaderBytes); name.CopyTo(bytes, HeaderBytes + text.Length); return bytes;
    }
    public static bool Decode(ReadOnlySpan<byte> bytes, out ChatPacket p)
    {
        p = default;
        if (bytes.Length < HeaderBytes || bytes.Length > MaximumBytes || bytes[0] != 'C' || bytes[1] != 'B' || bytes[2] != 'C' ||
            bytes[3] != 'H' || bytes[4] != 1 || bytes[6] != 0 || bytes[7] != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[60..]) != 0) return false;
        var text = BinaryPrimitives.ReadUInt16LittleEndian(bytes[56..]); var name = BinaryPrimitives.ReadUInt16LittleEndian(bytes[58..]);
        if (text > 800 || name > 128 || bytes.Length != HeaderBytes + text + name) return false;
        try
        {
            p = new((ChatMessage)bytes[5], Utf8.GetString(bytes.Slice(HeaderBytes, text)), Read(bytes, 8), Read(bytes, 16), Read(bytes, 32),
                Read(bytes, 40), BinaryPrimitives.ReadDoubleLittleEndian(bytes[48..]), Read(bytes, 24), Utf8.GetString(bytes.Slice(HeaderBytes + text, name)));
            return Valid(p);
        }
        catch (DecoderFallbackException) { p = default; return false; }
    }
    private static void Write(byte[] data, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), value);
    private static ulong Read(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt64LittleEndian(data[at..]);
}

public sealed class ChatDraft
{
    private char _high;
    public string Text { get; private set; } = "";
    public void Input(char c)
    {
        if (char.IsHighSurrogate(c)) { _high = c; return; }
        string value;
        if (char.IsLowSurrogate(c)) { if (_high == 0) return; value = new string(new[] { _high, c }); }
        else value = c.ToString();
        _high = '\0'; var rune = Rune.GetRuneAt(value, 0);
        if (!ChatText.Allowed(rune) || Text.EnumerateRunes().Count() >= ChatText.MaximumCharacters) return;
        Text += value;
    }
    public void Backspace()
    {
        _high = '\0'; if (Text.Length == 0) return;
        var count = Text.Length > 1 && char.IsLowSurrogate(Text[^1]) && char.IsHighSurrogate(Text[^2]) ? 2 : 1;
        Text = Text[..^count];
    }
    public void Clear() { Text = ""; _high = '\0'; }
}

public sealed class ChatHistory
{
    public const int Capacity = 50;
    private readonly List<ChatPacket> _messages = new();
    public IReadOnlyList<ChatPacket> Messages => _messages;
    public void Add(ChatPacket packet)
    {
        if (packet.Message != ChatMessage.Show || !ChatProtocol.Valid(packet)) return;
        if (_messages.Count == Capacity) _messages.RemoveAt(0);
        _messages.Add(packet);
    }
    public void Clear() => _messages.Clear();
}
