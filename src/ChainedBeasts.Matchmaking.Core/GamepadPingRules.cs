namespace ChainedBeasts.Matchmaking.Core;

public enum PingPadButton { DpadUp, DpadDown, DpadLeft, DpadRight }

public static class GamepadPingRules
{
    public const double HoldSeconds = .22;
    public const float Distance = 6, StickDeadZone = .35f;
    public static string ButtonLabel(PingPadButton button) => button switch
    {
        PingPadButton.DpadUp => "D-pad Up", PingPadButton.DpadDown => "D-pad Down",
        PingPadButton.DpadLeft => "D-pad Left", PingPadButton.DpadRight => "D-pad Right", _ => "Unbound"
    };

    public static bool Ahead(PingPosition player, PingPosition direction, PingPosition normal,
        float planeDistance, out PingPosition point)
    {
        point = default;
        if (!player.Valid || !direction.Valid) return false;
        var length = Math.Sqrt((double)direction.X * direction.X + (double)direction.Z * direction.Z);
        if (length < 1e-6) return false;
        return PingGround.Intersect(new(player.X + (float)(direction.X / length * Distance), player.Y + 10,
                player.Z + (float)(direction.Z / length * Distance)), new(0, -1, 0), normal, planeDistance, out point) &&
            PingProtocol.InRange(point, player);
    }
}
