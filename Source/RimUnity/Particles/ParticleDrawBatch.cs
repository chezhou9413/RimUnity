using System;
using System.Collections.Generic;
using RimUnity.Rendering;
using RimUnity.Diagnostics;

namespace RimUnity;

//材质段保留绘制顺序，共用有界网格页上传；不持有或复用预制体实例。
internal static class ParticleDrawBatch
{
    internal const int VertexLimit = 65536, IndexLimit = 196608;
    internal const int SegmentLimit = 256;
    private readonly record struct Segment(Material Material, int Layer);
    private static readonly List<Segment> segments = new();
    private sealed class Slot
    {
        internal readonly Mesh Mesh = new() { name = "RimUnity Shared Particle Batch", indexFormat = IndexFormat.UInt32 };
        internal int Frame;
        //每个相机及上传页使用独立槽，提交后同帧不再改写。
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
    private static int uploadPages;
    internal static int UploadPages => frame == Time.frameCount ? uploadPages : 0;
    internal static int Before => frame == Time.frameCount ? Inputs + IndependentDraws : 0;
    internal static int After => frame == Time.frameCount ? Outputs + IndependentDraws : 0;

    internal readonly record struct PoolUsage(int Pages, int UsedThisFrame, int IdlePages,
        long VertexCapacity, long IndexCapacity, long ManagedBufferBytes, long NativeCpuBytes);

    // Explicit lab sampling only; no per-frame work when the sampler is closed.
    // Includes all cameras. GPU allocations/retirement are owned by the host, not counted here.
    internal static PoolUsage ReadPoolUsage()
    {
        long vertices = 0, indices = 0, managed = 0, native = 0;
        foreach (var slot in slots)
        {
            var usage = slot.Mesh.ParticleBufferUsage();
            vertices += usage.Vertices; indices += usage.Indices;
            managed += usage.ManagedBytes; native += usage.NativeCpuBytes;
        }
        int used = frame == Time.frameCount ? cursor : 0;
        return new(slots.Count, used, slots.Count - used, vertices, indices, managed, native);
    }

    //多相机共享帧内游标，避免后一个相机覆盖前一个相机尚未冻结的网格。
    internal static void Begin()
    {
        if (active) throw new InvalidOperationException("粒子绘制批次不能重入。");
        if (frame != Time.frameCount)
        {
            frame = Time.frameCount; cursor = 0;
            Inputs = Outputs = IndependentDraws = uploadPages = 0;
            Trim();
        }
        active = true;
    }

    //相邻同材质仍合并成一段；材质切换只结束子网格，不立即上传整页。
    internal static bool TryAppend(Material source, int drawLayer, MaterialPropertyBlock properties,
        List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Color32> colors,
        List<Vector4> uv0, List<Vector4> uv1, List<Vector4> uv2, List<int> indices, List<ParticleVertex> packed = null)
    {
        using var cpu = LabCpuMetrics.Measure(LabCpuStage.BatchAttempt);
        int vertexCount = packed?.Count ?? vertices.Count;
        if (!active) { LabCpuMetrics.Batch(LabBatchReason.Inactive); return false; }
        LabBatchReason? reject = null;
        if (Diagnostics.ParticleDebugWindow.Capturing) reject = LabBatchReason.DebugCapture;
        else if (properties is { PropertyCount: > 0 }) reject = LabBatchReason.PropertyBlock;
        else if (properties is { HasStorageBindings: true }) reject = LabBatchReason.StorageBindings;
        else if (vertexCount > VertexLimit) reject = LabBatchReason.InputVertexLimit;
        else if (indices.Count > IndexLimit) reject = LabBatchReason.InputIndexLimit;
        else reject = ParticleBatchShaders.Check(source) switch
        {
            ParticleBatchShaders.Eligibility.MultiplePasses => LabBatchReason.MultiplePasses,
            ParticleBatchShaders.Eligibility.UnknownShader => LabBatchReason.UnknownShader,
            ParticleBatchShaders.Eligibility.UnsafeShader => LabBatchReason.UnsafeShader,
            _ => null
        };
        if (reject.HasValue)
        { LabCpuMetrics.Batch(reject.Value); Flush(); if (Sampling) IndependentDraws += source.passCount; return false; }
        if (pending != null)
        {
            LabBatchReason? boundary = source != material ? LabBatchReason.MaterialChanged
                : drawLayer != layer ? LabBatchReason.LayerChanged
                : source.engineMaterial.Version != materialVersion ? LabBatchReason.MaterialVersionChanged
                : pending.Mesh.vertexCount + vertexCount > VertexLimit ? LabBatchReason.BatchVertexLimit
                : indexCount + indices.Count > IndexLimit ? LabBatchReason.BatchIndexLimit : null;
            if (boundary.HasValue)
            {
                LabCpuMetrics.Batch(boundary.Value);
                bool full = pending.Mesh.vertexCount + vertexCount > VertexLimit || indexCount + indices.Count > IndexLimit
                    || segments.Count + 1 >= SegmentLimit;
                if (full) Flush(); else CloseSegment();
            }
        }
        if (pending == null)
        {
            if (cursor == slots.Count) slots.Add(new Slot());
            pending = slots[cursor++]; pending.Frame = frame; pending.Mesh.Clear();
            indexCount = 0;
        }
        if (material == null) { material = source; layer = drawLayer; materialVersion = source.engineMaterial.Version; }
        using (LabCpuMetrics.Measure(LabCpuStage.BatchGeometry))
            pending.Mesh.AppendParticleGeometry(vertices, normals, tangents, colors, uv0, uv1, uv2, indices, segments.Count, packed);
        LabCpuMetrics.Work(LabCpuStage.BatchGeometry, vertexCount, indices.Count);
        LabCpuMetrics.Batch(LabBatchReason.Accepted);
        indexCount += indices.Count;
        if (Sampling) Inputs++;
        return true;
    }

    private static void CloseSegment()
    {
        if (material == null) return;
        segments.Add(new(material, layer)); material = null;
    }

    //先完成整页再按原顺序提交子网格；不兼容项必须在自己的提交之前 Flush。
    internal static void Flush()
    {
        if (pending == null) return;
        using var cpu = LabCpuMetrics.Measure(LabCpuStage.BatchFlush);
        using var spike = Diagnostics.ParticleSpikeCapture.Measure("Particle.BatchFlush");
        try
        {
            CloseSegment();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh))
                    Graphics.DrawMesh(pending.Mesh, Matrix4x4.identity, segment.Material, segment.Layer, i);
                if (Sampling) Outputs++;
            }
            if (Sampling) uploadPages++;
        }
        finally { pending = null; material = null; indexCount = 0; segments.Clear(); }
    }

    //异常退出丢弃未提交段，不把半成品继续送进宿主队列。
    internal static void End()
    {
        pending = null; material = null; indexCount = 0; active = false; segments.Clear();
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
        slots.Clear(); cursor = 0; frame = -1; Inputs = Outputs = IndependentDraws = uploadPages = 0;
    }
}
