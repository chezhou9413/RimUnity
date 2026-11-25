using Brutal.VulkanApi;
using UnityEngine.Experimental.Rendering;
using Waylith.Graphics;
using Waylith.Rendering;

namespace RimUnity;

//离屏附件通过宿主公开接口分配；材质直接引用同一张原生纹理。
public sealed class RenderTexture : Texture
{
    internal UnityEngine.RenderTexture Target { get; }
    internal UnityEngine.RenderTexture CaptureTarget { get; }
    public int antiAliasing { get; }
    public int depth => Target.depth;
    public RenderTexture(int width, int height, int depth, int graphicsFormat, int antiAliasing = 1)
        : this(new UnityEngine.RenderTexture(width, height, depth,
            (UnityEngine.Experimental.Rendering.GraphicsFormat)graphicsFormat, 1), antiAliasing) { }
    private RenderTexture(UnityEngine.RenderTexture target, int samples) : base(target.CoreTexture, false)
    {
        if (samples != 1 && samples != 4) throw new System.ArgumentOutOfRangeException(nameof(samples));
        Target = target; antiAliasing = samples;
        CaptureTarget = samples == 1 ? target : new UnityEngine.RenderTexture(target.width * 2, target.height * 2, target.depth, target.graphicsFormat, 1);
    }
    //确保底层 GPU 颜色附件已在主线程分配，供自定义渲染管线步骤在渲染线程安全访问。
    public bool EnsureCreated()
    {
        EnsureAllocation(Target);
        return Target.Create() && Target.GpuTexture != null;
    }

    //宿主 Create 尚未映射单通道格式，在进入宿主绘制前分配真实颜色附件。
    internal void PrepareCapture() => EnsureAllocation(CaptureTarget);

    //仅接管宿主确实误映射的格式；释放或驱逐后仍从声明格式重新分配。
    private static void EnsureAllocation(UnityEngine.RenderTexture target)
    {
        VkFormat format = target.graphicsFormat switch
        {
            GraphicsFormat.R8_UNorm => VkFormat.R8UNorm,
            GraphicsFormat.R32_SFloat => VkFormat.R32SFloat,
            _ => VkFormat.Undefined
        };
        if (format == VkFormat.Undefined) return;
        if (target.GpuTexture != null && target.GpuTexture.Format != format) target.Release();
        if (target.GpuTexture == null)
        {
            target.CoreTexture.MemoryCategory = GpuMemoryCategory.RenderTarget;
            target.CoreTexture.GpuResource = GpuTextureService.CreateEmptyTexture2D(
                string.IsNullOrEmpty(target.name) ? "RimUnity." + target.graphicsFormat : target.name,
                target.width, target.height, 1, format, VkImageUsageFlags.ColorAttachmentBit, GpuMemoryCategory.RenderTarget)
                ?? throw new System.InvalidOperationException("无法分配 RimUnity 离屏附件：" + target.graphicsFormat);
        }
        if (!target.Create()) throw new System.InvalidOperationException("无法创建 RimUnity 离屏附件：" + target.graphicsFormat);
    }

    //将已渲染附件读回到宿主贴图，供游戏原生界面显示预览。
    public void ReadPixels(UnityEngine.Texture2D destination)
    {
        System.ArgumentNullException.ThrowIfNull(destination);
        if (Target.graphicsFormat is GraphicsFormat.R8_UNorm or GraphicsFormat.R32_SFloat)
            throw new System.NotSupportedException("宿主 ReadPixels 不支持单通道离屏附件：" + Target.graphicsFormat);
        var previous = UnityEngine.RenderTexture.active;
        try
        {
            UnityEngine.RenderTexture.active = Target;
            destination.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            destination.Apply();
        }
        finally { UnityEngine.RenderTexture.active = previous; }
    }

    //四个颜色样本在 GPU 线性降采样，不把抗锯齿设置静默改为单采样。
    internal void Resolve()
    {
        if (antiAliasing == 1) return;
        EnsureAllocation(Target);
        if (!Target.Create() || Target.GpuTexture == null || CaptureTarget.GpuTexture == null)
            throw new System.InvalidOperationException("离屏抗锯齿附件尚未生成。");
        Waylith.Rendering.GpuTextureService.BlitTexture(CaptureTarget.GpuTexture, CaptureTarget.width, CaptureTarget.height,
            0, 0, 0, CaptureTarget.width, CaptureTarget.height, Target.GpuTexture, 0, 0, 0, Target.width, Target.height, Brutal.VulkanApi.VkFilter.Linear);
    }
    //宿主负责等待附件释放，基类只解除包装引用。
    internal void Evict()
    {
        Target.Release();
        if (CaptureTarget != Target) CaptureTarget.Release();
    }

    //销毁包装时同时销毁所属的宿主对象。
    protected override void Release()
    {
        Target.Release();
        UnityEngine.Object.Destroy(Target);
        if (CaptureTarget != Target) { CaptureTarget.Release(); UnityEngine.Object.Destroy(CaptureTarget); }
        base.Release();
    }
}
