using System.Text;
using ChainedBeasts.Matchmaking.Core;

static class ChatChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var request=new ChatPacket(ChatMessage.Request,"Hello 👋",3,1,0,1,5,17);
        foreach(var packet in new[]{request,request with{Message=ChatMessage.Show,Author=7,Nonce=0,Name="Sam"},
            new(ChatMessage.Hello,Sequence:1,Nonce:3),new(ChatMessage.Welcome,Epoch:3,Sequence:1,Nonce:3)})
        {
            var bytes=ChatProtocol.Encode(packet);
            check(ChatProtocol.Decode(bytes,out var decoded)&&decoded==packet,"Chat handshake/data round-trip uses bounded UTF-8.");
        }
        var good=ChatProtocol.Encode(request);
        foreach(var offset in new[]{0,1,2,3,4,5,6,7,56,57,58,59,60,61,62,63})
        {var bad=(byte[])good.Clone();bad[offset]=255;check(!ChatProtocol.Decode(bad,out _),"Reject wrong chat headers, lengths and reserved bits.");}
        var utf8=(byte[])good.Clone();utf8[64]=255;
        check(!ChatProtocol.Decode(utf8,out _),"Reject invalid UTF-8 instead of replacing remote text.");
        check(!ChatProtocol.Decode(good[..^1],out _)&&!ChatProtocol.Decode(good.Concat(new byte[]{0}).ToArray(),out _),"Reject truncated and trailing chat bytes.");
        foreach(var packet in new[]{request with{Author=8},request with{Name="Fake host"},request with{Nonce=0},request with{Epoch=0},
            request with{PlayerIndex=8},request with{Created=double.NaN},request with{Text=""},request with{Text="<b>hello</b>"},
            request with{Text="bad\nline"},request with{Text=new string('a',201)}})
            check(!ChatProtocol.Valid(packet),"Reject noncanonical, oversized or spoofable request metadata.");
        check(ChatText.Clean("  hello\n<b>world</b>\t\u202E! ")=="hello bworld/b !","Chat text removes control/bidi/markup delimiters and collapses whitespace.");
        var max=string.Concat(Enumerable.Repeat("😀",200));var encoded=ChatProtocol.Encode(request with{Text=max});
        check(encoded.Length<=ChatProtocol.MaximumBytes&&ChatProtocol.Decode(encoded,out var maxRead)&&maxRead.Text==max,"Two hundred non-BMP characters remain within the fixed packet cap.");
        check(ChatProtocol.Remaining(request,5)==10&&ChatProtocol.Remaining(request,16)==0&&ChatProtocol.Remaining(request,1)==0,"Expired/future chat deliveries are rejected.");
        var draft=new ChatDraft();foreach(var c in "hi 😀")draft.Input(c);draft.Backspace();
        check(draft.Text=="hi ","Backspace removes an entire surrogate pair without corrupting the draft.");
        draft.Input('\uD800');draft.Input('x');check(draft.Text=="hi x","Orphan surrogate input cannot poison the draft.");
        foreach(var c in new string('W',500))draft.Input(c);
        check(draft.Text.EnumerateRunes().Count()==200,"Draft input remains bounded before encoding.");
        draft.Clear();foreach(var c in "<hi>\n\u202E")draft.Input(c);check(draft.Text=="hi","Draft accepts ordinary text while ignoring unsafe controls.");
        var history=new ChatHistory();for(ulong i=1;i<=55;i++)history.Add(request with{Message=ChatMessage.Show,Sequence=i,Nonce=0,Name="Sam"});
        check(history.Messages.Count==50&&history.Messages[0].Sequence==6,"Chat history has a fixed FIFO capacity.");
        history.Clear();check(history.Messages.Count==0,"Leaving a session can clear all chat history.");
        var random=new Random(73);for(var i=0;i<100;i++)
        {var bytes=new byte[random.Next(0,ChatProtocol.MaximumBytes+100)];random.NextBytes(bytes);check(!ChatProtocol.Decode(bytes,out _),"Arbitrary bounded bytes cannot become valid chat.");}
    }
}
