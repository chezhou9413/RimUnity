using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Waylith.Rendering;

namespace RimUnity.Diagnostics;

internal static partial class RenderDebugReport
{
    private static void ResourcesSection(StringBuilder b)
    {
        b.AppendLine("Host frame value metadata mapping=" + Rendering.HdrDrawFrame.MetadataLayout);
        Dump(b, "Bloom player settings", RimUnityMod.Bloom);
        Value(b, "Bloom effective", () => typeof(Rendering.BloomSettings).GetMethod("Snapshot", Flags)?.Invoke(RimUnityMod.Bloom, null));
        b.AppendLine("Last particle diagnostic composite state=" + RendererDebugCapture.CompositeState);
        b.AppendLine("Bridge active cameras=" + Camera.Active.Count + "; main=" + Camera.main?.name);
        foreach (var camera in Camera.Active.ToArray())
        {
            b.AppendLine("CAMERA " + Hierarchy(camera.transform) + " enabled=" + camera.enabled + " active=" + camera.isActiveAndEnabled + " main=" + (camera == Camera.main)
                + " depth=" + camera.depth + " mask=0x" + camera.cullingMask.ToString("X8") + " clear=" + camera.clearFlags + " background=" + camera.backgroundColor
                + " rect=" + camera.rect + " pixels=" + camera.pixelRect + " ortho=" + camera.orthographic + " near/far=" + camera.nearClipPlane + "/" + camera.farClipPlane);
            b.AppendLine(" view=" + camera.worldToCameraMatrix + " projection=" + camera.projectionMatrix);
            DescribeTexture(b, " target", camera.targetTexture?.NativeTexture);
            DescribeTexture(b, " screen target", camera.ScreenTarget?.NativeTexture);
        }
        b.AppendLine("Active bridge renderers: mesh=" + MeshRenderer.Active.Count + "; particles=" + ParticleSystem.Active.Count + "; line=" + LineRenderer.Active.Count + "; trail=" + TrailRenderer.Active.Count + "; skinned=" + SkinnedMeshRenderer.Active.Count);
        Dump(b, "NativeRuntime", typeof(Runtime.NativeRuntime));
        foreach (string key in new[] { "Objects", "Behaviours", "Pending", "PendingSet" }) Value(b, "NativeRuntime." + key, () => Read(typeof(Runtime.NativeRuntime), key));
        Dump(b, "HostCamera", typeof(HostCamera));
        Dump(b, "HostFrame", typeof(HostFrame));
        foreach (string key in new[] { "Canvases", "Entries" }) Value(b, "UGUI." + key, () => Read(typeof(UI.Runtime.UguiRuntime), key));
        foreach (string key in new[] { "white", "black", "gray", "normal" })
            DescribeTexture(b, "Bridge builtin " + key, (Read(typeof(Texture2D), key) as Texture2D)?.CoreTexture);
        var catalogs = Read(typeof(Assets.AssetLoader), "Catalogs") as IDictionary;
        if (catalogs == null) b.AppendLine("Catalog cache unavailable");
        else foreach (DictionaryEntry row in catalogs)
        {
            object catalog = row.Value;
            string root = Read(catalog, "Root") as string;
            b.AppendLine("CATALOG " + root); Fingerprint(b, Path.Combine(root, "catalog.json"));
            var manifest = Read(catalog, "Manifest"); Dump(b, "Manifest metadata", manifest);
            var resources = Read(catalog, "resources") as IDictionary;
            var records = Read(catalog, "records") as IDictionary;
            b.AppendLine("Cached resources=" + resources?.Count + "; records=" + records?.Count);
            var types = new Dictionary<string, int>();
            if (records != null) foreach (DictionaryEntry record in records) { string type = Read(record.Value, "type")?.ToString() ?? "?"; types[type] = types.GetValueOrDefault(type) + 1; }
            int missingDescriptions = 0, checkedDescriptions = 0;
            if (records != null) foreach (DictionaryEntry record in records)
            {
                string relative = Read(record.Value, "file") as string;
                if (string.IsNullOrEmpty(relative) || relative.Contains("://")) continue;
                string resourceRoot = root;
                var shared = Read(manifest, "sharedFiles") as IDictionary;
                if (shared != null && shared.Contains(relative))
                {
                    string owner = shared[relative]?.ToString();
                    var source = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => string.Equals(m.PackageIdPlayerFacing, owner, StringComparison.OrdinalIgnoreCase));
                    if (source == null) { b.AppendLine("MISSING shared owner=" + owner + " for " + relative); missingDescriptions++; continue; }
                    resourceRoot = Path.Combine(source.RootDir, "1.7", "Data", "RimUnity");
                }
                string full = Path.GetFullPath(Path.Combine(resourceRoot, relative));
                if (!full.StartsWith(Path.GetFullPath(resourceRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { b.AppendLine("Invalid descriptor path=" + relative); continue; }
                checkedDescriptions++;
                if (!File.Exists(full)) { missingDescriptions++; if (missingDescriptions <= 100) b.AppendLine("MISSING resource descriptor=" + relative + " (shared paths must be resolved by owner)"); }
            }
            b.AppendLine("Local descriptor existence checked=" + checkedDescriptions + "; absent=" + missingDescriptions + "; does not verify every transitive blob or GPU upload.");
            b.AppendLine("Declared resource types=" + string.Join(", ", types.Select(x => x.Key + ":" + x.Value)));
            if (resources == null) continue;
            int count = 0;
            foreach (DictionaryEntry resource in resources)
            {
                if (++count > 3000) { b.AppendLine("[cached resource list truncated at 3000]"); break; }
                try
                {
                b.AppendLine("RESOURCE " + resource.Key + " " + resource.Value?.GetType().FullName + " name=" + (resource.Value as RimUnity.Object)?.name);
                if (resource.Value is Texture texture) DescribeTexture(b, " texture", texture.NativeTexture);
                else if (resource.Value is Material material)
                {
                    b.AppendLine(" material shader=" + material.shader?.name + " supported=" + material.shader?.isSupported + " queue=" + material.renderQueue);
                    var native = material.engineMaterial;
                    DescribeShader(b, native?.Shader);
                }
                else if (resource.Value is Shader shader) DescribeShader(b, shader.EngineShader);
                }
                catch (Exception e) { b.AppendLine("RESOURCE failed " + resource.Key + ": " + Error(e)); }
            }
        }
        //The main menu override is a host texture; inspect only a field, never load ContentFinder assets.
        var menuManager = AccessTools.TypeByName("Verse.UIMenuBackgroundManager") ?? AccessTools.TypeByName("RimWorld.UIMenuBackgroundManager");
        var background = Read(menuManager, "background");
        Value(b, "Host menu background manager", () => background?.GetType().FullName);
        if (Read(background, "overrideBGImage") is UnityEngine.Texture2D bg)
        {
            b.AppendLine("Host menu background " + bg.name + " " + bg.width + "x" + bg.height + " format=" + bg.graphicsFormat);
            DescribeTexture(b, " menu core", Read(bg, "CoreTexture") as Waylith.Rendering.Texture);
        }
    }
    private static void DescribeShader(StringBuilder b, Waylith.Rendering.Shader shader)
    {
        if (shader == null) { b.AppendLine("shader=<null>"); return; }
        Dump(b, "shader", shader);
        Dump(b, "shader program", shader.Program);
        Dump(b, "shader pipeline", shader.PipelineState);
        Dump(b, "shader material layout", shader.MaterialLayout);
        foreach (string key in new[] { "VertexPath", "FragmentPath", "VertexShaderPath", "FragmentShaderPath" })
            if (Read(shader.Program, key) is string path && File.Exists(path)) Fingerprint(b, path);
    }
    private static string Hierarchy(Transform t)
    {
        var names = new List<string>(); for (int i = 0; t != null && i < 64; i++, t = t.parent) names.Add(t.gameObject.name);
        names.Reverse(); return string.Join("/", names);
    }
    private static void DescribeTexture(StringBuilder b, string label, Waylith.Rendering.Texture texture)
    {
        if (texture == null) { b.AppendLine(label + "=<null>"); return; }
        //Do not call EnsureResident/GetNativeTexturePtr/GetPixels: observation must not fix or hide the failure.
        b.AppendLine(label + " name=" + texture.Name + " size=" + texture.width + "x" + texture.height + " mips=" + texture.MipCount
            + " readable=" + texture.IsReadable + " rehydrate=" + texture.CanRehydrate + " residencyGeneration=" + texture.ResidencyGeneration
            + " category=" + texture.MemoryCategory + " filter=" + texture.filterMode + " wrap=" + texture.wrapMode);
        if (texture is Waylith.Rendering.Texture2D t)
        {
            var cpu = t.CpuData;
            b.AppendLine(" cpu: format=" + cpu?.Format + " hasData=" + cpu?.HasData + " linear=" + cpu?.Linear + " swizzle=" + cpu?.Swizzle);
            Value(b, " source/cache", () => Read(t, "SourcePath"));
            Value(b, " pending build", () => Read(t, "PendingBuild"));
        }
        var gpu = texture.GpuResource;
        if (gpu == null) { b.AppendLine(" gpu: NOT RESIDENT (may be normal for an unused asset)"); return; }
        b.AppendLine(" gpu: format=" + gpu.Format + " layout=" + gpu.CurrentLayout + " disposed=" + gpu.IsDisposed + " swizzle=" + gpu.Swizzle + " view=" + gpu.ImageView);
        foreach (string key in new[] { "Allocation", "AllocationBytes", "SizeBytes", "LastUsedFrame", "LastUseOrder", "MemoryPlacement" }) Value(b, " gpu." + key, () => Read(gpu, key));
    }
}
