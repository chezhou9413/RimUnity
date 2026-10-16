using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using RimUnity.Rendering;
using Waylith.Rendering;
using NativeMaterial = Waylith.Rendering.Material;
using NativeTexture = Waylith.Rendering.Texture;

namespace RimUnity.ScreenEfx;

/// <summary>BuildFrame-only writer. Each AddPass captures values immediately, without cloning a material.</summary>
public sealed class EfxFrameBuilder : IDisposable
{
    private static readonly Func<UnityEngine.Material, int, NativeMaterial> HostPass =
        AccessTools.Method(typeof(UnityEngine.Material), "PassMaterial").CreateDelegate<Func<UnityEngine.Material, int, NativeMaterial>>();
    private static Mesh triangle;
    private static readonly int TimeId = Shader.PropertyToID("_Time"), SourceSrgbId = Shader.PropertyToID("_RimUnitySrgb_MainTex");
    private readonly EfxFrame owner;
    private EfxLease lease;
    private readonly List<EfxPass> passes = new();
    private DrawProperty[] values = Array.Empty<DrawProperty>();
    private bool finished;
    internal EfxFrameBuilder(EfxFrame owner) => this.owner = owner;
    internal bool CanCache => values.Length <= 256;
    internal void Begin(EfxLease value) { lease = value; finished = false; }

    public void AddPass(Material material, int pass = 0, MaterialPropertyBlock properties = null)
    {
        EfxPlayer.AssertThread();
        ArgumentNullException.ThrowIfNull(material);
        Add(material.GetNativePass(pass), properties);
    }
    public void AddPass(UnityEngine.Material material, int pass = 0, UnityEngine.MaterialPropertyBlock properties = null)
    {
        EfxPlayer.AssertThread();
        ArgumentNullException.ThrowIfNull(material);
        if ((uint)pass >= material.passCount) throw new ArgumentOutOfRangeException(nameof(pass));
        Add(HostPass(material, pass), properties);
    }

    private void Add(NativeMaterial material, IDrawPropertyProvider properties)
    {
        EfxPlayer.AssertThread();
        if (finished) throw new ObjectDisposedException(nameof(EfxFrameBuilder));
        if (material?.Shader == null || material.Shader.HasErrors) throw new ArgumentException("Invalid screen-effect material.");
        if (properties?.HasStorageBindings == true) throw new NotSupportedException("Screen-effect overrides support scalar, vector, matrix and texture properties; put arrays in the material.");
        triangle ??= MakeTriangle();
        var command = new MeshDrawCommand(triangle.EngineMesh, material, default,
            System.Numerics.Matrix4x4.Identity, submeshIndex: 0, passId: 0);
        var draw = MeshDrawSubmission.DrawItemMaker.TryMake(in command, MeshDrawSubmission.ActiveArena,
            UnityEngine.Shader.GlobalKeywordKey, diagnosticPrefix: "RimUnity.ScreenEfx");
        if (draw == null) throw new InvalidOperationException($"Could not capture screen-effect draw for mesh '{triangle.name}' and material '{material.DebugName}'. See the preceding host rendering diagnostic.");
        try
        {
            var source = draw.Binding;
            var captured = owner.AcquirePass();
            captured.Capture(lease, draw.Mesh, source);
            //A clip may override time explicitly (for example with context.Time to pause animation).
            float t = Time.unscaledTime;
            captured.Apply(DrawProperty.Vector(TimeId, new(t / 20, t, t * 2, t * 3)));
            if (properties != null)
            {
                if (values.Length < properties.PropertyCount) values = new DrawProperty[properties.PropertyCount];
                int count = properties.CopyTo(values);
                try { for (int i = 0; i < count; i++) captured.Apply(values[i]); }
                finally { Array.Clear(values); }
            }
            //Display-encoded scene data is deliberately sampled without the world-shader sRGB decoder.
            captured.Apply(DrawProperty.Float(SourceSrgbId, 0));
            passes.Add(captured);
        }
        finally { FinalDrawItem.Return(draw); }
    }

