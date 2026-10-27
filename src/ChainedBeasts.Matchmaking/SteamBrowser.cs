using System.Globalization;
using ChainedBeasts.Matchmaking.Core;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using NativeTask = Il2CppSystem.Threading.Tasks.Task;

namespace ChainedBeasts.Matchmaking;

internal sealed class SteamBrowser : ILobbyBackend, IDisposable
{
    internal Browser Browser { get; }
    internal BrowserContext Context { get; private set; } = new(false, "Waiting for the main menu.", "", null);
    private CallResult<LobbyMatchList_t> _search;
    private CallResult<LobbyMatchList_t>.APIDispatchDelegate _searchDelegate;
    private Action<LobbyMatchList_t, bool> _searchAction;
    private Callback<LobbyDataUpdate_t> _dataCallback;
    private Callback<LobbyDataUpdate_t>.DispatchDelegate _dataDelegate;
    private Action<LobbyDataUpdate_t> _dataAction;
    private NativeTask _join;
    private long _searchToken, _metadataToken;
    private ulong _metadataLobby, _advertisedLobby;
    private bool _inCallback, _cancelPending, _disposed;
    private double _nextAdvert;
    private string _lastAdvert = "";

    internal SteamBrowser()
    {
        Browser = new Browser(this);
        Browser.Changed += LogStatus;
    }

    private static void LogStatus(string text) => Plugin.Logger.LogInfo("[Matchmaking] " + text);

    internal void Tick()
    {
        if (_disposed) return;
        if (_cancelPending && !_inCallback) CancelDiscovery();
        Context = ReadContext();
        Browser.Tick(Time.realtimeSinceStartupAsDouble, Context);
        if (_join != null)
        {
            // Await the game's own transition/timeout flow without starting another connection.
            if (_join.IsCompleted)
            {
                var connected = NetworkManagerEx.InstanceExists && NetworkManagerEx.Instance.Unity != null &&
                    NetworkManagerEx.Instance.Unity.IsConnectedClient && GameManager.InstanceExists &&
                    GameManager.Instance.State == GameManager.eState.SERVER_REMOTE;
                var failed = _join.IsFaulted;
                if (failed) Plugin.Logger.LogWarning("[Matchmaking] native join task failed: " + _join.Exception);
                _join = null;
                Browser.NativeJoinCompleted(connected, failed ? "Native join failed. Check the game error/log." : null);
            }
        }
        if (Time.realtimeSinceStartupAsDouble >= _nextAdvert)
        {
            _nextAdvert = Time.realtimeSinceStartupAsDouble + 2;
            AdvertiseOwnLobby();
        }
    }

    private static SteamTransportController Controller()
    {
        if (!NetworkManagerEx.InstanceExists) return null;
        return NetworkManagerEx.Instance.Transport?.TryCast<SteamTransportController>();
    }

    private static BrowserContext ReadContext()
    {
        try
        {
            if (!GameManager.InstanceExists || !NetworkManagerEx.InstanceExists)
                return Unavailable("Waiting for the game to initialize.");
            if (GameManager.Instance.State != GameManager.eState.MENU)
                return Unavailable("Return to the main menu to browse.");
            var controller = Controller();
            if (controller == null) return Unavailable("Steam transport required; Relay is not supported in this prototype.");
            if (NetworkManagerEx.Instance.Unity == null || NetworkManagerEx.Instance.Unity.IsListening ||
                controller.lobbyID.m_SteamID != 0 || controller.ConnectionState is ITransportController.eConnectionState.PENDING or ITransportController.eConnectionState.CONNECTED)
                return Unavailable("Leave the current lobby/connection before browsing.");
            if (!SteamUser.BLoggedOn()) return Unavailable("Sign in to Steam before browsing.");
            return new(true, "", controller.GetBuildSignature(), FoundationCompatibility.Read());
        }
        catch { return Unavailable("Steam or the game is not ready yet."); }
    }
    private static BrowserContext Unavailable(string why) => new(false, why, "", null);

