using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChainedBeasts.Matchmaking;

/// <summary>Plain managed gesture reader. Reads only the primary player's assigned pad; never owns native input.</summary>
internal sealed class GamepadPings
{
    private Gamepad _device;
    private IntPtr _owner;
    private PingPadButton _button;
    private bool _pending;
    private double _pressedAt;
    private int _width, _height;
    internal bool Open { get; private set; }
    internal bool Active => _pending || Open;
    internal bool HasTarget { get; private set; }
    internal PingPosition Target { get; private set; }
    internal PingKind Selected { get; private set; }
    internal Vector2 Center { get; private set; }
    internal PingVisual Preview { get; private set; }
    internal string Hint => "Right stick / release " + GamepadPingRules.ButtonLabel(_button) + " / center cancels";

    internal bool Tick(NetworkSession session, Camera camera, double now, out PingKind choice, out PingPosition target)
    {
        choice = PingKind.None; target = default;
        var wasActive = Active;
        var binding = Plugin.PingPadButton.Value;
        if (!Plugin.PingGamepadEnabled || (int)binding < 0 || (int)binding > 3)
        { Reset(); return wasActive; }
        // Cheap press scan avoids a player/device lookup on every idle frame. Never use Gamepad.current.
        if (!Active && !AnyPress(binding)) return false;
        var player = PingWorld.Local();
        var pad = player?.InputUser?.Device?.TryCast<Gamepad>();
        // GlobalInput.Device can fall back to lastActiveDevice. Require the explicit
        // ClientIdentifier assignment as well; an interface's Device alone is not ownership.
        if (pad != null && (GameInput.assignedDevices == null || player.clientID?.Value is null ||
            !GameInput.assignedDevices.TryGetValue(player.clientID.Value, out var assigned) || assigned == null || assigned.Pointer != pad.Pointer)) pad = null;
        var level = session?.Level;
        if (player == null || pad == null || !pad.added || level == null || level.ActiveGameState != GameLevel.eGameState.RUNNING ||
            player.IsInputBlocked || UIManager.GameInputBlocked || !Application.isFocused || BrowserController.IsOpen || HostToolsController.IsOpen)
        { Reset(); return wasActive; }
        if (Active && (_device.Pointer != pad.Pointer || _owner != player.playerData.Pointer || binding != _button ||
            Screen.width != _width || Screen.height != _height))
        { Reset(); return true; }
        var control = pad[(GamepadButton)(int)binding];
        if (!Active)
        {
            if (!control.wasPressedThisFrame) return false; // Another local/couch player's pad is not ours.
            _device = pad; _owner = player.playerData.Pointer; _button = binding;
            _width = Screen.width; _height = Screen.height; _pressedAt = now; _pending = true;
            HasTarget = Capture(player, pad, camera, out var captured); Target = captured;
            if (HasTarget) Preview = new() { Packet = new(PingMessage.Show, PingKind.Location),
                World = new(Target.X, Target.Y, Target.Z), Label = "PING TARGET", Until = now + 1 };
        }
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Reset(); return true; }
        if (Preview != null) Preview.Until = now + 1;
        var released = control.wasReleasedThisFrame || !control.isPressed;
        if (_pending)
        {
            if (released)
            {
                if (now - _pressedAt < GamepadPingRules.HoldSeconds && HasTarget) { choice = PingKind.Location; target = Target; }
                Reset(); return true;
            }
            if (now - _pressedAt >= GamepadPingRules.HoldSeconds)
            {
                _pending = false; Open = true; Selected = PingKind.None;
                Center = new(Screen.width / 2f, Screen.height / 2f);
            }
            return true;
        }
        var stick = pad.rightStick.ReadValue();
        Selected = PingProtocol.Sector(stick.x, -stick.y, GamepadPingRules.StickDeadZone);
        if (released)
        {
            if (Selected != PingKind.Location || HasTarget) { choice = Selected; target = Target; }
            Reset();
        }
        return true;
    }

    private static bool AnyPress(PingPadButton button)
    {
        var pads = Gamepad.all;
        for (var i = 0; i < pads.Count; i++)
            if (pads[i] != null && pads[i].added && pads[i][(GamepadButton)(int)button].wasPressedThisFrame) return true;
        return false;
    }

    private static bool Capture(Player player, Gamepad pad, Camera camera, out PingPosition point)
    {
        point = default;
        var origin = PingWorld.Position(player);
        var stick = pad.rightStick.ReadValue();
        if (!float.IsFinite(stick.x) || !float.IsFinite(stick.y)) return false;
        var forward = player.transform.forward;
        var direction = new PingPosition(forward.x, forward.y, forward.z);
        var plane = player.mouseTargetPlane; var normal = plane.m_Normal;
        if (stick.x * stick.x + stick.y * stick.y >= GamepadPingRules.StickDeadZone * GamepadPingRules.StickDeadZone)
        {
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy || camera.targetDisplay != 0) return false;
            var screen = camera.WorldToScreenPoint(new(origin.X, origin.Y, origin.Z));
            if (!float.IsFinite(screen.x) || !float.IsFinite(screen.y) || !float.IsFinite(screen.z) || screen.z <= 0) return false;
            var ray = camera.ScreenPointToRay(new(screen.x + stick.x * 80, screen.y + stick.y * 80, 0));
            var start = ray.m_Origin; var aim = ray.m_Direction;
            if (!PingGround.Intersect(new(start.x, start.y, start.z), new(aim.x, aim.y, aim.z),
                new(normal.x, normal.y, normal.z), plane.m_Distance, out var ground)) return false;
            direction = new(ground.X - origin.X, 0, ground.Z - origin.Z);
        }
        return GamepadPingRules.Ahead(origin, direction, new(normal.x, normal.y, normal.z), plane.m_Distance, out point);
    }

    internal void Reset()
    {
        _pending = Open = HasTarget = false; Selected = PingKind.None; Preview = null;
        _device = null; _owner = IntPtr.Zero; Target = default;
    }
}
