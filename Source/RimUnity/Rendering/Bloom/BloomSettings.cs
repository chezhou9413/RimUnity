using Verse;

namespace RimUnity.Rendering;

//阈值使用屏幕颜色尺度，模糊与强度计算在线性空间进行；设置只在主线程读取。
public sealed partial class BloomSettings : ModSettings
{
    public bool enabled;
    // Map HDR is independent of Bloom; absent settings keys intentionally default off.
    public bool mapHdrEnabled;
    public int layerMask;
    public float threshold = .8f, intensity = .6f, softKnee = .5f, scatter = .65f;
    //内容模组在地图生命周期内接管自己的层，不改写玩家保存的手动选择。
    private int controlledLayers, activeLayers;

    //主线程调用；同一层由一个内容模组负责，关闭时也覆盖手动勾选。
    public void SetLayerEnabled(int layer, bool active)
    {
        if ((uint)layer >= 32) throw new System.ArgumentOutOfRangeException(nameof(layer));
        int bit = 1 << layer;
        controlledLayers |= bit;
        if (active) activeLayers |= bit; else activeLayers &= ~bit;
    }

    //内容模组结束地图生命周期时撤销控制，恢复该层的手动设置。
    public void ReleaseLayer(int layer)
    {
        if ((uint)layer >= 32) throw new System.ArgumentOutOfRangeException(nameof(layer));
        int bit = 1 << layer;
        controlledLayers &= ~bit; activeLayers &= ~bit;
    }

    //只重置玩家设置，内容模组仍负责自己接管的层。
    public void ResetDefaults()
    {
        enabled = false;
        mapHdrEnabled = false;
        layerMask = 0;
        threshold = .8f;
        intensity = .6f;
        softKnee = .5f;
        scatter = .65f;
    }
    //玩家设置独立于地图存档。
    public override void ExposeData()
    {
        Scribe_Values.Look(ref enabled, "bloomEnabled", false);
        Scribe_Values.Look(ref mapHdrEnabled, "mapHdrEnabled", false);
        Scribe_Values.Look(ref layerMask, "bloomLayerMask", 0);
        Scribe_Values.Look(ref threshold, "bloomThreshold", .8f);
        Scribe_Values.Look(ref intensity, "bloomIntensity", .6f);
        Scribe_Values.Look(ref softKnee, "bloomSoftKnee", .5f);
        Scribe_Values.Look(ref scatter, "bloomScatter", .65f);
    }

    //给渲染线程传递不可变数值，不在渲染线程访问地图、模组设置或 Unity 对象。
    internal BloomFrame Snapshot()
    {
        int mask = ((enabled ? layerMask : 0) & ~controlledLayers) | activeLayers;
        return new(mask != 0 && Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null,
            Mathf.Clamp(threshold, 0, 4), Mathf.Clamp(intensity, 0, 4), Mathf.Clamp01(softKnee), Mathf.Clamp(scatter, .05f, .95f))
            { LayerMask = mask };
    }

}

//随宿主帧快照传递；替代的原始载荷只用于判断场景是否存在。
internal sealed record BloomFrame(bool Enabled, float Threshold, float Intensity, float SoftKnee, float Scatter) : System.IDisposable
{
    private int disposed;
    internal int LayerMask { get; init; }
    internal int FrameNumber { get; init; }
    internal HdrDrawFrame Draws { get; init; }
    internal ScreenEfx.EfxFrame Effects { get; init; }
    //快照独占筛选后的绘制项，释放时不触碰地图本身的绘制缓存。
    public void Dispose()
    {
        //The effects container can be pooled after retirement. An old payload must never dispose it twice.
        if (System.Threading.Interlocked.Exchange(ref disposed, 1) != 0) return;
        Draws?.Dispose(); Effects?.Dispose();
    }
}
