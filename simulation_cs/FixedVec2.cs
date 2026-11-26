namespace AoeSim;

public struct FixedVec2
{
    public long X;
    public long Y;

    public FixedVec2(long x = 0, long y = 0)
    {
        X = x;
        Y = y;
    }

    public static FixedVec2 FromTiles(double tileX, double tileY) =>
        new(Fixed.FromFloat(tileX), Fixed.FromFloat(tileY));

    public readonly FixedVec2 Add(FixedVec2 other) => new(Fixed.Add(X, other.X), Fixed.Add(Y, other.Y));

    public readonly FixedVec2 Sub(FixedVec2 other) => new(Fixed.Sub(X, other.X), Fixed.Sub(Y, other.Y));

    public readonly FixedVec2 Scaled(long factor) => new(Fixed.Mul(X, factor), Fixed.Mul(Y, factor));

    public readonly long LengthSq() => Fixed.Add(Fixed.Mul(X, X), Fixed.Mul(Y, Y));

    public readonly long DistanceSq(FixedVec2 other)
    {
        long deltaX = Fixed.Sub(X, other.X);
        long deltaY = Fixed.Sub(Y, other.Y);
        return Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY));
    }

    public readonly long CellX() => Fixed.ToInt(X);

    public readonly long CellY() => Fixed.ToInt(Y);
}
