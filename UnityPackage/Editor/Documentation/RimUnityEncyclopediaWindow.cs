using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed class RimUnityEncyclopediaWindow : EditorWindow
    {
        private List<ManualArticle> articles = new List<ManualArticle>();
        [SerializeField] private string search = "", selected = "";
        [SerializeField] private int section;
        private string error;
        private Vector2 navigationScroll, readingScroll;
        private GUIStyle paragraph, heading, subheading, code, selectedStyle;
        private static readonly string[] Sections = { "全部内容", "说明书", "原文·中文", "原文·English", "其他原文" };

        [MenuItem("RimUnity/百科全书", priority = 1)]
        [MenuItem("Tools/RimUnity/百科全书", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<RimUnityEncyclopediaWindow>("RimUnity 百科全书");
            window.minSize = new Vector2(840, 540);
            window.Show();
        }

        private void OnEnable() => Reload();
        private void Reload()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RimUnityEncyclopediaWindow).Assembly);
                if (package == null) throw new DirectoryNotFoundException("请通过 Package Manager 安装 RimUnity，以读取包内离线说明书。");
                articles = ManualLibrary.Load(Path.Combine(package.resolvedPath, "Documentation~"));
                error = null;
            }
            catch (Exception exception) { error = exception.Message; articles.Clear(); }
        }

        private void Styles()
        {
            if (paragraph != null) return;
            paragraph = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true, fontSize = 13, margin = new RectOffset(0, 0, 4, 8) };
            heading = new GUIStyle(paragraph) { fontSize = 21, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 14, 12) };
            subheading = new GUIStyle(paragraph) { fontSize = 16, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 14, 8) };
            code = new GUIStyle(EditorStyles.textArea) { wordWrap = true, richText = false, fontSize = 12, padding = new RectOffset(10, 10, 8, 8) };
            selectedStyle = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontStyle = FontStyle.Bold, padding = new RectOffset(8, 8, 7, 7) };
        }

        private void OnGUI()
        {
            Styles();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("RimUnity 百科全书", EditorStyles.boldLabel, GUILayout.Width(150));
                string next = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (next != search) { search = next; navigationScroll = Vector2.zero; }
                section = EditorGUILayout.Popup(section, Sections, GUILayout.Width(130));
                if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(70))) Reload();
            }
            if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
            var visible = articles.Where(a => (section == 0 || a.category == Sections[section]) && a.Matches(search)).ToList();
            if (visible.Count == 0) { EditorGUILayout.HelpBox("没有匹配的文档。可缩短搜索词或选择“全部内容”。", MessageType.Info); return; }
            var current = visible.FirstOrDefault(a => a.source == selected) ?? visible[0];
            selected = current.source;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(245)))
                {
                    navigationScroll = EditorGUILayout.BeginScrollView(navigationScroll);
                    GUILayout.Label(visible.Count + " 篇 · 标题与全文搜索", EditorStyles.miniLabel);
                    foreach (var group in visible.GroupBy(a => a.category))
                    {
                        GUILayout.Space(10); GUILayout.Label(group.Key, EditorStyles.boldLabel);
                        foreach (var article in group)
                            if (GUILayout.Button(article.title, article == current ? selectedStyle : EditorStyles.wordWrappedLabel,
                                GUILayout.MinHeight(30), GUILayout.ExpandWidth(true)))
                            { selected = article.source; readingScroll = Vector2.zero; GUI.FocusControl(null); }
                    }
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(current.source, EditorStyles.miniLabel);
                        if (GUILayout.Button("复制全文", GUILayout.Width(75))) EditorGUIUtility.systemCopyBuffer = current.body;
                        if (GUILayout.Button("打开文件", GUILayout.Width(75))) UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(current.file, 1);
                    }
                    readingScroll = EditorGUILayout.BeginScrollView(readingScroll);
                    DrawMarkdown(current);
                    GUILayout.Space(24);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawMarkdown(ManualArticle article)
        {
            string[] lines = article.body.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var block = new System.Text.StringBuilder();
                    while (++i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal)) block.AppendLine(lines[i]);
                    string text = block.ToString().TrimEnd();
                    if (GUILayout.Button("复制代码", EditorStyles.miniButton, GUILayout.Width(75))) EditorGUIUtility.systemCopyBuffer = text;
                    GUILayout.TextArea(text, code, GUILayout.ExpandWidth(true));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line)) { GUILayout.Space(4); continue; }
                int level = line.TakeWhile(c => c == '#').Count();
                if (level > 0 && level < line.Length && line[level] == ' ') line = line.Substring(level + 1);
                if (line.StartsWith("<!--", StringComparison.Ordinal)) continue;
                var links = Regex.Matches(line, @"(?<image>!)?\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)");
                string display = Regex.Replace(line, @"!?\[([^\]]+)\]\([^)]+\)", "$1");
                display = System.Security.SecurityElement.Escape(display);
                display = Regex.Replace(display, @"\*\*(.+?)\*\*", "<b>$1</b>");
                display = Regex.Replace(display, @"`([^`]+)`", "<b>$1</b>");
                GUILayout.Label(display, level == 1 ? heading : level > 1 ? subheading : paragraph);
                foreach (Match link in links)
                {
                    if (link.Groups["image"].Success) continue;
                    string target = link.Groups["url"].Value;
                    if (!GUILayout.Button("↗ " + link.Groups["label"].Value, EditorStyles.linkLabel)) continue;
                    if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) Application.OpenURL(target);
                    else
                    {
                        var resolved = ManualLibrary.ResolveLink(articles, article, target);
                        if (resolved != null) { selected = resolved.source; search = ""; section = 0; readingScroll = Vector2.zero; }
                        else EditorUtility.DisplayDialog("文档链接", "此链接指向未收录的文件：" + target + "\n请在仓库中查看对应文件。", "确定");
                    }
                }
            }
        }
    }
}
