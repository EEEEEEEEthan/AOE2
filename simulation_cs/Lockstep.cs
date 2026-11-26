namespace AoeSim;

/// <summary>
/// 回合时钟：只记录玩家数与每回合 tick 数，并让服务器按固定 tick 节奏推进回合号。
/// 服务器自己收集各玩家输入（见 <see cref="SimulationServer"/>），不等待全部玩家提交；
/// 缺输入的玩家按空输入前进，杜绝「文档说锁步、实现不等待」的双模型歧义。
/// </summary>
public sealed class Lockstep
{
    public int PlayerCount { get; }
    public int TicksPerTurn { get; }
    public long Turn { get; private set; }

    public Lockstep(int playerCount = 2, int ticksPerTurn = 6)
    {
        PlayerCount = System.Math.Max(1, playerCount);
        TicksPerTurn = System.Math.Max(1, ticksPerTurn);
    }

    /// <summary>到回合边界直接递增回合号，不依赖客户端提交。</summary>
    public void AdvanceTurn() => Turn += 1;
}
