namespace AoeSim;

/// <summary>
/// 客户端输入提前量：服务器只在当前回合起 +<see cref="MaxLookaheadTurns"/> 的窗口内接受未来帧，
/// 客户端必须按最近快照回合加一个覆盖网络往返的提前量提交，否则高延迟下帧到达时回合已过被整帧丢弃。
/// 提前量的换算集中在这里，便于按延迟注入与测试。
/// </summary>
public static class InputTiming
{
    /// <summary>与 <see cref="SimulationServer"/> 的未来帧接受窗口保持一致的上限。</summary>
    public const int MaxLookaheadTurns = 8;

    /// <summary>
    /// 默认提前量：覆盖常见的公网单向延迟并留出余量，同时保持在服务器接受窗口内。
    /// </summary>
    public const int DefaultLookaheadTurns = 4;

    /// <summary>把单向延迟（毫秒）换算成需提前的回合数：按往返向上取整，并夹在服务器窗口内。</summary>
    public static int LookaheadForLatency(int oneWayMilliseconds, int ticksPerTurn, int ticksPerSecond = 30)
    {
        int ticksPerTurnMs = ticksPerTurn * 1000 / System.Math.Max(1, ticksPerSecond);
        int roundTripMs = System.Math.Max(0, oneWayMilliseconds) * 2;
        int turns = (roundTripMs + System.Math.Max(1, ticksPerTurnMs) - 1) / System.Math.Max(1, ticksPerTurnMs);
        return System.Math.Clamp(turns, 1, MaxLookaheadTurns);
    }

    /// <summary>按最近快照回合计算提交回合；提前量钳制在服务器接受窗口内。</summary>
    public static long PlanTurn(long latestSnapshotTurn, int lookaheadTurns = DefaultLookaheadTurns)
    {
        int lookahead = System.Math.Clamp(lookaheadTurns, 1, MaxLookaheadTurns);
        return latestSnapshotTurn + lookahead;
    }
}
