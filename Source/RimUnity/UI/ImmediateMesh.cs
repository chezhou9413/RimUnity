using System;
using RimUnity.UI.Runtime;

namespace RimUnity.UI;

//为 IMGUI 提交带材质的四边形，坐标沿用当前 GUI 分组，裁切沿用宿主裁切栈。
public static class ImmediateMesh
{
    //四个点按边界顺序给出；每次提交都会快照材质参数，允许共用材质逐段绘制。
    public static void DrawQuad(Material material, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, Color color)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));
        if (!Runtime.NativeGuiHost.CanDraw) return;
        GuiVertex va = new() { position = a, uv = uvA, color = color };
        GuiVertex vb = new() { position = b, uv = uvB, color = color };
        GuiVertex vc = new() { position = c, uv = uvC, color = color };
        GuiVertex vd = new() { position = d, uv = uvD, color = color };
        Span<GuiVertex> vertices = stackalloc GuiVertex[6] { va, vb, vc, va, vc, vd };
        UiMeshSubmission.Draw(vertices, material.mainTexture, material);
    }
}
