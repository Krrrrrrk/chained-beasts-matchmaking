using ChainedBeasts.Matchmaking.Core;

static class ModerationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var backend = new ModerationBackend(); var policy = new HostModeration(backend); backend.Policy = policy;
        var host = new HostPeer(0, 100, "Host", 1); var friend = new HostPeer(7, 200, "<b>Alice\n</b>", 2);
        backend.People = new[] { host, friend, new HostPeer(8, 0, "Unknown", 1) };
        check(!policy.Refresh() && policy.Peers().Length == 0, "Nonhost has no host panel data.");
        check(!policy.RequestKick(7, 200) && !policy.ConfirmKick() && backend.Kicks.Count == 0, "Nonhost cannot request or confirm a kick.");
        backend.Scope = new(1, 77, 100, 0); policy.Refresh();
        check(policy.Peers().Length == 2 && policy.Peers().All(p => p.Client != 0), "Host's own connection is excluded.");
        backend.People = new[] { host, friend with { Name = "A\u202e\u200blice" } };
        check(policy.Peers().Single().Name == "Alice", "Invisible/bidi formatting is stripped from moderation labels without changing identity.");
        backend.People = new[] { host, friend with { Name = new string('A', 31) + "\U0001f43e" } };
        check(policy.Peers().Single().Name == new string('A', 31), "Bounded moderation label never ends with half an emoji surrogate pair.");
        backend.People = new[] { host, friend, new HostPeer(8, 0, "Unknown", 1) };
        check(!policy.RequestKick(0, 100) && !policy.RequestKick(8, 0), "Self and unverified identities cannot be kicked.");
        check(policy.AllowAdmission(0, false), "No blocks: unavailable identity leaves original native admission untouched.");
        check(policy.RequestKick(7, 200) && policy.Pending?.Name == "bAlice/b", "Kick confirmation uses current sanitized display name.");
        policy.CancelKick();
        check(!policy.ConfirmKick() && backend.Kicks.Count == 0, "Cancellation never disconnects.");
        check(policy.RequestKick(7, 200) && policy.ConfirmKick(), "Fresh host confirmation requests one disconnect.");
        check(backend.Kicks.SequenceEqual(new[] { 7UL }) && backend.BlockWasPresentDuringDisconnect, "Stable-account block is committed before the native disconnect call.");
        check(!policy.ConfirmKick() && backend.Kicks.Count == 1, "Duplicate confirmation cannot disconnect twice.");
        policy.Tick(); policy.Tick();
        check(backend.Kicks.Count == 1, "Repeated frames cannot repeat the same issued kick.");
        check(!policy.AllowAdmission(200, true) && policy.AllowAdmission(300, true), "Blocked Steam account is denied; unrelated account keeps native checks.");
        check(!policy.AllowAdmission(0, false), "Active blocks fail closed when a new connection's authenticated identity is unavailable.");
        check(policy.AllowAdmission(100, true), "Local host account is not locked out.");
        backend.People = new[] { host, friend with { Client = 19, Name = "Renamed" } }; policy.Tick();
        check(backend.Kicks.SequenceEqual(new[] { 7UL, 19UL }), "Already-approved racing reconnect with a new NGO ID is disconnected by account identity.");
        check(!policy.AllowAdmission(200, true), "Renaming or using a new connection ID cannot bypass the block.");
        check(policy.Unblock(200) && policy.AllowAdmission(200, true), "Host can explicitly allow rejoin.");
        backend.People = new[] { host, friend };
        policy.RequestKick(7, 200); backend.People = new[] { host, friend with { Account = 300 } };
        check(!policy.ConfirmKick() && policy.Blocked.Count == 0 && backend.Kicks.Count == 2, "Recycled client ID with a different account invalidates stale confirmation.");
        backend.People = new[] { host, friend }; policy.RequestKick(7, 200);
        backend.Scope = new(1, 78, 100, 0);
        check(!policy.ConfirmKick() && backend.Kicks.Count == 2, "A new lobby invalidates pending confirmation.");
        backend.Scope = new(1, 77, 100, 0); policy.Refresh();
        backend.ThrowDisconnect = true; backend.Policy = policy;
        policy.RequestKick(7, 200);
        check(!policy.ConfirmKick() && !policy.AllowAdmission(200, true), "A partially failing native disconnect keeps the rejoin block.");
        backend.ThrowDisconnect = false;
        check(policy.RequestKick(7, 200) && policy.ConfirmKick(), "Host may explicitly confirm another disconnect attempt.");
        backend.Scope = default; policy.Refresh();
        check(policy.Blocked.Count == 0 && policy.AllowAdmission(200, true), "Ending the hosted lobby clears session-only exclusions.");
        backend.Scope = new(2, 77, 100, 0); policy.Refresh();
        check(policy.Blocked.Count == 0, "Reused lobby identifier on a different server instance has fresh state.");
        backend.People = new[] { host, friend }; backend.Policy = policy;
        backend.ChangeScopeDuringRead = true;
        check(policy.Peers().Length == 0 && !policy.RequestKick(7, 200), "Context changes during roster read cannot produce an actionable stale list.");
        backend.ChangeScopeDuringRead = false;

        var full = new ModerationBackend { Scope = new(1, 77, 100, 0) }; var ledger = new HostModeration(full); full.Policy = ledger;
        for (var i = 0; i < HostModeration.MaximumBlocks; i++)
        { full.People = new[] { new HostPeer((ulong)i + 1, (ulong)i + 1000, "Player", 1) }; ledger.RequestKick((ulong)i+1, (ulong)i+1000); ledger.ConfirmKick(); }
        full.People = new[] { new HostPeer(999, 99999, "One more", 1) }; ledger.RequestKick(999, 99999);
        check(!ledger.ConfirmKick() && full.Kicks.Count == HostModeration.MaximumBlocks, "Full bounded ledger cannot kick without retaining the required block.");
        check(ledger.Unblock(1000) && ledger.RequestKick(999, 99999) && ledger.ConfirmKick(), "Unblocking makes space for a new explicit kick.");
    }

    private sealed class ModerationBackend : IHostModerationBackend
    {
        internal HostScope Scope;
        internal HostPeer[] People = Array.Empty<HostPeer>();
        internal readonly List<ulong> Kicks = new();
        internal HostModeration? Policy;
        internal bool ThrowDisconnect, ChangeScopeDuringRead;
        internal bool BlockWasPresentDuringDisconnect = true;
        public HostScope ReadScope() => Scope;
        public IReadOnlyList<HostPeer> ReadPeers()
        { if (ChangeScopeDuringRead) Scope = Scope with { Lobby = Scope.Lobby + 1 }; return People; }
        public void Disconnect(HostScope expectedScope, ulong client, ulong account, string reason)
        {
            Kicks.Add(client);
            if (Policy is not null) BlockWasPresentDuringDisconnect &= Policy.Blocked.Any(b => People.Any(p => p.Client == client && p.Account == b.Account));
            if (ThrowDisconnect) throw new IOException("Native disconnect partly failed.");
        }
    }
}
