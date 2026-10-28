# 0.6.0 implementation notes

The 0.1.1 search/join fix was confirmed working in a manual test. The new 0.1.2
presentation still needs an in-game visual check. It targets game 1.0.2, Steam build 26231777,
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

The browser requests nearby or worldwide nonfull Steam lobbies advertising
`cbmm_protocol=1`, normally filtered to the local native build. The same
matchmaking marker is checked when copying results and when refreshing a
selected lobby before joining. Unsupported Relay lobbies are outside the query.
Listing another build is an optional display mode; it does not enable joining
that build. The marker is a discovery hint, not authentication or proof of
matching gameplay mods.

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

## Presentation in 0.1.2

The managed `BrowserMenuView` and `BrowserTheme` draw an opaque ink-colored panel,
gold headings, teal actions, five lobby cards per page, and a room-details pane.
Geometry, hit targets and fonts share one scale factor. Long names are measured
and shortened with an ellipsis; full lobby IDs remain visible. GUI colors,
enabled state and draw depth are restored after each draw. Styles are private
to this view and reuse the existing GUI font. No texture/font assets are bundled.

Only UI actions are queued for `Update`. Filter changes wait for the existing
refresh cooldown, the button shows its countdown, and joining is disabled while
the chosen room is being checked. The Steam backend, compatibility policy,
directory reader and native join hooks are unchanged. The injected controller
has no managed-Lobby-parameter callbacks. A basic retained-control fallback is
available if custom drawing fails; it avoids the stripped scroll-view path.

Offline design previews use the production renderer with inert Unity substitutes
and sample rooms. They exercise layout at 720p, 1080p and 1440p, not native Unity
drawing or live connections. `UnityEngine.TextRenderingModule` is an existing
game-owned compile reference; only the two matchmaking DLLs are packaged.

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
Missing/malformed compatibility data or a failed optional bridge mean **mods unknown**, not
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

### Host kick and lobby-scoped exclusion in 0.3.0

F8 adds a local host-only panel, with a fresh confirmation before every manual
kick and an Allow rejoin action. It works while hosting a Steam lobby or a run;
it does not depend on pings, Foundation, or a mod on the target PC. It operates
on a whole NGO/Steam account connection, including local co-op characters.
The GUI queues actions for Update; native calls never run from OnGUI.

`HostModerationService.ReadScope` requires a listening **server** and its current
Steam lobby. Scope includes manager pointer, lobby ID, local Steam account and
local NGO client ID. Losing host authority/ending that lobby clears the in-memory
ledger and pending confirmation. Scene changes do not. A transient missing Steam
self lookup uses the known identity only for the same manager/lobby. Blocks are
not persisted, global Steam bans, IP bans, or bans on a person using other accounts.

The authenticated target is resolved through
`NetworkManager.ConnectionManager.ClientIdToTransportId(clientId)` (including its
success flag), then `SteamTransportController.transport.connectionMapping[id].id`.
The Steam sockets transport builds that map from the remote
`SteamNetworkingIdentity` in PollEvent. `UGSConnectionPayload.playerID` and
displayName are not authority: the payload is supplied by the client. Display
names are bounded and stripped of rich-text delimiters, control and bidi-format
characters; the full verified Steam ID is shown in confirmation.

Confirming a kick revalidates both lobby generation and current client/account
binding, adds the stable account block **before** disconnect, and consumes the
confirmation once. The native adapter rechecks scope and authenticated identity
again immediately before calling `NetworkManager.DisconnectClient(ulong, string)`.
If that native call throws, the block remains because a disconnect may already
have partially completed; a fresh explicit confirmation can retry. A bounded
ledger holds at most 256 accounts. It cannot silently kick when no block can be kept.

An additional Harmony **prefix** on the exact void method
`SteamTransportController.ConnectionApproval(NetworkManager.ConnectionApprovalRequest,
NetworkManager.ConnectionApprovalResponse)` enforces the host's blocks. For a
blocked account it sets Approved/CreatePlayerObject/Pending false, supplies a
reason, and skips the native handler. For everyone else the original executes
normally; this code never grants approval. If authenticated identity cannot be
resolved while the active lobby has blocks, admission fails closed. With no
blocks the original admission path is preserved even if identity is unavailable.
The hook ignores client PCs, local-host approval and callbacks for another
transport instance. Names, lobby metadata and client payload IDs cannot bypass it.

