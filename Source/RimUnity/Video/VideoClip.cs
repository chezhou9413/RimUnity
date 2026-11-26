using System;

namespace RimUnity.Video;

//保留文件路径或归档延迟读取回调，准备播放时才打开解码器。
public sealed class VideoClip : Object
{
    internal string FilePath { get; }
    internal Func<byte[]> DataLoader { get; }
    internal VideoClip(string path) => FilePath = path;
    internal VideoClip(string label, Func<byte[]> dataLoader)
    {
        FilePath = label;
        DataLoader = dataLoader;
    }

    public static VideoClip LoadFromMemory(string name, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("视频数据不能为空。", nameof(data));
        return new VideoClip(name ?? "", () => data) { name = name ?? "" };
    }

    public static VideoClip LoadFromMemory(string name, Func<byte[]> dataLoader)
    {
        ArgumentNullException.ThrowIfNull(dataLoader);
        return new VideoClip(name ?? "", dataLoader) { name = name ?? "" };
    }
}
