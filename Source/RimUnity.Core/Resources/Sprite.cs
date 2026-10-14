using System;

namespace RimUnity;

//精灵保留贴图区域、像素枢轴和九宫格边距。
public sealed class Sprite : Object
{
    public Texture2D texture { get; private set; }
    public Rect rect { get; private set; }
    public Rect textureRect => rect;
    public Vector2 pivot { get; private set; }
    public float pixelsPerUnit { get; private set; }
    public Vector4 border { get; private set; }
    public Bounds bounds => new(new Vector3((rect.width * .5f - pivot.x) / pixelsPerUnit, (rect.height * .5f - pivot.y) / pixelsPerUnit), new Vector3(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit));
    public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit = 100, uint extrude = 0, SpriteMeshType meshType = SpriteMeshType.FullRect, Vector4 border = default)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit));
        return new() { texture = texture, rect = rect, pivot = Vector2.Scale(pivot, rect.size), pixelsPerUnit = pixelsPerUnit, border = border };
    }
}
public enum SpriteMeshType { FullRect, Tight }
