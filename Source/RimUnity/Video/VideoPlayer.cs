using System;
using Waylith.VideoDecode;

namespace RimUnity.Video;

//通过宿主视频后端解码，生命周期与 RimUnity 对象保持一致。
public sealed class VideoPlayer : MonoBehaviour
{
    private Waylith.VideoDecode.VideoPlayer player;
    private RenderTexture ownTexture;
    private VideoClip videoClip;
    private string videoUrl;
    private VideoSource videoSource;
    private bool sourceDirty = true;
    private float speed = 1;
    public VideoClip clip { get => videoClip; set { videoClip = value; videoSource = VideoSource.VideoClip; sourceDirty = true; } }
    public string url { get => videoUrl; set { videoUrl = value; videoSource = VideoSource.Url; sourceDirty = true; } }
    public VideoSource source { get => videoSource; set { videoSource = value; sourceDirty = true; } }
    public VideoRenderMode renderMode { get; set; } = VideoRenderMode.RenderTexture;
    public VideoAspectRatio aspectRatio { get; set; } = VideoAspectRatio.FitHorizontally;
    public VideoAudioOutputMode audioOutputMode { get; set; } = VideoAudioOutputMode.Direct;
    public RenderTexture targetTexture { get; set; }
    public bool playOnAwake { get; set; } = true;
    public bool waitForFirstFrame { get; set; } = true;
    public bool skipOnDrop { get; set; } = true;
    public bool isLooping { get; set; }
    public bool sendFrameReadyEvents { get; set; }
    public bool audioTrackEnabled { get; set; } = true;
    public float directAudioVolume { get; set; } = 1;
    public bool directAudioMute { get; set; }
    public AudioSource targetAudioSource { get; set; }
    public bool isPrepared => !sourceDirty && player?.IsPrepared == true;
    public bool isPlaying => player?.IsPlaying == true;
    public bool isPaused => player?.IsPaused == true;
    public Texture texture => renderMode == VideoRenderMode.RenderTexture && targetTexture ? targetTexture : ownTexture;
    public uint width => (uint)(player?.Width ?? 0);
    public uint height => (uint)(player?.Height ?? 0);
    public double length => player?.Length ?? 0;
    public double time { get => player?.Time ?? 0; set { EnsurePlayer(); player.Seek(value); } }
    public long frame { get => player?.Frame ?? -1; set { EnsurePlayer(); player.SeekFrame(value); } }
    public ulong frameCount => (ulong)(player?.FrameCount ?? 0);
    public float frameRate => (float)(player?.FrameRate ?? 0);
    public ushort audioTrackCount => player?.HasAudio == true ? (ushort)1 : (ushort)0;
    public float playbackSpeed { get => speed; set { speed = value; if (player != null) player.PlaybackSpeed = value; } }
    public delegate void EventHandler(VideoPlayer source);
    public delegate void ErrorEventHandler(VideoPlayer source, string message);
    public delegate void FrameReadyEventHandler(VideoPlayer source, long frame);
    public event EventHandler prepareCompleted, loopPointReached;
    public event ErrorEventHandler errorReceived;
    public event FrameReadyEventHandler frameReady;

