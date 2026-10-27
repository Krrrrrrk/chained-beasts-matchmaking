using System.Globalization;

namespace ChainedBeasts.Matchmaking.Core;

/// <summary>Only directory data available before joining. Intentionally has no member/owner lookup.</summary>
public interface ILobbyDirectoryEntry
{
    ulong Id { get; }
    int Members { get; }
    int MemberLimit { get; }
    string Data(string key);
}

public static class LobbyDirectoryReader
{
    public static Lobby Read(ILobbyDirectoryEntry entry)
    {
        var name = entry.Data(LobbyPolicy.NameKey);
        if (string.IsNullOrWhiteSpace(name)) name = entry.Data("name");
        var title = string.IsNullOrWhiteSpace(name) ? "Lobby " + entry.Id.ToString(CultureInfo.InvariantCulture) : LobbyPolicy.Display(name);
        // The owner is unavailable outside membership. Never fabricate an owner from metadata.
        return new(entry.Id, 0, title, entry.Data("build"), entry.Data("transport_type"),
            entry.Members, entry.MemberLimit, entry.Data(LobbyPolicy.ProtocolKey),
            entry.Data(LobbyPolicy.FingerprintKey), entry.Data(LobbyPolicy.OwnerKey), entry.Data(LobbyPolicy.StateKey));
    }
}
