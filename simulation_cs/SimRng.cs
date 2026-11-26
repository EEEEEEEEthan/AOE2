namespace AoeSim;

public sealed class SimRng
{
    public const long Mask32 = 0xFFFF_FFFFL;

    public long State { get; set; }

    public SimRng(long seed = 1)
    {
        seed &= Mask32;
        State = seed != 0 ? seed : 0x9E37_79B9;
    }

    public long NextU32()
    {
        long x = State;
        x ^= (x << 13) & Mask32;
        x ^= x >> 17;
        x ^= (x << 5) & Mask32;
        State = x & Mask32;
        return State;
    }

    public long NextIntRange(long low, long high)
    {
        if (high <= low)
        {
            return low;
        }
        return low + NextU32() % (high - low + 1);
    }

    public bool NextBool() => (NextU32() & 1) == 1;

    public long NextFixed() => NextU32() >> (32 - Fixed.FractionBits);

    public long NextFixedRange(long low, long high)
    {
        if (high <= low)
        {
            return low;
        }
        return low + NextU32() % (high - low + 1);
    }

    public double NextFloat() => (double)NextU32() / Mask32;

    public SimRng Clone()
    {
        var other = new SimRng(1) { State = State };
        return other;
    }
}