    //模板和未使用组件不分配解码器、线程或视频纹理。
    private void EnsurePlayer()
    {
        Runtime.NativeRuntime.AssertThread();
        if (player == null)
        {
            player = new Waylith.VideoDecode.VideoPlayer { TargetProvider = ResolveTarget, PlaybackSpeed = speed };
            player.Prepared += () => prepareCompleted?.Invoke(this);
            player.LoopPointReached += () => loopPointReached?.Invoke(this);
            player.ErrorReceived += message =>
            {
                if (errorReceived != null) errorReceived(this, message);
                else Verse.Log.Error("[RimUnity] 视频播放失败：" + name + "：" + message);
            };
            player.FrameReady += index => { if (sendFrameReadyEvents) frameReady?.Invoke(this, index); };
        }
        if (sourceDirty)
        {
            string path = source == VideoSource.VideoClip ? videoClip?.FilePath : videoUrl;
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("视频尚未指定文件：" + name);
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) path = new Uri(path).LocalPath;
            player.Media = new VideoMedia { Path = path, Label = name };
            sourceDirty = false;
        }
        ApplySettings();
    }

    //播放与准备由主线程发起，推进使用宿主实时时钟。
    public void Prepare() { EnsurePlayer(); player.Prepare(); }
    public void Play() { EnsurePlayer(); player.Play(UnityEngine.Time.realtimeSinceStartupAsDouble); }
    public void Pause() => player?.Pause();
    public void Stop() => player?.Stop();
    //页面再次启用时也恢复自动播放，隐藏时 Stop 已经释放了上一次解码会话。
    private void OnEnable()
    {
        if (playOnAwake && (source == VideoSource.VideoClip ? videoClip != null : !string.IsNullOrWhiteSpace(videoUrl))) Play();
    }
    private void Update()
    {
        if (player == null) return;
        if (sourceDirty) { bool resume = player.IsPlaying; EnsurePlayer(); if (resume) Play(); }
        ApplySettings();
        player.Update(UnityEngine.Time.realtimeSinceStartupAsDouble);
    }

    //单音轨路由对应宿主后端，不忽略不支持的音轨。
    private static void CheckTrack(ushort index)
    {
        if (index != 0) throw new NotSupportedException("宿主视频后端只支持第一个音轨。");
    }
    public void EnableAudioTrack(ushort index, bool enabled) { CheckTrack(index); audioTrackEnabled = enabled; }
    public void SetDirectAudioVolume(ushort index, float volume) { CheckTrack(index); directAudioVolume = Mathf.Clamp01(volume); }
    public float GetDirectAudioVolume(ushort index) { CheckTrack(index); return directAudioVolume; }
    public void SetDirectAudioMute(ushort index, bool mute) { CheckTrack(index); directAudioMute = mute; }
    public bool GetDirectAudioMute(ushort index) { CheckTrack(index); return directAudioMute; }
    public void SetTargetAudioSource(ushort index, AudioSource source) { CheckTrack(index); targetAudioSource = source; }
    public AudioSource GetTargetAudioSource(ushort index) { CheckTrack(index); return targetAudioSource; }
    private void ApplySettings()
    {
        if (audioOutputMode == VideoAudioOutputMode.APIOnly) throw new NotSupportedException("视频原始音频回调尚未接通。");
        player.Looping = isLooping; player.SkipOnDrop = skipOnDrop;
        bool routed = audioOutputMode == VideoAudioOutputMode.AudioSource;
        player.AudioEnabled = audioTrackEnabled && (audioOutputMode == VideoAudioOutputMode.Direct || routed && targetAudioSource);
        player.AudioVolume = Mathf.Clamp01(routed && targetAudioSource ? targetAudioSource.volume : directAudioVolume);
        player.AudioMuted = routed && targetAudioSource ? targetAudioSource.mute : directAudioMute;
    }

    //解码器直接写入原生附件，UI 引用同一张 RenderTexture。
    private VideoTarget ResolveTarget()
    {
        if (renderMode != VideoRenderMode.RenderTexture && renderMode != VideoRenderMode.APIOnly)
            throw new NotSupportedException("视频当前支持 RenderTexture 和 APIOnly 图像输出。");
        RenderTexture output = renderMode == VideoRenderMode.RenderTexture ? targetTexture : null;
        if (!output)
        {
            if (ownTexture && (ownTexture.width != player.Width || ownTexture.height != player.Height))
            { Destroy(ownTexture); ownTexture = null; }
            ownTexture ??= new RenderTexture(player.Width, player.Height, 0, (int)UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
            output = ownTexture;
        }
        output.Target.Create();
        return new VideoTarget(output.Target.GpuTexture, output.width, output.height, (VideoAspect)aspectRatio);
    }
    //隐藏时停止音画；销毁时释放解码器与自身创建的附件。
    private void OnDisable()
    {
        Stop();
        if (ownTexture) Destroy(ownTexture);
        ownTexture = null;
    }
    private void OnDestroy() { player?.Dispose(); player = null; if (ownTexture) Destroy(ownTexture); ownTexture = null; }
}

//数值与 Unity 导出数据一致。
public enum VideoSource { VideoClip, Url }
public enum VideoRenderMode { CameraFarPlane, CameraNearPlane, RenderTexture, MaterialOverride, APIOnly }
public enum VideoAspectRatio { NoScaling, FitVertically, FitHorizontally, FitInside, FitOutside, Stretch }
public enum VideoAudioOutputMode { None, AudioSource, Direct, APIOnly }
