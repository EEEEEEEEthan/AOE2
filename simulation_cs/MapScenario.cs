using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>由地图放置项推导出的对局布局：起始位置、资源点与野生动物。</summary>
public sealed class MapScenario
{
    public List<ResourcePoint> Resources { get; } = new();
    public List<PlayerStart> Starts { get; } = new();
    public List<AnimalSpawn> Animals { get; } = new();
}

/// <summary>
/// 把编辑器保存的放置列表翻译成对局消费的布局数据，让同一份地图既能编辑也能开局。
/// 出生点按城镇中心占地居中换算成左上角格；资源与动物沿用放置项自身的类型/子类型。
/// 推导只用整数、按放置顺序生成，保证同一份地图每次开局一致。
/// </summary>
public static class MapScenarioBuilder
{
    public const int DefaultResourceAmount = 300;

    public static MapScenario FromPlacements(
        IReadOnlyList<MapPlacement> placements, TerrainGrid terrain)
    {
        var scenario = new MapScenario();
        foreach (MapPlacement placement in placements)
        {
            switch (placement.Kind)
            {
                case MapPlacement.KindSpawn:
                    scenario.Starts.Add(CreateStart(placement, terrain));
                    break;
                case MapPlacement.KindResource:
                    scenario.Resources.Add(new ResourcePoint(
                        placement.NodeType,
                        placement.X,
                        placement.Y,
                        placement.Amount > 0 ? placement.Amount : DefaultResourceAmount,
                        placement.Subtype));
                    break;
                case MapPlacement.KindAnimal:
                    if (AnimalSpecies.IsValid(placement.Subtype))
                    {
                        scenario.Animals.Add(new AnimalSpawn(
                            placement.Subtype, placement.X, placement.Y));
                    }
                    break;
            }
        }
        return scenario;
    }

    private static PlayerStart CreateStart(MapPlacement placement, TerrainGrid terrain)
    {
        int width = BuildingTypes.SizeWidth(BuildingTypes.TownCenter);
        int height = BuildingTypes.SizeHeight(BuildingTypes.TownCenter);
        int cellX = PlacementMath.CenterToTopLeft(placement.X, width, terrain.Width);
        int cellY = PlacementMath.CenterToTopLeft(placement.Y, height, terrain.Height);
        return new PlayerStart(placement.Team, cellX, cellY);
    }
}

internal static class PlacementMath
{
    /// <summary>把放置点所在格换算成建筑占地的左上角格，并夹在地图内。</summary>
    public static int CenterToTopLeft(int center, int size, int mapSize)
    {
        int topLeft = center - size / 2;
        int maxTopLeft = Math.Max(0, mapSize - size);
        return Math.Clamp(topLeft, 0, maxTopLeft);
    }
}
