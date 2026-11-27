using System;
using System.Runtime.InteropServices;

namespace RimUnity;

public sealed partial class ParticleSystemRenderer
{
    private void AppendTrailIndices(int start)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detail = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.IndexWrite);
#endif
        var slots = AppendSlots(indices, 6);
        slots[0] = start; slots[1] = start + 1; slots[2] = start + 2;
        slots[3] = start; slots[4] = start + 2; slots[5] = start + 3;
    }

    // Renderer-owned numeric buffers only. The spans live within AppendTrail;
    // no channel is resized or submitted until the entire trail has been written.
    private void ReserveTrail(int segments)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var detail = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.TrailReserve);
#endif
        int count = checked(segments * 4), indexCount = checked(segments * 6);
        AppendSlots(vertices, count);
        AppendSlots(uv, count);
        var packed = AppendSlots(packedUV, count);
        AppendSlots(custom1, count).Clear(); AppendSlots(custom2, count).Clear();
        AppendSlots(colors, count);
        AppendSlots(normals, count).Fill(streamNormal);
        AppendSlots(tangents, count).Fill(streamTangent);
        AppendSlots(indices, indexCount);
        if (streamPacking == StreamPacking.Zero) packed.Clear();
    }

    private void RollbackTrail(int vertexCount, int indexCount)
    {
        CollectionsMarshal.SetCount(vertices, vertexCount); CollectionsMarshal.SetCount(uv, vertexCount);
        CollectionsMarshal.SetCount(packedUV, vertexCount); CollectionsMarshal.SetCount(colors, vertexCount);
        CollectionsMarshal.SetCount(custom1, vertexCount); CollectionsMarshal.SetCount(custom2, vertexCount);
        CollectionsMarshal.SetCount(normals, vertexCount); CollectionsMarshal.SetCount(tangents, vertexCount);
        CollectionsMarshal.SetCount(indices, indexCount);
    }

    // Only constructed after reservation. The output lists stay unchanged in size
    // until the synchronous segment loop finishes or unwinds to rollback.
    private readonly ref struct ReservedTrailWriter(ParticleSystemRenderer renderer)
    {
        private readonly Span<Vector3> positions = CollectionsMarshal.AsSpan(renderer.vertices);
        private readonly Span<Vector2> coordinates = CollectionsMarshal.AsSpan(renderer.uv);
        private readonly Span<Vector4> packed = CollectionsMarshal.AsSpan(renderer.packedUV);
        private readonly Span<Color32> tint = CollectionsMarshal.AsSpan(renderer.colors);
        private readonly Span<int> triangles = CollectionsMarshal.AsSpan(renderer.indices);
        private readonly bool writePackedUV = renderer.streamPacking != StreamPacking.Zero;

        internal void WriteQuad(int v, int i, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float u0, float u1, Color32 c0, Color32 c1)
        {
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.VertexWrite))
#endif
            {
                positions[v] = p0; positions[v + 1] = p1; positions[v + 2] = p2; positions[v + 3] = p3;
                coordinates[v] = new(u0, 0); coordinates[v + 1] = new(u0, 1);
                coordinates[v + 2] = new(u1, 1); coordinates[v + 3] = new(u1, 0);
                if (writePackedUV)
                {
                    packed[v] = new(u0, 0, 0, 0); packed[v + 1] = new(u0, 1, 0, 0);
                    packed[v + 2] = new(u1, 1, 0, 0); packed[v + 3] = new(u1, 0, 0, 0);
                }
                tint[v] = c0; tint[v + 1] = c0; tint[v + 2] = c1; tint[v + 3] = c1;
            }
#if RIMUNITY_PARTICLE_ANALYSIS
            using (Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.IndexWrite))
#endif
            {
                int start = v;
                triangles[i] = start; triangles[i + 1] = start + 1; triangles[i + 2] = start + 2;
                triangles[i + 3] = start; triangles[i + 4] = start + 2; triangles[i + 5] = start + 3;
            }
        }
    }
}
