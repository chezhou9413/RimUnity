using System;
using System.Collections.Generic;

namespace RimUnity.UI.Runtime;

//世界画布的矩形裁切发生在网格提交前，保留插值后的颜色和 UV。
internal static class UiTriangleClip
{
    private static readonly List<GuiVertex> a = new(), b = new();
    internal static void Clip(ReadOnlySpan<GuiVertex> source, Rect clip, List<GuiVertex> target)
    {
        target.Clear();
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            a.Clear(); a.Add(source[i]); a.Add(source[i + 1]); a.Add(source[i + 2]);
            Cut(0, clip.xMin, true); Cut(0, clip.xMax, false); Cut(1, clip.yMin, true); Cut(1, clip.yMax, false);
            for (int j = 1; j + 1 < a.Count; j++) { target.Add(a[0]); target.Add(a[j]); target.Add(a[j + 1]); }
        }
    }
    private static void Cut(int axis, float edge, bool minimum)
    {
        b.Clear(); if (a.Count == 0) return;
        var previous = a[^1]; bool before = minimum ? previous.position[axis] >= edge : previous.position[axis] <= edge;
        foreach (var vertex in a)
        {
            bool inside = minimum ? vertex.position[axis] >= edge : vertex.position[axis] <= edge;
            if (inside != before)
            {
                float t = (edge - previous.position[axis]) / (vertex.position[axis] - previous.position[axis]);
                b.Add(new GuiVertex { position = Vector2.LerpUnclamped(previous.position, vertex.position, t), uv = Vector2.LerpUnclamped(previous.uv, vertex.uv, t), color = Color.LerpUnclamped(previous.color, vertex.color, t) });
            }
            if (inside) b.Add(vertex);
            previous = vertex; before = inside;
        }
        a.Clear(); a.AddRange(b);
    }
}
