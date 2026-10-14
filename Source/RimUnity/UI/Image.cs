namespace RimUnity.UI;

//沿用引擎的 Image 属性，同时暴露具有完整尺寸属性的矩形。
public class Image : MaskableGraphic
{
    public enum Type { Simple, Sliced, Tiled, Filled }
    public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
    public enum OriginHorizontal { Left, Right }
    public enum OriginVertical { Bottom, Top }
    public enum Origin90 { BottomLeft, TopLeft, TopRight, BottomRight }
    public enum Origin180 { Bottom, Left, Top, Right }
    public enum Origin360 { Bottom, Right, Top, Left }
    public Sprite sprite { get; set => SetProperty(ref field, value); }
    //与 UGUI 一致：无临时覆盖时读取基础精灵，写入 null 则恢复基础精灵。
    public Sprite overrideSprite { get => field != null ? field : sprite; set => SetProperty(ref field, value); }
    public Type type { get; set => SetProperty(ref field, value); }
    public FillMethod fillMethod { get; set; }
    public float fillAmount { get; set; } = 1f;
    public bool fillClockwise { get; set; } = true;
    public int fillOrigin { get; set; }
    public bool preserveAspect { get; set; }
    public bool fillCenter { get; set; } = true;
    public bool useSpriteMesh { get; set; }
    public float pixelsPerUnitMultiplier { get; set => SetProperty(ref field, value); } = 1f;
    public override Texture mainTexture => (overrideSprite != null ? overrideSprite : sprite)?.texture;

    //按精灵像素与画布参考比例设置原生尺寸。
    public void SetNativeSize()
    {
        Sprite s = overrideSprite != null ? overrideSprite : sprite;
        if (s == null) return;
        Canvas canvas = this.GetComponentInParent<Canvas>();
        float ratio = s.pixelsPerUnit / (canvas != null ? canvas.referencePixelsPerUnit : 100f);
        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = s.rect.size / ratio;
    }
}