The host update also checks already-connected accounts against the ledger. This
catches the race where a reconnect was approved immediately before Kick was
confirmed. Disconnects are issued once per current client/account binding rather
than repeatedly each frame. Unblocking still leaves all native build/capacity/
whitelist/run restrictions in place. A player may briefly enter Steam's lobby
before NGO admission is rejected; this is not a Steam lobby membership ban.

Cleanup is deliberately the game's normal disconnect lifecycle, not manual actor
destruction. Native SteamTransportController.ClientDisconnected updates its
connected-client roster and calls NetworkManagerEx.ClientDisconnected. The game
then uses NetworkSession.ServerClientDisconnected to remove/broadcast the client
list, broadcast its disconnect and release pause state. NGO's normal object
cleanup also remains intact. No extra ServerClientDisconnected RPC, save edit,
PlayerData.PlayerObject lookup or forced Despawn/Destroy is added. This source
trace does **not** prove that every mid-run character vanishes; a standing
disconnected character is an accepted preview limitation pending manual testing.

`SteamNetworkingSockets Transport for Netcode for GameObjects.dll` is an added
game-owned compile reference with Private=false. Only the existing two plugin
DLLs are shipped. Core and source-linked adapter/controller checks cover host
authority, stable-ID reconnects, spoofed payloads, fresh confirmation, double
callbacks, racing admissions, unknown identity, unblocking, scope resets and
preserving ordinary admission. The compiled callback audit includes the new
controller; the existing ping mutation/disconnect guard still applies to ping
classes only. Real Harmony/IL2CPP approval interception, native disconnect
delivery, character cleanup and subsequent round progression need a manual
host/client test. Relay/cross-platform moderation and permanent bans are deferred.

### Optional radial pings in 0.2.1

No native ping/chat/emote API was found in the inspected game player/session
bindings. This implementation adds a cosmetic IMGUI overlay and its own NGO
named-message channel, `cbmm.pings.v2`; it does not patch a gameplay method.
The four browser input prefixes and two native-join prefixes are unchanged.

`PingWorld` reads `NetworkSession.primaryIdentifier`, `sceneRoot`,
`runDataManager.playerDatas`, `PlayerManager.players`, and each spawned player's
`clientID.Value` / `playerData`. It checks current roster identity before using
an object, avoids the unsafe native `PlayerData.PlayerObject` convenience getter,
and refuses sending while paused, transitioning, restoring, or without a local
spawned player. `Camera.main.ScreenPointToRay` and the live player's
`mouseTargetPlane` capture a ground point at key-down (500-unit ray budget);
host validation limits its distance to 100 world units from the speaker.
Unavailable forward ground intersections disable the location choice. Thanks/Sorry use `Player.agentModel.headTransform` with a
small vertical offset, falling back to the live player root while the model is
unavailable. Nonfinite live poses/projections are skipped before drawing.

The host registers its relay once per listening NGO manager/network-session
instance and advertises its own Steam ID under `cbmm_ping_host_v2` on its owned
lobby. Clients check that tag against Steam's actual current lobby owner **after
membership**, then negotiate protocol 2 through the existing custom-message
manager. Ordinary vanilla hosts receive no ping handshake when that tag is
absent. A host and updated viewers are required for sharing; local-only feedback
remains available without a cooperating host. This handshake never changes
native connection approval, disconnects clients, or waits before starting a run.

