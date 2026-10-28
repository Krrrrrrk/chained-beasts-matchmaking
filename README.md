# Chained Beasts Matchmaking

I wanted a way to find public Chained Beasts games without swapping lobby codes.
This is my first pass: a small Steam lobby browser that uses the game's existing
hosting and joining system.

**0.6.2.** I've added party text chat alongside controller
and keyboard pings, the lobby browser and host controls. Builds and nongame
checks pass, and I've tested the browser, pings and Solo chat locally.
Physical-controller and full multiplayer checks still need more testing.

The menus and ping wheel use embedded CC0 icons tinted to the teal, gold, and
dark-purple interface. Thanks/Sorry keep their text labels as well as an icon.
Artwork sources and licenses are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## What it does

- Adds **Find public games** at the main menu. **F6** also opens/closes it.
- Scales the panel and text to the window, highlights the selected room, and shows a refresh countdown.
- Lists open Steam lobbies advertising Chained Beasts Matchmaking.
- Shows advertised room names (or a lobby ID), Steam member counts, and available mod information.
- Offers regional or worldwide searches and pages through the results.
- Refreshes the selected lobby before joining through the game's normal flow.
- Keeps known game-build and Foundation/TC mismatches from joining through the browser.
- Adds **tap G** location pings and an optional **hold G** wheel during a run: mark a location or display **Thanks!** / **Sorry!** above my character.
- Adds **F8** host controls to kick an account, block rejoining this lobby, or allow rejoin again.

Steam only returns searchable, joinable, nonfull lobbies. Private/friends-only
lobbies do not appear. The first preview supports the Steam transport on Windows;
Unity Relay/cross-platform discovery is outside this version. Member counts mean
Steam accounts, so a couch co-op player may not count as another Steam member.

**“Mods unknown” does not mean vanilla.** The matchmaking marker identifies
browser hosts; it does not establish which gameplay mods they use. With only
this browser installed, select a room and join normally.
If I also have Foundation/TC installed, hosts without Foundation information need confirmation that
their setup matches. When a host publishes matching Foundation information, the
label says **Advertised Foundation match**. This is a metadata hint covering
Foundation-registered gameplay content/settings; unrelated mods aren't covered.
Native admission and Foundation's handshake still decide whether a connection
is accepted.

Steam hides lobby-owner identities until joining, so listings without a name may
show `Lobby <ID>` instead of a host's name. That is expected and does not make
the room invalid. Public rooms can still fill, close, or reject a connection
after a search; the browser does not override the host's admission rules.

## Install

