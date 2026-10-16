using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    public sealed partial class RimUnityBuildWindow
    {
        //Owned solely by this window, on the editor thread. Never persists scan results until complete.
        private ScanStepper scanRunner;
        private bool scanPending, scanReloadProfile, scanValid;
        private int scanRevision;
        private double scanAfter;
        private float scanProgress;
        private string scanStage = "", scanDetail = "", scanNotice = "";
        private bool ScanBusy => scanPending || scanRunner != null;
        private bool CanExport => scanValid && !ScanBusy;

        private void RequestScan(bool reloadProfile = false)
        {
            scanRevision++;
            scanValid = false;
            scanPending = true;
            scanReloadProfile |= reloadProfile;
            scanAfter = EditorApplication.timeSinceStartup + .2;
            if (scanRunner == null) { scanProgress = 0; scanStage = "等待扫描"; }
            EditorApplication.update -= AdvanceScan;
            EditorApplication.update += AdvanceScan;
            Repaint();
        }

        private void StopScanning()
        {
            scanRunner?.Dispose();
            scanRunner = null;
            scanPending = false;
            scanReloadProfile = false;
            scanValid = false;
            EditorApplication.update -= AdvanceScan;
        }

        private void AdvanceScan()
        {
            if (activeExport != null || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                scanStage = "等待编辑器就绪";
                Repaint();
                return;
            }
            try
            {
                if (scanPending)
                {
                    scanRunner?.Dispose();
                    scanRunner = null;
                    if (EditorApplication.timeSinceStartup < scanAfter) { Repaint(); return; }
                    bool reload = scanReloadProfile;
                    scanReloadProfile = false;
                    scanPending = false;
                    scanProgress = 0;
                    scanRunner = new ScanStepper(ScanInventory(scanRevision, reload));
                }
                if (scanRunner != null && scanRunner.Advance())
                {
                    scanRunner = null;
                    if (!scanPending) EditorApplication.update -= AdvanceScan;
                }
            }
            catch (Exception exception)
            {
                StopScanning();
                error = "资源清点失败：" + exception.Message;
                scanNotice = "扫描失败，请刷新重试";
                Log(exception.ToString());
                Debug.LogException(exception);
            }
            Repaint();
        }

        private void ReportScan(float progress, string stage, string detail = "")
        {
            scanProgress = Mathf.Max(scanProgress, Mathf.Clamp01(progress));
            scanStage = stage; scanDetail = detail ?? "";
        }

        private IEnumerator ScanInventory(int revision, bool reload)
        {
            ReportScan(0, "读取配置");
            yield return null; //Paint progress before any AssetDatabase work.
            var scanProfile = JsonConvert.DeserializeObject<RimUnityWorkflow.Profile>(JsonConvert.SerializeObject(
                reload || profile == null ? RimUnityWorkflow.Load() : profile));
            if (profile == null || reload) profile = scanProfile;
            var nextEntries = new List<RimUnityWorkflow.Entry>();
            var ui = new HashSet<string>(uiResourcePaths, StringComparer.OrdinalIgnoreCase);
            var manualCopy = JsonConvert.DeserializeObject<RimUnityWorkflow.Entry[]>(JsonConvert.SerializeObject(manual));
            foreach (var entry in RimUnityWorkflow.DiscoverSteps(scanProfile,
                (done, total, path) => ReportScan(.02f + .18f * done / Math.Max(1, total), "检查入口", path)))
            { nextEntries.Add(entry); yield return null; }
            foreach (var entry in manualCopy)
            {
                var found = nextEntries.FirstOrDefault(e => e.path == entry.path);
                if (found == null) nextEntries.Add(entry);
                else { found.bundle = entry.bundle; found.mod = entry.mod; }
            }
            if (!initialized) ui.UnionWith(nextEntries.Where(e => e.ui).Select(e => e.path));
            foreach (var entry in nextEntries) entry.ui = ui.Contains(entry.path);

            var dependencies = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var usage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(nextEntries.Select(e => e.path), StringComparer.OrdinalIgnoreCase);
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nextEntries.Count; i++)
            {
                var entry = nextEntries[i];
                ReportScan(.2f + .15f * i / Math.Max(1, nextEntries.Count), "收集自动依赖", entry.path);
                string[] closure = AssetDatabase.GetDependencies(new[] { entry.path }, true).Concat(new[] { entry.path })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                dependencies[entry.path] = closure;
                foreach (string path in closure)
                {
                    sources.Add(path);
                    if (!IsInventoryAsset(path)) continue;
                    paths.Add(path);
                    if (!string.IsNullOrWhiteSpace(entry.bundle))
                    {
                        if (!usage.TryGetValue(path, out var bundles)) usage[path] = bundles = new HashSet<string>(StringComparer.Ordinal);
                        bundles.Add(entry.bundle);
                    }
                }
                yield return null;
            }
            var tags = AssetDatabase.GetAllAssetBundleNames();
            foreach (string bundle in tags)
            {
                foreach (string path in AssetDatabase.GetAssetPathsFromAssetBundle(bundle))
                    if (IsInventoryAsset(path)) paths.Add(path);
                yield return null;
            }
            var nextBundleNames = tags.Concat(scanProfile.mappings.Select(m => m.bundle)).Concat(nextEntries.Select(e => e.bundle))
                .Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().OrderBy(b => b, StringComparer.Ordinal).ToArray();
            var assets = new List<ResourceInventory.Asset>();
            int count = 0;
            foreach (string path in paths)
            {
                if (!IsInventoryAsset(path)) continue;
                ReportScan(.35f + .1f * count++ / Math.Max(1, paths.Count), "清点资源", path);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                var importer = AssetImporter.GetAtPath(path);
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                assets.Add(new ResourceInventory.Asset { path = path, type = type == typeof(GameObject) ? "Prefab" : type?.Name,
                    taggedBundle = importer == null || string.IsNullOrEmpty(importer.assetBundleVariant)
                        ? importer?.assetBundleName : importer.assetBundleName + "." + importer.assetBundleVariant, bytes = bytes });
                yield return null;
            }
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dirty = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            count = 0;
            foreach (string path in sources)
            {
                ReportScan(.45f + .15f * count++ / Math.Max(1, sources.Count), "检查依赖指纹", path);
                hashes[path] = AssetDatabase.GetAssetDependencyHash(path).ToString();
                dirty[path] = ExportChangeTracker.HasUnsavedChanges(path);
                yield return null;
            }
            var files = ExportChangeTracker.FingerprintFiles(scanProfile).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                ReportScan(.6f + .06f * i / Math.Max(1, files.Length), "检查工具链", file);
                foreach (var part in ExportChangeTracker.DigestSteps(file, hash => fileHashes[file] = hash))
                    yield return null;
            }
            var snapshots = ExportChangeTracker.Capture(scanProfile, nextEntries, ui, dependencies,
                path => hashes[path], path => dirty[path], path => fileHashes[path]);
            var baseline = ExportChangeTracker.Load();
            var output = new ExportChangeTracker.OutputReader(scanProfile);
            var signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshots.Length; i++)
            {
                var snapshot = snapshots[i];
                ReportScan(.66f + .32f * i / Math.Max(1, snapshots.Length), "校验发布内容", snapshot.path);
                if (!baseline.entries.TryGetValue(snapshot.key, out var old) || snapshot.uncertain || old.context != snapshot.context)
                { yield return null; continue; }
                //Catch each output error without swallowing cancellation/disposal of nested file iterators.
                using (var steps = output.ReadSteps(snapshot, hash => signatures[snapshot.key] = hash).GetEnumerator())
                {
                    while (true)
                    {
                        bool moved;
                        try { moved = steps.MoveNext(); }
                        catch (Exception) { signatures[snapshot.key] = null; break; }
                        if (!moved) break;
                        scanDetail = steps.Current;
                        yield return null;
                    }
                }
            }
            var nextTracker = new ExportChangeTracker(baseline, snapshots,
                item => signatures.TryGetValue(item.key, out string hash) ? hash : null);
            var nextInventory = ResourceInventory.Build(assets, nextEntries, usage, scanProfile, ui);
            foreach (var row in nextInventory)
            {
                row.changeReason = nextTracker.Reasons.TryGetValue(row.path, out string reason) ? reason
                    : sources.Contains(row.path) ? null : "未发布或尚未注册入口";
                yield return null;
            }
            if (revision != scanRevision) yield break; //A request during scan never installs stale results.
            entries = nextEntries; profile = scanProfile; inventory = nextInventory; changeTracker = nextTracker;
            uiResourcePaths.Clear(); uiResourcePaths.UnionWith(ui);
            bundleNames = nextBundleNames;
            inventoryTypes = new[] { "全部类型" }.Concat(inventory.Select(r => r.type).Distinct().OrderBy(t => t)).ToArray();
            inventoryType = Math.Min(inventoryType, inventoryTypes.Length - 1);
            resourceSelection.IntersectWith(inventory.Select(r => r.path));
            inventoryAssets.Clear();
            inventoryDirty = true;
            initialized = scanValid = true;
            error = ""; scanNotice = "清点完成";
            ReportScan(1, "清点完成");
        }

        private void DrawScanProgress()
        {
            Rect bar = new Rect(474, 43, Mathf.Max(1, position.width - 566), 30);
            if (ScanBusy)
            {
                EditorGUI.ProgressBar(bar, scanProgress, "扫描 " + Mathf.FloorToInt(scanProgress * 100) + "% · " + scanStage);
                GUI.Label(bar, new GUIContent("", scanDetail));
                if (GUI.Button(new Rect(position.width - 82, 43, 70, 30), "取消", inventoryButton))
                { StopScanning(); scanNotice = "扫描已取消，请刷新后导出"; Repaint(); }
            }
            else if (!string.IsNullOrEmpty(scanNotice))
                GUI.Label(new Rect(474, 43, position.width - 486, 30), scanNotice, faintText);
        }
    }
}
