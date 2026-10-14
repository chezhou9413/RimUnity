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
        internal bool dirty = true;
        internal int lastUse;
        //UI 网格频繁变化，使用宿主动态上传路径。
        internal Batch() => mesh.MarkDynamic();
    }
    private sealed class CanvasBatches { internal readonly List<Batch> batches = new(); internal int used; }
    private static readonly Dictionary<Canvas, CanvasBatches> canvases = new();
    private static readonly List<GuiVertex> clipped = new();
    internal static void BeginFrame()
    {
        foreach (var canvas in canvases.Values)
        {
            canvas.used = 0;
            //页面切换后回收长期未用的尾部批次，滚动期间保留短期容量。
            for (int i = canvas.batches.Count - 1; i >= 0; i--)
            {
                Batch batch = canvas.batches[i];
                if (Time.frameCount - batch.lastUse < 120) break;
                RimUnity.Object.Destroy(batch.mesh);
                if (batch.material != null) RimUnity.Object.Destroy(batch.material);
                canvas.batches.RemoveAt(i);
            }
        }
    }
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
        batch.lastUse = Time.frameCount;
        string key = source?.shader.name == "RimUnity_SDF" ? "RimUnity_WorldSDF" : source?.shader.name == "RimUnity_UIAdditive" ? "RimUnity_ParticleAdditive" : "RimUnity_ParticleAlpha";
        bool custom = source != null && source.shader.name != "RimUnity_SDF" && source.shader.name != "RimUnity_UI" && source.shader.name != "RimUnity_UIAdditive";
        if (custom) key = source.shader.name;
        if (batch.material == null || batch.material.shader.name != key || (custom && batch.material.shader != source.shader))
        {
            if (batch.material != null) RimUnity.Object.Destroy(batch.material);
            if (custom) batch.material = new Material(source);
            else
            {
                if (!Waylith.Rendering.ShaderRegistry.TryResolve(key, out var shader)) throw new InvalidOperationException("世界 UI Shader 未注册：" + key);
                batch.material = new Material(Shader.FromEngineShader(key, shader));
            }
        }
        if (source != null) batch.material.CopyPropertiesFromMaterial(source);
        else { batch.material.SetColor("_Color", Color.white); batch.material.SetVector("_MainTex_ST", new Vector4(1, 1, 0, 0)); }
        batch.material.mainTexture = texture ?? Texture2D.whiteTexture;
        UiMaskData.Apply(batch.material);
        Camera camera = root.worldCamera != null ? root.worldCamera : Camera.main;
        UiTriangleClip.Clip(data, Current.Clip, clipped); data = CollectionsMarshal.AsSpan(clipped);
        bool changed = batch.dirty || batch.vertices.Count != data.Length;
        batch.dirty = true;
        int cursor = 0;
        //整批共用逆投影和平面，交点已经是世界坐标，无需先转局部再转回世界。
        Matrix4x4 inverse = (camera.projectionMatrix * camera.worldToCameraMatrix).inverse;
        Rect viewport = camera.pixelRect;
        Vector3 normal = Current.Rect.rotation * Vector3.forward, planePoint = Current.Rect.position;
        float guiScale = Prefs.UIScale, screenHeight = Screen.height;
        foreach (ref readonly GuiVertex vertex in data)
        {
            float x = (vertex.position.x * guiScale - viewport.x) / viewport.width * 2 - 1;
            float y = 1 - (screenHeight - vertex.position.y * guiScale - viewport.y) / viewport.height * 2;
            Vector4 near = inverse * new Vector4(x, y, 1, 1), far = inverse * new Vector4(x, y, 0, 1);
            Vector3 origin = (Vector3)near / near.w, direction = ((Vector3)far / far.w - origin).normalized;
            float denominator = Vector3.Dot(direction, normal);
            if (Mathf.Abs(denominator) < .000001f) return;
            float distance = Vector3.Dot(planePoint - origin, normal) / denominator;
            if (distance < 0) return;
            changed |= Set(batch.vertices, cursor, origin + direction * distance);
            changed |= Set(batch.uv, cursor, vertex.uv);
            changed |= Set(batch.colors, cursor, vertex.color);
            Set(batch.indices, cursor, cursor);
            cursor++;
        }
        if (changed)
        {
            Trim(batch.vertices, cursor); Trim(batch.uv, cursor); Trim(batch.colors, cursor); Trim(batch.indices, cursor);
            batch.mesh.SetVertices(batch.vertices); batch.mesh.SetColors(batch.colors); batch.mesh.SetUVs(0, batch.uv);
            batch.mesh.SetTriangles(batch.indices, 0);
        }
        batch.dirty = false;
        RimUnity.Graphics.DrawMesh(batch.mesh, Matrix4x4.identity, batch.material, Current.RenderLayer);
    }

    //静止页面复用已经上传的网格，只有实际变化的坐标、颜色或 UV 才触发上传。
    private static bool Set<T>(List<T> values, int index, T value)
    {
        if (index == values.Count) { values.Add(value); return true; }
        if (EqualityComparer<T>.Default.Equals(values[index], value)) return false;
        values[index] = value; return true;
    }

    //裁剪后的顶点变少时同步收缩有效范围，容量留作下一次滚动复用。
    private static void Trim<T>(List<T> values, int count)
    {
        if (values.Count > count) values.RemoveRange(count, values.Count - count);
    }
}
