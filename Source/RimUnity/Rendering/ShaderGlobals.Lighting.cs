using Verse;
using RimWorld;
using HarmonyLib;

namespace RimUnity.Rendering;

internal static partial class ShaderGlobals
{
    //只缓存参数 ID，光照每帧刷新，不跨地图保留对象引用。
    private static readonly int SunDirectionId = Shader.PropertyToID("_RU_MapSunDirection"), MoonDirectionId = Shader.PropertyToID("_RU_MapMoonDirection"),
        SunColorId = Shader.PropertyToID("_RU_MapSunColor"), MoonColorId = Shader.PropertyToID("_RU_MapMoonColor"), AmbientId = Shader.PropertyToID("_RU_MapAmbient");

    // Loading can expose CurrentMap before its world layer and Tiles list are ready.
    // Do not use Map.Biome/TileInfo until the actual collection bounds are validated.
    private static readonly AccessTools.StructFieldRef<RimWorld.Planet.PlanetTile, int> TileLayerId
        = AccessTools.StructFieldRefAccess<RimWorld.Planet.PlanetTile, int>("layerId");

    private static bool TryGetMapLighting(out Map map, out BiomeDef biome)
    {
        map = null; biome = null;
        if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
            || Current.Game == null) return false;
        var game = Current.Game;
        int index = game.currentMapIndex;
        if (game.Maps == null || (uint)index >= (uint)game.Maps.Count) return false;
        Map candidate = game.Maps[index];
        if (candidate == null || candidate.Disposed || candidate.info == null || candidate.skyManager == null) return false;
        RimWorld.Planet.Tile tileInfo;
        if (candidate.IsPocketMap) tileInfo = candidate.pocketTileInfo;
        else
        {
            var grid = Current.Game.World?.grid;
            var tile = candidate.Tile;
            if (grid == null || !tile.Valid) return false;
            RimWorld.Planet.PlanetLayer layer;
            int layerId = TileLayerId(ref tile);
            if (layerId < 0) layer = grid.Surface;
            else if (!grid.PlanetLayers.TryGetValue(layerId, out layer)) return false;
            if (layer?.Tiles == null || (uint)tile.tileId >= (uint)layer.Tiles.Count) return false;
            tileInfo = layer.Tiles[tile.tileId];
        }
        if (tileInfo == null) return false;
        map = candidate; biome = tileInfo.PrimaryBiome;
        return true;
    }
    private static void UpdateMapLighting()
    {
        bool ready = TryGetMapLighting(out Map map, out BiomeDef biome);
        Vector4 sun = Vector4.zero, moon = Vector4.zero;
        Color ambient = Color.black, radiance = Color.black;
        if (ready)
        {
            //直接读取天空管理器已计算的日月参数。
            sun = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectSun);
            moon = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectMoon);
            UnityEngine.Color sky = map.skyManager.CurSky.colors.sky;
            ambient = radiance = new Color(sky.r, sky.g, sky.b, sky.a);
            if (biome != null && biome.disableSkyLighting)
            { ambient = Color.white; sun.w = moon.w = 0; }
        }
        Vector3 sunDir = new Vector3(-sun.x, 1, -sun.z).normalized;
        Vector3 moonDir = new Vector3(-moon.x, 1, -moon.z).normalized;
        Shader.SetGlobalVector(SunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0));
        Shader.SetGlobalVector(MoonDirectionId, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0));
        Shader.SetGlobalVector(SunColorId, new Vector4(radiance.r * sun.w, radiance.g * sun.w, radiance.b * sun.w, 0));
        Shader.SetGlobalVector(MoonColorId, new Vector4(radiance.r * moon.w, radiance.g * moon.w, radiance.b * moon.w, 0));
        Shader.SetGlobalVector(AmbientId, new Vector4(ambient.r, ambient.g, ambient.b, 0));
    }
}