    private void EnsureDataCallback()
    {
        if (_dataCallback != null) return;
        _dataAction = OnLobbyData;
        _dataDelegate = _dataAction;
        _dataCallback = Callback<LobbyDataUpdate_t>.Create(_dataDelegate);
    }

    public void Search(long token, SearchOptions options, string build)
    {
        CancelDiscovery();
        EnsureDataCallback();
        _searchToken = token;
        _searchAction = (data, failed) => OnSearch(token, data, failed);
        _searchDelegate = _searchAction;
        _search = CallResult<LobbyMatchList_t>.Create(_searchDelegate);
        SteamMatchmaking.AddRequestLobbyListStringFilter("transport_type", "steam", ELobbyComparison.k_ELobbyComparisonEqual);
        if (!options.OtherBuilds)
            SteamMatchmaking.AddRequestLobbyListStringFilter("build", build, ELobbyComparison.k_ELobbyComparisonEqual);
        // No cbmm_* filter: ordinary native public lobbies are included.
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(options.Worldwide ?
            ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide : ELobbyDistanceFilter.k_ELobbyDistanceFilterDefault);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(Browser.MaximumResults);
        var call = SteamMatchmaking.RequestLobbyList();
        if (call.m_SteamAPICall == 0) throw new InvalidOperationException("Steam rejected the lobby search request.");
        _search.Set(call, _searchDelegate);
    }

    private void OnSearch(long token, LobbyMatchList_t data, bool failed)
    {
        if (_disposed || token != _searchToken) return;
        _inCallback = true;
        try
        {
            if (failed) { Browser.SearchCompleted(token, Array.Empty<Lobby>(), "Steam lobby search failed (I/O).", Time.realtimeSinceStartupAsDouble); return; }
            var lobbies = new List<Lobby>();
            for (var i = 0; i < Math.Min((long)data.m_nLobbiesMatching, Browser.MaximumResults); i++)
            {
                try { lobbies.Add(ReadLobby(SteamMatchmaking.GetLobbyByIndex(i))); }
                catch (Exception e) { Plugin.Logger.LogWarning("[Matchmaking] skipped an unreadable lobby: " + e.GetType().Name); }
            }
            // Copy the current result IDs/data inside this callback before another list replaces them.
            Browser.SearchCompleted(token, lobbies, null, Time.realtimeSinceStartupAsDouble);
        }
        catch (Exception e) { Browser.SearchCompleted(token, Array.Empty<Lobby>(), "Could not read Steam search results: " + e.Message, Time.realtimeSinceStartupAsDouble); }
        finally { _inCallback = false; }
    }

    private static Lobby ReadLobby(CSteamID id)
    {
        if (!id.IsValid() || !id.IsLobby()) throw new InvalidOperationException("Invalid Steam lobby ID.");
        return LobbyDirectoryReader.Read(new SteamDirectoryEntry(id));
    }

    private sealed class SteamDirectoryEntry : ILobbyDirectoryEntry
    {
        private readonly CSteamID _id;
        internal SteamDirectoryEntry(CSteamID id) => _id = id;
        public ulong Id => _id.m_SteamID;
        public int Members => SteamMatchmaking.GetNumLobbyMembers(_id);
        public int MemberLimit => SteamMatchmaking.GetLobbyMemberLimit(_id);
        public string Data(string key) => SteamMatchmaking.GetLobbyData(_id, key);
    }

    public void RefreshLobby(long token, ulong lobby)
    {
        EnsureDataCallback();
        _metadataToken = token;
        _metadataLobby = lobby;
        if (!SteamMatchmaking.RequestLobbyData(new CSteamID(lobby)))
            throw new InvalidOperationException("Steam rejected the lobby metadata request.");
    }

