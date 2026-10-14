using System;

namespace RimUnity;

//图片解码、像素和上传使用 Waylith 原生贴图接口。
public sealed class Texture2D : Texture
{
    private static Texture2D white, black, gray, normal;
    private readonly bool linear;
    public Waylith.Rendering.Texture2D CoreTexture => (Waylith.Rendering.Texture2D)NativeTexture;
    public bool isReadable => CoreTexture.IsReadable;
    public TextureFormat format => (TextureFormat)CoreTexture.CpuData.Format;
    public int mipmapCount => CoreTexture.MipCount;
    public Texture2D(int width, int height, TextureFormat format = TextureFormat.RGBA32, bool mipChain = false, bool linear = false)
        : base(new Waylith.Rendering.Texture2D(width, height, (Waylith.Rendering.LoadedTextureFormat)format, mipChain, linear)) => this.linear = linear;
    private Texture2D(Waylith.Rendering.Texture2D native) : base(native) => linear = native.CpuData?.Linear ?? false;
    public static Texture2D whiteTexture => white ??= Solid("RimUnity White", new(255, 255, 255, 255));
    public static Texture2D blackTexture => black ??= Solid("RimUnity Black", new(0, 0, 0, 255));
    public static Texture2D grayTexture => gray ??= Solid("RimUnity Gray", new(128, 128, 128, 255));
    public static Texture2D normalTexture => normal ??= Solid("RimUnity Normal", new(128, 128, 255, 255));
    private static Texture2D Solid(string name, Color32 c) => new(Waylith.Rendering.Texture2D.CreateSolid(name, new(c.r, c.g, c.b, c.a)));
    public bool LoadImage(byte[] bytes)
    {
        if (!CoreTexture.LoadEncodedImage(bytes)) return false;
        var data = CoreTexture.CpuData;
        if (data.Linear != linear) CoreTexture.ReplaceCpuData(width, height, data.Format, data.Levels, data.MipCount, linear);
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
    public void SetPixel(int x, int y, Color c) => CoreTexture.SetPixel(Wrap(x, width), height - 1 - Wrap(y, height), new(c.r, c.g, c.b, c.a));
    public void SetPixels(Color[] colors) => CoreTexture.SetPixels(FlipRows(Array.ConvertAll(colors, c => new Waylith.Math.Color(c.r, c.g, c.b, c.a))));
    public void SetPixels32(Color32[] colors) => CoreTexture.SetPixels32(FlipRows(Array.ConvertAll(colors, c => new Waylith.Math.Color32(c.r, c.g, c.b, c.a))));

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
    public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) => CoreTexture.Apply(updateMipmaps, makeNoLongerReadable);
}
