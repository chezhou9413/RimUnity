// Recording doubles for the host API. They deliberately reject clearing without values,
// destroying referenced framebuffers and resuming a renderer with stale binding state.
using System.Reflection;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch() { } public HarmonyPatch(Type type, string name) { } }
    public static class AccessTools
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static Type TypeByName(string name) => typeof(AccessTools).Assembly.GetType(name);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, All);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name, All);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, All);
    }
}
namespace Brutal.Numerics { public struct float4 { public float4(float x, float y, float z, float w) { } } }
namespace Brutal.VulkanApi
{
    public enum VkFormat { Undefined, B8G8R8A8UNorm, R16G16B16A16SFloat, D24UNormS8UInt, D32SFloatS8UInt }
    public enum VkImageLayout { Undefined, ShaderReadOnlyOptimal, DepthStencilAttachmentOptimal, ColorAttachmentOptimal }
    public enum VkSampleCountFlags { _1Bit }
    public enum VkAttachmentLoadOp { Load, Clear, DontCare }
    public enum VkAttachmentStoreOp { Store, DontCare }
    public enum VkSubpassContents { Inline }
    public enum VkPipelineBindPoint { Graphics }
    public enum VkDependencyFlags { None }
    [Flags] public enum VkAccessFlags { None=0, DepthStencilAttachmentReadBit=1, DepthStencilAttachmentWriteBit=2, ShaderReadBit=4, ColorAttachmentWriteBit=8, TransferWriteBit=16, ColorAttachmentReadBit=32 }
    [Flags] public enum VkPipelineStageFlags { EarlyFragmentTestsBit=1, LateFragmentTestsBit=2, FragmentShaderBit=4, ColorAttachmentOutputBit=8, TransferBit=16 }
    [Flags] public enum VkImageAspectFlags { ColorBit=1, DepthBit=2, StencilBit=4 }
    public record struct VkImageView(int Id) { public IntPtr VkHandle => new(Id); }
    public record struct VkFramebuffer(int Id) { public bool IsNull() => Id == 0; }
    public record struct VkRenderPass(int Id) { public bool IsNull() => Id == 0; }
    public struct VkExtent2D(int width, int height) { public int Width = width, Height = height; }
    public struct VkRect2D { public VkRect2D(VkExtent2D extent) { } }
    public struct VkViewport { public float Width, Height, MinDepth, MaxDepth; }
    public struct VkClearColorValue { public Brutal.Numerics.float4 Float32; }
    public struct VkClearDepthStencilValue { public float Depth; public int Stencil; }
    public struct VkClearValue
    {
        public static implicit operator VkClearValue(VkClearColorValue v) => new();
        public static implicit operator VkClearValue(VkClearDepthStencilValue v) => new();
    }
    public unsafe struct VkRenderPassBeginInfo { public VkRenderPass RenderPass; public VkFramebuffer Framebuffer; public VkRect2D RenderArea; public int ClearValueCount; public VkClearValue* ClearValues; }
    public unsafe struct VkFramebufferCreateInfo { public VkRenderPass RenderPass; public int AttachmentCount, Width, Height, Layers; public VkImageView* Attachments; }
    public struct VkAttachmentDescription { public VkFormat Format; public VkSampleCountFlags Samples; public VkAttachmentLoadOp LoadOp, StencilLoadOp; public VkAttachmentStoreOp StoreOp, StencilStoreOp; public VkImageLayout InitialLayout, FinalLayout; }
    public struct VkAttachmentReference { public int Attachment; public VkImageLayout Layout; }
    public unsafe struct VkSubpassDescription { public VkPipelineBindPoint PipelineBindPoint; public int ColorAttachmentCount; public VkAttachmentReference* ColorAttachments; public VkAttachmentReference* DepthStencilAttachment; }
    public struct VkSubpassDependency { public int SrcSubpass, DstSubpass; public VkPipelineStageFlags SrcStageMask, DstStageMask; public VkAccessFlags SrcAccessMask, DstAccessMask; }
    public unsafe struct VkRenderPassCreateInfo { public int AttachmentCount, SubpassCount, DependencyCount; public VkAttachmentDescription* Attachments; public VkSubpassDescription* Subpasses; public VkSubpassDependency* Dependencies; }
    public struct VkImageSubresourceRange { public VkImageAspectFlags AspectMask; public int LevelCount, LayerCount; }
    public struct VkImageMemoryBarrier { public int Image, SrcQueueFamilyIndex, DstQueueFamilyIndex; public VkImageLayout OldLayout, NewLayout; public VkAccessFlags SrcAccessMask, DstAccessMask; public VkImageSubresourceRange SubresourceRange; }
    public struct VkMemoryBarrier { } public struct VkBufferMemoryBarrier { }
    public sealed class CommandBuffer(DeviceEx device)
    {
        internal RenderTexture Destination;
        internal object BoundOwner;
        internal int Clears;
        internal readonly List<VkFramebuffer> CompositeFramebuffers = new();
        internal readonly List<FinalDrawItem> DrawOrder = new();
        internal readonly List<MeshBatchRenderer> DrawOwners = new();
        internal readonly List<PostEffectPass> CompositeEffects = new();
        internal readonly List<VkImageMemoryBarrier> ImageBarriers = new();
        public unsafe void BeginRenderPass(in VkRenderPassBeginInfo begin, VkSubpassContents contents)
        {
            Destination = RenderTexture.Views[device.Views[begin.Framebuffer]];
            device.Referenced.Add(begin.Framebuffer);
            if (device.Loads[begin.RenderPass] == VkAttachmentLoadOp.Clear)
            {
                if (begin.ClearValueCount == 0) throw new InvalidOperationException("Clear attachment reopened without clear values");
                Destination.Value = 0; Clears++;
            }
            if (device.CompositePasses.Contains(begin.RenderPass)) CompositeFramebuffers.Add(begin.Framebuffer);
        }
        public void EndRenderPass() { }
        public void SetViewport(int index, ReadOnlySpan<VkViewport> values) { }
        public void SetScissor(int index, ReadOnlySpan<VkRect2D> values) { }
        public void PipelineBarrier(VkPipelineStageFlags src, VkPipelineStageFlags dst, VkDependencyFlags flags, ReadOnlySpan<VkMemoryBarrier> memory, ReadOnlySpan<VkBufferMemoryBarrier> buffers, ReadOnlySpan<VkImageMemoryBarrier> images)
        { foreach (var barrier in images) ImageBarriers.Add(barrier); }
    }
}
namespace Brutal.VulkanApi.Abstractions
{
    public sealed class DeviceEx
    {
        private int serial;
        public object VkAllocator => null;
        internal readonly Dictionary<VkFramebuffer, VkImageView> Views = new();
        internal readonly Dictionary<VkRenderPass, VkAttachmentLoadOp> Loads = new();
        internal readonly HashSet<VkRenderPass> CompositePasses = new();
        internal readonly List<VkAttachmentDescription> DepthAttachments = new();
        internal readonly HashSet<VkFramebuffer> Created = new(), Destroyed = new(), Referenced = new();
        internal readonly HashSet<(PostEffectPass Effect, int Slot)> ReferencedDescriptorPools = new();
        internal readonly HashSet<PostEffectPass> CreatedEffects = new(), DisposedEffects = new();
        internal VkRenderPass Pass(VkAttachmentLoadOp load) { var pass = new VkRenderPass(++serial); Loads.Add(pass, load); return pass; }
        internal VkFramebuffer Framebuffer(VkImageView view, bool owned = false)
        { var fb = new VkFramebuffer(++serial); Views.Add(fb, view); if (owned) Created.Add(fb); return fb; }
        public unsafe VkRenderPass CreateRenderPass(VkRenderPassCreateInfo info, object allocator)
        {
            var pass = Pass(info.Attachments[0].LoadOp);
            if (info.Attachments[0].Format != VkFormat.R16G16B16A16SFloat) CompositePasses.Add(pass);
            if (info.AttachmentCount > 1) DepthAttachments.Add(info.Attachments[1]);
            return pass;
        }
        public unsafe VkFramebuffer CreateFramebuffer(VkFramebufferCreateInfo info, object allocator) => Framebuffer(info.Attachments[0], true);
        public void DestroyFramebuffer(VkFramebuffer fb, object allocator)
        {
            if (Referenced.Contains(fb)) throw new InvalidOperationException("Destroyed framebuffer referenced by recorded commands");
            if (!Destroyed.Add(fb)) throw new InvalidOperationException("Framebuffer destroyed twice");
        }
        public void DestroyRenderPass(VkRenderPass pass, object allocator) { }
        internal void RetireDescriptorSlot(int slot) => ReferencedDescriptorPools.RemoveWhere(p => p.Slot == slot);
        internal void FinishRecording() { Referenced.Clear(); ReferencedDescriptorPools.Clear(); }
    }
}
namespace Waylith.Graphics
{
    public sealed class GpuTextureAllocation(RenderTexture owner)
    {
        internal RenderTexture Owner = owner;
        public VkImageLayout CurrentLayout;
        public VkFormat Format;
        public VkImageView ImageView => Owner.ColorView;
        public int Image => ImageView.Id;
        public void SetMipLayout(int mip, VkImageLayout layout) => CurrentLayout = layout;
    }
    public sealed class GraphicsDevice(DeviceEx device)
    { public DeviceEx Device => device; public void WaitIdleAndRun(Action action) => action(); }
}
namespace Waylith.Rendering
{
    public interface IRenderTarget { }
    public interface IRenderContext : IRenderTarget { int FramesInFlight { get; } int SubmissionFrameIndex { get; } DeviceEx Device { get; } }
    internal sealed class TestContext : IRenderContext
    { public int FramesInFlight => 2; public int SubmissionFrameIndex { get; set; } public DeviceEx Device { get; } = new(); }
    public sealed class FrameContext
    {
        public IRenderContext RenderContext; public CommandBuffer CommandBuffer; public VkRenderPass RenderPass; public VkFramebuffer Framebuffer;
        public VkExtent2D Extent; public VkFormat ColorFormat, DepthFormat; public VkSampleCountFlags SampleCount;
        public object Resources => null; public int SubmissionFrameIndex => RenderContext.SubmissionFrameIndex;
        public DeviceEx Device => RenderContext.Device; public Waylith.Graphics.GraphicsDevice GraphicsDevice => new(Device);
        public FrameContext(IRenderContext context, CommandBuffer command, RenderTexture scene)
        { RenderContext=context; CommandBuffer=command; Extent=scene.Extent; ColorFormat=scene.ColorFormat; DepthFormat=scene.DepthFormat; }
        public FrameContext(IRenderContext context, object resources, CommandBuffer command, VkRenderPass pass, VkFramebuffer framebuffer, VkExtent2D extent, VkSampleCountFlags samples, VkFormat color, VkFormat depth)
        { RenderContext=context; CommandBuffer=command; RenderPass=pass; Framebuffer=framebuffer; Extent=extent; SampleCount=samples; ColorFormat=color; DepthFormat=depth; }
    }
    public sealed class RenderTexture : IDisposable
    {
        static int next; internal static readonly Dictionary<VkImageView, RenderTexture> Views = new();
        public readonly VkImageView ColorView = new(++next);
        public int Width, Height; public VkFormat ColorFormat, DepthFormat;
        public Waylith.Graphics.GpuTextureAllocation Color, Depth;
        public bool HasDepth => Depth != null; public VkExtent2D Extent => new(Width, Height);
        internal float Value;
        public RenderTexture(int width, int height, VkFormat color, string name = null)
        { Width=width; Height=height; ColorFormat=color; Color=new(this); Views.Add(ColorView, this); }
        public void Dispose() { }
    }
    public sealed class PassAttachment(Func<int, RenderTexture> current, bool clear)
    {
        public FrameContext MakeFrameContext(in FrameContext ctx)
        {
            var scene = current(ctx.SubmissionFrameIndex);
            return new(ctx.RenderContext, null, ctx.CommandBuffer, ctx.Device.Pass(clear ? VkAttachmentLoadOp.Clear : VkAttachmentLoadOp.Load),
                ctx.Device.Framebuffer(scene.ColorView), scene.Extent, VkSampleCountFlags._1Bit, scene.ColorFormat, scene.DepthFormat);
        }
    }
    public enum BlendOp { Add }
    public enum BlendFactor { Zero, One, SrcAlpha }
    public enum StencilOp { Keep, Replace }
    public record struct BlendState(bool Enabled, BlendFactor SrcColor, BlendFactor DstColor, BlendOp ColorOp, BlendFactor SrcAlpha, BlendFactor DstAlpha, BlendOp AlphaOp);
    public record struct DepthState(bool Write);
    public record struct StencilState(int WriteMask, StencilOp PassOp, StencilOp FailOp, StencilOp DepthFailOp);
    public record struct PipelineState(BlendState Blend, DepthState Depth, StencilState Stencil);
    public sealed class Shader { internal bool Linear; public string Key = "TestShader"; public bool HasErrors; public SceneGrabPoint SceneGrab; }
    public sealed class SceneGrabPoint { public int Queue; }
    public sealed class MaterialBinding
    {
        private static int next; public int MaterialId = ++next; public int Id => MaterialId; public int MaterialVersion = 1;
        public string DebugName = "TestMaterial"; public Shader Shader = new(); public PipelineState PipelineState;
    }
    public sealed class FinalDrawItem
    {
        public byte PassId = 1; public int RenderQueue; public int SortKey;
        public int Layer; public bool HasOverride;
        public MaterialBinding Binding = new(); internal float Value; internal bool Multiply, Disposed; internal int DrawCount;
        internal float Alpha = -1;
        public FinalDrawItem(int queue, float value, bool light, bool multiply)
        {
            RenderQueue=SortKey=queue; Value=value; Multiply=multiply; Binding.Shader.Linear=light;
            Binding.PipelineState=new(new(light, BlendFactor.One, BlendFactor.One, BlendOp.Add, BlendFactor.Zero, BlendFactor.One, BlendOp.Add), default, default);
        }
    }
    public sealed class MeshDrawFrame(int frame) : IDisposable
    {
        public int FrameNumber = frame; public bool PreserveSceneColor;
        public System.Numerics.Vector4 BackgroundColor;
        public List<FinalDrawItem> Draws = new(), PersistentDraws = new();
        public static List<FinalDrawItem> RentList() => new();
        public void Dispose() { foreach (var draw in Draws.Concat(PersistentDraws)) draw.Disposed=true; }
    }
    public sealed class MeshBatchRenderer : IDisposable
    {
        private bool cached; internal int Invalidations; internal bool ThrowOnDraw;
        public MeshBatchRenderer() { } public MeshBatchRenderer(Waylith.Graphics.GraphicsDevice device, IRenderContext context) { }
        public void InvalidateCommandBufferBindings() { cached=false; Invalidations++; }
        public void OnRenderContextRebuilt(IRenderContext context) { cached=false; }
        public void RecordPass(in FrameContext ctx, MeshDrawFrame frame, byte pass, object view=null, string diagnosticPrefix=null, int queueMin=int.MinValue, int queueMax=int.MaxValue, int colors=-1, bool mergeBySortKey=false)
        {
            if (ThrowOnDraw) throw new InvalidOperationException("Injected draw failure");
            if (cached && !ReferenceEquals(ctx.CommandBuffer.BoundOwner, this)) throw new InvalidOperationException("Host resumed with stale binding cache");
            cached=true; ctx.CommandBuffer.BoundOwner=this;
            foreach (var draw in frame.Draws.Concat(frame.PersistentDraws).OrderBy(d => d.SortKey))
                if (draw.RenderQueue >= queueMin && draw.RenderQueue < queueMax)
                {
                    draw.DrawCount++;
                    ctx.CommandBuffer.DrawOrder.Add(draw);
                    ctx.CommandBuffer.DrawOwners.Add(this);
                    if (draw.Multiply) ctx.CommandBuffer.Destination.Value *= draw.Value;
                    else if (draw.Alpha >= 0) ctx.CommandBuffer.Destination.Value = draw.Value * draw.Alpha + ctx.CommandBuffer.Destination.Value * (1 - draw.Alpha);
                    else if (draw.Binding.Shader.Linear) ctx.CommandBuffer.Destination.Value += draw.Value;
                    else ctx.CommandBuffer.Destination.Value = draw.Value;
                }
        }
        public void Dispose() { }
    }
}
namespace Waylith.Rendering.PostFx
{
    public class PostEffectPass : IDisposable
    {
        private readonly RenderTexture[] inputs = new RenderTexture[2];
        private DeviceEx device;
        public PostEffectPass(string name, string path, int[] bindings, BlendState blend, int pushConstantSize, bool writeDepth) { }
        private void EnsureInline(in FrameContext ctx, VkRenderPass pass, VkSampleCountFlags samples)
        { if (device == null) { device=ctx.Device; device.CreatedEffects.Add(this); } }
        public void SetInput(int index, RenderTexture texture) => inputs[index]=texture;
        public void RecordInline(in FrameContext ctx, VkRenderPass pass, VkSampleCountFlags samples)
        {
            EnsureInline(in ctx, pass, samples);
            // Host RecordInline calls BeginFrame, resetting this instance's slot pool.
            // A reset frees all sets, including those referenced by earlier commands.
            if (!device.ReferencedDescriptorPools.Add((this, ctx.SubmissionFrameIndex)))
                throw new InvalidOperationException("Reset descriptor pool still referenced by recorded commands");
            if (device.DisposedEffects.Contains(this)) throw new InvalidOperationException("Used disposed effect");
            ctx.CommandBuffer.CompositeEffects.Add(this);
            ctx.CommandBuffer.BoundOwner=this;
            ctx.CommandBuffer.Destination.Value=inputs[0].Value+inputs[1].Value;
        }
        public void Dispose()
        {
            if (device == null) return;
            if (device.ReferencedDescriptorPools.Any(p => ReferenceEquals(p.Effect, this)))
                throw new InvalidOperationException("Disposed descriptor pool still referenced by recorded commands");
            if (!device.DisposedEffects.Add(this)) throw new InvalidOperationException("Effect disposed twice");
        }
    }
}
namespace RimWorldWaylith.Rendering
{
    public sealed class SceneColorTargets
    {
        private readonly RenderTexture[] scenes = { new(8,8,VkFormat.B8G8R8A8UNorm), new(8,8,VkFormat.B8G8R8A8UNorm) };
        public PassAttachment Attachment { get; }
        public PassAttachment LoadAttachment { get; set; }
        public SceneColorTargets() { Attachment=new(Current,true); LoadAttachment=new(Current,false); }
        public RenderTexture Current(int slot) => scenes[slot];
    }
    public sealed class MapRenderFeature
    {
        public sealed class MapMainPass
        {
            internal readonly MeshBatchRenderer renderer = new();
            public byte PassId => 1;
            public unsafe void RecordSlice(FrameContext ctx, MeshDrawFrame frame, PassAttachment attachment, bool clear, int min, int max)
            {
                var target=attachment.MakeFrameContext(ctx);
                var begin=new VkRenderPassBeginInfo { RenderPass=target.RenderPass, Framebuffer=target.Framebuffer, ClearValueCount=clear ? 2 : 0 };
                ctx.CommandBuffer.BeginRenderPass(begin,VkSubpassContents.Inline);
                renderer.RecordPass(target,frame,1,null,null,min,max,-1,true);
                ctx.CommandBuffer.EndRenderPass();
            }
        }
    }
}
namespace RimUnity
{
    internal static class RimUnityMod
    { internal static Rendering.BloomSettings Bloom = new(); internal static string ShaderDirectory => "."; }
}
namespace RimUnity.Diagnostics
{
    internal static partial class RenderDebugReport
    {
        internal static bool Collecting;
        internal static readonly RenderTraceBuffer Trace = new();
        internal static bool HdrTracing => Collecting && Trace.Active;
        private static string Error(Exception error) => error.GetBaseException().Message;
        internal static void Frame(MeshDrawFrame frame, bool hdr) { }
        internal static void Stage(string name, string detail) { if (Collecting) Trace.Record(name, detail); }
    }
    internal static class ParticleDebugWindow { internal static void CaptureFinalFrame(MeshDrawFrame source, MeshDrawFrame captured) { } }
}
namespace RimUnity.Rendering
{
    internal enum MapHdrMode { None, Legacy, Experiment }
    internal sealed class BloomSettings { internal MapHdrMode mapHdrMode = MapHdrMode.Experiment; }
    internal static class LinearShaderVariants { internal static bool TryGet(Shader shader, out Shader linear) { linear=shader; return shader.Linear; } }
    internal sealed class HdrDrawFrame : IDisposable
    {
        internal const byte MapPass=1; internal readonly MeshDrawFrame Frame;
        internal readonly Dictionary<FinalDrawItem, FinalDrawItem> LightDraws = new();
        internal static bool Needed(MeshDrawFrame source) => false;
        internal HdrDrawFrame(MeshDrawFrame source, bool lightBuffer=false)
        {
            Frame=Shell(source,MeshDrawFrame.RentList(),MeshDrawFrame.RentList());
            Frame.Draws.AddRange(source.Draws.Concat(source.PersistentDraws).Where(HdrLightBlend.ShouldCapture).OrderBy(d => d.SortKey));
            foreach (var draw in Frame.Draws) LightDraws.Add(draw, draw);
        }
        internal static MeshDrawFrame Shell(MeshDrawFrame source, List<FinalDrawItem> draws, List<FinalDrawItem> persistent)
            => new(source.FrameNumber) { Draws=draws, PersistentDraws=persistent, BackgroundColor=source.BackgroundColor };
        public void Dispose() { }
    }
    internal sealed partial class HdrDrawTarget : IDisposable
    {
        internal HdrDrawTarget(string name) { }
        internal RenderTexture Result => null;
        internal void Begin(in FrameContext ctx, RenderTexture scene, int frame, bool clear) { }
        internal void BindDepth(in FrameContext ctx, RenderTexture scene) { }
        internal void Record(in FrameContext ctx, RenderTexture scene, HdrDrawFrame snapshot, int min, int max) { }
        internal void WriteScene(in FrameContext ctx, RenderTexture scene, int frame) { }
        internal void RebuildTargets(IRenderContext context) { }
        public void Dispose() { }
    }
    internal static class BloomPreviewTransfer
    { internal static void Copy(CommandBuffer cmd, Waylith.Graphics.GpuTextureAllocation source, Waylith.Graphics.GpuTextureAllocation destination, int width, int height) => destination.Owner.Value=source.Owner.Value; }
    internal static class HdrSceneCopy { internal static void Clear(CommandBuffer cmd, RenderTexture texture) => texture.Value=0; }
}
