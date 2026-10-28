using ChainedBeasts.Matchmaking.Core;

static class DiscoveryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var query = DiscoveryRules.StringFilters(new(), "1.0.2");
        check(query[LobbyPolicy.ProtocolKey] == "1" && query["transport_type"] == "steam" && query["build"] == "1.0.2",
            "Default discovery requires the matchmaking marker, Steam transport and current build.");
        query = DiscoveryRules.StringFilters(new(true, true), "1.0.2");
        check(query[LobbyPolicy.ProtocolKey] == "1" && !query.ContainsKey("build"),
            "Worldwide and other-build display retain the matchmaking requirement.");
        var lobby = new Lobby(42, 0, "Test", "1.0.2", "steam", 1, 4);
        check(!DiscoveryRules.Visible(lobby) && !DiscoveryRules.Visible(lobby with { Protocol = "2" }),
            "Unmarked and unknown-protocol results cannot leak into the default list.");
        check(DiscoveryRules.Visible(lobby with { Protocol = "1" }), "Advertised matchmaking hosts remain visible.");
        var refreshed = lobby with { Protocol = "1" };
        check(!DiscoveryRules.Visible(refreshed with { Protocol = "" }),
            "The production revalidation predicate rejects a removed marker before joining.");
        var tags = DiscoveryRules.Advertisement(123, "<host>\n", true, null);
        check(tags[LobbyPolicy.ProtocolKey] == "1" && tags[LobbyPolicy.OwnerKey] == "123" &&
            tags[LobbyPolicy.StateKey] == "waiting" && tags[LobbyPolicy.NameKey] == "host" && !tags.ContainsKey(LobbyPolicy.FingerprintKey),
            "Opting out of the optional digest retains sanitized host presence and waiting state.");
        tags = DiscoveryRules.Advertisement(123, "host", false, "unknown");
        check(tags[LobbyPolicy.StateKey] == "closed" && tags[LobbyPolicy.FingerprintKey] == "unknown",
            "Closed hosts remain closed and optional compatibility metadata is explicit.");
    }
}
