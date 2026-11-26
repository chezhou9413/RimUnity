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

//主菜单前编译着色器、首次进图前预热管线；资源身份仍由原有加载器维护。
internal static class LoadingWarmup
{
    private static readonly List<AssetCatalog> catalogs = new();
    private static readonly Dictionary<int, NativeShader> shaders = new();
    private static bool compiled, mapRequested;
    private static LoadingWarmupFeature feature;
    internal static LoadingWarmupFeature Current => feature;

    //只在具有图形设备的宿主上安装一次预热入口。
    internal static void Install(EngineApplication app)
    {
        if (feature != null || app.Headless) return;
        try { feature = new LoadingWarmupFeature(app); app.FrameRenderer.AddFeature(feature); }
        catch (Exception error) { feature = null; Log.Error("[RimUnity] 管线预热入口不可用，保留按需创建：\n" + error); }
    }

    //主菜单出现前完成资源清单中的着色器编译。
    internal static void CompileBeforeMenu()
    {
        if (compiled) return;
        compiled = true;
        var watch = Stopwatch.StartNew(); int failures = 0;
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            if (!CatalogLoadPolicy.HasLoadFolders(mod)) continue;
            string root = Path.Combine(mod.RootDir, "1.7/Data/RimUnity");
            if (!CatalogStoreRegistry.HasCatalog(root)) continue;
            try
            {
                var catalog = AssetLoader.GetCatalog(root);
                if (!catalogs.Contains(catalog)) catalogs.Add(catalog);
                foreach (var record in catalog.Manifest.assets.Where(a => a.type == "Shader"))
                {
                    LongEventHandler.SetCurrentEventText(LoadingWarmupUI.Text("RimUnity：预编译着色器", "RimUnity: compiling shaders") + " · " + record.name);
                    try { Add(((RimUnity.Shader)catalog.Get(record.id)).EngineShader); }
                    catch (Exception error) { failures++; Log.Error("[RimUnity] 启动预编译失败：" + mod.PackageIdPlayerFacing + " / " + record.path + "\n" + error); }
                }
                //材质描述可能生成线性或反向减法变体；此处不加载贴图、预制体或运行组件。
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
        //宿主工作线程只编译冻结的着色器快照，全部结束后才返回。
        if (plan.Count != 0) Waylith.Rendering.ShaderPrewarmer.Warm(plan);
        Log.Message($"[RimUnity] 主菜单前 Shader 预编译完成：{catalogs.Count} 个资源清单，{shaders.Count} 个程序，{failures} 个资源加载失败，{watch.Elapsed.TotalMilliseconds:0} ms。宿主缓存继续有效。");
    }

    //按原生着色器身份去重。
    private static void Add(NativeShader shader)
    {
        shaders[shader.Id] = shader;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    private static class BeforeFirstMap
    {
        //地图初始化完成后，等主线程加载回调再捕获预热输入。
        private static void Postfix(Game __instance)
        {
            if (__instance.Maps.Count != 0) LongEventHandler.ExecuteWhenFinished(QueueMapWarmup);
        }
    }

    //追加加载事件，等待分帧预热结束后再进入正常地图更新。
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
        //使用粒子和条带实际生成的顶点布局。
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
        //后台加载线程只等待；主线程继续绘制提示，渲染线程逐帧填充管线缓存。
        LongEventHandler.QueueLongEvent(() =>
        {
            request.Wait();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (request.Error != null) Log.Error("[RimUnity] 地图管线预热失败，保留按需创建：\n" + request.Error);
                    else if (request.Cancelled) Log.Warning("[RimUnity] 地图管线预热未完成（窗口长时间未提交足够的渲染帧），保留按需创建。");
                    else Log.Message($"[RimUnity] 首次进图前管线预热完成：{owner.CompletedCount} 个请求，{watch.Elapsed.TotalMilliseconds:0} ms。");
                }
                finally { owner.Release(request); request.Dispose(); }
            });
        }, "LoadingAssets", true, error => Log.Error("[RimUnity] 管线加载事件失败：" + error));
        void AddLayout(VertexLayout layout) => layouts.TryAdd(layout.Id, layout);
    }

    //构造预热所需的常用顶点布局。
    private static VertexLayout CommonLayout(bool normal, bool tangent, int uvCount)
    {
        var attributes = new List<VertexAttribute> { new(VertexAttributeSemantic.Position, VertexAttributeFormat.Float32, 3) };
        if (normal) attributes.Add(new(VertexAttributeSemantic.Normal, VertexAttributeFormat.Float32, 3));
        if (tangent) attributes.Add(new(VertexAttributeSemantic.Tangent, VertexAttributeFormat.Float32, 4));
        attributes.Add(new(VertexAttributeSemantic.Color, VertexAttributeFormat.UNorm8, 4));
        for (int i = 0; i < uvCount; i++) attributes.Add(new(VertexAttributeSemantic.TexCoord0 + i, VertexAttributeFormat.Float32, 4));
        return new VertexLayout(attributes);
    }

    //宿主已停止渲染后释放请求和额外资源引用。
    internal static void Shutdown(EngineApplication app)
    {
        feature?.Work?.CancelPending();
        if (feature?.Work is { } request) feature.Release(request);
        if (feature != null) app.FrameRenderer.RemoveFeature(feature);
        feature = null; catalogs.Clear(); shaders.Clear(); compiled = mapRequested = false;
    }
}
