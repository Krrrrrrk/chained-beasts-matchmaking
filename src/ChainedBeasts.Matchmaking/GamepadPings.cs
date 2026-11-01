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
    private ulong _ownerNetwork, _ownerIndex, _ownerLocal;
    private readonly HashSet<string> _notes = new();
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
        Note("D-pad press observed");
        var player = PingWorld.Local();
        var client = player?.clientID?.Value;
        var pad = client is null ? null : OwnedPad(client, binding);
        var level = session?.Level;
        if (player == null || pad == null || !pad.added || level == null || level.ActiveGameState != GameLevel.eGameState.RUNNING ||
            player.IsInputBlocked || UIManager.GameInputBlocked || !Application.isFocused || BrowserController.IsOpen || HostToolsController.IsOpen)
        {
            Note($"press blocked: actor={player != null}, ownedPad={pad != null}, combat={level != null && level.ActiveGameState == GameLevel.eGameState.RUNNING}, actorBlocked={player?.IsInputBlocked == true}, uiBlocked={UIManager.GameInputBlocked}");
            Reset(); return wasActive;
        }
        if (Active && (_device.Pointer != pad.Pointer || _owner != player.Pointer || _ownerNetwork != client.NetworkID ||
            _ownerIndex != client.PlayerIndex || _ownerLocal != client.LocalID || binding != _button ||
            Screen.width != _width || Screen.height != _height))
        { Reset(); return true; }
        var control = pad[(GamepadButton)(int)binding];
        if (!Active)
        {
            if (!control.wasPressedThisFrame) return false; // Another local/couch player's pad is not ours.
            _device = pad; _owner = player.Pointer; _button = binding;
            _ownerNetwork = client.NetworkID; _ownerIndex = client.PlayerIndex; _ownerLocal = client.LocalID;
            Note("accepted primary controller: " + GamepadPingRules.ButtonLabel(binding));
            _width = Screen.width; _height = Screen.height; _pressedAt = now; _pending = true;
            HasTarget = Capture(player, out var captured); Target = captured;
            if (!HasTarget) Note("ground target unavailable; held quick messages remain available");
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

    private Gamepad OwnedPad(ClientIdentifier client, PingPadButton button)
    {
        if (Active) return IsOwner(_device, client) ? _device : null;
        var pads = Gamepad.all;
        for (var i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            if (pad != null && pad.added && pad[(GamepadButton)(int)button].wasPressedThisFrame && IsOwner(pad, client)) return pad;
        }
        return null;
    }

    private static bool IsOwner(Gamepad pad, ClientIdentifier client)
    {
        if (pad == null || !pad.added || client is null) return false;
        // Native read-only lookup includes its primary-player fallback when no explicit
        // assignments exist. Do not adopt Gamepad.current or a last-active InputUser device.
        var owner = GameInput.GetDeviceClient(pad);
        return owner is not null && owner.NetworkID == client.NetworkID && owner.LocalID == client.LocalID && owner.PlayerIndex == client.PlayerIndex;
    }

    private void Note(string message)
    {
        if (_notes.Count < 12 && _notes.Add(message)) Plugin.Logger.LogInfo("[Pings/controller] " + message);
    }

    private static bool AnyPress(PingPadButton button)
    {
        var pads = Gamepad.all;
        for (var i = 0; i < pads.Count; i++)
            if (pads[i] != null && pads[i].added && pads[i][(GamepadButton)(int)button].wasPressedThisFrame) return true;
        return false;
    }

    private static bool Capture(Player player, out PingPosition point)
    {
        point = default;
        var origin = PingWorld.Position(player);
        // Native NetworkAgent.AngleDifferenceFromFacing reads this same visual
        // transform; the network/player root can stay aligned to world north.
        var animator = player.agentModel?.networkAnimator;
        if (animator is null) return false;
        var facing = animator.transform;
        if (facing is null) return false;
        var forward = facing.forward;
        var plane = player.mouseTargetPlane; var normal = plane.m_Normal;
        return GamepadPingRules.Ahead(origin, new(forward.x, forward.y, forward.z),
            new(normal.x, normal.y, normal.z), plane.m_Distance, out point);
    }

    internal void Reset()
    {
        _pending = Open = HasTarget = false; Selected = PingKind.None; Preview = null;
        _device = null; _owner = IntPtr.Zero; _ownerNetwork = _ownerIndex = _ownerLocal = 0; Target = default;
    }
}
