using System.Collections.Generic;

namespace RimUnity;

public sealed partial class ParticleSystemRenderer
{
    // Renderer scratch only. Submit copies into an owned page before reuse.
    private readonly List<ParticleVertex> billboardVertices = new();
    private bool packedBillboards;
    private int GeometryVertexCount => packedBillboards ? billboardVertices.Count : vertices.Count;

    private bool CanPackBillboards(ParticleSystem system)
        => renderMode != ParticleSystemRenderMode.Mesh && streamPacking != StreamPacking.General
            && uiEntry == null && cameraUiEntry == null && !Diagnostics.ParticleDebugWindow.Capturing
            && (!system.textureSheetAnimation.enabled || system.textureSheetAnimation.mode != ParticleSystemAnimationMode.Sprites);

    private ParticleVertex PackedBillboardVertex(Vector3 position, Vector2 uvValue, Color32 color, Vector4 c1, Vector4 c2)
        => new() { Position = position, Normal = streamNormal, Tangent = streamTangent, Color = color,
            UV0 = streamPacking == StreamPacking.Zero ? default : new Vector4(uvValue.x, uvValue.y, 0, 0), UV1 = c1, UV2 = c2 };
}
