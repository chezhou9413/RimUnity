using System.Runtime.InteropServices;

namespace RimUnity;

// Internal CPU/upload contract. Field order matches the complete particle layout.
// Numeric values only; each renderer/page owns its buffer, never a borrowed span.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct ParticleVertex
{
    internal Vector3 Position, Normal;
    internal Vector4 Tangent;
    internal Color32 Color;
    internal Vector4 UV0, UV1, UV2;
}
