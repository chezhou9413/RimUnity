using System;
using Waylith.Rendering;

namespace RimUnity;

//将网格、材质和绘制参数提交到渲染队列。
public static class Graphics
{
    public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, int submesh = 0, MaterialPropertyBlock properties = null)
    {
        Runtime.NativeRuntime.AssertThread();
        if (mesh == null || material == null) return;
        if ((uint)layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
        if ((uint)submesh >= mesh.subMeshCount) throw new ArgumentOutOfRangeException(nameof(submesh));
        var geometry = mesh.EngineMesh;
        var range = MeshDrawSubmission.CopyProperties(properties);
        var storage = MeshDrawSubmission.CollectDrawStorage(properties);
        var transform = Matrix4x4.ToNumerics(matrix);
        byte pass = LayerPassRouter.Resolve(layer);
        for (int i = 0; i < material.passCount; i++)
        {
            var command = new MeshDrawCommand(geometry, material.GetNativePass(i), range, transform, submesh, pass, layer: layer, storage: storage);
            MeshDrawSubmission.Enqueue(in command);
        }
    }
}
