using System;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //插件入口保留作者熟悉的原生 Prefab 制作方式。
    public sealed class ExportWindow : EditorWindow
    {
        private GameObject prefab;
        private string logicalPath = "UI/Menu", description = "", target = "", message = "";
        private string gameRoot = "", shaderTool = "";

        [MenuItem("Tools/RimUnity/导出明文预制体")]
        private static void Open() => GetWindow<ExportWindow>("RimUnity 导出");

        //目标目录选中后记住在本机 EditorPrefs，不进入导出产物。
        private void OnEnable()
        {
            target = EditorPrefs.GetString("RimUnity.TargetMod", "");
            gameRoot = EditorPrefs.GetString("RimUnity.GameRoot", "");
            shaderTool = EditorPrefs.GetString("RimUnity.ShaderTool", "");
        }

        //出现错误时窗口显示具体节点，控制台保留完整异常。
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity → RimWorld 1.7", EditorStyles.boldLabel);
            if (GUILayout.Button("打开 RimUnity 百科全书")) RimUnityEncyclopediaWindow.Open();
            prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefab, typeof(GameObject), false);
            logicalPath = EditorGUILayout.TextField("逻辑路径", logicalPath);
            EditorGUILayout.LabelField("描述");
            description = EditorGUILayout.TextArea(description, GUILayout.Height(70));
            EditorGUILayout.LabelField("目标 Mod", target);
            if (GUILayout.Button("选择 Mod 目录"))
            {
                string selected = EditorUtility.OpenFolderPanel("选择含 About/About.xml 的目录", target, "");
                if (!string.IsNullOrEmpty(selected)) { target = selected; EditorPrefs.SetString("RimUnity.TargetMod", target); }
            }
            EditorGUILayout.HelpBox("当前支持能力以包内 README 为准。不支持的组件和设置将阻止提交，保留上一份成功清单。", MessageType.Info);
            EditorGUILayout.LabelField("自定义 Shader 转换", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            gameRoot = EditorGUILayout.TextField("RimWorld 安装目录", gameRoot);
            shaderTool = EditorGUILayout.TextField("ShaderTool exe", shaderTool);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString("RimUnity.GameRoot", gameRoot);
                EditorPrefs.SetString("RimUnity.ShaderTool", shaderTool);
            }
            if (GUILayout.Button("选择离线 Shader 工具"))
            {
                string selected = EditorUtility.OpenFilePanel("选择 RimUnity.ShaderTool.exe", string.IsNullOrEmpty(shaderTool) ? "" : System.IO.Path.GetDirectoryName(shaderTool), "exe");
                if (!string.IsNullOrEmpty(selected)) { shaderTool = selected; EditorPrefs.SetString("RimUnity.ShaderTool", shaderTool); }
            }
            using (new EditorGUI.DisabledScope(prefab == null || string.IsNullOrWhiteSpace(target)))
                if (GUILayout.Button("导出"))
                    try { PrefabExporter.Export(target, new ExportRequest { prefab = prefab, path = logicalPath, description = description }); message = "导出成功。游戏表现仍需验收。"; }
                    catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
        }
    }
}
