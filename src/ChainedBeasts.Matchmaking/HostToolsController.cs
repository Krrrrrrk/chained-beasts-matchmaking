using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

public sealed class HostToolsController : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private readonly HostToolsView _view = new();
    private HostPeer[] _peers = Array.Empty<HostPeer>();
    private IReadOnlyList<BlockedAccount> _blocked = Array.Empty<BlockedAccount>();
    private bool _wasVisible, _loggedFailure;
    private CursorLockMode _wasLocked;
    private double _refreshAt;
    public HostToolsController(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        var service = Plugin.HostTools;
        try
        {
            if (service == null || !Plugin.HostToolsEnabled) { SetOpen(false); return; }
            service.Tick();
            if (!service.Policy.Active || BrowserController.IsOpen || !Application.isFocused || _view.CloseRequested)
            { _view.CloseRequested = false; SetOpen(false); }
            var keyboard = Keyboard.current; var key = Plugin.HostToolsKey.Value;
            if (service.Policy.Active && !BrowserController.IsOpen && !PingController.IsWheelOpen && !ChatController.BlocksInput && Application.isFocused &&
                keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame) SetOpen(!IsOpen);
            if (!IsOpen) return;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { SetOpen(false); return; }
            if (_view.KickRequested) { _view.KickRequested = false; service.Policy.RequestKick(_view.KickClient, _view.KickAccount); }
            if (_view.CancelRequested) { _view.CancelRequested = false; service.Policy.CancelKick(); }
            if (_view.ConfirmRequested) { _view.ConfirmRequested = false; service.Policy.ConfirmKick(); _refreshAt = 0; }
            if (_view.UnblockRequested) { _view.UnblockRequested = false; service.Policy.Unblock(_view.UnblockAccount); _refreshAt = 0; }
            if (Time.realtimeSinceStartupAsDouble >= _refreshAt)
            {
                _refreshAt = Time.realtimeSinceStartupAsDouble + .25;
                _blocked = service.Policy.Blocked;
                try { _peers = service.Policy.Peers(); }
                catch
                {
                    // Keep Allow rejoin accessible even if the native peer/identity list is temporarily unreadable.
                    _peers = Array.Empty<HostPeer>();
                }
            }
        }
        catch (Exception e)
        {
            SetOpen(false);
            if (!_loggedFailure) { _loggedFailure = true; Plugin.Logger.LogWarning("[Host tools] panel update failed: " + e); }
        }
    }

    public void OnGUI()
    {
        if (!IsOpen || Plugin.HostTools == null || !Plugin.HostTools.Policy.Active || Screen.width < 1 || Screen.height < 1) return;
        try { _view.Draw(Plugin.HostTools.Policy, _peers, _blocked); }
        catch (Exception e)
        { SetOpen(false); if (!_loggedFailure) { _loggedFailure = true; Plugin.Logger.LogWarning("[Host tools] drawing failed: " + e); } }
    }

    [HideFromIl2Cpp]
    private void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        if (open)
        {
            _wasVisible = Cursor.visible; _wasLocked = Cursor.lockState; _refreshAt = 0;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            // Yield to a native pause/main menu that opened while this panel was visible.
            var nativeMenu = GameManager.InstanceExists && GameManager.Instance.State == GameManager.eState.MENU ||
                NetworkSession.InstanceExists && NetworkSession.Instance.IsPaused;
            if (!nativeMenu && !BrowserController.IsOpen) { Cursor.visible = _wasVisible; Cursor.lockState = _wasLocked; }
            Plugin.HostTools?.Policy.CancelKick();
            _view.KickRequested = _view.ConfirmRequested = _view.CancelRequested = _view.UnblockRequested = false;
        }
    }
    public void OnDestroy() { SetOpen(false); _view.Dispose(); }
}