    private void OnLobbyData(LobbyDataUpdate_t data)
    {
        if (_disposed || _metadataLobby == 0 || data.m_ulSteamIDLobby != _metadataLobby ||
            data.m_ulSteamIDMember != data.m_ulSteamIDLobby) return;
        var token = _metadataToken;
        var id = _metadataLobby;
        _metadataLobby = 0;
        _metadataToken = 0;
        _inCallback = true;
        try
        {
            Context = ReadContext();
            Browser.LobbyRefreshed(token, data.m_bSuccess != 0 ? ReadLobby(new CSteamID(id)) : null,
                data.m_bSuccess != 0 ? null : "Steam reports that lobby is unavailable.", Context);
        }
        catch (Exception e) { Browser.LobbyRefreshed(token, null, "Could not refresh the lobby: " + e.Message, Context); }
        finally { _inCallback = false; }
    }

    public void JoinNative(ulong lobby)
    {
        // High-level native join preserves transition hints, tagged transport selection and timeout.
        if (_join != null) throw new InvalidOperationException("A native join is already pending.");
        var tagged = JoinCodeTag.Format(TransportType.Steam, lobby.ToString(CultureInfo.InvariantCulture));
        _join = GameManager.Instance.JoinRemoteServer(tagged);
        if (_join == null) throw new InvalidOperationException("The game returned no join task.");
    }

    public void CancelDiscovery()
    {
        _searchToken = 0;
        _metadataLobby = 0;
        _metadataToken = 0;
        if (_inCallback) { _cancelPending = true; return; }
        _cancelPending = false;
        try { _search?.Cancel(); _search?.Dispose(); }
        catch (Exception e) { Plugin.Logger.LogDebug("[Matchmaking] search cleanup: " + e.GetType().Name); }
        finally { _search = null; _searchDelegate = null; _searchAction = null; }
    }

    private void AdvertiseOwnLobby()
    {
        try
        {
            var controller = Controller();
            if (controller == null || !NetworkManagerEx.Instance.Unity.IsServer || controller.lobbyID.m_SteamID == 0)
            { _advertisedLobby = 0; _lastAdvert = ""; return; }
            var id = controller.lobbyID;
            if (SteamMatchmaking.GetLobbyOwner(id) != SteamUser.GetSteamID()) return;
            if (!Plugin.AdvertiseCompatibility.Value) { RemoveOwnAdvert(); return; }
            var waiting = controller.isLobbyOpen && SceneManager.GetSceneByName("GameServerLobby").isLoaded;
            var fingerprint = FoundationCompatibility.Read() ?? "unknown";
            var owner = SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture);
            var state = waiting ? "waiting" : "closed";
            var name = LobbyPolicy.Display(SteamFriends.GetPersonaName());
            var identity = id.m_SteamID + "/" + fingerprint + "/" + state + "/" + name;
            if (_lastAdvert == identity) return;
            var okay = SteamMatchmaking.SetLobbyData(id, LobbyPolicy.ProtocolKey, "1") &
                SteamMatchmaking.SetLobbyData(id, LobbyPolicy.OwnerKey, owner) &
                SteamMatchmaking.SetLobbyData(id, LobbyPolicy.FingerprintKey, fingerprint) &
                SteamMatchmaking.SetLobbyData(id, LobbyPolicy.NameKey, name) &
                SteamMatchmaking.SetLobbyData(id, LobbyPolicy.StateKey, state);
            _advertisedLobby = id.m_SteamID;
            if (okay) _lastAdvert = identity;
        }
        catch (Exception e) { Plugin.Logger.LogDebug("[Matchmaking] optional host metadata unavailable: " + e.GetType().Name); }
    }

    private void RemoveOwnAdvert()
    {
        if (_advertisedLobby == 0) return;
        var id = new CSteamID(_advertisedLobby);
        if (SteamMatchmaking.GetLobbyOwner(id) == SteamUser.GetSteamID())
            foreach (var key in LobbyPolicy.AdvertKeys) SteamMatchmaking.DeleteLobbyData(id, key);
        _advertisedLobby = 0;
        _lastAdvert = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CancelDiscovery(); _dataCallback?.Dispose(); RemoveOwnAdvert(); }
        catch (Exception e) { Plugin.Logger.LogDebug("[Matchmaking] shutdown: " + e.GetType().Name); }
        _dataCallback = null;
        _dataDelegate = null;
        _dataAction = null;
        // Native join tasks belong to the game; no disconnect or lobby teardown here.
    }
}
