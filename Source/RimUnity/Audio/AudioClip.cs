using System;
using System.IO;
using Waylith.Audio;

namespace RimUnity;

//音频数据直接由宿主音频后端解码，资源销毁时释放声音句柄。
public sealed class AudioClip : Object
{
    internal IAudioSound Sound { get; }
    public float length => (float)Sound.LengthFrames / Math.Max(1, Sound.Frequency);
    public int channels => Sound.Channels;
    public int frequency => Sound.Frequency;
    private AudioClip(IAudioSound sound) => Sound = sound;
    public static AudioClip LoadFromFile(string path)
    {
        Runtime.NativeRuntime.AssertThread();
        var backend = AudioBackend.Current ?? throw new InvalidOperationException("宿主音频后端尚未初始化。");
        return new AudioClip(backend.LoadSound(Path.GetFullPath(path), streaming: false)) { name = Path.GetFileNameWithoutExtension(path) };
    }
    protected override void Release() => Sound.Release();
}
