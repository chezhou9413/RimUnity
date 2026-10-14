using System.Collections.Generic;

namespace RimUnity;

public enum WindZoneMode { Directional, Spherical }

//方向风和球形风输出世界空间加速度，脉冲及湍流保持连续时间变化。
public sealed class WindZone : MonoBehaviour
{
    internal static readonly HashSet<WindZone> Active = new();
    public WindZoneMode mode;
    public float radius = 5, windMain = 1, windTurbulence = 1, windPulseMagnitude = .5f, windPulseFrequency = .01f;
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);

    //球形风在半径外无影响；有种子的连续波只近似 Unity 的原生湍流场。
    internal Vector3 Acceleration(Vector3 position, float time, uint seed)
    {
        Vector3 delta = position - transform.position;
        float attenuation = mode == WindZoneMode.Spherical ? Mathf.Clamp01(1 - delta.magnitude / Mathf.Max(.0001f, radius)) : 1;
        Vector3 direction = mode == WindZoneMode.Spherical ? delta.normalized : transform.forward;
        float phase = ParticleSystem.Random(seed, 213) * Mathf.PI * 2;
        float strength = windMain + Mathf.Sin(time * windPulseFrequency * Mathf.PI * 2) * windPulseMagnitude;
        Vector3 turbulence = new(Mathf.Sin(time * 1.13f + phase), Mathf.Sin(time * 1.71f + phase), Mathf.Sin(time * 1.37f + phase));
        return (direction * strength + turbulence * windTurbulence) * attenuation;
    }
}
