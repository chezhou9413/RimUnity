using System.Collections.Generic;

namespace RimUnity.UI.Runtime;

//图片阴影沿用原始三角形与 UV，九宫格、填充和遮罩保持同一几何。
internal static partial class UguiImageRenderer
{
    private static readonly List<Shadow> ImageEffects = new();

    //按组件顺序叠加效果，最后保留原始图形。
    private static void DrawEffects(UguiRuntime.Entry entry, Graphic graphic)
    {
        graphic.GetComponents(ImageEffects);
        foreach (Shadow effect in ImageEffects)
        {
            if (!effect.isActiveAndEnabled) continue;
            int start = 0, end = Vertices.Count;
            Vector2 distance = effect.effectDistance;
            Apply(distance);
            if (effect is Outline)
            {
                Apply(new Vector2(distance.x, -distance.y));
                Apply(new Vector2(-distance.x, distance.y));
                Apply(-distance);
            }

            //Unity 的效果顺序是偏移副本在前、未改动顶点在后。
            void Apply(Vector2 localOffset)
            {
                Vector2 offset = RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(localOffset))
                    - RectTransformUtility.ToGui(entry.Canvas, entry.Rect.TransformPoint(Vector3.zero));
                for (int i = start; i < end; i++)
                {
                    GuiVertex original = Vertices[i];
                    Vertices.Add(original);
                    Color shade = effect.effectColor;
                    if (effect.useGraphicAlpha) shade.a *= original.color.a / 255f;
                    original.position += offset;
                    original.color = shade;
                    Vertices[i] = original;
                }
                start = end;
                end = Vertices.Count;
            }
        }
        ImageEffects.Clear();
    }
}
