using System;
using System.IO;
using System.Numerics;
using Brutal.VulkanApi;
using Waylith.Graphics;
using Waylith.Rendering;
using Waylith.Rendering.PostFx;
using NativeRenderTexture = Waylith.Rendering.RenderTexture;

namespace RimUnity.Rendering;

//五级半分辨率金字塔，全部计算留在 GPU；在途帧各自拥有目标，避免覆盖仍被 GPU 采样的图像。
internal sealed class BloomPipeline : IDisposable
{
    private const int Levels = 5;
    private readonly CompositePass scene;
    private readonly PostEffectPass[] down = new PostEffectPass[Levels], up = new PostEffectPass[Levels - 1];
    private readonly PostEffectPass combine, hdrCombine;
    private readonly HdrDrawTarget layers = new("RimUnity.BloomLayers");
    private NativeRenderTexture[][] targets;
    private bool prepared;
    private int width, height;
    internal readonly IRenderPass[] Passes;

    //资源路径在主线程模组初始化时确定；渲染线程仅使用已发布的路径。
    internal BloomPipeline(CompositePass scene)
    {
        this.scene = scene;
        for (int i = 0; i < Levels; i++) down[i] = Make("Down" + i, i == 0 ? "BloomPrefilter" : "BloomDown", new[] { 0 });
        for (int i = 0; i < Levels - 1; i++) up[i] = Make("Up" + i, "BloomUp", new[] { 0, 1 });
        var blend = new BlendState(true, BlendFactor.One, BlendFactor.One, BlendOp.Add, BlendFactor.Zero, BlendFactor.One, BlendOp.Add);
        combine = new PostEffectPass("RimUnity.Bloom.Composite", PathFor("BloomComposite"), new[] { 0, 1 }, blend, 16);
        hdrCombine = Make("HdrComposite", "HdrComposite", new[] { 0, 1 });
        Passes = new IRenderPass[] { new Stage(this, true), new Stage(this, false) };
    }

    //原生后处理通道自行编译独立 GLSL，不使用 Unity Blit 的兼容占位实现。
    private static string PathFor(string name) => Path.Combine(RimUnityMod.ShaderDirectory, name + ".frag.glsl");
    private static PostEffectPass Make(string name, string shader, int[] bindings)
        => new("RimUnity.Bloom." + name, PathFor(shader), bindings, pushConstantSize: 16);

    //UNorm 场景保存显示颜色；sRGB 附件由采样器解码，浮点场景则约定为线性 HDR。
    private static bool Gamma(NativeRenderTexture texture) => texture.ColorFormat is VkFormat.B8G8R8A8UNorm or VkFormat.R8G8B8A8UNorm;

    //按宿主帧槽位分配，窗口尺寸变化由宿主的安全重建点统一释放。
    private void Ensure(in FrameContext ctx, NativeRenderTexture source)
    {
        if (targets != null && width == source.Width && height == source.Height) return;
        ReleaseTargets(); width = source.Width; height = source.Height;
        targets = new NativeRenderTexture[ctx.RenderContext.FramesInFlight][];
        for (int frame = 0; frame < targets.Length; frame++)
        {
            targets[frame] = new NativeRenderTexture[Levels * 2 - 1];
            for (int i = 0; i < targets[frame].Length; i++)
            {
                int level = i < Levels ? i : i - Levels;
                targets[frame][i] = new NativeRenderTexture(Math.Max(1, width >> (level + 1)), Math.Max(1, height >> (level + 1)),
                    VkFormat.R16G16B16A16SFloat, name: $"RimUnity.Bloom.{frame}.{i}");
            }
        }
    }

    //软阈值保留临界亮度的平滑过渡；只在线性光强上做模糊和合成。
    private void Prepare(in FrameContext ctx, BloomFrame settings)
    {
        NativeRenderTexture source = scene.Source?.Invoke(ctx.SubmissionFrameIndex);
        Prepare(in ctx, settings, source);
    }

