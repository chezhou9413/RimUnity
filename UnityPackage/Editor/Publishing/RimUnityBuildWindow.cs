using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor { public sealed partial class RimUnityBuildWindow : EditorWindow
{
    private RimUnityWorkflow.Profile profile;
    private List<RimUnityWorkflow.Entry> entries = new List<RimUnityWorkflow.Entry>();
    private readonly List<RimUnityWorkflow.Entry> manual = new List<RimUnityWorkflow.Entry>();
    private readonly List<string> messages = new List<string>();
    private Vector2 resourcesScroll, settingsScroll, logScroll;
    private string search = "", error = "";
    private int tab;
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
        window.minSize = new Vector2(1100, 650);
        window.Show();
    }
    private void OnEnable()
    {
        minSize = new Vector2(760, 600);
        LoadExportHistory();
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
        catch (Exception exception) { error = "窗口预设读取失败：" + exception.Message; Log(error); Debug.LogException(exception); }
    }
    private void OnDisable()
    {
        SavePreset();
        inventoryAssets.Clear();
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
    private void RefreshEntries(bool reloadProfile = false)
    {
        try
        {
            profile = reloadProfile || profile == null ? RimUnityWorkflow.Load() : profile;
            var previousUi = entries.ToDictionary(e => e.path, e => e.ui, StringComparer.OrdinalIgnoreCase);
            entries = RimUnityWorkflow.Discover(profile);
            foreach (var item in entries) if (previousUi.TryGetValue(item.path, out bool ui)) item.ui = ui;
            foreach (var item in manual)
            {
                var current = entries.FirstOrDefault(e => e.path == item.path);
                if (current == null) entries.Add(item);
                else { current.bundle = item.bundle; current.mod = item.mod; current.ui = item.ui; }
            }
            RefreshDuplicates();
            RebuildInventory();
            initialized = true;
            error = "";
        }
        catch (Exception exception) { error = exception.Message; Log(exception.ToString()); Debug.LogException(exception); }
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
        InventoryStyles();
        DrawBuildHeader();
        var layout = InventoryGeometry.Calculate(position.width, position.height, !string.IsNullOrEmpty(error));
        Rect body = InventoryRect(layout.Content);
        if (!string.IsNullOrEmpty(error))
        {
            EditorGUI.HelpBox(InventoryRect(layout.Error), error, MessageType.Error);
        }
        if (profile == null)
        {
            if (GUI.Button(new Rect(body.x + 12, body.y + 12, 160, 34), "重新载入配置", inventoryButton)) RefreshEntries();
            return;
        }
        if (tab == 0) Resources(layout);
        else if (tab == 1) Settings(body);
        else Logs(body);
        Footer(InventoryRect(layout.Footer));
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
        var mapping = profile.mappings.FirstOrDefault(m => m.bundle == batchBundle)
            ?? profile.mappings.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.bundle));
        if (mapping == null) return;
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
        RefreshEntries();
    }
    private void Log(string text)
    {
        messages.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
        if (activeExport != null) activeExport.messages.Add(messages[messages.Count - 1]);
        if (messages.Count > 2000) messages.RemoveAt(0);
        Repaint();
    }
    private void SaveLog()
    {
        string directory = Path.GetFullPath("Library/RimUnity/Reports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "window-export.log");
        File.WriteAllText(path, string.Join("\n", messages));
        SaveExportHistory();
        Debug.Log("RimUnity 窗口执行记录：" + path);
    }
    private void Footer(Rect area)
    {
        EditorGUI.DrawRect(area, PanelColor());
        EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1), DividerColor());
        GUI.Label(new Rect(12, area.y + 12, Mathf.Max(100, area.width - 395), 34),
            inventory.Length + " 个资源 · " + bundleNames.Length + " 个 AB 包 · " + inventory.Count(r => r.Unassigned) + " 个未归属", inventoryCaption);
        float x = area.xMax - 374;
        rebuild = EditorGUI.Popup(new Rect(x, area.y + 13, 104, 32), rebuild ? 0 : 1, new[] { "完整重建", "增量更新" }, inventoryPopup) == 0;
        using (new EditorGUI.DisabledScope(resourceSelection.Count == 0))
            if (GUI.Button(new Rect(x + 114, area.y + 13, 110, 32), new GUIContent("导出选中", "增量导出选中资源，保留其他已发布入口"), inventoryButton)) Publish(SelectedEntries(), false);
        using (new EditorGUI.DisabledScope(entries.Count == 0))
            if (PrimaryButton(new Rect(x + 234, area.y + 13, 128, 32), rebuild ? "构建并导出" : "增量导出")) Publish(entries.ToArray(), rebuild);
    }
    private void Publish(RimUnityWorkflow.Entry[] choice, bool full)
    {
        if (choice.Length == 0) return;
        BeginExportHistory(choice, full);
        try
        {
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
            activeExport.targets = choice.Select(e => Path.GetFullPath(Path.Combine(profile.repository, e.mod, "1.7/Data/RimUnity")))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var inputPaths = new HashSet<string>(choice.Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            activeExport.dependencies = AssetDatabase.GetDependencies(inputPaths.ToArray(), true)
                .Where(p => !inputPaths.Contains(p) && !AssetDatabase.IsValidFolder(p)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            string blockers = RimUnityPublisher.DescribeBlockers(choice);
            if (blockers != null) throw new InvalidOperationException(blockers);
            if (full && !EditorUtility.DisplayDialog("确认完整重建", "将以 " + choice.Length + " 个入口重建目标 Mod 的清单。没有 AssetBundle 归属的旧入口会删除。导出失败不替换正式目录。", "重建并发布", "取消"))
            { activeExport.status = "已取消"; Log("用户取消完整重建，未执行导出。"); return; }
            tab = 2;
            SavePreset();
            RimUnityPublisher.Export(profile, choice, full, Log);
            activeExport.status = "成功";
            try { activeExport.CaptureOutputs(); }
            catch (Exception reportError) { Log("发布已成功，但输出快照读取失败：" + reportError.Message); }
            error = ""; RefreshEntries();
        }
        catch (Exception exception) { activeExport.status = exception is OperationCanceledException ? "已取消" : "失败"; error = exception.Message; Log(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            activeExport.finished = DateTime.Now;
            activeExport = null;
            InvalidateExportDetails();
            try { SaveLog(); } catch (Exception reportError) { Debug.LogWarning("导出记录保存失败：" + reportError.Message); }
        }
    }
}

}
