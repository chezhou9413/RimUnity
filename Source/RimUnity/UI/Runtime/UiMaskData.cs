using System;
using Verse;

namespace RimUnity.UI.Runtime;

//用投影四边形反解 Mask 的 UV，最多八层，与常规八位 Stencil 的嵌套上限一致。
internal static class UiMaskData
{
    internal static UguiRuntime.Entry Current;
    private static readonly Vector4[] rows = new Vector4[32];
    private static readonly ComputeBuffer buffer = new(32, 16);
    private static Material defaultMaterial;
    internal static Material DefaultMaterial()
    {
        if (defaultMaterial == null)
        {
            if (!Waylith.Rendering.ShaderRegistry.TryResolve("RimUnity_UI", out var shader)) throw new InvalidOperationException("RimUnity_UI 未注册。");
            defaultMaterial = new Material(Shader.FromEngineShader("RimUnity_UI", shader));
        }
        return defaultMaterial;
    }
    internal static Material Apply(Material material)
    {
        material = UiRectMaskData.Apply(material);
        var masks = Current?.Masks;
        int count = masks?.Length ?? 0;
        if (count == 0) { if (material != null && material.HasProperty("_UiMaskCount")) material.SetFloat("_UiMaskCount", 0); return material; }
        if (count > 8) throw new NotSupportedException("Mask 嵌套不能超过八层。");
        if (material == null)
        {
            material = DefaultMaterial();
        }
        if (!material.HasProperty("_UiMaskCount")) throw new NotSupportedException("UI Shader 缺少 Mask 覆盖率入口：" + material.shader.name);
        material.SetFloat("_UiMaskCount", count);
        for (int i = 0; i < count; i++)
        {
            Mask mask = masks[i]; var rect = mask.transform; Rect r = rect.rect;
            Vector2 P(float x, float y) => RectTransformUtility.ToGui(Current.Canvas, rect.TransformPoint(new Vector3(x, y))) * Prefs.UIScale;
            Matrix4x4 inverse = Homography(P(r.xMin, r.yMin), P(r.xMax, r.yMin), P(r.xMax, r.yMax), P(r.xMin, r.yMax)).inverse;
            rows[i * 4] = new Vector4(inverse.m00, inverse.m01, inverse.m03, 0);
            rows[i * 4 + 1] = new Vector4(inverse.m10, inverse.m11, inverse.m13, 0);
            rows[i * 4 + 2] = new Vector4(inverse.m30, inverse.m31, inverse.m33, 0);
            Texture texture = Texture2D.whiteTexture; Rect uv = new(0, 0, 1, 1);
            if (mask.graphic is RimUnity.UI.Image image)
            {
                var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
                if (sprite != null) { texture = sprite.texture; uv = new Rect(sprite.textureRect.x / texture.width, sprite.textureRect.y / texture.height, sprite.textureRect.width / texture.width, sprite.textureRect.height / texture.height); }
            }
            else if (mask.graphic is RawImage raw) { texture = raw.texture ?? Texture2D.whiteTexture; uv = raw.uvRect; }
            else throw new NotSupportedException("Mask 目前需要 Image 或 RawImage。");
            rows[i * 4 + 3] = new Vector4(uv.x, uv.y, uv.width, uv.height);
            material.SetTexture("_UiMask" + i, texture);
        }
        buffer.SetData(rows); material.SetBuffer("_UiMasks", buffer);
        return material;
    }
    //单位矩形到任意平面投影四边形的单应矩阵。
    internal static Matrix4x4 Homography(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        float dx1 = p1.x - p2.x, dx2 = p3.x - p2.x, dx3 = p0.x - p1.x + p2.x - p3.x;
        float dy1 = p1.y - p2.y, dy2 = p3.y - p2.y, dy3 = p0.y - p1.y + p2.y - p3.y;
        float denominator = dx1 * dy2 - dx2 * dy1;
        float g = Mathf.Abs(denominator) > .000001f ? (dx3 * dy2 - dx2 * dy3) / denominator : 0;
        float h = Mathf.Abs(denominator) > .000001f ? (dx1 * dy3 - dx3 * dy1) / denominator : 0;
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = p1.x - p0.x + g * p1.x; m.m01 = p3.x - p0.x + h * p3.x; m.m03 = p0.x;
        m.m10 = p1.y - p0.y + g * p1.y; m.m11 = p3.y - p0.y + h * p3.y; m.m13 = p0.y;
        m.m30 = g; m.m31 = h;
        return m;
    }
}
