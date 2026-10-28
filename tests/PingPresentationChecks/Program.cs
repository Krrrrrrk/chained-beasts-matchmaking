using System.Text.Json;
using ChainedBeasts.Matchmaking;
using ChainedBeasts.Matchmaking.Core;
using UnityEngine;
var count=0;
void Check(bool value,string message) { if(!value) throw new Exception(message);count++; }
var visual=new PingVisual { Packet=new(PingMessage.Show,PingKind.Location),World=new(4,0,8),Label="P1  Look here!",Until=104 };
foreach(var size in new[]{(1280,720),(1920,1080),(2560,1440)}) {
    Screen.width=size.Item1; Screen.height=size.Item2;
    using var view=new PingView(); var camera=new Camera();
    float firstLabel=0; Vector3[] ground=null;
    foreach(var motion in new[]{0,1,2}) {
        camera.Offset=new(motion*2,0,motion*-1); camera.Zoom=40+motion*7;
        camera.Inputs.Clear();GUI.Commands.Clear();
        GUI.color=new(.3f,.4f,.5f,.6f);GUI.depth=42;GUI.enabled=false;
        view.Draw(new[]{visual},camera,100,false,default,PingKind.None,true,"");
        Check(camera.Inputs.Count==33,"Location projects the map center and a bounded world-space ring.");
        var ring=camera.Inputs.Skip(1).ToArray();
        Check(ring.All(v=>Math.Abs(v.y-.03f)<.0001 && Math.Abs(Math.Sqrt(Math.Pow(v.x-4,2)+Math.Pow(v.z-8,2))-.65)<.001),
            "Ring vertices lie on the arena XZ floor around the original world point.");
        if(ground!=null) Check(ring.Zip(ground).All(v=>v.First.x==v.Second.x && v.First.y==v.Second.y && v.First.z==v.Second.z),
            "Camera pan/zoom does not change ring world coordinates.");
        ground=ring;
        var commands=JsonSerializer.SerializeToElement(GUI.Commands);
        var label=commands.EnumerateArray().Single(c=>c.GetProperty("kind").GetString()=="text" && c.GetProperty("text").GetString()==visual.Label);
        var x=label.GetProperty("x").GetSingle();
        if(motion==0) firstLabel=x; else Check(x!=firstLabel,"Projected label follows the camera transform, not a cached screen coordinate.");
        Check(commands.GetArrayLength()<270,"Ground marker drawing is bounded even at different zooms.");
        Check(GUI.color.r==.3f && GUI.color.a==.6f && GUI.depth==42 && !GUI.enabled,"Drawing restores the caller's GUI state.");
        Check(visual.World.x==4 && visual.World.y==0 && visual.World.z==8,"Drawing never changes the marker's map point.");
    }
    Camera.InvalidProjection=true;GUI.Commands.Clear();camera.Inputs.Clear();
    view.Draw(new[]{visual},camera,100,false,default,PingKind.None,true,"");
    Check(GUI.Commands.Count==0,"Nonfinite projection does not draw an orphan ground marker.");Camera.InvalidProjection=false;
    foreach(var kind in new[]{PingKind.Thanks,PingKind.Sorry}) {
        visual.Packet=new(PingMessage.Show,kind); GUI.Commands.Clear(); camera.Inputs.Clear();
        view.Draw(new[]{visual},camera,100,false,default,PingKind.None,true,"");
        Check(camera.Inputs.Count==1,"Thanks/Sorry keep one above-character anchor and never receive a ground ring.");
    }
    visual.Packet=new(PingMessage.Show,PingKind.Location);
}

using(var view=new PingView()) {
    GUI.Commands.Clear(); Screen.width=1280; Screen.height=720;
    view.Draw(Array.Empty<PingVisual>(),new Camera(),100,true,new(640,360),PingKind.Thanks,true,"Shared through the host",
        "Right stick / release D-pad Up / center cancels");
    var commands=JsonSerializer.SerializeToElement(GUI.Commands);
    var texts=commands.EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text").Select(c=>c.GetProperty("text").GetString()).ToArray();
    Check(texts.Contains("Right stick / release D-pad Up / center cancels"),"Controller wheel displays its actual device controls.");
    Check(!texts.Any(t=>t.Contains("Move mouse")||t.Contains("release G")),"Controller instructions do not misleadingly ask for mouse or keyboard input.");
}

