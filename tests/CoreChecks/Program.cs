using ChainedBeasts.Matchmaking.Core;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var digest = new string('a', 64);
var context = new BrowserContext(true, "", "1.0.2|release", digest);
var publicLobby = new Lobby(10, 20, "Host", context.Build, "steam", 1, 4);
var matched = publicLobby with { Protocol = "1", Fingerprint = digest, AdvertOwner = "20", AdvertState = "waiting" };
var unavailable = context with { Available = false, Reason = "Busy in native lobby." };
var backend = new FakeBackend();
var browser = new Browser(backend);

Check(LobbyPolicy.Assess(publicLobby, context).Compatibility == Compatibility.Unknown, "Native public hosts must appear without browser tags.");
Check(LobbyPolicy.Assess(matched, context).Compatibility == Compatibility.FoundationMatched, "Exact Foundation identity.");
Check(LobbyPolicy.Assess(matched with { Fingerprint = digest.ToUpperInvariant() }, context).Allowed, "Hex digest casing is immaterial.");
Check(!LobbyPolicy.Assess(matched with { Fingerprint = new string('b', 64) }, context).Allowed, "Known mod mismatch rejected.");
Check(!LobbyPolicy.Assess(matched, context with { Fingerprint = null }).Allowed, "Unmodded browser cannot accept known Foundation host as compatible.");
Check(LobbyPolicy.Assess(matched with { AdvertOwner = "999" }, context).Compatibility == Compatibility.Unknown, "Host migration invalidates stale compatibility tag.");
Check(LobbyPolicy.Assess(matched with { Fingerprint = "bad" }, context).Compatibility == Compatibility.Unknown, "Malformed identity is never verified.");
Check(!LobbyPolicy.Assess(matched with { AdvertState = "closed" }, context).Allowed, "Advertised closed room blocked.");
foreach (var lobby in new[] { publicLobby with { Id = 0 }, publicLobby with { Owner = 0 }, publicLobby with { Build = "old" },
    publicLobby with { Transport = "relay" }, publicLobby with { Members = 4 }, publicLobby with { Members = -1 },
    publicLobby with { MemberLimit = 0 }, publicLobby with { MemberLimit = 250 }, publicLobby with { Build = "" } })
    Check(!LobbyPolicy.Assess(lobby, context).Allowed, "Invalid/incompatible lobby blocked.");
Check(LobbyPolicy.Display("<b>Bad\n\r\tName</b>", 8) == "bBadName", "Control/rich-text delimiters stripped and display bounded.");
Check(LobbyPolicy.Display(null) == "Unknown", "Null remote name fallback.");
Check(LobbyPolicy.Display("  ") == "Unknown", "Whitespace remote name fallback.");
Check(!LobbyPolicy.IsDigest(new string('g', 64)), "Nonhex digest rejected.");

Check(!browser.Search(new(), 0, unavailable) && backend.Searches == 0, "No Steam request while native session busy.");
Check(browser.Search(new(), 0, context), "Start normal discovery.");
var first = backend.Token;
Check(!browser.Search(new(), 1, context) && backend.Searches == 1, "No competing browser search.");
browser.SearchCompleted(first, new[] { publicLobby, publicLobby }, null, 1);
Check(browser.Lobbies.Count == 1 && browser.Operation == Operation.Idle, "Deduplicate public result IDs.");
browser.SearchCompleted(first, Array.Empty<Lobby>(), null, 2);
Check(browser.Lobbies.Count == 1, "Repeated callback cannot replace completed results.");
Check(!browser.Search(new(), 2, context), "Respect search cooldown.");
Check(!browser.BeginJoin(10, false, 2, context) && backend.Joins.Count == 0 && backend.Refreshes == 0, "Unknown mods require an explicit acknowledgement.");
Check(browser.BeginJoin(10, true, 2, context), "Native public room may be selected intentionally.");
var selectedToken = backend.Token;
Check(!browser.BeginJoin(10, true, 2, context), "No duplicate metadata/join action.");
browser.LobbyRefreshed(selectedToken, publicLobby with { Id = 11 }, null, context);
Check(browser.Operation == Operation.CheckingLobby && backend.Joins.Count == 0, "Unrelated metadata notification ignored.");
browser.LobbyRefreshed(selectedToken, publicLobby with { Members = 4 }, null, context);
Check(browser.Operation == Operation.Idle && backend.Joins.Count == 0, "Room filled between list and Join.");

Check(browser.BeginJoin(10, true, 3, context), "Retry after refreshed rejection.");
browser.LobbyRefreshed(backend.Token, publicLobby with { Owner = 21 }, null, context);
Check(backend.Joins.Count == 0, "Host change must not silently redirect join.");
Check(browser.BeginJoin(10, true, 3, context), "Check changed native build.");
browser.LobbyRefreshed(backend.Token, publicLobby with { Build = "old" }, null, context);
Check(backend.Joins.Count == 0, "Fresh native-build mismatch blocked.");
Check(browser.BeginJoin(10, true, 3, context), "Check changed mod profile.");
browser.LobbyRefreshed(backend.Token, matched with { Fingerprint = new string('b', 64) }, null, context);
Check(backend.Joins.Count == 0, "Fresh known mod mismatch overrides acknowledgement.");
Check(browser.BeginJoin(10, true, 3, context), "Revalidate local state.");
browser.LobbyRefreshed(backend.Token, publicLobby, null, unavailable);
Check(backend.Joins.Count == 0, "Local native connection changed during metadata refresh.");
Check(browser.BeginJoin(10, true, 3, context), "Begin final valid join.");
var joinToken = backend.Token;
browser.LobbyRefreshed(joinToken, publicLobby, null, context);
Check(backend.Joins.SequenceEqual(new ulong[] { 10 }) && browser.Operation == Operation.Joining, "Only fresh validated lobby reaches native join once.");
browser.LobbyRefreshed(joinToken, publicLobby, null, context);
Check(backend.Joins.Count == 1, "Duplicate callback cannot join twice.");
browser.Cancel();
browser.Tick(100, unavailable);
Check(browser.Operation == Operation.Joining, "UI closure/tick must not cancel a game-owned connection.");
browser.NativeJoinCompleted(true);
Check(browser.Operation == Operation.Idle && browser.Lobbies.Count == 0, "Connection completion clears stale results.");

