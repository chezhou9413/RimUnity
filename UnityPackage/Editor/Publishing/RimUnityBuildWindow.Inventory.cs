using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        private const float InventoryRowHeight = 32;
        private const string InventoryDragKey = "RimUnity.ResourceInventory.Paths";
        private ResourceInventory.Row[] inventory = Array.Empty<ResourceInventory.Row>(), visibleInventory = Array.Empty<ResourceInventory.Row>();
        private string[] bundleNames = Array.Empty<string>(), inventoryTypes = new[] { "全部类型" };
        private readonly HashSet<string> resourceSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        //Window-only preview handles; cleared on refresh/close, no ownership of imported assets.
        private readonly Dictionary<string, Object> inventoryAssets = new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
        private Vector2 bundleScroll;
        private Vector2 dragOrigin;
        private int inventoryDragControl;
        private string packageSearch = "", selectionAnchor, dragPath;
        private bool onlyShared, inventoryDirty = true, sortDescending;
        private int inventoryType, inventorySort, inventoryUi;
        private GUIStyle rowText, faintText;

        private void RebuildInventory()
        {
            var usage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(entries.Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            var tags = AssetDatabase.GetAllAssetBundleNames();
            bundleNames = tags.Concat(profile.mappings.Select(m => m.bundle)).Concat(entries.Select(e => e.bundle))
                .Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().OrderBy(b => b, StringComparer.Ordinal).ToArray();
            foreach (string bundle in tags)
                foreach (string path in AssetDatabase.GetAssetPathsFromAssetBundle(bundle))
                    if (!AssetDatabase.IsValidFolder(path)) paths.Add(path);
            foreach (var group in entries.Where(e => !string.IsNullOrEmpty(e.bundle)).GroupBy(e => e.bundle))
                foreach (string path in AssetDatabase.GetDependencies(group.Select(e => e.path).ToArray(), true))
                {
                    if (!IsInventoryAsset(path)) continue;
                    paths.Add(path);
                    if (!usage.TryGetValue(path, out var bundles)) usage.Add(path, bundles = new HashSet<string>(StringComparer.Ordinal));
                    bundles.Add(group.Key);
                }
            var assets = new List<ResourceInventory.Asset>();
            foreach (string path in paths.Where(IsInventoryAsset))
            {
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                var importer = AssetImporter.GetAtPath(path);
                long bytes = 0;
                try { if (File.Exists(path)) bytes = new FileInfo(path).Length; }
                catch (IOException) { }
                assets.Add(new ResourceInventory.Asset { path = path, type = type == typeof(GameObject) ? "Prefab" : type?.Name,
                    taggedBundle = importer == null || string.IsNullOrEmpty(importer.assetBundleVariant)
                        ? importer?.assetBundleName : importer.assetBundleName + "." + importer.assetBundleVariant, bytes = bytes });
            }
            inventory = ResourceInventory.Build(assets, entries, usage, profile, uiResourcePaths);
            inventoryTypes = new[] { "全部类型" }.Concat(inventory.Select(r => r.type).Distinct().OrderBy(t => t)).ToArray();
            inventoryType = Math.Min(inventoryType, inventoryTypes.Length - 1);
            resourceSelection.IntersectWith(inventory.Select(r => r.path));
            inventoryAssets.Clear();
            inventoryDirty = true;
        }

        private static bool IsInventoryAsset(string path) => !string.IsNullOrEmpty(path)
            && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
            && !AssetDatabase.IsValidFolder(path) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase);

        private static bool InventoryIssue(ResourceInventory.Row row) => row.entry != null
            ? row.entry.status != "pending" || row.entry.missingScripts > 0
            : !string.IsNullOrEmpty(row.bundle) && string.IsNullOrWhiteSpace(row.mod);

        private void FilterInventory()
        {
            if (!inventoryDirty) return;
            var query = inventory.Where(r => r.InBundle(bundleFilter) && r.Matches(search)
                && (inventoryType == 0 || r.type == inventoryTypes[inventoryType])
                && r.MatchesUi(inventoryUi) && (!onlyShared || r.Shared) && (!onlyIssues || InventoryIssue(r)));
            Func<ResourceInventory.Row, object> key = inventorySort == 1 ? r => r.type : inventorySort == 2 ? r => (object)r.ui
                : inventorySort == 3 ? r => r.bundle ?? "" : inventorySort == 4 ? r => r.mod ?? "" : inventorySort == 5 ? r => (object)r.bytes
                : inventorySort == 6 ? r => InventoryIssue(r) ? "需检查" : r.Shared ? "共享" : r.entry == null ? "依赖" : "就绪" : r => r.name;
            visibleInventory = (sortDescending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(r => r.path).ToArray();
            inventoryDirty = false;
        }

        private void Resources(InventoryGeometry layout)
        {
            inventoryDragControl = GUIUtility.GetControlID(FocusType.Passive);
            HandleInventoryDrag();
            EditorGUI.DrawRect(InventoryRect(layout.Sidebar), PanelColor());
            EditorGUI.DrawRect(InventoryRect(layout.Main), EditorGUIUtility.isProSkin ? new Color(.16f, .17f, .18f) : new Color(.91f, .92f, .93f));
            DrawPackageSidebar(layout);
            Rect toolbar = InventoryRect(layout.Search);
            bool compact = toolbar.height > 32;
            float searchWidth = compact ? toolbar.width : toolbar.width - 450;
            EditorGUI.BeginChangeCheck();
            search = GUI.TextField(new Rect(toolbar.x, toolbar.y, searchWidth, 32), search, inventorySearch);
            float x = compact ? toolbar.x : toolbar.x + searchWidth + 8;
            float y = compact ? toolbar.y + 38 : toolbar.y;
            inventoryType = EditorGUI.Popup(new Rect(x, y, 106, 32), inventoryType, inventoryTypes, inventoryPopup);
            inventoryUi = EditorGUI.Popup(new Rect(x + 114, y, 112, 32), inventoryUi, new[] { "全部 UI 状态", "UI 资源", "非 UI 资源" }, inventoryPopup);
            onlyShared = GUI.Toggle(new Rect(x + 234, y, 70, 32), onlyShared, "仅共享", inventoryButton);
            onlyIssues = GUI.Toggle(new Rect(x + 312, y, 70, 32), onlyIssues, "仅问题", inventoryButton);
            if (EditorGUI.EndChangeCheck()) { inventoryDirty = true; resourcesScroll = Vector2.zero; }
            FilterInventory();
            GUI.Label(new Rect(x + 390, y, 50, 32), visibleInventory.Length + " 项", faintText);
            DrawInventoryTable(InventoryRect(layout.Table));
            DrawInventorySelection(InventoryRect(layout.Selection));
            EditorGUI.DrawRect(new Rect(layout.Sidebar.Right, layout.Sidebar.y, 1, layout.Sidebar.height), DividerColor());
        }

        private void DrawPackageSidebar(InventoryGeometry layout)
        {
            Rect area = InventoryRect(layout.Sidebar);
            GUI.Label(new Rect(area.x + 12, area.y + 7, area.width - 24, 28), "AB 包", inventoryHeading);
            packageSearch = GUI.TextField(new Rect(area.x + 10, area.y + 40, area.width - 20, 30), packageSearch, inventorySearch);
            var packages = bundleNames.Where(b => string.IsNullOrWhiteSpace(packageSearch) || b.IndexOf(packageSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            Rect list = InventoryRect(layout.BundleList);
            bundleScroll = GUI.BeginScrollView(list, bundleScroll, new Rect(0, 0, list.width - 16, (packages.Length + 2) * 32));
            int index = 0;
            void Row(string bundle, string label, int count) => DrawPackageRow(new Rect(0, index++ * 32, list.width - 16, 32), bundle, label, count);
            Row("", "全部资源", inventory.Length);
            foreach (string bundle in packages) Row(bundle, bundle, inventory.Count(r => r.InBundle(bundle)));
            Row(ResourceInventory.UnassignedKey, "未归属", inventory.Count(r => r.Unassigned));
            GUI.EndScrollView();
            Rect details = InventoryRect(layout.BundleDetails);
            EditorGUI.DrawRect(new Rect(details.x, details.y, details.width, 1), DividerColor());
            string label = string.IsNullOrEmpty(bundleFilter) ? "全部资源" : bundleFilter == ResourceInventory.UnassignedKey ? "未归属" : bundleFilter;
            GUI.Label(new Rect(details.x + 12, details.y + 8, details.width - 24, 25), new GUIContent(label, label), inventoryHeading);
            var selectedPackage = inventory.Where(r => r.InBundle(bundleFilter)).ToArray();
            GUI.Label(new Rect(details.x + 12, details.y + 37, details.width - 24, 25), selectedPackage.Length + " 项 · 源文件 " + EditorUtility.FormatBytes(selectedPackage.Sum(r => r.bytes)), faintText);
            var mapping = profile.mappings.FirstOrDefault(m => m.bundle == bundleFilter);
            string output = mapping == null ? "选择左侧包查看输出归属" : "输出：" + (string.IsNullOrWhiteSpace(mapping.mod) ? "尚未配置" : mapping.mod);
            GUI.Label(new Rect(details.x + 12, details.y + 65, details.width - 24, 42), new GUIContent(output, output), inventoryCaption);
            float buttonWidth = (details.width - 30) / 2;
            Rect create = new Rect(details.x + 10, details.yMax - 44, buttonWidth, 32);
            if (GUI.Button(create, "新建 AB 包", inventoryButton)) PopupWindow.Show(create, new NewBundlePopup(this));
            if (GUI.Button(new Rect(create.xMax + 10, create.y, buttonWidth, 32), "配置输出", inventoryButton)) tab = 1;
        }

        private void DrawPackageRow(Rect rect, string bundle, string label, int count)
        {
            if (bundleFilter == bundle) EditorGUI.DrawRect(rect, SelectionColor());
            GUI.Label(new Rect(rect.x + 8, rect.y + 7, 18, 18), EditorGUIUtility.IconContent("Prefab Icon"));
            GUI.Label(new Rect(rect.x + 30, rect.y + 3, rect.width - 70, 26), new GUIContent(label, label), rowText);
            GUI.Label(new Rect(rect.xMax - 34, rect.y + 3, 32, 26), count.ToString(), faintText);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                bundleFilter = bundle;
                if (profile.mappings.Any(m => m.bundle == bundle)) batchBundle = bundle;
                inventoryDirty = true; resourcesScroll = Vector2.zero; Event.current.Use(); Repaint();
            }
            if (!string.IsNullOrEmpty(bundle) && bundle != ResourceInventory.UnassignedKey) HandlePackageDrop(rect, bundle);
        }
        private void HandlePackageDrop(Rect rect, string bundle)
        {
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform && evt.type != EventType.Repaint)) return;
            string[] paths = DragAndDrop.GetGenericData(InventoryDragKey) as string[] ?? DragAndDrop.paths;
            bool valid = paths != null && paths.Length > 0 && paths.All(p => p.StartsWith("Assets/", StringComparison.Ordinal) && IsInventoryAsset(p));
            if (!valid) return;
            if (evt.type == EventType.Repaint && DragAndDrop.visualMode == DragAndDropVisualMode.Move)
                GUI.Box(rect, GUIContent.none, "SelectionRect");
            if (evt.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Move; evt.Use(); }
            else if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                MoveInventory(paths, bundle);
                DragAndDrop.SetGenericData(InventoryDragKey, null);
                evt.Use();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawInventoryTable(Rect rect)
        {
            float width = Mathf.Max(900, rect.width - 16), flexible = width - 86;
            var starts = new[] { 30f, 30 + flexible * .34f, 30 + flexible * .46f,
                86 + flexible * .46f, 86 + flexible * .64f, 86 + flexible * .79f, 86 + flexible * .89f, width };
            Rect header = new Rect(rect.x, rect.y, rect.width, 32);
            GUI.Box(header, GUIContent.none, EditorStyles.toolbar);
            GUI.BeginGroup(header);
            bool all = visibleInventory.Length > 0 && visibleInventory.All(r => resourceSelection.Contains(r.path));
            bool chooseAll = GUI.Toggle(new Rect(6, 7, 19, 18), all, GUIContent.none);
            if (chooseAll != all)
                foreach (var row in visibleInventory) { if (chooseAll) resourceSelection.Add(row.path); else resourceSelection.Remove(row.path); }
            string[] labels = { "资源名称", "类型", "UI", "AB 归属", "输出模组", "源大小", "状态" };
            GUI.BeginGroup(new Rect(30, 0, Mathf.Max(1, header.width - 30), 32));
            for (int i = 0; i < labels.Length; i++)
            {
                Rect column = new Rect(starts[i] - 30 - resourcesScroll.x, 0, starts[i + 1] - starts[i], 32);
                if (GUI.Button(column, new GUIContent(labels[i] + (inventorySort == i ? sortDescending ? " ▾" : " ▴" : ""), i == 5 ? "源文件体积，不代表导出后或运行时内存大小" : labels[i]), inventoryButton))
                { sortDescending = inventorySort == i && !sortDescending; inventorySort = i; inventoryDirty = true; }
            }
            GUI.EndGroup();
            GUI.EndGroup();
            Rect viewport = new Rect(rect.x, rect.y + 32, rect.width, Mathf.Max(1, rect.height - 32));
            resourcesScroll = GUI.BeginScrollView(viewport, resourcesScroll, new Rect(0, 0, width, visibleInventory.Length * InventoryRowHeight));
            int first = Mathf.Max(0, Mathf.FloorToInt(resourcesScroll.y / InventoryRowHeight));
            int last = Mathf.Min(visibleInventory.Length, first + Mathf.CeilToInt(viewport.height / InventoryRowHeight) + 2);
            for (int i = first; i < last; i++) DrawInventoryRow(visibleInventory[i], i, starts, width);
            GUI.EndScrollView();
            if (visibleInventory.Length == 0) GUI.Label(new Rect(viewport.x + 12, viewport.y + 12, viewport.width - 24, 40), "没有匹配的资源。可调整包、类型或搜索条件。", EditorStyles.wordWrappedLabel);
        }

        private Object InventoryAsset(string path)
        {
            if (!inventoryAssets.TryGetValue(path, out var asset)) inventoryAssets[path] = asset = AssetDatabase.LoadMainAssetAtPath(path);
            return asset;
        }

        private void DrawInventoryRow(ResourceInventory.Row row, int index, float[] starts, float width)
        {
            Rect rect = new Rect(0, index * InventoryRowHeight, width, InventoryRowHeight);
            bool selected = resourceSelection.Contains(row.path);
            if (selected) EditorGUI.DrawRect(rect, SelectionColor());
            else if ((index & 1) == 0) EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, .025f) : new Color(0, 0, 0, .025f));
            bool toggled = GUI.Toggle(new Rect(6, rect.y + 7, 18, 18), selected, GUIContent.none);
            if (toggled != selected) { if (toggled) resourceSelection.Add(row.path); else resourceSelection.Remove(row.path); }
            Object asset = InventoryAsset(row.path);
            Texture icon = asset == null ? AssetDatabase.GetCachedIcon(row.path) : AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            if (icon != null) GUI.DrawTexture(new Rect(starts[0] + 4, rect.y + 4, 24, 24), icon, ScaleMode.ScaleToFit);
            if (asset != null && AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID())) Repaint();
            GUI.Label(new Rect(starts[0] + 30, rect.y + 3, starts[1] - starts[0] - 30, 22), new GUIContent(row.name, row.path), rowText);
            void Cell(int column, string text, string tip = null) => GUI.Label(new Rect(starts[column], rect.y + 3, starts[column + 1] - starts[column], 22), new GUIContent(text, tip ?? text), rowText);
            Cell(1, row.type);
            bool nextUi = GUI.Toggle(new Rect(starts[2] + 18, rect.y + 6, 24, 22), row.ui,
                new GUIContent("", "标记为 UI 资源；材质和 Shader 按 UI 模式导出，其余类型用于分类。"));
            if (nextUi != row.ui) SetInventoryUi(new[] { row }, nextUi);
            Cell(3, string.IsNullOrEmpty(row.bundle) ? row.Unassigned ? "未归属" : "自动依赖" : row.bundle,
                "归属：" + (row.bundle ?? "自动") + "\n引用包：" + string.Join("、", row.references));
            Cell(4, string.IsNullOrEmpty(row.mod) ? row.Shared && string.IsNullOrEmpty(row.bundle) ? "自动共享" : "未配置" : Path.GetFileName(row.mod.TrimEnd('/', '\\')), row.mod);
            Cell(5, EditorUtility.FormatBytes(row.bytes), "源文件：" + row.path + "\n不是导出后大小或内存占用");
            string status = InventoryIssue(row) ? "需检查" : row.Shared ? "共享" : row.entry != null ? "就绪" : "依赖";
            bool issue = InventoryIssue(row);
            Color indicator = issue ? new Color(.94f, .66f, .24f) : row.Shared ? new Color(.39f, .68f, .91f) : new Color(.36f, .72f, .46f);
            EditorGUI.DrawRect(new Rect(starts[6] + 7, rect.y + 13, 6, 6), indicator);
            GUI.Label(new Rect(starts[6] + 17, rect.y + 3, starts[7] - starts[6] - 17, 26),
                new GUIContent(status, row.entry == null ? "依赖随入口导出；拖动可以明确指定 AB 归属" : Status(row.entry) + "\n" + row.entry.error), rowText);
            HandleInventoryMouse(rect, row, index);
        }

        private void HandleInventoryMouse(Rect rect, ResourceInventory.Row row, int index)
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition) && evt.mousePosition.x >= 28)
            {
                if (evt.clickCount == 2) { Ping(new RimUnityWorkflow.Entry { path = row.path }); evt.Use(); return; }
                if (evt.shift && selectionAnchor != null)
                {
                    int anchor = Array.FindIndex(visibleInventory, r => r.path == selectionAnchor);
                    if (anchor >= 0) for (int i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) resourceSelection.Add(visibleInventory[i].path);
                }
                else if (evt.control || evt.command)
                { if (!resourceSelection.Add(row.path)) resourceSelection.Remove(row.path); }
                else if (!resourceSelection.Contains(row.path)) { resourceSelection.Clear(); resourceSelection.Add(row.path); }
                selectionAnchor = row.path; dragPath = row.path; dragOrigin = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                GUIUtility.hotControl = inventoryDragControl;
                evt.Use(); Repaint();
            }
        }

        private void HandleInventoryDrag()
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseUp && GUIUtility.hotControl == inventoryDragControl)
            { GUIUtility.hotControl = 0; dragPath = null; evt.Use(); return; }
            if (evt.type == EventType.DragExited) { DragAndDrop.SetGenericData(InventoryDragKey, null); dragPath = null; }
            if (evt.type != EventType.MouseDrag || dragPath == null || GUIUtility.hotControl != inventoryDragControl
                || (GUIUtility.GUIToScreenPoint(evt.mousePosition) - dragOrigin).sqrMagnitude <= 16 || resourceSelection.Count == 0) return;
            string[] paths = resourceSelection.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (paths.Any(p => !p.StartsWith("Assets/", StringComparison.Ordinal))) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = paths;
            DragAndDrop.objectReferences = paths.Select(InventoryAsset).Where(a => a != null).ToArray();
            DragAndDrop.SetGenericData(InventoryDragKey, paths);
            GUIUtility.hotControl = 0;
            DragAndDrop.StartDrag(paths.Length + " 个资源 · 拖到 AB 包更改归属");
            dragPath = null; evt.Use();
        }
        private void DrawInventorySelection(Rect area)
        {
            EditorGUI.DrawRect(area, PanelColor());
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1), DividerColor());
            var selected = inventory.Where(r => resourceSelection.Contains(r.path)).ToArray();
            string title = selected.Length == 0 ? "选择资源查看归属" : "已选择 " + selected.Length + " 个资源 · " + string.Join("、", selected.Select(r => r.type).Distinct());
            GUI.Label(new Rect(area.x + 12, area.y + 8, area.width - 24, 24), title, inventoryHeading);
            string description = selected.Length == 0 ? "将资源拖到左侧 AB 包，可以更改归属。双击资源可定位到 Project。"
                : selected.Length == 1 ? selected[0].path : "当前归属：" + string.Join("、", selected.Select(r => r.bundle ?? "自动依赖").Distinct());
            GUI.Label(new Rect(area.x + 12, area.y + 35, area.width - 24, 22), new GUIContent(description, description), faintText);
            float x = area.x + 12, y = area.y + 66;
            Rect Take(float width)
            {
                if (x + width > area.xMax - 12) { x = area.x + 12; y += 38; }
                var rect = new Rect(x, y, width, 32); x += width + 8; return rect;
            }
            var mappings = profile.mappings.Where(m => !string.IsNullOrEmpty(m.bundle)).GroupBy(m => m.bundle).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
            using (new EditorGUI.DisabledScope(selected.Length == 0 || mappings.Length == 0))
            {
                Rect popup = Take(140);
                if (mappings.Length > 0)
                {
                    int target = Mathf.Max(0, Array.FindIndex(mappings, m => m.bundle == batchBundle));
                    target = EditorGUI.Popup(popup, target, mappings.Select(m => m.bundle).ToArray(), inventoryPopup);
                    batchBundle = mappings[target].bundle;
                }
                else GUI.Label(popup, "无目标包", inventoryPopup);
                if (GUI.Button(Take(80), "迁移归属", inventoryButton)) MoveInventory(selected.Select(r => r.path).ToArray(), batchBundle);
            }
            using (new EditorGUI.DisabledScope(selected.Length == 0))
                if (GUI.Button(Take(80), "定位资源", inventoryButton))
                { Selection.objects = selected.Select(r => InventoryAsset(r.path)).Where(a => a != null).ToArray(); if (Selection.objects.Length > 0) EditorGUIUtility.PingObject(Selection.objects[0]); }
            bool hasPins = (profile.resourceOwners ?? Array.Empty<RimUnityWorkflow.ResourceOwner>()).Any(p => resourceSelection.Contains(p.path));
            using (new EditorGUI.DisabledScope(!hasPins))
                if (GUI.Button(Take(76), new GUIContent("取消指定", "清除显式共享归属策略，保留当前 AB 标签"), inventoryButton))
                {
                    profile.resourceOwners = profile.resourceOwners.Where(p => !resourceSelection.Contains(p.path)).ToArray();
                    SaveOwnership(); RefreshEntries(); GUIUtility.ExitGUI();
                }
            using (new EditorGUI.DisabledScope(selected.Length == 0))
            {
                bool mixed = EditorGUI.showMixedValue;
                try
                {
                    EditorGUI.showMixedValue = selected.Any(r => r.ui) && selected.Any(r => !r.ui);
                    bool ui = selected.Length > 0 && selected.All(r => r.ui);
                    bool nextUi = GUI.Toggle(Take(80), ui, new GUIContent("UI 资源", "批量设置所选资源的 UI 标记；与每行复选框一致"), inventoryButton);
                    if (nextUi != ui) SetInventoryUi(selected, nextUi);
                }
                finally { EditorGUI.showMixedValue = mixed; }
            }
            using (new EditorGUI.DisabledScope(mappings.Length == 0))
                if (GUI.Button(Take(128), "加入 Project 选择", inventoryButton)) AddSelection();
            if (GUI.Button(Take(50), "清空", inventoryButton)) resourceSelection.Clear();
        }
        private void SetInventoryUi(ResourceInventory.Row[] rows, bool ui)
        {
            var paths = new HashSet<string>(rows.Select(r => r.path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths) { if (ui) uiResourcePaths.Add(path); else uiResourcePaths.Remove(path); }
            foreach (var row in inventory.Where(r => paths.Contains(r.path))) row.ui = ui;
            foreach (var entry in entries.Concat(manual).Where(e => paths.Contains(e.path))) entry.ui = ui;
            inventoryDirty = true;
            SavePreset();
            Repaint();
        }
        private void MoveInventory(string[] paths, string bundle)
        {
            var previousMappings = profile.mappings;
            try
            {
                var mappings = profile.mappings.Where(m => m.bundle == bundle).ToArray();
                if (mappings.Length > 1) throw new InvalidOperationException("AB 包存在重复输出映射，请先修正：" + bundle);
                var mapping = mappings.FirstOrDefault() ?? new RimUnityWorkflow.Mapping { bundle = bundle, mod = "" };
                if (mappings.Length == 0) profile.mappings = profile.mappings.Concat(new[] { mapping }).ToArray();
                MigrateResources(paths, mapping);
                resourceSelection.Clear(); resourceSelection.UnionWith(paths); error = "";
                if (string.IsNullOrWhiteSpace(mapping.mod)) Log("AB 归属已移动；发布前请在构建设置中配置 " + bundle + " 的输出目录。");
            }
            catch (Exception exception) { profile.mappings = previousMappings; error = exception.Message; Log(exception.ToString()); Debug.LogException(exception); }
        }

        private RimUnityWorkflow.Entry[] SelectedEntries() => inventory.Where(r => resourceSelection.Contains(r.path)).Select(row =>
        {
            string bundle = row.bundle ?? (row.references.Length == 1 ? row.references[0] : null);
            return row.entry ?? new RimUnityWorkflow.Entry { path = row.path, bundle = bundle, mod = row.mod, type = row.type,
                ui = row.ui, status = string.IsNullOrEmpty(bundle) ? "unassigned-owner" : "pending" };
        }).ToArray();

        private static Color SelectionColor() => EditorGUIUtility.isProSkin ? new Color(.17f, .32f, .49f) : new Color(.56f, .74f, .94f);

        private sealed class NewBundlePopup : PopupWindowContent
        {
            private readonly RimUnityBuildWindow owner;
            private string bundle = "", mod = "", message;
            internal NewBundlePopup(RimUnityBuildWindow owner) { this.owner = owner; }
            public override Vector2 GetWindowSize() => new Vector2(390, 175);
            public override void OnGUI(Rect rect)
            {
                GUILayout.Label("新建 AB 包", EditorStyles.boldLabel);
                bundle = EditorGUILayout.TextField("包名", bundle);
                mod = EditorGUILayout.TextField("目标 Mod 根目录", mod);
                if (GUILayout.Button("选择输出目录")) { string path = EditorUtility.OpenFolderPanel("选择含 About/About.xml 的 Mod", owner.profile.repository, ""); if (!string.IsNullOrEmpty(path)) mod = path; }
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);
                if (GUILayout.Button("创建"))
                {
                    string name = bundle.Trim();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9][a-z0-9._/-]*$") || name.Contains("..")) { message = "包名使用小写字母、数字、下划线、短横线或斜线。"; return; }
                    if (owner.profile.mappings.Any(m => m.bundle == name)) { message = "该 AB 包已经存在。"; return; }
                    if (!string.IsNullOrEmpty(mod) && !File.Exists(Path.Combine(owner.profile.repository, mod, "About/About.xml"))) { message = "请选择含 About/About.xml 的 Mod 根目录。"; return; }
                    owner.profile.mappings = owner.profile.mappings.Concat(new[] { new RimUnityWorkflow.Mapping { bundle = name, mod = mod } }).ToArray();
                    owner.SaveOwnership(); owner.bundleFilter = name; owner.RefreshEntries(); editorWindow.Close();
                }
            }
        }
    }
}
