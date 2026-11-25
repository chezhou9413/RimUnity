using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//主线程保存绘制快照，沿用宿主的排序键、实例参数和骨骼缓冲。
internal sealed class HdrDrawFrame : IDisposable
{
    internal static readonly byte MapPass = (byte)AccessTools.Field(
        AccessTools.TypeByName("RimWorldWaylith.Rendering.RenderPassIds"), "MapMain").GetValue(null);
    internal static readonly string MetadataLayout;
    private static readonly Action<MeshDrawFrame, MeshDrawFrame> CopyMetadata = FrameMetadataCopier.Build<MeshDrawFrame>(out MetadataLayout);
    internal readonly MeshDrawFrame Frame;
    //Only the light-buffer snapshot needs to place each copied command back among native draws.
    internal readonly Dictionary<FinalDrawItem, FinalDrawItem> LightDraws;
    internal static string CommandBufferMode => SnapshotDrawBuffers.Status;
    private Workspace workspace;
    private int disposed;
    //Only empty CPU buffers survive between snapshots/games. No material or draw references
    //remain in the idle pool. A lease belongs to one snapshot until its render-frame disposal.
    private sealed class Workspace
    {
        internal readonly List<FinalDrawItem> Dynamic = new();
        internal readonly record struct CapturedBinding(MaterialBinding Binding, bool Linear, int GrabQueue);
        internal readonly Dictionary<MaterialBinding, CapturedBinding> Bindings = new();
        internal readonly List<byte> Linear = new();
        internal readonly List<FinalDrawItem> DrawSlots = new();
        private int usedDraws;
        internal FinalDrawItem CopyDraw(FinalDrawItem source, CapturedBinding captured)
        {
            int index = usedDraws;
            if (index == DrawSlots.Count) DrawSlots.Add(SnapshotDrawBuffers.Create());
            var result = DrawSlots[index];
            usedDraws++;
            SnapshotDrawBuffers.Copy(source, result, captured.Binding, captured.GrabQueue);
            return result;
        }
        //Owned binding buffers, not a material-result cache: every lease repopulates all values.
        //Only immutable numeric identity remains after Clear; shaders/layouts/textures are dropped.
        internal readonly List<MaterialBinding> BindingSlots = new();
        private readonly record struct Identity(long Id, long MaterialId, long VariantId);
        private sealed class Bucket
        {
            internal readonly List<MaterialBinding> Slots = new();
            internal int Used;
        }
        private readonly Dictionary<Identity, Bucket> identities = new();
        private readonly List<Bucket> activeBuckets = new();
        internal bool Bloom;
        internal int Epoch;
        internal MaterialBinding Capture(MaterialBinding source, Waylith.Rendering.Shader shader, ShaderVariantKey key, PipelineState state)
        {
            var identity = new Identity(source.Id, source.MaterialId, VariantId(key));
            bool firstIdentity = !identities.TryGetValue(identity, out var bucket);
            if (firstIdentity) identities.Add(identity, bucket = new Bucket());
            if (bucket.Used == 0) activeBuckets.Add(bucket);
            int slot = bucket.Used;
            MaterialBinding binding;
            if (slot == bucket.Slots.Count)
            {
                binding = new MaterialBinding(source.Id, source.MaterialId, source.MaterialVersion, source.DebugName,
                    shader, source.MaterialLayout, source.Uniforms, source.Resources, source.RenderQueue,
                    key, state, source.WrapModeOverride, source.UsesGlobals);
                //Register ownership before publishing the slot; Clear also covers partial construction.
                try
                {
                    bucket.Slots.Add(binding);
                    bucket.Used++;
                    BindingSlots.Add(binding);
                }
                catch
                {
                    if (bucket.Used == slot) //Not yet owned by the bucket.
                    {
                        binding.Update(0, null, null, null, default, default, 0, default, default, null, false);
                        binding.UpdateStorageArrays(Array.Empty<MaterialStorageArray>());
                    }
                    throw;
                }
                Diagnostics.DpaDiagnostics.Binding(Bloom, firstIdentity ? 1 : 2);
            }
            else
            {
                binding = bucket.Slots[slot];
                bucket.Used++;
                Diagnostics.DpaDiagnostics.Binding(Bloom, 0);
            }
            binding.Update(source.MaterialVersion, source.DebugName, shader, source.MaterialLayout, source.Uniforms,
                source.Resources, source.RenderQueue, key, state, source.WrapModeOverride, source.UsesGlobals);
            binding.UpdateStorageArrays(source.StorageArrays);
            HdrMaterialBindings.Stamp(binding);
            return binding;
        }
        internal void Clear()
        {
            Dynamic.Clear(); Bindings.Clear(); Linear.Clear();
            for (int i = 0; i < usedDraws; i++) SnapshotDrawBuffers.Clear(DrawSlots[i]);
            usedDraws = 0;
            foreach (var bucket in activeBuckets)
            {
                for (int i = 0; i < bucket.Used; i++)
                {
                    var binding = bucket.Slots[i];
                    binding.Update(0, null, null, null, default, default, 0, default, default, null, false);
                    binding.UpdateStorageArrays(Array.Empty<MaterialStorageArray>());
                }
                bucket.Used = 0;
            }
            activeBuckets.Clear();
        }
        internal bool CanRetain => Dynamic.Capacity <= 8192 && Linear.Capacity <= 8192 && DrawSlots.Capacity <= 8192 && Bindings.EnsureCapacity(0) <= 2048 && BindingSlots.Count <= 2048 && identities.Count <= 2048;
    }
    private static readonly Func<ShaderVariantKey, long> VariantId = AccessTools.Method(
        AccessTools.TypeByName("Waylith.Rendering.ShaderVariantIdRegistry"), "GetOrCreateId").CreateDelegate<Func<ShaderVariantKey, long>>();
    private static readonly object WorkspaceGate = new();
    private static readonly Stack<Workspace> Workspaces = new(8);
    private static int workspaceEpoch;
    private static Workspace RentWorkspace(bool bloom)
    {
        lock (WorkspaceGate)
        {
            bool created = Workspaces.Count == 0;
            var result = created ? new Workspace() : Workspaces.Pop();
            result.Bloom = bloom;
            if (created) Diagnostics.DpaDiagnostics.Workspace(bloom, false);
            result.Epoch = workspaceEpoch;
            return result;
        }
    }
    private static void ReturnWorkspace(Workspace value)
    {
        value.Clear();
        if (!value.CanRetain) { Diagnostics.DpaDiagnostics.Workspace(value.Bloom, true); return; }
        lock (WorkspaceGate) if (value.Epoch == workspaceEpoch && Workspaces.Count < 8) Workspaces.Push(value);
    }
    //Called only at graphics shutdown, after the host has retired its in-flight frames.
    internal static void ClearWorkspaces() { lock (WorkspaceGate) { workspaceEpoch++; Workspaces.Clear(); } }
    internal bool IsLinear(int index) => workspace.Linear[index] != 0;
    private delegate FinalDrawItem RentDraw(System.Numerics.Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<Waylith.Rendering.Texture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private static readonly RentDraw Rent = AccessTools.Method(typeof(FinalDrawItem), "Rent").CreateDelegate<RentDraw>();
    private static readonly AccessTools.FieldRef<FinalDrawItem, DrawSortKey> SortKey =
        AccessTools.FieldRefAccess<FinalDrawItem, DrawSortKey>("<SortKey>k__BackingField");

    //只有地图帧包含线性材质时才需要接管颜色附件。
    internal static bool Needed(MeshDrawFrame frame)
    {
        foreach (var draw in frame.Draws) if (IsMap(draw) && LinearShaderVariants.TryGet(draw.Binding.Shader, out _)) return true;
        foreach (var draw in frame.PersistentDraws) if (IsMap(draw) && LinearShaderVariants.TryGet(draw.Binding.Shader, out _)) return true;
        return false;
    }
    private static bool IsMap(FinalDrawItem draw) => draw.PassId == 0 || draw.PassId == MapPass;

    internal static bool ShouldCapture(FinalDrawItem draw, int mask, bool readOnlyDepth)
        => IsMap(draw) && (mask & (1 << draw.Layer)) != 0
            //折射由宿主在普通场景抓屏，泛光层不复制被折射的背景。
            && (!readOnlyDepth || draw.Binding.Shader.SceneGrab == null);

    //泛光层本帧没有可绘制内容时，向地图和预览返回空结果。
    internal static HdrDrawFrame CaptureBloom(MeshDrawFrame source, int mask)
    {
        var snapshot = new HdrDrawFrame(source, mask, readOnlyDepth: true);
        return snapshot.Frame == null ? null : snapshot;
    }

    //从首个可见指令继续复制，空泛光层无需租用工作区或构建地图快照。
    internal HdrDrawFrame(MeshDrawFrame source, int mask = -1, bool readOnlyDepth = false, bool lightBuffer = false)
    {
        using var cpu = Diagnostics.LabCpuMetrics.Measure(readOnlyDepth || lightBuffer
            ? Diagnostics.LabCpuStage.BloomSnapshot : Diagnostics.LabCpuStage.HdrSnapshot);
        ArgumentNullException.ThrowIfNull(source);
        if (lightBuffer) LightDraws = new();
        var trace = new Diagnostics.DpaDiagnostics.HdrCapture(readOnlyDepth || lightBuffer,
            System.Runtime.CompilerServices.Unsafe.SizeOf<MaterialUniformBytes>());
        bool complete = false;
        var stage = Diagnostics.DpaDiagnostics.HdrStage.Setup;
        var token = trace.Begin();
        try
        {
            var draws = source.Draws;
            var persistent = source.PersistentDraws;
            trace.DynamicInput = draws.Count; trace.PersistentInput = persistent.Count;
            int firstDynamic = 0, p = 0;
            if (readOnlyDepth)
            {
                while (firstDynamic < draws.Count && !ShouldCapture(draws[firstDynamic], mask, true)) firstDynamic++;
                if (firstDynamic == draws.Count)
                {
                    while (p < persistent.Count && !ShouldCapture(persistent[p], mask, true)) p++;
                    if (p == persistent.Count)
                    {
                        Diagnostics.LabCpuMetrics.RecordSnapshot(true, 0, mask);
                        trace.End(Diagnostics.DpaDiagnostics.HdrStage.FilterDynamic, token);
                        complete = true;
                        return;
                    }
                }
            }
            workspace = RentWorkspace(readOnlyDepth || lightBuffer);
            Frame = Shell(source, MeshDrawFrame.RentList(), MeshDrawFrame.RentList());
            trace.End(stage, token); stage = Diagnostics.DpaDiagnostics.HdrStage.FilterDynamic; token = trace.Begin();
            var dynamic = workspace.Dynamic;
            for (int i = firstDynamic; i < draws.Count; i++)
            {
                var item = draws[i];
                if (lightBuffer ? HdrLightBlend.ShouldCapture(item) : ShouldCapture(item, mask, readOnlyDepth))
                    dynamic.Add(item);
            }
            trace.End(stage, token); stage = Diagnostics.DpaDiagnostics.HdrStage.SortDynamic; token = trace.Begin();
            dynamic.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
            trace.End(stage, token); stage = Diagnostics.DpaDiagnostics.HdrStage.MergeAndLookup; token = trace.Begin();
            var bindings = workspace.Bindings;
            int d = 0;
            //与宿主合并顺序一致：键相等时先画持久绘制项。
            while (p < persistent.Count || d < dynamic.Count)
            {
                if (p < persistent.Count && !(lightBuffer ? HdrLightBlend.ShouldCapture(persistent[p])
                    : ShouldCapture(persistent[p], mask, readOnlyDepth))) { p++; continue; }
                var item = p < persistent.Count && (d == dynamic.Count || persistent[p].SortKey.CompareTo(dynamic[d].SortKey) <= 0)
                    ? persistent[p++] : dynamic[d++];
                var original = item.Binding;
                bool found = bindings.TryGetValue(original, out var captured);
                trace.End(stage, token); token = default;
                if (!found)
                {
                    stage = Diagnostics.DpaDiagnostics.HdrStage.BindingRefresh; token = trace.Begin();
                    bool linear = LinearShaderVariants.TryGet(original.Shader, out var shader);
                    if (!linear) shader = original.Shader;
                    var state = original.PipelineState;
                    if (lightBuffer)
                    {
                        if (!HdrLightBlend.TryAdapt(state, out state))
                            throw new InvalidOperationException("Light-buffer capture requires an adaptable blend.");
                        linear = true;
                    }
                    else if (readOnlyDepth) state = state with { Depth = state.Depth with { Write = false },
                        Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
                    var variant = original.ShaderVariant;
                    var key = new ShaderVariantKey(shader, variant.DynamicKeywordKeyId, variant.DynamicKeywordKey,
                        variant.UseTexture, variant.OutlineMrt, variant.Instanced);
                    captured = new Workspace.CapturedBinding(workspace.Capture(original, shader, key, state), linear, shader.SceneGrab?.Queue ?? int.MinValue);
                    bindings.Add(original, captured); trace.Bindings++;
                    trace.End(stage, token); token = default;
                }
                stage = Diagnostics.DpaDiagnostics.HdrStage.CommandCopy; token = trace.Begin();
                var copy = SnapshotDrawBuffers.Available ? workspace.CopyDraw(item, captured)
                    : Rent(item.Matrix, item.PassId, item.Mesh, captured.Binding, true, in item.Uniforms, in item.Resources,
                        Frame.Draws.Count, item.RenderQueue, item.Instancing, item.Layer, 0, item.Storage);
                if (!SnapshotDrawBuffers.Available) SortKey(copy) = item.SortKey;
                //If list growth fails, the rented item has not acquired a frame owner yet.
                try { Frame.Draws.Add(copy); }
                catch { if (!SnapshotDrawBuffers.Available) FinalDrawItem.Return(copy); throw; }
                LightDraws?.Add(item, copy);
                workspace.Linear.Add(captured.Linear ? (byte)1 : (byte)0); trace.Commands++;
                trace.End(stage, token); stage = Diagnostics.DpaDiagnostics.HdrStage.MergeAndLookup; token = trace.Begin();
            }
            Diagnostics.LabCpuMetrics.RecordSnapshot(readOnlyDepth || lightBuffer, Frame.Draws.Count, mask);
            trace.End(stage, token); token = default; complete = true;
        }
        catch { trace.End(stage, token); token = default; Dispose(); throw; }
        finally { trace.Complete(complete); }
    }

    //借用的 PersistentDraws 不归临时帧销毁，所有绘制项只由快照释放一次。
    internal static MeshDrawFrame Shell(MeshDrawFrame source, List<FinalDrawItem> draws, List<FinalDrawItem> persistent)
    {
        var frame = new MeshDrawFrame(source.FrameNumber, source.View, draws, persistent, source.SunDirection);
        try { CopyMetadata(source, frame); return frame; }
        catch { frame.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        var released = workspace;
        workspace = null;
        try
        {
            LightDraws?.Clear();
            //Owned command buffers stay with this lease; the host must not enqueue them.
            if (SnapshotDrawBuffers.Available) Frame?.Draws.Clear();
            Frame?.Dispose();
        }
        finally { if (released != null) ReturnWorkspace(released); }
    }
}
