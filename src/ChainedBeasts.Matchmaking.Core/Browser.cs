namespace ChainedBeasts.Matchmaking.Core;

public interface ILobbyBackend
{
    void Search(long token, SearchOptions options, string build);
    void CancelDiscovery();
    void RefreshLobby(long token, ulong lobby);
    void JoinNative(ulong lobby);
}

/// <summary>Production orchestration; all callbacks/ticks run on the game thread.</summary>
public sealed class Browser
{
    public const int MaximumResults = 50;
    public const double SearchTimeout = 22, MetadataTimeout = 8, ResultLifetime = 60, RefreshCooldown = 3;
    private readonly ILobbyBackend _backend;
    private long _token;
    private double _deadline, _searchedAt = double.NegativeInfinity, _refreshAt;
    private Lobby? _selected;
    private bool _acknowledged;
    private readonly List<Lobby> _lobbies = new();
    public Browser(ILobbyBackend backend) => _backend = backend;
    public Operation Operation { get; private set; }
    public string Status { get; private set; } = "Refresh to find open Steam lobbies.";
    public IReadOnlyList<Lobby> Lobbies => _lobbies;
    public event Action<string>? Changed;

    public bool Search(SearchOptions options, double now, BrowserContext context)
    {
        if (!context.Available) return Reject(context.Reason);
        if (Operation != Operation.Idle) return Reject("An operation is already in progress.");
        if (now < _refreshAt) return Reject("Wait a moment before refreshing again.");
        _refreshAt = now + RefreshCooldown;
        _lobbies.Clear();
        _selected = null;
        Operation = Operation.Searching;
        _deadline = now + SearchTimeout;
        Set("Searching for open Steam lobbies...");
        var token = ++_token;
        try { _backend.Search(token, options, context.Build); return true; }
        catch (Exception e) { Fail("Search failed: " + e.Message); return false; }
    }

    public void SearchCompleted(long token, IReadOnlyList<Lobby> lobbies, string? error, double now)
    {
        if (Operation != Operation.Searching || token != _token) return;
        if (error != null) { Fail(error); return; }
        _lobbies.AddRange(lobbies.Where(x => x.Id != 0).DistinctBy(x => x.Id).Take(MaximumResults));
        _searchedAt = now;
        Operation = Operation.Idle;
        Set(_lobbies.Count == 0 ? "No open lobbies found. Try Worldwide or refresh later." :
            $"Found {_lobbies.Count} open Steam lobbies. Mod setups may be unknown.");
    }

    public bool BeginJoin(ulong id, bool acknowledgeUnknownMods, double now, BrowserContext context)
    {
        if (!context.Available) return Reject(context.Reason);
        if (Operation != Operation.Idle) return Reject("An operation is already in progress.");
        if (now - _searchedAt > ResultLifetime) return Reject("Results are old. Refresh before joining.");
        var lobby = _lobbies.FirstOrDefault(x => x.Id == id);
        if (lobby == null) return Reject("Select a lobby from the current results.");
        var assessment = LobbyPolicy.Assess(lobby, context);
        if (!assessment.Allowed) return Reject(assessment.Detail);
        if (LobbyPolicy.NeedsModConfirmation(assessment, context) && !acknowledgeUnknownMods)
            return Reject("Confirm you checked the host's mod setup, then press Join.");
        _selected = lobby;
        _acknowledged = acknowledgeUnknownMods;
        Operation = Operation.CheckingLobby;
        _deadline = now + MetadataTimeout;
        Set("Checking the selected lobby again...");
        var token = ++_token;
        try { _backend.RefreshLobby(token, id); return true; }
        catch (Exception e) { Fail("Lobby refresh failed: " + e.Message); return false; }
    }

    public void LobbyRefreshed(long token, Lobby? lobby, string? error, BrowserContext context)
    {
        if (Operation != Operation.CheckingLobby || token != _token) return;
        if (error != null || lobby == null) { Fail(error ?? "The lobby is no longer available."); return; }
        if (lobby.Id != _selected?.Id) return; // unrelated lobby-data callbacks must not complete this operation
        if (!context.Available) { Fail(context.Reason); return; }
        if (lobby.Owner != 0 && _selected.Owner != 0 && lobby.Owner != _selected.Owner)
        { Fail("The lobby host changed. Refresh the list."); return; }
        if (_selected.Protocol == "1" && lobby.Protocol == "1" && lobby.AdvertOwner != _selected.AdvertOwner)
        { Fail("The advertised host changed. Refresh the list."); return; }
        var assessment = LobbyPolicy.Assess(lobby, context);
        if (!assessment.Allowed) { Fail(assessment.Detail); return; }
        if (LobbyPolicy.NeedsModConfirmation(assessment, context) && !_acknowledged)
        { Fail("Mod information changed. Check it with the host and select the lobby again."); return; }
        Operation = Operation.Joining; // set before native entry invokes our cancellation prefix
        _backend.CancelDiscovery();
        Set("Joining through the game. Its connection and compatibility checks still apply.");
        try { _backend.JoinNative(lobby.Id); }
        catch (Exception e) { Fail("Native join failed: " + e.Message); }
    }

    public void NativeJoinCompleted(bool connected, string? error = null)
    {
        if (Operation != Operation.Joining) return;
        Operation = Operation.Idle;
        _selected = null;
        _lobbies.Clear();
        Set(connected ? "Connected through the native game lobby." : error ?? "The game did not complete the connection. Check its error message.");
    }

    public void Tick(double now, BrowserContext context)
    {
        if (Operation is not (Operation.Searching or Operation.CheckingLobby)) return;
        if (!context.Available) { Cancel(context.Reason); return; }
        if (now >= _deadline) Fail(Operation == Operation.Searching ? "Steam lobby search timed out. Refresh to retry." :
            "Lobby metadata refresh timed out. No join was attempted.");
    }

    public void Cancel(string reason = "Search cancelled.")
    {
        // A native connection belongs to the game; closing our UI must not abort it.
        if (Operation == Operation.Joining) return;
        ++_token;
        _backend.CancelDiscovery();
        Operation = Operation.Idle;
        _selected = null;
        Set(reason);
    }

    private void Fail(string reason)
    {
        ++_token;
        _backend.CancelDiscovery();
        Operation = Operation.Idle;
        _selected = null;
        Set(reason);
    }
    private bool Reject(string reason) { Set(reason); return false; }
    private void Set(string message) { Status = message; Changed?.Invoke(message); }
}
