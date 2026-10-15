using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        private void Settings(Rect area)
        {
            EditorGUI.DrawRect(area, PanelColor());
            GUI.Label(new Rect(area.x + 16, area.y + 8, area.width - 32, 28), "构建设置", inventoryHeading);
            GUI.Label(new Rect(area.x + 16, area.y + 38, area.width - 32, 24), "配置 AB 包的输出目录、导出选项与工具链。修改后点击底部保存。", faintText);
            Rect viewport = new Rect(area.x, area.y + 72, area.width, Mathf.Max(1, area.height - 130));
            float width = viewport.width - 20;
            float contentHeight = 734 + profile.mappings.Length * 164;
            settingsScroll = GUI.BeginScrollView(viewport, settingsScroll, new Rect(0, 0, width, contentHeight));
            float left = 16, innerWidth = width - 32, y = 0;
            void Section(string title, string hint)
            {
                GUI.Label(new Rect(left, y, innerWidth, 26), title, inventoryHeading); y += 32;
                GUI.Label(new Rect(left, y, innerWidth, 48), hint, inventoryCaption); y += 56;
            }
            Section("内容包输出位置", "选择含 About/About.xml 的 Mod 根目录。明文资源写入其下的 1.7/Data/RimUnity；多个 AB 包可以输出到同一个 Mod。");
            int remove = -1;
            for (int i = 0; i < profile.mappings.Length; i++)
            {
                var mapping = profile.mappings[i];
                EditorGUI.DrawRect(new Rect(left, y, innerWidth, 1), DividerColor()); y += 8;
                GUI.Label(new Rect(left, y, innerWidth - 90, 24), "输出映射 " + (i + 1).ToString("00"), inventoryHeading);
                if (GUI.Button(new Rect(left + innerWidth - 76, y, 76, 28), "移除", inventoryButton)) remove = i;
                y += 34;
                GUI.Label(new Rect(left, y, 120, 32), "包名 / AB 标签", inventoryCaption);
                mapping.bundle = GUI.TextField(new Rect(left + 128, y, innerWidth - 128, 32), mapping.bundle ?? "", settingsInput);
                y += 40;
                mapping.mod = SettingsPath(new Rect(left, y, innerWidth, 32), "目标 Mod 根目录", mapping.mod, false, profile.repository);
                y += 38;
                string output = "尚未配置目标目录";
                if (!string.IsNullOrWhiteSpace(mapping.mod))
                {
                    try { output = "资源输出：" + Path.GetFullPath(Path.Combine(profile.repository ?? "", mapping.mod, "1.7/Data/RimUnity")); }
                    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
                    { output = "目录格式无效，请检查路径。"; }
                }
                GUI.Label(new Rect(left + 128, y, innerWidth - 128, 30), new GUIContent(output, output), faintText);
                y += 44;
            }
            if (GUI.Button(new Rect(left, y, 150, 32), "+ 新增输出映射", inventoryButton))
            {
                profile.mappings = profile.mappings.Concat(new[] { new RimUnityWorkflow.Mapping { bundle = "", mod = "" } }).ToArray();
                settingsScroll.y = y; Repaint();
            }
            y += 54;
            Section("导出设置", "这些开关仅调整导出副本的 Renderer，源 Prefab 保留原设置。未勾选的功能继续按原规则检查。");
            profile.disableCastShadows = GUI.Toggle(new Rect(left, y, innerWidth, 32), profile.disableCastShadows, "自动关闭投射阴影 / Cast Shadows", settingsToggle); y += 38;
            profile.disableReceiveShadows = GUI.Toggle(new Rect(left, y, innerWidth, 32), profile.disableReceiveShadows, "自动关闭接收阴影 / Receive Shadows", settingsToggle); y += 38;
            profile.disableLightProbes = GUI.Toggle(new Rect(left, y, innerWidth, 32), profile.disableLightProbes, "自动关闭光照探针 / Light Probes", settingsToggle); y += 38;
            profile.disableReflectionProbes = GUI.Toggle(new Rect(left, y, innerWidth, 32), profile.disableReflectionProbes, "自动关闭反射探针 / Reflection Probes", settingsToggle); y += 52;
            Section("制作工具链", "内容仓库用于解析相对输出路径。自定义 Shader 导出需要有效的 RimWorld 安装目录与 ShaderTool。");
            profile.repository = SettingsPath(new Rect(left, y, innerWidth, 32), "内容仓库", profile.repository, false, profile.repository); y += 42;
            profile.gameRoot = SettingsPath(new Rect(left, y, innerWidth, 32), "RimWorld 1.7", profile.gameRoot, false, profile.gameRoot); y += 42;
            profile.shaderTool = SettingsPath(new Rect(left, y, innerWidth, 32), "ShaderTool", profile.shaderTool, true, profile.repository); y += 46;
            profile.auditOnReload = GUI.Toggle(new Rect(left, y, innerWidth, 32), profile.auditOnReload, "会话首次载入时自动检查资源", settingsToggle); y += 44;
            GUI.Label(new Rect(left, y, innerWidth, 60), "AB 标签和手动入口决定发布范围，依赖由导出器自动收集。反射探针开关不会删除材质中的 Cubemap。", inventoryCaption);
            GUI.EndScrollView();
            //Array edits happen after drawing the current snapshot, avoiding mismatched rows.
            if (remove >= 0) { profile.mappings = profile.mappings.Where((_, index) => index != remove).ToArray(); Repaint(); }
            Rect actions = new Rect(area.x, area.yMax - 52, area.width, 52);
            EditorGUI.DrawRect(new Rect(actions.x, actions.y, actions.width, 1), DividerColor());
            GUI.Label(new Rect(actions.x + 16, actions.y + 8, Mathf.Max(1, actions.width - 260), 32), "配置保存到当前 Unity 工程的 ProjectSettings", faintText);
            if (PrimaryButton(new Rect(actions.xMax - 226, actions.y + 9, 210, 32), "保存配置并刷新检查"))
            {
                try { File.WriteAllText(RimUnityWorkflow.ProfilePath, JsonConvert.SerializeObject(profile, Formatting.Indented)); RefreshEntries(); }
                catch (Exception exception) { error = exception.Message; Log(exception.ToString()); Debug.LogException(exception); }
            }
        }

        private string SettingsPath(Rect area, string label, string value, bool file, string initial)
        {
            GUI.Label(new Rect(area.x, area.y, 120, 32), label, inventoryCaption);
            value = GUI.TextField(new Rect(area.x + 128, area.y, area.width - 216, 32), value ?? "", settingsInput);
            if (GUI.Button(new Rect(area.xMax - 76, area.y, 76, 32), "浏览…", inventoryButton))
            {
                string directory = initial ?? "";
                if (file && !string.IsNullOrEmpty(value))
                    try { directory = Path.GetDirectoryName(value); } catch (ArgumentException) { }
                string selected = file ? EditorUtility.OpenFilePanel("选择 RimUnity.ShaderTool.exe", directory, "exe") : EditorUtility.OpenFolderPanel(label, directory, "");
                if (!string.IsNullOrEmpty(selected)) value = selected.Replace('\\', '/');
            }
            return value;
        }
    }
}
