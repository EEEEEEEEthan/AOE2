using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>
/// 把原 GDScript 用例的语义断言移植到 C# 内核；返回空列表表示全过。
/// 按系统拆分到 KernelSelfTest.*.cs 各 partial 分片，这里只保留聚合入口。
/// </summary>
public static partial class KernelSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckFixed(failures);
        CheckRng(failures);
        CheckEcs(failures);
        CheckPathfinding(failures);
        CheckFlowFieldCache(failures);
        CheckCombat(failures);
        CheckUnitTypes(failures);
        CheckOccupancy(failures);
        CheckGridRange(failures);
        CheckProjectile(failures);
        CheckAttack(failures);
        CheckStance(failures);
        CheckGather(failures);
        CheckGarrison(failures);
        CheckMonk(failures);
        CheckRelic(failures);
        CheckLockstep(failures);
        CheckVision(failures);
        CheckTech(failures);
        CheckTerrain(failures);
        return failures;
    }
}
