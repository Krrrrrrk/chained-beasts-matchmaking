using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
using UnityEngine.InputSystem;

static class ControllerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using (var f = new Wheel())
        {
            var warps=f.Mouse.Warps;
            f.Frame(true);
            check(!f.Drawn && !PingController.IsWheelOpen && !Cursor.visible && Cursor.lockState==CursorLockMode.Locked,
                "Initial G press waits for tap/hold without drawing the wheel or touching cursor ownership.");
            f.Frame(false,new(1200,650));
            var ping=PingView.Last.Visuals.Single();
            check(ping.Packet.Kind==PingKind.Location && ping.Packet.Position==new PingPosition(10,0,10),
                "Quick release emits exactly one location using the floor point captured on key-down.");
            check(f.Mouse.Warps==warps && !PingController.IsWheelOpen,"Quick tap never warps the pointer or opens the wheel.");
            var until=ping.Until;
            f.Frame(false);f.Frame(false);
            check(PingView.Last.Visuals.Single().Until==until,"Idle updates after a tap cannot grant or refresh another ping.");
            f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Single().Until==until,"Quick taps use the same cooldown as wheel messages.");
            f.Frame(false,dt:1.6);f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Single().Until>until,"Tap rearms after the ordinary shared cooldown.");
        }
        using (var f = new Wheel())
        {
            f.Frame(true);f.Frame(true,dt:.21);
            check(!f.Drawn,"A short held press still has no wheel before the hold threshold.");
            f.Frame(true,dt:.02);
            check(PingView.Last.Open && PingController.IsWheelOpen,"Crossing 0.22 seconds opens the option wheel.");
            f.Frame(false);
            check(PingView.Last.Visuals.Length==0,"Releasing the held wheel at center cancels instead of becoming a quick ping.");
        }
        using (var f = new Wheel())
        {
            f.Frame(true);f.Frame(false,dt:.5);
            check(PingView.Last.Visuals.Length==0,"A long gap with no held frame cannot misclassify a long hold as a quick tap.");
        }
        using (var f = new Wheel())
        {
            Camera.main.Direction=new(1,0,0);f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Length==0 && !f.Node.Log.Messages.Any(s=>s.Contains("disabled")),
                "Quick tap without a valid ground point sends nothing and leaves the subsystem running.");
        }
        foreach(var reason in new[]{"escape","focus","pause","resize","mouse","keyboard","scene","browser","host"})
        using (var f = new Wheel())
        {
            f.Frame(true);var warps=f.Mouse.Warps;
            switch(reason) {
                case "focus":Application.isFocused=false;break;
                case "pause":f.Node.Session.IsPaused=true;break;
                case "resize":Screen.width=1280;break;
                case "mouse":Mouse.current=new();break;
                case "keyboard":Keyboard.current=new();break;
                case "scene":f.Node.Ex.SceneOperationInProgress=true;break;
                case "browser":BrowserController.IsOpen=true;break;
                case "host":typeof(HostToolsController).GetProperty(nameof(HostToolsController.IsOpen)).SetValue(null,true);break;
            }
            f.Frame(false,escape:reason=="escape");
            check(PingView.Last.Visuals.Length==0 && !PingController.IsWheelOpen && f.Mouse.Warps==warps,
                "Pending tap cancels safely on "+reason+" without moving the cursor or sending.");
            BrowserController.IsOpen=false;typeof(HostToolsController).GetProperty(nameof(HostToolsController.IsOpen)).SetValue(null,false);
        }

        using (var f = new Wheel())
        {
            InertBus.ClientReads = 0; f.Frame(false);
            check(!f.Drawn && InertBus.ClientReads == 0, "Idle wheel performs no GUI drawing or local-player ownership scan.");
            f.Hold();
            check(PingView.Last.Open && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Hold key opens wheel and releases cursor.");
            f.Hold(); f.Frame(false);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Center release does not emit a ping.");
            f.Mouse.ApplyWarp();
            check(!Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Cancel restores the pre-wheel cursor flags.");
            check(Near(f.Mouse.position.Value, new(900, 500)), "Cancel restores the original aim point.");
        }
        using (var f = new Wheel())
        {
            var captured = new Vector3(10, 0, 10);
            Physics.Surface = new(10, 9.9f, 10); // A camera-facing occluder must not become the map target.
            f.Hold();
            check(PingView.Last.HasTarget, "A native ground-plane intersection enables location selection despite an elevated collider.");
            Physics.Surface = new(80, 9, 0);
            Camera.main.PanX = 20; Camera.main.PanZ = -5; Camera.main.Height = 25;
            f.Frame(true, f.Direction(0, -120));
            check(PingView.Last.Selected == PingKind.Location, "Up gesture selects the location sector.");
            f.Frame(false);
            var shown = PingView.Last.Visuals.Single();
            check(shown.Packet.Kind == PingKind.Location && shown.Packet.Position == new PingPosition(captured.x, captured.y, captured.z), "Ground target is captured before wheel movement and remains fixed despite camera motion.");
            check(shown.Label == "P1  Look here!", "Location label identifies the correct roster character.");
            f.Node.Players.players[0].transform.position = new(5,0,7);
            Camera.main.PanX = -30; Camera.main.Height = 40;
            f.Frame(false);
            check(PingView.Last.Visuals.Single().World.x == captured.x && PingView.Last.Visuals.Single().World.y == 0 &&
                PingView.Last.Visuals.Single().World.z == captured.z, "Moving the player/camera never repositions an existing location marker.");
            f.Frame(false, dt: 4.1);
            check(PingView.Last.Visuals.Length == 0, "Controller removes an expired marker.");
        }
        using (var f = new Wheel())
        {
            f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Single().Label == "P1  Thanks!", "Thanks renders above the local character.");
            f.Node.Players.players[0].transform.position = new(4, 0, 5);
            f.Frame(false);
            var world = PingView.Last.Visuals.Single().World;
            check(world.x == 4 && world.y == 2.7f && world.z == 5, "Overhead message follows the live moving character.");
            var until = PingView.Last.Visuals.Single().Until;
            f.Gesture(PingKind.Sorry);
            check(PingView.Last.Visuals.Single().Label == "P1  Thanks!" && PingView.Last.Visuals.Single().Until == until, "Cooldown blocks a second immediate gesture without refreshing the old label.");
            f.Frame(false, dt: 1.6); f.Gesture(PingKind.Sorry);
            check(PingView.Last.Visuals.Length == 1 && PingView.Last.Visuals[0].Label == "P1  Sorry!", "Next valid message replaces the account's previous visual.");
        }
        using (var f = new Wheel())
        {
            f.Node.Players.players[0].agentModel = new() { headTransform = new() { position = new(4, 1.4f, 5) } };
            f.Gesture(PingKind.Thanks);
            var world = PingView.Last.Visuals.Single().World;
            check(world.x == 4 && Math.Abs(world.y - 1.95f) < .001 && world.z == 5, "Loaded model head transform anchors the message above the actual head.");
            f.Node.Players.players[0].agentModel.headTransform.position = new(float.NaN, 0, 0); f.Frame(false);
            check(PingView.Last.Visuals.Length == 0, "Invalid transient model pose is hidden rather than passed into drawing.");
        }
        using (var f = new Wheel())
        {
            f.Node.Players.players[0].transform.position = new(float.NaN, 0, 0); f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Length == 0 && !f.Node.Log.Messages.Any(s => s.Contains("disabled")), "Invalid live root pose skips a send without faulting the subsystem.");
        }
        using (var f = new Wheel())
        {
            f.Frame(true, new(float.NaN, 0));
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Invalid input coordinates cannot produce a native GUI rectangle.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); f.Frame(true, f.Direction(100, 80), escape: true);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Escape cancels a highlighted option.");
            f.Frame(true, f.Direction(-100, 80));
            check(!PingView.Last.Open, "Holding the original key after Escape does not reopen the wheel.");
            f.Frame(false); f.Hold();
            check(PingView.Last.Open, "Release then fresh press re-arms the wheel.");
        }
        using (var f = new Wheel())
        {
            f.Frame(false, new(1919, 1079)); f.Hold();
            var center = PingView.Last.Center;
            check(center.x < 1919 && center.y > 1 && f.Mouse.Warps > 0, "Corner opening recenters the pointer into a visible wheel.");
            f.Hold();
            check(PingView.Last.Selected == PingKind.None, "Cursor recentering itself cannot select a ping.");
            f.Frame(true, f.Direction(0, -120)); f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Kind == PingKind.Location, "Top-edge opening can still select the upper sector.");
            f.Mouse.ApplyWarp();
            check(Near(f.Mouse.position.Value, new(1919, 1079)), "Corner selection restores the exact original cursor point.");
        }
        using (var f = new Wheel())
        {
            f.Mouse.IgnoreWarps = true;
            f.Frame(false, new(1919, 1079)); f.Hold(); f.Frame(true, dt: .3);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "A refused cursor warp cancels safely instead of sending a random choice.");
        }
        using (var f = new Wheel())
        {
            Camera.main.Direction = new(1,0,0); f.Hold();
            check(!PingView.Last.HasTarget, "A camera ray parallel to the ground disables location.");
            f.Frame(true, f.Direction(0, -120)); f.Frame(false);
            check(PingView.Last.Visuals.Length == 0, "Releasing disabled location sends nothing.");
            f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Single().Packet.Kind == PingKind.Thanks, "Unavailable ground intersection does not disable quick messages.");
        }
        using (var f = new Wheel())
        {
            Camera.main.PanX = 1000; f.Hold();
            check(!PingView.Last.HasTarget, "Distant ground intersection is not a usable location target.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); f.Frame(true, f.Direction(100, 80));
            var warps = f.Mouse.Warps; Application.isFocused = false; f.Hold();
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0 && f.Mouse.Warps == warps, "Losing focus cancels without moving another application's cursor.");
            Application.isFocused = true; f.Hold();
            check(!PingView.Last.Open, "Focus recovery while key is held cannot emit/reopen.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); Screen.width = 1280; Screen.height = 720; f.Hold();
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Window resize cancels the old geometry/target.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); Mouse.current = null; f.Hold();
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Mouse removal cancels without an acquisition-like callback.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); Keyboard.current = null; f.Hold();
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Keyboard removal cancels without sending.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); f.Frame(true, f.Direction(100, 80)); Mouse.current = new(); f.Frame(false);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Switching to a different mouse cannot release a stale selection.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); f.Frame(true, f.Direction(100, 80)); Keyboard.current = new(); f.Frame(false);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Switching keyboards cannot release a stale selection.");
        }
        using (var f = new Wheel())
        {
            f.Gesture(PingKind.Thanks); f.Node.Ex.SceneOperationInProgress = true; f.Frame(false);
            check(!f.Drawn, "Scene loading hides the controller overlay.");
            f.Node.Ex.SceneOperationInProgress = false; f.Frame(false);
            check(PingView.Last.Visuals.Length == 0, "Old labels do not reappear after the scene becomes ready.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); f.Node.Session.IsPaused = true;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None; // Native pause menu's request.
            var warps = f.Mouse.Warps; f.Frame(false);
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "Pause cancels the selection before key release.");
            check(Cursor.visible && Cursor.lockState == CursorLockMode.None && f.Mouse.Warps == warps,
                "Cancel yields cursor ownership to the native pause menu instead of hiding/relocking it.");
        }
        using (var f = new Wheel())
        {
            f.Gesture(PingKind.Thanks); f.Node.Session.IsPaused = true; f.Frame(false);
            check(!f.Drawn, "World messages do not draw over the native pause menu.");
            f.Frame(false, dt: 5); f.Node.Session.IsPaused = false; f.Frame(false);
            check(PingView.Last.Visuals.Length == 0, "Pausing does not preserve an expired overhead label.");
        }
        using (var f = new Wheel())
        {
            Plugin.PingKey.Value = Key.None; f.Hold();
            check(!PingView.Last.Open && PingView.Last.Visuals.Length == 0, "None binding disables input without indexing an invalid native key.");
        }
        using (var f = new Wheel())
        {
            Plugin.PingKey.Value = Key.F8; f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Single().Packet.Kind == PingKind.Thanks, "Configured alternative key drives the same wheel.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); Plugin.PingsEnabled = false; f.Frame(false);
            check(f.Controller.Destroyed && !Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Plugin unload closes the wheel and destroys its old controller.");
            check(f.Node.Manager.CustomMessagingManager.Handlers.Count == 0, "Unload removes the optional relay handler.");
        }
        using (var f = new Wheel())
        {
            f.Hold(); PingView.ThrowOnDraw = true; f.Hold();
            check(f.Node.Log.Messages.Any(s => s.Contains("disabled for this launch")) && !Cursor.visible,
                "Drawing failure restores the cursor and stays inside the ping subsystem.");
            check(InertBus.DisconnectCalls == 0, "UI failure never disconnects the game.");
        }
        check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "Assembly-CSharp" or "Unity.Netcode.Runtime" or "Unity.InputSystem"),
            "Controller/adapter checks did not load game runtime assemblies.");
    }

    private static bool Near(Vector2 a, Vector2 b) => Math.Abs(a.x-b.x) < .01 && Math.Abs(a.y-b.y) < .01;

    private sealed class Wheel : IDisposable
    {
        internal readonly Node Node;
        internal readonly PingController Controller;
        internal readonly Keyboard Keyboard;
        internal readonly Mouse Mouse;
        internal bool Drawn;
        internal Wheel()
        {
            InertBus.Reset(); Node = InertBus.Add(0, true, false); InertBus.Roster(0);
            Plugin.PingsEnabled = true; Plugin.PingKey.Value = Key.G;
            Keyboard = UnityEngine.InputSystem.Keyboard.current = new(); Mouse = UnityEngine.InputSystem.Mouse.current = new();
            Mouse.position.Value = new(900, 500);
            Screen.width = 1920; Screen.height = 1080; Application.isFocused = true;
            Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked;
            Physics.HasSurface = true; Physics.Surface = new(10, 0, 10);
            Camera.main = new(); PingView.ThrowOnDraw = false; BrowserController.IsOpen = false;
            Controller = new(new IntPtr(30000)); Frame(false);
        }
        internal void Frame(bool held, Vector2? pointer = null, bool escape = false, double dt = .05)
        {
            InertBus.Now += dt; Node.Activate();
            Keyboard[Key.G].Set(held && Plugin.PingKey.Value == Key.G);
            Keyboard[Key.F8].Set(held && Plugin.PingKey.Value == Key.F8);
            Keyboard.escapeKey.Set(escape);
            UnityEngine.InputSystem.Mouse.current?.ApplyWarp();
            if (pointer is { } p && UnityEngine.InputSystem.Mouse.current != null) UnityEngine.InputSystem.Mouse.current.position.Value = p;
            PingView.Last = null; Controller.Update(); Controller.OnGUI();
            Drawn = PingView.Last != null;
            PingView.Last ??= new(false, default, PingKind.None, false, "not drawn", Array.Empty<PingVisual>());
        }
        internal Vector2 Direction(float x, float y)
        { var c = PingView.Last?.Center ?? new Vector2(900, 580); return new(c.x+x, Screen.height-(c.y+y)); }
        internal void Hold() { Frame(true); Frame(true, dt: .23); }
        internal void Gesture(PingKind kind)
        {
            Hold(); Frame(true, kind switch
            { PingKind.Location => Direction(0, -120), PingKind.Thanks => Direction(120, 80), _ => Direction(-120, 80) });
            Frame(false);
        }
        public void Dispose()
        {
            Application.isFocused = true; PingView.ThrowOnDraw = false;
            Node.Activate(); Controller.OnDestroy(); Plugin.PingsEnabled = true;
            UnityEngine.InputSystem.Mouse.current = Mouse; UnityEngine.InputSystem.Keyboard.current = Keyboard;
        }
    }
}
