using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

public sealed class PingController : MonoBehaviour
{
    public static bool IsWheelOpen { get; private set; }
    private readonly PingView _view = new();
    private readonly GamepadPings _pad = new();
    private readonly List<PingVisual> _visuals = new();
    private readonly List<PingVisual> _previewVisuals = new();
    private PingNetwork _network;
    private Camera _camera;
    private IntPtr _session;
    private ulong _scene, _sceneName;
    private bool _open, _pending, _wasVisible, _failed, _suppressUntilRelease;
    private const double HoldSeconds = .22;
    private double _pressedAt;
    private CursorLockMode _wasLocked;
    private Vector2 _center, _mouseStart, _returnPointer;
    private Mouse _openingMouse;
    private Keyboard _openingKeyboard;
    private int _openedWidth, _openedHeight;
    private bool _awaitWarp;
    private double _warpUntil;
    private PingKind _selected;
    private PingPosition _target;
    private bool _hasTarget;
    private double _sendAfter, _cameraAt;

    public PingController(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        if (_failed) return;
        try
        {
            if (!Plugin.PingsEnabled) { Cancel(); _visuals.Clear(); _network?.Dispose(); UnityEngine.Object.Destroy(this); return; }
            if (_network == null) { _network = new(); _network.Received += Show; _network.Cleared += ClearVisuals; }
            _network.Tick();
            var ready = PingWorld.Ready(out var session);
            var scene = ready ? session.sceneRoot.NetworkObjectId : 0;
            var name = ready ? PingWorld.SceneName(session) : 0;
            if (!ready || _session != session.Pointer || _scene != scene || _sceneName != name)
            {
                Cancel(); _visuals.Clear(); _camera = null;
                _session = ready ? session.Pointer : IntPtr.Zero; _scene = scene; _sceneName = name;
            }
            if (!ready) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (_camera == null || now >= _cameraAt) { _camera = Camera.main; _cameraAt = now + .5; }
            RefreshVisuals(now);
            if (ChatController.BlocksInput || !Application.isFocused || BrowserController.IsOpen || HostToolsController.IsOpen)
            { Cancel(); _suppressUntilRelease = true; return; }
            if (!_open && !_pending)
            {
                var consumed = _pad.Tick(session, _camera, now, out var padChoice, out var padTarget);
                IsWheelOpen = _pad.Open;
                if (consumed)
                {
                    if (padChoice != PingKind.None) Send(padChoice, true, padTarget, now);
                    return;
                }
            }
            var keyboard = Keyboard.current; var mouse = Mouse.current; var key = Plugin.PingKey.Value;
            if (keyboard == null || mouse == null || key == Key.None || !Application.isFocused || BrowserController.IsOpen || HostToolsController.IsOpen)
            { Cancel(); _suppressUntilRelease = true; return; }
            var control = keyboard[key];
            if (_suppressUntilRelease)
            { if (!control.isPressed) _suppressUntilRelease = false; return; }
            if (!_open && !_pending && !control.wasPressedThisFrame) return;
            if (PingWorld.Local() == null) { Cancel(); _suppressUntilRelease = true; return; }
            if (!_open && !_pending && control.wasPressedThisFrame && !Begin(mouse, keyboard, now)) return;
            if (Screen.width != _openedWidth || Screen.height != _openedHeight ||
                mouse.Pointer != _openingMouse?.Pointer || keyboard.Pointer != _openingKeyboard?.Pointer)
            { Cancel(); _suppressUntilRelease = true; return; }
            if (keyboard.escapeKey.wasPressedThisFrame) { Cancel(); _suppressUntilRelease = true; return; }
            if (_pending)
            {
                if (control.wasReleasedThisFrame || !control.isPressed)
                {
                    var tap = now - _pressedAt < HoldSeconds;
                    var hasTarget = _hasTarget; var target = _target;
                    Cancel();
                    if (tap) Send(PingKind.Location, hasTarget, target, now);
                    return;
                }
                if (now - _pressedAt >= HoldSeconds) Open(mouse);
                return;
            }
            if (!_open) return;
            var pointer = mouse.position.ReadValue();
            var guiPointer = new Vector2(pointer.x, Screen.height - pointer.y);
            if (_awaitWarp)
            {
                var distance = guiPointer - _mouseStart;
                if (distance.x * distance.x + distance.y * distance.y <= 64) _awaitWarp = false;
                else
                {
                    if (!control.isPressed || now >= _warpUntil) { Cancel(); _suppressUntilRelease = true; }
                    return;
                }
            }
            var delta = guiPointer - _mouseStart;
            var scale = PingView.Scale;
            _selected = PingProtocol.Sector(delta.x, delta.y, 42 * scale);
            if (control.wasReleasedThisFrame || !control.isPressed)
            {
                var choice = _selected; var hasTarget = _hasTarget; var target = _target;
                Cancel();
                Send(choice, hasTarget, target, now);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    [HideFromIl2Cpp]
    private bool Begin(Mouse mouse, Keyboard keyboard, double now)
    {
        var point = mouse.position.ReadValue();
        if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) return false;
        _hasTarget = TryTarget(point, out _target);
        _pressedAt = now; _pending = true;
        _openedWidth = Screen.width; _openedHeight = Screen.height;
        _openingMouse = mouse; _openingKeyboard = keyboard;
        return true;
    }

    [HideFromIl2Cpp]
    private void Send(PingKind choice, bool hasTarget, PingPosition target, double now)
    {
        if (now < _sendAfter || choice == PingKind.None || choice == PingKind.Location && !hasTarget) return;
        var player = PingWorld.Local();
        if (player != null && _network.Submit(choice, choice == PingKind.Location ? target : PingWorld.Position(player)))
            _sendAfter = now + PingProtocol.Cooldown;
    }

    [HideFromIl2Cpp]
    private void Open(Mouse mouse)
    {
        var point = mouse.position.ReadValue();
        if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) { Cancel(); return; }
        var original = new Vector2(point.x, Screen.height - point.y);
        var scale = PingView.Scale;
        var minX = Math.Min(240 * scale, Screen.width / 2f);
        var minY = Math.Min(165 * scale, Screen.height / 2f);
        _center = new(Math.Clamp(original.x, minX, Math.Max(minX, Screen.width - 240 * scale)),
            Math.Clamp(original.y, minY, Math.Max(minY, Screen.height - 230 * scale)));
        // Preserve the map point captured at key-down, before any radial/cursor movement.
        _pending = false; _selected = PingKind.None; _open = IsWheelOpen = true;
        _openedWidth = Screen.width; _openedHeight = Screen.height;
        _openingMouse = mouse; _openingKeyboard = Keyboard.current; _returnPointer = point; _mouseStart = _center;
        _wasVisible = Cursor.visible; _wasLocked = Cursor.lockState;
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        _awaitWarp = Math.Abs(original.x - _center.x) > 1 || Math.Abs(original.y - _center.y) > 1;
        if (_awaitWarp) mouse.WarpCursorPosition(new(_center.x, Screen.height - _center.y));
        _warpUntil = Time.realtimeSinceStartupAsDouble + .25;
    }

    [HideFromIl2Cpp]
    private bool TryTarget(Vector2 pointer, out PingPosition position)
    {
        position = default;
        var player = PingWorld.Local();
        if (player == null || _camera == null || !_camera.enabled || !_camera.gameObject.activeInHierarchy || _camera.targetDisplay != 0) return false;
        var ray = _camera.ScreenPointToRay(new Vector3(pointer.x, pointer.y, 0));
        // The native Player.GetMouseGroundPos uses this plane, not the first scene collider.
        // Read the generated value-type fields directly, avoiding extra native Plane/Ray calls.
        var plane = player.mouseTargetPlane;
        var origin = ray.m_Origin; var direction = ray.m_Direction; var normal = plane.m_Normal;
        if (!PingGround.Intersect(new(origin.x, origin.y, origin.z), new(direction.x, direction.y, direction.z),
                new(normal.x, normal.y, normal.z), plane.m_Distance, out position)) return false;
        return PingProtocol.InRange(position, PingWorld.Position(player));
    }

    [HideFromIl2Cpp]
    private void Show(PingPacket packet, double remaining)
    {
        // Callback only from our managed relay Tick/Submit. One active marker per Steam/NGO account.
        var player = PingWorld.Find(packet.Author, packet.PlayerIndex);
        if (player == null || packet.Scene != _scene || packet.SceneName != _sceneName || remaining <= 0) return;
        var world = new Vector3(packet.Position.X, packet.Position.Y, packet.Position.Z);
        if (packet.Kind != PingKind.Location && !PingWorld.HeadPosition(player, out world)) return;
        for (var i = _visuals.Count - 1; i >= 0; i--)
            if (_visuals[i].Packet.Author == packet.Author) _visuals.RemoveAt(i);
        if (_visuals.Count >= PingProtocol.MaximumSenders) return;
        var index = player.clientID.Value.PlayerIndex + 1;
        _visuals.Add(new() { Packet = packet, Until = Time.realtimeSinceStartupAsDouble + remaining,
            Label = "P" + index + "  " + PingProtocol.Label(packet.Kind),
            World = world });
    }

    [HideFromIl2Cpp]
    private void RefreshVisuals(double now)
    {
        for (var i = _visuals.Count - 1; i >= 0; i--)
        {
            var visual = _visuals[i];
            var player = PingWorld.Find(visual.Packet.Author, visual.Packet.PlayerIndex);
            if (now >= visual.Until || player == null) { _visuals.RemoveAt(i); continue; }
            if (visual.Packet.Kind != PingKind.Location)
            {
                if (!PingWorld.HeadPosition(player, out var position)) { _visuals.RemoveAt(i); continue; }
                visual.World = position;
            }
        }
    }

    public void OnGUI()
    {
        if (_failed || !Plugin.PingsEnabled || _network == null || _session == IntPtr.Zero ||
            !_open && !_pad.Active && _visuals.Count == 0 || Screen.width < 1 || Screen.height < 1 || PingWorld.NativeMenuOwnsCursor) return;
        try
        {
            var now = Time.realtimeSinceStartupAsDouble;
            var pad = _pad.Active;
            var visuals = _visuals;
            if (pad && _pad.Preview != null)
            {
                _previewVisuals.Clear(); _previewVisuals.AddRange(_visuals); _previewVisuals.Add(_pad.Preview);
                visuals = _previewVisuals;
            }
            _view.Draw(visuals, _camera, now, _open || _pad.Open, pad ? _pad.Center : _center,
                pad ? _pad.Selected : _selected, pad ? _pad.HasTarget : _hasTarget,
                !_open && !_pad.Open ? "" : now < _sendAfter ? "Ping ready in " + Math.Ceiling(_sendAfter - now) + "s" : _network.Status,
                pad ? _pad.Hint : null);
        }
        catch (Exception e) { Fail(e); }
    }

    [HideFromIl2Cpp]
    private void Cancel()
    {
        _pad.Reset(); _previewVisuals.Clear();
        if (!_open) IsWheelOpen = false;
        _pending = false;
        if (!_open) { _openingMouse = null; _openingKeyboard = null; return; }
        _open = IsWheelOpen = false; _selected = PingKind.None;
        _awaitWarp = false;
        // A pause/menu opened during the gesture owns the cursor now; do not hide or relock it.
        var restore = !PingWorld.NativeMenuOwnsCursor && !BrowserController.IsOpen;
        if (restore) { Cursor.visible = _wasVisible; Cursor.lockState = _wasLocked; }
        // Restore the original aim point; do not move another application's pointer after focus loss.
        try
        {
            var current = Mouse.current;
            if (restore && Application.isFocused && current != null && current.Pointer == _openingMouse?.Pointer &&
                Screen.width == _openedWidth && Screen.height == _openedHeight) current.WarpCursorPosition(_returnPointer);
        }
        catch { }
        _openingMouse = null;
        _openingKeyboard = null;
    }
    [HideFromIl2Cpp]
    private void ClearVisuals() => _visuals.Clear();
    [HideFromIl2Cpp]
    private void Fail(Exception e)
    {
        Cancel(); _visuals.Clear(); _network?.Dispose(); _view.Dispose(); _failed = true;
        Plugin.Logger.LogError("[Pings] disabled for this launch; browser/game continue normally: " + e);
    }
    public void OnDestroy() { Cancel(); _network?.Dispose(); _view.Dispose(); }
}
