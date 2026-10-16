using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimUnity.Assets;
using RimUnity.Serialization;
using Verse;
using Waylith;
using Waylith.Graphics.Meshes;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//Process/device lifetime only: once before the menu, once before the first map.
//Catalog/resource identity stays with AssetLoader; shutdown releases all extra references.
internal static class LoadingWarmup
{
    private static readonly List<AssetCatalog> catalogs = new();
    private static readonly Dictionary<int, NativeShader> shaders = new();
    private static bool compiled, mapRequested;
    private static LoadingWarmupFeature feature;

    internal static void Install(EngineApplication app)
    {
        if (feature != null || app.Headless) return;
        try { feature = new LoadingWarmupFeature(app); app.FrameRenderer.AddFeature(feature); }
        catch (Exception error) { feature = null; Log.Error("[RimUnity] 管线预热入口不可用，保留按需创建：\n" + error); }
    }

    internal static void CompileBeforeMenu()
    {
        if (compiled) return;
        compiled = true;
        var watch = Stopwatch.StartNew(); int failures = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (!CatalogLoadPolicy.HasLoadFolders(mod)) continue;
            string root = Path.Combine(mod.RootDir, "1.7/Data/RimUnity");
            if (!File.Exists(Path.Combine(root, "catalog.json"))) continue;
            try
            {
                var catalog = AssetLoader.GetCatalog(root);
                if (!catalogs.Contains(catalog)) catalogs.Add(catalog);
                foreach (var record in catalog.Manifest.assets.Where(a => a.type == "Shader"))
                {
                    LongEventHandler.SetCurrentEventText("RimUnity：预编译着色器 · " + record.name);
                    try { Add(((RimUnity.Shader)catalog.Get(record.id)).EngineShader); }
                    catch (Exception error) { failures++; Log.Error("[RimUnity] 启动预编译失败：" + mod.PackageIdPlayerFacing + " / " + record.path + "\n" + error); }
                }
                //Mapped/standard materials may produce Linear or reverse-subtract variants.
                //Inspect descriptors only: never load textures, prefabs or run components here.
                foreach (var record in catalog.Manifest.assets.Where(a => a.type == "Material"))
                {
                    try
                    {
                        var data = catalog.Read<ResourceData>(record.file);
                        var value = data.members.FirstOrDefault(m => m.name == "shader")?.value;
                        if (value?.kind == "string" && Waylith.Rendering.ShaderRegistry.TryResolve(value.text, out var native))
                            Add(StandardParticleShader.Resolve(native, data.colorSpace == "Linear").EngineShader);
                    }
                    catch (Exception error) { failures++; Log.Error("[RimUnity] 材质着色器预编译失败：" + record.path + "\n" + error); }
                }
            }
            catch (Exception error) { failures++; Log.Error("[RimUnity] 无法预编译内容模组：" + mod.PackageIdPlayerFacing + "\n" + error); }
        }
        var plan = new Waylith.Rendering.ShaderWarmupPlan();
        string keywords = UnityEngine.Shader.GlobalKeywordKey;
        foreach (var shader in shaders.Values.Where(s => s.PrecompiledStages == null && !s.HasErrors))
        {
            var variant = new Waylith.Rendering.ShaderVariantKey(shader, Waylith.Rendering.ShaderKeywordIdMaker.GetOrCreateId(keywords), keywords, shader.UseTexture);
            plan.Graphics.Add(new(shader.VertexShaderFileName, Waylith.Rendering.RuntimeShaderStage.Vertex, variant));
            plan.Graphics.Add(new(shader.FragmentShaderFileName, Waylith.Rendering.RuntimeShaderStage.Fragment, variant));
        }
        //Host workers compile only immutable shader snapshots and finish before this call returns.
        if (plan.Count != 0) Waylith.Rendering.ShaderPrewarmer.Warm(plan);
        Log.Message($"[RimUnity] 主菜单前 Shader 预编译完成：{catalogs.Count} 个资源清单，{shaders.Count} 个程序，{failures} 个资源加载失败，{watch.Elapsed.TotalMilliseconds:0} ms。宿主缓存继续有效。");
    }

    private static void Add(NativeShader shader)
    {
        shaders[shader.Id] = shader;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    private static class BeforeFirstMap
    {
        private static void Postfix(Game __instance)
        {
            if (__instance.Maps.Count != 0) LongEventHandler.ExecuteWhenFinished(QueueMapWarmup);
        }
    }

    private static void QueueMapWarmup()
    {
        if (feature == null) return;
        using var effects = ScreenEfx.EfxPlayer.CaptureWarmup();
        if (mapRequested && effects == null) return;
        bool firstMap = !mapRequested;
        mapRequested = true;
        CompileBeforeMenu();
        if (shaders.Count == 0 && effects == null) return;
        var watch = Stopwatch.StartNew();
        var layouts = new Dictionary<int, VertexLayout>();
        //These are the actual layouts emitted by our particle/ribbon geometry builders.
        AddLayout(CommonLayout(true, true, 3));
        AddLayout(CommonLayout(false, false, 1));
        AddLayout(CommonLayout(true, true, 1));
        foreach (var catalog in firstMap ? catalogs : Enumerable.Empty<AssetCatalog>())
            foreach (var record in catalog.Manifest.assets.Where(a => a.type == "Mesh"))
                try { AddLayout(((Mesh)catalog.Get(record.id)).EngineMesh.CpuStorage.Layout); }
                catch (Exception error) { Log.Error("[RimUnity] 网格布局预热失败：" + record.path + "\n" + error); }
        var owner = feature;
        owner.Request(firstMap ? shaders.Values.ToArray() : Array.Empty<NativeShader>(), layouts.Values.ToArray(),
            RimUnityMod.Bloom.Snapshot().Enabled, UnityEngine.Shader.GlobalKeywordKey, effects);
        var request = owner.Work;
        //The loading worker waits; the main thread remains free to submit loading frames.
        //Only the render thread touches the real renderer's pipeline/module caches.
        LongEventHandler.QueueLongEvent(() =>
        {
            request.Wait();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (request.Cancelled) Log.Warning("[RimUnity] 地图管线预热未执行（窗口未提交渲染帧），保留按需创建。");
                    else if (request.Error != null) Log.Error("[RimUnity] 地图管线预热失败，保留按需创建：\n" + request.Error);
                    else Log.Message($"[RimUnity] 首次进图前管线预热完成：{owner.CompletedCount} 个请求，{watch.Elapsed.TotalMilliseconds:0} ms。");
                }
                finally { owner.Release(request); request.Dispose(); }
            });
        }, "LoadingAssets", true, error => Log.Error("[RimUnity] 管线加载事件失败：" + error));
        void AddLayout(VertexLayout layout) => layouts.TryAdd(layout.Id, layout);
    }

    private static VertexLayout CommonLayout(bool normal, bool tangent, int uvCount)
    {
        var attributes = new List<VertexAttribute> { new(VertexAttributeSemantic.Position, VertexAttributeFormat.Float32, 3) };
        if (normal) attributes.Add(new(VertexAttributeSemantic.Normal, VertexAttributeFormat.Float32, 3));
        if (tangent) attributes.Add(new(VertexAttributeSemantic.Tangent, VertexAttributeFormat.Float32, 4));
        attributes.Add(new(VertexAttributeSemantic.Color, VertexAttributeFormat.UNorm8, 4));
        for (int i = 0; i < uvCount; i++) attributes.Add(new(VertexAttributeSemantic.TexCoord0 + i, VertexAttributeFormat.Float32, 4));
        return new VertexLayout(attributes);
    }

    internal static void Shutdown(EngineApplication app)
    {
        feature?.Work?.CancelPending();
        if (feature?.Work is { } request) feature.Release(request);
        if (feature != null) app.FrameRenderer.RemoveFeature(feature);
        feature = null; catalogs.Clear(); shaders.Clear(); compiled = mapRequested = false;
    }
}
