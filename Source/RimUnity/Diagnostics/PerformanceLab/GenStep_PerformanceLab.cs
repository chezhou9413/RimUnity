using Verse;

namespace RimUnity.Diagnostics;

//空白棋盘地面提供尺度参照，不生成殖民者、动物、矿物或建筑。
public sealed class GenStep_PerformanceLab : GenStep
{
    public override int SeedPart => 94131015;

    //地面保持平坦，测试密度由控制面板决定。
    public override void Generate(Map map, GenStepParams parms)
    {
        var light = DefDatabase<TerrainDef>.GetNamed("Concrete");
        var dark = DefDatabase<TerrainDef>.GetNamed("PavedTile");
        foreach (IntVec3 cell in map.AllCells)
            map.terrainGrid.SetTerrain(cell, ((cell.x / 8 + cell.z / 8) & 1) == 0 ? light : dark);
        map.fogGrid.ClearAllFog();
        MapGenerator.PlayerStartSpot = map.Center;
    }
}
