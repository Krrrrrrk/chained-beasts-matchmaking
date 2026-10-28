using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

namespace Il2CppSystem {
    public sealed class Action<T> {private readonly System.Action<T> _action;private Action(System.Action<T> action){_action=action;}
        public void Invoke(T value)=>_action(value);public static implicit operator Action<T>(System.Action<T> action)=>new(action);}
}
namespace Il2CppInterop.Runtime.Attributes
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class HideFromIl2CppAttribute : Attribute { }
}
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public readonly IntPtr Pointer;
        public bool Destroyed;
        public MonoBehaviour(IntPtr pointer) { Pointer = pointer; }
    }
    public static class Object { public static void Destroy(object value) { if (value is MonoBehaviour component) component.Destroyed = true; } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x=x; this.y=y; }
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x-b.x, a.y-b.y);
    }
    public static class Application { public static bool isFocused = true; }
    public static class Screen { public static int width = 1920, height = 1080; }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static bool visible; public static CursorLockMode lockState; }
    public sealed class Camera
    {
        public static Camera main = new();
        public bool enabled = true; public GameObject gameObject = new(); public int targetDisplay;
        public float PanX, PanZ, Height = 10;
        public Vector3 Direction = new(0,-1,0);
        public Vector3 WorldToScreenPoint(Vector3 point) => new((point.x-PanX)*90,(point.z-PanZ)*50,1);
        public Ray ScreenPointToRay(Vector3 position) => new() { Pointer = position,
            m_Origin = new(position.x / 90 + PanX, Height, position.y / 50 + PanZ), m_Direction = Direction };
    }
    public struct Ray { public Vector3 Pointer, m_Origin, m_Direction; }
    public struct Plane { public Vector3 m_Normal; public float m_Distance; }
    public struct RaycastHit { public Vector3 point; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static bool HasSurface = true;
        public static Vector3 Surface = new(10, 0, 10);
        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction interaction)
        { hit = new() { point = Surface }; return HasSurface; }
    }
}
namespace UnityEngine.InputSystem.LowLevel
{
    public enum GamepadButton { DpadUp, DpadDown, DpadLeft, DpadRight }
}
namespace UnityEngine.InputSystem
{
    public class InputDevice {
        private static int _next=50000;
        public readonly IntPtr Pointer=new(++_next);
        public bool added=true;
        public T TryCast<T>() where T:class => this as T;
    }
    public sealed class Gamepad:InputDevice {
        public static readonly List<Gamepad> all=new();
        public static Gamepad current;
        private readonly Dictionary<LowLevel.GamepadButton,Button> _buttons=new();
        public Gamepad(){all.Add(this);}
        public Button this[LowLevel.GamepadButton key] {get {if(!_buttons.ContainsKey(key)) _buttons[key]=new();return _buttons[key];}}
        public VectorControl rightStick=new();
    }
    public enum Key { None, G, T, Escape, F8, Enter, NumpadEnter, Backspace, PageUp, PageDown }
    public sealed class Button
    {
        public bool isPressed, wasPressedThisFrame, wasReleasedThisFrame;
        public void Set(bool value)
        { wasPressedThisFrame = value && !isPressed; wasReleasedThisFrame = !value && isPressed; isPressed = value; }
    }
    public sealed class Keyboard
    {
        private static int _nextPointer = 20000;
        public readonly IntPtr Pointer = new(++_nextPointer);
        public static Keyboard current = new();
        private readonly Dictionary<Key, Button> _keys = new();
        public Button this[Key key] { get { if (!_keys.ContainsKey(key)) _keys[key] = new(); return _keys[key]; } }
        public Button escapeKey => this[Key.Escape];
        public Button enterKey=>this[Key.Enter]; public Button numpadEnterKey=>this[Key.NumpadEnter];
        public Button backspaceKey=>this[Key.Backspace];public Button pageUpKey=>this[Key.PageUp];public Button pageDownKey=>this[Key.PageDown];
        private readonly List<Il2CppSystem.Action<char>> _text=new();
        public int TextSubscriptions=>_text.Count;
        public bool ThrowOnTextSubscribe,ThrowOnTextRemove;
        public void add_onTextInput(Il2CppSystem.Action<char> handler){if(ThrowOnTextSubscribe)throw new InvalidOperationException("Injected subscription failure");_text.Add(handler);}
        public void remove_onTextInput(Il2CppSystem.Action<char> handler){if(ThrowOnTextRemove)throw new InvalidOperationException("Injected unsubscription failure");_text.Remove(handler);}
        public void Type(string text){foreach(var c in text)foreach(var handler in _text.ToArray())handler.Invoke(c);}
    }
    public sealed class VectorControl { public Vector2 Value; public Vector2 ReadValue() => Value; }
    public sealed class Mouse
    {
        private static int _nextPointer = 10000;
        public static Mouse current = new();
        public readonly IntPtr Pointer = new(++_nextPointer);
        public VectorControl position = new();
        public int Warps;
        public bool IgnoreWarps;
        private Vector2? _pending;
        public void WarpCursorPosition(Vector2 point) { Warps++; if (!IgnoreWarps) _pending = point; }
        public void ApplyWarp() { if (_pending is { } point) position.Value = point; _pending = null; }
    }
}
namespace ChainedBeasts.Matchmaking
{
    internal sealed class ChatView {
        internal int Page;
        internal static string LastDraft="",LastStatus="";internal static bool LastOpen,ThrowOnDraw;
        internal void Draw(IReadOnlyList<ChatPacket> messages,bool open,string draft,string status,string key) {
            if(ThrowOnDraw)throw new InvalidOperationException("Injected chat drawing failure");LastDraft=draft;LastStatus=status;LastOpen=open;}
    }
    internal sealed class HostToolsView
    {
        internal bool CloseRequested, ConfirmRequested, CancelRequested, KickRequested, UnblockRequested;
        internal ulong KickClient, KickAccount, UnblockAccount;
        internal static int Draws;
        public void Dispose() { }
        internal void Draw(HostModeration policy, IReadOnlyList<HostPeer> peers, IReadOnlyList<BlockedAccount> blocked) { Draws++; }
    }
    public static class BrowserController { public static bool IsOpen; }
    internal sealed class PingVisual
    {
        internal PingPacket Packet;
        internal Vector3 World;
        internal string Label;
        internal double Until;
    }
    internal sealed class PingView : IDisposable
    {
        internal sealed record Frame(bool Open, Vector2 Center, PingKind Selected, bool HasTarget, string Status, PingVisual[] Visuals);
        internal static Frame Last;
        internal static bool ThrowOnDraw;
        internal bool Disposed;
        internal static float Scale => Math.Clamp(Math.Min(Screen.width / 1600f, Screen.height / 900f), .8f, 1.8f);
        internal void Draw(IReadOnlyList<PingVisual> visuals, Camera camera, double now, bool open,
            Vector2 center, PingKind selected, bool hasTarget, string status, string controlsHint = null)
        {
            if (ThrowOnDraw) throw new InvalidOperationException("Injected drawing failure.");
            Last = new(open, center, selected, hasTarget, status,
                visuals.Select(v => new PingVisual { Packet = v.Packet, World = v.World, Label = v.Label, Until = v.Until }).ToArray());
        }
        public void Dispose() { Disposed = true; }
    }
}
