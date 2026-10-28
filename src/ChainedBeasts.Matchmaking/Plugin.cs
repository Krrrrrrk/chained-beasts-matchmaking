using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ChainedBeasts.Matchmaking.Core;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency("local.chainedbeasts.modfoundation", BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class Plugin : BasePlugin
{
    public const string Guid = "local.chainedbeasts.matchmaking";
    public const string Name = "Chained Beasts Matchmaking";
    public const string Version = "0.6.2";
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<Key> ToggleKey = null!;
    internal static ConfigEntry<bool> AdvertiseCompatibility = null!;
    internal static ConfigEntry<Key> PingKey = null!;
    internal static bool PingGamepadEnabled;
    internal static ConfigEntry<PingPadButton> PingPadButton;
    internal static bool PingsEnabled;
    internal static bool ChatEnabled;
    internal static ConfigEntry<Key> ChatKey;
    internal static bool HostToolsEnabled;
    internal static ConfigEntry<Key> HostToolsKey;
    internal static HostModerationService HostTools;
    internal static SteamBrowser Service;
    internal static bool BrowserEnabled;
    internal static bool RequireProtocol = true;
    partial void ConfigureLocal();
    private Harmony _harmony;

    public override void Load()
    {
        Logger = Log;
        if (!Config.Bind("General", "Enabled", true, "Enable the browser, pings and host controls on the next launch.").Value) return;
        BrowserEnabled = Config.Bind("Browser", "Enabled", true, "Enable the F6 browser and main-menu launcher. Pings, chat and host advertising remain available. Restart to apply.").Value;
        RequireProtocol = true;
        ConfigureLocal();
        ToggleKey = Config.Bind("Browser", "ToggleKey", Key.F6, "Open/close the browser at the main menu.");
        AdvertiseCompatibility = Config.Bind("Browser", "AdvertiseCompatibility", true,
            "Tag your existing Steam lobby with optional Foundation compatibility information. Does not change its visibility or joinability.");
        PingKey = Config.Bind("Pings", "HoldKey", Key.G, "Tap to ping the aimed floor point; hold for 0.22 seconds, move toward an option, then release. Restart to apply.");
        PingGamepadEnabled = Config.Bind("Pings", "GamepadEnabled", true, "Enable combat-only tap/hold pings on the primary player's assigned controller. Restart to apply.").Value;
        PingPadButton = Config.Bind("Pings", "GamepadButton", Core.PingPadButton.DpadUp, "Combat-only D-pad binding. Tap to ping six units ahead; hold, choose with right stick, release. Restart to apply.");
        ChatEnabled = Config.Bind("Chat", "Enabled", true, "Enable optional party text chat. Host and participants need the chat update. Restart to apply.").Value;
        ChatKey = Config.Bind("Chat", "OpenKey", Key.T, "Press T to open party chat with a physical keyboard. Enter sends, Escape cancels. Restart to apply.");
        PingsEnabled = Config.Bind("Pings", "Enabled", true, "Enable the optional cosmetic ping wheel. Sharing requires an updated host and receiving players. Restart to apply.").Value;
        HostToolsEnabled = Config.Bind("HostTools", "Enabled", true, "Allow the Steam host to kick accounts and block rejoining for the current lobby. Restart to apply.").Value;
        HostToolsKey = Config.Bind("HostTools", "ToggleKey", Key.F8, "Open/close host controls in your hosted Steam lobby or run. Use a different key from the ping wheel.");
        try
        {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            using var sha = SHA256.Create();
            var digest = Convert.ToHexString(sha.ComputeHash(stream));
            if (!digest.Equals("BA3EF02FA87DA4C41BB27D7610539C9578132204F0FB5713833E2EF93415CE29", StringComparison.Ordinal))
                throw new NotSupportedException("Uninspected game build; browser disabled. GameAssembly SHA256=" + digest);
            _harmony = new Harmony(Guid);
            // Cancel only our own discovery when native code entry/invite joining takes over.
            _harmony.Patch(AccessTools.DeclaredMethod(typeof(GameManager), nameof(GameManager.JoinRemoteServer), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNativeJoin)));
            _harmony.Patch(AccessTools.DeclaredMethod(typeof(SteamTransportController), nameof(SteamTransportController.JoinLobby), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNativeJoin)));
            foreach (var name in new[] { nameof(UIMainMenu.InvokeSelection), nameof(UIMainMenu.HandleSubmit), nameof(UIMainMenu.HandleCancel), nameof(UIMainMenu.HandleNavigation) })
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(UIMainMenu), name),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowNativeMenuInput)));
            Service = new SteamBrowser();
            AddComponent<BrowserController>();
            if (PingsEnabled)
            {
                try { AddComponent<PingController>(); }
                catch (Exception pingError) { PingsEnabled = false; Log.LogError("[Pings] initialization failed; browser remains available: " + pingError); }
            }
            if (ChatEnabled)
            {
                try
                {
                    _harmony.Patch(AccessTools.DeclaredMethod(typeof(NetworkSession), nameof(NetworkSession.CanPause), Type.EmptyTypes),
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowNativePause)));
                    AddComponent<ChatController>();
                    Log.LogInfo($"[Chat] ready; {ChatKey.Value} opens party chat. Runtime test pending.");
                }
                catch (Exception chatError)
                { ChatEnabled = false; ChatController.ShutdownCurrent(); Log.LogError("[Chat] initialization failed; other features remain available: " + chatError); }
            }
            if (HostToolsEnabled)
            {
                try
                {
                    HostTools = new();
                    _harmony.Patch(AccessTools.DeclaredMethod(typeof(SteamTransportController), nameof(SteamTransportController.ConnectionApproval),
                        new[] { typeof(Unity.Netcode.NetworkManager.ConnectionApprovalRequest), typeof(Unity.Netcode.NetworkManager.ConnectionApprovalResponse) }),
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeConnectionApproval)));
                    AddComponent<HostToolsController>();
                    Log.LogInfo($"[Host tools] ready; {HostToolsKey.Value} opens host-only kick/rejoin-block controls. Runtime test pending.");
                }
                catch (Exception hostError)
                { HostTools?.Policy.Clear(); HostTools = null; HostToolsEnabled = false; Log.LogError("[Host tools] initialization failed; host controls disabled: " + hostError); }
            }
            Log.LogInfo($"[Matchmaking] {Version} ready; browser enabled={BrowserEnabled}. Live browser behavior awaits testing.");
            if (PingsEnabled) Log.LogInfo($"[Pings] tap/hold {PingKey.Value}; controller={PingGamepadEnabled}, button={PingPadButton.Value} (combat only). Sharing needs an updated host. Runtime test pending.");
        }
        catch (Exception e)
        {
            _harmony?.UnpatchSelf();
            ChatEnabled = false; ChatController.ShutdownCurrent();
            HostTools?.Policy.Clear(); HostTools = null; HostToolsEnabled = PingsEnabled = false;
            Service?.Dispose();
            Service = null;
            Log.LogError("[Matchmaking] initialization failed; browser disabled: " + e);
        }
    }

    private static void BeforeNativeJoin()
    {
        Service?.Browser.Cancel("Native lobby joining took over.");
        BrowserController.CloseRequested = true;
    }
    private static bool AllowNativeMenuInput() => !BrowserController.IsOpen && !ChatController.BlocksInput;
    private static bool AllowNativePause(ref bool __result)
    {
        if (!ChatController.BlocksInput) return true;
        __result = false; return false;
    }
    private static bool BeforeConnectionApproval(SteamTransportController __instance,
        Unity.Netcode.NetworkManager.ConnectionApprovalRequest request, Unity.Netcode.NetworkManager.ConnectionApprovalResponse response) =>
        HostTools?.BeforeApproval(__instance, request, response) ?? true;

    public override bool Unload()
    {
        ChatEnabled = false; ChatController.ShutdownCurrent();
        PingsEnabled = false;
        HostToolsEnabled = false; HostTools?.Policy.Clear(); HostTools = null;
        BrowserController.CloseRequested = true;
        Service?.Dispose();
        Service = null;
        _harmony?.UnpatchSelf();
        return true;
    }
}

internal static class FoundationCompatibility
{
    // Optional read-only bridge. No reference to, or patches in, the TC/Foundation binaries.
    // The managed internal method is not a stable public API; fail to Unknown if it changes.
    internal static string Read()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == "ChainedBeasts.ModFoundation");
            var type = assembly?.GetType("ChainedBeasts.ModFoundation.Networking.MultiplayerServices");
            var method = type?.GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            var result = method?.Invoke(null, null) as string;
            return LobbyPolicy.IsDigest(result) ? result : null;
        }
        catch { return null; }
    }
}
