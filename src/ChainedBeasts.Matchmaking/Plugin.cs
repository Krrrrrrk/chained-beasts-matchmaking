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
public sealed class Plugin : BasePlugin
{
    public const string Guid = "local.chainedbeasts.matchmaking";
    public const string Name = "Chained Beasts Matchmaking";
    public const string Version = "0.1.0";
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<Key> ToggleKey = null!;
    internal static ConfigEntry<bool> AdvertiseCompatibility = null!;
    internal static SteamBrowser Service;
    private Harmony _harmony;

    public override void Load()
    {
        Logger = Log;
        if (!Config.Bind("General", "Enabled", true, "Enable the browser on the next launch.").Value) return;
        ToggleKey = Config.Bind("Browser", "ToggleKey", Key.F6, "Open/close the browser at the main menu.");
        AdvertiseCompatibility = Config.Bind("Browser", "AdvertiseCompatibility", true,
            "Tag your existing Steam lobby with optional Foundation compatibility information. Does not change its visibility or joinability.");
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
            Log.LogInfo($"[Matchmaking] {Version} ready; press {ToggleKey.Value} at the main menu. Live browser behavior awaits testing.");
        }
        catch (Exception e)
        {
            _harmony?.UnpatchSelf();
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
    private static bool AllowNativeMenuInput() => !BrowserController.IsOpen;

    public override bool Unload()
    {
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
