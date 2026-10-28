namespace ChainedBeasts.Matchmaking.Core;

public readonly record struct HostScope(long Instance, ulong Lobby, ulong HostAccount, ulong LocalClient)
{ public bool Active => Instance != 0 && Lobby != 0 && HostAccount != 0; }
public sealed record HostPeer(ulong Client, ulong Account, string Name, int Characters);
public sealed record BlockedAccount(ulong Account, string Name);
public sealed record KickConfirmation(ulong Generation, ulong Client, ulong Account, string Name);
public interface IHostModerationBackend
{
    HostScope ReadScope();
    IReadOnlyList<HostPeer> ReadPeers();
    void Disconnect(HostScope expectedScope, ulong client, ulong account, string reason);
}

/// <summary>Host-only, in-memory lobby exclusions. No commands from remote peers or save storage.</summary>
public sealed class HostModeration
{
    public const int MaximumBlocks = 256, MaximumPeers = 16;
    public const string KickReason = "The host removed you from this lobby. Rejoining this lobby is blocked.";
    private readonly IHostModerationBackend _backend;
    private readonly Dictionary<ulong, BlockedAccount> _blocked = new();
    private readonly Dictionary<ulong, ulong> _issued = new();
    private HostScope _scope;
    private ulong _generation;
    public KickConfirmation? Pending { get; private set; }
    public string Status { get; private set; } = "Host a Steam lobby to manage players.";
    public bool Active => _scope.Active;
    public int BlockedCount => _blocked.Count;
    public IReadOnlyList<BlockedAccount> Blocked => _blocked.Values.ToArray();
    public HostModeration(IHostModerationBackend backend) { _backend = backend; }

    public bool Refresh()
    {
        var next = _backend.ReadScope();
        if (next != _scope)
        {
            _scope = next; _generation++; _blocked.Clear(); _issued.Clear(); Pending = null;
            Status = next.Active ? "Host controls ready. Blocks last until this lobby ends or you unblock them." : "Host a Steam lobby to manage players.";
        }
        return _scope.Active;
    }

    public HostPeer[] Peers()
    {
        if (!Refresh()) return Array.Empty<HostPeer>();
        var generation = _generation;
        var peers = _backend.ReadPeers().Take(MaximumPeers).Where(p => p.Client != _scope.LocalClient && p.Account != _scope.HostAccount)
            .GroupBy(p => p.Client).Select(g => g.First() with { Name = SafeName(g.First().Name) }).ToArray();
        return Refresh() && generation == _generation ? peers : Array.Empty<HostPeer>();
    }

    public bool RequestKick(ulong client, ulong account)
    {
        var peers = Peers();
        var peer = peers.FirstOrDefault(p => p.Client == client && p.Account == account);
        if (!Active || peer is null || account == 0) { Pending = null; Status = "Player identity changed or is unavailable; refresh and select them again."; return false; }
        Pending = new(_generation, client, account, peer.Name);
        return true;
    }
    public void CancelKick() => Pending = null;

    public bool ConfirmKick()
    {
        var request = Pending;
        Pending = null; // Consume before any native call, including one that throws or re-enters.
        if (request is null) return false;
        var peers = Peers();
        if (!Active || request.Generation != _generation || !peers.Any(p => p.Client == request.Client && p.Account == request.Account))
        { Status = "Lobby/player changed. Nothing kicked; select the current player again."; return false; }
        if (!_blocked.ContainsKey(request.Account) && _blocked.Count >= MaximumBlocks)
        { Status = "This lobby's block list is full. Unblock an account first; nothing kicked."; return false; }
        // Commit by authenticated account BEFORE disconnecting, so an immediate fresh connection is denied.
        _blocked[request.Account] = new(request.Account, request.Name);
        _issued[request.Client] = request.Account;
        try
        {
            _backend.Disconnect(_scope, request.Client, request.Account, KickReason);
            Status = request.Name + " was sent a disconnect; rejoining this lobby is blocked.";
            return true;
        }
        catch
        {
            Status = request.Name + " is blocked, but disconnect failed. Select Kick again to retry.";
            return false; // Keep the block: the native call may already have partly completed.
        }
    }

    /// <summary>Also catches a connection approved just before the host clicked Kick.</summary>
    public void Tick()
    {
        if (!Refresh() || _blocked.Count == 0) return;
        var peers = Peers();
        foreach (var client in _issued.Keys.Where(id => !peers.Any(p => p.Client == id && p.Account == _issued[id])).ToArray()) _issued.Remove(client);
        foreach (var peer in peers)
        {
            if (peer.Account == 0 || !_blocked.ContainsKey(peer.Account) || _issued.TryGetValue(peer.Client, out var account) && account == peer.Account) continue;
            _issued[peer.Client] = peer.Account;
            try { _backend.Disconnect(_scope, peer.Client, peer.Account, KickReason); }
            catch { Status = "A blocked player could not be disconnected; use Kick again to retry."; }
        }
    }

    // Called in native approval before any original handler can approve the request.
    public bool AllowAdmission(ulong authenticatedAccount, bool identityKnown)
    {
        if (!Refresh() || _blocked.Count == 0) return true;
        if (identityKnown && authenticatedAccount == _scope.HostAccount) return true;
        return identityKnown && authenticatedAccount != 0 && !_blocked.ContainsKey(authenticatedAccount);
    }
    public bool Unblock(ulong account)
    {
        if (!Refresh() || !_blocked.Remove(account)) return false;
        Status = "Rejoin block removed. The game's normal admission rules still apply.";
        return true;
    }
    public void Clear() { _scope = default; _generation++; _blocked.Clear(); _issued.Clear(); Pending = null; }
    private static string SafeName(string name)
    {
        var display = LobbyPolicy.Display(new string((name ?? "").Where(c =>
            !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()), 32);
        return display.Length > 0 && char.IsHighSurrogate(display[^1]) ? display[..^1] : display;
    }
}
