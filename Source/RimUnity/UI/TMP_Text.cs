using System;
using System.Globalization;

namespace RimUnity.UI;

//TMP 文本抽象基类，与 Unity TMPro.TMP_Text 继承层次保持一致。
public abstract class TMP_Text : MaskableGraphic, ILayoutElement
{
    public string text { get; set => SetProperty(ref field, value); } = "";
    public TMP_FontAsset font { get; set => SetProperty(ref field, value); }
    public float fontSize { get; set => SetProperty(ref field, value); } = 36;
    public float fontSizeMin { get; set => SetProperty(ref field, value); } = 18;
    public float fontSizeMax { get; set => SetProperty(ref field, value); } = 72;
    public bool enableAutoSizing { get; set => SetProperty(ref field, value); }
    public bool enableWordWrapping { get; set => SetProperty(ref field, value); } = true;
    public bool richText { get; set => SetProperty(ref field, value); } = true;
    public bool enableKerning { get; set => SetProperty(ref field, value); } = true;
    public TextOverflowModes overflowMode { get; set => SetProperty(ref field, value); }
    public int alignment { get; set => SetProperty(ref field, value); } = 257;
    public float characterSpacing { get; set => SetProperty(ref field, value); }
    public float lineSpacing { get; set => SetProperty(ref field, value); }
    public Vector4 margin { get; set => SetProperty(ref field, value); }
    //与 TMP 的 Color32 属性一致，导出字节颜色在写入材质时才归一化。
    public Color32 faceColor { get; set; } = Color.white;
    public Color32 outlineColor { get; set; } = Color.black;
    public float outlineWidth { get; set; }
    public float faceDilate { get; set; }
    public float italicSlant { get; set => SetProperty(ref field, value); }
    //字体自带字距与粗体增量使用 TMP 的百分之一 em 单位。
    public float fontSpacing { get; set => SetProperty(ref field, value); }
    public new RectTransform rectTransform => (RectTransform)transform;
    public override Texture mainTexture => font != null && font.atlasTextures.Length > 0 ? font.atlasTextures[0] : null;
    public float minWidth => 0;
    public float minHeight => 0;
    public abstract float preferredWidth { get; }
    public abstract float preferredHeight { get; }
    public float flexibleWidth => -1;
    public float flexibleHeight => -1;
    public int layoutPriority => 0;
    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }
    public void SetText(string value) => text = value;
    public void SetText(string format, params object[] values) => text = string.Format(CultureInfo.InvariantCulture, format, values);
    public abstract void ForceMeshUpdate();
    public abstract Vector2 GetPreferredValues(string value, float width = float.PositiveInfinity, float height = float.PositiveInfinity);
}
