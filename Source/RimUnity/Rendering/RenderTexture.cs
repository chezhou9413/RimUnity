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
    //四个颜色样本在 GPU 线性降采样，不把抗锯齿设置静默改为单采样。
    //将已渲染附件读回到宿主贴图，供游戏原生界面显示预览。
    public void ReadPixels(UnityEngine.Texture2D destination)
    {
        System.ArgumentNullException.ThrowIfNull(destination);
        var previous = UnityEngine.RenderTexture.active;
        try
        {
            UnityEngine.RenderTexture.active = Target;
            destination.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            destination.Apply();
        }
        finally { UnityEngine.RenderTexture.active = previous; }
    }

    internal void Resolve()
    {
        if (antiAliasing == 1) return;
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
