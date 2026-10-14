using Verse;

namespace RimUnity.Rendering;

//阈值使用屏幕颜色尺度，模糊与强度计算在线性空间进行；设置只在主线程读取。
public sealed class BloomSettings : ModSettings
{
    public bool enabled;
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

    //玩家设置独立于地图存档。
    public override void ExposeData()
    {
        Scribe_Values.Look(ref enabled, "bloomEnabled", false);
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

    //使用标准布局处理中文行高，滑块不占用标签区域。
    internal void Draw(UnityEngine.Rect rect)
    {
        var listing = new Listing_Standard();
        listing.Begin(rect);
        listing.CheckboxLabeled("启用手动选择层的 Bloom 泛光", ref enabled);
        listing.Label("内容模组接管的层由其自身设置控制；这里控制其他层，强度参数共用。默认不选层；普通地表通常在 0 层，不建议勾选。");
        UnityEngine.Rect layers = listing.GetRect(8 * 26);
        for (int layer = 0; layer < 32; layer++)
        {
            bool selected = (layerMask & (1 << layer)) != 0;
            Widgets.CheckboxLabeled(new UnityEngine.Rect(layers.x + (layer / 8) * layers.width / 4, layers.y + (layer % 8) * 26,
                layers.width / 4 - 12, 24), $"Layer {layer}", ref selected);
            if (selected) layerMask |= 1 << layer; else layerMask &= ~(1 << layer);
        }
        listing.Gap();
        listing.Label($"亮部阈值：{threshold:F2}"); threshold = listing.Slider(threshold, 0, 2);
        listing.Label($"泛光强度：{intensity:F2}"); intensity = listing.Slider(intensity, 0, 3);
        listing.Label($"阈值柔和度：{softKnee:F2}"); softKnee = listing.Slider(softKnee, 0, 1);
        listing.Label($"光晕扩散：{scatter:F2}"); scatter = listing.Slider(scatter, .05f, .95f);
        listing.End();
    }
}

//随宿主帧快照传递；替代的原始载荷只用于判断场景是否存在。
internal sealed record BloomFrame(bool Enabled, float Threshold, float Intensity, float SoftKnee, float Scatter) : System.IDisposable
{
    internal int LayerMask { get; init; }
    internal int FrameNumber { get; init; }
    internal HdrDrawFrame Draws { get; init; }
    //快照独占筛选后的绘制项，释放时不触碰地图本身的绘制缓存。
    public void Dispose() => Draws?.Dispose();
}
