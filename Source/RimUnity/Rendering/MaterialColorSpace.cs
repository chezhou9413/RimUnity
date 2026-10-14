using Waylith.Rendering;

namespace RimUnity.Rendering;

//替换贴图时同步采样编码标记；数值贴图保持原值，颜色贴图由 Linear Shader 解码。
internal static class MaterialColorSpace
{
    internal static void Install() => Material.TextureChanged += Apply;

    //贴图编码与材质参数保持同步，数值纹理不进行颜色解码。
    private static void Apply(Material material, int nameID, Texture value)
    {
        if (!ShaderPropertyId.TryGetName(nameID, out string name)) return;
        string flag = ShaderColorSpace.TextureFlag + name;
        if (!material.HasProperty(flag)) return;
        bool srgb = value is Cubemap cube ? !cube.Linear : value?.NativeTexture is Waylith.Rendering.Texture2D texture && !(texture.CpuData?.Linear ?? false);
        material.SetFloat(flag, srgb ? 1 : 0);
    }
}
