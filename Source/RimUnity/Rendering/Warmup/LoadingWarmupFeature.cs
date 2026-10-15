using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith;
using Waylith.Graphics;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//No draws, meshes or texture uploads are submitted. Only actual host pipeline caches are populated.
internal sealed class LoadingWarmupFeature : IRenderFeature
{
    private readonly EngineApplication app;
    private readonly IRenderPass[] passes;
    private LoadingWarmupWork work;
    private NativeShader[] shaders = Array.Empty<NativeShader>();
    private VertexLayout[] layouts = Array.Empty<VertexLayout>();
    private bool bloom;
    private string keywords;
    internal LoadingWarmupWork Work => Volatile.Read(ref work);
    internal int CompletedCount { get; private set; }
    public bool IsActive => Work is { Finished: false };
    public IReadOnlyList<IRenderPass> Passes => passes;

    private static readonly AccessTools.FieldRef<MeshBatchRenderer, MeshPipelineCache> Cache = AccessTools.FieldRefAccess<MeshBatchRenderer, MeshPipelineCache>("pipelines");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> FrameLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("frameDescriptorSetLayout");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> GlobalLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("globalTextureDescriptorSetLayout");

    internal LoadingWarmupFeature(EngineApplication app) { this.app = app; passes = new IRenderPass[] { new Pass(this) }; }
    internal void Request(NativeShader[] programs, VertexLayout[] vertexLayouts, bool warmBloom, string globalKeywords)
    {
        shaders = programs; layouts = vertexLayouts; bloom = warmBloom; keywords = globalKeywords; CompletedCount = 0;
        Volatile.Write(ref work, new LoadingWarmupWork());
    }
    internal void Release(LoadingWarmupWork request)
    {
        if (!ReferenceEquals(Work, request)) return;
        Volatile.Write(ref work, null); shaders = Array.Empty<NativeShader>(); layouts = Array.Empty<VertexLayout>();
    }
    public object CaptureFrame(in FrameTiming timing)
    {
        if (Work is { Finished: false }) Verse.LongEventHandler.SetCurrentEventText("RimUnity：预热地图渲染管线");
        return Work?.Payload;
    }
    public void OnRenderTargetRebuilt(IRenderTarget target) { }

    private sealed class Pass : IRenderPass
    {
        private readonly LoadingWarmupFeature owner;
        internal Pass(LoadingWarmupFeature owner) => this.owner = owner;
        public byte PassId => 0;
        public PassAttachment Attachment => null;
        public bool PreBackbuffer => true;
        public void OnRenderTargetRebuilt(IRenderTarget target) { }
        public void Dispose() { }
        public void Record(in FrameContext ctx, object payload)
        {
            if (payload is not LoadingWarmupWork.FramePayload frame) return;
            var context = ctx;
            frame.Work.Run(() => owner.Warm(in context));
        }
    }

    private void Warm(in FrameContext ctx)
    {
        var mapFeature = app.FrameRenderer.Features.FirstOrDefault(f => f.GetType().FullName == "RimWorldWaylith.Rendering.MapRenderFeature")
            ?? throw new InvalidOperationException("宿主地图渲染 Feature 不存在。");
        var main = mapFeature.Passes.First();
        object sceneTargets = RequiredField(main, "sceneTargets");
        var attachment = (PassAttachment)AccessTools.Property(sceneTargets.GetType(), "Attachment").GetValue(sceneTargets)
            ?? throw new InvalidOperationException("地图颜色附件尚未准备。");
        var sceneContext = attachment.MakeFrameContext(in ctx);
        var scene = HdrMapHooks.WarmupScene(sceneTargets, ctx.SubmissionFrameIndex);
        var regular = (MeshBatchRenderer)RequiredField(main, "renderer");
        var hdr = HdrMapHooks.WarmupTarget(main).PrepareWarmup(in ctx, scene);
        var targets = new List<(MeshBatchRenderer renderer, VkRenderPass pass, VkSampleCountFlags samples, bool linear, bool readOnly)> {
            (regular, sceneContext.RenderPass, sceneContext.SampleCount, false, false),
            (hdr.renderer, hdr.pass, VkSampleCountFlags._1Bit, true, false)
        };
        if (bloom)
        {
            var post = app.FrameRenderer.Features.FirstOrDefault(f => f.GetType().FullName == "RimWorldWaylith.Rendering.PostFxFeature");
            if (post != null)
            {
                var composite = (Waylith.Rendering.PostFx.CompositePass)RequiredField(post, "composite");
                var layers = BloomHooks.Get(composite).PrepareWarmup(in ctx, scene);
                targets.Add((layers.renderer, layers.pass, VkSampleCountFlags._1Bit, true, true));
            }
        }
        int failures = 0;
        var firstErrors = new List<string>();
        foreach (var target in targets)
        {
            var visited = new HashSet<int>();
            foreach (var original in shaders)
            {
                var shader = original;
                if (target.linear && LinearShaderVariants.TryGet(original, out var linear)) shader = linear;
                if (!visited.Add(shader.Id) || shader.HasErrors) continue;
                var state = shader.PipelineState;
                if (target.readOnly) state = state with { Depth = state.Depth with { Write = false },
                    Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
                var variant = new ShaderVariantKey(shader, ShaderKeywordIdMaker.GetOrCreateId(keywords), keywords, shader.UseTexture);
                var binding = new MaterialBinding(0, 0, 0, "RimUnity.Warmup." + shader.Key, shader, shader.MaterialLayout,
                    default, default, shader.DefaultRenderQueue, variant, state, null, false);
                foreach (var layout in layouts)
                {
                    //Do not manufacture invalid shader/mesh combinations merely to warm a cache.
                    if (shader.VertexInputLayout.Slots.Any(slot => slot.required && !layout.HasAttribute(slot.semantic))) continue;
                    try
                    {
                        var frameLayout = FrameLayout(target.renderer); var globalLayout = GlobalLayout(target.renderer);
                        var pipeline = Cache(target.renderer).GetOrCreatePipeline(in frameLayout, in globalLayout, binding, layout,
                            MeshTopology.Triangles, in state, target.pass, target.samples, 1, HdrDrawFrame.MapPass, "RimUnity.Warmup");
                        if (pipeline.IsNull()) throw new InvalidOperationException("管线被宿主拒绝。");
                        CompletedCount++;
                    }
                    catch (Exception error)
                    {
                        failures++;
                        if (firstErrors.Count < 8) firstErrors.Add(shader.Key + " / layout=" + layout.Id + "：" + error.Message);
                    }
                }
            }
        }
        if (failures != 0) throw new InvalidOperationException(failures + " 个管线预热失败；已完成 " + CompletedCount + " 个。\n" + string.Join("\n", firstErrors));
    }

    private static object RequiredField(object owner, string name)
        => (AccessTools.Field(owner.GetType(), name) ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner);
}
