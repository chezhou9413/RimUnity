using System;
using System.Runtime.InteropServices;
using TexturePixelData = Waylith.Rendering.TexturePixelData;
using LoadedTextureFormat = Waylith.Rendering.LoadedTextureFormat;

namespace RimUnity;

//读取三维浮点体素，保留深度与有符号速度分量。
internal static class ParticleVectorField
{
    internal static Vector3 Sample(Texture3D texture, Vector3 uvw)
    {
        TexturePixelData data = texture.CoreTexture.CpuData;
        if (data == null || !data.HasData) throw new InvalidOperationException("向量力场纹理必须保留 CPU 体素：" + texture.name);
        Vector3 p = new(uvw.x * texture.width - .5f, uvw.y * texture.height - .5f, uvw.z * texture.depth - .5f);
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        if (texture.filterMode == FilterMode.Point) return Read(data, texture, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        Vector3 a = Vector3.Lerp(Read(data, texture, x, y, z), Read(data, texture, x + 1, y, z), fx);
        Vector3 b = Vector3.Lerp(Read(data, texture, x, y + 1, z), Read(data, texture, x + 1, y + 1, z), fx);
        Vector3 c = Vector3.Lerp(Read(data, texture, x, y, z + 1), Read(data, texture, x + 1, y, z + 1), fx);
        Vector3 d = Vector3.Lerp(Read(data, texture, x, y + 1, z + 1), Read(data, texture, x + 1, y + 1, z + 1), fx);
        return Vector3.Lerp(Vector3.Lerp(a, b, fy), Vector3.Lerp(c, d, fy), fz);
    }

    //Clamp、Repeat 和镜像环绕与纹理采样坐标一致，浮点 RGB 直接表示有符号向量。
    private static Vector3 Read(TexturePixelData data, Texture3D texture, int x, int y, int z)
    {
        int Address(int value, int size)
        {
            if (texture.wrapMode == TextureWrapMode.Repeat) return (value % size + size) % size;
            if (texture.wrapMode == TextureWrapMode.Mirror)
            { int n = (value % (2 * size) + 2 * size) % (2 * size); return n < size ? n : 2 * size - 1 - n; }
            if (texture.wrapMode == TextureWrapMode.MirrorOnce) value = value < 0 ? -value - 1 : value;
            return Math.Clamp(value, 0, size - 1);
        }
        int i = (Address(x, texture.width) + texture.width * (Address(y, texture.height) + texture.height * Address(z, texture.depth))) * 4;
        if (data.Format == LoadedTextureFormat.RgbaFloat)
        { ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(data.Levels[0]); return new(values[i], values[i + 1], values[i + 2]); }
        if (data.Format == LoadedTextureFormat.Rgba32)
        { byte[] values = data.Levels[0]; return new Vector3(values[i], values[i + 1], values[i + 2]) / 255f; }
        throw new NotSupportedException("向量力场只接受 RGBAFloat／RGBA32，实际为 " + data.Format);
    }
}
