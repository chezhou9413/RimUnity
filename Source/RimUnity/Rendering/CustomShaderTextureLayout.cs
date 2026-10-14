using System;
using System.Linq;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//把 Unity 的纹理像素尺寸变量绑定到宿主实际使用的纹理资源。
public static class CustomShaderTextureLayout
{
    //宿主 GLSL 解析器只识别 _ST，_TexelSize 需要补充动态尺寸语义。
    public static MaterialLayout BindTexelSizes(MaterialLayout layout)
    {
        MaterialUniformSlot Bind(MaterialUniformSlot slot)
        {
            const string suffix = "_TexelSize";
            if (slot.Type != MaterialUniformType.Vector4 || !slot.PropertyName.EndsWith(suffix, StringComparison.Ordinal)) return slot;
            string textureName = slot.PropertyName[..^suffix.Length];
            foreach (var texture in layout.TextureSlots)
            {
                if (texture.Kind == MaterialTextureKind.Sampler || texture.PropertyName != textureName) continue;
                return slot with
                {
                    Type = MaterialUniformType.TexelSize,
                    TextureResourceSlot = texture.ResourceSlot,
                    DefaultValue = MaterialUniformValue.FromVector(MaterialUniformType.TexelSize, slot.DefaultValue.Vector4)
                };
            }
            return slot;
        }

        return new MaterialLayout(layout.UniformSlots.Select(Bind).ToArray(), layout.TextureSlots, layout.UniformSize,
            layout.StorageBufferSlots, layout.HasErrors,
            layout.UniformBlocks.Select(block => block with { Members = block.Members.Select(Bind).ToArray() }).ToArray());
    }
}
