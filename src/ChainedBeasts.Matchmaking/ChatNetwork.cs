using System.Security.Cryptography;
using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Allocator = Unity.Collections.Allocator;

namespace ChainedBeasts.Matchmaking;

/// <summary>Optional bounded party-chat relay. Never gates admission, writes run data, or disconnects players.</summary>
internal sealed class ChatNetwork : IDisposable
{
    private const string Channel = "cbmm.chat.v1", AdvertKey = "cbmm_chat_host_v1";
    private const double ProbeInterval = 4, LeaseLifetime = 12;
    private sealed class Peer
    {
        internal ulong Nonce;
        internal double LastHello, Until;
    }
    private readonly Queue<(ulong Sender, ChatPacket Packet)> _pending = new();
    private readonly Dictionary<ulong, Peer> _peers = new();
    private readonly PingGate _requests = new();
    private readonly ChatProfanity _filter = new();
    private ulong _displaySequence;
    private readonly HashSet<string> _sendErrors = new();
    private NetworkManager _manager;
    private IntPtr _session;
    private CustomMessagingManager.HandleNamedMessageDelegate _handler;
    private ulong _epoch, _sequence, _relaySequence, _ownNonce, _helloSequence, _pendingHello, _advertisedLobby;
    private double _helloAt, _maintenanceAt, _lastWelcome;
    private Exception _receiveFailure;
    private bool _disposed, _faulted, _solo;
    internal bool Shared => !_disposed && !_solo && _manager != null &&
        (_manager.IsServer || _epoch != 0 && Time.realtimeSinceStartupAsDouble - _lastWelcome < LeaseLifetime);
    internal string Status => !_filter.Ready ? "Game chat filter loading/unavailable - text withheld" : _faulted ? "Chat relay unavailable" : _solo ? "Solo preview - only you can see these messages" : Shared ?
        _manager.IsServer ? ActivePeers().Length == 0 ? "Chat ready - no other chat users yet" :
            $"Shared with {ActivePeers().Length} updated peer(s)" : "Shared through the host" :
        "Chat unavailable - the host needs the chat update";
    internal event Action<ChatPacket> Received;
    internal event Action Cleared;

    private static ulong Nonce() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;

