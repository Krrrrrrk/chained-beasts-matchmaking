using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

public sealed class BrowserController : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    internal static bool CloseRequested;
    private readonly BrowserMenuView _menu = new();
    private bool _wasVisible, _failureLogged;
    private CursorLockMode _wasLocked;

    public BrowserController(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try
        {
            var service = Plugin.Service;
            if (service == null) { if (IsOpen) SetOpen(false); return; }
            service.Tick();
            var inMenu = GameManager.InstanceExists && GameManager.Instance.State == GameManager.eState.MENU;
            if (CloseRequested || _menu.CloseRequested || !inMenu)
            { CloseRequested = _menu.CloseRequested = false; SetOpen(false); _menu.OpenRequested = false; }
            var keyboard = Keyboard.current;
            var key = Plugin.ToggleKey.Value;
            if (inMenu && (_menu.OpenRequested || keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame))
            { _menu.OpenRequested = false; SetOpen(!IsOpen); }
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);
            var now = Time.realtimeSinceStartupAsDouble;
            if (_menu.RefreshRequested && IsOpen && service.Context.Available &&
                service.Browser.Operation == Operation.Idle && now >= _menu.RefreshAfter)
            {
                _menu.RefreshRequested = false; _menu.Selected = 0; _menu.Acknowledge = false; _menu.Page = 0;
                if (service.Browser.Search(new(_menu.Worldwide, _menu.OtherBuilds), now, service.Context))
                    _menu.RefreshAfter = now + Browser.RefreshCooldown;
            }
            if (_menu.JoinRequested)
            {
                _menu.JoinRequested = false;
                if (IsOpen) service.Browser.BeginJoin(_menu.Selected, _menu.Acknowledge, now, service.Context);
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
        try { _menu.Draw(service, IsOpen); }
        catch (Exception e)
        {
            if (!_failureLogged) { Plugin.Logger.LogError("[Matchmaking] styled UI unavailable; using basic controls: " + e); _failureLogged = true; }
            _menu.UseFallback = true;
        }
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
            _menu.RefreshRequested = true;
        }
        else
        {
            _menu.RefreshRequested = _menu.JoinRequested = false;
            Cursor.visible = _wasVisible; Cursor.lockState = _wasLocked;
            Plugin.Service?.Browser.Cancel();
        }
    }
    public void OnDestroy() { if (IsOpen) SetOpen(false); }
}