    internal void Commit(EfxFrame frame)
    {
        if (finished) throw new ObjectDisposedException(nameof(EfxFrameBuilder));
        if (passes.Count != 0) { lease.Retain(); frame.Leases.Add(lease); frame.Passes.AddRange(passes); }
        passes.Clear(); finished = true;
    }
    public void Dispose() { finished = true; lease = null; passes.Clear(); Array.Clear(values); }
    //The host has drained GPU work; no later EndFrame will run to service deferred destruction.
    internal static void Shutdown() { if (triangle != null) Object.DestroyImmediate(triangle); triangle = null; }
    private static Mesh MakeTriangle()
    {
        var mesh = new Mesh
        {
            name = "RimUnity.ScreenEfx.Fullscreen",
            vertices = new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) },
            //Exported Unity samplers already invert V. Supply Unity's bottom-origin UVs
            //for a positive-height Vulkan viewport so every pass preserves orientation.
            uv = new[] { new Vector2(0, 1), new Vector2(2, 1), new Vector2(0, -1) },
            triangles = new[] { 0, 1, 2 }
        };
        try
        {
            //EngineMesh only queues an upload. PostFx capture runs after the host's normal
            //mesh-upload drain, so this first-use mesh must be uploaded before TryMake.
            //Upload only this immutable triangle once; do not drain unrelated pending meshes.
            var native = mesh.EngineMesh;
            Waylith.EngineApplication.Instance.GraphicsDevice.MeshUploads.Upload(native);
            if (!native.GpuResource.HasBuffers)
                throw new InvalidOperationException("The screen-effect fullscreen mesh upload produced no GPU buffers.");
            return mesh;
        }
        catch { Object.Destroy(mesh); throw; }
    }
}

internal sealed class EfxFrame : IDisposable
{
    //Only fully retired frames enter this bounded pool. Session epochs reject old render-thread returns.
    private static readonly object poolGate = new();
    private static readonly Stack<EfxFrame> pool = new(8);
    private static int poolEpoch;
    private int epoch;
    private readonly List<EfxPass> cachedPasses = new();
    private int usedPasses;
    internal readonly EfxFrameBuilder Builder;
    internal readonly List<EfxPass> Passes = new();
    internal readonly List<EfxLease> Leases = new();
    private int references = 1, disposed;
    internal EfxFrame() { epoch = poolEpoch; Builder = new EfxFrameBuilder(this); }
    internal static EfxFrame Rent()
    {
        lock (poolGate)
        {
            var frame = pool.Count == 0 ? new EfxFrame() : pool.Pop();
            frame.references = 1; frame.disposed = 0; frame.epoch = poolEpoch;
            return frame;
        }
    }
    internal static void ClearPool() { lock (poolGate) { poolEpoch++; pool.Clear(); } }
    internal static int CachedFrameCount { get { lock (poolGate) return pool.Count; } }
    internal EfxPass AcquirePass()
    {
        if (usedPasses == cachedPasses.Count) cachedPasses.Add(new EfxPass());
        return cachedPasses[usedPasses++];
    }
    internal void Retain() => Interlocked.Increment(ref references);
    internal void Drop()
    {
        if (Interlocked.Decrement(ref references) != 0) return;
        foreach (var lease in Leases) lease.Drop();
        Leases.Clear(); Passes.Clear(); Builder.Dispose();
        int bytes = 0;
        foreach (var pass in cachedPasses) { pass.ClearReferences(); bytes += pass.BufferBytes; }
        usedPasses = 0;
        lock (poolGate)
            if (epoch == poolEpoch && pool.Count < 8 && cachedPasses.Count <= 64 && bytes <= 256 * 1024 && Builder.CanCache) pool.Push(this);
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) Drop(); }
}

