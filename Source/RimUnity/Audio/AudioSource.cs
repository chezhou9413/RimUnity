using System;
using Waylith.Audio;

namespace RimUnity;

//二维界面语音组件；活动状态、主音量和销毁均跟随 RimUnity 与宿主音频后端。
public sealed class AudioSource : MonoBehaviour
{
    private IAudioVoice voice;
    private AudioClip playingClip;
    private bool paused;
    public AudioClip clip { get; set; }
    public bool playOnAwake { get; set; } = true;
    public bool loop { get; set; }
    public bool mute { get; set; }
    public float volume { get; set; } = 1;
    public float pitch { get; set; } = 1;
    public float panStereo { get; set; }
    public float spatialBlend
    {
        get => 0;
        set { if (value != 0) throw new NotSupportedException("RimUnity AudioSource 当前只支持二维界面音频。"); }
    }
    public bool isPlaying => playingClip && !paused && voice?.IsPlaying == true;
    private void Start() { if (playOnAwake && clip) Play(); }
    public void Play()
    {
        Stop();
        if (!clip || !isActiveAndEnabled) return;
        var backend = AudioBackend.Current ?? throw new InvalidOperationException("宿主音频后端尚未初始化。");
        voice = backend.Play(clip.Sound, null, paused: true, autoRelease: true)
            ?? throw new InvalidOperationException("音频后端未能创建播放声部：" + clip.name);
        playingClip = clip;
        Apply();
        voice.SetSpatial(false, 1, 500, AudioRolloffModel.Inverse, 0);
        voice.SetPaused(false);
    }
    public void Stop() { voice?.Stop(); voice = null; playingClip = null; paused = false; }
    public void Pause() { voice?.SetPaused(true); paused = true; }
    public void UnPause() { voice?.SetPaused(false); paused = false; }
    private void Update()
    {
        if (voice == null) return;
        if (!playingClip) { Stop(); return; }
        Apply();
    }
    //后端主监听音量已由 RimWorld 设置，此处只应用组件自己的音量。
    private void Apply()
    {
        voice.SetVolume(Math.Clamp(volume, 0, 1)); voice.SetMuted(mute);
        voice.SetPitch(pitch); voice.SetPan(Math.Clamp(panStereo, -1, 1)); voice.SetLoop(loop);
    }
    private void OnDisable() => Stop();
    private void OnDestroy() => Stop();
}
