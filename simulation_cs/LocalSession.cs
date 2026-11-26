namespace AoeSim;

/// <summary>本机服务器：客户端只经快照字节解码后拿到状态，绝不直接读服务器模拟对象。</summary>
public sealed class LocalSession
{
    public SimulationServer Server { get; }
    public SimulationClient Client { get; }

    public LocalSession(GameSim sim)
    {
        Server = new SimulationServer(sim);
        Client = new SimulationClient();
    }

    public void Submit(CommandFrame frame) => Server.Enqueue(frame);

    public long Step()
    {
        long checksum = Server.Step();
        Client.ApplySnapshot(Server.Snapshot().Encode());
        return checksum;
    }
}