The binary packet is exactly 84 bytes, with magic/version/type, an unpredictable
host-session epoch, a monotonic sender sequence, scene NetworkObjectId and name
hash, roster player index, author ID, three finite bounded coordinates, creation
time on NGO's server timeline, and a client-instance nonce. No
remote text, serialization type names, delegates, or object pointers are accepted.
NGO's actual request sender is mapped to a current owned character; requests may
not supply an author. Only the server can send display events. Only peers that
negotiated this channel receive them. Session/scene mismatch, missing ownership,
duplicates, and source sends faster than one per 1.5 seconds are rejected. A
request must match the sender's negotiated nonce. The host assigns a separate
monotonic display sequence, so a restarted client cannot get stuck behind its
old sequence on other PCs. Receivers deduplicate without applying a second
arrival-time cooldown: delayed packets may arrive together. Timestamp checks
allow up to one second of NGO clock skew, cap lifetime at four seconds, deduct
transit time, and discard expired messages. Fixed-size
callbacks copy into a bounded 32-entry queue and do live-object work in Update.
Sender state is capped at eight and pruned on disconnect; handlers, metadata,
capabilities, and sequence gates are cleared on session exit. Four-second
correlated probes renew twelve-second capability leases. A replaced host epoch
can recover without touching the native connection; loss of capability falls
back to local preview. One peer's send failure does not disable the other peers.
Native callback errors are deferred to Update before removing their handler.
No data is persisted.

The injected `PingController` exposes only Unity callbacks; typed helpers are
hidden from IL2CPP. Plain managed `PingView` owns three procedurally generated
ring-sector textures, a shared disc texture, and bounded world labels. Texture
masks are constructed in managed buffers and copied in bulk. It reuses retained IMGUI drawing
calls, restores GUI/cursor state, and disposes textures. One visual per sender
lasts at most four seconds. Near a screen edge, input is centered in the clamped
wheel after the target is captured. The controller waits for that warp, cancels
if it is refused, and restores the original pointer/lock state on close. Focus
loss does not warp another application's pointer. Resize/device changes cancel;
native pause/menu cursor ownership is preserved and world messages stay hidden
over the pause menu. Unload removes the controller. Idle frames do not draw or scan local ownership.
Keyboard/mouse gestures control only the primary character;
the wheel doesn't pause play or block normal movement/attacks. Aim at the surface
before holding the key. Drawing/relay failures disable only that optional subsystem.

`UnityEngine.PhysicsModule` and `Unity.Collections` are game-owned compile
references, not new distributed dependencies. Only the original two plugin DLLs
are deployed. Core checks exercise the production codec, scene/session/authority
policy, distance limits, replay/spam gates, gesture sectors, and malformed input.
The source-linked integration checks exercise the production relay, safe player
lookup, and controller against inert substitutes: delivery, vanilla peers,
ownership, disconnect/send races, host/client relay reset, compressed/delayed
messages, cursor/input cancellation, expired visuals, and failure containment.
A PE metadata/IL check inspects the actual built callback signatures, checks the
GUID/version, and rejects game-data mutations/NGO start/stop/disconnect calls in
the ping classes. It is a narrow regression guard, not a native safety proof.
The build script records source/output hashes after those checks and requires
the same hashes for skipped-build packaging or deployment.
They cannot establish IL2CPP marshalling, ground-target suitability, above-head placement,
or real host/client delivery. Test those manually. Relay/cross-platform transport,
separate couch co-op senders, localization, audio, off-screen
arrows, and occlusion are outside this preview.

An earlier connection-log review also identified that native join-task completion
can precede the NGO connected event, producing a premature failure **status** in
the browser. That status doesn't call disconnect/shutdown. It is not changed by
the ping work and remains a separate reporting issue.

### Nongame versus runtime evidence

The core project is the actual production orchestration/policy, tested using an
inert backend for callback races, stale results, revalidation, context changes,
timeouts, cancellation, and mod/build/host/capacity changes. Tests make no Steam
or Unity calls. The plugin builds against the installed runtime API. Those checks
do not exercise IL2CPP delegate marshalling, Harmony's native hooks, menu drawing,
Steam listing visibility or a real connection. All remain manual test gates.

Only the two matchmaking DLLs are packaged/deployed. No loader, generated game
assemblies, TC/Foundation/Battle Numbers DLLs, saves, raw logs, or local backups
belong in Git. The game hash guard disables unsupported native builds. There is
no mid-run join, reconnect, host migration, skill/rank matchmaking or latency measurement.


