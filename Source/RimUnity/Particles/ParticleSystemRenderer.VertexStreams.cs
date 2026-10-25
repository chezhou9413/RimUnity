using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RimUnity;

//数值流按顺序打包，位置、颜色、法线和切线保持独立顶点属性。
public sealed partial class ParticleSystemRenderer
{
    private readonly List<Vector3> normals = new();
    private readonly List<Vector4> tangents = new();
    private Vector3[] sourceNormals;
    private Vector4[] sourceTangents;
    private Vector2[] sourceUV3, sourceUV4;
    private Vector2 streamUV3, streamUV4;
    private int streamParticleIndex, streamVertexId;
    private ParticleSystem.Particle streamParticle;
    private Vector3 streamSize, streamVelocity, streamCenter, streamNormal;
    private Vector4 streamTangent;
    private float streamFrame, streamBlend;
    private Rect streamCurrentUV = new(0, 0, 1, 1), streamNextUV = new(0, 0, 1, 1);
    private readonly record struct StreamElement(ParticleSystemVertexStream Stream, int Width, int Offset);
    private StreamElement[] particleStreamPlan = Array.Empty<StreamElement>(), vertexStreamPlan = Array.Empty<StreamElement>();
    [InlineArray(12)] private struct StreamValues { private float element; }
    private StreamValues preparedStreamValues;
    private bool streamValuesPrepared;
    private enum StreamPacking { Default, UVOnly, Zero, General }
    private StreamPacking streamPacking;

    // Only numeric streams occupy TEXCOORD0..2. Position/normal/tangent/color
    // remain separate attributes; their presence or order cannot change this route.
    private static StreamPacking SelectStreamPacking(int count, StreamElement[] particle, StreamElement[] vertex)
        => count == 0 ? StreamPacking.Default
            : particle.Length == 0 && vertex.Length == 0 ? StreamPacking.Zero
            : particle.Length == 0 && vertex.Length == 1 && vertex[0].Stream == ParticleSystemVertexStream.UV
                ? StreamPacking.UVOnly : StreamPacking.General;

    // Layout metadata changes only through vertexStreams. No material/resource identity cache.
    private static (StreamElement[] Particle, StreamElement[] Vertex) BuildStreamLayout(ParticleSystemVertexStream[] streams)
    {
        var particle = new List<StreamElement>(); var vertex = new List<StreamElement>(); int offset = 0;
        foreach (var stream in streams)
        {
            int width = StreamWidth(stream);
            if (width == 0) continue;
            bool varies = stream is ParticleSystemVertexStream.UV or ParticleSystemVertexStream.UV2
                or ParticleSystemVertexStream.UV3 or ParticleSystemVertexStream.UV4 or ParticleSystemVertexStream.VertexID;
            (varies ? vertex : particle).Add(new(stream, width, offset)); offset += width;
        }
        return (particle.ToArray(), vertex.ToArray());
    }

    // This template is overwritten for every particle, before its first vertex. Trails
    // call ClearMesh and keep the original per-vertex evaluation path below.
    private void PrepareParticleStreams(Vector4 c1, Vector4 c2)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.StreamPrepare);
