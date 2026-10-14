using System;
using System.Collections.Generic;
using Waylith.Audio;

namespace RimUnity;

//二维界面语音组件；活动状态、主音量和销毁均跟随 RimUnity 与宿主音频后端。
public sealed class AudioSource : MonoBehaviour
{
    private IAudioVoice voice;
    private AudioClip playingClip;
    private bool paused;
    private readonly List<(IAudioVoice voice, AudioClip clip, float scale)> oneShots = new();
    private float[] outputScratch = Array.Empty<float>();
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
    public bool isPlaying => !paused && (playingClip && voice?.IsPlaying == true || oneShots.Exists(s => s.clip && s.voice.IsPlaying));
    //活动对象恢复时重新处理自动播放，首次启用同样走这个入口。
    private void OnEnable() { if (playOnAwake && clip) Play(); }
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
    //一次性语音不替换 clip，也不打断同组件已有声部。
    public void PlayOneShot(AudioClip audio, float volumeScale = 1)
    {
        if (!audio || !isActiveAndEnabled) return;
        var backend = AudioBackend.Current ?? throw new InvalidOperationException("宿主音频后端尚未初始化。");
        var shot = backend.Play(audio.Sound, null, paused: true, autoRelease: true)
            ?? throw new InvalidOperationException("音频后端未能创建语音声部：" + audio.name);
        float scale = Math.Max(0, volumeScale);
        shot.SetSpatial(false, 1, 500, AudioRolloffModel.Inverse, 0);
        ApplyVoice(shot, scale, false); shot.SetPaused(false);
        oneShots.Add((shot, audio, scale));
        paused = false;
    }
    public void Stop()
    {
        voice?.Stop(); voice = null; playingClip = null; paused = false;
        foreach (var shot in oneShots) shot.voice.Stop();
        oneShots.Clear();
    }
    public void Pause() { voice?.SetPaused(true); foreach (var shot in oneShots) shot.voice.SetPaused(true); paused = true; }
    public void UnPause() { voice?.SetPaused(false); foreach (var shot in oneShots) shot.voice.SetPaused(false); paused = false; }
    private void Update()
    {
        if (voice != null)
        {
            if (!playingClip) { voice.Stop(); voice = null; }
            else Apply();
        }
        for (int i = oneShots.Count - 1; i >= 0; i--)
        {
            var shot = oneShots[i];
            if (!shot.clip || !paused && !shot.voice.IsPlaying) { shot.voice.Stop(); oneShots.RemoveAt(i); }
            else ApplyVoice(shot.voice, shot.scale, false);
        }
    }
    //后端主监听音量已由 RimWorld 设置，此处只应用组件自己的音量。
    private void Apply()
    {
        ApplyVoice(voice, 1, loop);
    }
    private void ApplyVoice(IAudioVoice output, float scale, bool looping)
    {
        output.SetVolume(Math.Clamp(volume, 0, 1) * scale); output.SetMuted(mute);
        output.SetPitch(pitch); output.SetPan(Math.Clamp(panStereo, -1, 1)); output.SetLoop(looping);
    }
    //按需启用后端采样，汇总当前实际播放声部，供口型读取音量。
    public void GetOutputData(float[] samples, int channel)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (channel < 0 || channel > 1) throw new ArgumentOutOfRangeException(nameof(channel));
        Array.Clear(samples);
        if (paused || mute || samples.Length == 0) return;
        if (outputScratch.Length != samples.Length) outputScratch = new float[samples.Length];
        if (voice?.IsPlaying == true) Add(voice, 1);
        foreach (var shot in oneShots) if (shot.voice.IsPlaying) Add(shot.voice, shot.scale);
        void Add(IAudioVoice output, float scale)
        {
            output.EnableOutputTap(); Array.Clear(outputScratch); output.ReadOutput(outputScratch, channel);
            float gain = Math.Clamp(volume, 0, 1) * scale;
            for (int i = 0; i < samples.Length; i++) samples[i] += outputScratch[i] * gain;
        }
    }
    private void OnDisable() => Stop();
    private void OnDestroy() => Stop();
}
