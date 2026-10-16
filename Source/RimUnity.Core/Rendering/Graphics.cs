using System;
using Waylith.Rendering;

namespace RimUnity;

//将网格、材质和绘制参数提交到渲染队列。
public static class Graphics
{
    private static System.Collections.Generic.List<MeshDrawCommand> capture;
    //离屏相机复用正常绘制过程，但不把命令混入地图队列。
    public static void Capture(System.Collections.Generic.List<MeshDrawCommand> commands, Action draw)
    {
        Runtime.NativeRuntime.AssertThread();
        var previous = capture;
        capture = commands;
        try { draw(); }
        finally { capture = previous; }
    }
    public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, int submesh = 0, MaterialPropertyBlock properties = null)
    {
        Runtime.NativeRuntime.AssertThread();
        if (mesh == null || material == null) return;
        if ((uint)layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
        if ((uint)submesh >= mesh.subMeshCount) throw new ArgumentOutOfRangeException(nameof(submesh));
        var transform = Matrix4x4.ToNumerics(matrix);
        var geometry = mesh.GetDrawMesh(transform.GetDeterminant() < 0);
        var range = MeshDrawSubmission.CopyProperties(properties);
        var storage = MeshDrawSubmission.CollectDrawStorage(properties);
        byte pass = LayerPassRouter.Resolve(layer);
        for (int i = 0; i < material.passCount; i++)
        {
            var passGeometry = material.UsesTriangleEdges(i) ? mesh.TriangleEdges().GetDrawMesh(transform.GetDeterminant() < 0) : geometry;
            var command = new MeshDrawCommand(passGeometry, material.GetNativePass(i), range, transform, submesh, pass, layer: layer, storage: storage);
            if (capture != null) capture.Add(command);
            else MeshDrawSubmission.Enqueue(in command);
        }
    }
}
