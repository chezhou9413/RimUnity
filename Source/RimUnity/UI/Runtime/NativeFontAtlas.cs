using System;
using Brutal.ImGuiApi;

namespace RimUnity.UI.Runtime;

//从 ImGui 原生字形图集复制实际像素，普通 Text 和材质 Text 使用同一份纹理。
internal static class NativeFontAtlas
{
    private static Texture2D texture;
    private static int signature;

    //只在字形图集扩容或更新时上传；UI 顶点使用向上的 UV，因此像素行在这里翻转。
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
        var colors = new Color32[checked(atlas.Width * atlas.Height)];
        for (int y = 0; y < atlas.Height; y++)
            for (int x = 0; x < atlas.Width; x++)
            {
                int source = (y * atlas.Width + x) * 4;
                colors[(atlas.Height - 1 - y) * atlas.Width + x] = new(pixels[source], pixels[source + 1], pixels[source + 2], pixels[source + 3]);
            }
        texture.SetPixels32(colors); texture.Apply(false, false); signature = version;
        return texture;
    }
}
