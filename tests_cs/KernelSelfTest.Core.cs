using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>内核基础语义：定点数、随机数、ECS 与校验和。</summary>
public static partial class KernelSelfTest
{
    private static void CheckFixed(List<string> failures)
    {
        Expect(failures, Fixed.ToInt(Fixed.FromInt(7)) == 7, "fixed int roundtrip");
        Expect(failures, Fixed.ToFloat(Fixed.FromFloat(1.5)) == 1.5, "fixed float roundtrip");
        Expect(failures, Fixed.Mul(Fixed.FromFloat(1.5), Fixed.FromFloat(2.0)) == Fixed.FromFloat(3.0), "fixed multiply");
        Expect(failures, Fixed.Div(Fixed.FromInt(1), Fixed.FromInt(2)) == Fixed.Half, "fixed divide");
        Expect(failures, Fixed.Sqrt(Fixed.FromInt(4)) == Fixed.FromInt(2), "fixed sqrt");
        var point = FixedVec2.FromTiles(3.0, 4.0);
        Expect(failures, point.DistanceSq(new FixedVec2(0, 0)) == Fixed.FromInt(25), "pythagorean distance");
        Expect(failures, point.CellX() == 3 && point.CellY() == 4, "fixed cell index");
    }

    private static void CheckRng(List<string> failures)
    {
        var first = new SimRng(12345);
        var second = new SimRng(12345);
        bool same = true;
        for (int draw = 0; draw < 64; draw++)
        {
            same &= first.NextU32() == second.NextU32();
        }
        Expect(failures, same, "rng same seed identical");
        var source = new SimRng(42);
        for (int draw = 0; draw < 10; draw++)
        {
            source.NextU32();
        }
        SimRng clone = source.Clone();
        bool cloneMatches = true;
        for (int draw = 0; draw < 20; draw++)
        {
            cloneMatches &= source.NextU32() == clone.NextU32();
        }
        Expect(failures, cloneMatches, "rng clone continues stream");
        var ranged = new SimRng(7);
        bool inRange = true;
        for (int draw = 0; draw < 500; draw++)
        {
            long value = ranged.NextIntRange(3, 9);
            inRange &= value >= 3 && value <= 9;
        }
        Expect(failures, inRange, "rng range respected");
    }

    private static void CheckEcs(List<string> failures)
    {
        var world = new SimWorld(1);
        long a = world.Spawn();
        long b = world.Spawn();
        Expect(failures, a != b, "ecs unique ids");
        world.SetValue(a, SimComponents.Position, SimComponents.X, Fixed.FromInt(5));
        Expect(failures, world.GetValue(a, SimComponents.Position, SimComponents.X) == Fixed.FromInt(5), "ecs set get");
        world.Attach(a, SimComponents.Health);
        Expect(failures, world.HasComponent(a, SimComponents.Health) && !world.HasComponent(b, SimComponents.Health), "ecs component isolation");
        world.Despawn(b);
        Expect(failures, !world.IsAlive(b) && !world.HasComponent(b, SimComponents.Position), "ecs despawn clears");
        Expect(failures, SampleChecksum() == SampleChecksum(), "ecs checksum deterministic");
        Expect(failures, SampleChecksum() != SampleChecksumWithExtra(), "ecs checksum tracks state");
    }

    private static long SampleChecksumWithExtra()
    {
        var world = new SimWorld(5);
        for (int index = 0; index < 12; index++)
        {
            long id = world.Spawn();
            world.Attach(id, SimComponents.Position);
            world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.FromInt(index));
            world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.FromInt(index * 2));
        }
        world.Tick = 3;
        world.SetValue(4, SimComponents.Position, SimComponents.X, Fixed.FromInt(400));
        return world.Checksum();
    }

    private static long SampleChecksum()
    {
        var world = new SimWorld(5);
        for (int index = 0; index < 12; index++)
        {
            long id = world.Spawn();
            world.Attach(id, SimComponents.Position);
            world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.FromInt(index));
            world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.FromInt(index * 2));
        }
        world.Tick = 3;
        return world.Checksum();
    }
}