    internal void Tick()
    {
        if (_disposed || _faulted) return;
        try
        {
            // Never unregister a native delegate from inside its own callback stack.
            if (_receiveFailure != null) throw new InvalidOperationException("Chat receive failed.", _receiveFailure);
            var manager = NetworkManager.Singleton;
            if (GameManager.InstanceExists && GameManager.Instance.State == GameManager.eState.SERVER_SINGLE_PLAYER &&
                NetworkSession.InstanceExists && NetworkSession.Instance.IsSpawned)
            {
                if (!_solo || _session != NetworkSession.Instance.Pointer)
                { Reset(); _solo = true; _session = NetworkSession.Instance.Pointer; _epoch = Nonce(); }
                return;
            }
            if (_solo) Reset();
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
                Plugin.Logger.LogInfo("[Chat] protocol 1 relay registered (" + (manager.IsServer ? "host" : "client") + ")");
            }
            var now = Time.realtimeSinceStartupAsDouble;
            var queued = _pending.Count;
            while (queued-- > 0)
            {
                var item = _pending.Dequeue();
                if (item.Packet.Message is ChatMessage.Request or ChatMessage.Show &&
                    ChatProtocol.Remaining(item.Packet, manager.ServerTime.Time) > 0 && !PingWorld.Ready(out _))
                { _pending.Enqueue(item); continue; }
                Process(item.Sender, item.Packet, now);
            }
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
                { if (_epoch != 0) { _epoch = 0; _displaySequence = 0; Cleared?.Invoke(); } }
                if (!advertised) return; // No unregistered custom messages to ordinary vanilla hosts.
                if (now >= _helloAt)
                {
                    _helloAt = now + (_epoch == 0 ? 2 : ProbeInterval);
                    _pendingHello = ++_helloSequence;
                    Send(NetworkManager.ServerClientId, new(ChatMessage.Hello, Sequence: _pendingHello, Nonce: _ownNonce));
                }

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
            var length = reader.Length - reader.Position;
            if (length < ChatProtocol.HeaderBytes || length > ChatProtocol.MaximumBytes || !reader.TryBeginRead(length)) return;
            var data = new Il2CppStructArray<byte>(length);
            reader.ReadBytesSafe(ref data, length);
            if (ChatProtocol.Decode(data.ToArray(), out var packet)) _pending.Enqueue((sender, packet));
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

    private void Process(ulong sender, ChatPacket packet, double now)
    {
        if (_manager.IsServer)
        {
            if (sender == _manager.LocalClientId || !ConnectedIds().Contains(sender)) return;
            if (packet.Message == ChatMessage.Hello)
            {
                if (_peers.TryGetValue(sender, out var old) && now - old.LastHello < 1) return;
                if (old == null && _peers.Count >= ChatProtocol.MaximumSenders) return;
                if (old == null || old.Nonce != packet.Nonce) _requests.Forget(sender);
                _peers[sender] = new() { Nonce = packet.Nonce, LastHello = now, Until = now + LeaseLifetime };
                Send(sender, new(ChatMessage.Welcome, Epoch: _epoch, Sequence: packet.Sequence, Nonce: packet.Nonce));
                return;
            }
            if (packet.Message == ChatMessage.Request && _peers.TryGetValue(sender, out var peer) &&
                peer.Until > now && peer.Nonce == packet.Nonce) Relay(sender, packet, now);
        }
        else if (sender == NetworkManager.ServerClientId)
        {
            if (packet.Message == ChatMessage.Welcome)
            {
                if (packet.Nonce != _ownNonce || packet.Sequence != _pendingHello || _pendingHello == 0) return;
                _pendingHello = 0; _lastWelcome = now;
                if (_epoch != packet.Epoch)
                {
                    _epoch = packet.Epoch; _displaySequence = 0; Cleared?.Invoke();
                    Plugin.Logger.LogInfo("[Chat] host relay confirmed");
                }
                return;
            }
            if (!Shared || packet.Message != ChatMessage.Show || packet.Epoch != _epoch ||
                ChatProtocol.Remaining(packet, _manager.ServerTime.Time) <= 0) return;
            if (packet.Sequence > _displaySequence)
            {
                _displaySequence = packet.Sequence;
                if (_filter.TryPacket(packet, out var filtered)) Received?.Invoke(filtered);
            }
        }
    }

    private bool Relay(ulong sender, ChatPacket request, double now)
    {
        if (!PingWorld.Ready(out var session)) return false;
        var player = PingWorld.Find(sender, request.PlayerIndex);
        if (player == null || !PingWorld.Position(player).Valid) return false;
        if (request.Epoch != _epoch || ChatProtocol.Remaining(request, _manager.ServerTime.Time) <= 0 ||
            !_requests.Accept(sender, request.Sequence, request.Created)) return false;
        var name = ChatText.Clean(player.clientID.Value.DisplayName, ChatText.MaximumName);
        if (name.Length == 0) name = "Player " + (request.PlayerIndex + 1);
        var result = request with { Message = ChatMessage.Show, Author = sender, Nonce = 0, Sequence = ++_relaySequence, Name = name };
        if (!_filter.TryPacket(result, out var filtered)) return false;
        result = filtered;
        Received?.Invoke(result);
        foreach (var peer in ActivePeers()) Send(peer, result);
        return true;
    }

    internal bool Submit(string text)
    {
        text = ChatText.Clean(text);
        if (_disposed || _faulted || !(Shared || _solo) || text.Length == 0 || PingWorld.Local() is not { } player) return false;
        if (!_filter.TryText(text, ChatText.MaximumCharacters, out var filtered)) return false;
        text = filtered;
        var id = player.clientID.Value;
        try
        {
            if (_solo)
            {
                if (!GameManager.InstanceExists || GameManager.Instance.State != GameManager.eState.SERVER_SINGLE_PLAYER ||
                    !NetworkSession.InstanceExists || NetworkSession.Instance.Pointer != _session) return false;
                var now = Time.realtimeSinceStartupAsDouble;
                var name = ChatText.Clean(id.DisplayName, ChatText.MaximumName);
                if (name.Length == 0) name = "Player " + (id.PlayerIndex + 1);
                var preview = new ChatPacket(ChatMessage.Show, text, _epoch, ++_sequence, id.NetworkID, id.PlayerIndex,
                    Math.Max(0, now), Name: name);
                if (!_filter.TryPacket(preview, out var result) || !_requests.Accept(id.NetworkID, preview.Sequence, preview.Created)) return false;
                Received?.Invoke(result);
                return true;
            }
            var packet = new ChatPacket(ChatMessage.Request, text, _epoch, ++_sequence, 0, id.PlayerIndex,
                Math.Max(0, _manager.ServerTime.Time), _ownNonce);
            return _manager.IsServer ? Relay(_manager.LocalClientId, packet, Time.realtimeSinceStartupAsDouble) :
                Send(NetworkManager.ServerClientId, packet);
        }
        catch (Exception e) { Fail(e); return false; }
    }

    private bool Send(ulong client, ChatPacket packet)
    {
        if (_manager == null || !_manager.IsListening) return false;
        if (_manager.IsServer && !ConnectedIds().Contains(client)) { _peers.Remove(client); return false; }
        var data = ChatProtocol.Encode(packet);
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
                if (_sendErrors.Count < 4 && _sendErrors.Add(key)) Plugin.Logger.LogWarning("[Chat] message not delivered; relay will retry negotiation: " + key);
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
        Plugin.Logger.LogWarning("[Chat] relay disabled; chat is unavailable: " + e);
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
        _filter.Reset(); _solo = false;
        _manager = null; _handler = null; _session = IntPtr.Zero;
        _epoch = _advertisedLobby = _relaySequence = _ownNonce = _helloSequence = _pendingHello = 0;
        _peers.Clear(); _pending.Clear(); _requests.Clear(); _displaySequence = 0; _sendErrors.Clear();
        _helloAt = _maintenanceAt = _lastWelcome = 0; _receiveFailure = null; Cleared?.Invoke();
    }
    public void Dispose() { Reset(); _disposed = true; }
}
