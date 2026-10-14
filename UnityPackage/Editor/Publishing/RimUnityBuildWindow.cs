using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor { public sealed class RimUnityBuildWindow : EditorWindow
{
    private RimUnityWorkflow.Profile profile;
    private List<RimUnityWorkflow.Entry> entries = new List<RimUnityWorkflow.Entry>();
    private readonly List<RimUnityWorkflow.Entry> manual = new List<RimUnityWorkflow.Entry>();
    private readonly List<string> messages = new List<string>();
    private Vector2 resourcesScroll, settingsScroll, logScroll;
    private string search = "", error = "";
    private int tab, destination;
    private bool rebuild = true, onlyIssues, initialized;
    private GUIStyle titleStyle, subtitleStyle, pathStyle;
    private const string PresetPath = RimUnityWorkflow.WindowPresetPath;
    [Serializable] private sealed class Preset
    {
        public bool rebuild;
        public string[] uiPaths;
        public RimUnityWorkflow.Entry[] manual;
    }

    [MenuItem("RimUnity/明文资源构建管理器", priority = 0)]
    public static void Open()
    {
        var window = GetWindow<RimUnityBuildWindow>("RimUnity 构建管理");
        window.minSize = new Vector2(860, 580);
        window.Show();
    }
    private void OnEnable()
    {
        RefreshEntries();
        try
        {
            if (!File.Exists(PresetPath)) return;
            var preset = JsonConvert.DeserializeObject<Preset>(File.ReadAllText(PresetPath));
            rebuild = preset.rebuild;
            foreach (var entry in preset.manual ?? Array.Empty<RimUnityWorkflow.Entry>()) manual.Add(entry);
            RefreshEntries();
            var ui = new HashSet<string>(preset.uiPaths ?? Array.Empty<string>());
            foreach (var entry in entries) entry.ui = ui.Contains(entry.path);
        }
        catch (Exception exception) { error = "窗口预设读取失败：" + exception.Message; }
    }
    private void OnDisable()
    {
        SavePreset();
    }
    private void SavePreset()
    {
        if (!initialized) return;
        try
        {
            File.WriteAllText(PresetPath, JsonConvert.SerializeObject(new Preset {
                rebuild = rebuild, uiPaths = entries.Where(e => e.ui).Select(e => e.path).ToArray(),
                manual = manual.ToArray() }, Formatting.Indented));
        }
        catch (Exception exception) { Debug.LogWarning("窗口预设保存失败：" + exception.Message); }
    }
    private void RefreshEntries()
    {
        try
        {
            profile = profile ?? RimUnityWorkflow.Load();
            var previousUi = entries.ToDictionary(e => e.path, e => e.ui, StringComparer.OrdinalIgnoreCase);
            entries = RimUnityWorkflow.Discover(profile);
            foreach (var item in entries) if (previousUi.TryGetValue(item.path, out bool ui)) item.ui = ui;
            foreach (var item in manual)
            {
                var current = entries.FirstOrDefault(e => e.path == item.path);
                if (current == null) entries.Add(item);
                else { current.bundle = item.bundle; current.mod = item.mod; current.ui = item.ui; }
            }
            initialized = true;
            error = "";
        }
        catch (Exception exception) { error = exception.Message; }
        Repaint();
    }
    private void Styles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, margin = new RectOffset(8, 8, 10, 6) };
        subtitleStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { margin = new RectOffset(8, 8, 2, 10) };
        pathStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
    }
    private void OnGUI()
    {
        Styles();
        GUILayout.Space(8);
        GUILayout.Label("RIMUNITY  /  明文资源构建", titleStyle);
        GUILayout.Label("沿用 Unity 制作资源，按 AssetBundle 归属输出。检查、导出与版本替换在这里完成。", subtitleStyle);
        var accent = EditorGUIUtility.isProSkin ? new Color(0.20f, 0.68f, 0.78f) : new Color(0.12f, 0.48f, 0.60f);
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(1, 3, GUILayout.ExpandWidth(true)), accent);
        GUILayout.Space(10);
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (profile == null) { if (GUILayout.Button("重新载入配置")) RefreshEntries(); return; }
        GUILayout.BeginHorizontal();
        Card("源入口", entries.Count.ToString());
        Card("可发布", entries.Count(e => e.status == "pending").ToString());
        Card("检查阻碍", entries.Count(e => e.status != "pending").ToString());
        Card("输出目录", profile.mappings.Select(m => m.mod).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().Count().ToString());
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        tab = GUILayout.Toolbar(tab, new[] { "资源清单", "输出与工具配置", "执行记录" }, GUILayout.Height(28));
        GUILayout.Space(8);
        if (tab == 0) Resources(); else if (tab == 1) Settings(); else Logs();
        GUILayout.Space(8);
        Footer();
        GUILayout.Space(8);
    }
    private static void Card(string label, string value)
    {
        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label(label, EditorStyles.miniLabel);
        GUILayout.Label(value, new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
        GUILayout.EndVertical();
    }
    private string[] Destinations() => profile.mappings.Select(m => m.bundle + " → " + m.mod).ToArray();
    private void Resources()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
        onlyIssues = GUILayout.Toggle(onlyIssues, "仅看检查问题", EditorStyles.toolbarButton, GUILayout.Width(105));
        if (GUILayout.Button("刷新检查", EditorStyles.toolbarButton, GUILayout.Width(85))) RefreshEntries();
        if (GUILayout.Button("定位首个问题", EditorStyles.toolbarButton, GUILayout.Width(100))) Ping(entries.FirstOrDefault(e => e.status != "pending"));
        GUILayout.EndHorizontal();
        var visible = entries.Where(e => (!onlyIssues || e.status != "pending" || e.missingScripts > 0) &&
            (string.IsNullOrWhiteSpace(search) || (e.path + " " + e.bundle + " " + e.status).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        resourcesScroll = EditorGUILayout.BeginScrollView(resourcesScroll);
        foreach (var group in visible.GroupBy(e => e.bundle ?? "未分配"))
        {
            GUILayout.Space(6);
            GUILayout.Label(group.Key + "  ·  " + group.Count() + " 个入口", EditorStyles.boldLabel);
            foreach (var entry in group)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(Path.GetFileName(entry.path), entry.path), EditorStyles.label, GUILayout.MinWidth(170))) Ping(entry);
                GUILayout.FlexibleSpace();
                bool ui = GUILayout.Toggle(entry.ui, new GUIContent("UI", "为屏幕界面材质/Shader 导出 GUI 顶点布局"), GUILayout.Width(45));
                if (ui != entry.ui) { entry.ui = ui; SavePreset(); }
                GUILayout.Label(Status(entry), EditorStyles.miniLabel, GUILayout.Width(105));
                GUILayout.EndHorizontal();
                GUILayout.Label(entry.path, pathStyle);
                if (!string.IsNullOrEmpty(entry.error)) EditorGUILayout.HelpBox(entry.error, MessageType.Warning);
                if (entry.missingScripts > 0) EditorGUILayout.HelpBox("含 " + entry.missingScripts + " 个缺失脚本，将随资源记录警告。", MessageType.Warning);
                GUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        if (profile.mappings.Length > 0) destination = EditorGUILayout.Popup(Math.Min(destination, profile.mappings.Length - 1), Destinations());
        using (new EditorGUI.DisabledScope(profile.mappings.Length == 0))
            if (GUILayout.Button("加入 Project 中选中的资源", GUILayout.Width(215))) AddSelection();
        GUILayout.EndHorizontal();
        GUILayout.Label("清单由 AssetBundle 标签和手动加入的入口组成，发布时整份清单都会导出。单击资源名定位到 Project；依赖自动随入口导出。界面材质/Shader 请勾选 UI。", pathStyle);
    }
    private static string Status(RimUnityWorkflow.Entry entry)
    {
        if (entry.status == "pending") return entry.catalogued ? "清单已有入口" : "尚未对应清单";
        if (entry.status == "missing-script") return "缺失脚本";
        if (entry.status == "excluded-by-profile") return "配置排除";
        if (entry.status == "unsupported-shadergraph") return "Shader Graph";
        if (entry.status == "unassigned-owner") return "未分配目标";
        if (entry.status == "script-requires-adaptation") return "脚本需适配";
        return "资源需检查";
    }
    private static void Ping(RimUnityWorkflow.Entry entry)
    {
        if (entry == null) return;
        var asset = AssetDatabase.LoadMainAssetAtPath(entry.path);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
    private void AddSelection()
    {
        var mapping = profile.mappings[destination];
        foreach (var asset in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
            var entry = entries.FirstOrDefault(e => e.path == path);
            if (entry == null)
            {
                entry = new RimUnityWorkflow.Entry { path = path, type = asset.GetType().FullName, status = "pending" };
                if (asset is GameObject go)
                    entry.missingScripts = go.GetComponentsInChildren<Component>(true).Count(c => c == null);
                manual.Add(entry); entries.Add(entry);
            }
            entry.bundle = mapping.bundle; entry.mod = mapping.mod;
            if (!manual.Any(e => e.path == path)) manual.Add(entry);
        }
        SavePreset();
    }
    private void Settings()
    {
        settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
        GUILayout.Label("内容包输出位置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("选择含 About/About.xml 的 Mod 根目录。程序会在其下生成 1.7/Data/RimUnity，不要选择 exports 子目录。多个包可输出到同一个 Mod。", MessageType.Info);
        var mappings = profile.mappings.ToList();
        int remove = -1;
        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            mapping.bundle = EditorGUILayout.TextField("包名 / AB 标签", mapping.bundle);
            if (GUILayout.Button("移除", GUILayout.Width(55))) remove = i;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            mapping.mod = EditorGUILayout.TextField("目标 Mod 根目录", mapping.mod);
            if (GUILayout.Button("浏览", GUILayout.Width(55)))
            {
                string path = EditorUtility.OpenFolderPanel("选择内容 Mod 根目录", profile.repository, "");
                if (!string.IsNullOrEmpty(path)) mapping.mod = path.Replace('\\', '/');
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrWhiteSpace(mapping.mod))
                GUILayout.Label("明文输出：" + Path.GetFullPath(Path.Combine(profile.repository, mapping.mod, "1.7/Data/RimUnity")), pathStyle);
            GUILayout.EndVertical();
        }
        if (remove >= 0) mappings.RemoveAt(remove);
        if (GUILayout.Button("新增输出映射")) mappings.Add(new RimUnityWorkflow.Mapping { bundle = "", mod = "" });
        profile.mappings = mappings.ToArray();
        GUILayout.Space(14);
        GUILayout.Label("导出设置", EditorStyles.boldLabel);
        profile.disableCastShadows = EditorGUILayout.Toggle("自动关闭 Cast Shadows", profile.disableCastShadows);
        profile.disableReceiveShadows = EditorGUILayout.Toggle("自动关闭接收阴影", profile.disableReceiveShadows);
        profile.disableLightProbes = EditorGUILayout.Toggle("自动关闭光照探针", profile.disableLightProbes);
        profile.disableReflectionProbes = EditorGUILayout.Toggle("自动关闭反射探针", profile.disableReflectionProbes);
        EditorGUILayout.HelpBox("勾选后仅关闭导出副本中的对应 Renderer 设置，并记录位置；原 prefab 不变。未勾选项保持严格检查。反射探针选项不会删除材质自身的 Cubemap。设置后点击下方保存配置。", MessageType.Info);
        GUILayout.Space(14);
        GUILayout.Label("制作工具链", EditorStyles.boldLabel);
        profile.repository = Folder("内容仓库", profile.repository);
        profile.gameRoot = Folder("RimWorld 1.7", profile.gameRoot);
        GUILayout.BeginHorizontal();
        profile.shaderTool = EditorGUILayout.TextField("ShaderTool", profile.shaderTool);
        if (GUILayout.Button("浏览", GUILayout.Width(55)))
        {
            string path = EditorUtility.OpenFilePanel("选择 RimUnity.ShaderTool.exe", Path.GetDirectoryName(profile.shaderTool), "exe");
            if (!string.IsNullOrEmpty(path)) profile.shaderTool = path;
        }
        GUILayout.EndHorizontal();
        profile.auditOnReload = EditorGUILayout.Toggle("会话首次载入时检查", profile.auditOnReload);
        GUILayout.Space(12);
        EditorGUILayout.HelpBox("AB 标签就是发布范围。打上已配置包名的资源会进入清单；没有标签的独立入口，请在资源页手动加入并指定目标包。材质、贴图、Shader 和动画依赖由 RimUnity 自动收集。", MessageType.Info);
        GUILayout.Space(10);
        if (GUILayout.Button("保存配置并刷新检查", GUILayout.Height(30)))
        {
            File.WriteAllText(RimUnityWorkflow.ProfilePath, JsonConvert.SerializeObject(profile, Formatting.Indented));
            RefreshEntries();
        }
        EditorGUILayout.EndScrollView();
    }
    private static string Folder(string label, string value)
    {
        GUILayout.BeginHorizontal(); value = EditorGUILayout.TextField(label, value);
        if (GUILayout.Button("浏览", GUILayout.Width(55)))
        { string path = EditorUtility.OpenFolderPanel(label, value, ""); if (!string.IsNullOrEmpty(path)) value = path; }
        GUILayout.EndHorizontal(); return value;
    }
    private void Logs()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("打开检查报告"))
        {
            string path = Path.GetFullPath("Library/RimUnity/Reports/inventory.json");
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
        }
        if (GUILayout.Button("复制执行记录")) EditorGUIUtility.systemCopyBuffer = string.Join("\n", messages);
        if (GUILayout.Button("保存执行记录")) SaveLog();
        if (GUILayout.Button("清空记录")) messages.Clear();
        GUILayout.EndHorizontal();
        logScroll = EditorGUILayout.BeginScrollView(logScroll);
        foreach (string message in messages) GUILayout.Label(message, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }
    private void Log(string text)
    {
        messages.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
        if (messages.Count > 2000) messages.RemoveAt(0);
        Repaint();
    }
    private void SaveLog()
    {
        string directory = Path.GetFullPath("Library/RimUnity/Reports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "window-export.log");
        File.WriteAllText(path, string.Join("\n", messages));
        Debug.Log("RimUnity 窗口执行记录：" + path);
    }
    private void Footer()
    {
        var blocked = entries.Where(e => e.status != "pending").ToArray();
        if (blocked.Length > 0)
        {
            EditorGUILayout.HelpBox("清单中有 " + blocked.Length + " 个检查问题。首个：" + blocked[0].path + "\n" +
                (blocked[0].error ?? blocked[0].status), MessageType.Warning);
            if (GUILayout.Button("查看检查问题并定位首个阻碍"))
            { tab = 0; onlyIssues = true; search = ""; Ping(blocked[0]); }
        }
        rebuild = EditorGUILayout.Popup("发布方式", rebuild ? 0 : 1, new[] { "完整重建 · 成功后删除旧版本", "增量更新 · 保留其他入口" }) == 0;
        EditorGUILayout.HelpBox(rebuild ? "新清单只保留当前入口及依赖。没有 AssetBundle 归属、也不在手动入口中的旧资源会移除；全部成功后才替换和清理旧文件。" :
            "更新当前清单中的入口，保留其他旧入口及其依赖。失败或取消不替换正式目录。", MessageType.Info);
        using (new EditorGUI.DisabledScope(entries.Count == 0))
            if (GUILayout.Button(rebuild ? "完整重建并发布" : "增量导出并发布", GUILayout.Height(36)))
            {
                var choice = entries.ToArray();
                string blockers = RimUnityPublisher.DescribeBlockers(choice);
                if (blockers != null)
                {
                    error = blockers; Log(blockers); SaveLog(); tab = 0; onlyIssues = true; search = "";
                    Ping(choice.First(e => e.status != "pending"));
                    return;
                }
                if (rebuild && !EditorUtility.DisplayDialog("确认完整重建", "将以 " + choice.Length + " 个入口重建目标 Mod 的清单。没有 AssetBundle 归属的旧入口会删除。导出失败不替换正式目录。", "重建并发布", "取消")) return;
                tab = 2;
                try
                {
                    SavePreset();
                    foreach (var entry in choice)
                    {
                        var asset = AssetDatabase.LoadMainAssetAtPath(entry.path);
                        if (asset == null) throw new InvalidOperationException("源资源不存在：" + entry.path);
                        if (asset is GameObject prefab && prefab.GetComponentsInChildren<Component>(true).Any(c => c == null))
                            throw new InvalidOperationException("预制体含缺失脚本：" + entry.path);
                        var mappings = profile.mappings.Where(m => m.bundle == entry.bundle).ToArray();
                        if (mappings.Length != 1 || string.IsNullOrWhiteSpace(mappings[0].mod))
                            throw new InvalidOperationException("包必须对应一个明确的目标目录：" + entry.bundle);
                        entry.mod = mappings[0].mod;
                    }
                    RimUnityPublisher.Export(profile, choice, rebuild, Log); error = ""; RefreshEntries();
                }
                catch (Exception exception) { error = exception.Message; Log(exception.ToString()); Debug.LogException(exception); }
                finally { SaveLog(); }
            }
    }
}

}