foreach(var size in new[]{(640,360),(1280,720),(1920,1080),(2560,1440)})
foreach(var open in new[]{true,false}) {
    Screen.width=size.Item1;Screen.height=size.Item2;GUI.Commands.Clear();
    var chat=new ChatView();
    var messages=Enumerable.Range(1,50).Select(i=>new ChatPacket(ChatMessage.Show,new string('W',200),1,(ulong)i,0,0,1,0,new string('W',32))).ToArray();
    GUI.color=new(.2f,.3f,.4f,.5f);GUI.depth=99;GUI.enabled=false;
    chat.Draw(messages,open,new string('W',200),"Shared through the host","Enter");
    var commands=JsonSerializer.SerializeToElement(GUI.Commands);
    Check(commands.EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text").All(c=>c.GetProperty("y").GetSingle()>=0 &&
        c.GetProperty("y").GetSingle()+c.GetProperty("height").GetSingle()<=Screen.height),"Chat rows/input remain within the window at supported preview sizes.");
    Check(commands.GetArrayLength()<40,"Chat drawing stays bounded by at most five messages, not the whole 50-message history.");
    Check(GUI.color.a==.5f&&GUI.depth==99&&!GUI.enabled,"Chat restores the caller's GUI state.");
    chat.Page=int.MaxValue;chat.Draw(messages,true,"","Shared through the host","Enter");
    Check(chat.Page is >=9 and <50,"Chat clamps keyboard history pagination to the bounded history, adapting for long messages.");
}
GUI.Commands.Clear();
new ChatView().Draw(Array.Empty<ChatPacket>(),true,"hello","Solo preview - only you can see these messages","T");
var soloText=JsonSerializer.SerializeToElement(GUI.Commands).EnumerateArray()
    .Where(c=>c.GetProperty("kind").GetString()=="text").Select(c=>c.GetProperty("text").GetString()).ToArray();
Check(soloText.Contains("SOLO CHAT PREVIEW")&&soloText.Any(t=>t.Contains("Nothing is sent")),
    "Solo editor explicitly labels local-only preview rather than implying shared delivery.");
