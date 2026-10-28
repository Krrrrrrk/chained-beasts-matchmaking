using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;

static class SoloChatChecks
{
    internal static void Run(Action<bool, string> check)
    {
        InertBus.Reset();
        var node = InertBus.Add(0, true, false); InertBus.Roster(0); node.Activate();
        ((SteamTransportController)node.Ex.Transport).lobbyID = new(0);
        node.Manager.IsListening = false;
        using var chat = new ChatNetwork();
        var messages = new List<ChatPacket>(); var clears = 0;
        chat.Received += messages.Add; chat.Cleared += () => clears++;
        void Frame(GameManager.eState mode = GameManager.eState.SERVER_SINGLE_PLAYER)
        { node.Activate(); GameManager.Instance.State = mode; chat.Tick(); }
        Frame();
        check(!chat.Shared && chat.Status.Contains("Solo preview"), "Native Solo mode is labelled local preview, never shared chat.");
        check(chat.Submit("hello badword") && messages.Count == 1 && messages[0].Text == "hello *******" && ChatProtocol.Valid(messages[0]),
            "Solo previews use the actual chat adapter, bounded protocol and filter without a Steam lobby.");
        check(!chat.Submit("too soon") && messages.Count == 1, "Solo preview preserves the message cooldown.");
        InertBus.Now += 2; Frame();
        Crosstales.BWF.BWFManager.instance.isReady = false;
        check(!chat.Submit("badword") && messages.Count == 1, "Solo never echoes raw text while the native filter is unavailable.");
        Crosstales.BWF.BWFManager.instance.isReady = true;
        node.Players.players[0].clientID.Value.DisplayName = "badword name";
        check(chat.Submit("ready") && messages[^1].Name == "******* name", "Solo preview also filters the owned character's display name.");
        check(node.Manager.CustomMessagingManager.Handlers.Count == 0 && InertBus.History.Count == 0 &&
            !Steamworks.SteamMatchmaking.Data.Keys.Any(k => k.Contains("chat")),
            "Solo does not register a relay, advertise chat, or send network traffic.");
        var previousClears = clears;
        node.Session.Pointer = new IntPtr(9999); Frame();
        check(clears == previousClears + 1 && chat.Submit("new run"), "A fresh Solo session clears history and rate bookkeeping.");
        Frame(GameManager.eState.SERVER_LOCAL);
        check(!chat.Shared && !chat.Status.Contains("Solo preview") && !chat.Submit("not solo"),
            "A multiplayer host with a missing lobby never falls back to a fake successful Solo send.");
        Frame(GameManager.eState.MENU);
        check(!chat.Submit("menu"), "Returning to the menu clears preview eligibility.");
        check(InertBus.DisconnectCalls == 0, "Solo chat never starts, stops or disconnects a session.");
    }
}
