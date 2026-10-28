using System.Globalization;

namespace ChainedBeasts.Matchmaking.Core;

/// <summary>Discovery metadata only; native admission remains authoritative.</summary>
public static class DiscoveryRules
{
    public static Dictionary<string, string> StringFilters(SearchOptions options, string build, bool requireProtocol = true)
    {
        var result = new Dictionary<string, string> { ["transport_type"] = "steam" };
        if (!options.OtherBuilds) result["build"] = build;
        if (requireProtocol) result[LobbyPolicy.ProtocolKey] = "1";
        return result;
    }

    public static bool Visible(Lobby lobby, bool requireProtocol = true) => !requireProtocol || lobby.Protocol == "1";

    public static Dictionary<string, string> Advertisement(ulong owner, string name, bool waiting, string? fingerprint)
    {
        var result = new Dictionary<string, string>
        {
            [LobbyPolicy.ProtocolKey] = "1",
            [LobbyPolicy.OwnerKey] = owner.ToString(CultureInfo.InvariantCulture),
            [LobbyPolicy.NameKey] = LobbyPolicy.Display(name),
            [LobbyPolicy.StateKey] = waiting ? "waiting" : "closed"
        };
        if (fingerprint != null) result[LobbyPolicy.FingerprintKey] = fingerprint;
        return result;
    }
}
