using System;
using System.Collections.Generic;
using Image = RimUnity.UI.Image;

namespace RimUnity.UI.Runtime;

//补齐精灵网格、分区径向填充和逐格平铺。
internal static partial class UguiImageRenderer
{
    //按源索引展开三角形，UV 保留源纹理坐标。
    private static void Tight(UguiRuntime.Entry entry, Sprite sprite, Rect rect, Color color)
    {
        SpriteGeometry.Geometry geometry = SpriteGeometry.Get(sprite);
        foreach (int index in geometry.Triangles)
        {
            Vector2 local = rect.min + Vector2.Scale(rect.size, geometry.Positions[index]);
            Vertices.Add(new GuiVertex { position = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(local)),
                uv = geometry.UV[index], color = color });
        }
    }

    //180 度使用两半矩形，360 度使用四个象限，遵守 Unity 的局部角度规则。
    private static void RadialFilled(UguiRuntime.Entry entry, RimUnity.UI.Image image, Rect rect, Rect uv, Color color, float amount)
    {
        if (image.fillMethod == Image.FillMethod.Radial90)
        {
            RadialSection(entry, rect, uv, color, amount, image.fillClockwise, image.fillOrigin);
            return;
        }
        int count = image.fillMethod == Image.FillMethod.Radial180 ? 2 : 4;
        for (int part = 0; part < count; part++)
        {
            Rect section;
            float fill;
            int corner;
            if (count == 2)
            {
                int even = image.fillOrigin > 1 ? 1 : 0;
                section = image.fillOrigin == 0 || image.fillOrigin == 2
                    ? new Rect(part == even ? 0f : 0.5f, 0f, 0.5f, 1f)
                    : new Rect(0f, part == even ? 0.5f : 0f, 1f, 0.5f);
                fill = amount * 2f - (image.fillClockwise ? part : 1 - part);
                corner = (part + image.fillOrigin + 3) % 4;
            }
            else
            {
                section = new Rect(part < 2 ? 0f : 0.5f, part == 0 || part == 3 ? 0f : 0.5f, 0.5f, 0.5f);
                int order = (part + image.fillOrigin) % 4;
                fill = amount * 4f - (image.fillClockwise ? order : 3 - order);
                corner = (part + 2) % 4;
            }
            RadialSection(entry, SubRect(rect, section), SubRect(uv, section), color, Mathf.Clamp01(fill), image.fillClockwise, corner);
        }
    }

    //用穿过角点的直线裁剪归一化矩形，位置与 UV 共用裁剪比例。
    private static void RadialSection(UguiRuntime.Entry entry, Rect rect, Rect uv, Color color, float amount, bool clockwise, int corner)
    {
        if (amount < 0.001f) return;
        if (amount > 0.999f) { Quad(entry, rect, uv, color); return; }
        Vector2[] quad = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        Vector2 pivot = quad[corner];
        float start = clockwise ? 90f - corner * 90f : -corner * 90f;
        float angle = (start + (clockwise ? -1f : 1f) * amount * 90f) * Mathf.Deg2Rad;
        Vector2 ray = new(Mathf.Cos(angle), Mathf.Sin(angle));
        float sign = clockwise ? 1f : -1f;
        List<Vector2> polygon = new(5);
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = quad[i], b = quad[(i + 1) % 4];
            float da = sign * Cross(ray, a - pivot), db = sign * Cross(ray, b - pivot);
            if (da >= 0f) polygon.Add(a);
            if ((da >= 0f) != (db >= 0f)) polygon.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            Add(entry, rect, uv, polygon[0], color);
            Add(entry, rect, uv, polygon[i], color);
            Add(entry, rect, uv, polygon[i + 1], color);
        }
    }

    //二维叉积判断顶点在裁剪线的哪一侧。
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    //把归一化子区域映射到目标矩形。
    private static Rect SubRect(Rect rect, Rect fraction) => new(rect.min + Vector2.Scale(rect.size, fraction.min), Vector2.Scale(rect.size, fraction.size));

    //边框保持九宫格厚度，中间和各边按源精灵尺寸重复，末尾单元裁切 UV。
    private static void Tiled(UguiRuntime.Entry entry, RimUnity.UI.Image image, Sprite sprite, Rect rect, Rect uv, Color color)
    {
        float units = sprite.pixelsPerUnit / entry.Canvas.referencePixelsPerUnit * image.pixelsPerUnitMultiplier;
        if (!float.IsFinite(units) || units <= 0f) throw new InvalidOperationException("Image 的像素比例必须是有限正数。");
        Vector4 border = sprite.border, scaled = border / units;
        float sx = scaled.x + scaled.z > rect.width ? rect.width / (scaled.x + scaled.z) : 1f;
        float sy = scaled.y + scaled.w > rect.height ? rect.height / (scaled.y + scaled.w) : 1f;
        float[] x = { rect.xMin, rect.xMin + scaled.x * sx, rect.xMax - scaled.z * sx, rect.xMax };
        float[] y = { rect.yMin, rect.yMin + scaled.y * sy, rect.yMax - scaled.w * sy, rect.yMax };
        float[] u = { uv.xMin, uv.xMin + border.x / sprite.texture.width, uv.xMax - border.z / sprite.texture.width, uv.xMax };
        float[] v = { uv.yMin, uv.yMin + border.y / sprite.texture.height, uv.yMax - border.w / sprite.texture.height, uv.yMax };
        float tileWidth = (sprite.rect.width - border.x - border.z) / units;
        float tileHeight = (sprite.rect.height - border.y - border.w) / units;
        if (tileWidth <= 0f) tileWidth = x[2] - x[1];
        if (tileHeight <= 0f) tileHeight = y[2] - y[1];
        for (int col = 0; col < 3; col++) for (int row = 0; row < 3; row++)
        {
            if (col == 1 && row == 1 && !image.fillCenter) continue;
            Rect area = Rect.MinMaxRect(x[col], y[row], x[col + 1], y[row + 1]);
            if (area.width <= 0f || area.height <= 0f) continue;
            Rect sample = Rect.MinMaxRect(u[col], v[row], u[col + 1], v[row + 1]);
            TileArea(entry, area, sample, col == 1 ? tileWidth : area.width, row == 1 ? tileHeight : area.height, color, sprite.texture, image.material);
        }
    }

    //分批提交平铺网格，避免大区域的顶点列表随单元数量无限增长。
    private static void TileArea(UguiRuntime.Entry entry, Rect area, Rect uv, float width, float height, Color color, Texture texture, Material material)
    {
        int columns = Mathf.CeilToInt(area.width / width), rows = Mathf.CeilToInt(area.height / height);
        for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
        {
            float w = Mathf.Min(width, area.width - col * width), h = Mathf.Min(height, area.height - row * height);
            Quad(entry, new Rect(area.x + col * width, area.y + row * height, w, h), new Rect(uv.x, uv.y, uv.width * w / width, uv.height * h / height), color);
            if (Vertices.Count >= 60000)
            {
                UiMeshSubmission.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Vertices), texture, material);
                Vertices.Clear();
            }
        }
    }
}
