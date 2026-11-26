using System.Runtime.Intrinsics;

namespace RimUnity;

//以解析 Perlin 梯度构造连续 Curl 噪声；不依赖 Unity 原生噪声表。
internal static partial class ParticleNoise
{
    private const float InverseSqrtTwo = .7071067811865475f;
    // Fixed numeric constants, copied out by value. No writable view or scene
    // references escape; this table has no per-game state or invalidation needs.
    private static readonly Vector3[] gradients =
    {
        new(InverseSqrtTwo, InverseSqrtTwo, 0), new(-InverseSqrtTwo, InverseSqrtTwo, 0),
        new(InverseSqrtTwo, -InverseSqrtTwo, 0), new(-InverseSqrtTwo, -InverseSqrtTwo, 0),
        new(InverseSqrtTwo, 0, InverseSqrtTwo), new(-InverseSqrtTwo, 0, InverseSqrtTwo),
        new(InverseSqrtTwo, 0, -InverseSqrtTwo), new(-InverseSqrtTwo, 0, -InverseSqrtTwo),
        new(0, InverseSqrtTwo, InverseSqrtTwo), new(0, -InverseSqrtTwo, InverseSqrtTwo),
        new(0, InverseSqrtTwo, -InverseSqrtTwo), new(0, -InverseSqrtTwo, -InverseSqrtTwo)
    };

    //低、中质量复用势场，仍保留三个空间维度，分别只需求一组、两组梯度。
    internal static Vector3 Curl(Vector3 point, uint seed, ParticleSystemNoiseQuality quality)
    {
        if (quality == ParticleSystemNoiseQuality.Low)
        {
            Vector3 gradient = PerlinGradient(point, seed);
            return new Vector3(gradient.y - gradient.z, gradient.z - gradient.x, gradient.x - gradient.y) * InverseSqrtTwo;
        }
        // All potential fields sample the same position. Reuse the lattice,
        // interpolation and hash salts within this call, without cross-frame state.
        int ix = Mathf.FloorToInt(point.x), iy = Mathf.FloorToInt(point.y), iz = Mathf.FloorToInt(point.z);
        Vector3 offset = new(point.x - ix, point.y - iy, point.z - iz);
        Vector3 fade = new(Fade(offset.x), Fade(offset.y), Fade(offset.z));
        Vector3 derivative = new(FadeDerivative(offset.x), FadeDerivative(offset.y), FadeDerivative(offset.z));
        bool high = quality == ParticleSystemNoiseQuality.High;
        if (Vector128.IsHardwareAccelerated && offset.x is >= 0 and <= 1
            && offset.y is >= 0 and <= 1 && offset.z is >= 0 and <= 1)
            return CurlCorners(ix, iy, iz, offset, fade, derivative, seed, high);
        float xy = 0, xz = 0, yx = 0, yz = 0, zx = 0, zy = 0;
        for (int z = 0; z < 2; z++)
        {
            float wz = z == 0 ? 1 - fade.z : fade.z, dz = z == 0 ? -derivative.z : derivative.z;
            for (int y = 0; y < 2; y++)
            {
                float wy = y == 0 ? 1 - fade.y : fade.y, dy = y == 0 ? -derivative.y : derivative.y;
                for (int x = 0; x < 2; x++)
                {
                    float wx = x == 0 ? 1 - fade.x : fade.x, dx = x == 0 ? -derivative.x : derivative.x;
                    uint salt = unchecked((uint)((ix + x) * 73856093 ^ (iy + y) * 19349663 ^ (iz + z) * 83492791));
                    float px = offset.x - x, py = offset.y - y, pz = offset.z - z;
                    float weight = wx * wy * wz;
                    Vector3 gx = LatticeGradient(salt, seed), gy = LatticeGradient(salt, seed ^ 0x68bc21ebu);
                    float vx = gx.x * px + gx.y * py + gx.z * pz;
                    float vy = gy.x * px + gy.y * py + gy.z * pz;
                    // Curl consumes two derivative components per field at High.
                    // Keep the original per-component arithmetic and corner order.
                    xy += gx.y * weight + vx * wx * dy * wz;
                    xz += gx.z * weight + vx * wx * wy * dz;
                    yx += gy.x * weight + vy * dx * wy * wz;
                    yz += gy.z * weight + vy * wx * wy * dz;
                    if (high)
                    {
                        Vector3 gz = LatticeGradient(salt, seed ^ 0x02e5be93u);
                        float vz = gz.x * px + gz.y * py + gz.z * pz;
                        zx += gz.x * weight + vz * dx * wy * wz;
                        zy += gz.y * weight + vz * wx * dy * wz;
                    }
                    else zx += gx.x * weight + vx * dx * wy * wz;
                }
            }
        }
        //每个分量由两项导数相减，固定缩放保持强度量级，不按位置归一化旋涡场。
        return new Vector3((high ? zy : xy) - yz, xz - zx, yx - xy) * InverseSqrtTwo;
    }

    //对八角点梯度插值直接求导，避免为 Curl 在六个邻近位置重复采样。
    private static Vector3 PerlinGradient(Vector3 point, uint seed)
    {
        int ix = Mathf.FloorToInt(point.x), iy = Mathf.FloorToInt(point.y), iz = Mathf.FloorToInt(point.z);
        Vector3 offset = new(point.x - ix, point.y - iy, point.z - iz);
        Vector3 fade = new(Fade(offset.x), Fade(offset.y), Fade(offset.z));
        Vector3 derivative = new(FadeDerivative(offset.x), FadeDerivative(offset.y), FadeDerivative(offset.z));
        Vector3 result = Vector3.zero;
        for (int z = 0; z < 2; z++)
        {
            float wz = z == 0 ? 1 - fade.z : fade.z, dz = z == 0 ? -derivative.z : derivative.z;
            for (int y = 0; y < 2; y++)
            {
                float wy = y == 0 ? 1 - fade.y : fade.y, dy = y == 0 ? -derivative.y : derivative.y;
                for (int x = 0; x < 2; x++)
                {
                    float wx = x == 0 ? 1 - fade.x : fade.x, dx = x == 0 ? -derivative.x : derivative.x;
                    Vector3 gradient = LatticeGradient(ix + x, iy + y, iz + z, seed);
                    float value = gradient.x * (offset.x - x) + gradient.y * (offset.y - y) + gradient.z * (offset.z - z);
                    float weight = wx * wy * wz;
                    result.x += gradient.x * weight + value * dx * wy * wz;
                    result.y += gradient.y * weight + value * wx * dy * wz;
                    result.z += gradient.z * weight + value * wx * wy * dz;
                }
            }
        }
        return result;
    }

    //五次插值让格点边界的一阶、二阶导数连续。
    private static float Fade(float value) => value * value * value * (value * (value * 6 - 15) + 10);

    //五次插值的解析导数。
    private static float FadeDerivative(float value) => 30 * value * value * (value - 1) * (value - 1);

    //使用现有确定性哈希选择十二个单位梯度，不推进发射器随机序列。
    private static Vector3 LatticeGradient(int x, int y, int z, uint seed)
    {
        uint salt = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791));
        return LatticeGradient(salt, seed);
    }
    private static Vector3 LatticeGradient(uint salt, uint seed)
    {
        int index = (int)(ParticleSystem.Random(seed, salt) * 12);
        return gradients[index];
    }
}
