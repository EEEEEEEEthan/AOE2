using System.Collections.Generic;
using System.Threading;
using AoeSim;

namespace AOE2Framework;

/// <summary>DedicatedServer 多连接集成自测：每个连接拿到自己的 observer team 与本队视野快照。</summary>
public static class ServerSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        var sim = new GameSim(new NavGrid(24, 24), 909, 2, 6);
        long blue = sim.SpawnUnit(4, 4, 0, 50, 0, 0.0, 0.0, 0.0);
        long red = sim.SpawnUnit(19, 19, 1, 50, 0, 0.0, 0.0, 0.0);

        var server = new DedicatedServer(0, sim, 60);
        var first = new RemoteClient();
        var second = new RemoteClient();
        server.Start();
        try
        {
            first.Connect("127.0.0.1", server.Port);
            second.Connect("127.0.0.1", server.Port);

            Expect(failures,
                first.ViewerTeam != second.ViewerTeam
                && System.Math.Min(first.ViewerTeam, second.ViewerTeam) == 0
                && System.Math.Max(first.ViewerTeam, second.ViewerTeam) == 1,
                "handshake assigns distinct observer teams 0 and 1");

            bool received = WaitForSnapshots(first, second, 3000);
            Expect(failures, received, "both connections receive snapshots");
            if (received)
            {
                CheckViewer(failures, first, blue, red);
                CheckViewer(failures, second, red, blue);
                Expect(failures,
                    !SameBytes(first.State.Latest!.VisionStates, second.State.Latest!.VisionStates),
                    "each connection carries its own vision bitmap");
            }
        }
        catch (System.Exception error)
        {
            failures.Add($"dedicated server integration threw: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            first.Disconnect();
            second.Disconnect();
            server.Stop();
        }
        return failures;
    }

    private static void CheckViewer(
        List<string> failures, RemoteClient client, long ownUnit, long enemyUnit)
    {
        SnapshotFrame? latest = client.State.Latest;
        if (latest == null)
        {
            failures.Add($"team {client.ViewerTeam} has no snapshot");
            return;
        }
        bool hasOwn = false;
        bool hasEnemy = false;
        foreach (EntitySnapshot entity in latest.Entities)
        {
            hasOwn |= entity.Id == ownUnit;
            hasEnemy |= entity.Id == enemyUnit;
        }
        Expect(failures, hasOwn, $"team {client.ViewerTeam} snapshot keeps its own unit");
        Expect(failures, !hasEnemy, $"team {client.ViewerTeam} snapshot hides the other team");
    }

    private static bool WaitForSnapshots(RemoteClient first, RemoteClient second, int timeoutMilliseconds)
    {
        long deadline = System.Environment.TickCount64 + timeoutMilliseconds;
        while (System.Environment.TickCount64 < deadline)
        {
            first.Poll();
            second.Poll();
            if (first.State.Latest != null && second.State.Latest != null
                && first.State.Latest.Tick > 0 && second.State.Latest.Tick > 0)
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return first.State.Latest != null && second.State.Latest != null;
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
