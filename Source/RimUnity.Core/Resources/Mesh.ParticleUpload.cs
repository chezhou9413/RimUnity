using System.Runtime.InteropServices;

namespace RimUnity;

//粒子固定布局按顶点连续写入，减少逐通道跨步访问 CPU 上传缓冲。
public sealed partial class Mesh
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ParticleUploadVertex
    {
        internal Vector3 Position, Normal;
        internal Vector4 Tangent;
        internal Color32 Color;
        internal Vector4 UV0, UV1, UV2;
    }

    //只接管已有的完整粒子布局，其他 Mesh 继续使用通用通道上传。
    private unsafe bool UploadParticleVertices(byte* target, int mask, int stride)
    {
        if (mask != 63 || stride != sizeof(ParticleUploadVertex)) return false;
        var output = (ParticleUploadVertex*)target;
        for (int i = 0; i < positions.Count; i++)
            output[i] = new ParticleUploadVertex
            {
                Position = positions[i], Normal = normalData[i], Tangent = tangentData[i], Color = colorData[i],
                UV0 = texcoords[0][i], UV1 = texcoords[1][i], UV2 = texcoords[2][i]
            };
        return true;
    }
}
