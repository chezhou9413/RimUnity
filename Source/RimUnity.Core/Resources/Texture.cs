using System;
using System.Runtime.CompilerServices;

namespace RimUnity;

//管理原生贴图的所有权与采样状态。
public abstract class Texture : Object
{
    private static readonly ConditionalWeakTable<Waylith.Rendering.Texture, Texture> Wrappers = new();
    protected Texture(Waylith.Rendering.Texture native, bool owns = true)
    {
        NativeTexture = native ?? throw new ArgumentNullException(nameof(native)); OwnsResource = owns;
        Wrappers.Add(native, this);
    }
    public Waylith.Rendering.Texture NativeTexture { get; }
    protected bool OwnsResource { get; }
    public override string name { get => NativeTexture.Name; set => NativeTexture.Name = value; }
    public int width => NativeTexture.width;
    public int height => NativeTexture.height;
    public FilterMode filterMode { get => (FilterMode)NativeTexture.filterMode; set => NativeTexture.filterMode = (Waylith.Rendering.FilterMode)value; }
    public TextureWrapMode wrapMode { get => (TextureWrapMode)NativeTexture.wrapMode; set => NativeTexture.wrapMode = (Waylith.Rendering.TextureWrapMode)value; }
    public int anisoLevel { get => NativeTexture.anisoLevel; set => NativeTexture.anisoLevel = value; }
    public float mipMapBias { get => NativeTexture.mipMapBias; set => NativeTexture.mipMapBias = value; }
    internal static Texture Resolve(Waylith.Rendering.Texture native)
    {
        if (native is null) return null;
        if (Wrappers.TryGetValue(native, out Texture result)) return result;
        throw new InvalidOperationException("原生贴图未登记 RimUnity 所有权：" + native.Name);
    }
    //引用外部引擎贴图，不接管其释放责任。
    public static Texture FromNative(Waylith.Rendering.Texture native)
    {
        ArgumentNullException.ThrowIfNull(native);
        return Wrappers.TryGetValue(native, out Texture value) ? value : new NativeTextureReference(native);
    }
    private sealed class NativeTextureReference : Texture
    {
        internal NativeTextureReference(Waylith.Rendering.Texture native) : base(native, false) { }
    }
    protected override void Release() { Wrappers.Remove(NativeTexture); if (OwnsResource) NativeTexture.Dispose(); }
}

//枚举数值保持导出格式一致。
public enum FilterMode { Point, Bilinear, Trilinear }
public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, DXT1 = 10, DXT5 = 12, RFloat = 18, RGFloat = 19, RGBAFloat = 20, BC7 = 25, BC4 = 26, BC5 = 27, R8 = 63 }
