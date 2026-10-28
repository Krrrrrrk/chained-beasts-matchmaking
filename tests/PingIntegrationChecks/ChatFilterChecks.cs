using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using Crosstales.BWF;

// These substitutes exercise call routing/readiness/failure behavior, NOT the game's proprietary word lists.
namespace Crosstales.BWF
{
    public sealed class BWFManager { public static BWFManager instance = new(); public bool isReady = true; }
}
public static class PlayerNameFilter
{
    public static bool Throw;
    public static int Calls;
    public static string Override;
    public static string Sanitize(string text)
    {
        Calls++;
        if (Throw) throw new InvalidOperationException("must-not-log-original-" + text);
        if (BWFManager.instance == null || !BWFManager.instance.isReady) return text; // Actual native fallback behavior.
        return Override ?? text.Replace("badword", "*******", StringComparison.OrdinalIgnoreCase);
    }
}
static class ChatFilterChecks
{
    internal static void Run(Action<bool,string> check)
    {
        InertBus.Reset(); var node=InertBus.Add(0,true,false);node.Activate();PlayerNameFilter.Override=null;
        var filter=new ChatProfanity();
        check(filter.TryText("hello badword 👋",200,out var text)&&text=="hello ******* 👋","Uses the native string sanitizer while preserving ordinary Unicode text.");
        check(PlayerNameFilter.Calls==1,"Profanity filtering calls the generated game binding rather than a copied dictionary.");
        var packet=new ChatPacket(ChatMessage.Show,"badword",1,1,7,0,1,0,"badword gamer");
        check(filter.TryPacket(packet,out var safe)&&safe.Text=="*******"&&safe.Name=="******* gamer"&&safe.Author==7&&safe.PlayerIndex==0,
            "Filtering covers both message/name and preserves authenticated metadata.");
        BWFManager.instance.isReady=false;var calls=PlayerNameFilter.Calls;
        check(!filter.Ready&&!filter.TryText("badword",200,out _)&&PlayerNameFilter.Calls==calls,
            "Not-ready filter never calls the native pass-through fallback or exposes raw chat.");
        BWFManager.instance=null;
        check(!filter.TryText("badword",200,out _)&&BWFManager.instance==null,"Missing native filter stays missing; chat does not create/initialize a service.");
        BWFManager.instance=new(){isReady=true};
        check(filter.TryText("ready again",200,out text)&&text=="ready again","A normally loading filter recovers automatically when ready.");
        PlayerNameFilter.Override=new string('W',1000);
        check(filter.TryText("input",200,out text)&&text.Length==200,"Native output is re-bounded to the chat protocol's limit.");
        check(filter.TryText("name",32,out text)&&text.Length==32,"Native name output stays within its separate limit.");
        PlayerNameFilter.Override="";
        check(filter.TryText("input",200,out text)&&text=="***","A completely removed result becomes a safe visible placeholder.");
        PlayerNameFilter.Override=null;PlayerNameFilter.Throw=true;
        check(!filter.TryText("secret-sentinel",200,out _)&&!filter.Ready,"A native sanitizer exception blocks text rather than falling back to the original.");
        calls=PlayerNameFilter.Calls;filter.TryText("secret-sentinel",200,out _);
        check(PlayerNameFilter.Calls==calls&&node.Log.Messages.Count(x=>x.Contains("profanity filter unavailable"))==1,
            "Failed filter is contained with one warning instead of per-message exception spam.");
        check(node.Log.Messages.All(x=>!x.Contains("secret-sentinel")&&!x.Contains("must-not-log-original")),"Filter failure logs contain no original chat/name or exception message.");
        PlayerNameFilter.Throw=false;filter.Reset();
        check(filter.TryText("badword",200,out text)&&text=="*******","A relay/session reset can retry a failed native filter.");
    }
}
