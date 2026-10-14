using System;
using Brutal.ImGuiApi;

namespace RimUnity.UI.Runtime;

//从 ImGui 原生字形图集复制实际像素，普通 Text 和材质 Text 使用同一份纹理。
internal static class NativeFontAtlas
{
    private static Texture2D texture;
    private static int signature;

    //只在字形图集扩容或更新时上传；直接复制同为从上到下的设备像素。
    internal static unsafe Texture2D Capture()
    {
        var atlas = ImGui.GetIO().Fonts.TexData;
        if (atlas.IsNull() || atlas.Width <= 0 || atlas.Height <= 0 || atlas.BytesPerPixel != 4)
            throw new InvalidOperationException("原生字体图集尚未提供 RGBA 像素。");
        var pixels = (byte*)(void*)atlas.GetPixelsAt(0, 0);
        if (pixels == null) throw new InvalidOperationException("原生字体图集像素不可读。");
        var hash = new HashCode();
        hash.Add(atlas.UniqueID); hash.Add((nint)pixels); hash.Add(atlas.Width); hash.Add(atlas.Height);
        hash.Add(atlas.UsedRect); hash.Add(atlas.UpdateRect);
        foreach (var update in atlas.Updates.Span) hash.Add(update);
        int version = hash.ToHashCode();
        if (texture != null && signature == version) return texture;
        if (texture == null || texture.width != atlas.Width || texture.height != atlas.Height)
        {
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false, true)
            { name = "RimUnity 字体图集", wrapMode = TextureWrapMode.Clamp };
        }
        byte[] bytes = new ReadOnlySpan<byte>(pixels, checked(atlas.Width * atlas.Height * 4)).ToArray();
        texture.CoreTexture.ReplaceCpuData(atlas.Width, atlas.Height, Waylith.Rendering.LoadedTextureFormat.Rgba32,
            new[] { bytes }, 1, true);
        signature = version;
        return texture;
    }
}