#endif

        streamValuesPrepared = false;
        if (streamPacking != StreamPacking.General) return;
        Span<float> values = preparedStreamValues; values.Clear();
        foreach (var item in particleStreamPlan)
        {
            Vector4 value = StreamValue(item.Stream, default, c1, c2);
            for (int i = 0; i < item.Width; i++) values[item.Offset + i] = value[i];
        }
        streamValuesPrepared = true;
    }

    //只公开有实际数据来源的流，未知类型在导入时明确失败。
    private static int StreamWidth(ParticleSystemVertexStream stream) => stream switch
    {
        ParticleSystemVertexStream.Position or ParticleSystemVertexStream.Normal or ParticleSystemVertexStream.Tangent or ParticleSystemVertexStream.Color => 0,
        ParticleSystemVertexStream.UV or ParticleSystemVertexStream.UV2 or ParticleSystemVertexStream.UV3 or ParticleSystemVertexStream.UV4 or ParticleSystemVertexStream.SizeXY
            or ParticleSystemVertexStream.StableRandomXY or ParticleSystemVertexStream.VaryingRandomXY or ParticleSystemVertexStream.Custom1XY or ParticleSystemVertexStream.Custom2XY => 2,
        ParticleSystemVertexStream.SizeXYZ or ParticleSystemVertexStream.Rotation3D or ParticleSystemVertexStream.RotationSpeed3D
            or ParticleSystemVertexStream.Center or ParticleSystemVertexStream.Velocity or ParticleSystemVertexStream.StableRandomXYZ or ParticleSystemVertexStream.VaryingRandomXYZ
            or ParticleSystemVertexStream.Custom1XYZ or ParticleSystemVertexStream.Custom2XYZ => 3,
        ParticleSystemVertexStream.StableRandomXYZW or ParticleSystemVertexStream.VaryingRandomXYZW or ParticleSystemVertexStream.Custom1XYZW or ParticleSystemVertexStream.Custom2XYZW => 4,
        ParticleSystemVertexStream.VertexID or ParticleSystemVertexStream.MeshIndex or ParticleSystemVertexStream.ParticleIndex
            or ParticleSystemVertexStream.AnimBlend or ParticleSystemVertexStream.AnimFrame or ParticleSystemVertexStream.SizeX or ParticleSystemVertexStream.Rotation
            or ParticleSystemVertexStream.RotationSpeed or ParticleSystemVertexStream.Speed or ParticleSystemVertexStream.AgePercent or ParticleSystemVertexStream.InvStartLifetime
            or ParticleSystemVertexStream.StableRandomX or ParticleSystemVertexStream.VaryingRandomX or ParticleSystemVertexStream.Custom1X or ParticleSystemVertexStream.Custom2X => 1,
        _ => throw new NotSupportedException("粒子顶点流尚未接通：" + stream)
    };

    //随机四元组保持粒子内稳定；VaryingRandom 的具体噪声序列不承诺与 Unity 相同。
    private Vector4 RandomStream(bool varying)
    {
        uint seed = streamParticle.randomSeed;
        if (varying) seed ^= (uint)(streamParticle.elapsedAge * 60);
        return new Vector4(ParticleSystem.Random(seed, 97), ParticleSystem.Random(seed, 98), ParticleSystem.Random(seed, 99), ParticleSystem.Random(seed, 100));
    }

    //动画第二帧 UV 由同一个局部坐标映射，避免帧间混合重复采样当前帧。
    private void PackStreams(Vector2 uvValue, Vector4 c1, Vector4 c2)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.StreamPack);
