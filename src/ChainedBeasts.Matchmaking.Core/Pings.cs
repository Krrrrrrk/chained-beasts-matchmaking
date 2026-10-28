using System.Buffers.Binary;

namespace ChainedBeasts.Matchmaking.Core;

public enum PingKind : byte { None, Location, Thanks, Sorry }
public enum PingMessage : byte { Hello = 1, Welcome, Request, Show }

public readonly record struct PingPosition(float X, float Y, float Z)
{
    public bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) &&
        Math.Abs(X) <= 100000 && Math.Abs(Y) <= 100000 && Math.Abs(Z) <= 100000;
    public double DistanceSquared(PingPosition other) =>
        Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2) + Math.Pow(Z - other.Z, 2);
}

// Fixed, versioned binary payload. No remote text, type names, or native object pointers.
public readonly record struct PingPacket(PingMessage Message, PingKind Kind = PingKind.None,
    ulong Epoch = 0, ulong Sequence = 0, ulong Scene = 0, ulong SceneName = 0,
    ulong Author = 0, ulong PlayerIndex = 0, PingPosition Position = default, double Created = 0, ulong Nonce = 0);

public static class PingProtocol
{
    public const byte Version = 2;
    public const int Bytes = 84;
    public const double Cooldown = 1.5, Lifetime = 4, MaximumDistance = 100;
    public const int MaximumSenders = 8;
    public static string Label(PingKind kind) => kind switch
    { PingKind.Location => "Look here!", PingKind.Thanks => "Thanks!", PingKind.Sorry => "Sorry!", _ => "" };

    public static byte[] Encode(PingPacket packet)
    {
        if (!Valid(packet)) throw new ArgumentException("Invalid ping packet.");
        var data = new byte[Bytes];
        data[0] = (byte)'C'; data[1] = (byte)'B'; data[2] = (byte)'P'; data[3] = (byte)'G';
        data[4] = Version; data[5] = (byte)packet.Message; data[6] = (byte)packet.Kind;
        Write(data, 8, packet.Epoch); Write(data, 16, packet.Sequence); Write(data, 24, packet.Scene);
        Write(data, 32, packet.SceneName); Write(data, 40, packet.Author); Write(data, 48, packet.PlayerIndex);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(56), packet.Position.X);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(60), packet.Position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(64), packet.Position.Z);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(68), packet.Created);
        Write(data, 76, packet.Nonce);
        return data;
    }

    public static bool Decode(ReadOnlySpan<byte> data, out PingPacket packet)
    {
        packet = default;
        if (data.Length != Bytes || data[0] != 'C' || data[1] != 'B' || data[2] != 'P' || data[3] != 'G' ||
            data[4] != Version || data[7] != 0) return false;
        packet = new((PingMessage)data[5], (PingKind)data[6], Read(data, 8), Read(data, 16), Read(data, 24),
            Read(data, 32), Read(data, 40), Read(data, 48), new(
                BinaryPrimitives.ReadSingleLittleEndian(data[56..]), BinaryPrimitives.ReadSingleLittleEndian(data[60..]),
                BinaryPrimitives.ReadSingleLittleEndian(data[64..])), BinaryPrimitives.ReadDoubleLittleEndian(data[68..]), Read(data, 76));
        return Valid(packet);
    }

    private static bool Valid(PingPacket p)
    {
        if (p.Message == PingMessage.Hello || p.Message == PingMessage.Welcome)
            return p.Kind == PingKind.None && p.Sequence != 0 && p.Scene == 0 && p.SceneName == 0 &&
                p.Author == 0 && p.PlayerIndex == 0 && p.Position == default && p.Created == 0 && p.Nonce != 0 &&
                (p.Message == PingMessage.Hello ? p.Epoch == 0 : p.Epoch != 0);
        return (p.Message == PingMessage.Request || p.Message == PingMessage.Show) &&
            p.Kind is >= PingKind.Location and <= PingKind.Sorry && p.Epoch != 0 && p.Sequence != 0 &&
            p.Position.Valid && double.IsFinite(p.Created) && p.Created >= 0 &&
            (p.Message == PingMessage.Request ? p.Author == 0 && p.Nonce != 0 : p.Nonce == 0);
    }
    private static void Write(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
    private static ulong Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    public static ulong NameHash(string name)
    {
        ulong hash = 14695981039346656037;
        foreach (var c in name) { hash ^= c; hash = unchecked(hash * 1099511628211); }
        return hash;
    }

    // GUI coordinates: up=location, lower right=thanks, lower left=sorry.
    public static PingKind Sector(double x, double y, double deadZone)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x * x + y * y < deadZone * deadZone) return PingKind.None;
        var angle = Math.Atan2(x, -y);
        return Math.Abs(angle) <= Math.PI / 3 ? PingKind.Location : x >= 0 ? PingKind.Thanks : PingKind.Sorry;
    }

    public static bool InRange(PingPosition point, PingPosition player) => point.Valid && player.Valid &&
        point.DistanceSquared(player) <= MaximumDistance * MaximumDistance;

    public static double Remaining(PingPacket p, double now) =>
        !double.IsFinite(now) || now < 0 || !double.IsFinite(p.Created) || p.Created < 0 || now < p.Created - 1
            ? 0 : Math.Clamp(p.Created + Lifetime - now, 0, Lifetime);

    public static bool CanRelay(PingPacket p, ulong epoch, ulong scene, ulong name, bool ownsPlayer, PingPosition player, double now) =>
        Valid(p) && p.Message == PingMessage.Request && epoch != 0 && p.Epoch == epoch && p.Scene == scene &&
        p.SceneName == name && ownsPlayer && Remaining(p, now) > 0 && (p.Kind != PingKind.Location || InRange(p.Position, player));

    public static bool CanShow(PingPacket p, bool fromServer, ulong epoch, ulong scene, ulong name, bool hasPlayer, double now) =>
        Valid(p) && p.Message == PingMessage.Show && fromServer && epoch != 0 && p.Epoch == epoch &&
        p.Scene == scene && p.SceneName == name && hasPlayer && Remaining(p, now) > 0;
}

/// <summary>Per-sender duplicate and spam guard, retained through scene changes, reset on session exit.</summary>
public sealed class PingGate
{
    private readonly Dictionary<ulong, (ulong Sequence, double At)> _seen = new();
    public bool Accept(ulong sender, ulong sequence, double now, bool rateLimit = true)
    {
        if (sequence == 0 || !double.IsFinite(now)) return false;
        if (_seen.TryGetValue(sender, out var previous))
        { if (sequence <= previous.Sequence || rateLimit && now - previous.At < PingProtocol.Cooldown) return false; }
        else if (_seen.Count >= PingProtocol.MaximumSenders) return false;
        _seen[sender] = (sequence, now);
        return true;
    }
    public void Retain(IEnumerable<ulong> live)
    {
        var ids = new HashSet<ulong>(live);
        foreach (var id in _seen.Keys.Where(id => !ids.Contains(id)).ToArray()) _seen.Remove(id);
    }
    public void Clear() => _seen.Clear();
    public void Forget(ulong sender) => _seen.Remove(sender);
}
