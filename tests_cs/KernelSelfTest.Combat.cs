using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>战斗语义：伤害、兵种克制、弹道、攻击指令与姿态。</summary>
public static partial class KernelSelfTest
{
    private static void CheckCombat(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(12, 12), 21);
        long attacker = sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0);
        long victim = sim.SpawnUnit(6, 5, 1, 30, 10, 1.0, 1.0, 0.0);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "combat enemy dies");

        var friendly = new GameSim(new NavGrid(12, 12), 22);
        friendly.SpawnUnit(5, 5, 0, 40, 10, 1.0, 1.0, 0.0);
        friendly.SpawnUnit(6, 5, 0, 40, 10, 1.0, 1.0, 0.0);
        for (int tick = 0; tick < 40; tick++)
        {
            friendly.SimulateTick();
        }
        int aliveCount = 0;
        foreach (long id in friendly.World.Entities())
        {
            if (friendly.World.IsAlive(id))
            {
                aliveCount += 1;
            }
        }
        Expect(failures, aliveCount == 2, "combat friendly fire disabled");
    }

    private static void CheckUnitTypes(List<string> failures)
    {
        var armored = new GameSim(new NavGrid(12, 12), 401);
        long armoredAttacker = armored.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long armoredTarget = armored.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(armored, armoredAttacker, SimComponents.Damage, 10);
        SetStat(armored, armoredAttacker, SimComponents.AttackClass, UnitAttackClass.Melee);
        SetStat(armored, armoredTarget, SimComponents.ArmorMelee, 3);
        long armoredFull = armored.World.GetValue(armoredTarget, SimComponents.Health, SimComponents.MaxHp);
        armored.IssueAttack(armoredAttacker, armoredTarget);
        armored.SimulateTick();
        Expect(failures,
            armored.World.GetValue(armoredTarget, SimComponents.Health, SimComponents.Hp) == armoredFull - 7,
            "armor reduces matching attack class");

        var piercing = new GameSim(new NavGrid(12, 12), 402);
        long piercer = piercing.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long drilled = piercing.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(piercing, piercer, SimComponents.Damage, 10);
        SetStat(piercing, piercer, SimComponents.AttackClass, UnitAttackClass.Pierce);
        SetStat(piercing, drilled, SimComponents.ArmorPierce, 4);
        SetStat(piercing, drilled, SimComponents.ArmorMelee, 0);
        long drilledFull = piercing.World.GetValue(drilled, SimComponents.Health, SimComponents.MaxHp);
        piercing.IssueAttack(piercer, drilled);
        piercing.SimulateTick();
        Expect(failures,
            piercing.World.GetValue(drilled, SimComponents.Health, SimComponents.Hp) == drilledFull - 6,
            "only the matching armor column applies");

        var minimum = new GameSim(new NavGrid(12, 12), 403);
        long weak = minimum.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long wall = minimum.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(minimum, weak, SimComponents.Damage, 2);
        SetStat(minimum, weak, SimComponents.AttackClass, UnitAttackClass.Melee);
        SetStat(minimum, wall, SimComponents.ArmorMelee, 50);
        long wallFull = minimum.World.GetValue(wall, SimComponents.Health, SimComponents.MaxHp);
        minimum.IssueAttack(weak, wall);
        minimum.SimulateTick();
        Expect(failures,
            minimum.World.GetValue(wall, SimComponents.Health, SimComponents.Hp) == wallFull - 1,
            "damage never drops below one");

        var counter = new GameSim(new NavGrid(12, 12), 404);
        long spearman = counter.SpawnUnit(5, 5, 0, UnitDefs.Spearman, CombatStance.NoAttack);
        long knight = counter.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        counter.IssueAttack(spearman, knight);
        counter.SimulateTick();
        long spearDamage = 100 - counter.World.GetValue(knight, SimComponents.Health, SimComponents.Hp);
        Expect(failures, spearDamage == 19, "spearman bonus vs cavalry");

        var plain = new GameSim(new NavGrid(12, 12), 405);
        long swordsman = plain.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long plainKnight = plain.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        plain.IssueAttack(swordsman, plainKnight);
        plain.SimulateTick();
        long swordDamage = 100 - plain.World.GetValue(plainKnight, SimComponents.Health, SimComponents.Hp);
        Expect(failures, spearDamage > swordDamage, "counter unit outdamages plain unit");

        var archerSim = new GameSim(new NavGrid(16, 16), 406);
        long archer = archerSim.SpawnUnit(5, 5, 0, UnitDefs.Archer, CombatStance.NoAttack);
        long prey = archerSim.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        Expect(failures, archerSim.World.GetValue(archer, SimComponents.Combat, SimComponents.IsRanged) == 1, "archer marked ranged");
        archerSim.IssueAttack(archer, prey);
        archerSim.SimulateTick();
        Expect(failures,
            archerSim.World.GetValue(prey, SimComponents.Health, SimComponents.Hp) == 100,
            "ranged damage travels as projectile");
        archerSim.SimulateTick();
        Expect(failures,
            archerSim.World.GetValue(prey, SimComponents.Health, SimComponents.Hp) < 100,
            "projectile lands on later tick");

        Expect(failures, UnitTypeChecksum() == UnitTypeChecksum(), "unit types deterministic");
        Expect(failures, UnitTypeChecksum() != 0, "unit type checksum populated");
    }

    private static void CheckSiege(List<string> failures)
    {
        var vsBuilding = new GameSim(new NavGrid(16, 16), 408);
        long ram = vsBuilding.SpawnUnit(5, 5, 0, UnitDefs.BatteringRam, CombatStance.NoAttack);
        Expect(failures, ram >= 0, "battering ram is available to every civilization");
        long hall = vsBuilding.SpawnBuilding(6, 5, 1, 1000);
        vsBuilding.IssueAttack(ram, hall);
        vsBuilding.SimulateTick();
        long buildingDamage = 1000 - vsBuilding.World.GetValue(hall, SimComponents.Health, SimComponents.Hp);
        Expect(failures, buildingDamage == 95, "battering ram applies siege bonus to buildings");

        var vsUnit = new GameSim(new NavGrid(16, 16), 409);
        long ramUnit = vsUnit.SpawnUnit(5, 5, 0, UnitDefs.BatteringRam, CombatStance.NoAttack);
        long soldier = vsUnit.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        long soldierFull = vsUnit.World.GetValue(soldier, SimComponents.Health, SimComponents.Hp);
        vsUnit.IssueAttack(ramUnit, soldier);
        vsUnit.SimulateTick();
        long unitDamage = soldierFull - vsUnit.World.GetValue(soldier, SimComponents.Health, SimComponents.Hp);
        Expect(failures, unitDamage == 5, "battering ram keeps low damage against units");
        Expect(failures, unitDamage < buildingDamage, "siege bonus only applies to buildings");
    }

    private static void SetStat(GameSim sim, long id, string column, long value) =>
        sim.World.SetValue(id, SimComponents.Combat, column, value);

    private static long UnitTypeChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 407, 2, 6);
        for (long type = 0; type < 4; type++)
        {
            sim.SpawnUnit(3 + (int)type, 4, 0, type);
            sim.SpawnUnit(3 + (int)type, 16, 1, type);
        }
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static void CheckProjectile(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(32, 32), 65);
        sim.SpawnUnit(2, 5, 0, 100, 10, 1.0, 6.0, 0.0);
        long victim = sim.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        sim.SimulateTick();
        sim.SimulateTick();
        bool aliveBeforeImpact = sim.World.IsAlive(victim);
        sim.SimulateTick();
        bool deadAfterImpact = !sim.World.IsAlive(victim);
        Expect(failures, aliveBeforeImpact, "projectile damage delayed by travel ticks");
        Expect(failures, deadAfterImpact, "projectile applies damage on arrival");
    }

    private static void CheckAttack(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 51);
        long attacker = sim.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5);
        long victim = sim.SpawnUnit(12, 2, 1, 30, 0, 1.0, 1.0, 0.0);
        sim.IssueAttack(attacker, victim);
        Expect(failures, sim.World.HasComponent(attacker, SimComponents.AttackTarget), "attack locks explicit target");
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "attack chases and kills explicit target");

        var friendly = new GameSim(new NavGrid(12, 12), 52);
        long ally = friendly.SpawnUnit(4, 4, 0, 60, 5, 1.0, 1.0, 0.0);
        long friend = friendly.SpawnUnit(5, 4, 0, 60, 5, 1.0, 1.0, 0.0);
        friendly.IssueAttack(ally, friend);
        Expect(failures, !friendly.World.HasComponent(ally, SimComponents.AttackTarget), "attack ignores friendly target");

        var priority = new GameSim(new NavGrid(12, 12), 53);
        long hunter = priority.SpawnUnit(5, 5, 0, 100, 10, 1.0, 2.0, 0.0);
        long near = priority.SpawnUnit(5, 6, 1, 100, 0, 1.0, 1.0, 0.0);
        long far = priority.SpawnUnit(6, 6, 1, 100, 0, 1.0, 1.0, 0.0);
        priority.IssueAttack(hunter, far);
        priority.SimulateTick();
        priority.SimulateTick();
        long farHp = priority.World.GetValue(far, SimComponents.Health, SimComponents.Hp);
        long nearHp = priority.World.GetValue(near, SimComponents.Health, SimComponents.Hp);
        Expect(failures, farHp < 100 && nearHp == 100, "explicit attack beats nearest auto target");

        var release = new GameSim(new NavGrid(20, 20), 54);
        long chaser = release.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5);
        long doomed = release.SpawnUnit(10, 2, 1, 30, 0, 1.0, 1.0, 0.0);
        release.IssueAttack(chaser, doomed);
        release.World.Despawn(doomed);
        release.SimulateTick();
        Expect(failures, !release.World.HasComponent(chaser, SimComponents.AttackTarget), "attack clears lock when target gone");

        Expect(failures, AttackDeterminismChecksum() == AttackDeterminismChecksum(), "attack lockstep deterministic");
        Expect(failures, AttackDeterminismChecksum() != 0, "attack checksum populated");
    }

    private static long AttackDeterminismChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 61, 2, 6);
        long attacker = sim.SpawnUnit(3, 3, 0, 60, 8, 1.0, 1.0, 0.15);
        long victim = sim.SpawnUnit(12, 3, 1, 60, 8, 1.0, 1.0, 0.0);
        sim.IssueAttack(attacker, victim);
        long checksum = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
    }

    private static void CheckStance(List<string> failures)
    {
        var silent = new GameSim(new NavGrid(12, 12), 71);
        silent.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        long bystander = silent.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            silent.SimulateTick();
        }
        Expect(failures,
            silent.World.GetValue(bystander, SimComponents.Health, SimComponents.Hp) == 100,
            "no attack stance does not auto fire");

        var standGround = new GameSim(new NavGrid(16, 16), 72);
        long guard = standGround.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.StandGround);
        standGround.SpawnUnit(9, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            standGround.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(standGround.World.GetValue(guard, SimComponents.Position, SimComponents.X)) == 2
            && Fixed.ToInt(standGround.World.GetValue(guard, SimComponents.Position, SimComponents.Y)) == 2,
            "stand ground never chases");

        var standFire = new GameSim(new NavGrid(16, 16), 73);
        long sentry = standFire.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long intruder = standFire.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 10; tick++)
        {
            standFire.SimulateTick();
        }
        Expect(failures,
            standFire.World.GetValue(intruder, SimComponents.Health, SimComponents.Hp) < 100,
            "stand ground fires at in-range target");
        Expect(failures,
            standFire.World.GetValue(sentry, SimComponents.Move, SimComponents.Active) == 0,
            "stand ground does not move to engage");

        var aggressive = new GameSim(new NavGrid(24, 24), 74);
        long hunter = aggressive.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Aggressive);
        aggressive.SpawnUnit(8, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            aggressive.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(aggressive.World.GetValue(hunter, SimComponents.Position, SimComponents.X)) > 2,
            "aggressive stance chases into larger aggro radius");

        var defensive = new GameSim(new NavGrid(24, 24), 75);
        long defender = defensive.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Defensive);
        defensive.SpawnUnit(8, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            defensive.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(defensive.World.GetValue(defender, SimComponents.Position, SimComponents.X)) == 2,
            "defensive stance does not chase beyond its short aggro radius");

        var marching = new GameSim(new NavGrid(20, 20), 76);
        long marcher = marching.SpawnUnit(2, 5, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Aggressive);
        long roadblock = marching.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        marching.IssueMove(marcher, 15, 5);
        for (int tick = 0; tick < 40; tick++)
        {
            marching.SimulateTick();
        }
        Expect(failures,
            marching.World.GetValue(roadblock, SimComponents.Health, SimComponents.Hp) == 100,
            "moving unit does not auto engage");

        var ordered = new GameSim(new NavGrid(12, 12), 77);
        long commanded = ordered.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        long victim = ordered.SpawnUnit(6, 5, 1, 30, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        ordered.IssueAttack(commanded, victim);
        ordered.SimulateTick();
        Expect(failures,
            ordered.World.GetValue(victim, SimComponents.Health, SimComponents.Hp) == 20,
            "explicit attack overrides no attack stance");

        var setter = new GameSim(new NavGrid(12, 12), 78);
        long trooper = setter.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0);
        Expect(failures,
            setter.World.GetValue(trooper, SimComponents.Combat, SimComponents.Stance) == CombatStance.Aggressive,
            "unit spawns with default stance");
        setter.ApplyCommand(new LockstepCommand(0, LockstepCommand.KindStance, trooper, 0, 0, -1, -1, CombatStance.StandGround));
        Expect(failures,
            setter.World.GetValue(trooper, SimComponents.Combat, SimComponents.Stance) == CombatStance.StandGround,
            "stance command updates stance");

        Expect(failures,
            StanceChecksum(CombatStance.Aggressive) != StanceChecksum(CombatStance.NoAttack),
            "stance participates in checksum");
        Expect(failures,
            StanceChecksum(CombatStance.Defensive) == StanceChecksum(CombatStance.Defensive),
            "stance checksum deterministic");
    }

    private static long StanceChecksum(long stance)
    {
        var sim = new GameSim(new NavGrid(12, 12), 79);
        sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, stance);
        sim.SpawnUnit(6, 5, 1, 100, 10, 1.0, 1.0, 0.0, stance);
        return sim.World.Checksum();
    }
}
