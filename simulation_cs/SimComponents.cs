#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
#endif

namespace AoeSim;

public static class SimComponents
{
    public const string Position = "position";
    public const string Velocity = "velocity";
    public const string Move = "move";
    public const string Domain = "domain";
    public const string Health = "health";
    public const string Combat = "combat";
    public const string AttackTarget = "attack_target";
    public const string Engage = "engage";
    public const string Team = "team";
    public const string Gather = "gather";
    public const string Footprint = "footprint";
    public const string Projectile = "projectile";
    public const string Resource = "resource";
    public const string ResourceNode = "resource_node";
    public const string HuntingAnimal = "hunting_animal";
    public const string Corpse = "corpse";
    public const string DropSite = "drop_site";
    public const string Match = "match";
    public const string PlayerState = "player_state";
    public const string PlayerProgress = "player_progress";
    public const string King = "king";
    public const string Building = "building";
    public const string GateState = "gate_state";
    public const string Construct = "construct";
    public const string Repair = "repair";
    public const string Wonder = "wonder";
    public const string Relic = "relic";
    public const string Monk = "monk";
    public const string RelicSlot = "relic_slot";
    public const string Order = "order";
    public const string Rally = "rally";
    public const string Garrison = "garrison";
    public const string Garrisoned = "garrisoned";
    public const string Transport = "transport";
    public const string Aboard = "aboard";
    public const string MarketState = "market_state";
    public const string Trade = "trade";

    public const string X = "x";
    public const string Y = "y";
    public const string DomainId = "domain_id";
    public const string TargetX = "target_x";
    public const string TargetY = "target_y";
    public const string Speed = "speed";
    public const string Active = "active";
    public const string Hp = "hp";
    public const string MaxHp = "max_hp";
    public const string Damage = "damage";
    public const string UnitType = "unit_type";
    public const string AttackClass = "attack_class";
    public const string ArmorClass = "armor_class";
    public const string IsRanged = "is_ranged";
    public const string ArmorMelee = "armor_melee";
    public const string ArmorPierce = "armor_pierce";
    public const string ArmorSiege = "armor_siege";
    public const string RangeSq = "range_sq";
    public const string RangeMin = "range_min";
    public const string RangeMax = "range_max";
    public const string Cooldown = "cooldown";
    public const string CooldownLeft = "cooldown_left";
    public const string TeamId = "team_id";
    public const string TargetEntity = "target_entity";
    public const string Stance = "stance";
    public const string AutoTarget = "auto_target";
    public const string OriginX = "origin_x";
    public const string OriginY = "origin_y";
    public const string FootprintWidth = "width";
    public const string FootprintHeight = "height";
    public const string BuildingType = "b_type";
    public const string SizeWidth = "size_w";
    public const string SizeHeight = "size_h";
    public const string MaxProgress = "max_progress";
    public const string TargetBase = "target_base";
    public const string ProjectileTarget = "projectile_target";
    public const string ProjectileDamage = "projectile_damage";
    public const string ProjectileTicksLeft = "projectile_ticks_left";

    public const string Wood = "wood";
    public const string Food = "food";
    public const string Gold = "gold";
    public const string Stone = "stone";
    public static readonly string[] ResourceColumns = { Wood, Food, Gold, Stone };
    public const string NodeType = "node_type";
    public const string Subtype = "subtype";
    public const string Remaining = "remaining";
    public const string Species = "species";
    public const string Meat = "meat";
    public const string RotTicks = "rot_ticks";
    public const string AcceptMask = "accept_mask";
    public const string GatherState = "gather_state";
    public const string NodeEntity = "node_entity";
    public const string DropEntity = "drop_entity";
    public const string Rate = "rate";
    public const string CarryType = "carry_type";
    public const string CarryAmount = "carry_amount";
    public const string CarryCapacity = "carry_capacity";
    public const string Progress = "progress";

    public const string Phase = "phase";
    public const string WinnerTeam = "winner_team";
    public const string VictoryReason = "victory_reason";
    public const string EndTick = "end_tick";
    public const string RuleMask = "rule_mask";
    public const string WonderHoldTicks = "wonder_hold_ticks";
    public const string RelicHoldTicks = "relic_hold_ticks";
    public const string ScoreTickLimit = "score_tick_limit";
    public const string Defeated = "defeated";
    public const string Winner = "winner";
    public const string Score = "score";
    public const string Participants = "participants";
    public const string WonderHold = "wonder_hold";
    public const string RelicHold = "relic_hold";
    public const string HadKing = "had_king";
    public const string Surrendered = "surrendered";
    public const string Complete = "complete";
    public const string HolderTeam = "holder_team";

