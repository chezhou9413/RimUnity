using System;

namespace RimUnity;

//整数坐标用于导出字段及图集索引。
[Serializable]
public struct Vector2Int
{
    public int x, y;
    public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    public static Vector2Int zero => default;
    public static Vector2Int one => new(1, 1);
    public static implicit operator Vector2(Vector2Int value) => new(value.x, value.y);
}
[Serializable]
public struct Vector3Int
{
    public int x, y, z;
    public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3Int zero => default;
    public static implicit operator Vector3(Vector3Int value) => new(value.x, value.y, value.z);
}