Check(browser.Search(new(true, true), 101, context), "Start worldwide/other-build search.");
Check(backend.Options is { Worldwide: true, OtherBuilds: true }, "User search options reach backend.");
var oldToken = backend.Token;
browser.Cancel("Native code-entry search took over.");
Check(browser.Search(new(), 105, context), "New query after cancellation.");
var current = backend.Token;
browser.SearchCompleted(oldToken, new[] { publicLobby }, null, 106);
Check(browser.Operation == Operation.Searching && browser.Lobbies.Count == 0, "Late cancelled search cannot replace new query.");
browser.SearchCompleted(current, Array.Empty<Lobby>(), null, 106);
Check(browser.Operation == Operation.Idle && browser.Status.Contains("No open"), "Empty search is a normal result.");
Check(browser.Search(new(), 110, context), "Begin timeout case.");
var timeoutToken = backend.Token;
browser.Tick(110 + Browser.SearchTimeout, context);
Check(browser.Operation == Operation.Idle && browser.Status.Contains("timed out"), "Search timeout releases UI.");
browser.SearchCompleted(timeoutToken, new[] { publicLobby }, null, 140);
Check(browser.Lobbies.Count == 0, "Late timed-out result ignored.");

Check(browser.Search(new(), 150, context), "Search large result set.");
browser.SearchCompleted(backend.Token, Enumerable.Range(1, 80).Select(i => publicLobby with { Id = (ulong)i }).ToArray(), null, 151);
Check(browser.Lobbies.Count == Browser.MaximumResults, "Bound result memory/UI workload.");
Check(!browser.BeginJoin(10, true, 151 + Browser.ResultLifetime + 1, context), "Stale results cannot start a join.");
Check(browser.Search(new(), 230, context), "Search before metadata timeout.");
browser.SearchCompleted(backend.Token, new[] { matched }, null, 231);
Check(browser.BeginJoin(10, false, 232, context), "Verified Foundation match does not require unknown-mod acknowledgement.");
var metaToken = backend.Token;
browser.Tick(232 + Browser.MetadataTimeout, context);
Check(browser.Operation == Operation.Idle && browser.Status.Contains("No join"), "Metadata timeout fails closed.");
browser.LobbyRefreshed(metaToken, matched, null, context);
Check(backend.Joins.Count == 1, "Late metadata cannot cause a timed-out join.");
Check(browser.BeginJoin(10, false, 241, context), "Recheck verified listing.");
browser.LobbyRefreshed(backend.Token, publicLobby, null, context);
Check(backend.Joins.Count == 1 && browser.Status.Contains("changed"), "Loss of verified profile requires a new acknowledgement.");

Check(browser.Search(new(), 250, context), "Begin scene-change cancellation case.");
browser.Tick(251, unavailable);
Check(browser.Operation == Operation.Idle && browser.Status == unavailable.Reason, "Leaving menu cancels discovery.");
backend.ThrowSearch = true;
Check(!browser.Search(new(), 260, context) && browser.Operation == Operation.Idle, "Backend search failure cannot wedge UI.");
backend.ThrowSearch = false;
Check(browser.Search(new(), 270, context), "Retry backend search.");
browser.SearchCompleted(backend.Token, new[] { publicLobby }, null, 271);
backend.ThrowMetadata = true;
Check(!browser.BeginJoin(10, true, 272, context) && browser.Operation == Operation.Idle, "Metadata request failure never calls native join.");
backend.ThrowMetadata = false;
Check(browser.BeginJoin(10, true, 273, context), "Retry metadata after exception.");
backend.ThrowJoin = true;
browser.LobbyRefreshed(backend.Token, publicLobby, null, context);
Check(browser.Operation == Operation.Idle && browser.Status.Contains("Native join failed"), "Synchronous native join error releases browser state.");
Console.WriteLine($"PASS: {checks} production-core nongame checks. No Steam or Unity calls executed.");

sealed class FakeBackend : ILobbyBackend
{
    public long Token;
    public int Searches, Refreshes;
    public bool ThrowSearch, ThrowMetadata, ThrowJoin;
    public SearchOptions? Options;
    public List<ulong> Joins = new();
    public void Search(long token, SearchOptions options, string build)
    { if (ThrowSearch) throw new IOException("Offline"); Token = token; Options = options; Searches++; }
    public void CancelDiscovery() { }
    public void RefreshLobby(long token, ulong lobby)
    { if (ThrowMetadata) throw new IOException("Offline"); Token = token; Refreshes++; }
    public void JoinNative(ulong lobby)
    { if (ThrowJoin) throw new InvalidOperationException("Not ready"); Joins.Add(lobby); }
}
