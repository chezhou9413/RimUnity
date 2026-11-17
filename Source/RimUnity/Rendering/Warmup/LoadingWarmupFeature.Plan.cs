using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith.Graphics;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//计划只保留宿主资源引用；不跨帧保存命令缓冲区或 FrameContext。
internal sealed partial class LoadingWarmupFeature
{
    private sealed record PipelineRequest(MeshBatchRenderer Renderer, VkRenderPass Pass, VkSampleCountFlags Samples,
        MaterialBinding Binding, VertexLayout Layout);

    //先收集实际可用的组合，以便加载窗口显示准确的任务总数。
    private void Prepare(in FrameContext ctx)
    {
        requests = new List<PipelineRequest>();
        if (shaders.Length != 0)
        {
            var main = MapPass();
            object sceneTargets = RequiredField(main, "sceneTargets");
            var attachment = (PassAttachment)AccessTools.Property(sceneTargets.GetType(), "Attachment").GetValue(sceneTargets)
                ?? throw new InvalidOperationException("地图颜色附件尚未准备。");
            var sceneContext = attachment.MakeFrameContext(in ctx);
            var scene = HdrMapHooks.WarmupScene(sceneTargets, ctx.SubmissionFrameIndex);
            var mode = RimUnityMod.Bloom?.mapHdrMode ?? MapHdrMode.None;
            var targets = new List<(MeshBatchRenderer renderer, VkRenderPass pass, VkSampleCountFlags samples, bool linear, bool readOnly, bool light)> {
                ((MeshBatchRenderer)RequiredField(main, "renderer"), sceneContext.RenderPass, sceneContext.SampleCount, false, false, false)
            };
            if (mode == MapHdrMode.Legacy)
            {
                var hdr = HdrMapHooks.WarmupTarget(main).PrepareWarmup(in ctx, scene);
                targets.Add((hdr.renderer, hdr.pass, VkSampleCountFlags._1Bit, true, false, false));
            }
            else if (mode == MapHdrMode.Experiment)
            {
                var light = HdrMapHooks.WarmupLightTarget(main).PrepareWarmup(in ctx, scene);
                targets.Add((light.renderer, light.pass, VkSampleCountFlags._1Bit, true, true, true));
            }
            if (bloom && Composite() is { } composite)
            {
                var pipeline = BloomHooks.Get(composite);
                if (pipeline != null)
                {
                    var layers = pipeline.PrepareWarmup(in ctx, scene);
                    targets.Add((layers.renderer, layers.pass, VkSampleCountFlags._1Bit, true, true, false));
                }
            }
            foreach (var target in targets)
            {
                var visited = new HashSet<int>();
                foreach (var original in shaders)
                {
                    var shader = original;
                    if (target.linear && LinearShaderVariants.TryGet(original, out var linear)) shader = linear;
                    if (!visited.Add(shader.Id) || shader.HasErrors) continue;
                    var state = shader.PipelineState;
                    if (target.light)
                    {
                        if (!HdrLightBlend.TryAdapt(state, out state)) continue;
                    }
                    else if (target.readOnly) state = state with { Depth = state.Depth with { Write = false },
                        Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
                    var variant = new ShaderVariantKey(shader, ShaderKeywordIdMaker.GetOrCreateId(keywords), keywords, shader.UseTexture);
                    var binding = new MaterialBinding(0, 0, 0, "RimUnity.Warmup." + shader.Key, shader, shader.MaterialLayout,
                        default, default, shader.DefaultRenderQueue, variant, state, null, false);
                    foreach (var layout in layouts)
                    {
                        //不为缺失必需顶点属性的网格制造无效管线。
                        if (shader.VertexInputLayout.Slots.Any(slot => slot.required && !layout.HasAttribute(slot.semantic))) continue;
                        requests.Add(new(target.renderer, target.pass, target.samples, binding, layout));
                    }
                }
            }
        }
        System.Threading.Volatile.Write(ref totalCount, requests.Count + (effects == null ? 0 : 1));
    }

    //特效缓存仍通过现有后处理实例预热。
    private int WarmEffects(in FrameContext ctx)
    {
        var composite = Composite();
        if (composite == null) return 0;
        var pipeline = BloomHooks.Get(composite);
        if (pipeline == null) return 0;
        var scene = HdrMapHooks.WarmupScene(RequiredField(MapPass(), "sceneTargets"), ctx.SubmissionFrameIndex);
        return pipeline.PrepareEfxWarmup(in ctx, scene, effects);
    }

    //地图主通道拥有预热所需的附件和原生渲染器。
    private IRenderPass MapPass() => app.FrameRenderer.Features.FirstOrDefault(f => f.GetType().FullName == "RimWorldWaylith.Rendering.MapRenderFeature")?.Passes.First()
        ?? throw new InvalidOperationException("宿主地图渲染 Feature 不存在。");

    //后处理可能在无对应场景时缺席，沿用现有预热范围。
    private Waylith.Rendering.PostFx.CompositePass Composite()
    {
        var post = app.FrameRenderer.Features.FirstOrDefault(f => f.GetType().FullName == "RimWorldWaylith.Rendering.PostFxFeature");
        return post == null ? null : (Waylith.Rendering.PostFx.CompositePass)RequiredField(post, "composite");
    }

    //宿主字段变更时明确报错，避免使用无效缓存继续预热。
    private static object RequiredField(object owner, string name)
        => (AccessTools.Field(owner.GetType(), name) ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner);
}
