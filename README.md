# Chained Beasts Matchmaking

I wanted a way to find public Chained Beasts games without swapping lobby codes.
This is my first pass: a small Steam lobby browser that uses the game's existing
hosting and joining system.

**Experimental 0.1.2 preview.** I've given the browser a clearer dark panel,
gold headings, teal actions, lobby cards, and a separate room-details pane.
The search/join flow carries over from 0.1.1. Builds and nongame checks pass;
the new styling still needs an in-game visual check. Other mods' gameplay or
round-transition behavior is outside this browser's scope.

## What it does

- Adds **Find public games** at the main menu. **F6** also opens/closes it.
- Scales the panel and text to the window, highlights the selected room, and shows a refresh countdown.
- Lists open Steam lobbies, including hosts who haven't installed this mod.
- Shows advertised room names (or a lobby ID), Steam member counts, and available mod information.
- Offers regional or worldwide searches and pages through the results.
- Refreshes the selected lobby before joining through the game's normal flow.
- Keeps known game-build and Foundation/TC mismatches from joining through the browser.

Steam only returns searchable, joinable, nonfull lobbies. Private/friends-only
lobbies do not appear. The first preview supports the Steam transport on Windows;
Unity Relay/cross-platform discovery is outside this version. Member counts mean
Steam accounts, so a couch co-op player may not count as another Steam member.

**“Mods unknown” does not mean vanilla.** Most ordinary lobbies have no mod
information. With only this browser installed, select a room and join normally.
If I also have Foundation/TC installed, unmarked hosts need confirmation that
their setup matches. When a host publishes matching Foundation information, the
label says **Advertised Foundation match**. This is a metadata hint covering
Foundation-registered gameplay content/settings; unrelated mods aren't covered.
Native admission and Foundation's handshake still decide whether a connection
is accepted.

Steam hides lobby-owner identities until joining, so ordinary listings often
show `Lobby <ID>` instead of a host's name. That is expected and does not make
the room invalid. Public rooms can still fill, close, or reject a connection
after a search; the browser does not override the host's admission rules.

## Install

Requires Chained Beasts **1.0.2 / Steam build 26231777**, Unity **6000.3.21f1**, and
**BepInEx 6.0.0-be.791+b782da4, Windows x64 Unity IL2CPP**.
Download the loader from the [official BepInEx build page](https://builds.bepinex.dev/projects/bepinex_be/791)
and follow the [IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
The loader and game files are not included here. A different native game build
disables this prototype until its integration has been checked.

There is no tested release yet. Build locally or use a preview ZIP supplied with
this project. With the game closed, merge its `BepInEx` folder into the game
folder. It installs two files together:

```text
BepInEx/plugins/ChainedBeasts.Matchmaking/ChainedBeasts.Matchmaking.dll
BepInEx/plugins/ChainedBeasts.Matchmaking/ChainedBeasts.Matchmaking.Core.dll
```

Foundation, Terms & Contusions, and Battle Numbers are optional. The browser does
not replace their DLLs. Hosts do not need the browser just to appear in a search;
they need it only to publish the optional browser compatibility tags.

At the main menu, click **Find public games** or press **F6**. Refresh, select a
lobby, check its details, then join. The host still starts the run. There is no
automatic join, run start, mid-run joining, reconnect, or host migration feature.
Close the browser before using the game's other menus.

## Configuration and removal

After its first load, settings are in
`BepInEx/config/local.chainedbeasts.matchmaking.cfg`:

```ini
[General]
Enabled = true

[Browser]
ToggleKey = F6
AdvertiseCompatibility = true
```

`Enabled = false` disables the browser after restarting. The advertising switch
only publishes namespaced metadata on your own existing Steam lobby; it does not
make a private lobby public or reopen a closed game.

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
package/deploy them. It never starts or stops the game. Output, backups, logs,
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

Use a friend's fresh lobby for the first test. Steam discovery/joins are not
verified just because the DLL builds.

**Bug reports:** please include both `BepInEx/LogOutput.log` from the game folder
and `%USERPROFILE%/AppData/LocalLow/Featherweight Games/ChainedBeasts/Player.log`.
Include `Player-prev.log` if you've relaunched since the failure. Tell me whether
you were browsing, hosting or joining, and what mods each PC had installed.

Steam API behavior: [Valve's matchmaking documentation](https://partner.steamgames.com/doc/api/ISteamMatchmaking#RequestLobbyList).
