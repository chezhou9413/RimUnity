using System;

namespace RimUnity;

//图片解码、像素和上传使用 Waylith 原生贴图接口。
public sealed partial class Texture2D : Texture
{
    private static Texture2D white, black, gray, normal;
    private readonly bool linear;
    //图集只在像素提交后刷新，不在每帧复制静态贴图。
    public int contentRevision { get; private set; }
    public bool isExternallyOwned => !OwnsResource;
    public Waylith.Rendering.Texture2D CoreTexture => (Waylith.Rendering.Texture2D)NativeTexture;
    public bool isReadable => CoreTexture.IsReadable;
    public TextureFormat format => (TextureFormat)CoreTexture.CpuData.Format;
    public int mipmapCount => CoreTexture.MipCount;
    //按真实格式和全部 mip 估算载荷，避免把压缩资源按 RGBA32 计入缓存预算。
    public long storageSizeBytes
    {
        get { long bytes = 0; for (int i = 0; i < mipmapCount; i++) bytes += CoreTexture.CpuData.GetLevelSizeBytes(i); return bytes; }
    }
    public Texture2D(int width, int height, TextureFormat format = TextureFormat.RGBA32, bool mipChain = false, bool linear = false)
        : base(new Waylith.Rendering.Texture2D(width, height, (Waylith.Rendering.LoadedTextureFormat)format, mipChain, linear)) => this.linear = linear;
    private Texture2D(Waylith.Rendering.Texture2D native) : base(native) => linear = native.CpuData?.Linear ?? false;
    private Texture2D(Waylith.Rendering.Texture2D native, bool owns) : base(native, owns) => linear = native.CpuData?.Linear ?? false;
    internal static Texture2D Borrow(Waylith.Rendering.Texture2D native) => new(native, false);
    //游戏图标保持宿主所有权，UI 精灵只共享相同的设备贴图。
    public static Texture2D FromNative(Waylith.Rendering.Texture2D native) => (Texture2D)Texture.FromNative(native);
    public static Texture2D whiteTexture => white ??= Solid("RimUnity White", new(255, 255, 255, 255));
    public static Texture2D blackTexture => black ??= Solid("RimUnity Black", new(0, 0, 0, 255));
    public static Texture2D grayTexture => gray ??= Solid("RimUnity Gray", new(128, 128, 128, 255));
    public static Texture2D normalTexture => normal ??= Solid("RimUnity Normal", new(128, 128, 255, 255));
    private static Texture2D Solid(string name, Color32 c) => new(Waylith.Rendering.Texture2D.CreateSolid(name, new(c.r, c.g, c.b, c.a)));
    public bool LoadImage(byte[] bytes, bool markNonReadable = false)
    {
        //宿主通用加载器会扩散透明像素的 RGB，破坏 Spine 等预乘纹理；PNG 直接保留解码像素。
        byte[] pixels = Waylith.Rendering.PngDecoder.TryDecodeRgba32(bytes, out int decodedWidth, out int decodedHeight);
        if (pixels != null)
        {
            PrepareFileWrite(true);
            CoreTexture.Opaque = true;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 255) { CoreTexture.Opaque = false; break; }
            CoreTexture.ReplaceCpuData(decodedWidth, decodedHeight, Waylith.Rendering.LoadedTextureFormat.Rgba32, new[] { pixels }, 1, linear);
        }
        else
        {
            if (!CoreTexture.LoadEncodedImage(bytes)) return false;
            PrepareFileWrite(true);
        }
        var data = CoreTexture.CpuData;
        if (data.Linear != linear) CoreTexture.ReplaceCpuData(width, height, data.Format, data.Levels, data.MipCount, linear);
        contentRevision++;
        if (markNonReadable) CoreTexture.Apply(false, true);
        return true;
    }
    public Color GetPixel(int x, int y) { var c = CoreTexture.GetPixel(Wrap(x, width), height - 1 - Wrap(y, height)); return new(c.r, c.g, c.b, c.a); }
    //双线性取样用于贴图粒子发射，边界遵守贴图的环绕模式。
    public Color GetPixelBilinear(float u, float v)
    {
        float x = u * width - .5f, y = v * height - .5f;
        int left = Mathf.FloorToInt(x), bottom = Mathf.FloorToInt(y);
        Color a = GetPixel(Wrap(left, width), Wrap(bottom, height));
        Color b = GetPixel(Wrap(left + 1, width), Wrap(bottom, height));
        Color c = GetPixel(Wrap(left, width), Wrap(bottom + 1, height));
        Color d = GetPixel(Wrap(left + 1, width), Wrap(bottom + 1, height));
        return Color.Lerp(Color.Lerp(a, b, x - left), Color.Lerp(c, d, x - left), y - bottom);
    }
    private int Wrap(int index, int length)
    {
        if (wrapMode == TextureWrapMode.Repeat) return (index % length + length) % length;
        if (wrapMode == TextureWrapMode.Mirror)
        { int i = (index % (length * 2) + length * 2) % (length * 2); return i < length ? i : length * 2 - i - 1; }
        if (wrapMode == TextureWrapMode.MirrorOnce) index = index < 0 ? -index - 1 : index;
        return Mathf.Clamp(index, 0, length - 1);
    }
    public Color[] GetPixels() => FlipRows(Array.ConvertAll(CoreTexture.GetPixels(), c => new Color(c.r, c.g, c.b, c.a)));
    public Color32[] GetPixels32() => FlipRows(Array.ConvertAll(CoreTexture.GetPixels32(), c => new Color32(c.r, c.g, c.b, c.a)));
    public void SetPixel(int x, int y, Color c) { PrepareFileWrite(); CoreTexture.SetPixel(Wrap(x, width), height - 1 - Wrap(y, height), new(c.r, c.g, c.b, c.a)); }
    public void SetPixels(Color[] colors) { PrepareFileWrite(); CoreTexture.SetPixels(FlipRows(Array.ConvertAll(colors, c => new Waylith.Math.Color(c.r, c.g, c.b, c.a)))); }
    public void SetPixels32(Color32[] colors) { PrepareFileWrite(); CoreTexture.SetPixels32(FlipRows(Array.ConvertAll(colors, c => new Waylith.Math.Color32(c.r, c.g, c.b, c.a)))); }

    //像素 API 从左下开始，设备数据从左上开始；只翻转独立副本。
    private T[] FlipRows<T>(T[] pixels)
    {
        if (pixels.Length != checked(width * height)) throw new ArgumentException("像素数量与贴图尺寸不一致。", nameof(pixels));
        for (int y = 0; y < height / 2; y++)
            for (int x = 0; x < width; x++)
            {
                int first = y * width + x, last = (height - 1 - y) * width + x;
                (pixels[first], pixels[last]) = (pixels[last], pixels[first]);
            }
        return pixels;
    }
    public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false)
    {
        CoreTexture.Apply(updateMipmaps, makeNoLongerReadable);
        contentRevision++;
    }
}
