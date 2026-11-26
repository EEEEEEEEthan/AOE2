using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>自测共用底座：断言与跨用例的校验辅助，供各 *SelfTest 复用。</summary>
public static class SelfTestBase
{
    public static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    public static void AdvanceTicks(GameSim sim, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            sim.SimulateTick();
        }
    }

    public static bool ContainsEntity(SnapshotFrame frame, long id)
    {
        foreach (EntitySnapshot entity in frame.Entities)
        {
            if (entity.Id == id)
            {
                return true;
            }
        }
        return false;
    }

    public static bool SameStates(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }
}
