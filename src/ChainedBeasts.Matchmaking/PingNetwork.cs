using System.Security.Cryptography;
using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Allocator = Unity.Collections.Allocator;

namespace ChainedBeasts.Matchmaking;

/// <summary>Optional cosmetic host relay. Never gates admission, writes run data, or disconnects players.</summary>
internal sealed class PingNetwork : IDisposable
{
    private const string Channel = "cbmm.pings.v2", AdvertKey = "cbmm_ping_host_v2";
    private const double ProbeInterval = 4, LeaseLifetime = 12;
    private sealed class Peer
    {
        internal ulong Nonce;
        internal double LastHello, Until;
    }
    private readonly Queue<(ulong Sender, PingPacket Packet)> _pending = new();
    private readonly Dictionary<ulong, Peer> _peers = new();
    private readonly PingGate _requests = new(), _displayed = new();
    private readonly HashSet<string> _sendErrors = new();
    private NetworkManager _manager;
    private IntPtr _session;
    private CustomMessagingManager.HandleNamedMessageDelegate _handler;
    private ulong _epoch, _sequence, _relaySequence, _ownNonce, _helloSequence, _pendingHello, _advertisedLobby;
    private double _helloAt, _maintenanceAt, _lastWelcome;
    private Exception _receiveFailure;
    private bool _disposed, _faulted;
    internal bool Shared => !_disposed && _manager != null &&
        (_manager.IsServer || _epoch != 0 && Time.realtimeSinceStartupAsDouble - _lastWelcome < LeaseLifetime);
    internal string Status => _faulted ? "Relay unavailable - local preview only" : Shared ?
        _manager.IsServer ? ActivePeers().Length == 0 ? "Local preview - no updated peers yet" :
            $"Shared with {ActivePeers().Length} updated peer(s)" : "Shared through the host" :
        "Local preview - the host needs the ping update";
    internal event Action<PingPacket, double> Received;
    internal event Action Cleared;

    private static ulong Nonce() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;

