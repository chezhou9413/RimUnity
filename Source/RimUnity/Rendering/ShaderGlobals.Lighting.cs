using Verse;

namespace RimUnity.Rendering;

internal static partial class ShaderGlobals
{
    //只缓存参数 ID，光照每帧刷新，不跨地图保留对象引用。
    private static readonly int SunDirectionId = Shader.PropertyToID("_RU_MapSunDirection"), MoonDirectionId = Shader.PropertyToID("_RU_MapMoonDirection"),
        SunColorId = Shader.PropertyToID("_RU_MapSunColor"), MoonColorId = Shader.PropertyToID("_RU_MapMoonColor"), AmbientId = Shader.PropertyToID("_RU_MapAmbient");

    //读取地图公开的天空光照，向原生 Shader 提供方向和颜色。
    private static void UpdateMapLighting()
    {
        Map map = Find.CurrentMap;
        Vector4 sun = Vector4.zero, moon = Vector4.zero;
        Color ambient = Color.black, radiance = Color.black;
        if (map != null)
        {
            //直接读取天空管理器已计算的日月参数。
            sun = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectSun);
            moon = Shader.GetGlobalVector(ShaderPropertyIDs.WaterCastVectMoon);
            var sky = map.skyManager.CurSky.colors.sky;
            ambient = radiance = new Color(sky.r, sky.g, sky.b, sky.a);
            if (map.Biome != null && map.Biome.disableSkyLighting)
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
