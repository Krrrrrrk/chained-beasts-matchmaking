namespace ChainedBeasts.Matchmaking.Core;

/// <summary>Ground-plane cursor targeting, independent of scene colliders and the radial gesture.</summary>
public static class PingGround
{
    public static bool Intersect(PingPosition origin, PingPosition direction, PingPosition normal,
        float planeDistance, out PingPosition point)
    {
        point = default;
        if (!origin.Valid || !direction.Valid || !normal.Valid || !float.IsFinite(planeDistance)) return false;
        var normalLength = (double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z;
        var directionLength = (double)direction.X * direction.X + (double)direction.Y * direction.Y + (double)direction.Z * direction.Z;
        if (normalLength < 1e-12 || directionLength < 1e-12) return false;
        var denominator = (double)normal.X * direction.X + (double)normal.Y * direction.Y + (double)normal.Z * direction.Z;
        if (Math.Abs(denominator) <= 1e-6 * Math.Sqrt(normalLength * directionLength)) return false;
        var t = -((double)normal.X * origin.X + (double)normal.Y * origin.Y + (double)normal.Z * origin.Z + planeDistance) / denominator;
        if (!double.IsFinite(t) || t <= 0 || t * Math.Sqrt(directionLength) > 500) return false;
        var candidate = new PingPosition((float)(origin.X + t * direction.X), (float)(origin.Y + t * direction.Y), (float)(origin.Z + t * direction.Z));
        if (!candidate.Valid) return false;
        point = candidate;
        return true;
    }
}
