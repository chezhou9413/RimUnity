using System;
using System.Collections.Generic;
using RimUnity.UI;

namespace RimUnity.UI;

//TMP 使用自己的图集、排版和 SDF 材质，不退化为普通 Text。
public sealed partial class TextMeshProUGUI : TMP_Text
{
    public override float preferredWidth
    {
        get
        {
            Build(float.PositiveInfinity, float.PositiveInfinity);
            return measured.x + Mathf.Max(0f, margin.x) + Mathf.Max(0f, margin.z);
        }
    }

    public override float preferredHeight
    {
        get
        {
            float availWidth = rectTransform.rect.width - margin.x - margin.z;
            Build(availWidth > 0f ? availWidth : float.PositiveInfinity, float.PositiveInfinity);
            return measured.y + Mathf.Max(0f, margin.y) + Mathf.Max(0f, margin.w);
        }
    }

    public override void ForceMeshUpdate()
    {
        cached = null;
        Build(rectTransform.rect.width - margin.x - margin.z, rectTransform.rect.height - margin.y - margin.w);
    }

    //无副作用测量：不修改 text 属性、不触发 UguiDirty 布局标脏，避免聚焦输入框每帧整画布重排。
    public override Vector2 GetPreferredValues(string value, float width = float.PositiveInfinity, float height = float.PositiveInfinity)
        => _MeasureValues(value, width, height);

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
