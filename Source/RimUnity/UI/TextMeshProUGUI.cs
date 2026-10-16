using System;
using System.Collections.Generic;
using RimUnity.UI;

namespace RimUnity.UI;

//TMP 使用自己的图集、排版和 SDF 材质，不退化为普通 Text。
public sealed partial class TextMeshProUGUI : MaskableGraphic, ILayoutElement
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
    public float preferredWidth { get { Build(float.PositiveInfinity, float.PositiveInfinity); return measured.x + margin.x + margin.z; } }
    public float preferredHeight { get { Build(rectTransform.rect.width - margin.x - margin.z, float.PositiveInfinity); return measured.y + margin.y + margin.w; } }
    public float flexibleWidth => -1;
    public float flexibleHeight => -1;
    public int layoutPriority => 0;
    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }
    public void SetText(string value) => text = value;
    public void SetText(string format, params object[] values) => text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, values);
    public void ForceMeshUpdate() { cached = null; Build(rectTransform.rect.width - margin.x - margin.z, rectTransform.rect.height - margin.y - margin.w); }
    public Vector2 GetPreferredValues(string value, float width = float.PositiveInfinity, float height = float.PositiveInfinity)
    { string old = text; text = value; try { Build(width, height); return measured; } finally { text = old; cached = null; } }
    private readonly Dictionary<Texture2D, Material> sdfMaterials = new();
    private void OnDestroy() { foreach (var m in sdfMaterials.Values) Destroy(m); sdfMaterials.Clear(); }
    internal Material SdfMaterial(Texture2D atlas)
    {
        if (!sdfMaterials.TryGetValue(atlas, out var material))
        {
            if (!Waylith.Rendering.ShaderRegistry.TryResolve("RimUnity_SDF", out var shader)) throw new InvalidOperationException("RimUnity_SDF 未注册。");
            material = new Material(Shader.FromEngineShader("RimUnity_SDF", shader)); sdfMaterials.Add(atlas, material);
        }
        material.SetTexture("_MainTex", atlas); material.SetColor("_FaceColor", faceColor); material.SetColor("_OutlineColor", outlineColor);
        material.SetFloat("_OutlineWidth", outlineWidth); material.SetFloat("_FaceDilate", faceDilate);
        return material;
    }
}

//枚举值与 Unity TMP 的导出值一致，尚未实现的模式在导出端明确拒绝。
public enum TextOverflowModes { Overflow, Ellipsis, Masking, Truncate, ScrollRect, Page, Linked }
