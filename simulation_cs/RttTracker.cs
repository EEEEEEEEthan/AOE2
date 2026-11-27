namespace AoeSim;

/// <summary>
/// 客户端 RTT 采样与提前量换算：把 Ping/Echo 往返毫秒平滑成 EWMA，
/// 取一半作为单向延迟交给 <see cref="InputTiming.LookaheadForLatency"/>；
/// 无有效样本或样本过期时回退 <see cref="InputTiming.DefaultLookaheadTurns"/>。
/// 时钟可注入，便于在无真实网络延迟时测试高延迟分支。
/// </summary>
public sealed class RttTracker
{
    /// <summary>相邻两次 ping 的最小间隔，避免采样抢占上行带宽。</summary>
    public const int PingIntervalMilliseconds = 250;

    /// <summary>EWMA 平滑系数：越大越跟随最新样本，越小越稳。</summary>
    public const double SmoothingFactor = 0.3;

    /// <summary>样本新鲜期；超过则视为断流，提前量回退默认值。</summary>
    public const long SampleTimeoutMilliseconds = 3000;

    private readonly System.Func<long> _clock;
    private double _smoothedRttMilliseconds = -1;
    private long _lastSampleAt = long.MinValue;

    public RttTracker(System.Func<long>? clock = null)
    {
        _clock = clock ?? (static () => System.Environment.TickCount64);
    }

    /// <summary>最近一次采样的单调时钟时刻；从未采样时为 <see cref="long.MinValue"/>。</summary>
    public long LastSampleAt => _lastSampleAt;

    /// <summary>平滑后的往返毫秒；从未采样时为 -1。</summary>
    public double SmoothedRttMilliseconds => _smoothedRttMilliseconds;

    /// <summary>存在未过期的往返样本。</summary>
    public bool HasFreshSample =>
        _smoothedRttMilliseconds >= 0
        && _clock() - _lastSampleAt <= SampleTimeoutMilliseconds;

    /// <summary>记录一次往返毫秒采样：首个样本直接采用，其后按 EWMA 平滑。</summary>
    public void Record(long roundTripMilliseconds)
    {
        long sample = System.Math.Max(0, roundTripMilliseconds);
        _smoothedRttMilliseconds = _smoothedRttMilliseconds < 0
            ? sample
            : _smoothedRttMilliseconds + SmoothingFactor * (sample - _smoothedRttMilliseconds);
        _lastSampleAt = _clock();
    }

    /// <summary>按当前样本换算输入提前量；无新鲜样本时回退默认提前量。</summary>
    public int LookaheadFor(int ticksPerTurn, int ticksPerSecond)
    {
        if (!HasFreshSample)
        {
            return InputTiming.DefaultLookaheadTurns;
        }
        int oneWayMilliseconds = (int)System.Math.Round(_smoothedRttMilliseconds / 2.0);
        return InputTiming.LookaheadForLatency(oneWayMilliseconds, ticksPerTurn, ticksPerSecond);
    }
}