Requires Chained Beasts **1.0.2 / Steam build 26231777**, Unity **6000.5.11f1**, and
**BepInEx 6.0.0-be.791+b782da4, Windows x64 Unity IL2CPP**.
Download the loader from the [official BepInEx build page](https://builds.bepinex.dev/projects/bepinex_be/791)
and follow the [IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
The loader and game files are not included here. A different native game build
disables this prototype until its integration has been checked.

Download the mod ZIP from [GitHub Releases](https://github.com/Krrrrrrk/chained-beasts-matchmaking/releases/latest).
With the game closed, merge its `BepInEx` folder into the game folder. It installs
two files together:

```text
BepInEx/plugins/ChainedBeasts.Matchmaking/ChainedBeasts.Matchmaking.dll
BepInEx/plugins/ChainedBeasts.Matchmaking/ChainedBeasts.Matchmaking.Core.dll
```

Foundation, Terms & Contusions, and Battle Numbers are optional. The browser does
not replace their DLLs. Hosts need this matchmaking mod and must advertise a
waiting, joinable lobby to appear in the browser. This is discovery metadata,
not a guarantee that every gameplay mod matches.

At the main menu, click **Find public games** or press **F6**. Refresh, select a
lobby, check its details, then join. The host still starts the run. There is no
automatic join, run start, mid-run joining, reconnect, or host migration feature.
Close the browser before using the game's other menus.

## Pings and quick messages

Point at a nearby floor spot and **tap G** for a location ping. A quick tap never
opens the wheel or moves the cursor.

**Hold G for 0.22 seconds** to open the wheel, move toward a choice, and release:

- **Up: Ping here.** Marks the floor point under the cursor when I first held G.
- **Lower right: Thanks.** Shows `Thanks!` over my character as I move.
- **Lower left: Sorry.** Shows `Sorry!` over my character as I move.
- Once the wheel is open, release at the center or press **Esc** to cancel.
- Tap and wheel messages share the same cooldown.

### Controller

During live combat, my primary character's assigned controller can use:

- **Tap D-pad Up:** ping six ground units ahead. Tilt the right stick to aim;
  with the stick centered, it uses the character's facing direction.
- **Hold D-pad Up for 0.22 seconds:** open the same wheel at the screen center.
  Use the right stick: up for Ping here, lower right for Thanks, lower left for Sorry.
- **Release D-pad Up:** send the highlighted choice. Return the stick to center
  before releasing to cancel.

A **PING TARGET** ring previews the location captured when I first pressed the
button. Moving the stick to choose a message doesn't move that saved target.
The controller wheel never moves the mouse cursor or requires a mouse/keyboard.
Combat keeps running, and the right stick may still turn my character while
choosing; this doesn't change the game's bindings or block attacks/movement.

Controller pings are disabled in merchants, training, potion/surgery menus,
pause/results screens and other native input blocks. D-pad menu navigation stays
with the game. Only the controller assigned to the account's primary character
can send; another couch co-op player's pad cannot operate that character's wheel.
Separate pings for extra local characters aren't implemented.

Messages last four seconds, with a 1.5-second cooldown and one active marker per
account. Delayed network messages use their remaining time rather than restarting
that timer. Location pings must be within 100 world units and use a small pulsing
ground ring. The target comes from the game's mouse-aim ground plane, so props
and character colliders cannot move it up toward the camera. It stays at the
point chosen before the wheel opens; the ring follows the floor perspective as
the camera pans or zooms. Thanks/Sorry follow the character's head when its model is loaded.
The game keeps running
while the wheel is open; no mouse click is needed. Use a different `HoldKey` if
G is already assigned to something else.

Near a screen edge, the pointer moves into the wheel so every direction remains
reachable. Closing it restores the original aim point. Losing focus, resizing,
or changing input devices cancels the selection.

**For shared pings, the Steam host and each person viewing them need pings
enabled in 0.2.1 or newer. Controller input requires 0.5.1 on the sending PC.** An unmodded player can still play normally but won't see these
messages. Without an updated host, the wheel says **Local preview** and only I
see my marker. The host can relay between updated clients without requiring
Foundation, TC, or Battle Numbers. This doesn't add admission checks or kick
players who don't have the update.

This preview supports keyboard/mouse and the primary character's assigned
controller. Separate wheels for extra couch co-op characters, translated labels,
sounds, off-screen arrows, and wall occlusion aren't included. Markers
clear during scene changes or when the speaker despawns; nothing is saved.

## Party chat

With a local character in the hosted lobby/free-roam area or a run, press
**T** to type. **Enter sends**, **Esc cancels**, and **Backspace** deletes.
Use **Page Up / Page Down** to read older/newer messages. The closed panel briefly
shows the five newest messages as inline sender/text rows; opening chat shows
their full text, using fewer rows per page when long messages need more space.

Party chat requires **0.5.0 or newer with Chat enabled**; use **0.5.1 on the
host and receiving PCs** for the game profanity-filter integration.
Chat uses its own host relay and doesn't depend on pings being enabled. With an
older/non-chat host it says unavailable and won't pretend a message was shared.
In native Solo mode, **Solo chat preview** lets me test typing and the filter
alone. Only I see those messages; nothing is transmitted. Leaving Solo or
starting a new session clears that local history.
Only participants who negotiated chat receive chat packets. There is no public
server/global chat, external service, voice chat or Steam-friends messaging.

Typing temporarily blocks my local character's gameplay input; the match keeps
running. Esc closes chat without intentionally opening pause. Chat closes on
pause, scene transitions, focus/keyboard loss, a new input block or unload, and
returns its temporary input blocks. Other clients keep playing normally.
Pings, the browser and F8 hotkeys are suppressed while typing.

Messages are limited to **200 Unicode characters**, one every **1.5 seconds**.
The host identifies the sender from the actual connection/owned character and
supplies a sanitized roster name. Names and text render as plain text. History
keeps the newest **50 messages in memory**, clearing on session/relay reset;
the plugin doesn't write chat bodies to disk. Joining later doesn't fetch an
earlier transcript, and delivery during transitions has a bounded ten-second
expiry rather than an offline queue.

Chat text and sender names use the **same built-in filter as player names**.
My client filters outgoing text, the host filters every relayed request, and
updated receivers filter incoming messages too (including from an older host).
This uses the game's existing languages, dictionaries and replacement behavior;
it doesn't introduce a separate word list or change the game's filter settings.
While the native filter is loading/unavailable, text is withheld and chat shows
that status instead of passing the original through. A native filtering error
withholds text until a relay/session reset; normal gameplay and pings continue.
The actual native dictionary coverage still needs an in-game check.

This first editor needs a **physical keyboard**, including for controller players.
It supports appending text and Backspace; clipboard/paste, caret selection,
controller on-screen keyboards, IME composition UI and chat commands aren't
implemented. Unicode input is accepted, but the installed font's glyph coverage
still needs testing. Menu/gameplay input cleanup also needs a manual test.

## Host controls

While I'm hosting a **Steam** lobby or run, **F8** opens the player list. I can
choose **Kick + block rejoin**, check the name and Steam ID, then confirm. It
disconnects that entire Steam account, including any local co-op characters.
The game keeps running while the panel is open.

The block follows the authenticated Steam account, so changing their displayed
name or reconnecting with a different connection number doesn't bypass it.
Admission is checked for public-browser, code, and invite joins. **Only the host
needs this update for kicking/blocking; the other player can be unmodded.**

Blocks are **for this hosted lobby**, not permanent account bans. They stay
through rounds and scene changes, clear when I stop hosting that lobby, and
aren't saved. **Allow rejoin** removes a block immediately; the game's usual
build, capacity, whitelist, and run-state restrictions still apply. I can't kick
my own account, and joining clients don't get host controls.

Character removal uses the game's normal disconnect cleanup. I haven't manually
verified that path yet; a disconnected character could remain standing. This
version doesn't force-despawn actors or delete roster/save data to remove it.
It supports the inspected Steam transport, not Relay/cross-platform hosting.

## Configuration and removal

After its first load, settings are in
`BepInEx/config/local.chainedbeasts.matchmaking.cfg`:

```ini
[General]
Enabled = true

[Browser]
Enabled = true
ToggleKey = F6
AdvertiseCompatibility = true

[Pings]
Enabled = true
HoldKey = G
GamepadEnabled = true
GamepadButton = DpadUp

[Chat]
Enabled = true
OpenKey = T

[HostTools]
Enabled = true
ToggleKey = F8
```

`[General] Enabled = false` disables the plugin after restarting.
`[Browser] Enabled = false` hides Find public games and disables F6 after
restarting, while keeping pings, chat, host controls and host discovery metadata. `[Pings]
Enabled = false` disables just the wheel, its messages, and its relay after
restarting. `GamepadEnabled = false` disables controller input while keeping G
and received pings. `GamepadButton` accepts `DpadUp`, `DpadDown`, `DpadLeft` or
`DpadRight`; restart after editing. `AdvertiseCompatibility` controls the optional
Foundation digest; matchmaking presence still advertises on my existing Steam
lobby. Neither setting makes a private lobby public or reopens a closed game.
`[Chat] Enabled = false` disables the chat panel, typing and relay after restarting.
Use a chat key that differs from browser, ping and host-control keys.
`[HostTools] Enabled = false` disables the host panel and rejoin-block hook after
restarting. Use different keys for host controls and pings. Close the ping wheel
before opening F8.

To uninstall, close the game and remove the `ChainedBeasts.Matchmaking` plugin
folder above. Its config can also be removed. Leave the loader and other mods
alone. This mod stores no run ownership, progression, or custom save data.
For an upgrade rollback, restore both old matchmaking DLLs together from your
backup. The local deployment script records a backup path and copies current
user data before installing.

## Build

Install **PowerShell 7** and a .NET SDK capable of targeting **net6.0**. Generate
the game's runtime interop assemblies through the installed IL2CPP loader first.
No shipped game assemblies are included in the repository.

```powershell
pwsh -NoProfile -File scripts/build.ps1 -Package
# Optional: install only this browser while the game is closed.
pwsh -NoProfile -File scripts/build.ps1 -Package -Deploy
```

For another Steam library, add `-GameDir 'D:\SteamLibrary\steamapps\common\Chained Beasts'`.
The script builds both DLLs, runs the production-core nongame checks, and can
package/deploy them. It also runs the actual ping relay/controller code against
inert game/Steam/NGO substitutes and inspects the built plugin's callback surface.
Source-linked presentation checks cover ground-ring projection while the camera
pans or zooms and preserve the separate above-head quick messages.
Those checks do not run Unity or establish real multiplayer success.
`-SkipBuild` packaging/deployment requires matching source and DLL hashes from
the last successful checks. The script never starts or stops the game. Output, backups, logs,
and copied binaries stay ignored. See the [implementation notes](https://github.com/Krrrrrrk/chained-beasts-matchmaking/blob/main/docs/DESIGN.md)
for the native integration and remaining limits.

## First manual test

1. Start at the main menu and check that the button/F6 panel opens and closes.
2. Refresh regionally, then worldwide. An empty list is a valid result.
3. Have a friend host a fresh normal **Steam** lobby and wait before starting.
   Their browser mod is optional. Refresh and find their lobby; ordinary rooms
   may show a lobby ID rather than the host's name.
4. Check the mod setup, select their lobby, and join. Confirm native character
   selection and ready/start behavior still work on both PCs.
5. Separately check code-entry/invites, closing a listed lobby, a full lobby, and
   cancellation/timeout. Test matching and mismatched Foundation parties too.
6. With pings enabled on both PCs, enter a fresh Steam run. Tap G once and
   confirm one location ping with no wheel or cursor jump. Then hold G for
   the wheel and try all three
   wheel choices from both host and client. Confirm the same location is marked
   on both screens and Thanks/Sorry follow the correct character. Mark a floor
   detail, then move until the camera pans/zooms: the ring should stay on that
   detail, even when pointing through a character or a prop.
7. Try a center release, Esc, repeated quick sends, a scene change, and leaving
   the session. Open near a screen corner, resize, and briefly switch focus.
   Check cursor restoration, expiration, and no carried-over markers.
8. With a host who doesn't have the update, confirm the wheel explicitly says
   Local preview. Nobody else should see its output.
9. While hosting, open F8 and cancel a kick confirmation first. Then kick a
   consenting friend in the lobby and try their code/invite/public rejoin. It
   should be denied until **Allow rejoin**, subject to normal game eligibility.
10. Repeat during a fresh run. Check that the correct account disconnects, what
    happens to its character, and that the remaining players can still control
    their characters and finish/transition after the round. End the lobby and
    verify a new hosted lobby starts with an empty block list. The client doesn't
    need this mod for this test.

11. With the primary character's paired controller, tap/hold D-pad Up during
    combat and try all three right-stick sectors. Check the preview stays at
    the captured floor point, center-release cancels, and the cursor doesn't move.
    Try with mouse/keyboard disconnected, during camera movement, and from host
    and client. Confirm D-pad navigation still works in each native menu.
12. Unplug/reconnect or reassign the pad with the wheel open, open pause/results,
    and try a second local controller. There should be no stale send or ownership
    mix-up; a fresh press on the primary character's paired pad should work again.

13. With chat enabled on host and client, press T, type a short message,
    send it, and reply from the other PC. Check each sender/character label,
    Unicode input, shared cooldown, history paging, and long-message wrapping.
14. While typing, verify W/A/S/D, attack/execute keys, G and F8 don't act on
    gameplay. Esc should cancel chat; the next fresh Esc should pause normally.
    Test a scene change, focus loss, keyboard unplug and closing the game while
    typing. Movement/controls must recover with no stuck input block.
15. Check an older/non-chat host and a mixed party: unavailable status should be
    explicit, older players should receive no unknown chat packets, and their
    normal gameplay and pings should continue.

16. On 0.5.1, send an ordinary sentence and a word the game's username filter
    recognizes, from both host and client. Confirm the same native replacement
    appears in messages and names, with no gameplay/input regression. Check the
    loading/unavailable status if the game's filter isn't ready yet.

Use a friend's fresh lobby for the first test. Steam discovery/joins are not
verified just because the DLL builds.

**Bug reports:** please include both `BepInEx/LogOutput.log` from the game folder
and `%USERPROFILE%/AppData/LocalLow/Featherweight Games/ChainedBeasts/Player.log`.
Include `Player-prev.log` if you've relaunched since the failure. Tell me whether
you were browsing, hosting or joining, and what mods each PC had installed.
For ping reports, include which PC sent it and whether the wheel said Shared or
Local preview.

Steam API behavior: [Valve's matchmaking documentation](https://partner.steamgames.com/doc/api/ISteamMatchmaking#RequestLobbyList).
