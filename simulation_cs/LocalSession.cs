namespace AoeSim;

/// <summary>表现层驱动一场对局的统一入口：实时本地局与录像回放都实现这一接口。</summary>
public interface ILocalSession
{
    SimulationServer Server { get; }
    SimulationClient Client { get; }
    void Submit(CommandFrame frame);
    long Step();
}

/// <summary>本机服务器：客户端只经快照字节解码后拿到状态，绝不直接读服务器模拟对象。</summary>
public sealed class LocalSession : ILocalSession
{
    public SimulationServer Server { get; }
    public SimulationClient Client { get; }

    public LocalSession(GameSim sim)
    {
        Server = new SimulationServer(sim);
        Client = new SimulationClient();
        Client.ApplyStaticMap(StaticMapFrame.FromTerrain(sim.Terrain));
    }

    public void Submit(CommandFrame frame) => Server.Enqueue(frame);

    /// <summary>本机局直接传递不可变快照对象，跳过每帧编解码；编码路径只留给网络。</summary>
    public long Step()
    {
        long checksum = Server.Step();
        Client.ApplySnapshot(Server.Snapshot());
        return checksum;
    }
}
