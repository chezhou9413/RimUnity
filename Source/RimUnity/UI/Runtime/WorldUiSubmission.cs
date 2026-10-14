using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Verse;

namespace RimUnity.UI.Runtime;

//相机和世界画布通过场景网格绘制，深度测试由 Waylith 完成。
internal static class WorldUiSubmission
{
    internal static UguiRuntime.Entry Current;
    private sealed class Batch
    {
        internal Mesh mesh = new() { indexFormat = IndexFormat.UInt32 };
        internal Material material;
        internal readonly List<Vector3> vertices = new();
        internal readonly List<Vector2> uv = new();
        internal readonly List<Color32> colors = new();
        internal readonly List<int> indices = new();
    }
    private sealed class CanvasBatches { internal readonly List<Batch> batches = new(); internal int used; }
    private static readonly Dictionary<Canvas, CanvasBatches> canvases = new();
    private static readonly List<GuiVertex> clipped = new();
    internal static void BeginFrame() { foreach (var canvas in canvases.Values) canvas.used = 0; }
    internal static void Release(Canvas canvas)
    {
        if (!canvases.Remove(canvas, out var state)) return;
        foreach (var batch in state.batches) { RimUnity.Object.Destroy(batch.mesh); if (batch.material != null) RimUnity.Object.Destroy(batch.material); }
    }
    //GUI 几何通过所属矩形平面还原为三维顶点，透视校正在场景顶点阶段进行。
    internal static void Draw(ReadOnlySpan<GuiVertex> data, Texture texture, Material source)
    {
        Canvas root = Current.Canvas.rootCanvas;
        if (!canvases.TryGetValue(root, out var state)) canvases.Add(root, state = new());
        if (state.used == state.batches.Count) state.batches.Add(new());
        Batch batch = state.batches[state.used++];
        string key = source?.shader.name == "RimUnity_SDF" ? "RimUnity_WorldSDF" : source?.shader.name == "RimUnity_UIAdditive" ? "RimUnity_ParticleAdditive" : "RimUnity_ParticleAlpha";
        if (source != null && source.shader.name != "RimUnity_SDF" && source.shader.name != "RimUnity_UI" && source.shader.name != "RimUnity_UIAdditive")
            throw new NotSupportedException("世界 UI 材质尚未提供场景入口：" + source.shader.name);
        if (batch.material == null || batch.material.shader.name != key)
        {
            if (batch.material != null) RimUnity.Object.Destroy(batch.material);
            if (!Waylith.Rendering.ShaderRegistry.TryResolve(key, out var shader)) throw new InvalidOperationException("世界 UI Shader 未注册：" + key);
            batch.material = new Material(Shader.FromEngineShader(key, shader));
        }
        if (source != null) batch.material.CopyPropertiesFromMaterial(source);
        else { batch.material.SetColor("_Color", Color.white); batch.material.SetVector("_MainTex_ST", new Vector4(1, 1, 0, 0)); }
        batch.material.mainTexture = texture ?? Texture2D.whiteTexture;
        UiMaskData.Apply(batch.material);
        batch.vertices.Clear(); batch.uv.Clear(); batch.colors.Clear(); batch.indices.Clear();
        Camera camera = root.worldCamera != null ? root.worldCamera : Camera.main;
        UiTriangleClip.Clip(data, Current.Clip, clipped); data = CollectionsMarshal.AsSpan(clipped);
        foreach (ref readonly GuiVertex vertex in data)
        {
            Vector2 screen = new(vertex.position.x * Prefs.UIScale, Screen.height - vertex.position.y * Prefs.UIScale);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Current.Rect, screen, camera, out Vector2 local)) return;
            batch.indices.Add(batch.vertices.Count); batch.vertices.Add(Current.Rect.TransformPoint(local)); batch.uv.Add(vertex.uv); batch.colors.Add(vertex.color);
        }
        batch.mesh.Clear(); batch.mesh.SetVertices(batch.vertices); batch.mesh.SetColors(batch.colors); batch.mesh.SetUVs(0, batch.uv); batch.mesh.SetTriangles(batch.indices, 0); batch.mesh.RecalculateBounds();
        RimUnity.Graphics.DrawMesh(batch.mesh, Matrix4x4.identity, batch.material, Current.Rect.gameObject.layer);
    }
}