    internal void Tick()
    {
        if (_disposed || _faulted) return;
        try
        {
            // Never unregister a native delegate from inside its own callback stack.
            if (_receiveFailure != null) throw new InvalidOperationException("Ping receive failed.", _receiveFailure);
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !NetworkSession.InstanceExists ||
                !NetworkSession.Instance.IsSpawned || !GameManager.InstanceExists || GameManager.Instance.State == GameManager.eState.MENU)
            { if (_manager != null) Reset(); return; }
            var transport = NetworkManagerEx.InstanceExists ? NetworkManagerEx.Instance.Transport?.TryCast<SteamTransportController>() : null;
            var lobby = transport?.lobbyID ?? default;
            if (!lobby.IsValid() || !lobby.IsLobby()) { if (_manager != null) Reset(); return; }
            if (_manager?.Pointer != manager.Pointer || _session != NetworkSession.Instance.Pointer)
            {
                Reset(); _manager = manager; _session = NetworkSession.Instance.Pointer; _ownNonce = Nonce();
                if (manager.CustomMessagingManager == null) { Reset(); return; }
                _handler = (Action<ulong, FastBufferReader>)Receive;
                manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, _handler);
                if (manager.IsServer) _epoch = Nonce();
                Plugin.Logger.LogInfo("[Pings] protocol 2 relay registered (" + (manager.IsServer ? "host" : "client") + ")");
            }
            var now = Time.realtimeSinceStartupAsDouble;
            while (_pending.Count > 0)
            { var item = _pending.Dequeue(); Process(item.Sender, item.Packet, now); }
            if (now < _maintenanceAt) return;
            _maintenanceAt = now + 1;
            if (manager.IsServer)
            {
                if (_advertisedLobby != lobby.m_SteamID && SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == SteamUser.GetSteamID().m_SteamID &&
                    SteamMatchmaking.SetLobbyData(lobby, AdvertKey, SteamUser.GetSteamID().m_SteamID.ToString()))
                    _advertisedLobby = lobby.m_SteamID;
                var live = ConnectedIds(); live.Add(manager.LocalClientId);
                foreach (var id in _peers.Keys.Where(id => !live.Contains(id)).ToArray()) _peers.Remove(id);
                _requests.Retain(live);
            }
            else
            {
                var owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
                var advertised = owner != 0 && SteamMatchmaking.GetLobbyData(lobby, AdvertKey) == owner.ToString();
                if (!advertised || now - _lastWelcome >= LeaseLifetime)
                { if (_epoch != 0) { _epoch = 0; _displayed.Clear(); Cleared?.Invoke(); } }
                if (!advertised) return; // No unregistered custom messages to ordinary vanilla hosts.
                if (now >= _helloAt)
                {
                    _helloAt = now + (_epoch == 0 ? 2 : ProbeInterval);
                    _pendingHello = ++_helloSequence;
                    Send(NetworkManager.ServerClientId, new(PingMessage.Hello, Sequence: _pendingHello, Nonce: _ownNonce));
                }
                if (PingWorld.Ready(out _)) _displayed.Retain(PingWorld.Networks());
            }
        }
        catch (Exception e) { Fail(e); }
    }

    // Copy exactly one bounded payload. All game-object reads/changes to subscriptions occur in Update.
    private void Receive(ulong sender, FastBufferReader reader)
    {
        if (_disposed || _faulted || _pending.Count >= 32) return;
        try
        {
            if (reader.Length - reader.Position != PingProtocol.Bytes || !reader.TryBeginRead(PingProtocol.Bytes)) return;
            var data = new Il2CppStructArray<byte>(PingProtocol.Bytes);
            reader.ReadBytesSafe(ref data, PingProtocol.Bytes);
            if (PingProtocol.Decode(data.ToArray(), out var packet)) _pending.Enqueue((sender, packet));
        }
        catch (Exception e) { _receiveFailure ??= e; }
    }

    private HashSet<ulong> ConnectedIds()
    {
        var live = new HashSet<ulong>();
        var ids = _manager.ConnectedClientsIds;
        var count = ids.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ulong>>().Count;
        for (var i = 0; i < count; i++) live.Add(ids[i]);
        return live;
    }
    private ulong[] ActivePeers()
    {
        var now = Time.realtimeSinceStartupAsDouble;
        return _peers.Where(pair => pair.Value.Until > now).Select(pair => pair.Key).ToArray();
    }

    private void Process(ulong sender, PingPacket packet, double now)
    {
        if (_manager.IsServer)
        {
            if (sender == _manager.LocalClientId || !ConnectedIds().Contains(sender)) return;
            if (packet.Message == PingMessage.Hello)
            {
                if (_peers.TryGetValue(sender, out var old) && now - old.LastHello < 1) return;
                if (old == null && _peers.Count >= PingProtocol.MaximumSenders) return;
                if (old == null || old.Nonce != packet.Nonce) _requests.Forget(sender);
                _peers[sender] = new() { Nonce = packet.Nonce, LastHello = now, Until = now + LeaseLifetime };
                Send(sender, new(PingMessage.Welcome, Epoch: _epoch, Sequence: packet.Sequence, Nonce: packet.Nonce));
                return;
            }
            if (packet.Message == PingMessage.Request && _peers.TryGetValue(sender, out var peer) &&
                peer.Until > now && peer.Nonce == packet.Nonce) Relay(sender, packet, now);
        }
        else if (sender == NetworkManager.ServerClientId)
        {
            if (packet.Message == PingMessage.Welcome)
            {
                if (packet.Nonce != _ownNonce || packet.Sequence != _pendingHello || _pendingHello == 0) return;
                _pendingHello = 0; _lastWelcome = now;
                if (_epoch != packet.Epoch)
                {
                    _epoch = packet.Epoch; _displayed.Clear(); Cleared?.Invoke();
                    Plugin.Logger.LogInfo("[Pings] host relay confirmed");
                }
                return;
            }
            if (!Shared || !PingWorld.Ready(out var session) || !PingProtocol.CanShow(packet, true, _epoch,
                session.sceneRoot.NetworkObjectId, PingWorld.SceneName(session),
                PingWorld.Find(packet.Author, packet.PlayerIndex) != null, _manager.ServerTime.Time)) return;
            if (_displayed.Accept(packet.Author, packet.Sequence, now, rateLimit: false))
                Received?.Invoke(packet, PingProtocol.Remaining(packet, _manager.ServerTime.Time));
        }
    }

    private bool Relay(ulong sender, PingPacket request, double now)
    {
        if (!PingWorld.Ready(out var session)) return false;
        var player = PingWorld.Find(sender, request.PlayerIndex);
        if (player == null || !PingWorld.Position(player).Valid) return false;
        if (!PingProtocol.CanRelay(request, _epoch, session.sceneRoot.NetworkObjectId,
            PingWorld.SceneName(session), true, PingWorld.Position(player), _manager.ServerTime.Time) ||
            !_requests.Accept(sender, request.Sequence, request.Created)) return false;
        var result = request with { Message = PingMessage.Show, Author = sender, Nonce = 0, Sequence = ++_relaySequence,
            Position = request.Kind == PingKind.Location ? request.Position : PingWorld.Position(player) };
        Received?.Invoke(result, PingProtocol.Remaining(result, _manager.ServerTime.Time));
        foreach (var peer in ActivePeers()) Send(peer, result);
        return true;
    }

    internal bool Submit(PingKind kind, PingPosition position)
    {
        if (_disposed || PingWorld.Local() is not { } player || !PingWorld.Ready(out var session) ||
            kind is < PingKind.Location or > PingKind.Sorry) return false;
        var id = player.clientID.Value;
        if (!position.Valid || !PingWorld.Position(player).Valid ||
            kind == PingKind.Location && !PingProtocol.InRange(position, PingWorld.Position(player))) return false;
        try
        {
            var packet = new PingPacket(PingMessage.Request, kind, _epoch == 0 ? 1 : _epoch, ++_sequence,
                session.sceneRoot.NetworkObjectId, PingWorld.SceneName(session), 0, id.PlayerIndex, position,
                _manager == null ? 0 : Math.Max(0, _manager.ServerTime.Time), _ownNonce == 0 ? 1 : _ownNonce);
            if (Shared)
                return _manager.IsServer ? Relay(_manager.LocalClientId, packet, Time.realtimeSinceStartupAsDouble) :
                    Send(NetworkManager.ServerClientId, packet);
            Received?.Invoke(packet with { Message = PingMessage.Show, Author = id.NetworkID, Nonce = 0 }, PingProtocol.Lifetime);
            return true;
        }
        catch (Exception e) { Fail(e); return false; }
    }

    private bool Send(ulong client, PingPacket packet)
    {
        if (_manager == null || !_manager.IsListening) return false;
        if (_manager.IsServer && !ConnectedIds().Contains(client)) { _peers.Remove(client); return false; }
        var data = PingProtocol.Encode(packet);
        var writer = new FastBufferWriter(data.Length, Allocator.Temp, data.Length);
        try
        {
            writer.WriteBytesSafe((Il2CppStructArray<byte>)data);
            try { _manager.CustomMessagingManager.SendNamedMessage(Channel, client, writer, NetworkDelivery.ReliableSequenced); }
            catch (Exception e)
            {
                // One disappearing peer must not silence the entire party or affect native admission.
                if (_manager.IsServer && _peers.TryGetValue(client, out var peer)) peer.Until = 0;
                var key = e.GetType().Name + ": " + e.Message;
                if (_sendErrors.Count < 4 && _sendErrors.Add(key)) Plugin.Logger.LogWarning("[Pings] message not delivered; relay will retry negotiation: " + key);
                return false;
            }
            return true;
        }
        finally { writer.Dispose(); }
    }

    private void Fail(Exception e)
    {
        if (_faulted) return;
        Reset(); _faulted = true;
        Plugin.Logger.LogWarning("[Pings] relay disabled; local preview remains available: " + e);
    }
    private void Reset()
    {
        try { _manager?.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel); } catch { }
        try
        {
            if (_advertisedLobby != 0 && SteamMatchmaking.GetLobbyOwner(new(_advertisedLobby)).m_SteamID == SteamUser.GetSteamID().m_SteamID)
                SteamMatchmaking.DeleteLobbyData(new(_advertisedLobby), AdvertKey);
        }
        catch { }
        _manager = null; _handler = null; _session = IntPtr.Zero;
        _epoch = _advertisedLobby = _relaySequence = _ownNonce = _helloSequence = _pendingHello = 0;
        _peers.Clear(); _pending.Clear(); _requests.Clear(); _displayed.Clear(); _sendErrors.Clear();
        _helloAt = _maintenanceAt = _lastWelcome = 0; _receiveFailure = null; Cleared?.Invoke();
    }
    public void Dispose() { Reset(); _disposed = true; }
}
