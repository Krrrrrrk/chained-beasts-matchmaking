using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

static class GamepadChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using(var f=new PadFixture())
        {
            InertBus.ClientReads=0;f.Frame(false);
            check(InertBus.ClientReads==0,"Idle connected pads require no live-player ownership scan.");
            f.Frame(true);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Single().Label=="PING TARGET","Pad key-down previews the actual six-unit ground target.");
            check(!Cursor.visible && Cursor.lockState==CursorLockMode.Locked,"Pad preview never claims the mouse cursor.");
            f.Frame(false);
            var ping=PingView.Last.Visuals.Single();
            check(ping.Packet.Kind==PingKind.Location && ping.Packet.Position==new PingPosition(0,0,6),"Pad tap sends the primary player's facing-ground target with no mouse/keyboard attached.");
            var until=ping.Until;f.Frame(false);f.Frame(false);
            check(PingView.Last.Visuals.Single().Until==until,"Pad tap emits once, not on subsequent idle updates.");
            f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Single().Packet.Kind==PingKind.Location,"Pad wheel shares the ordinary location/message cooldown.");
            f.Frame(false,dt:1.6);f.Gesture(PingKind.Sorry);
            check(PingView.Last.Visuals.Single().Label=="P1  Sorry!","Controller quick message uses the existing local relay and roster label.");
        }

        using(var f=new PadFixture(7))
        {
            var before=f.Node.GameplayImage();f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Author==7 && PingView.Last.Visuals.Single().Packet.PlayerIndex==0,
                "Joining-client controller pings retain the actual local network owner and character index.");
            check(f.Node.GameplayImage()==before,"Controller pings do not alter roster, spawn state or gameplay positions.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(false);var until=PingView.Last.Visuals.Single().Until;
            Plugin.PingKey.Value=Key.G;var keys=Keyboard.current=new();Mouse.current=new();Mouse.current.position.Value=new(900,500);
            f.Frame(false);keys[Key.G].Set(true);f.Frame(false);keys[Key.G].Set(false);f.Frame(false);
            check(PingView.Last.Visuals.Single().Until==until,"Switching from controller tap to G cannot bypass the shared cooldown.");
        }
        foreach(var kind in new[]{PingKind.Location,PingKind.Thanks,PingKind.Sorry})
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(true,dt:.23);
            check(PingController.IsWheelOpen && PingView.Last.Open,"Holding the assigned pad opens the same radial after 0.22 seconds.");
            f.Frame(true,PadFixture.Direction(kind));f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Kind==kind,"Right-stick direction sends "+kind+" on D-pad release.");
            check(!PingController.IsWheelOpen && !Cursor.visible && Cursor.lockState==CursorLockMode.Locked,"Pad wheel closes without changing cursor state.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true,new(1,0));f.Frame(true,new(-1,0),dt:.23);
            f.Frame(true,new(0,1));f.Frame(false);
            var point=PingView.Last.Visuals.Single().Packet.Position;
            check(Math.Abs(point.X-6)<.001 && Math.Abs(point.Z)<.001,"Screen-relative initial stick aim fixes a world point before radial selection changes the stick.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(true,dt:.23);f.Frame(true,new(.1f,.1f));f.Frame(false);
            check(PingView.Last.Visuals.Length==0 && !PingController.IsWheelOpen,"Stick dead zone and center release cancel instead of accidentally pinging.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(false,dt:.5);
            check(PingView.Last.Visuals.Length==0,"A hitch cannot turn a long controller hold into a tap.");
        }
        using(var f=new PadFixture())
        {
            f.OtherHeld=true;Gamepad.current=f.Other;f.Frame(false);f.Frame(false);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Length==0,"An unpaired second controller/current-pad switch cannot operate the primary player's wheel.");
            f.OtherHeld=false;f.Frame(false);f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Author==0,"The paired primary pad still works while another device is Gamepad.current.");
        }

        using(var f=new PadFixture())
        {
            // Simulate a GlobalInput.Device fallback changing independently of actual assignment.
            f.Player.InputUser.Device=f.Other;f.OtherHeld=true;Gamepad.current=f.Other;f.Frame(false);
            f.OtherHeld=false;f.Frame(false);
            check(PingView.Last.Visuals.Length==0 && !PingController.IsWheelOpen,"A last-active/global device fallback cannot bypass the explicit client-device map.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(true,dt:.23);
            f.Player.InputUser.Device=f.Other;GameInput.assignedDevices[f.Player.clientID.Value]=f.Other;f.Frame(false);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Length==0,"An actual device reassignment cancels the prior controller's wheel.");
            f.OtherHeld=false;f.Frame(false);f.OtherHeld=true;f.Frame(false);f.OtherHeld=false;f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Kind==PingKind.Location,"A fresh press works after reassignment to the new paired controller.");
        }
        foreach(var binding in Enum.GetValues<PingPadButton>())
        using(var f=new PadFixture())
        {
            Plugin.PingPadButton.Value=binding;f.Frame(true);f.Frame(false);
            check(PingView.Last.Visuals.Single().Packet.Kind==PingKind.Location,"Configured controller binding works: "+binding);
        }
        foreach(var gate in new[]{"ui","blocked","lobby","result","paused","focus","browser","host","unassigned","removed","mapping-lost"})
        using(var f=new PadFixture())
        {
            f.ApplyGate(gate);f.Frame(true);f.Frame(false);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Length==0,"No controller interception or ping while "+gate+" owns input.");
        }
        foreach(var gate in new[]{"ui","blocked","lobby","result","paused","focus","browser","host","unassigned","removed","mapping-lost","reassigned","owner","resize","scene","disabled","binding"})
        using(var f=new PadFixture())
        {
            f.Frame(true);f.Frame(true,dt:.23);f.Frame(true,PadFixture.Direction(PingKind.Thanks));
            f.ApplyGate(gate);f.Frame(false);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Length==0,"Active controller gesture cancels safely on "+gate+".");
            check(f.Node.Log.Messages.All(x=>!x.Contains("disabled for this launch")),"Normal controller cancellation is not an interop fault: "+gate);
        }
        using(var f=new PadFixture())
        {
            f.Frame(true,new(float.NaN,0));f.Frame(false);
            check(PingView.Last.Visuals.Length==0,"Invalid controller aim cannot send a location.");
            f.Frame(false);f.Gesture(PingKind.Thanks);
            check(PingView.Last.Visuals.Single().Packet.Kind==PingKind.Thanks,"Unavailable controller ground aim does not disable quick messages.");
        }
        using(var f=new PadFixture())
        {
            f.Frame(true);var keys=Keyboard.current=new();keys.escapeKey.Set(true);f.Frame(true);
            check(!PingController.IsWheelOpen && PingView.Last.Visuals.Length==0,"Keyboard Escape can cancel a pending controller gesture.");
        }
        check(InertBus.DisconnectCalls==0,"Controller cosmetics never disconnect or start/stop the game.");
    }

    private sealed class PadFixture:IDisposable
    {
        internal readonly Node Node;
        internal readonly Player Player;
        internal readonly Gamepad Pad,Other;
        internal readonly PingController Controller;
        internal bool OtherHeld;
        internal PadFixture(ulong id=0)
        {
            InertBus.Reset();Node=InertBus.Add(id,id==0,false);InertBus.Roster(id);Node.Activate();
            Plugin.PingsEnabled=true;Plugin.PingGamepadEnabled=true;Plugin.PingKey.Value=Key.None;
            Player=Node.Players.players[0];Pad=new();Other=new();Player.InputUser.Device=Pad;GameInput.assignedDevices[Player.clientID.Value]=Pad;
            Keyboard.current=null;Mouse.current=null;Camera.main=new();
            Application.isFocused=true;BrowserController.IsOpen=false;SetHostOpen(false);
            Cursor.visible=false;Cursor.lockState=CursorLockMode.Locked;Screen.width=1920;Screen.height=1080;
            PingView.ThrowOnDraw=false;Controller=new(new IntPtr(70000));Frame(false);
        }
        internal void Frame(bool held,Vector2? stick=null,double dt=.05)
        {
            InertBus.Now+=dt;Node.Activate();
            foreach(var button in Enum.GetValues<GamepadButton>())
            {
                Pad[button].Set(held && (int)button==(int)Plugin.PingPadButton.Value);
                Other[button].Set(OtherHeld && (int)button==(int)Plugin.PingPadButton.Value);
            }
            if(stick is {} value) Pad.rightStick.Value=value;
            PingView.Last=null;Controller.Update();Controller.OnGUI();
            PingView.Last??=new(false,default,PingKind.None,false,"not drawn",Array.Empty<PingVisual>());
        }
        internal void Gesture(PingKind kind){Frame(true,new(0,0));Frame(true,dt:.23);Frame(true,Direction(kind));Frame(false);}
        internal static Vector2 Direction(PingKind kind)=>kind switch{PingKind.Location=>new(0,1),PingKind.Thanks=>new(1,-.6f),_=>new(-1,-.6f)};
        internal void ApplyGate(string gate)
        {
            switch(gate)
            {
                case "ui":UIManager.GameInputBlocked=true;break;
                case "blocked":Player.IsInputBlocked=true;break;
                case "lobby":Node.Session.Level=null;break;
                case "result":Node.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;break;
                case "paused":Node.Session.IsPaused=true;break;
                case "focus":Application.isFocused=false;break;
                case "browser":BrowserController.IsOpen=true;break;
                case "host":SetHostOpen(true);break;
                case "unassigned":Player.InputUser.Device=null;break;
                case "mapping-lost":GameInput.assignedDevices.Clear();break;
                case "removed":Pad.added=false;break;
                case "reassigned":Player.InputUser.Device=Other;break;
                case "owner":Player.playerData.Pointer=new(99999);break;
                case "resize":Screen.width=1280;break;
                case "scene":Node.Ex.SceneOperationInProgress=true;break;
                case "disabled":Plugin.PingGamepadEnabled=false;break;
                case "binding":Plugin.PingPadButton.Value=PingPadButton.DpadLeft;break;
            }
        }
        public void Dispose(){Node.Activate();Controller.OnDestroy();Application.isFocused=true;BrowserController.IsOpen=false;SetHostOpen(false);}
        private static void SetHostOpen(bool value)=>typeof(HostToolsController).GetProperty(nameof(HostToolsController.IsOpen)).SetValue(null,value);
    }
}
