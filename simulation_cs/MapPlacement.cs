namespace AoeSim;

/// <summary>
/// 地图放置项：出生点、资源点、建筑或单位。只作地图元数据随 MapData 存读，
/// 运行期按需取用；与 TerrainGrid / NavGrid 分开，避免影响高度与占位。
/// 各字段按 kind 取用：spawn/building/unit 用 Team，resource 用 NodeType/Subtype/Amount，
/// building 用 BuildingType，unit 用 UnitType，animal 用 Subtype 物种。
/// </summary>
public sealed class MapPlacement
{
    public const string KindSpawn = "spawn";
    public const string KindResource = "resource";
    public const string KindBuilding = "building";
    public const string KindUnit = "unit";
    public const string KindAnimal = "animal";

    public string Kind { get; set; } = KindSpawn;
    public int X { get; set; }
    public int Y { get; set; }
    public int Team { get; set; }
    public int NodeType { get; set; }
    public int Subtype { get; set; }
    public int Amount { get; set; }
    public int BuildingType { get; set; }
    public int UnitType { get; set; }

    public MapPlacement Clone() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        Team = Team,
        NodeType = NodeType,
        Subtype = Subtype,
        Amount = Amount,
        BuildingType = BuildingType,
        UnitType = UnitType,
    };
}
