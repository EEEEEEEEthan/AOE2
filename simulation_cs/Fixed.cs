namespace AoeSim;

public static class Fixed
{
    public const int FractionBits = 16;
    public const long Scale = 1L << FractionBits;
    public const long Half = Scale >> 1;

    public static long FromInt(long value) => value << FractionBits;

    public static long FromFloat(double value) =>
        (long)System.Math.Round(value * Scale, System.MidpointRounding.AwayFromZero);

    public static double ToFloat(long value) => (double)value / Scale;

    public static long ToInt(long value) => value >> FractionBits;

    public static long Add(long a, long b) => a + b;

    public static long Sub(long a, long b) => a - b;

    public static long Mul(long a, long b) => (a * b) >> FractionBits;

    public static long MulInt(long a, long factor) => a * factor;

    public static long Div(long a, long b)
    {
        if (b == 0)
        {
            return 0;
        }
        return (a << FractionBits) / b;
    }

    public static long DivInt(long a, long divisor)
    {
        if (divisor == 0)
        {
            return 0;
        }
        return a / divisor;
    }

    public static long Neg(long a) => -a;

    public static long Abs(long a) => a >= 0 ? a : -a;

    public static long Min(long a, long b) => a < b ? a : b;

    public static long Max(long a, long b) => a > b ? a : b;

    public static long Clamp(long value, long low, long high) => Min(Max(value, low), high);

    public static long Lerp(long a, long b, long t) => a + Mul(b - a, t);

    public static long Sign(long a)
    {
        if (a > 0)
        {
            return 1;
        }
        if (a < 0)
        {
            return -1;
        }
        return 0;
    }

    public static long Isqrt(long value)
    {
        if (value < 2)
        {
            return value > 0 ? value : 0;
        }
        long x = value;
        long y = (x + 1) >> 1;
        while (y < x)
        {
            x = y;
            y = (x + value / x) >> 1;
        }
        return x;
    }

    public static long Sqrt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }
        return Isqrt(value << FractionBits);
    }
}