    public const string Age = "age";
    public const string Research = "research";
    public const string Civ = "civ";
    public const string ResearchKind = "research_kind";
    public const string ResearchTarget = "research_target";
    public const string ResearchProgress = "research_progress";
    public const string ResearchMaxProgress = "research_max_progress";
    public const string ResearchPaidFood = "research_paid_food";
    public const string ResearchPaidGold = "research_paid_gold";
    public const string ResearchBuilding = "research_building";

    public const string State = "state";
    public const string ConvertProgress = "convert_progress";
    public const string ConvertRate = "convert_rate";
    public const string ConvertRequired = "convert_required";
    public const string HealRate = "heal_rate";
    public const string ConversionResistance = "conversion_resistance";
    public const string LastHp = "last_hp";
    public const string Carrier = "carrier";
    public const string StoredIn = "stored_in";
    public const string SpawnX = "spawn_x";
    public const string SpawnY = "spawn_y";
    public const string GoldRate = "gold_rate";
    public const string GoldPeriod = "gold_period";
    public const string GoldProgress = "gold_progress";

    public const string OrderType = "order_type";
    public const string OrderState = "order_state";
    public const string Aux = "aux";
    public const string AuxX = "aux_x";
    public const string AuxY = "aux_y";
    public const string QueueCount = "queue_count";
    public const string RallyX = "rally_x";
    public const string RallyY = "rally_y";
    public const string RallyTarget = "rally_target";

    public const string Count = "count";
    public const string Capacity = "capacity";
    public const string BonusDamage = "bonus_damage";
    public const string BaseDamage = "base_damage";
    public const string BaseCooldown = "base_cooldown";
    public const string TownBell = "town_bell";
    public const string Contribution = "contribution";
    public const string HadGather = "had_gather";

    public static readonly string[] RateColumns = { "rate_wood", "rate_food", "rate_gold", "rate_stone" };
    public const string TradeHome = "trade_home";
    public const string TradeTarget = "trade_target";

    /// <summary>待执行指令队列固定深度：每槽存 type/target/x/y/aux_x/aux_y 六列。</summary>
    public const int QueueDepth = 3;

    public static readonly string[] QueueTypeColumns = { "q0_type", "q1_type", "q2_type" };
    public static readonly string[] QueueTargetColumns = { "q0_target", "q1_target", "q2_target" };
    public static readonly string[] QueueXColumns = { "q0_x", "q1_x", "q2_x" };
    public static readonly string[] QueueYColumns = { "q0_y", "q1_y", "q2_y" };
    public static readonly string[] QueueAuxXColumns = { "q0_aux_x", "q1_aux_x", "q2_aux_x" };
    public static readonly string[] QueueAuxYColumns = { "q0_aux_y", "q1_aux_y", "q2_aux_y" };
    public static readonly string[] QueueAuxColumns = { "q0_aux", "q1_aux", "q2_aux" };

    public const string Population = "population";
    public const string Pop = "pop";
    public const string Used = "used";
    public const string Cap = "cap";
    public const string CapMax = "cap_max";
    public const string PopCost = "pop_cost";

    public const string Production = "production";
    public const string ProdCurrent = "prod_current";
    public const string ProdTicksLeft = "prod_ticks_left";
    public const string ProdTicksTotal = "prod_ticks_total";
    public const string ProdQueueCount = "prod_queue_count";
    public const string ProdRepeat = "prod_repeat";
    public const string ProdRepeatType = "prod_repeat_type";
    public static readonly string[] ProdQueueColumns = { "prod_q0", "prod_q1", "prod_q2", "prod_q3", "prod_q4" };

#if DEBUG
    /// <summary>
    /// 本类声明的全部组件名与列名。列按需惰性创建，缺失列不能直接判为拼错，
    /// 因此调试期只在名称从未声明时抛错，兼顾「未知列立即报错」与「未写入列读 0」。
    /// </summary>
    internal static readonly HashSet<string> KnownNames = CollectKnownNames();

    private static HashSet<string> CollectKnownNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldInfo field in typeof(SimComponents).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is string name)
            {
                names.Add(name);
            }
            else if (field.GetValue(null) is string[] columns)
            {
                names.UnionWith(columns);
            }
        }
        return names;
    }
#endif
}