internal sealed class EfxPass
{
    private delegate FinalDrawItem RentDraw(System.Numerics.Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<NativeTexture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private static readonly RentDraw Rent = AccessTools.Method(typeof(FinalDrawItem), "Rent").CreateDelegate<RentDraw>();
    private static int generation;
    internal static int NextGeneration() => Interlocked.Increment(ref generation);
    internal EfxLease Lease { get; private set; }
    internal MeshDrawGeometry Mesh { get; private set; }
    internal MaterialBinding Binding { get; private set; }
    private MaterialStorageArray[] storage = Array.Empty<MaterialStorageArray>();
    private byte[][] buffers = Array.Empty<byte[]>();
    internal int BufferBytes { get { int size = 0; foreach (var bytes in buffers) size += bytes?.Length ?? 0; return size; } }
    internal EfxPass() { }
    internal EfxPass(EfxLease lease, MeshDrawGeometry mesh, MaterialBinding binding) { Lease = lease; Mesh = mesh; Binding = binding; }

    internal void Capture(EfxLease lease, MeshDrawGeometry mesh, MaterialBinding source)
    {
        Lease = lease; Mesh = mesh;
        var shader = LinearShaderVariants.TryGet(source.Shader, out var raw) ? raw : source.Shader;
        CopyFrom(source, shader, true);
    }

    private void CopyFrom(MaterialBinding source, Waylith.Rendering.Shader shader, bool capture)
    {
        var variant = source.ShaderVariant;
        var key = new ShaderVariantKey(shader, variant.DynamicKeywordKeyId, variant.DynamicKeywordKey, variant.UseTexture);
        var state = new PipelineState(BlendState.Opaque, new DepthState(false, false), FaceCull.None);
        if (Binding == null || Binding.Id != source.Id || Binding.MaterialId != source.MaterialId || Binding.Shader != shader || Binding.ShaderVariant != key)
            Binding = new MaterialBinding(source.Id, source.MaterialId, source.MaterialVersion, source.DebugName,
                shader, source.MaterialLayout, source.Uniforms, source.Resources, source.RenderQueue, key, state,
                Waylith.Rendering.TextureWrapMode.Clamp, false);
        else
            Binding.Update(source.MaterialVersion, source.DebugName, shader, source.MaterialLayout, source.Uniforms,
                source.Resources, source.RenderQueue, key, state, Waylith.Rendering.TextureWrapMode.Clamp, false);
        var original = source.StorageArrays;
        if (storage.Length != original.Length) { storage = new MaterialStorageArray[original.Length]; buffers = new byte[original.Length][]; }
        for (int i = 0; i < original.Length; i++)
        {
            var item = original[i];
            //Late binding only writes dimensions. Other immutable blocks can be borrowed from the retained frame.
            if (!capture && !HasDimensions(source.MaterialLayout, item.PropertyId)) { storage[i] = item; continue; }
            if (buffers[i]?.Length != item.Bytes.Length) buffers[i] = new byte[item.Bytes.Length];
            item.Bytes.CopyTo(buffers[i], 0);
            storage[i] = new MaterialStorageArray(item.PropertyId, NextGeneration(), buffers[i]);
        }
        Binding.UpdateStorageArrays(storage);
    }

    private static readonly int ScreenId = Shader.PropertyToID("_ScreenParams"), TexelId = Shader.PropertyToID("_MainTex_TexelSize"), MainId = Shader.PropertyToID("_MainTex");
    private static bool HasDimensions(MaterialLayout layout, int id)
    {
        foreach (var block in layout.UniformBlocks)
            if (block.PropertyId == id)
                foreach (var slot in block.Members) if (slot.PropertyId == ScreenId || slot.PropertyId == TexelId) return true;
        return false;
    }

    internal void ClearReferences()
    {
        Lease = null; Mesh = null;
        if (Binding == null) return;
        Binding.Update(Binding.MaterialVersion, Binding.DebugName, Binding.Shader, Binding.MaterialLayout, default, default,
            Binding.RenderQueue, Binding.ShaderVariant, Binding.PipelineState, Binding.WrapModeOverride, false);
        //Drop borrowed arrays before the source frame is reused, retain only this pass's own byte buffers.
        for (int i = 0; i < storage.Length; i++) storage[i] = default;
    }

    internal FinalDrawItem CreateDraw() => Rent(System.Numerics.Matrix4x4.Identity, 0, Mesh, Binding, true,
        in Binding.Uniforms, in Binding.Resources, 0, Binding.RenderQueue, null, 0, 0, null);

    internal void Apply(DrawProperty property)
    {
        var uniforms = Binding.Uniforms;
        var resources = Binding.Resources;
        bool changed = false;
        foreach (var slot in Binding.MaterialLayout.TextureSlots)
            if (slot.Kind != MaterialTextureKind.Sampler && slot.PropertyId == property.PropertyId && property.Type == DrawPropertyType.Texture)
            {
                resources.Set(slot.ResourceSlot, (NativeTexture)property.ReferenceValue);
                changed = true;
            }
        foreach (var slot in Binding.MaterialLayout.UniformSlots)
            if (slot.PropertyId == property.PropertyId) { uniforms.Write(slot.Offset, Value(slot, property)); changed = true; }
        foreach (var block in Binding.MaterialLayout.UniformBlocks)
            foreach (var slot in block.Members)
                if (slot.PropertyId == property.PropertyId)
                    foreach (var array in Binding.StorageArrays)
                        if (array.PropertyId == block.PropertyId)
                        {
                            MaterialUniformBytes bytes = default;
                            bytes.Write(0, Value(slot, property));
                            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in bytes, 1))
                                .Slice(0, MaterialLayout.GetStd140Size(slot.Type)).CopyTo(array.Bytes.AsSpan(slot.Offset));
                        }
        //Storage blocks already have a fresh generation. Re-registering the same pipeline for byte-only writes is unnecessary.
        if (changed)
            Binding.Update(Binding.MaterialVersion, Binding.DebugName, Binding.Shader, Binding.MaterialLayout,
                uniforms, resources, Binding.RenderQueue, Binding.ShaderVariant, Binding.PipelineState, Binding.WrapModeOverride, false);
    }

    private MaterialUniformValue Value(MaterialUniformSlot slot, DrawProperty property)
    {
        var vector = property.VectorValue;
        if (slot.Type == MaterialUniformType.TextureST)
        {
            if (Binding.Shader.FlipTextureST) { vector.Y = 1 - vector.Y; vector.W = -vector.W; }
            if (Binding.Shader.PrecompiledStages?.ScaleOffsetTextureST == true) vector = new(vector.Z, vector.W, vector.X, vector.Y);
        }
        return slot.Type switch
        {
            MaterialUniformType.Float => MaterialUniformValue.FromFloat(property.FloatValue),
            MaterialUniformType.Int => MaterialUniformValue.FromInt(property.IntValue),
            MaterialUniformType.Matrix3x3 or MaterialUniformType.Matrix4x4 => MaterialUniformValue.FromMatrix(slot.Type, property.MatrixValue),
            _ => MaterialUniformValue.FromVector(slot.Type, vector)
        };
    }

    //Only invoked on render-owned copies; the captured instruction remains immutable.
    internal EfxPass BindInput(NativeTexture input, int width, int height, EfxPass result = null)
    {
        result ??= new EfxPass();
        if (ReferenceEquals(result, this)) throw new ArgumentException("Late binding must not modify the captured pass.", nameof(result));
        result.Lease = Lease; result.Mesh = Mesh;
        result.CopyFrom(Binding, Binding.Shader, false);
        result.Apply(DrawProperty.Texture(MainId, input));
        result.Apply(DrawProperty.Vector(TexelId, new(1f / width, 1f / height, width, height)));
        result.Apply(DrawProperty.Vector(ScreenId, new(width, height, 1 + 1f / width, 1 + 1f / height)));
        return result;
    }
}
