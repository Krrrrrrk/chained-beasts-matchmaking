using System.Buffers.Binary;
using ChainedBeasts.Matchmaking.Core;

static class PingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var request = new PingPacket(PingMessage.Request, PingKind.Location, 99, 1, 43,
            PingProtocol.NameHash("arena"), 0, 0, new(12, 0, -3), Nonce: 123);
        foreach (var p in new[] { new PingPacket(PingMessage.Hello, Sequence: 1, Nonce: 123), new(PingMessage.Welcome, Epoch: 99, Sequence: 1, Nonce: 123),
            request, request with { Message = PingMessage.Show, Author = 123, Nonce = 0 },
            request with { Kind = PingKind.Thanks }, request with { Kind = PingKind.Sorry } })
        {
            var bytes = PingProtocol.Encode(p);
            check(bytes.Length == PingProtocol.Bytes && PingProtocol.Decode(bytes, out var read) && p == read, "Ping wire round-trip including host ID zero.");
        }
        var good = PingProtocol.Encode(request);
        check(!PingProtocol.Decode(good[..^1], out _), "Reject truncated ping.");
        check(!PingProtocol.Decode(good.Concat(new byte[] { 0 }).ToArray(), out _), "Reject oversized ping.");
        foreach (var offset in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        { var bad = (byte[])good.Clone(); bad[offset] = 255; check(!PingProtocol.Decode(bad, out _), "Reject wrong magic/version/type/action/reserved field."); }
        foreach (var offset in new[] { 56, 60, 64 })
            foreach (var number in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
            {
                var bad = (byte[])good.Clone(); BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(offset), number);
                check(!PingProtocol.Decode(bad, out _), "Reject nonfinite or unbounded coordinate.");
            }
        var forged = (byte[])good.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(40), 123);
        check(!PingProtocol.Decode(forged, out _), "Requests cannot supply an author; the server uses actual NGO sender.");
        var zeroSequence = (byte[])good.Clone(); Array.Clear(zeroSequence, 16, 8);
        check(!PingProtocol.Decode(zeroSequence, out _), "Reject zero sequence.");
        var emptyEpoch = (byte[])good.Clone(); Array.Clear(emptyEpoch, 8, 8);
        check(!PingProtocol.Decode(emptyEpoch, out _), "Reject missing session epoch.");
        var emptyNonce = (byte[])good.Clone(); Array.Clear(emptyNonce, 76, 8);
        check(!PingProtocol.Decode(emptyNonce, out _), "Reject missing negotiated client nonce.");
        foreach (var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            var bad = (byte[])good.Clone(); BinaryPrimitives.WriteDoubleLittleEndian(bad.AsSpan(68), number);
            check(!PingProtocol.Decode(bad, out _), "Reject invalid server-timeline timestamp.");
        }
        check(PingProtocol.Remaining(request, 0) == 4, "A fresh receipt has at most four seconds.");
        check(PingProtocol.Remaining(request, 3.75) == .25, "Transit time is subtracted from lifetime.");
        check(PingProtocol.Remaining(request, 4) == 0, "Expiration boundary drops the marker.");
        check(PingProtocol.Remaining(request with { Created = 2 }, 0) == 0, "Far future timestamp rejected.");
        check(PingProtocol.Remaining(request with { Created = 1 }, 0) == 4, "Small NGO clock skew never extends lifetime beyond four seconds.");
        check(PingProtocol.Remaining(request with { Created = -1 }, 0) == 0, "Negative source time rejected even in lifetime helper.");
        check(PingProtocol.Remaining(request, double.NaN) == 0, "Nonfinite local timeline cannot produce a drawable lifetime.");
        check(PingProtocol.CanRelay(request, 99, 43, request.SceneName, true, default, 0), "Current nearby owned request accepted.");
        check(!PingProtocol.CanRelay(request, 100, 43, request.SceneName, true, default, 0), "Previous session cannot relay.");
        check(!PingProtocol.CanRelay(request, 99, 44, request.SceneName, true, default, 0), "Previous scene object cannot relay.");
        check(!PingProtocol.CanRelay(request, 99, 43, PingProtocol.NameHash("merchant"), true, default, 0), "Reused scene ID requires scene-name match.");
        check(!PingProtocol.CanRelay(request, 99, 43, request.SceneName, false, default, 0), "Unknown or spoofed player cannot relay.");
        check(!PingProtocol.CanRelay(request with { Position = new(101, 0, 0) }, 99, 43, request.SceneName, true, default, 0), "Reject location beyond 100 world units.");
        check(PingProtocol.InRange(new(100, 0, 0), default), "Range boundary accepted.");
        var shown = request with { Message = PingMessage.Show, Author = 17, Nonce = 0 };
        check(PingProtocol.CanShow(shown, true, 99, 43, request.SceneName, true, 0), "Current server relay may display.");
        check(!PingProtocol.CanShow(shown, false, 99, 43, request.SceneName, true, 0), "Client cannot directly spoof a displayed message.");
        check(!PingProtocol.CanShow(shown, true, 100, 43, request.SceneName, true, 0), "Stale session display rejected.");
        check(!PingProtocol.CanShow(shown, true, 99, 44, request.SceneName, true, 0), "Stale scene display rejected.");
        check(!PingProtocol.CanShow(shown, true, 99, 43, request.SceneName, false, 0), "Despawned speaker rejected.");
        var gate = new PingGate();
        check(gate.Accept(0, 1, 0), "Host with NGO ID zero can ping.");
        check(!gate.Accept(0, 1, 10), "Duplicate callback rejected even after cooldown.");
        check(!gate.Accept(0, 2, 1.49), "Burst spam rejected.");
        check(gate.Accept(0, 2, 1.5), "Cooldown boundary allows next ping.");
        check(!gate.Accept(0, 1, 10), "Out-of-order delivery rejected.");
        check(gate.Accept(1, 1, 0), "Different players have independent cooldowns.");
        check(!gate.Accept(1, 2, double.NaN), "Invalid clock rejected.");
        gate.Clear(); check(gate.Accept(0, 1, 0), "Fresh session resets stale sequence/cooldown.");
        gate.Retain(Array.Empty<ulong>()); check(gate.Accept(0, 1, 0), "Departed peer gate removed.");
        for (ulong i = 1; i < 8; i++) gate.Accept(i, 1, 0);
        check(!gate.Accept(8, 1, 0), "Bound total sender state.");
        gate.Clear(); gate.Accept(0, 10, 0); gate.Accept(1, 10, 0); gate.Forget(0);
        check(gate.Accept(0, 1, 0) && !gate.Accept(1, 1, 10), "A restarted sender resets only its own request gate.");
        gate.Clear();
        check(gate.Accept(0, 1, 0, false) && gate.Accept(0, 2, 0, false), "Receiver accepts distinct server events batched into one frame.");
        check(!gate.Accept(0, 2, 10, false), "Disabling arrival cooldown still rejects duplicate server events.");
        check(PingProtocol.Sector(0, 0, 42) == PingKind.None, "Center release cancels.");
        check(PingProtocol.Sector(10, 20, 42) == PingKind.None, "Small mouse jitter cancels.");
        check(PingProtocol.Sector(0, -100, 42) == PingKind.Location, "Up selects location.");
        check(PingProtocol.Sector(100, 65, 42) == PingKind.Thanks, "Lower right selects Thanks.");
        check(PingProtocol.Sector(-100, 65, 42) == PingKind.Sorry, "Lower left selects Sorry.");
        check(PingProtocol.Sector(double.NaN, 0, 42) == PingKind.None, "Invalid pointer cancels.");
        check(PingProtocol.Label((PingKind)200) == "", "Remote action cannot inject arbitrary UI text.");
        // Malformed network input must never throw or deserialize objects.
        var random = new Random(37);
        for (var i = 0; i < 1000; i++) { var bytes = new byte[random.Next(0, 140)]; random.NextBytes(bytes); check(!PingProtocol.Decode(bytes, out _), "Random malformed packet safely ignored."); }
    }
}
