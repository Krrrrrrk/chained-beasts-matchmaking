// Deliberately inert API substitutes. These tests load no Unity/Steam/IL2CPP DLLs.
// The production relay/world adapter is linked into this process unchanged.
using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;
using UnityEngine;

internal sealed class Node
{
    internal readonly ulong Id;
    internal readonly NetworkManager Manager;
    internal readonly NetworkManagerEx Ex;
    internal readonly NetworkSession Session;
    internal readonly PlayerManager Players = new();
    internal PingNetwork Pings;
    internal readonly List<PingPacket> Shown = new();
    internal readonly List<double> Lifetimes = new();
    internal readonly InertLogger Log = new();
    internal bool Online = true;
    internal bool PingTicksEnabled = true;
    internal Node(ulong id, bool host, bool updated = true)
    {
        Id = id;
        Manager = new() { LocalClientId = id, IsServer = host, Pointer = new((long)id + 100), CustomMessagingManager = new(id) };
        Ex = new() { Unity = Manager, Transport = new SteamTransportController { lobbyID = new(77) } };
        Session = new() { Pointer = new((long)id + 1000) };
        if (updated) { Pings = new(); Pings.Received += Show; }
    }
    private void Show(PingPacket packet, double remaining) { Shown.Add(packet); Lifetimes.Add(remaining); }
    internal void ReloadPings() { Activate(); Pings?.Dispose(); Pings = new(); Pings.Received += Show; }
    internal string GameplayImage() => string.Join("|", Players.players.Select(p =>
        $"{p.playerData.Pointer}:{p.playerData.playerID}:{p.clientID.Value.NetworkID}:{p.clientID.Value.PlayerIndex}:{p.transform.position.x},{p.transform.position.y},{p.transform.position.z}:{p.IsSpawned}"));
    internal void Activate()
    {
        NetworkManager.Singleton = Manager; NetworkManagerEx.Instance = Ex;
        NetworkSession.Instance = Session; PlayerManager.Instance = Players;
        GameManager.Instance = new(); Steamworks.SteamUser.Self = new(10000 + Id);
        Plugin.Logger = Log; Time.realtimeSinceStartupAsDouble = InertBus.Now;
    }
    internal void Tick() { Activate(); if (Online && PingTicksEnabled) Pings?.Tick(); }
    internal bool Submit(PingKind kind, PingPosition point = default) { Activate(); return Pings.Submit(kind, point); }
    internal void Roster(params ulong[] ids)
    {
        Manager.ConnectedClientsIds = new(ids);
        var transport = (SteamTransportController)Ex.Transport;
        Manager.ConnectionManager.Map.Clear(); transport.transport.connectionMapping.Clear(); transport.clientPayloads.Clear(); transport.connectedClients.Clear();
        Players.players.Clear(); Session.runDataManager.playerDatas.Clear();
        for (var i = 0; i < ids.Length; i++)
        {
            var data = new PlayerData { playerID = i, Pointer = new(2000 + i) };
            var client = new ClientIdentifier { NetworkID = ids[i], PlayerIndex = (ulong)i };
            var account = 10000 + ids[i];
            Manager.ConnectionManager.Map[ids[i]] = account;
            transport.transport.connectionMapping[account] = new() { id = new(account) };
            transport.clientPayloads[ids[i]] = new() { displayName = "Player " + ids[i], playerID = account.ToString() };
            transport.connectedClients.Add(client);
            if (ids[i] == Id) Session.primaryIdentifier = client;
            Players.players.Add(new() { playerData = data, clientID = new() { Value = client }, transform = new() { position = new(i * 4, 0, 0) } });
            Session.runDataManager.playerDatas.Add(data);
        }
    }
}