#endif

        // Explicit UV-only layouts have zero custom channels, unlike the empty
        // default layout, which passes custom values through. Also used by trails.
        switch (streamPacking)
        {
            case StreamPacking.Default:
                packedUV.Add(new Vector4(uvValue.x, uvValue.y, 0, 0)); custom1.Add(c1); custom2.Add(c2); return;
            case StreamPacking.UVOnly:
                packedUV.Add(new Vector4(uvValue.x, uvValue.y, 0, 0)); custom1.Add(default); custom2.Add(default); return;
            case StreamPacking.Zero:
                packedUV.Add(default); custom1.Add(default); custom2.Add(default); return;
        }
        Span<float> values = stackalloc float[12];
        if (streamValuesPrepared)
        {
            ReadOnlySpan<float> template = preparedStreamValues; template.CopyTo(values);
            foreach (var item in vertexStreamPlan)
            {
                // The layout admits only these vertex-varying streams. Write their
                // known component counts directly, preserving offsets and UV2 math.
                int offset = item.Offset;
                switch (item.Stream)
                {
                    case ParticleSystemVertexStream.UV:
                        values[offset] = uvValue.x; values[offset + 1] = uvValue.y; break;
                    case ParticleSystemVertexStream.UV2:
                        Vector4 nextUV = NextFrameUV(uvValue);
                        values[offset] = nextUV.x; values[offset + 1] = nextUV.y; break;
                    case ParticleSystemVertexStream.UV3:
                        values[offset] = streamUV3.x; values[offset + 1] = streamUV3.y; break;
                    case ParticleSystemVertexStream.UV4:
                        values[offset] = streamUV4.x; values[offset + 1] = streamUV4.y; break;
                    case ParticleSystemVertexStream.VertexID:
                        values[offset] = streamVertexId; break;
                    default: throw new InvalidOperationException("Invalid vertex stream plan: " + item.Stream);
                }
            }
        }
        else { values.Clear(); PackUnpreparedStreams(values, uvValue, c1, c2); }
        packedUV.Add(new Vector4(values[0], values[1], values[2], values[3]));
        custom1.Add(new Vector4(values[4], values[5], values[6], values[7]));
        custom2.Add(new Vector4(values[8], values[9], values[10], values[11]));
    }
    private void PackUnpreparedStreams(Span<float> values, Vector2 uvValue, Vector4 c1, Vector4 c2)
    {
        int offset = 0;
        foreach (var stream in streams)
        {
            int width = StreamWidth(stream);
            if (width == 0) continue;
            Vector4 value = StreamValue(stream, uvValue, c1, c2);
            for (int i = 0; i < width; i++) values[offset++] = value[i];
        }
    }
    private Vector4 StreamValue(ParticleSystemVertexStream stream, Vector2 uvValue, Vector4 c1, Vector4 c2)
    {
        int id = (int)stream;
        if (id >= (int)ParticleSystemVertexStream.Custom1X && id <= (int)ParticleSystemVertexStream.Custom1XYZW) return c1;
        if (id >= (int)ParticleSystemVertexStream.Custom2X && id <= (int)ParticleSystemVertexStream.Custom2XYZW) return c2;
        if (id >= (int)ParticleSystemVertexStream.StableRandomX && id <= (int)ParticleSystemVertexStream.VaryingRandomXYZW) return RandomStream(id >= (int)ParticleSystemVertexStream.VaryingRandomX);
        return stream switch
        {
            ParticleSystemVertexStream.UV => new Vector4(uvValue.x, uvValue.y, 0, 0),
            ParticleSystemVertexStream.UV3 => new Vector4(streamUV3.x, streamUV3.y, 0, 0),
            ParticleSystemVertexStream.UV4 => new Vector4(streamUV4.x, streamUV4.y, 0, 0),
            ParticleSystemVertexStream.VertexID => new Vector4(streamVertexId, 0, 0, 0),
            ParticleSystemVertexStream.ParticleIndex => new Vector4(streamParticleIndex, 0, 0, 0),
            ParticleSystemVertexStream.MeshIndex => new Vector4(MeshIndex(streamParticle), 0, 0, 0),
            ParticleSystemVertexStream.UV2 => NextFrameUV(uvValue),
            ParticleSystemVertexStream.Center => streamCenter,
            ParticleSystemVertexStream.SizeX or ParticleSystemVertexStream.SizeXY or ParticleSystemVertexStream.SizeXYZ => streamSize,
            ParticleSystemVertexStream.Rotation => new Vector4(streamParticle.rotation * Mathf.Deg2Rad, 0, 0, 0),
            ParticleSystemVertexStream.Rotation3D => streamParticle.rotation3D * Mathf.Deg2Rad,
            ParticleSystemVertexStream.RotationSpeed => new Vector4(streamParticle.angularVelocity * Mathf.Deg2Rad, 0, 0, 0),
            ParticleSystemVertexStream.RotationSpeed3D => streamParticle.angularVelocity3D * Mathf.Deg2Rad,
            ParticleSystemVertexStream.Velocity => streamVelocity,
            ParticleSystemVertexStream.Speed => new Vector4(streamVelocity.magnitude, 0, 0, 0),
            ParticleSystemVertexStream.AgePercent => new Vector4(ParticleSystem.Age(streamParticle), 0, 0, 0),
            ParticleSystemVertexStream.InvStartLifetime => new Vector4(streamParticle.startLifetime > 0 ? 1 / streamParticle.startLifetime : 0, 0, 0, 0),
            ParticleSystemVertexStream.AnimFrame => new Vector4(streamFrame, 0, 0, 0),
            ParticleSystemVertexStream.AnimBlend => new Vector4(streamBlend, 0, 0, 0),
            _ => throw new NotSupportedException("粒子数据流缺少求值器：" + stream)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector4 NextFrameUV(Vector2 uvValue)
        => new(streamNextUV.x + (uvValue.x - streamCurrentUV.x) / streamCurrentUV.width * streamNextUV.width,
            streamNextUV.y + (uvValue.y - streamCurrentUV.y) / streamCurrentUV.height * streamNextUV.height, 0, 0);
}
