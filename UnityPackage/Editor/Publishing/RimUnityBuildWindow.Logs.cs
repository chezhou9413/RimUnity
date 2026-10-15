using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        private const string ExportHistoryPath = "Library/RimUnity/Reports/export-history.json";
        private List<ExportHistory> exportHistory = new List<ExportHistory>();
        private ExportHistory activeExport, selectedExport, cachedExport;
        private Vector2 exportDetailScroll;
        private int exportDetailTab, cachedDetailTab = -1;
        private string exportDetailText = "";
        private float exportDetailWidth, exportDetailHeight;

        private void LoadExportHistory()
        {
            try
            {
                if (File.Exists(ExportHistoryPath))
                    exportHistory = (JsonConvert.DeserializeObject<List<ExportHistory>>(File.ReadAllText(ExportHistoryPath)) ?? new List<ExportHistory>())
                        .Where(r => r != null).Take(50).ToList();
                foreach (var record in exportHistory.Where(r => r.status == "进行中")) record.status = "已中断";
                selectedExport = exportHistory.FirstOrDefault();
                InvalidateExportDetails();
            }
            catch (Exception exception) { Debug.LogWarning("导出历史读取失败：" + exception.Message); }
        }
        private void SaveExportHistory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExportHistoryPath));
            string temporary = ExportHistoryPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(exportHistory, Formatting.Indented));
            if (File.Exists(ExportHistoryPath)) File.Replace(temporary, ExportHistoryPath, null);
            else File.Move(temporary, ExportHistoryPath);
        }
        private void BeginExportHistory(RimUnityWorkflow.Entry[] choice, bool full)
        {
            activeExport = new ExportHistory { full = full,
                inputs = choice.Select(e => (e.type ?? "资源") + "  " + e.path + "\n  AB 包：" + e.bundle).ToArray() };
            exportHistory.Insert(0, activeExport);
            if (exportHistory.Count > 50) exportHistory.RemoveRange(50, exportHistory.Count - 50);
            selectedExport = activeExport;
            logScroll = exportDetailScroll = Vector2.zero;
            exportDetailTab = 0;
            InvalidateExportDetails();
            tab = 2;
        }
        private void InvalidateExportDetails() { cachedExport = null; Repaint(); }
        private string ExportDetails(ExportHistory record, int page)
        {
            var text = new StringBuilder();
            if (page == 0)
            {
                text.AppendLine("类型：" + (record.full ? "全量重建" : "增量导出"));
                text.AppendLine("状态：" + record.status);
                text.AppendLine("开始：" + record.started.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                if (record.finished != default)
                {
                    text.AppendLine("结束：" + record.finished.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    text.AppendLine("耗时：" + (record.finished - record.started).TotalSeconds.ToString("0.00") + " 秒");
                }
                text.AppendLine("\n本次入口：" + record.inputs.Length + " 项");
                text.AppendLine("自动依赖：" + record.dependencies.Length + " 项");
                text.AppendLine("\n目标路径");
                text.AppendLine(record.targets.Length == 0 ? "目标尚未通过配置检查。" : string.Join("\n\n", record.targets));
                text.AppendLine("\n" + (record.full ? "输出清单为本次入口及依赖的最终发布结果。" : "输出清单为增量发布后的最终结果，包含保留的旧入口及依赖。"));
                if (record.status != "成功") text.AppendLine("\n未记录成功输出，请在执行详情中查看失败或取消原因。");
            }
            else if (page == 1)
            {
                text.AppendLine("本次入口（" + record.inputs.Length + "）\n");
                text.AppendLine(string.Join("\n\n", record.inputs));
                text.AppendLine("\n自动依赖（" + record.dependencies.Length + "）\n");
                text.AppendLine(string.Join("\n", record.dependencies));
            }
            else if (page == 2)
            {
                text.AppendLine(record.full ? "本次全量发布结果" : "增量发布后的最终清单（含保留的旧资源）");
                if (record.outputs.Count == 0) text.AppendLine("\n暂无成功输出快照。请查看执行详情。");
                foreach (var output in record.outputs)
                {
                    text.AppendLine("\n目标：" + output.target);
                    text.AppendLine("\n资源（" + output.assets.Length + "）\n");
                    text.AppendLine(string.Join("\n\n", output.assets));
                    text.AppendLine("\n实际文件（" + output.files.Length + "）\n");
                    text.AppendLine(string.Join("\n", output.files));
                }
            }
            else text.AppendLine(record.messages.Count == 0 ? "暂无执行详情。" : string.Join("\n\n", record.messages));
            return text.ToString();
        }
        private string CompleteExportDetails(ExportHistory record)
            => string.Join("\n\n────────────────────────\n\n", Enumerable.Range(0, 4).Select(p => ExportDetails(record, p)));

        private void Logs(Rect area)
        {
            float sidebarWidth = Mathf.Clamp(area.width * .26f, 196, 260);
            Rect sidebar = new Rect(area.x, area.y, sidebarWidth, area.height - 52);
            Rect details = new Rect(sidebar.xMax + 8, area.y, area.width - sidebarWidth - 8, area.height - 52);
            EditorGUI.DrawRect(sidebar, PanelColor());
            EditorGUI.DrawRect(details, PanelColor());
            GUI.Label(new Rect(sidebar.x + 12, sidebar.y + 8, sidebar.width - 24, 28), "导出记录  ·  " + exportHistory.Count, inventoryHeading);
            Rect list = new Rect(sidebar.x + 6, sidebar.y + 44, sidebar.width - 12, Mathf.Max(1, sidebar.height - 50));
            logScroll = GUI.BeginScrollView(list, logScroll, new Rect(0, 0, list.width - 18, exportHistory.Count * 68));
            for (int i = 0; i < exportHistory.Count; i++)
            {
                if (i * 68 + 64 < logScroll.y || i * 68 > logScroll.y + list.height) continue;
                var record = exportHistory[i];
                Rect row = new Rect(0, i * 68, list.width - 20, 64);
                if (record == selectedExport) EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? new Color(.22f, .34f, .47f) : new Color(.65f, .78f, .92f));
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                { selectedExport = record; exportDetailScroll = Vector2.zero; InvalidateExportDetails(); }
                GUI.Label(new Rect(row.x + 8, row.y + 4, row.width - 16, 26), record.started.ToString("yyyy-MM-dd HH:mm:ss"), rowText);
                GUI.Label(new Rect(row.x + 8, row.y + 32, row.width - 16, 24), (record.full ? "全量" : "增量") + "  ·  " + record.status, faintText);
            }
            GUI.EndScrollView();
            if (selectedExport == null)
                GUI.Label(new Rect(details.x + 16, details.y + 16, details.width - 32, 80), "暂无导出记录。执行打包或导出后，在左侧按时间选择一次记录查看详情。", inventoryCaption);
            else
            {
                GUI.Label(new Rect(details.x + 12, details.y + 8, details.width - 24, 28), selectedExport.started.ToString("yyyy-MM-dd HH:mm:ss") + "  /  " + selectedExport.status, inventoryHeading);
                int page = GUI.Toolbar(new Rect(details.x + 12, details.y + 44, details.width - 24, 32), exportDetailTab,
                    new[] { "概览", "涉及资源", "输出资源", "执行详情" }, inventoryButton);
                if (page != exportDetailTab) { exportDetailTab = page; exportDetailScroll = Vector2.zero; }
                Rect viewport = new Rect(details.x + 8, details.y + 88, details.width - 16, Mathf.Max(1, details.height - 96));
                float width = viewport.width - 18;
                if (cachedExport != selectedExport || cachedDetailTab != exportDetailTab || Mathf.Abs(exportDetailWidth - width) > .5f)
                {
                    exportDetailText = ExportDetails(selectedExport, exportDetailTab);
                    exportDetailWidth = width;
                    exportDetailHeight = Mathf.Max(viewport.height, logText.CalcHeight(new GUIContent(exportDetailText), width));
                    cachedExport = selectedExport; cachedDetailTab = exportDetailTab;
                }
                exportDetailScroll = GUI.BeginScrollView(viewport, exportDetailScroll, new Rect(0, 0, width, exportDetailHeight));
                EditorGUI.SelectableLabel(new Rect(0, 0, width, exportDetailHeight), exportDetailText, logText);
                GUI.EndScrollView();
            }
            Rect actions = new Rect(area.x, area.yMax - 52, area.width, 52);
            EditorGUI.DrawRect(actions, PanelColor());
            EditorGUI.DrawRect(new Rect(actions.x, actions.y, actions.width, 1), DividerColor());
            using (new EditorGUI.DisabledScope(selectedExport == null))
            {
                if (GUI.Button(new Rect(actions.x + 12, actions.y + 10, 116, 32), "复制此次记录", inventoryButton)) EditorGUIUtility.systemCopyBuffer = CompleteExportDetails(selectedExport);
                if (GUI.Button(new Rect(actions.x + 140, actions.y + 10, 116, 32), "保存此次记录", inventoryButton))
                {
                    try
                    {
                        string path = Path.Combine(Path.GetDirectoryName(ExportHistoryPath), "export-" + selectedExport.started.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllText(path, CompleteExportDetails(selectedExport));
                        EditorUtility.RevealInFinder(Path.GetFullPath(path));
                    }
                    catch (Exception exception) { error = "记录保存失败：" + exception.Message; }
                }
            }
            GUI.Label(new Rect(actions.x + 272, actions.y + 10, actions.width - 284, 32), "保留最近 50 次 · 随工程保存在 Library", faintText);
        }
    }
}
