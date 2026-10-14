namespace RimUnity;

public enum LightType { Spot, Directional, Point, Area, Rectangle = Area, Disc }

//灯光配置由地图粒子照明读取，照明计算交给游戏 GlowGrid。
public sealed class Light : Behaviour
{
    public LightType type { get; set; } = LightType.Point;
    public Color color { get; set; } = Color.white;
    public float intensity { get; set; } = 1;
    public float range { get; set; } = 10;
    public float spotAngle { get; set; } = 30;
}