### Embedded CC0 icons (0.3.1)

Five Kenney icons add visual cues to the browser/host headers and the G wheel
and world labels. Original PNGs and pinned download hashes are in assets/icons.
UiIcons reads fixed 64x64 alpha resources from the plugin DLL, using the same
bulk Color32 upload API as the existing wheel. No image decoder, network request,
new native reference, or separate artwork installation is needed. Each view owns
and disposes its textures; tint and opacity restore GUI state after drawing.
The build fingerprint includes embedded masks and the ZIP includes artwork credits.
This is presentation only: input, wire protocol, cooldown, joining, and host
moderation policy are unchanged. Offline layouts do not establish Unity rendering.


### Ground location targeting (0.3.2)

The prior all-layer Physics.Raycast could select any near-camera collider,
including a prop or an invisible surface above the arena. Player.GetMouseGroundPos
in the inspected game instead uses Camera.main.ScreenPointToRay and the player's
mouseTargetPlane, then flattens the result to the arena ground. The controller now
reads that generated plane's normal/distance and ray fields, computes the forward
intersection in managed PingGround, and applies the existing 100-unit range rule.
Invalid/parallel/behind-camera/uninitialized planes are rejected. No collider or
input mutation and no native fallback aiming/acquisition call is involved.

The snapshot still happens before the wheel moves the cursor. Locations retain
that world point through camera/player movement; quick messages still follow the
head. The former fixed screen circle is now a bounded 32-segment XZ ground ring
projected through the current camera at draw time (at most eight small strokes
per segment). Labels remain readable billboards. The four-second lifetime,
protocol v2, sender/scene validation and host relaying are unchanged.

This matches the inspected flat arena ground; it does not attach to arbitrary
raised props, sloped meshes, enemies, or moving objects, and provides no occlusion
or offscreen arrow. Core geometry and source-linked drawing/controller checks are
nongame evidence. Actual ground placement and host/client agreement need manual
confirmation, particularly during zoom, camera shake, and scene transitions.


### Tap versus hold (0.3.3)

Key-down captures the ground point and binds the pending gesture to its current
mouse, keyboard, window size and scene, without drawing or touching the cursor.
A release before 0.22 seconds submits one location via the existing relay and
cooldown. Crossing that threshold while held opens the ordinary wheel; releasing
its center cancels. A release observed after the threshold without a held update
cancels, avoiding a false quick ping after a hitch. Escape, focus/menu/scene loss,
pause, resizing and device changes cancel pending taps as well as open wheels.
No packet or wire-protocol change is needed. Both DLLs remain a matched build.

### Assigned-controller pings (0.4.0)

GamepadPings is a managed helper, not an injected MonoBehaviour or native patch.
It scans raw D-pad press edges cheaply, then resolves PingWorld.Local and the
live Player.InputUser.Device.TryCast<Gamepad>(), cross-checked against
GameInput.assignedDevices[player.clientID.Value]. GlobalInput.Device can otherwise
fall back to the last-active device. It never uses Gamepad.current,
lastActiveDevice, a network-supplied device ID, or unassigned extra local pads.
The installed InputActions Player map uses left stick for Move and right stick
for Target with no D-pad binding; the UI map uses D-pad for Navigate. Polling
does not consume events, rebind actions, or disable native input.

Admission requires NetworkSession.Level.ActiveGameState == RUNNING, an unblocked
local primary player, no UIManager.GameInputBlocked, no pause/transition, focus,
and closed browser/host panels. Invalid identity/device, lost pairing, unplug,
remapping, owner change or resize cancels a pending/open gesture. Its device and
player-data identity stay fixed until release. Idle frames do not scan live
player ownership. Keyboard/mouse input can be absent or independently disabled.
Keyboard and controller gestures are serialized, sharing the same sender gate.

A tap before 0.22 seconds submits a location. Holding opens the screen-centered
wheel, with right-stick sector selection and 0.35 dead zone; centered release
cancels. No mouse warp, cursor lock change, native pause, input block, or aim
suppression is used. Combat keeps running; right-stick native aim can still turn
the character. No face button is stolen as cancel (East is native Throw).