foreach(var open in new[]{false,true}) {
    Screen.width=1920;Screen.height=1080;GUI.Commands.Clear();
    var messages=new[]{new ChatPacket(ChatMessage.Show,"Thanks for the help!",1,1,0,0,1,0,"Player 1"),
        new ChatPacket(ChatMessage.Show,"I'll grab the potion.",1,2,7,1,1,0,new string('W',32)),
        new ChatPacket(ChatMessage.Show,"Watch the spikes!",1,3,0,0,1,0,"Player 1"),
        new ChatPacket(ChatMessage.Show,"Thanks!",1,4,7,1,1,0,"Player 2"),
        new ChatPacket(ChatMessage.Show,"wow",1,5,0,0,1,0,"Player 1")};
    var chat=new ChatView();chat.Draw(messages,open,"On my way","Shared through the host","T");
    var drawn=JsonSerializer.SerializeToElement(GUI.Commands);
    var panel=drawn.EnumerateArray().First(c=>c.GetProperty("kind").GetString()=="rect");
    Check(panel.GetProperty("width").GetSingle()<=Screen.width/3f && (open || panel.GetProperty("height").GetSingle()<Screen.height/5f),
        "Short chat occupies a compact corner rather than a large gameplay panel.");
    var alpha=panel.GetProperty("color")[3].GetSingle();
    Check(alpha>.5f&&alpha<.95f,"Chat background is translucent while retaining contrast.");
    Check(drawn.EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text").All(c=>c.GetProperty("color")[3].GetSingle()==1),
        "Only panel fills are translucent; message and control text remain opaque.");
    var name=drawn.EnumerateArray().Single(c=>c.GetProperty("kind").GetString()=="text"&&c.GetProperty("text").GetString().StartsWith("P2/WWW"));
    var label=name.GetProperty("text").GetString();
    Check(label.EndsWith("...:"),"Long display names shorten while preserving the inline colon.");
    var measure=new GUIStyle{fontSize=name.GetProperty("size").GetInt32()};
    Check(measure.CalcSize(new GUIContent{text=label}).x<=name.GetProperty("width").GetSingle(),
        "Rendered author names stay inside their available width.");
    if(args.Length>0) {
        Directory.CreateDirectory(args[0]);
        File.WriteAllText(Path.Combine(args[0],open?"chat-open.json":"chat-feed.json"),
            JsonSerializer.Serialize(new{width=Screen.width,height=Screen.height,mode=open?"Typing":"Message feed",commands=GUI.Commands}));
    }
}
foreach(var size in new[]{(640,360),(1920,1080)})
foreach(var open in new[]{false,true}) {
    Screen.width=size.Item1;Screen.height=size.Item2;GUI.Commands.Clear();
    var chat=new ChatView();
    var messages=Enumerable.Range(1,50).Select(i=>new ChatPacket(ChatMessage.Show,"line "+i,1,(ulong)i,0,0,1,0,"Player 1")).ToArray();
    chat.Draw(messages,open,"","Shared through the host","T");
    var drawn=JsonSerializer.SerializeToElement(GUI.Commands);
    var texts=drawn.EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text").ToArray();
    var authors=texts.Where(c=>c.GetProperty("text").GetString()=="P1/Player 1:").ToArray();
    var bodies=texts.Where(c=>c.GetProperty("text").GetString().StartsWith("line ")).ToArray();
    Check(authors.Length==5&&bodies.Select(c=>c.GetProperty("text").GetString()).SequenceEqual(Enumerable.Range(46,5).Select(i=>"line "+i)),
        "Five newest short messages appear in chronological order using the requested sender format.");
    Check(authors.Zip(bodies).All(p=>p.First.GetProperty("y").GetSingle()==p.Second.GetProperty("y").GetSingle()&&
        p.Second.GetProperty("x").GetSingle()>p.First.GetProperty("x").GetSingle()),
        "Sender and message begin beside each other on the same line.");
    if(!open) Check(drawn.EnumerateArray().First().GetProperty("height").GetSingle()<Screen.height*.25f,
        "Five short inline messages retain the small feed footprint.");
    if(open) {
        var history=new List<string>();
        for(var page=0;page<10;page++) {
            chat.Page=page;GUI.Commands.Clear();chat.Draw(messages,true,"","Shared through the host","T");
            history.AddRange(JsonSerializer.SerializeToElement(GUI.Commands).EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text")
                .Select(c=>c.GetProperty("text").GetString()).Where(t=>t.StartsWith("line ")));
        }
        Check(history.Count==50&&history.Distinct().Count()==50,"Paging exposes every retained short message exactly once.");
        chat.Page=int.MaxValue;chat.Draw(messages,true,"","Shared through the host","T");
        Check(chat.Page==9,"Fifty short messages use ten five-message pages.");
    }
    if(open) {
        GUI.Commands.Clear();
        var full=new[]{new ChatPacket(ChatMessage.Show,new string('W',200),1,1,0,0,1,0,"Player 1")};
        chat.Draw(full,true,"","Shared through the host","T");
        var recovered=string.Concat(JsonSerializer.SerializeToElement(GUI.Commands).EnumerateArray().Where(c=>c.GetProperty("kind").GetString()=="text")
            .Select(c=>c.GetProperty("text").GetString()).Where(t=>t.StartsWith("W"))).Replace("\n","");
        Check(recovered==full[0].Text,"Long inline messages remain complete when reading the open history.");
    }
}
Console.WriteLine($"PASS: {count} source-linked ping presentation checks with inert camera/GUI. No Unity runtime invoked.");
namespace ChainedBeasts.Matchmaking {
    internal static class Plugin { internal static Option PingKey=new(); }
    internal class Option { public string Value=>"G"; }
}
