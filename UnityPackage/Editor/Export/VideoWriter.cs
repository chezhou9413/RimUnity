using System;
using RimUnity.Serialization;
using UnityEngine.Video;

namespace RimUnity.Editor
{
    //单音轨设置以属性恢复，保留静音、音量和组件路由。
    internal static class VideoWriter
    {
        internal static void Write(VideoPlayer video, ComponentData data, ValueWriter writer, string path)
        {
            if (video.renderMode != VideoRenderMode.RenderTexture && video.renderMode != VideoRenderMode.APIOnly)
                throw new NotSupportedException(path + "：视频需要 RenderTexture 或 APIOnly 图像输出。");
            if (video.audioOutputMode == VideoAudioOutputMode.APIOnly || video.controlledAudioTrackCount > 1)
                throw new NotSupportedException(path + "：视频需要单音轨直接输出或 AudioSource 路由。");
            if (video.controlledAudioTrackCount == 0) return;
            void Put(string name, object value) => data.members.Add(new MemberData { name = name, property = true, value = writer.Write(value, path + "." + name) });
            Put("audioTrackEnabled", video.IsAudioTrackEnabled(0));
            Put("directAudioVolume", video.GetDirectAudioVolume(0));
            Put("directAudioMute", video.GetDirectAudioMute(0));
            Put("targetAudioSource", video.GetTargetAudioSource(0));
        }
    }
}
