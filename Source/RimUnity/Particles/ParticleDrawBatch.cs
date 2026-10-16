using System;
using System.Collections.Generic;
using RimUnity.Rendering;

namespace RimUnity;

//连续且材质一致的粒子绘制共用动态网格，不持有或复用预制体实例。
internal static class ParticleDrawBatch
{
    internal const int VertexLimit = 65536, IndexLimit = 196608;
    private sealed class Slot
    {
        internal readonly Mesh Mesh = new() { name = "RimUnity Shared Particle Batch", indexFormat = IndexFormat.UInt32 };
        internal int Frame;
        //每个相机及连续绘制段使用独立槽，提交后同帧不再改写。
        internal Slot() => Mesh.MarkDynamic();
    }
    private static readonly List<Slot> slots = new();
    private static int frame = -1, cursor, layer, materialVersion, indexCount;
    private static bool active;
    private static Slot pending;
    private static Material material;
    internal static bool Sampling;
    internal static int Inputs { get; private set; }
    internal static int Outputs { get; private set; }
    internal static int IndependentDraws { get; private set; }
    internal static int Before => frame == Time.frameCount ? Inputs + IndependentDraws : 0;
    internal static int After => frame == Time.frameCount ? Outputs + IndependentDraws : 0;

    //多相机共享帧内游标，避免后一个相机覆盖前一个相机尚未冻结的网格。
    internal static void Begin()
    {
        if (active) throw new InvalidOperationException("粒子绘制批次不能重入。");
        if (frame != Time.frameCount)
        {
            frame = Time.frameCount; cursor = 0;
            Inputs = Outputs = IndependentDraws = 0;
            Trim();
        }
        active = true;
    }

    //兼容的相邻项按原索引顺序追加；改变材质、图层或超过容量就先提交已有段。
    internal static bool TryAppend(Material source, int drawLayer, MaterialPropertyBlock properties,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Color32> colors,
        List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices)
    {
        if (!active) return false;
        if (Diagnostics.ParticleDebugWindow.Capturing || properties is { PropertyCount: > 0 } || properties is { HasStorageBindings: true }
            || vertices.Count > VertexLimit || indices.Count > IndexLimit || !ParticleBatchShaders.Allows(source))
        { Flush(); if (Sampling) IndependentDraws += source.passCount; return false; }
        if (pending != null && (source != material || drawLayer != layer || source.engineMaterial.Version != materialVersion
            || pending.Mesh.vertexCount + vertices.Count > VertexLimit || indexCount + indices.Count > IndexLimit)) Flush();
        if (pending == null)
        {
            if (cursor == slots.Count) slots.Add(new Slot());
            pending = slots[cursor++]; pending.Frame = frame; pending.Mesh.Clear();
            material = source; layer = drawLayer; materialVersion = source.engineMaterial.Version; indexCount = 0;
        }
        pending.Mesh.AppendParticleGeometry(vertices, normals, tangents, colors, uv0, uv1, uv2, indices);
        indexCount += indices.Count;
        if (Sampling) Inputs++;
        return true;
    }

    //只有一个颜色 Pass，合并后仍保留原来的透明三角形先后顺序。
    internal static void Flush()
    {
        if (pending == null) return;
        using var spike = Diagnostics.ParticleSpikeCapture.Measure("Particle.BatchFlush");
        Graphics.DrawMesh(pending.Mesh, Matrix4x4.identity, material, layer);
        if (Sampling) Outputs++;
        pending = null; material = null; indexCount = 0;
    }

    //异常退出丢弃未提交段，不把半成品继续送进宿主队列。
    internal static void End()
    {
        pending = null; material = null; indexCount = 0; active = false;
    }

    //短期峰值复用容量；超过 128 个的闲置尾槽及时回收，其余闲置 120 帧回收。
    private static void Trim()
    {
        while (slots.Count > 0)
        {
            var slot = slots[^1]; int idle = unchecked(frame - slot.Frame);
            if (idle < 120 && (slots.Count <= 128 || idle <= 1)) break;
            Object.Destroy(slot.Mesh); slots.RemoveAt(slots.Count - 1);
        }
    }

    //读档和图形退出时解除所有 CPU/GPU 缓冲引用。
    internal static void Clear()
    {
        End();
        foreach (var slot in slots) Object.DestroyImmediate(slot.Mesh);
        slots.Clear(); cursor = 0; frame = -1; Inputs = Outputs = IndependentDraws = 0;
    }
}
