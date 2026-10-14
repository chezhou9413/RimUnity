using System;
using System.Runtime.CompilerServices;

namespace RimUnity.UI;

//保存引擎 Sprite 类型未提供的源精灵几何数据。
public static class SpriteGeometry
{
    internal sealed record Geometry(Vector2[] Positions, Vector2[] UV, int[] Triangles);
    private static readonly ConditionalWeakTable<Sprite, Geometry> Data = new();

    //加载器传入与 Unity Image.GenerateSprite 相同的归一化顶点。
    public static void Register(Sprite sprite, Vector2[] positions, Vector2[] uv, int[] triangles)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(uv);
        ArgumentNullException.ThrowIfNull(triangles);
        if (positions.Length != uv.Length || triangles.Length % 3 != 0)
            throw new ArgumentException("Sprite 顶点、UV 或三角形数量不匹配。");
        for (int i = 0; i < positions.Length; i++)
            if (!float.IsFinite(positions[i].x) || !float.IsFinite(positions[i].y) || !float.IsFinite(uv[i].x) || !float.IsFinite(uv[i].y))
                throw new ArgumentException("Sprite 几何包含非有限坐标。");
        foreach (int index in triangles)
            if (index < 0 || index >= positions.Length) throw new ArgumentException("Sprite 三角形索引越界。");
        Data.Remove(sprite);
        Data.Add(sprite, new Geometry((Vector2[])positions.Clone(), (Vector2[])uv.Clone(), (int[])triangles.Clone()));
    }

    //要求几何的图片不能在缺数据时静默退回矩形。
    internal static Geometry Get(Sprite sprite) => Data.TryGetValue(sprite, out Geometry geometry) ? geometry :
        throw new InvalidOperationException("紧密 Sprite 未导出几何数据：" + sprite.name);
}
