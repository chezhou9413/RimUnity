namespace RimUnity.Video;

//保留文件路径，准备播放时才打开解码器。
public sealed class VideoClip : Object
{
    internal string FilePath { get; }
    internal VideoClip(string path) => FilePath = path;
}
