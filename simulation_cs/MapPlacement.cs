namespace AoeSim;

/// <summary>
/// 地图放置项：出生点、资源点或建筑。只作地图元数据随 MapData 存读，
/// 运行期按需取用；与 TerrainGrid / NavGrid 分开，避免影响高度与占位。
/// </summary>
public sealed class MapPlacement
{
    public const string KindSpawn = "spawn";
    public const string KindResource = "resource";
    public const string KindBuilding = "building";

    public string Kind { get; set; } = KindSpawn;
    public int X { get; set; }
    public int Y { get; set; }
    public int Team { get; set; }
    public int NodeType { get; set; }
    public int Amount { get; set; }

    public MapPlacement Clone() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        Team = Team,
        NodeType = NodeType,
        Amount = Amount,
    };
}
