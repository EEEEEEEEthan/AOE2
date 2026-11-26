namespace AoeSim;

public static class SimComponents
{
    public const string Position = "position";
    public const string Velocity = "velocity";
    public const string Move = "move";
    public const string Health = "health";
    public const string Combat = "combat";
    public const string AttackTarget = "attack_target";
    public const string Team = "team";
    public const string Gather = "gather";
    public const string Resource = "resource";
    public const string ResourceNode = "resource_node";
    public const string DropSite = "drop_site";

    public const string X = "x";
    public const string Y = "y";
    public const string TargetX = "target_x";
    public const string TargetY = "target_y";
    public const string Speed = "speed";
    public const string Active = "active";
    public const string Hp = "hp";
    public const string MaxHp = "max_hp";
    public const string Damage = "damage";
    public const string RangeSq = "range_sq";
    public const string Cooldown = "cooldown";
    public const string CooldownLeft = "cooldown_left";
    public const string TeamId = "team_id";
    public const string TargetEntity = "target_entity";

    public const string Wood = "wood";
    public const string Food = "food";
    public const string Gold = "gold";
    public const string Stone = "stone";
    public static readonly string[] ResourceColumns = { Wood, Food, Gold, Stone };
    public const string NodeType = "node_type";
    public const string Remaining = "remaining";
    public const string AcceptMask = "accept_mask";
    public const string GatherState = "gather_state";
    public const string NodeEntity = "node_entity";
    public const string DropEntity = "drop_entity";
    public const string Rate = "rate";
    public const string CarryType = "carry_type";
    public const string CarryAmount = "carry_amount";
    public const string CarryCapacity = "carry_capacity";
    public const string Progress = "progress";
}
