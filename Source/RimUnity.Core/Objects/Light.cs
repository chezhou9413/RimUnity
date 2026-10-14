namespace RimUnity;

public enum LightType { Spot, Directional, Point, Area, Rectangle = Area, Disc }

//灯光配置由地图粒子照明读取，照明计算交给游戏 GlowGrid。
public sealed class Light : MonoBehaviour
{
    internal static readonly System.Collections.Generic.HashSet<Light> Active = new();
    public static System.Collections.Generic.IEnumerable<Light> allLights => Active;
    public LightShadows shadows { get; set; }
    public float shadowStrength { get; set; } = 1;
    public float shadowBias { get; set; } = .05f;
    public float shadowNormalBias { get; set; } = .4f;
    public int cullingMask { get; set; } = -1;
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);
    public LightType type { get; set; } = LightType.Point;
    public Color color { get; set; } = Color.white;
    public float intensity { get; set; } = 1;
    public float range { get; set; } = 10;
    public float spotAngle { get; set; } = 30;
}
public enum LightShadows { None, Hard, Soft }
