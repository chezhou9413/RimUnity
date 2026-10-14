
namespace RimUnity.UI;

//直接显示贴图，可通过 uvRect 选取区域。
public class RawImage : RimUnity.UI.MaskableGraphic
{
    public Texture texture { get; set; }
    public Rect uvRect { get; set; } = new(0, 0, 1, 1);
    public override Texture mainTexture => texture;
    public new RectTransform rectTransform => (RectTransform)transform;

    //按贴图尺寸设置矩形大小。
    public void SetNativeSize()
    {
        if (texture == null) return;
        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = new Vector2(texture.width * uvRect.width, texture.height * uvRect.height);
    }
}
