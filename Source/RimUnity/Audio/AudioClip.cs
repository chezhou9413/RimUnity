using System;
using System.IO;
using Waylith.Audio;

namespace RimUnity;

//音频数据直接由宿主音频后端解码，资源销毁时释放声音句柄。
public sealed class AudioClip : Object
{
    internal IAudioSound Sound { get; }
    private bool ownsSound = true;
    private UnityEngine.AudioClip hostOwner;
    //宿主歌曲保留原声音所有权，桥接控件借用同一已解码声源。
    public static AudioClip FromHost(UnityEngine.AudioClip source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sound = (IAudioSound)HarmonyLib.AccessTools.Property(typeof(UnityEngine.AudioClip), "Sound").GetValue(source);
        return new AudioClip(sound) { ownsSound = false, hostOwner = source, name = source.name };
    }
    public float length => (float)Sound.LengthFrames / Math.Max(1, Sound.Frequency);
    public int channels => Sound.Channels;
    public int frequency => Sound.Frequency;
    private AudioClip(IAudioSound sound) => Sound = sound;
    public static AudioClip LoadFromFile(string path, bool streaming = false)
    {
        Runtime.NativeRuntime.AssertThread();
        var backend = AudioBackend.Current ?? throw new InvalidOperationException("宿主音频后端尚未初始化。");
        return new AudioClip(backend.LoadSound(Path.GetFullPath(path), streaming)) { name = Path.GetFileNameWithoutExtension(path) };
    }
    protected override void Release() { if (ownsSound) Sound.Release(); GC.KeepAlive(hostOwner); hostOwner = null; }
}
