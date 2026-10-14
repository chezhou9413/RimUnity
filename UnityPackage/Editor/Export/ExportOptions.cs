using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RimUnity.Editor
{
    //每次导出独立设置，不改变编辑器全局偏好。
    public sealed class ExportOptions
    {
        public bool disableCastShadows, disableReceiveShadows, disableLightProbes, disableReflectionProbes;
        //用于运行时才挂入相机画布的独立 UI 预制体，不改变源对象组件。
        public bool cameraUi;
        internal bool MutatesRenderers => disableCastShadows || disableReceiveShadows || disableLightProbes || disableReflectionProbes;
        public Action<string> log;

        internal void Prepare(GameObject root, string assetPath)
        {
            if (!MutatesRenderers) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string hierarchy = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                string location = assetPath + " / " + (string.IsNullOrEmpty(hierarchy) ? root.name : hierarchy) + " / " + renderer.GetType().Name;
                void Report(string option, object previous)
                {
                    string message = "已关闭导出副本的 " + option + "：" + location + "（原值：" + previous + "）";
                    if (log != null) log(message); else Debug.Log("[RimUnity] " + message);
                }
                if (disableCastShadows && renderer.shadowCastingMode != ShadowCastingMode.Off)
                { var previous = renderer.shadowCastingMode; renderer.shadowCastingMode = ShadowCastingMode.Off; Report("Cast Shadows", previous); }
                if (disableReceiveShadows && renderer.receiveShadows)
                { renderer.receiveShadows = false; Report("Receive Shadows", true); }
                if (disableLightProbes && renderer.lightProbeUsage != LightProbeUsage.Off)
                { var previous = renderer.lightProbeUsage; renderer.lightProbeUsage = LightProbeUsage.Off; Report("Light Probes", previous); }
                if (disableReflectionProbes && renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)
                { var previous = renderer.reflectionProbeUsage; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off; Report("Reflection Probes", previous); }
            }
        }
    }
}
