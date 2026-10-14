namespace RimUnity;

//射线保存归一化方向，以世界距离查询点。
public struct Ray
{
    private Vector3 directionValue;
    public Vector3 origin { get; set; }
    public Vector3 direction { get => directionValue; set => directionValue = value.normalized; }
    public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; directionValue = direction.normalized; }
    public Vector3 GetPoint(float distance) => origin + directionValue * distance;
}
