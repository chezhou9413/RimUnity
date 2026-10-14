using System;

namespace RimUnity;

public sealed partial class SkinnedMeshRenderer
{
    //供粒子蒙皮发射形状读取当前骨骼姿态，普通绘制仍保持 GPU 蒙皮。
    public void BakeMesh(Mesh target, bool useScale = false)
    {
        if (sharedMesh == null) throw new InvalidOperationException("蒙皮发射形状没有网格。");
        var weights = Rendering.SkinWeights.Get(sharedMesh);
        Vector3[] positions = sharedMesh.vertices, normals = sharedMesh.normals;
        Matrix4x4[] bind = sharedMesh.bindposes;
        if (bind.Length != bones.Length) throw new InvalidOperationException("蒙皮骨骼与 Bind Pose 数量不一致。");
        Matrix4x4 inverse = transform.worldToLocalMatrix;
        var pose = new Matrix4x4[bones.Length];
        for (int i = 0; i < pose.Length; i++) pose[i] = inverse * bones[i].localToWorldMatrix * bind[i];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 point = Vector3.zero, normal = Vector3.zero;
            Vector4 range = weights.ranges[i];
            for (int j = 0; j < (int)range.y; j++)
            {
                Vector4 influence = weights.influences[(int)range.x + j];
                Matrix4x4 bone = pose[(int)influence.x];
                point += bone.MultiplyPoint3x4(positions[i]) * influence.y;
                if (normals.Length == positions.Length) normal += bone.MultiplyVector(normals[i]) * influence.y;
            }
            if (range.y != 0) { positions[i] = point; if (normals.Length == positions.Length) normals[i] = normal.normalized; }
            if (useScale)
            {
                Matrix4x4 matrix = transform.localToWorldMatrix;
                positions[i] = Vector3.Scale(positions[i], new Vector3(matrix.MultiplyVector(Vector3.right).magnitude, matrix.MultiplyVector(Vector3.up).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude));
            }
        }
        target.Clear(); target.indexFormat = sharedMesh.indexFormat; target.vertices = positions; target.normals = normals;
        target.uv = sharedMesh.uv; target.uv2 = sharedMesh.uv2; target.uv3 = sharedMesh.uv3; target.uv4 = sharedMesh.uv4;
        target.colors = sharedMesh.colors; target.subMeshCount = sharedMesh.subMeshCount;
        for (int i = 0; i < target.subMeshCount; i++) target.SetTriangles(sharedMesh.GetIndices(i), i);
        target.RecalculateBounds();
    }
}