Initial stick direction is interpreted in screen space using the gameplay
camera's projection/ray and the live mouseTargetPlane. Neutral stick falls back
to Player.transform.forward. GamepadPingRules.Ahead normalizes XZ and projects
six world units ahead onto that same ground plane. The resulting map point is
captured before radial selection, previewed as PING TARGET, and sent unchanged
through protocol v2. The preview is local, transient, and never sent as a packet.
It does not target enemies/props, perform navigation queries, or change saves.

Pings.GamepadEnabled defaults true. Pings.GamepadButton defaults DpadUp and
accepts only four D-pad directions; changes are intended to apply on restart.
The host/receivers need any compatible protocol-v2 ping build, while the sending
PC needs 0.4.0 for controller input. Extra couch characters and their own device
senders remain outside this preview.

Nongame checks source-link the real helper/controller, with inert paired/unpaired
pads, camera, scene/UI gates, hot-unplug/reassignment, remapping, cooldown and
packet submission. Metadata audit includes GamepadPings in the getter-only and
no-network-lifecycle guards. These checks cannot prove IL2CPP device casting,
Steam Input layouts, physical stick feel, native menu gating, facing correctness,
or host/client visuals; those still need manual testing. No game was launched.


### Party text chat (0.5.0)

Chat is a separate opt-in capability: cbmm.chat.v1 / cbmm_chat_host_v1. Its
ChatNetwork follows the established owner-tag/nonce/epoch/lease negotiation,
without changing PingNetwork or ping protocol v2. Callback code copies a bounded
packet into a 32-entry mailbox; Update authenticates/relays it. Only connected,
handshaken peers receive text, and clients never probe an unadvertised host.

ChatProtocol has a 64-byte header and at most 800 text + 128 name UTF-8 bytes.
Strict decoding rejects invalid UTF-8, extra/truncated/reserved bytes, invalid
message roles/epochs, noncanonical text and oversized fields. Text is capped at
200 Unicode scalar values; roster names at 32. Controls, bidi/format characters
and angle-bracket delimiters are removed, with GUI rich text disabled. Request
names/authors cannot be supplied by clients: relay identity uses the native
sender and a matching PingWorld.Find owned character. The relay supplies its
native ClientIdentifier.DisplayName. A source-time PingGate applies the shared
1.5-second limit and replay checks. Receivers require the host/epoch and a newer
global display sequence. Delivery expires after ten seconds; bounded requests
and displays may wait for scene readiness, without an offline transcript.

ChatController is the fourth injected component. All typed helpers are hidden
from IL2CPP; public callbacks remain Unity callbacks and scalar bool properties.
Text input uses the retained Keyboard.add/remove_onTextInput Action<char> API,
not stripped IMGUI TextEditor paths. A capped char queue and surrogate-aware
ChatDraft provide append/backspace input. There is no paste, arbitrary command
execution, IME composition overlay or gamepad keyboard in this version.

Opening requires a ready spawned primary player, no pause/native input block,
and closed browser/host/ping panels. It borrows one Player.PushInputBlock and
UIManager.PushGameInputBlock<UIManager> lease, releasing each once with native
Pop counterparts. The UI generic uses an existing native reference type. These
methods increment/decrement the inspected native counters; no counters are
manually edited. Counter reset/destruction cleanup skips already-cleared leases.
Close retains the typing guard for 0.15 seconds through the close-key frame.
NetworkSession.CanPause (bool, no arguments) has a narrow prefix returning false
while that guard is active, avoiding CallbackContext-struct detours. Existing
plugin hotkeys check the same guard. Unload calls synchronous ShutdownCurrent
before unpatching, and rendering/subscription failures release input and isolate
chat. This is not an invulnerability or network pause feature.

History is a 50-entry FIFO, session/relay scoped and memory-only. The open panel
pages up to five complete inline messages, adapting page capacity to the available
height and draft size. The closed panel previews five single-line messages for
twelve seconds; long previews shorten with an ellipsis and remain complete in
the open history. Sender labels share the first message line.
ChatView measures and caches bounded wrapped lines and never inserts remote
markup. The build/package still ships only our two DLLs and documents/licenses.

