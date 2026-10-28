using ChainedBeasts.Matchmaking.Core;
using Steamworks;
using Unity.Netcode;

namespace ChainedBeasts.Matchmaking;

internal sealed class HostModerationService : IHostModerationBackend
{
    internal HostModeration Policy { get; }
    private bool _errorLogged;
    private HostScope _lastHost;
    internal HostModerationService() { Policy = new(this); }

    public HostScope ReadScope()
    {
        if (!Plugin.HostToolsEnabled || !NetworkManagerEx.InstanceExists) return _lastHost = default;
        var ex = NetworkManagerEx.Instance;
        var manager = ex.Unity;
        if (manager == null || !manager.IsServer || !manager.IsListening || ex.isLeavingLobby) return _lastHost = default;
        var transport = ex.Transport?.TryCast<SteamTransportController>();
        if (transport == null || !transport.lobbyID.IsValid() || !transport.lobbyID.IsLobby()) return _lastHost = default;
        var self = SteamUser.GetSteamID().m_SteamID;
        if (self == 0 && _lastHost.Instance == manager.Pointer.ToInt64() && _lastHost.Lobby == transport.lobbyID.m_SteamID) self = _lastHost.HostAccount;
        if (self == 0) return default;
        return _lastHost = new(manager.Pointer.ToInt64(), transport.lobbyID.m_SteamID, self, manager.LocalClientId);
    }

    internal static bool Identity(NetworkManager manager, SteamTransportController transport, ulong client, out ulong account)
    {
        account = 0;
        // NGO client numbers change on reconnect. Bind them to the authenticated Steam socket,
        // not UGSConnectionPayload.playerID/displayName (which originate in a client's payload).
        if (manager?.ConnectionManager == null || transport?.transport?.connectionMapping == null) return false;
        var mapped = manager.ConnectionManager.ClientIdToTransportId(client);
        if (!mapped.Item2 || !transport.transport.connectionMapping.TryGetValue(mapped.Item1, out var connection) ||
            connection is null || !connection.id.IsValid() || connection.id.IsLobby()) return false;
        account = connection.id.m_SteamID;
        return account != 0;
    }

    public IReadOnlyList<HostPeer> ReadPeers()
    {
        var result = new List<HostPeer>();
        var scope = ReadScope();
        if (!scope.Active) return result;
        var manager = NetworkManagerEx.Instance.Unity;
        var transport = NetworkManagerEx.Instance.Transport.TryCast<SteamTransportController>();
        var ids = manager.ConnectedClientsIds;
        var count = ids.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ulong>>().Count;
        for (var i = 0; i < Math.Min(count, HostModeration.MaximumPeers); i++)
        {
            var client = ids[i];
            if (client == scope.LocalClient) continue;
            Identity(manager, transport, client, out var account);
            var name = "Player";
            if (transport.clientPayloads != null && transport.clientPayloads.TryGetValue(client, out var payload) && payload is not null)
                name = payload.displayName;
            var characters = 0;
            var roster = transport.connectedClients;
            if (roster != null)
                for (var j = 0; j < Math.Min(roster.Count, 16); j++) if (roster[j] is { } id && id.NetworkID == client) characters++;
            result.Add(new(client, account, name, characters));
        }
        return result;
    }

    public void Disconnect(HostScope expectedScope, ulong client, ulong account, string reason)
    {
        var scope = ReadScope();
        if (!scope.Active || scope != expectedScope || client == scope.LocalClient || account == scope.HostAccount)
            throw new InvalidOperationException("Host authority changed.");
        var manager = NetworkManagerEx.Instance.Unity;
        var transport = NetworkManagerEx.Instance.Transport.TryCast<SteamTransportController>();
        if (!Identity(manager, transport, client, out var current) || current != account) throw new InvalidOperationException("Player identity changed.");
        manager.DisconnectClient(client, reason);
        Plugin.Logger.LogInfo("[Host tools] host requested a native disconnect and blocked this Steam account for the lobby");
    }

    internal bool BeforeApproval(SteamTransportController controller, NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        if (!Plugin.HostToolsEnabled || request == null || response == null) return true;
        if (!NetworkManagerEx.InstanceExists || NetworkManagerEx.Instance.Unity?.IsServer != true) return true;
        bool known = false;
        try
        {
            if (!ReadScope().Active) return true;
            var manager = NetworkManagerEx.Instance.Unity;
            var liveTransport = NetworkManagerEx.Instance.Transport?.TryCast<SteamTransportController>();
            if (controller is null || liveTransport is null || controller.Pointer != liveTransport.Pointer) return true;
            if (request.ClientNetworkId == manager.LocalClientId) return true;
            if (!Policy.Refresh() || Policy.BlockedCount == 0) return true;
            known = Identity(manager, controller, request.ClientNetworkId, out var account);
            if (Policy.AllowAdmission(account, known)) return true;
        }
        catch (Exception e)
        {
            if (!_errorLogged) { _errorLogged = true; Plugin.Logger.LogWarning("[Host tools] identity verification unavailable: " + e); }
            if (Policy.BlockedCount == 0) return true;
        }
        response.Approved = false; response.Pending = false; response.CreatePlayerObject = false;
        response.Reason = known ? "The host has blocked this account from rejoining this lobby." :
            "The host could not verify your Steam identity while this lobby has rejoin blocks.";
        return false;
    }

    internal void Tick()
    {
        try { Policy.Tick(); }
        catch (Exception e)
        {
            // Do not erase active blocks on a transient identity/roster read failure.
            if (!_errorLogged) { _errorLogged = true; Plugin.Logger.LogWarning("[Host tools] refresh failed: " + e); }
        }
    }
}