    //地图与离屏预览共用亮部提取、金字塔与图层捕获。
    private void Prepare(in FrameContext ctx, BloomFrame settings, NativeRenderTexture source)
    {
        prepared = false;
        if (source == null) return;
        if (settings.Draws == null || settings.Draws.Frame.Draws.Count == 0) return;
        Ensure(in ctx, source);
        layers.Begin(in ctx, source, settings.FrameNumber, clear: true);
        layers.BindDepth(in ctx, source);
        layers.Record(in ctx, source, settings.Draws);
        var layerSource = layers.Result;
        var frame = targets[ctx.SubmissionFrameIndex];
        float threshold = settings.Threshold <= .04045f ? settings.Threshold / 12.92f : MathF.Pow((settings.Threshold + .055f) / 1.055f, 2.4f);
        for (int i = 0; i < Levels; i++)
        {
            down[i].SetInput(0, i == 0 ? layerSource : frame[i - 1]); down[i].Output = frame[i];
            //选中层已经在线性浮点附件中完成透明叠加。
            down[i].SetPushConstants(new Vector4(threshold, settings.SoftKnee, 0, 0));
            down[i].Record(in ctx, settings);
        }
        for (int i = Levels - 2; i >= 0; i--)
        {
            up[i].SetInput(0, frame[i]); up[i].SetInput(1, i == Levels - 2 ? frame[i + 1] : frame[Levels + i + 1]);
            up[i].Output = frame[Levels + i]; up[i].SetPushConstants(new Vector4(settings.Scatter, 0, 0, 0));
            up[i].Record(in ctx, settings);
        }
        prepared = true;
    }

    //预览已有完整的线性场景与深度，只复用统一 Bloom 并输出显示颜色。
    internal void RenderPreview(in FrameContext ctx, NativeRenderTexture source, NativeRenderTexture linearScene,
        NativeRenderTexture destination, BloomFrame settings)
    {
        Prepare(in ctx, settings, source);
        hdrCombine.SetInput(0, linearScene);
        hdrCombine.SetInput(1, prepared ? targets[ctx.SubmissionFrameIndex][Levels] : linearScene);
        hdrCombine.SetPushConstants(new Vector4(prepared ? settings.Intensity : 0, 1, 0, 0));
        hdrCombine.Output = destination;
        hdrCombine.Record(in ctx, settings);
    }

    //浮点场景在最后一次合成才编码；普通场景继续沿用宿主的显示颜色路径。
    private void Composite(in FrameContext ctx, BloomFrame settings)
    {
        NativeRenderTexture source = scene.Source?.Invoke(ctx.SubmissionFrameIndex);
        if (source == null) return;
        var hdr = HdrMapHooks.LinearScene(source, settings.FrameNumber);
        if (hdr != null)
        {
            hdrCombine.SetInput(0, hdr);
            hdrCombine.SetInput(1, prepared ? targets[ctx.SubmissionFrameIndex][Levels] : hdr);
            hdrCombine.SetPushConstants(new Vector4(prepared ? settings.Intensity : 0, scene.Saturation, 0, 0));
            hdrCombine.RecordInline(in ctx, ctx.RenderPass, ctx.SampleCount);
            return;
        }
        scene.Record(in ctx, settings);
        if (!prepared) return;
        combine.SetInput(0, source); combine.SetInput(1, targets[ctx.SubmissionFrameIndex][Levels]);
        combine.SetPushConstants(new Vector4(settings.Intensity, Gamma(source) ? 1 : 0, scene.Saturation, 0));
        combine.RecordInline(in ctx, ctx.RenderPass, ctx.SampleCount);
    }

    //宿主在等待在途帧后调用重建／释放入口，不能由设置窗口直接销毁 GPU 对象。
    internal void ReleaseTargets()
    {
        layers.Dispose();
        if (targets != null) foreach (var frame in targets) foreach (var texture in frame) texture.Dispose();
        targets = null;
        prepared = false;
        foreach (var pass in down) pass.Output = null;
        foreach (var pass in up) pass.Output = null;
        combine.Invalidate();
        hdrCombine.Invalidate();
    }

    //后处理实例结束时释放缓存及所有管线对象。
    public void Dispose()
    {
        ReleaseTargets(); foreach (var pass in down) pass.Dispose(); foreach (var pass in up) pass.Dispose(); combine.Dispose(); hdrCombine.Dispose();
    }

    //拆分在屏幕渲染之前和之内的两段，避免在一个 Vulkan RenderPass 中嵌套另一个。
    private sealed class Stage(BloomPipeline owner, bool prepare) : IRenderPass
    {
        public byte PassId => prepare ? owner.down[0].PassId : owner.combine.PassId;
        public PassAttachment Attachment => null;
        public bool PreBackbuffer => prepare;
        //两个阶段共享管线，生命周期由宿主 Feature 的统一入口负责。
        public void OnRenderTargetRebuilt(IRenderTarget target) { }
        public void Dispose() { }
        public void Record(in FrameContext ctx, object featurePayload)
        {
            if (featurePayload is not BloomFrame settings) return;
            if (prepare)
            {
                owner.prepared = false;
                if (settings.Enabled && settings.Intensity > 0) owner.Prepare(in ctx, settings);
            }
            else owner.Composite(in ctx, settings);
        }
    }
}
