using System.Text.Json;
using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;

static class HostViewChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var size in new[]{(640,360),(1280,720),(1920,1080),(2560,1440)})
        foreach(var mode in new[]{"empty","peers","pending","blocked"})
        {
            Screen.width=size.Item1;Screen.height=size.Item2;
            var backend=new Backend();var policy=new HostModeration(backend);policy.Refresh();
            if(mode!="empty") backend.Peers=new[]{new HostPeer(7,10007,"First guest",1),new HostPeer(8,0,"Unverified guest",2)};
            if(mode=="pending") policy.RequestKick(7,10007);
            if(mode=="blocked") {policy.RequestKick(7,10007);policy.ConfirmKick();}
            using var view=new HostToolsView();
            var calls=backend.Disconnects;
            GUI.Commands.Clear();GUI.color=new(.2f,.3f,.4f,.5f);GUI.contentColor=new(.6f,.7f,.8f,.9f);GUI.depth=41;GUI.enabled=false;
            view.Draw(policy,policy.Peers(),policy.Blocked);
            var commands=JsonSerializer.SerializeToElement(GUI.Commands);
            var texts=commands.EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text").ToArray();
            check(texts.Any(c=>c.GetProperty("text").GetString()=="HOST CONTROLS / F8"),"Real host view renders the configured F8 header for "+mode+".");
            check(texts.All(c=>c.GetProperty("x").GetSingle()>=0&&c.GetProperty("y").GetSingle()>=0&&
                c.GetProperty("x").GetSingle()+c.GetProperty("width").GetSingle()<=Screen.width+1&&
                c.GetProperty("y").GetSingle()+c.GetProperty("height").GetSingle()<=Screen.height+1),
                "Real host view stays in bounds at supported resolutions for "+mode+".");
            check(GUI.color.a==.5f&&GUI.contentColor.a==.9f&&GUI.depth==41&&!GUI.enabled,"Host drawing restores all shared GUI state.");
            check(backend.Disconnects==calls&&!view.KickRequested&&!view.ConfirmRequested,"Drawing the host panel cannot disconnect or request a kick.");
            if(mode is "peers" or "blocked")
                check(texts.Where(c=>c.GetProperty("text").GetString()=="Kick + block rejoin").Select(c=>c.GetProperty("enabled").GetBoolean()).SequenceEqual(new[]{true,false}),
                    "Unknown socket identity keeps its kick button disabled; verified guest remains selectable.");
            if(mode=="pending") check(texts.Any(c=>c.GetProperty("text").GetString()=="Confirm kick + block rejoin"),"Actual renderer exposes the explicit confirmation step.");
            if(mode=="blocked") check(texts.Any(c=>c.GetProperty("text").GetString()=="Allow rejoin"),"Actual renderer keeps unblock controls accessible.");
        }
    }
    private sealed class Backend:IHostModerationBackend
    {
        internal HostPeer[] Peers=Array.Empty<HostPeer>();internal int Disconnects;
        public HostScope ReadScope()=>new(1,77,10000,0);
        public IReadOnlyList<HostPeer> ReadPeers()=>Peers;
        public void Disconnect(HostScope scope,ulong client,ulong account,string reason)=>Disconnects++;
    }
}
