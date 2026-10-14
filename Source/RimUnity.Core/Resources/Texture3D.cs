using System;

namespace RimUnity;

//体积纹理为粒子向量场保留 CPU 像素，并可交给原生 GPU 采样。
public sealed class Texture3D : Texture
{
    public Waylith.Rendering.Texture3D CoreTexture => (Waylith.Rendering.Texture3D)NativeTexture;
    public int depth => CoreTexture.depth;
    public Texture3D(int width, int height, int depth, TextureFormat format, bool mipChain, bool linear = false)
        : base(new Waylith.Rendering.Texture3D(width, height, depth, (Waylith.Rendering.LoadedTextureFormat)format, linear,
            mipChain ? 1 + (int)MathF.Floor(MathF.Log2(Math.Max(width, Math.Max(height, depth)))) : 1)) { }
    public Color[] GetPixels() => Array.ConvertAll(CoreTexture.CpuData.GetPixels(), c => new Color(c.r, c.g, c.b, c.a));
    public void SetPixels(Color[] colors) => CoreTexture.CpuData.SetPixels(Array.ConvertAll(colors, c => new Waylith.Math.Color(c.r, c.g, c.b, c.a)));
    public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) => CoreTexture.Apply(updateMipmaps, makeNoLongerReadable);
}
