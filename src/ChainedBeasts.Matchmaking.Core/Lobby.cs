namespace ChainedBeasts.Matchmaking.Core;

public sealed record Lobby(
    ulong Id, ulong Owner, string Title, string Build, string Transport,
    int Members, int MemberLimit, string Protocol = "", string Fingerprint = "",
    string AdvertOwner = "", string AdvertState = "");

public sealed record BrowserContext(bool Available, string Reason, string Build, string? Fingerprint);
public sealed record SearchOptions(bool Worldwide = false, bool OtherBuilds = false);
public enum Operation { Idle, Searching, CheckingLobby, Joining }
public enum Compatibility { Unknown, FoundationMatched, Blocked }
public sealed record Assessment(Compatibility Compatibility, string Detail)
{
    public bool Allowed => Compatibility != Compatibility.Blocked;
}

public static class LobbyPolicy
{
    public const string ProtocolKey = "cbmm_protocol";
    public const string FingerprintKey = "cbmm_compat";
    public const string OwnerKey = "cbmm_owner";
    public const string StateKey = "cbmm_state";
    public const string NameKey = "cbmm_name";
    public static readonly string[] AdvertKeys = { ProtocolKey, FingerprintKey, OwnerKey, StateKey, NameKey };

    public static Assessment Assess(Lobby lobby, BrowserContext context)
    {
        // Steam deliberately withholds GetLobbyOwner until this client joins.
        // An unknown owner (zero) is normal for a valid public search result.
        if (lobby.Id == 0) return Block("Invalid lobby identity.");
        if (string.IsNullOrEmpty(context.Build) || !string.Equals(lobby.Build, context.Build, StringComparison.Ordinal))
            return Block("Different or missing game build.");
        if (lobby.Transport != "steam") return Block("This prototype supports Steam transport only.");
        if (lobby.Members < 1 || lobby.MemberLimit is < 1 or > 4 || lobby.Members >= lobby.MemberLimit)
            return Block("Full lobby or invalid Steam member count.");
        var advertValid = lobby.Protocol == "1" &&
            ulong.TryParse(lobby.AdvertOwner, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var advertisedOwner) && advertisedOwner != 0 &&
            (lobby.Owner == 0 || lobby.Owner == advertisedOwner);
        if (advertValid && lobby.AdvertState != "waiting") return Block("Host is not advertising a waiting lobby.");
        if (advertValid && IsDigest(lobby.Fingerprint))
        {
            if (!IsDigest(context.Fingerprint)) return Block("Host advertises Foundation content; no local Foundation digest is available.");
            if (!string.Equals(lobby.Fingerprint, context.Fingerprint, StringComparison.OrdinalIgnoreCase))
                return Block("Foundation gameplay versions or settings differ.");
            return new(Compatibility.FoundationMatched, "Advertised Foundation match; native handshake still required. Other mods are not checked.");
        }
        return new(Compatibility.Unknown, "Mods unknown. The game's normal connection checks still apply.");
    }

    public static bool NeedsModConfirmation(Assessment assessment, BrowserContext context) =>
        assessment.Compatibility == Compatibility.Unknown && IsDigest(context.Fingerprint);

    public static bool IsDigest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static Assessment Block(string reason) => new(Compatibility.Blocked, reason);

    // Steam display names/metadata are remote input. Do not render tags/control characters.
    public static string Display(string? text, int maximum = 44)
    {
        if (maximum < 1) return "";
        var clean = new string((text ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(maximum).ToArray()).Trim();
        return clean.Length == 0 ? "Unknown" : clean;
    }
}
