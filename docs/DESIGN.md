# 0.1.1 implementation notes

The panel and public searches have been observed in a manual test. Connection
success still awaits a manual test of this fix. It targets game 1.0.2, Steam build 26231777,
GameAssembly SHA-256
`ba3ef02fa87da4c41bb27d7610539c9578132204f0fb5713833e2ef93415ce29`.
BepInEx's generated runtime interop assemblies are compile references only;
inspection stubs, native game binaries and generated bindings are not shipped.

## Discovery and joining

Native `SteamTransportController.<CreateOnlineLobby>d__44.MoveNext` requests
`SteamMatchmaking.CreateLobby(Public, 4)`. Native creation writes `joincode`,
`transport_type=steam`, and a `build` signature formed from the game version and
build flavour. It initially closes joining while the native host gets ready.
The browser does not change those fields, visibility, member limits or admission.

The browser requests nearby or worldwide nonfull Steam lobbies, normally filtered
to the local native build. There is **no browser-mod advertising/tag filter**:
native public lobbies can appear even without this plugin. Unsupported Relay
lobbies are outside the query. Listing another build is an optional display
mode; it does not enable joining that build.

It copies IDs and data inside its own rooted `CallResult<LobbyMatchList_t>`
callback. Pagination bounds UI work; search count is bounded to 50. Results older
than 60 seconds need a refresh. On Join it requests `RequestLobbyData` and waits
for a matching lobby-level `LobbyDataUpdate_t`, then rechecks build,
transport, capacity, and advertised Foundation compatibility. Metadata is a hint:
Steam notifications have no per-request nonce, joinability has no native getter
here, and state can change after the refresh. Native approval remains authoritative.

**0.1.0 directory bug:** `GetLobbyOwner` is a membership-only API and returns nil
before joining. The old reader treated that expected nil as an invalid lobby and
displayed `Host 000000`, disabling Join for normal public rooms. The directory
reader now has no owner/member-identity lookup in its interface. It reads only
public metadata and member counts; an unknown owner is valid. Missing room names
use `Lobby <ID>`. The native join obtains the real owner after entering Steam's
lobby. See [Valve's GetLobbyOwner contract](https://partner.steamgames.com/doc/api/ISteamMatchmaking#GetLobbyOwner).

The join action uses `JoinCodeTag.Format(Steam, numericLobbyId)` and
`GameManager.JoinRemoteServer(string)`. This preserves the game's own transition,
transport selection, wait and timeout handling. Native `JoinLobby(string)` parses
numeric lobby IDs and delegates to `JoinLobby(CSteamID)`. It does not re-run a
join-code search for a numeric ID. There is no direct `StartClient`, forced
connection approval, custom network transport, automatic run start or combat hook.

## Hook inventory

Harmony prefixes on `GameManager.JoinRemoteServer(string)` and
`SteamTransportController.JoinLobby(string)` invalidate/cancel **our** pending
discovery and request UI closure before native joining takes over. Originals run
with unchanged arguments/return values. This avoids competing with native code
entry/invite searches. The browser also refuses to search during a pending native
connection or outside the main menu. Other third-party lobby-search plugins are
not coordinated by this version.

Prefixes on the four void methods `UIMainMenu.InvokeSelection`, `HandleSubmit`,
`HandleCancel`, and `HandleNavigation` suppress underlying main-menu actions only
while our panel is open. F6/Escape/Close releases that gate. No async-return method
is skipped. The panel uses the basic IMGUI controls retained by the game; it avoids
the stripped IMGUI scroll-view path, uses pages, and queues network/UI actions for
`Update` rather than executing them in `OnGUI`. Cursor state is restored on close
or destruction. Other game menus should be closed before opening the browser.

## Optional Foundation identification

Both browser hosts and clients may have Foundation installed. A read-only managed
reflection bridge obtains `MultiplayerServices.Fingerprint()` when available.
This method is internal, so it is an explicitly provisional integration; failure
or a changed method yields Unknown. There is no compile-time Foundation dependency
and no modification of its DLL, ownership, checkpoints, or handshake.

The native lobby owner can publish `cbmm_protocol=1`, `cbmm_owner=<ownerSteamId>`,
`cbmm_state=waiting|closed`, `cbmm_name=<displayName>`, and `cbmm_compat=<FoundationDigest|unknown>` every two
seconds when changed. Waiting requires native `isLobbyOpen` and a loaded
`GameServerLobby` scene. It never opens that lobby itself. The advertised owner is
unverified outside membership; it is never substituted for an actual Steam owner.
An advertised owner change during refresh requires reselection. Digests are
compared as advertised hints and a known difference blocks the browser's Join.
No tag, malformed tags, or a failed optional bridge mean **mods unknown**, not
vanilla. Only a local Foundation install requires confirmation for an unknown
setup; a browser-only install can attempt ordinary native admission directly.
The digest covers only Foundation-registered content, prices, enable settings
and registered gameplay binaries. Other mods may still be incompatible. Names
and remote metadata are bounded/sanitized for display and never treated as code.

All interaction uses the game's existing Steam client/callback pump on the game
thread. The plugin does not initialize or shut down Steam, query in the background,
publish an external player directory, or require a matchmaking server/API key.
On unload it cancels its handlers and removes its own metadata when still owner.
Native connection tasks belong to the game and are not cancelled by closing UI.

## Validation boundary

The core project is the actual production orchestration/policy, tested using an
inert backend for callback races, stale results, revalidation, context changes,
timeouts, cancellation, and mod/build/host/capacity changes. Tests make no Steam
or Unity calls. The plugin builds against the installed runtime API. Those checks
do not exercise IL2CPP delegate marshalling, Harmony's native hooks, menu drawing,
Steam listing visibility or a real connection. All remain manual test gates.

Only the two matchmaking DLLs are packaged/deployed. No loader, generated game
assemblies, TC/Foundation/Battle Numbers DLLs, saves, raw logs, or local backups
belong in Git. The game hash guard disables unsupported native builds. There is
no mid-run join, reconnect, host migration, skill/rank matchmaking or ping probe.
