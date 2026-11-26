using System;
using System.Collections.Generic;
using System.Diagnostics;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>热路径整型列访问：解析一次、循环内直取，并对比字符串键开销。</summary>
public static partial class KernelSelfTest
{
    private const int BenchmarkIterations = 1_000_000;

    private static void CheckColumnBinding(List<string> failures)
    {
        var world = new SimWorld(1);
        long id = world.Spawn();
        world.Attach(id, SimComponents.Position);
        world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.FromInt(5));
        world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.FromInt(7));

        ComponentAccess position = world.Access(SimComponents.Position);
        int xColumn = position.Column(SimComponents.X);
        int yColumn = position.Column(SimComponents.Y);
        Expect(failures, xColumn >= 0 && yColumn >= 0, "known columns resolve to ids");
        Expect(failures, position.Get(id, xColumn) == Fixed.FromInt(5), "int access reads x column");
        Expect(failures, position.Get(id, yColumn) == Fixed.FromInt(7), "int access reads y column");
        Expect(failures, position.Get(id, xColumn) == world.GetValue(id, SimComponents.Position, SimComponents.X), "int access matches string access");
        int unsetColumn = position.Column(SimComponents.Meat);
        Expect(failures, unsetColumn < 0, "declared but unwritten column resolves to -1");
        Expect(failures, position.Get(id, unsetColumn) == 0, "unwritten column int read returns 0");
#if DEBUG
        bool getThrew = false;
        try
        {
            world.GetValue(id, SimComponents.Position, "no_such_column");
        }
        catch (InvalidOperationException)
        {
            getThrew = true;
        }
        Expect(failures, getThrew, "unknown column string read throws in debug");
#endif

        long stringValue = 0;
        long intValue = 0;
        for (int index = 0; index < 10_000; index++)
        {
            stringValue += world.GetValue(id, SimComponents.Position, SimComponents.X);
            intValue += position.Get(id, xColumn);
        }
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < BenchmarkIterations; index++)
        {
            stringValue += world.GetValue(id, SimComponents.Position, SimComponents.X);
        }
        watch.Stop();
        long stringTicks = watch.ElapsedTicks;
        watch.Restart();
        for (int index = 0; index < BenchmarkIterations; index++)
        {
            intValue += position.Get(id, xColumn);
        }
        watch.Stop();
        long intTicks = watch.ElapsedTicks;
        Expect(failures, intValue == stringValue, "benchmark reads same values");
        Expect(failures, intTicks <= stringTicks, "int column access no slower than string");
    }
}
