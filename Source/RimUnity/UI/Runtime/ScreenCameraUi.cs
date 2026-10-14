using System;
using System.Linq;
using Verse;

namespace RimUnity.UI.Runtime;

//独立 UI 相机先用完整场景顶点格式渲染，再按相机深度合成到界面。
internal static class ScreenCameraUi
{
    private static Material composite;
    private static readonly GuiVertex[] quad = new GuiVertex[6];
    internal static void Draw()
    {
        foreach (Camera camera in Camera.Active.OrderBy(c => c.depth))
        {
            if (!camera.isActiveAndEnabled || camera == Camera.main || camera.targetTexture != null) continue;
            camera.Render();
            composite ??= new Material(Shader.Find("RimUnity_UIPremultiply") ?? throw new InvalidOperationException("UI 相机合成 Shader 未注册。"));
            Rect r = camera.pixelRect;
            float x0 = r.xMin / Prefs.UIScale, x1 = r.xMax / Prefs.UIScale;
            float y0 = (Screen.height - r.yMax) / Prefs.UIScale, y1 = (Screen.height - r.yMin) / Prefs.UIScale;
            Rect uv = camera.rect;
            quad[0] = Vertex(x0, y0, uv.xMin, uv.yMax); quad[1] = Vertex(x1, y0, uv.xMax, uv.yMax); quad[2] = Vertex(x1, y1, uv.xMax, uv.yMin);
            quad[3] = quad[0]; quad[4] = quad[2]; quad[5] = Vertex(x0, y1, uv.xMin, uv.yMin);
            UiMeshSubmission.Draw(quad, camera.ScreenTarget, composite);
        }
    }
    private static GuiVertex Vertex(float x, float y, float u, float v) => new() { position = new Vector2(x, y), uv = new Vector2(u, v), color = Color.white };
}
