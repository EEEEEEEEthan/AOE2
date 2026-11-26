using System.Collections.Generic;

namespace AoeSim;

public sealed class Lockstep
{
    public int PlayerCount { get; }
    public int TicksPerTurn { get; }
    public long Turn { get; private set; }

    private readonly Dictionary<long, Dictionary<long, List<LockstepCommand>>> _turns = new();

    public Lockstep(int playerCount = 2, int ticksPerTurn = 6)
    {
        PlayerCount = System.Math.Max(1, playerCount);
        TicksPerTurn = System.Math.Max(1, ticksPerTurn);
    }

    public bool SubmitTurn(long turnIndex, long player, List<LockstepCommand> commands)
    {
        if (player < 0 || player >= PlayerCount || turnIndex < Turn)
        {
            return false;
        }
        if (!_turns.TryGetValue(turnIndex, out Dictionary<long, List<LockstepCommand>>? slots))
        {
            slots = new Dictionary<long, List<LockstepCommand>>();
            _turns[turnIndex] = slots;
        }
        if (slots.ContainsKey(player))
        {
            return false;
        }
        slots[player] = new List<LockstepCommand>(commands);
        return true;
    }

    public int ReadyPlayers(long turnIndex) =>
        _turns.TryGetValue(turnIndex, out Dictionary<long, List<LockstepCommand>>? slots) ? slots.Count : 0;

    public bool CanAdvance() => ReadyPlayers(Turn) == PlayerCount;

    public List<LockstepCommand> TakeCommands()
    {
        var flattened = new List<LockstepCommand>();
        if (!_turns.TryGetValue(Turn, out Dictionary<long, List<LockstepCommand>>? slots))
        {
            return flattened;
        }
        var players = new List<long>(slots.Keys);
        players.Sort();
        foreach (long player in players)
        {
            flattened.AddRange(slots[player]);
        }
        _turns.Remove(Turn);
        Turn += 1;
        return flattened;
    }

    public int PendingTurns() => _turns.Count;
}
