using ChainedBeasts.Matchmaking.Core;

static class PingGroundChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var up = new PingPosition(0, 1, 0);
        foreach(var direction in new[]{new PingPosition(0,0,1),new(1,0,0),new(-1,0,0),new(0,0,-1),new(3,9,4)})
        {
            check(GamepadPingRules.Ahead(new(10,0,20),direction,up,0,out var target) &&
                Math.Abs(target.DistanceSquared(new(10,0,20))-36)<.0001 && target.Y==0,
                "Controller target stays six ground units away regardless of stick/facing magnitude.");
        }
        foreach(var direction in new[]{default(PingPosition),new PingPosition(0,1,0),new(float.NaN,0,1),new(0,0,float.PositiveInfinity)})
            check(!GamepadPingRules.Ahead(default,direction,up,0,out _),"Invalid/vertical controller facing cannot manufacture a location.");
        check(!GamepadPingRules.Ahead(default,new(1,0,0),default,0,out _),"Controller aim rejects an uninitialized ground plane.");
        foreach(var binding in Enum.GetValues<PingPadButton>())
            check(GamepadPingRules.ButtonLabel(binding).StartsWith("D-pad "),"Configurable controller directions have readable prompts.");
        check(PingGround.Intersect(new(2, 10, 4), new(0, -1, 0), up, 0, out var flat) && flat == new PingPosition(2, 0, 4),
            "A top-down cursor ray lands on the ground plane, not an elevated prop.");
        check(PingGround.Intersect(new(2, 10, 4), new(0, -1, 1), up, 0, out var oblique) && oblique == new PingPosition(2, 0, 14),
            "An oblique camera ray reaches the map point beyond the camera-facing surface.");
        check(PingGround.Intersect(new(2, 10, 4), new(0, -2, 2), up, 0, out var scaled) && scaled == oblique,
            "Ray direction scaling does not change the ground intersection.");
        check(PingGround.Intersect(new(2, 10, 4), new(0, -1, 0), new(0, 2, 0), -6, out var elevated) && elevated.Y == 3,
            "Reads the native plane's normal and distance instead of assuming a collider or hardcoded height.");
        foreach (var direction in new[] { new PingPosition(1, 0, 0), new(1, -1e-8f, 0), new(0, 1, 0), default,
            new(float.NaN, -1, 0), new(0, float.PositiveInfinity, 0) })
            check(!PingGround.Intersect(new(2, 10, 4), direction, up, 0, out var rejected) && rejected == default,
                "Parallel, behind-camera, zero, and invalid rays do not produce a location.");
        check(!PingGround.Intersect(new(0, 501, 0), new(0, -1, 0), up, 0, out _), "Ground beyond the ray budget is rejected.");
        check(!PingGround.Intersect(new(0, 10, 0), new(0, -1, 0), default, 0, out _), "Uninitialized native plane is rejected.");
        check(!PingGround.Intersect(new(0, 10, 0), new(0, -1, 0), up, float.NaN, out _), "Invalid native plane distance is rejected.");
        check(!PingGround.Intersect(new(float.NaN, 10, 0), new(0, -1, 0), up, 0, out _), "Invalid camera origin is rejected.");
        check(PingGround.Intersect(new(0, 10, 0), new(0, -1, 1), up, 0, out var one) &&
            PingGround.Intersect(new(0, 20, -10), new(0, -1, 1), up, 0, out var two) && one == two,
            "Different camera positions aimed at the same map point produce identical world coordinates.");
        var packet = new PingPacket(PingMessage.Show, PingKind.Location, 2, 1, 4, 5, 0, 0, oblique, 1);
        check(PingProtocol.Decode(PingProtocol.Encode(packet), out var received) && received.Position == oblique,
            "Ground coordinates survive the existing wire format unchanged.");
    }
}
