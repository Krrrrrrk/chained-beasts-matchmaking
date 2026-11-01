using System.Reflection;
using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
using UnityEngine.InputSystem;

static class PingDeliveryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using(var f=new Pair())
        {
            foreach(var data in f.Guest.Session.runDataManager.playerDatas.ToArray())
            {
                var at=f.Guest.Session.runDataManager.playerDatas.IndexOf(data);
                f.Guest.Session.runDataManager.playerDatas[at]=new(){Pointer=new(99000+at),playerID=data.playerID};
            }
            check(f.Host.Submit(PingKind.Location,new(3,0,4)),"Host can submit while recipient has refreshed player-data instances.");f.Run(.3);
            check(f.Visuals.Length==1&&f.Visuals[0].Packet.Author==0&&f.Visuals[0].World.x==3,
                "A real receiving controller draws a host ping despite equivalent replacement roster instances.");
            f.Guest.Activate();check(f.Network.Submit(PingKind.Thanks,new(4,0,0)),"Client with refreshed roster can submit through its existing actor binding.");f.Run(.3);
            check(f.Host.Shown.Any(p=>p.Author==7&&p.Kind==PingKind.Thanks),"Host receives the client's ping in the opposite direction.");
        }
        using(var f=new Pair())
        {
            f.Guest.Ex.SceneOperationInProgress=true;
            check(f.Host.Submit(PingKind.Thanks),"Host sends during the recipient's brief readiness gap.");f.Run(.4);
            check(f.Visuals.Length==0,"No marker is drawn into an unready scene.");
            f.Guest.Ex.SceneOperationInProgress=false;f.Run(.15);
            check(f.Visuals.Length==1&&f.Visuals[0].Until-InertBus.Now<PingProtocol.Lifetime,
                "Recipient initializes scene state before draining queued pings and preserves remaining lifetime.");
        }
        using(var f=new Pair())
        {
            var actor=f.Guest.Players.players[0];f.Guest.Players.players.RemoveAt(0);
            f.Host.Submit(PingKind.Sorry);f.Run(.4);check(f.Visuals.Length==0,"A missing remote actor withholds an overhead ping.");
            f.Guest.Players.players.Insert(0,actor);f.Run(.15);
            check(f.Visuals.Length==1&&f.Visuals[0].Packet.Kind==PingKind.Sorry,"A briefly late actor binding can display the same bounded pending ping.");
        }
        using(var f=new Pair())
        {
            f.Guest.Ex.SceneOperationInProgress=true;f.Host.Submit(PingKind.Thanks);f.Run(4.5);
            f.Guest.Ex.SceneOperationInProgress=false;f.Run(.2);
            check(f.Visuals.Length==0,"An expired pending ping never appears after readiness returns.");
            check(InertBus.DisconnectCalls==0,"Delivery recovery never disconnects or starts/stops the game session.");
        }
    }
    private sealed class Pair:IDisposable
    {
        internal readonly Node Host,Guest;
        internal readonly PingController Controller;
        internal PingNetwork Network=>(PingNetwork)typeof(PingController).GetField("_network",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Controller);
        internal PingVisual[] Visuals=Array.Empty<PingVisual>();
        internal Pair()
        {
            InertBus.Reset();Host=InertBus.Add(0,true);Guest=InertBus.Add(7,false,false);InertBus.Roster(0,7);
            Plugin.PingsEnabled=true;Keyboard.current=null;Mouse.current=null;BrowserController.IsOpen=false;Application.isFocused=true;
            Camera.main=new();Screen.width=1920;Screen.height=1080;Guest.Activate();Controller=new(new IntPtr(71000));Run(3);
        }
        internal void Run(double seconds)
        {
            var until=InertBus.Now+seconds;
            do {
                Host.Tick();Guest.Activate();PingView.Last=null;Controller.Update();Controller.OnGUI();
                Visuals=PingView.Last?.Visuals??Array.Empty<PingVisual>();InertBus.Pump();InertBus.Now+=.05;
            }while(InertBus.Now<until);
        }
        public void Dispose(){Guest.Activate();Controller.OnDestroy();Host.Activate();Host.Pings.Dispose();}
    }
}