internal static class InertBus
{
    internal sealed record Envelope(ulong From, ulong To, string Channel, byte[] Bytes);
    internal static readonly Dictionary<ulong, Node> Nodes = new();
    internal static readonly Queue<Envelope> Queue = new();
    internal static readonly List<Envelope> History = new();
    internal static double Now;
    internal static int Unregistered, DisconnectCalls;
    internal static int ClientReads;
    internal static readonly List<(ulong Host, ulong Client, string Reason)> Disconnects = new();
    internal static bool ThrowForOffline;
    internal static void Reset()
    {
        foreach (var node in Nodes.Values) { node.Activate(); node.Pings?.Dispose(); }
        Nodes.Clear(); Queue.Clear(); History.Clear(); Now = 0;
        Crosstales.BWF.BWFManager.instance=new(){isReady=true};PlayerNameFilter.Throw=false;PlayerNameFilter.Calls=0;PlayerNameFilter.Override=null;
        UnityEngine.InputSystem.Gamepad.all.Clear(); UnityEngine.InputSystem.Gamepad.current=null;
        UIManager.GameInputBlocked=false;GameInput.assignedDevices.Clear();Plugin.PingGamepadEnabled=true;Plugin.PingPadButton.Value=PingPadButton.DpadUp;
        Unregistered = DisconnectCalls = 0; Disconnects.Clear(); ThrowForOffline = false;
        Steamworks.SteamMatchmaking.Data.Clear(); Steamworks.SteamMatchmaking.Owner = new(10000);
    }
    internal static Node Add(ulong id, bool host = false, bool updated = true)
    { var n = new Node(id, host, updated); Nodes.Add(id, n); return n; }
    internal static void Roster(params ulong[] ids) { foreach (var node in Nodes.Values) node.Roster(ids); }
    internal static void Send(ulong from, ulong to, string channel, byte[] bytes)
    {
        if (ThrowForOffline && (!Nodes.ContainsKey(to) || !Nodes[to].Online)) throw new IOException("Peer disconnected during send.");
        var envelope = new Envelope(from, to, channel, (byte[])bytes.Clone()); Queue.Enqueue(envelope); History.Add(envelope);
    }
    internal static void Pump()
    {
        while (Queue.Count > 0)
        {
            var envelope = Queue.Dequeue();
            if (!Nodes.TryGetValue(envelope.To, out var node) || !node.Online) continue;
            node.Activate();
            if (!node.Manager.CustomMessagingManager.Handlers.TryGetValue(envelope.Channel, out var handler)) { Unregistered++; continue; }
            handler.Invoke(envelope.From, new(envelope.Bytes));
        }
    }
    internal static void Run(double seconds)
    {
        var until = Now + seconds;
        do
        {
            foreach (var node in Nodes.Values) node.Tick();
            Pump(); Now += .25;
        } while (Now < until);
    }
}

internal sealed class InertLogger
{
    internal readonly List<string> Messages = new();
    internal void LogInfo(object text) => Messages.Add(text.ToString());
    internal void LogWarning(object text) => Messages.Add("WARNING " + text);
    internal void LogError(object text) => Messages.Add("ERROR " + text);
}

