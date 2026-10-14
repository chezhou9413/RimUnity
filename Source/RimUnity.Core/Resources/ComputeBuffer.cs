using System;
using System.Runtime.InteropServices;
using Waylith.Rendering;

namespace RimUnity;

//结构化缓冲保留提交时的环形快照，销毁包装不会提前修改已提交数据。
public sealed class ComputeBuffer : IDisposable
{
    private readonly InstanceDataBuffer data = new();
    private bool disposed;
    private int generation = -1;
    public int count { get; }
    public int stride { get; }
    public ComputeBuffer(int count, int stride)
    {
        if (count <= 0 || stride <= 0 || stride % 4 != 0) throw new ArgumentOutOfRangeException(nameof(count), "数量必须为正数，步长必须是四的正整数倍。");
        this.count = count; this.stride = stride;
    }
    public void SetData<T>(T[] source) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(source.AsSpan());
        if (Marshal.SizeOf<T>() != stride || source.Length > count) throw new ArgumentException("缓冲数据的步长或数量与声明不符。", nameof(source));
        generation = data.Write(bytes, stride, source.Length);
    }
    internal ReadOnlySpan<byte> Bytes { get { ObjectDisposedException.ThrowIf(disposed, this); return data.GetBytes(generation); } }
    internal InstanceStorageBinding Binding(int id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (generation < 0) throw new InvalidOperationException("ComputeBuffer 尚未写入数据。");
        return new(id, data, generation);
    }
    public void Release() => Dispose();
    public void Dispose() => disposed = true;
}
