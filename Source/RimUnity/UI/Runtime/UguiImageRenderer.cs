using System;
using System.Collections.Generic;
using Image = RimUnity.UI.Image;

namespace RimUnity.UI.Runtime;

//把 Image 的精灵与填充参数转换为 Waylith GUI 三角形。
internal static partial class UguiImageRenderer
{
    private static readonly List<GuiVertex> Vertices = new();

    //RawImage 与 Image 共用顶点提交和画布变换。
    internal static void DrawRaw(UguiRuntime.Entry entry, RawImage image, Color color)
    {
        Vertices.Clear();
        Quad(entry, entry.Rect.rect, image.uvRect, color);
        UiMeshSubmission.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Vertices), image.texture, image.material);
    }

    //图片保持原有精灵、颜色、九宫格和填充参数。
    internal static void Draw(UguiRuntime.Entry entry, RimUnity.UI.Image image, Color color)
    {
        Selectable button = image.GetComponentInParent<Selectable>();
        Sprite sprite = button != null && button.targetGraphic == image && button.StateSprite != null ? button.StateSprite :
            image.overrideSprite != null ? image.overrideSprite : image.sprite;
        Texture texture = sprite?.texture;
        Rect uv = sprite != null && texture != null ? new Rect(sprite.textureRect.x / texture.width, sprite.textureRect.y / texture.height,
            sprite.textureRect.width / texture.width, sprite.textureRect.height / texture.height) : new Rect(0, 0, 1, 1);
        Rect rect = entry.Rect.rect;
        if (rect.width <= 0 || rect.height <= 0 || color.a <= 0) return;
        Vertices.Clear();
        if (image.preserveAspect && sprite != null && (image.type == Image.Type.Simple || image.type == Image.Type.Filled))
        {
            float ratio = sprite.rect.width / sprite.rect.height;
            Vector2 size = rect.width / rect.height > ratio ? new Vector2(rect.height * ratio, rect.height) : new Vector2(rect.width, rect.width / ratio);
            rect = new Rect(rect.position + Vector2.Scale(rect.size - size, entry.Rect.pivot), size);
        }
        if (image.type == Image.Type.Simple && image.useSpriteMesh && sprite != null)
        {
            Tight(entry, sprite, rect, color);
        }
        else if (image.type == Image.Type.Tiled && sprite != null)
            Tiled(entry, image, sprite, rect, uv, color);
        else if (image.type == Image.Type.Sliced && sprite != null && sprite.border != Vector4.zero)
            Sliced(entry, image, sprite, rect, uv, color);
        else if (image.type == Image.Type.Filled && image.fillAmount < 1f)
        {
            float amount = Mathf.Clamp01(image.fillAmount);
            if (amount <= 0) return;
            if (image.fillMethod >= Image.FillMethod.Radial90) RadialFilled(entry, image, rect, uv, color, amount);
            else
            {
                bool horizontal = image.fillMethod == Image.FillMethod.Horizontal;
                if (horizontal)
                {
                    if (image.fillOrigin == 1) { rect.x += rect.width * (1 - amount); uv.x += uv.width * (1 - amount); }
                    rect.width *= amount; uv.width *= amount;
                }
                else
                {
                    if (image.fillOrigin == 1) { rect.y += rect.height * (1 - amount); uv.y += uv.height * (1 - amount); }
                    rect.height *= amount; uv.height *= amount;
                }
                Quad(entry, rect, uv, color);
            }
        }
        else Quad(entry, rect, uv, color);
        UiMeshSubmission.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Vertices), texture, image.material);
    }

    //九宫格边框按精灵 PPU、画布参考值和倍率换算为 UI 单位。
    private static void Sliced(UguiRuntime.Entry entry, RimUnity.UI.Image image, Sprite sprite, Rect r, Rect uv, Color color)
    {
        Vector4 b = sprite.border;
        float units = sprite.pixelsPerUnit / entry.Canvas.referencePixelsPerUnit * image.pixelsPerUnitMultiplier;
        if (units <= 0) throw new InvalidOperationException("Image 的 pixelsPerUnitMultiplier 必须大于零。");
        Vector4 d = b / units;
        float sx = d.x + d.z > r.width ? r.width / (d.x + d.z) : 1f;
        float sy = d.y + d.w > r.height ? r.height / (d.y + d.w) : 1f;
        float[] x = { r.xMin, r.xMin + d.x * sx, r.xMax - d.z * sx, r.xMax };
        float[] y = { r.yMin, r.yMin + d.y * sy, r.yMax - d.w * sy, r.yMax };
        float[] u = { uv.xMin, uv.xMin + b.x / sprite.texture.width, uv.xMax - b.z / sprite.texture.width, uv.xMax };
        float[] v = { uv.yMin, uv.yMin + b.y / sprite.texture.height, uv.yMax - b.w / sprite.texture.height, uv.yMax };
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
        {
            if (i == 1 && j == 1 && !image.fillCenter) continue;
            Quad(entry, Rect.MinMaxRect(x[i], y[j], x[i + 1], y[j + 1]), Rect.MinMaxRect(u[i], v[j], u[i + 1], v[j + 1]), color);
        }
    }

    //矩形转为两组三角形，局部旋转和缩放由 Transform 矩阵保留。
    private static void Quad(UguiRuntime.Entry e, Rect r, Rect uv, Color c)
    {
        Add(e, r, uv, new Vector2(0, 0), c); Add(e, r, uv, new Vector2(0, 1), c); Add(e, r, uv, new Vector2(1, 1), c);
        Add(e, r, uv, new Vector2(0, 0), c); Add(e, r, uv, new Vector2(1, 1), c); Add(e, r, uv, new Vector2(1, 0), c);
    }

    //构造携带原精灵 UV 的屏幕顶点。
    private static void Add(UguiRuntime.Entry e, Rect r, Rect uv, Vector2 p, Color c)
    {
        Vector2 local = r.min + Vector2.Scale(r.size, p);
        Vertices.Add(new GuiVertex { position = RectTransformUtility.ToGui(e.Canvas, e.Rect.TransformPoint(local)),
            uv = uv.min + Vector2.Scale(uv.size, p), color = c });
    }
}