namespace ChainedBeasts.Matchmaking
{
    internal static class Plugin
    {
        internal static InertLogger Logger = new();
        internal static bool PingsEnabled = true;
        internal static bool ChatEnabled=true;
        internal static InertOption<UnityEngine.InputSystem.Key> ChatKey=new(){Value=UnityEngine.InputSystem.Key.Enter};
        internal static bool PingGamepadEnabled=true;
        internal static InertOption<PingPadButton> PingPadButton=new(){Value=Core.PingPadButton.DpadUp};
        internal static bool HostToolsEnabled = true;
        internal static InertOption<UnityEngine.InputSystem.Key> HostToolsKey = new() { Value = UnityEngine.InputSystem.Key.F8 };
        internal static HostModerationService HostTools;
        internal static InertOption<UnityEngine.InputSystem.Key> PingKey = new() { Value = UnityEngine.InputSystem.Key.G };
    }
    internal sealed class InertOption<T> { internal T Value; }
}
namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
    }
    public sealed class Transform { public Vector3 position; public Vector3 forward=new(0,0,1); }
    public sealed class GameObject { public bool activeInHierarchy = true; }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>
    {
        internal T[] Data;
        public Il2CppStructArray(int count) { Data = new T[count]; }
        public static explicit operator Il2CppStructArray<T>(T[] source) => new(source.Length) { Data = (T[])source.Clone() };
        public T[] ToArray() => (T[])Data.Clone();
    }
}
namespace Il2CppSystem.Collections.Generic { public interface IReadOnlyCollection<T> { int Count { get; } } }
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Unity.Netcode
{
    public enum NetworkDelivery { ReliableSequenced }
    public sealed class InertIds : Il2CppSystem.Collections.Generic.IReadOnlyCollection<ulong>
    {
        private readonly ulong[] _ids;
        public InertIds(params ulong[] ids) { _ids = ids; }
        public ulong this[int index] => _ids[index];
        public int Count => _ids.Length;
        public T Cast<T>() where T : class => (T)(object)this;
    }
    public sealed class NetworkManager
    {
        public static NetworkManager Singleton;
        public const ulong ServerClientId = 0;
        public IntPtr Pointer;
        public ulong LocalClientId;
        public bool IsServer, IsListening = true;
        public CustomMessagingManager CustomMessagingManager;
        public NetworkConnectionManager ConnectionManager = new();
        public double ClockOffset;
        public NetworkTime ServerTime => new() { Time = InertBus.Now + ClockOffset };
        public InertIds ConnectedClientsIds = new();
        public void DisconnectClient(ulong id) { InertBus.DisconnectCalls++; }
        public void DisconnectClient(ulong id, string reason) { InertBus.DisconnectCalls++; InertBus.Disconnects.Add((LocalClientId, id, reason)); }
        public sealed class ConnectionApprovalRequest { public ulong ClientNetworkId; }
        public sealed class ConnectionApprovalResponse { public bool Approved = true, CreatePlayerObject = true, Pending = true; public string Reason = "native"; }
    }
    public sealed class NetworkConnectionManager
    {
        public readonly Dictionary<ulong, ulong> Map = new();
        public int Lookups;
        public (ulong, bool) ClientIdToTransportId(ulong id) { Lookups++; return Map.TryGetValue(id, out var value) ? (value, true) : (0, false); }
    }
    public struct NetworkTime { public double Time; }
    public sealed class CustomMessagingManager
    {
        private readonly ulong _owner;
        internal readonly Dictionary<string, HandleNamedMessageDelegate> Handlers = new();
        public CustomMessagingManager(ulong owner) { _owner = owner; }
        public sealed class HandleNamedMessageDelegate
        {
            private Action<ulong, FastBufferReader> _callback;
            public static implicit operator HandleNamedMessageDelegate(Action<ulong, FastBufferReader> callback) => new() { _callback = callback };
            public void Invoke(ulong sender, FastBufferReader reader) => _callback(sender, reader);
        }
        public void RegisterNamedMessageHandler(string channel, HandleNamedMessageDelegate handler) => Handlers.Add(channel, handler);
        public void UnregisterNamedMessageHandler(string channel) => Handlers.Remove(channel);
        public void SendNamedMessage(string channel, ulong client, FastBufferWriter writer, NetworkDelivery delivery) => InertBus.Send(_owner, client, channel, writer.Data);
    }
    public struct FastBufferWriter : IDisposable
    {
        internal byte[] Data;
        public FastBufferWriter(int count, Unity.Collections.Allocator allocator, int max) { Data = new byte[count]; }
        public void WriteBytesSafe(Il2CppStructArray<byte> data) { Data = data.ToArray(); }
        public void Dispose() { }
    }
    public struct FastBufferReader
    {
        private readonly byte[] _data;
        public FastBufferReader(byte[] data) { _data = data; Position = 0; }
        public int Length => _data.Length;
        public int Position { get; private set; }
        public bool TryBeginRead(int count) => count >= 0 && Position + count <= Length;
        public void ReadBytesSafe(ref Il2CppStructArray<byte> target, int count)
        { if (!TryBeginRead(count)) throw new IOException("Underflow"); target = (Il2CppStructArray<byte>)_data.AsSpan(Position, count).ToArray(); Position += count; }
    }
}
namespace Steamworks
{
    public readonly struct CSteamID
    {
        public readonly ulong m_SteamID;
        public CSteamID(ulong id) { m_SteamID = id; }
        public bool IsValid() => m_SteamID != 0;
        public bool IsLobby() => m_SteamID == 77;
    }
    public static class SteamUser { public static CSteamID Self; public static CSteamID GetSteamID() => Self; }
    public static class SteamMatchmaking
    {
        internal static CSteamID Owner = new(10000);
        internal static readonly Dictionary<string, string> Data = new();
        public static CSteamID GetLobbyOwner(CSteamID lobby) => lobby.IsLobby() ? Owner : default;
        public static string GetLobbyData(CSteamID lobby, string key) => Data.GetValueOrDefault(key, "");
        public static bool SetLobbyData(CSteamID lobby, string key, string value)
        { if (SteamUser.Self.m_SteamID != Owner.m_SteamID) return false; Data[key] = value; return true; }
        public static bool DeleteLobbyData(CSteamID lobby, string key) => Data.Remove(key);
    }
}

