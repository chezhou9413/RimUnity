using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RimUnity.Rendering;

//完整骨骼权重伴随共享 Mesh 保存，实例只上传一次，不受原生四权重 API 限制。
internal sealed class SkinWeights
{
    private static readonly ConditionalWeakTable<Mesh, SkinWeights> Resources = new();
    internal Vector4[] ranges, influences;
    internal static void Register(Mesh mesh, int[] counts, int[] indices, float[] weights)
    {
        if (counts.Length == 0 && indices.Length == 0 && weights.Length == 0) return;
        if (counts.Length != mesh.vertexCount || indices.Length != weights.Length) throw new InvalidOperationException("骨骼权重数组数量不匹配：" + mesh.name);
        var data = new SkinWeights { ranges = new Vector4[counts.Length], influences = new Vector4[weights.Length] };
        int offset = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] < 0 || counts[i] > 255 || offset + counts[i] > weights.Length) throw new InvalidOperationException("顶点骨骼影响范围无效：" + mesh.name);
            data.ranges[i] = new Vector4(offset, counts[i], 0, 0); offset += counts[i];
        }
        if (offset != weights.Length) throw new InvalidOperationException("骨骼权重存在未引用的数据：" + mesh.name);
        for (int i = 0; i < weights.Length; i++)
        {
            if (indices[i] < 0 || weights[i] < 0 || !float.IsFinite(weights[i])) throw new InvalidOperationException("骨骼权重数据无效：" + mesh.name);
            data.influences[i] = new Vector4(indices[i], weights[i], 0, 0);
        }
        Resources.Add(mesh, data);
    }
    //脚本创建的 Mesh 可继续使用熟悉的 boneWeights 四权重接口。
    internal static SkinWeights Get(Mesh mesh)
    {
        if (Resources.TryGetValue(mesh, out var data)) return data;
        var source = mesh.boneWeights;
        if (source.Length != mesh.vertexCount) throw new InvalidOperationException("网格缺少顶点骨骼权重：" + mesh.name);
        var counts = new int[source.Length]; var indices = new List<int>(); var weights = new List<float>();
        for (int i = 0; i < source.Length; i++)
        {
            var w = source[i];
            void Add(int index, float weight) { if (weight == 0) return; counts[i]++; indices.Add(index); weights.Add(weight); }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        }
        Register(mesh, counts, indices.ToArray(), weights.ToArray());
        return Resources.GetValue(mesh, _ => throw new InvalidOperationException("空蒙皮网格：" + mesh.name));
    }
}