Nongame checks use real protocol, relay, draft, controller and view code with
inert native substitutes: host/client delivery, spoofing/replays/rate limits,
vanilla-host isolation, Unicode/malformed payloads, input/subscription pairing,
counter reset and draw/unload failures, bounded history and layout. Native
Action<char> marshalling, generic input-block execution, Harmony pause gating,
actual gameplay suppression, font coverage and physical keyboard/host/client
behavior remain manual test gates. The game was not launched or installed.


### Native username profanity filter reused for chat (0.5.1)

The inspected PlayerNameFilter.Sanitize(string) (token 100673130) calls the
existing Crosstales.BWF.BWFManager.ReplaceAll with PlayerNameFilter.LANGUAGES.
SteamTransportController calls it for player names. Its fixed language array
contains English, German, Spanish, French, Italian, Japanese, Korean, Portuguese,
Russian, Turkish and Chinese. The wrapper itself imposes no username-length
truncation: it passes the string to ReplaceAll. We keep chat/name limits outside
the native call. No proprietary dictionaries are copied or redistributed.

Native Sanitize returns its input unchanged when the manager is missing or not
ready. ChatProfanity therefore reads the lowercase BWFManager.instance field
getter and isReady first, avoiding the potentially creating/searching uppercase
Instance accessor. It never creates/loads/configures the manager or changes its
language list, replacement settings, or game username handling. Native failure
returns no text, logs one exception TYPE without its message/original chat, and
keeps text withheld until the relay/session resets. Normal loading readiness
can recover without a reset. A fully removed result becomes ***; all outputs
are re-sanitized and bounded before entering the protocol/history.

ChatNetwork filters outgoing local text, then re-filters authenticated host
relay text and native sender names. A client bypassing its editor cannot bypass
an updated host. Receivers filter valid host displays as well, covering older
0.5.0 hosts; withheld display sequences are consumed without storing raw text.
Filtering runs in Update/Submit, never in the native receive callback. It is
mandatory for this build, with no opt-out setting. Chat v1/ping v2, acquisition,
movement, host admission and game saves are unchanged.

Assembly-CSharp-firstpass.dll is a compile-only Private=false reference to the
installed runtime interop types. The distributable remains just our two DLLs
plus README/licenses/checksums. Nongame tests provide an explicit fake filter
sentinel (badword), not the actual dictionary: native API routing, loading,
errors, missing service, no auto-creation, length limits, Unicode, names, host
bypass resistance, and old-host receiver filtering are covered. They cannot
prove the native word lists, replacement results, or native filter readiness in
this launch. Confirm those manually; no game launch or deployment was performed.


### T-to-talk default (0.5.2)

Chat.OpenKey now defaults to T. Enter and numpad Enter still submit while typing;
Esc cancels. Existing local configurations must set OpenKey = T explicitly,
since a changed BepInEx default does not override an existing Enter preference.
The source-linked controller checks cover T opening, no Enter opening under
that binding, normal T characters in the draft, Enter submission and cleanup.


### Browser presence and Solo chat preview (0.6.0)

Browser.Enabled controls both F6 and the main-menu launcher after restart.
The browser service keeps ticking while its UI is disabled so a host still
publishes its presence. Protocol/owner/name/waiting-state metadata are separate
from AdvertiseCompatibility, which now controls only the optional Foundation
digest. Disabling that digest deletes its key without removing host presence.
No change is made to lobby visibility, native admission or run-state rules.

ChatNetwork explicitly recognizes GameManager.SERVER_SINGLE_PLAYER before
checking a Steam lobby or listening relay. This mode emits only local filtered
preview packets, with the existing cooldown and owned-character validation.
It registers no message handler, publishes no chat tag and sends no traffic.
A session-pointer change or leaving Solo clears local history/bookkeeping.
Missing multiplayer host capability still reports unavailable; it never becomes
a Solo fallback. The UI labels this preview explicitly. Nongame checks exercise
the source-linked adapter; real Solo UI/filter behavior and multiplayer delivery
remain manual checks.
