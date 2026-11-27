using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>锁步回合时钟、输入前瞻与指令编解码语义。</summary>
public static partial class KernelSelfTest
{
    private static void CheckLockstep(List<string> failures)
    {
        var lockstep = new Lockstep(2, 6);
        Expect(failures,
            lockstep.Turn == 0 && lockstep.PlayerCount == 2 && lockstep.TicksPerTurn == 6,
            "lockstep exposes the fixed turn clock");
        lockstep.AdvanceTurn();
        Expect(failures, lockstep.Turn == 1, "lockstep advances without waiting for submissions");

        Expect(failures, InputTiming.LookaheadForLatency(300, 6) == 3, "300ms one way maps to three turns");
        Expect(failures, InputTiming.LookaheadForLatency(0, 6) == 1, "zero latency still looks one turn ahead");
        Expect(failures,
            InputTiming.LookaheadForLatency(5000, 6) == InputTiming.MaxLookaheadTurns,
            "lookahead clamps to the server window");
        Expect(failures,
            InputTiming.PlanTurn(7) == 7 + InputTiming.DefaultLookaheadTurns,
            "planned turn offsets the snapshot turn by the lookahead");

        long now = 0;
        var tracker = new RttTracker(() => now);
        Expect(failures,
            tracker.LookaheadFor(6, 30) == InputTiming.DefaultLookaheadTurns,
            "no RTT sample falls back to the default lookahead");
        tracker.Record(20);
        Expect(failures, tracker.LookaheadFor(6, 30) == 1,
            "low RTT keeps lookahead at one turn instead of over-advancing");
        var laggyTracker = new RttTracker(() => now);
        laggyTracker.Record(600);
        Expect(failures, laggyTracker.LookaheadFor(6, 30) > tracker.LookaheadFor(6, 30),
            "high RTT raises the lookahead above the low-latency case");
        Expect(failures,
            laggyTracker.LookaheadFor(6, 30) <= InputTiming.MaxLookaheadTurns,
            "RTT-derived lookahead stays inside the server window");
        var smoothingTracker = new RttTracker(() => now);
        smoothingTracker.Record(1000);
        smoothingTracker.Record(0);
        Expect(failures,
            smoothingTracker.SmoothedRttMilliseconds > 0
            && smoothingTracker.SmoothedRttMilliseconds < 1000,
            "RTT samples are smoothed instead of replaced");
        now += RttTracker.SampleTimeoutMilliseconds + 1;
        Expect(failures,
            laggyTracker.LookaheadFor(6, 30) == InputTiming.DefaultLookaheadTurns,
            "expired RTT sample falls back to the default lookahead");

        var original = new LockstepCommand(1, LockstepCommand.KindGather, 12, 0, 0, 30, 31);
        LockstepCommand restored = LockstepCommand.FromArray(original.ToArray());
        Expect(failures,
            restored.Player == 1 && restored.Kind == LockstepCommand.KindGather && restored.Entity == 12
            && restored.TargetEntity == 30 && restored.DropEntity == 31,
            "lockstep command roundtrip");

        var attackCommand = new LockstepCommand(1, LockstepCommand.KindAttack, 5, 0, 0, 99, -1);
        LockstepCommand restoredAttack = LockstepCommand.FromArray(attackCommand.ToArray());
        Expect(failures,
            restoredAttack.Kind == LockstepCommand.KindAttack && restoredAttack.Entity == 5
            && restoredAttack.TargetEntity == 99,
            "lockstep attack command roundtrip");

        var stanceCommand = new LockstepCommand(0, LockstepCommand.KindStance, 4, 0, 0, -1, -1, CombatStance.StandGround);
        LockstepCommand restoredStance = LockstepCommand.FromArray(stanceCommand.ToArray());
        Expect(failures,
            restoredStance.Kind == LockstepCommand.KindStance && restoredStance.Entity == 4
            && restoredStance.Stance == CombatStance.StandGround,
            "lockstep stance command roundtrip");

        Expect(failures,
            new LockstepCommand(0, LockstepCommand.KindHeal, 3, 0, 0, 8, -1).ToArray().Length == LockstepCommand.FieldCount,
            "command serialized with fixed width");
        Expect(failures, LockstepCommand.KindHeal == 5 && LockstepCommand.KindConvert == 6
            && LockstepCommand.KindPickupRelic == 7 && LockstepCommand.KindDepositRelic == 8,
            "monk command kinds stable");
        Expect(failures, LockstepCommand.KindStance == 9, "stance command kind avoids reserved ids");
        Expect(failures, LockstepCommand.KindAttackMove == 10 && LockstepCommand.KindPatrol == 11
            && LockstepCommand.KindGuard == 12 && LockstepCommand.KindFollow == 13
            && LockstepCommand.KindHold == 14 && LockstepCommand.KindSetRally == 15,
            "order command kinds stable after stance");

        var patrol = new LockstepCommand(
            0, LockstepCommand.KindPatrol, 6, 2, 3, -1, -1,
            stance: -1, cell2X: 8, cell2Y: 9, aux: 0, queue: 1);
        LockstepCommand restoredPatrol = LockstepCommand.FromArray(patrol.ToArray());
        Expect(failures,
            restoredPatrol.Kind == LockstepCommand.KindPatrol && restoredPatrol.Entity == 6
            && restoredPatrol.CellX == 2 && restoredPatrol.CellY == 3
            && restoredPatrol.Cell2X == 8 && restoredPatrol.Cell2Y == 9 && restoredPatrol.Queue == 1,
            "patrol command roundtrip keeps second endpoint");

        var frame = new CommandFrame { Player = 0, Turn = 3, Commands = new[] { patrol } };
        LockstepCommand decodedPatrol = CommandFrame.Decode(frame.Encode()).Commands[0];
        Expect(failures,
            decodedPatrol.Cell2X == 8 && decodedPatrol.Cell2Y == 9
            && decodedPatrol.Aux == 0 && decodedPatrol.Queue == 1,
            "command frame encodes patrol endpoints and queue flag");
    }
}
