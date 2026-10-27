using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

public sealed class BrowserController : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    internal static bool CloseRequested;
    private bool _wasVisible, _worldwide, _otherBuilds, _acknowledge, _refresh, _join, _openRequested;
    private CursorLockMode _wasLocked;
    private ulong _selected;
    private int _page;
    private bool _failureLogged;
    private const int PageSize = 5;

    public BrowserController(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try
        {
            var service = Plugin.Service;
            if (service == null) { if (IsOpen) SetOpen(false); return; }
            service.Tick();
            var inMenu = GameManager.InstanceExists && GameManager.Instance.State == GameManager.eState.MENU;
            if (CloseRequested || !inMenu) { CloseRequested = false; SetOpen(false); _openRequested = false; }
            var keyboard = Keyboard.current;
            var key = Plugin.ToggleKey.Value;
            if (inMenu && (_openRequested || keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame))
            { _openRequested = false; SetOpen(!IsOpen); }
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
            if (_refresh)
            {
                _refresh = false; _selected = 0; _acknowledge = false; _page = 0;
                if (IsOpen) service.Browser.Search(new(_worldwide, _otherBuilds), Time.realtimeSinceStartupAsDouble, service.Context);
            }
            if (_join)
            {
                _join = false;
                if (IsOpen) service.Browser.BeginJoin(_selected, _acknowledge, Time.realtimeSinceStartupAsDouble, service.Context);
            }
        }
        catch (Exception e)
        {
            if (!_failureLogged) { Plugin.Logger.LogError("[Matchmaking] browser update failed: " + e); _failureLogged = true; }
            Plugin.Service?.Browser.Cancel("Browser error. Close and reopen it; preserve the logs if it repeats.");
        }
    }

    public void OnGUI()
    {
        var service = Plugin.Service;
        if (service == null || !GameManager.InstanceExists || GameManager.Instance.State != GameManager.eState.MENU) return;
        GUI.depth = -1100;
        if (!IsOpen)
        {
            GUILayout.BeginArea(new Rect(Math.Max(10, Screen.width - 270), 20, 250, 70));
            try { if (GUILayout.Button($"Find public games ({Plugin.ToggleKey.Value})")) _openRequested = true; }
            finally { GUILayout.EndArea(); }
            return;
        }
        var width = Math.Min(850f, Math.Max(300, Screen.width - 40));
        var height = Math.Min(650f, Math.Max(300, Screen.height - 40));
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2, 20, width, height), "Chained Beasts — Public Steam Games", GUI.skin.window);
        var enabled = GUI.enabled;
        try
        {
            GUILayout.Space(22);
            GUILayout.Label("Experimental browser. Steam members count accounts, not local characters.");
            var worldwide = GUILayout.Toggle(_worldwide, "Worldwide (may have higher latency)");
            var otherBuilds = GUILayout.Toggle(_otherBuilds, "Show other game builds (joining them stays disabled)");
            if (worldwide != _worldwide || otherBuilds != _otherBuilds)
            { _worldwide = worldwide; _otherBuilds = otherBuilds; _refresh = true; }
            GUILayout.BeginHorizontal();
            GUI.enabled = service.Context.Available && service.Browser.Operation == Operation.Idle;
            if (GUILayout.Button("Refresh")) _refresh = true;
            GUI.enabled = enabled;
            if (GUILayout.Button("Close (Esc)")) CloseRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(service.Context.Available ? service.Browser.Status : service.Context.Reason);
            GUILayout.Space(6);
            var lobbies = service.Browser.Lobbies;
            var pages = Math.Max(1, (lobbies.Count + PageSize - 1) / PageSize);
            _page = Math.Clamp(_page, 0, pages - 1);
            for (var i = _page * PageSize; i < Math.Min(lobbies.Count, (_page + 1) * PageSize); i++)
            {
                var lobby = lobbies[i];
                var assessment = LobbyPolicy.Assess(lobby, service.Context);
                var label = assessment.Compatibility switch { Compatibility.FoundationMatched => "Advertised Foundation match", Compatibility.Blocked => "Unavailable", _ => "Mods unknown" };
                if (GUILayout.Button($"{(_selected == lobby.Id ? "> " : "")}{lobby.Title} — {lobby.Members}/{lobby.MemberLimit} Steam members — {label}"))
                { _selected = lobby.Id; _acknowledge = false; }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous")) _page = Math.Max(0, _page - 1);
            GUILayout.Label($"Page {_page + 1}/{pages}");
            if (GUILayout.Button("Next")) _page = Math.Min(pages - 1, _page + 1);
            GUILayout.EndHorizontal();
            // Avoid a compiler-generated Lobby-parameter method on this injected MonoBehaviour.
            Lobby selected = null;
            foreach (var lobby in lobbies) { if (lobby.Id == _selected) { selected = lobby; break; } }
            if (selected != null)
            {
                var assessment = LobbyPolicy.Assess(selected, service.Context);
                GUILayout.Space(8);
                GUILayout.Label($"Name: {selected.Title} | Build: {LobbyPolicy.Display(selected.Build, 60)}");
                GUILayout.Label(assessment.Detail);
                if (LobbyPolicy.NeedsModConfirmation(assessment, service.Context))
                    _acknowledge = GUILayout.Toggle(_acknowledge, "I checked this host has the same Foundation/TC setup.");
                GUI.enabled = service.Context.Available && service.Browser.Operation == Operation.Idle && assessment.Allowed &&
                    (!LobbyPolicy.NeedsModConfirmation(assessment, service.Context) || _acknowledge);
                if (GUILayout.Button("Join selected lobby")) _join = true;
                GUI.enabled = enabled;
            }
            GUILayout.Space(6);
            GUILayout.Label("Only open, nonfull public/searchable Steam lobbies appear. No games? Try Worldwide.");
        }
        catch (Exception e)
        {
            if (!_failureLogged) { Plugin.Logger.LogError("[Matchmaking] browser UI failed: " + e); _failureLogged = true; }
        }
        finally { GUI.enabled = enabled; GUILayout.EndArea(); }
    }

    [HideFromIl2Cpp]
    private void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        if (open)
        {
            _failureLogged = false;
            _wasVisible = Cursor.visible; _wasLocked = Cursor.lockState;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            _refresh = true;
        }
        else
        {
            _refresh = _join = false;
            Cursor.visible = _wasVisible; Cursor.lockState = _wasLocked;
            Plugin.Service?.Browser.Cancel();
        }
    }
    public void OnDestroy() { if (IsOpen) SetOpen(false); }
}
