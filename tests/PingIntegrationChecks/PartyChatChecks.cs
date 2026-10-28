using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
using UnityEngine.InputSystem;

static class PartyChatChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using(var f=new Party())
        {
            f.Run(3);
            check(f.Chats.Values.All(c=>c.Shared),"Chat negotiates an owner-correlated relay independently of pings.");
            f.Nodes[7].Activate();check(f.Chats[7].Submit("Hello party"),"An updated client queues party text.");f.Run(.75);
            check(f.Messages.Values.All(m=>m.Count==1&&m[0].Author==7&&m[0].PlayerIndex==1&&m[0].Text=="Hello party"),
                "Host and both clients receive the same text with the real sender and owned character slot.");
            check(f.Messages[0][0].Name=="Sample name","Host supplies the native roster name, not a name in the request.");
            var envelope=InertBus.History.Last(e=>e.Channel=="cbmm.chat.v1"&&e.From==7&&ChatProtocol.Decode(e.Bytes,out var p)&&p.Message==ChatMessage.Request);
            InertBus.Send(7,0,envelope.Channel,envelope.Bytes);InertBus.Pump();f.Run(.25);
            check(f.Messages[0].Count==1,"Duplicate chat request cannot be relayed twice.");
            f.Nodes[7].Activate();f.Chats[7].Submit("Too soon");f.Run(.25);
            check(f.Messages[0].Count==1,"Host rate-limits rapid requests even if the client bypasses its UI cooldown.");
            var shown=InertBus.History.Last(e=>e.Channel=="cbmm.chat.v1"&&e.From==0&&e.To==8&&ChatProtocol.Decode(e.Bytes,out var p)&&p.Message==ChatMessage.Show);
            InertBus.Send(7,8,shown.Channel,shown.Bytes);InertBus.Pump();f.Run(.25);
            check(f.Messages[8].Count==1,"A peer cannot impersonate a host display message.");
            var forged=(byte[])envelope.Bytes.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(32),8);
            InertBus.Send(7,0,envelope.Channel,forged);InertBus.Pump();f.Run(.25);
            check(f.Messages[0].Count==1,"Request-author spoofing is rejected before relay.");
            f.Run(2);f.Nodes[0].Activate();check(f.Chats[0].Submit("Host message"),"Host can use the same party chat.");f.Run(.5);
            check(f.Messages.Values.All(m=>m.Count==2&&m[^1].Author==0),"Host's network ID zero is authenticated correctly.");
            f.Nodes[8].Online=false;InertBus.ThrowForOffline=true;f.Run(2);f.Nodes[7].Activate();f.Chats[7].Submit("Still here");f.Run(.75);
            check(f.Messages[0].Count==3&&f.Messages[7].Count==3,"One departing recipient does not break chat for the remaining party.");
            check(InertBus.DisconnectCalls==0,"Chat send failures never disconnect or start/stop the session.");
        }

        using(var f=new Party())
        {
            f.Run(3);f.Nodes[7].Activate();f.Chats[7].Submit("baseline");f.Run(.75);
            var envelope=InertBus.History.Last(e=>e.Channel=="cbmm.chat.v1"&&e.From==7&&ChatProtocol.Decode(e.Bytes,out var p)&&p.Message==ChatMessage.Request);
            ChatProtocol.Decode(envelope.Bytes,out var request);f.Run(2);
            f.Nodes[0].Players.players[1].clientID.Value.DisplayName="badword gamer";
            var bypass=request with{Text="raw badword request",Sequence=request.Sequence+100,Created=InertBus.Now};
            InertBus.Send(7,0,envelope.Channel,ChatProtocol.Encode(bypass));InertBus.Pump();f.Run(.75);
            check(f.Messages.Values.All(m=>m.Count==2&&m[^1].Text=="raw ******* request"&&m[^1].Name=="******* gamer"),
                "Host filters raw requests and names even if the client bypasses local filtering.");
            var broadcast=InertBus.History.Last(e=>e.Channel=="cbmm.chat.v1"&&e.From==0&&e.To==8&&ChatProtocol.Decode(e.Bytes,out var p)&&p.Message==ChatMessage.Show);
            ChatProtocol.Decode(broadcast.Bytes,out var display);
            InertBus.Send(0,8,broadcast.Channel,ChatProtocol.Encode(display with{Text="older host badword",Name="badword name",Sequence=display.Sequence+100}));
            InertBus.Pump();f.Run(.25);
            check(f.Messages[8][^1].Text=="older host *******"&&f.Messages[8][^1].Name=="******* name",
                "New receivers also filter a valid display from an older unfiltered host.");
        }
        using(var f=new Party())
        {
            f.Run(3);Crosstales.BWF.BWFManager.instance.isReady=false;f.Nodes[7].Activate();
            check(!f.Chats[7].Submit("badword")&&f.Chats[7].Status.Contains("filter loading/unavailable"),
                "Unavailable native filter prevents sending and exposes an explicit status.");
            check(f.Messages.Values.All(m=>m.Count==0),"Unready filter produces no unfiltered echo.");
        }
        using(var f=new Party(hostChat:false))
        {
            f.Run(3);f.Nodes[7].Activate();
            check(!f.Chats[7].Shared&&!f.Chats[7].Submit("Not shared"),"Vanilla/non-chat hosts do not receive a fake local-success echo.");
            check(!InertBus.History.Any(e=>e.Channel=="cbmm.chat.v1")&&InertBus.Unregistered==0,"Chat sends no probe or text to an unadvertised host.");
        }
        using(var f=new Editor())
        {
            f.Frame(true);
            check(ChatController.IsOpen&&ChatController.BlocksInput&&f.Player.inputBlockCount==1&&UIManager.inputBlockCount==1,"Opening chat borrows exactly one native player/UI input block.");
            check(f.Keys.TextSubscriptions==1,"Typing has one native text-input subscription.");
            f.Frame(false);f.Keys.Type("Hello 👋");f.Frame(false);
            check(ChatView.LastDraft=="Hello 👋","Actual controller draft receives Unicode text from the subscribed keyboard.");
            f.Frame(true);
            check(!ChatController.IsOpen&&ChatController.BlocksInput,"Sending closes chat but keeps its input guard through the send-key frame.");
            f.Frame(false,dt:.2);
            check(!ChatController.BlocksInput&&f.Player.inputBlockCount==0&&UIManager.inputBlockCount==0&&f.Keys.TextSubscriptions==0,
                "Successful close releases both blocks and text input exactly once.");
            f.Controller.OnDestroy();f.Controller.OnDestroy();
            check(f.Player.PushCount==1&&f.Player.PopCount==1,"Repeated destruction does not pop a native block twice.");
        }

        using(var f=new Editor(Key.T))
        {
            f.Frame(true);check(!ChatController.IsOpen,"Enter alone no longer opens chat when T is configured.");
            f.Frame(false);f.Keys.Type("t");f.Keys[Key.T].Set(true);f.Frame(false);
            check(ChatController.IsOpen&&f.Keys.TextSubscriptions==1,"T opens chat and subscribes only after the opening key's text event.");
            f.Keys[Key.T].Set(false);f.Keys.Type("test with t");f.Frame(false);
            check(ChatView.LastDraft=="test with t","Typing T inside chat remains ordinary message text.");
            f.Frame(true);check(!ChatController.IsOpen,"Enter still sends and closes T-opened chat.");
            f.Frame(false,dt:.2);check(!ChatController.BlocksInput&&f.Player.inputBlockCount==0,"T chat returns gameplay input after sending.");
        }
        foreach(var reason in new[]{"escape","pause","focus","keyboard","scene","ui","actor","unload","draw","ui-reset","actor-reset"})
        using(var f=new Editor())
        {
            f.Frame(true);f.Frame(false);f.Keys.Type("unsent");
            switch(reason)
            {
                case "pause":f.Node.Session.IsPaused=true;break;
                case "focus":Application.isFocused=false;break;
                case "keyboard":Keyboard.current=new();break;
                case "scene":f.Node.Ex.SceneOperationInProgress=true;break;
                case "ui":UIManager.PushGameInputBlock<UIManager>();break;
                case "actor":f.Player.PushInputBlock();break;
                case "unload":Plugin.ChatEnabled=false;break;
                case "draw":ChatView.ThrowOnDraw=true;break;
                case "ui-reset":UIManager.inputBlockCount=0;break;
                case "actor-reset":f.Player.inputBlockCount=0;break;
            }
            f.Frame(false,escape:reason=="escape");f.Frame(false,dt:.3);
            check(!ChatController.IsOpen&&!ChatController.BlocksInput&&f.Keys.TextSubscriptions==0,"Chat input cleanup survives "+reason+".");
            check(UIManager.inputBlockCount==(reason=="ui"?1:0)&&f.Player.inputBlockCount==(reason=="actor"?1:0),
                "Chat removes only its own blocks without underflow on "+reason+".");
        }

        using(var f=new Editor())
        {
            f.Keys.ThrowOnTextSubscribe=true;f.Frame(true);
            check(!ChatController.IsOpen&&!ChatController.BlocksInput&&f.Player.inputBlockCount==0&&UIManager.inputBlockCount==0,
                "A text-subscription error restores both native input leases before disabling chat.");
        }
        using(var f=new Editor())
        {
            f.Frame(true);f.Frame(false);f.Keys.ThrowOnTextRemove=true;f.Frame(false,escape:true);f.Frame(false,dt:.3);
            check(!ChatController.IsOpen&&!ChatController.BlocksInput&&f.Player.inputBlockCount==0&&UIManager.inputBlockCount==0,
                "A native unsubscription error cannot leave gameplay input blocked.");
            f.Keys.Type("stale callback");f.Frame(true);
            check(!ChatController.IsOpen,"Failed unsubscription disables reopening and makes an orphan callback inert.");
        }
        using(var f=new Editor())
        {
            f.Player.IsInputBlocked=true;f.Frame(true);
            check(!ChatController.IsOpen&&f.Player.PushCount==0,"Native blocked UI/player prevents chat from opening over another interaction.");
        }
        using(var f=new Editor())
        {
            f.Frame(true);f.Frame(false);f.Keys.Type("pending");f.Frame(false);ChatController.ShutdownCurrent();
            check(!ChatController.IsOpen&&!ChatController.BlocksInput&&f.Keys.TextSubscriptions==0&&f.Player.inputBlockCount==0,
                "Plugin unload has a synchronous input/subscription cleanup path.");
        }
    }

    private sealed class Party:IDisposable
    {
        internal readonly Dictionary<ulong,Node> Nodes=new();
        internal readonly Dictionary<ulong,ChatNetwork> Chats=new();
        internal readonly Dictionary<ulong,List<ChatPacket>> Messages=new();
        internal Party(bool hostChat=true)
        {
            InertBus.Reset();
            foreach(var id in new ulong[]{0,7,8})Nodes[id]=InertBus.Add(id,id==0,false);
            InertBus.Roster(0,7,8);
            foreach(var id in Nodes.Keys)
            {
                Messages[id]=new();if(id==0&&!hostChat)continue;
                Nodes[id].Activate();var chat=new ChatNetwork();chat.Received+=p=>Messages[id].Add(p);Chats[id]=chat;
            }
        }
        internal void Run(double seconds)
        {
            var until=InertBus.Now+seconds;
            do {foreach(var pair in Chats){if(!Nodes[pair.Key].Online)continue;Nodes[pair.Key].Activate();pair.Value.Tick();}
                InertBus.Pump();InertBus.Now+=.25;}while(InertBus.Now<until);
        }
        public void Dispose(){foreach(var pair in Chats){Nodes[pair.Key].Activate();pair.Value.Dispose();}}
    }
    private sealed class Editor:IDisposable
    {
        internal readonly Node Node;internal readonly Player Player;internal readonly Keyboard Keys;internal readonly ChatController Controller;
        internal Editor(Key openKey=Key.Enter)
        {
            InertBus.Reset();Node=InertBus.Add(0,true,false);InertBus.Roster(0);Node.Activate();Player=Node.Players.players[0];
            Plugin.ChatEnabled=true;Plugin.ChatKey.Value=openKey;Keys=Keyboard.current=new();
            Application.isFocused=true;BrowserController.IsOpen=false;ChatView.ThrowOnDraw=false;
            Controller=new(new IntPtr(80000));Frame(false);
        }
        internal void Frame(bool enter,bool escape=false,double dt=.05)
        {
            InertBus.Now+=dt;Node.Activate();Keys.enterKey.Set(enter);Keys.escapeKey.Set(escape);
            Controller.Update();Controller.OnGUI();
        }
        public void Dispose(){Node.Activate();ChatView.ThrowOnDraw=false;Controller.OnDestroy();Application.isFocused=true;Plugin.ChatEnabled=true;}
    }
}