public class Transport
{
    private static long _nextPointer = 50000;
    public readonly IntPtr Pointer = new(++_nextPointer);
    public T TryCast<T>() where T : class => this as T;
}
public sealed class SteamTransportController : Transport
{
    public Steamworks.CSteamID lobbyID;
    public InertSteamTransport transport = new();
    public Dictionary<ulong, UGSConnectionPayload> clientPayloads = new();
    public List<ClientIdentifier> connectedClients = new();
}
public sealed class InertSteamTransport { public Dictionary<ulong, InertSteamConnection> connectionMapping = new(); }
public sealed class InertSteamConnection { public Steamworks.CSteamID id; }
public sealed class UGSConnectionPayload { public string displayName, playerID; }
public sealed class GameManager
{
    public enum eState { MENU, SERVER_LOCAL, SERVER_REMOTE, SERVER_SINGLE_PLAYER }
    public static GameManager Instance;
    public static bool InstanceExists => Instance != null;
    public eState State = eState.SERVER_REMOTE;
}
public sealed class NetworkManagerEx
{
    public static NetworkManagerEx Instance;
    public static bool InstanceExists => Instance != null;
    public bool SceneOperationInProgress;
    public bool isLeavingLobby;
    public NetworkManager Unity;
    public Transport Transport;
}
public sealed class NetworkSession
{
    public static NetworkSession Instance;
    public static bool InstanceExists => Instance != null;
    public IntPtr Pointer;
    public bool IsSpawned = true, TransitionPending, isRestoringFromCheckpoint, isRestarting, IsPaused;
    public NetworkSceneRoot sceneRoot = new();
    public RunDataManager runDataManager = new();
    public ClientIdentifier primaryIdentifier;
    public GameLevel Level=new();
}
public sealed class NetworkSceneRoot { public ulong NetworkObjectId = 43; public bool IsSpawned = true, AllowUpdates = true; public string sceneName = "arena"; }
public sealed class RunDataManager { public List<PlayerData> playerDatas = new(); }
public sealed class PlayerData { public IntPtr Pointer; public int playerID; }
public sealed class ClientIdentifier { public ulong NetworkID, PlayerIndex; public string DisplayName="Sample name"; }
public sealed class Variable<T> { public T Value; }
public class UIManager {public static int inputBlockCount;public static bool GameInputBlocked {get=>inputBlockCount>0;set=>inputBlockCount=value?1:0;}
        public static void PushGameInputBlock<T>(){inputBlockCount++;}public static void PopGameInputBlock<T>(){inputBlockCount--;}}
public sealed class GameLevel { public enum eGameState { RUNNING, LEVEL_COMPLETE, LEVEL_FAILED, DEMO_COMPLETE } public eGameState ActiveGameState; }
public static class GameInput {public static Dictionary<ClientIdentifier,UnityEngine.InputSystem.InputDevice> assignedDevices=new(); public sealed class IUserInput {public UnityEngine.InputSystem.InputDevice Device;}}
public sealed class Player
{
    public GameInput.IUserInput InputUser=new();
    private bool _inputBlocked;
    public bool IsInputBlocked {get=>_inputBlocked||inputBlockCount>0;set=>_inputBlocked=value;}
    public int inputBlockCount,PushCount,PopCount;
    public void PushInputBlock(){inputBlockCount++;PushCount++;}public void PopInputBlock(){inputBlockCount--;PopCount++;}
    public bool IsSpawned = true;
    public GameObject gameObject = new();
    public Transform transform = new();
    public PlayerData playerData;
    private Variable<ClientIdentifier> _clientID;
    public Variable<ClientIdentifier> clientID { get { InertBus.ClientReads++; return _clientID; } set { _clientID = value; } }
    public AgentModel agentModel;
    public Plane mouseTargetPlane = new() { m_Normal = new(0,1,0) };
}
public sealed class AgentModel { public Transform headTransform; }
public sealed class PlayerManager
{
    public static PlayerManager Instance;
    public static bool InstanceExists => Instance != null;
    public List<Player> players = new();
}
