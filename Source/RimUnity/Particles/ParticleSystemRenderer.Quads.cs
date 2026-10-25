using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RimUnity;

public sealed partial class ParticleSystemRenderer
{
    // Default/UV-only/zero layouts have no per-corner stream evaluation.
    // Reserve four numeric slots per channel and write the original corner order.
    // General layouts retain Vertex/PackStreams, including VertexID and UV2 rules.
    private void AppendSimpleBillboard(Vector3 center, Vector3 x, Vector3 y, Rect rect, Color32 color, Vector4 c1, Vector4 c2)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.VertexWrite);
#endif

        Vector2 t0 = new(rect.xMin, rect.yMin), t1 = new(rect.xMin, rect.yMax),
            t2 = new(rect.xMax, rect.yMax), t3 = new(rect.xMax, rect.yMin);
        if (packedBillboards)
        {
            var output = FourSlots(billboardVertices);
            Vector4 customA = streamPacking == StreamPacking.Default ? c1 : default;
            Vector4 customB = streamPacking == StreamPacking.Default ? c2 : default;
            output[0] = PackedBillboardVertex(center - x - y, t0, color, customA, customB);
            output[1] = PackedBillboardVertex(center - x + y, t1, color, customA, customB);
            output[2] = PackedBillboardVertex(center + x + y, t2, color, customA, customB);
            output[3] = PackedBillboardVertex(center + x - y, t3, color, customA, customB);
            return;
        }
        AppendFour(vertices, center - x - y, center - x + y, center + x + y, center + x - y);
        AppendFour(uv, t0, t1, t2, t3);
        if (streamPacking == StreamPacking.Zero) FourSlots(packedUV).Clear();
        else AppendFour(packedUV, new(t0.x, t0.y, 0, 0), new(t1.x, t1.y, 0, 0), new(t2.x, t2.y, 0, 0), new(t3.x, t3.y, 0, 0));
        FourSlots(custom1).Fill(streamPacking == StreamPacking.Default ? c1 : default);
        FourSlots(custom2).Fill(streamPacking == StreamPacking.Default ? c2 : default);
        FourSlots(colors).Fill(color);
        FourSlots(normals).Fill(streamNormal);
        FourSlots(tangents).Fill(streamTangent);
    }

    private static void AppendFour<T>(List<T> target, T a, T b, T c, T d) where T : unmanaged
    {
        Span<T> slots = FourSlots(target);
        slots[0] = a; slots[1] = b; slots[2] = c; slots[3] = d;
    }

    // Spans never escape the synchronous write or cross a resize of this list.
    // All four slots are initialized before any mesh submission can observe them.
    private static Span<T> FourSlots<T>(List<T> target) where T : unmanaged => AppendSlots(target, 4);

    // Numeric storage only; the caller initializes the whole slice synchronously.
    private static Span<T> AppendSlots<T>(List<T> target, int length) where T : unmanaged
    {
        int start = target.Count, count = checked(start + length);
#if RIMUNITY_PARTICLE_ANALYSIS
        if (target.Capacity < count) Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.BufferGrowths);
#endif
        target.EnsureCapacity(count);
        CollectionsMarshal.SetCount(target, count);
        return CollectionsMarshal.AsSpan(target).Slice(start, length);
    }
}
