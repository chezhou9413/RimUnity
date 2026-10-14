using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RimUnity.UI.Runtime;

namespace RimUnity.UI;

//骨骼等动态图形提供局部三角形，画布统一处理坐标、透明度与遮罩。
public abstract class MeshGraphic : MaskableGraphic
{
    private readonly UiMeshWriter writer = new();
    protected abstract void PopulateMesh(UiMeshWriter output);
    internal void DrawMesh(UguiRuntime.Entry entry, Color tint)
    {
        writer.Entry = entry; writer.Tint = tint;
        try { PopulateMesh(writer); }
        finally { writer.Entry = null; }
    }
}

//每个图形复用提交缓冲区，按提交顺序保留多材质图层。
public sealed class UiMeshWriter
{
    internal UguiRuntime.Entry Entry;
    internal Color Tint;
    private readonly List<GuiVertex> transformed = new();
    public void Submit(ReadOnlySpan<GuiVertex> vertices, Texture texture, Material material, bool premultipliedAlpha = false)
    {
        if (Entry == null) throw new InvalidOperationException("网格只能在画布绘制回调中提交。");
        transformed.Clear();
        Color tint = Tint;
        if (premultipliedAlpha) { tint.r *= tint.a; tint.g *= tint.a; tint.b *= tint.a; }
        //同一批骨骼顶点共享矩阵与视口，避免逐顶点遍历父级和求相机逆矩阵。
        Matrix4x4 transform = Entry.Rect.localToWorldMatrix;
        bool overlay = Entry.Canvas.EffectiveMode == RenderMode.ScreenSpaceOverlay;
        float guiScale = Verse.Prefs.UIScale, height = Screen.height;
        float canvasScale = Entry.Canvas.scaleFactor;
        Rect viewport = default;
        if (!overlay)
        {
            Camera camera = Entry.Canvas.rootCanvas.worldCamera ?? Camera.main;
            viewport = camera.pixelRect;
            transform = camera.projectionMatrix * camera.worldToCameraMatrix * transform;
        }
        foreach (GuiVertex source in vertices)
        {
            GuiVertex vertex = source;
            if (overlay)
            {
                Vector3 world = transform.MultiplyPoint3x4(source.position);
                vertex.position = new Vector2(world.x * canvasScale, height - world.y * canvasScale) / guiScale;
            }
            else
            {
                Vector4 clip = transform * new Vector4(source.position.x, source.position.y, 0, 1);
                vertex.position = Mathf.Abs(clip.w) < 1e-7f ? new Vector2(0, height) / guiScale
                    : new Vector2(viewport.x + (clip.x / clip.w + 1) * .5f * viewport.width,
                        height - viewport.y - (1 - clip.y / clip.w) * .5f * viewport.height) / guiScale;
            }
            vertex.color = (Color)source.color * tint;
            transformed.Add(vertex);
        }
        UiMeshSubmission.Draw(CollectionsMarshal.AsSpan(transformed), texture, material);
    }
}
