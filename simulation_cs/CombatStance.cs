namespace AoeSim;

/// <summary>原作四档交战姿态：进攻 / 防御 / 驻守 / 禁止还击。作为模拟状态随校验和同步。</summary>
public static class CombatStance
{
    public const long Aggressive = 0;
    public const long Defensive = 1;
    public const long StandGround = 2;
    public const long NoAttack = 3;

    /// <summary>进攻姿态在射程外额外索敌的格数，追击半径最大。</summary>
    public const long AggressiveAcquireTiles = 6;

    /// <summary>防御姿态在射程外额外索敌的格数，只做短距离接战。</summary>
    public const long DefensiveAcquireTiles = 2;

    /// <summary>进攻姿态允许离开接战起点的最大格数。</summary>
    public const long AggressiveLeashTiles = 12;

    /// <summary>防御姿态允许离开接战起点的最大格数，超出即归位。</summary>
    public const long DefensiveLeashTiles = 4;

    public static bool IsValid(long stance) =>
        stance >= Aggressive && stance <= NoAttack;

    /// <summary>是否允许自主索敌；禁止还击档关闭。</summary>
    public static bool AutoAcquires(long stance) => stance != NoAttack;

    /// <summary>射程之外额外索敌的格数。</summary>
    public static long AcquireTiles(long stance) => stance switch
    {
        Aggressive => AggressiveAcquireTiles,
        Defensive => DefensiveAcquireTiles,
        _ => 0,
    };

    /// <summary>接战后允许离开起点的格数；驻守为 0，表示原地不追击。</summary>
    public static long LeashTiles(long stance) => stance switch
    {
        Aggressive => AggressiveLeashTiles,
        Defensive => DefensiveLeashTiles,
        _ => 0,
    };
}
