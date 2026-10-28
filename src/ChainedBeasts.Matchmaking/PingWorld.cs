using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

namespace ChainedBeasts.Matchmaking;

internal static class PingWorld
{
    internal static bool NativeMenuOwnsCursor => HostToolsController.IsOpen || GameManager.InstanceExists && GameManager.Instance.State == GameManager.eState.MENU ||
        NetworkSession.InstanceExists && NetworkSession.Instance.IsPaused;
    internal static bool Ready(out NetworkSession session)
    {
        session = null;
        if (!GameManager.InstanceExists || GameManager.Instance.State == GameManager.eState.MENU ||
            !NetworkManagerEx.InstanceExists || NetworkManagerEx.Instance.SceneOperationInProgress ||
            !NetworkSession.InstanceExists || !PlayerManager.InstanceExists) return false;
        var current = NetworkSession.Instance;
        if (current == null || !current.IsSpawned || current.TransitionPending || current.isRestoringFromCheckpoint ||
            current.isRestarting || current.sceneRoot == null || !current.sceneRoot.IsSpawned || !current.sceneRoot.AllowUpdates ||
            current.runDataManager?.playerDatas == null || PlayerManager.Instance.players == null) return false;
        session = current;
        return true;
    }

    internal static Player Find(ulong network, ulong index)
    {
        if (!Ready(out var session)) return null;
        var players = PlayerManager.Instance.players;
        var roster = session.runDataManager.playerDatas;
        // Inspect only current live bindings; avoid PlayerData.PlayerObject and PlayerManager.PlayerCount.
        for (var i = 0; i < Math.Min(players.Count, 8); i++)
        {
            var player = players[i];
            if (player == null || !player.IsSpawned || !player.gameObject.activeInHierarchy) continue;
            var client = player.clientID?.Value;
            var data = player.playerData;
            if (client is null || data == null || data.playerID < 0 || data.playerID >= 8 ||
                client.NetworkID != network || client.PlayerIndex != index ||
                client.PlayerIndex != (ulong)data.playerID) continue;
            for (var j = 0; j < Math.Min(roster.Count, 8); j++)
                if (roster[j]?.Pointer == data.Pointer) return player;
        }
        return null;
    }

    internal static Player Local()
    {
        if (!Ready(out var session) || session.IsPaused) return null;
        var client = session.primaryIdentifier;
        var network = NetworkManagerEx.Instance.Unity;
        return client is not null && network != null && client.NetworkID == network.LocalClientId
            ? Find(client.NetworkID, client.PlayerIndex) : null;
    }

    internal static PingPosition Position(Player player)
    { var p = player.transform.position; return new(p.x, p.y, p.z); }
    internal static bool HeadPosition(Player player, out Vector3 position)
    {
        var model = player.agentModel;
        var head = model != null ? model.headTransform : null;
        position = head != null ? head.position + new Vector3(0, .55f, 0) :
            player.transform.position + new Vector3(0, 2.7f, 0);
        return new PingPosition(position.x, position.y, position.z).Valid;
    }
    internal static ulong SceneName(NetworkSession session) => PingProtocol.NameHash(session.sceneRoot.sceneName ?? "");

    internal static ulong[] Networks()
    {
        if (!Ready(out _)) return Array.Empty<ulong>();
        var result = new HashSet<ulong>();
        var players = PlayerManager.Instance.players;
        for (var i = 0; i < Math.Min(players.Count, 8); i++)
        {
            var player = players[i];
            if (player != null && player.IsSpawned && player.clientID?.Value is { } client) result.Add(client.NetworkID);
        }
        return result.ToArray();
    }
}
