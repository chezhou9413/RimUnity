using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace RimUnity;

internal static partial class ParticleNoise
{
    // Four lanes represent the XY corners of one Z slice. Reduce in the original
    // corner order so the sampled field stays identical, including octave sums.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Vector3 CurlCorners(int ix, int iy, int iz, Vector3 offset, Vector3 fade, Vector3 derivative,
        uint seed, bool high)
    {
        uint x0 = unchecked((uint)(ix * 73856093)), x1 = unchecked((uint)((ix + 1) * 73856093));
        uint y0 = unchecked((uint)(iy * 19349663)), y1 = unchecked((uint)((iy + 1) * 19349663));
        var xySalt = Vector128.Create(x0 ^ y0, x1 ^ y0, x0 ^ y1, x1 ^ y1);
        var px = Vector128.Create(offset.x, offset.x - 1, offset.x, offset.x - 1);
        var py = Vector128.Create(offset.y, offset.y, offset.y - 1, offset.y - 1);
        var wx = Vector128.Create(1 - fade.x, fade.x, 1 - fade.x, fade.x);
        var wy = Vector128.Create(1 - fade.y, 1 - fade.y, fade.y, fade.y);
        var dx = Vector128.Create(-derivative.x, derivative.x, -derivative.x, derivative.x);
        var dy = Vector128.Create(-derivative.y, -derivative.y, derivative.y, derivative.y);
        var wxy = wx * wy;
        float xy = 0, xz = 0, yx = 0, yz = 0, zx = 0, zy = 0;
        for (int z = 0; z < 2; z++)
        {
            var salt = (xySalt ^ Vector128.Create(unchecked((uint)((iz + z) * 83492791)))) * Vector128.Create(0x9e3779b9u);
            var wz = Vector128.Create(z == 0 ? 1 - fade.z : fade.z);
            var dz = Vector128.Create(z == 0 ? -derivative.z : derivative.z);
            var pz = Vector128.Create(offset.z - z);
            var weight = wxy * wz;
            CornerGradient(salt, seed, out var xx, out var gxY, out var gxZ);
            var vx = xx * px + gxY * py + gxZ * pz;
            CornerGradient(salt, seed ^ 0x68bc21ebu, out var gyX, out var yy, out var gyZ);
            var vy = gyX * px + yy * py + gyZ * pz;
            xy = AddCorners(xy, gxY * weight + vx * wx * dy * wz);
            xz = AddCorners(xz, gxZ * weight + vx * wx * wy * dz);
            yx = AddCorners(yx, gyX * weight + vy * dx * wy * wz);
            yz = AddCorners(yz, gyZ * weight + vy * wx * wy * dz);
            if (high)
            {
                CornerGradient(salt, seed ^ 0x02e5be93u, out var gzX, out var gzY, out var zz);
                var vz = gzX * px + gzY * py + zz * pz;
                zx = AddCorners(zx, gzX * weight + vz * dx * wy * wz);
                zy = AddCorners(zy, gzY * weight + vz * wx * dy * wz);
            }
            else zx = AddCorners(zx, xx * weight + vx * dx * wy * wz);
        }
        return new Vector3((high ? zy : xy) - yz, xz - zx, yx - xy) * InverseSqrtTwo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float AddCorners(float sum, Vector128<float> values)
        => sum + values.GetElement(0) + values.GetElement(1) + values.GetElement(2) + values.GetElement(3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CornerGradient(Vector128<uint> salt, uint seed,
        out Vector128<float> x, out Vector128<float> y, out Vector128<float> z)
    {
        var hash = Vector128.Create(seed) ^ salt;
        hash ^= hash >> 16;
        hash *= Vector128.Create(0x7feb352du);
        hash ^= hash >> 15;
        // Multiplication by a power of two exactly matches division in ParticleSystem.Random.
        var indices = Vector128.ConvertToInt32(Vector128.ConvertToSingle(hash & Vector128.Create(0xFFFFFFu))
            * Vector128.Create(1f / 16777216f) * Vector128.Create(12f));
        var a = gradients[indices.GetElement(0)];
        var b = gradients[indices.GetElement(1)];
        var c = gradients[indices.GetElement(2)];
        var d = gradients[indices.GetElement(3)];
        x = Vector128.Create(a.x, b.x, c.x, d.x);
        y = Vector128.Create(a.y, b.y, c.y, d.y);
        z = Vector128.Create(a.z, b.z, c.z, d.z);
    }
}
