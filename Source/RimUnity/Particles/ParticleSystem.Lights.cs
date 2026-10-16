using System;
using System.Collections.Generic;
using Verse;

namespace RimUnity;

//粒子点光源接入地图真实 GlowGrid；这是格子照明，不是 Unity 的三维阴影管线。
public sealed partial class ParticleSystem
{
    public LightsModule lights = new();
    private Map lightMap;
    private float nextLightUpdate;
    private readonly Dictionary<ulong, MapLight> mapLights = new();
    private readonly HashSet<ulong> liveLights = new();
    private readonly List<ulong> expiredLights = new();
    private readonly record struct MapLight(int Handle, IntVec3 Cell, float Range, ColorInt Color);

    //Light 是灯光模板，模板对象保持禁用，仅读取颜色、强度及范围。
    public sealed class LightsModule
    {
        public bool enabled, useRandomDistribution = true, useParticleColor = true, sizeAffectsRange = true, alphaAffectsIntensity = true;
        public float ratio = 1;
        public int maxLights = 20;
        public Light light;
        public MinMaxCurve range = 1, intensity = 1;
        public float rangeMultiplier { get => range.curveMultiplier; set => range.curveMultiplier = value; }
        public float intensityMultiplier { get => intensity.curveMultiplier; set => intensity.curveMultiplier = value; }
    }

    //地图照明按十赫兹刷新，未变的格子、颜色和半径不重建泛光数据。
    private void UpdateParticleLights()
    {
        if (!lights.enabled || lights.light == null) { ReleaseParticleLights(); return; }
        if (lights.light.type != LightType.Point) throw new NotSupportedException("粒子地图照明目前只接通 Point 光源。");
        if (lightMap == null) lightMap = Find.CurrentMap;
        if (lightMap == null || Find.CurrentMap != lightMap) { ReleaseParticleLights(); return; }
        float now = Time.realtimeSinceStartup;
        if (now < nextLightUpdate) return;
        nextLightUpdate = now + .1f;
        liveLights.Clear();
        int available = Math.Max(0, lights.maxLights);
        float ratio = Mathf.Clamp01(lights.ratio);
        for (int i = 0; i < count && liveLights.Count < available; i++)
        {
            Particle p = particles[i];
            bool chosen = lights.useRandomDistribution ? Random(p.randomSeed, 221) < ratio : ratio > 0 && (long)Math.Floor(p.identity * (double)ratio) != (long)Math.Floor((p.identity - 1) * (double)ratio);
            if (!chosen) continue;
            Vector3 position = WorldPosition(p);
            IntVec3 cell = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y), Mathf.FloorToInt(position.z));
            if (!cell.InBounds(lightMap)) continue;
            float age = Age(p), random = Random(p.randomSeed, 222);
            Color color = lights.light.color;
            Color particleColor = ColorAt(p);
            if (lights.useParticleColor) color *= particleColor;
            float intensity = Math.Max(0, lights.light.intensity * lights.intensity.Evaluate(age, random));
            if (lights.alphaAffectsIntensity) intensity *= particleColor.a;
            color.r *= intensity; color.g *= intensity; color.b *= intensity; color.a = 1;
            float radius = lights.light.range * lights.range.Evaluate(age, random);
            if (lights.sizeAffectsRange) radius *= Mathf.Abs(SizeAt(p).x * ParticleScale(p).x);
            radius = Mathf.Round(Mathf.Clamp(radius, 0, GlowGrid.MaxLightRadius) * 8) / 8;
            if (radius <= 0 || intensity <= 0) continue;
            Color32 bytes = color;
            ColorInt value = new(bytes.r, bytes.g, bytes.b, bytes.a);
            liveLights.Add(p.identity);
            if (mapLights.TryGetValue(p.identity, out var previous))
            {
                if (previous.Cell == cell && previous.Range == radius && previous.Color.Equals(value)) continue;
                lightMap.glowGrid.DeregisterCustomGlower(previous.Handle);
            }
            int handle = lightMap.glowGrid.RegisterCustomGlower(cell, radius, value);
            mapLights[p.identity] = new MapLight(handle, cell, radius, value);
        }
        expiredLights.Clear();
        foreach (var pair in mapLights) if (!liveLights.Contains(pair.Key)) expiredLights.Add(pair.Key);
        foreach (ulong id in expiredLights) { lightMap.glowGrid.DeregisterCustomGlower(mapLights[id].Handle); mapLights.Remove(id); }
    }

    //不创建地图 Thing，灯光句柄不进入存档，退出与禁用后立即注销。
    private void ReleaseParticleLights()
    {
        if (lightMap != null) foreach (var item in mapLights.Values) lightMap.glowGrid.DeregisterCustomGlower(item.Handle);
        mapLights.Clear(); liveLights.Clear(); expiredLights.Clear();
        lightMap = null;
    }
}
