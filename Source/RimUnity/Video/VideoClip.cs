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
}
