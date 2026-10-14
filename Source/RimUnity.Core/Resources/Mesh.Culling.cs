using Waylith.Graphics.Meshes;

namespace RimUnity;

//镜像变换反转三角形绕序，保持外表面与正缩放实例一致。
public sealed partial class Mesh
{
    private Waylith.Graphics.Meshes.Mesh mirrored;
    private long mirroredVersion = -1;

    //只在网格数据变化时更新镜像副本，正负缩放实例不会互相改写索引。
    internal unsafe Waylith.Graphics.Meshes.Mesh GetDrawMesh(bool reverseWinding)
    {
        Flush();
        if (!reverseWinding) return native;
        if (mirrored == null) mirrored = new() { DebugName = name + " (mirrored)" };
        if (mirroredVersion == native.Version) return mirrored;

        CpuMeshData storage = native.CpuStorage.Clone();
        void* buffer = (void*)storage.GetIndexBuffer().Pointer;
        for (int i = 0; i < native.IndexCount; i += 3)
        {
            if (native.IndexFormat == MeshIndexFormat.UInt32)
            {
                uint* indices = (uint*)buffer;
                uint second = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = second;
            }
            else
            {
                ushort* indices = (ushort*)buffer;
                ushort second = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = second;
            }
        }
        mirrored.ReplaceCpuStorage(storage);
        mirroredVersion = native.Version;
        return mirrored;
    }
}
