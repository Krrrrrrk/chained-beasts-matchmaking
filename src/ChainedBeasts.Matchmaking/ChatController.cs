using ChainedBeasts.Matchmaking.Core;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChainedBeasts.Matchmaking;

public sealed class ChatController : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static bool BlocksInput { get; private set; }
    private static ChatController _active;
    private readonly ChatHistory _history = new();
    private readonly ChatDraft _draft = new();
    private readonly ChatView _view = new();
    private readonly Queue<char> _characters = new();
    private ChatNetwork _network;
    private Keyboard _keyboard;
    private Il2CppSystem.Action<char> _textHandler;
    private Player _blockedPlayer;
    private IntPtr _session, _owner;
    private double _sendAfter, _feedUntil, _releaseAt, _backspaceAt;
    private bool _failed, _uiBlock;
    private string _status = "";
    public ChatController(IntPtr pointer) : base(pointer) { _active = this; }

    public void Update()
    {
        if (_failed) { var network = _network; _network = null; network?.Dispose(); return; }
        try
        {
            if (!Plugin.ChatEnabled) { Stop(); UnityEngine.Object.Destroy(this); return; }
            if (_network == null) { _network = new(); _network.Received += Receive; _network.Cleared += Clear; }
            _network.Tick();
            var now = Time.realtimeSinceStartupAsDouble;
            var ready = PingWorld.Ready(out var session);
            if (!ready || !Application.isFocused || session.IsPaused || BrowserController.IsOpen || HostToolsController.IsOpen)
            { Close(true); ReleaseBlock(); return; }
            if (_session != session.Pointer)
            { Close(true); ReleaseBlock(); _history.Clear(); _session = session.Pointer; }
            if (!IsOpen && BlocksInput && now >= _releaseAt) ReleaseBlock();
            var keys = Keyboard.current;
            if (!IsOpen)
            {
                var key = Plugin.ChatKey.Value;
                if (BlocksInput || keys == null || key == Key.None || !keys[key].wasPressedThisFrame || PingController.IsWheelOpen || UIManager.GameInputBlocked) return;
                var player = PingWorld.Local();
                if (player == null || player.IsInputBlocked) return;
                _keyboard = keys; _owner = player.playerData.Pointer;
                player.PushInputBlock(); _blockedPlayer = player; BlocksInput = true;
                try
                {
                    UIManager.PushGameInputBlock<UIManager>(); _uiBlock = true;
                    _textHandler = (Action<char>)TextInput; keys.add_onTextInput(_textHandler);
                    IsOpen = true; _view.Page = 0; _status = "";
                }
                catch { Close(true); ReleaseBlock(); throw; }
                return;
            }
            var local = PingWorld.Local();
            if (keys == null || keys.Pointer != _keyboard?.Pointer || local == null || local.playerData.Pointer != _owner ||
                UIManager.inputBlockCount != 1 || local.inputBlockCount != 1)
            { Close(true); return; }
            if (keys.escapeKey.wasPressedThisFrame) { Close(true); return; }
            while (_characters.Count > 0) _draft.Input(_characters.Dequeue());
            if (keys.pageUpKey.wasPressedThisFrame) _view.Page++;
            if (keys.pageDownKey.wasPressedThisFrame) _view.Page = Math.Max(0, _view.Page - 1);
            if (keys.backspaceKey.wasPressedThisFrame || keys.backspaceKey.isPressed && now >= _backspaceAt)
            { _draft.Backspace(); _backspaceAt = now + (keys.backspaceKey.wasPressedThisFrame ? .4 : .05); }
            if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame)
            {
                var text = ChatText.Clean(_draft.Text);
                if (text.Length == 0) { Close(true); return; }
                if (now < _sendAfter) { _status = "Wait a moment before sending again."; return; }
                if (_network.Submit(text)) { _status = ""; _sendAfter = now + PingProtocol.Cooldown; Close(true); }
                else _status = "Not sent. " + _network.Status;
            }
        }
        catch (Exception e)
        {
            Stop(); _failed = true;
            Plugin.Logger.LogError("[Chat] disabled for this launch; preserve the logs: " + e);
        }
    }

    [HideFromIl2Cpp]
    private void TextInput(char character)
    { if (!_failed && IsOpen && _characters.Count < 512) _characters.Enqueue(character); }
    [HideFromIl2Cpp]
    private void Receive(ChatPacket packet)
    { _history.Add(packet); _feedUntil = Time.realtimeSinceStartupAsDouble + 12; }
    [HideFromIl2Cpp]
    private void Clear() { Close(true); ReleaseBlock(); _history.Clear(); _feedUntil = 0; }
    [HideFromIl2Cpp]
    private void Close(bool discard)
    {
        IsOpen = false;
        var keyboard = _keyboard; var handler = _textHandler;
        _keyboard = null; _textHandler = null;
        try { if (keyboard != null && handler != null) keyboard.remove_onTextInput(handler); }
        catch (Exception e) { _failed = true; ReleaseBlock(); Plugin.Logger.LogWarning("[Chat] text-input cleanup failed; chat disabled: " + e.GetType().Name); }
        _characters.Clear(); if (discard) _draft.Clear();
        // Keep the game/pause/ping hotkeys blocked through the close key's input frame.
        if (_blockedPlayer is not null || _uiBlock) _releaseAt = Time.realtimeSinceStartupAsDouble + .15;
    }
    [HideFromIl2Cpp]
    private void ReleaseBlock()
    {
        var player = _blockedPlayer; _blockedPlayer = null; BlocksInput = false;
        try { if (player is not null && player.inputBlockCount > 0) player.PopInputBlock(); }
        catch (Exception e) { Plugin.Logger.LogWarning("[Chat] player input-block cleanup: " + e.GetType().Name); }
        var uiBlock = _uiBlock; _uiBlock = false;
        try { if (uiBlock && UIManager.inputBlockCount > 0) UIManager.PopGameInputBlock<UIManager>(); }
        catch (Exception e) { Plugin.Logger.LogWarning("[Chat] UI input-block cleanup: " + e.GetType().Name); }
    }
    [HideFromIl2Cpp]
    private void Stop() { Close(true); ReleaseBlock(); _network?.Dispose(); }
    public void OnGUI()
    {
        if (_failed || !Plugin.ChatEnabled || _network == null || !Application.isFocused ||
            !PingWorld.Ready(out var session) || session.IsPaused || BrowserController.IsOpen || HostToolsController.IsOpen ||
            !IsOpen && Time.realtimeSinceStartupAsDouble >= _feedUntil) return;
        try { _view.Draw(_history.Messages, IsOpen, _draft.Text, _status.Length > 0 ? _status : _network.Status, Plugin.ChatKey.Value.ToString()); }
        catch (Exception e) { Stop(); _failed = true; Plugin.Logger.LogError("[Chat] drawing failed; input restored: " + e); }
    }
    [HideFromIl2Cpp]
    internal static void ShutdownCurrent() { if (_active is not null) _active.Stop(); }
    public void OnDestroy() { Stop(); if (_active?.Pointer == Pointer) _active = null; }
}
