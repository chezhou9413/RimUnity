using System;
using System.Runtime.InteropServices;

namespace RimUnity;

//粒子固定布局按顶点连续写入，减少逐通道跨步访问 CPU 上传缓冲。
public sealed partial class Mesh
{
    //只接管已有的完整粒子布局，其他 Mesh 继续使用通用通道上传。
    private unsafe bool UploadParticleVertices(byte* target, int mask, int stride)
    {
        if (mask != 63 || stride != sizeof(ParticleVertex)) return false;
        if (particleVertices.Count != 0)
        {
            CollectionsMarshal.AsSpan(particleVertices).CopyTo(new Span<ParticleVertex>(target, particleVertices.Count));
            return true;
        }
        var output = (ParticleVertex*)target;
        for (int i = 0; i < positions.Count; i++)
            output[i] = new ParticleVertex
            {
                Position = positions[i], Normal = normalData[i], Tangent = tangentData[i], Color = colorData[i],
                UV0 = texcoords[0][i], UV1 = texcoords[1][i], UV2 = texcoords[2][i]
            };
        return true;
    }
}
