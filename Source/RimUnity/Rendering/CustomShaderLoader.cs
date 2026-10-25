using System;
using System.Collections.Generic;
using System.IO;
using RimUnity.Assets;
using RimUnity.Serialization;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//明文 GLSL 由宿主编译器生成设备代码，游戏自带缓存负责避免重复编译。
internal static class CustomShaderLoader
{
    private static readonly HashSet<string> reportedApproximations = new(StringComparer.Ordinal);

    //缓存键标识导出变体，对外名称仍对应 Unity Shader，供辉光选择等业务使用。
    private static RimUnity.Shader Wrap(ShaderData data, Waylith.Rendering.Shader engine)
    {
        var shader = RimUnity.Shader.FromEngineShader(data.key, engine);
        shader.name = data.sourceName;
        if (!string.IsNullOrEmpty(data.geometry) && data.geometry != "triangle-edges")
            throw new NotSupportedException("未知网格展开方式：" + data.geometry);
        shader.triangleEdges = data.geometry == "triangle-edges";
        ShaderDepthComparisons.Register(engine.Program, data.depthCompare);
        return shader;
    }

    //组合使用独立的资源身份，避免覆盖单 Pass 材质共享的 Shader 对象。
    private static RimUnity.Shader RestorePasses(AssetCatalog catalog, ShaderData data, RimUnity.Shader shader)
    {
        if (data.additionalPasses.Length == 0) return shader;
        var passes = new RimUnity.Shader[data.additionalPasses.Length + 1];
        passes[0] = shader;
        for (int i = 0; i < data.additionalPasses.Length; i++)
        {
            passes[i + 1] = (RimUnity.Shader)catalog.Get(data.additionalPasses[i]);
            if (passes[i + 1].passCount != 1) throw new InvalidDataException("附加 Shader Pass 不能嵌套组合：" + data.sourceName);
        }
        shader.SetPasses(passes);
        return shader;
    }

    //读取导出程序并注册材质布局，兼容说明按原始 Shader 去重。
    internal static RimUnity.Shader Load(AssetCatalog catalog, AssetRecord record)
    {
        ShaderData data = catalog.Read<ShaderData>(record.file);
        if (data.formatVersion != 1 || data.key != record.id || data.profile is not ("world" or "ui")) throw new InvalidDataException("Shader 描述不合法：" + record.path);
        ShaderMaterialDefaults.Register(data.key, data.defaults);
        //资源键包含 packageId 的点号；宿主的路径回退会把它当扩展名截断，只接受完整键命中。
        if (ShaderRegistry.TryResolve(data.key, out var cached) && string.Equals(cached.Key, data.key, StringComparison.Ordinal))
            return RestorePasses(catalog, data, Wrap(data, cached));
        string vertexPath = catalog.Resolve(data.vertexFile), fragmentPath = catalog.Resolve(data.fragmentFile);
        string vertex = File.ReadAllText(vertexPath), fragment = File.ReadAllText(fragmentPath);
        ShaderBindingValidation.Validate(vertex, fragment);
        var layout = CustomShaderTextureLayout.BindTexelSizes(ShaderStorageLayout.Extract(fragment, data.key));
        var inputs = ShaderMetadataParser.ExtractVertexInputLayout(vertex, data.key);
        if (layout.HasErrors || inputs.HasErrors || ShaderMetadataParser.ValidateStageInterface(vertex, fragment, vertexPath, fragmentPath))
            throw new InvalidDataException("Shader 布局或阶段接口不合法：" + data.sourceName);
        var program = new ShaderProgramDefinition
        {
            vertexShaderFileName = vertexPath, fragmentShaderFileName = fragmentPath,
            useTexture = layout.TextureSlots.Length > 0, flipTextureST = false,
            transparent = data.blend, depthTest = data.depthTest, depthWrite = data.depthWrite, colorWrite = data.colorWrite,
            cullMode = Enum.Parse<FaceCull>(data.cull), renderQueue = new RenderQueueValue(data.renderQueue),
            stencilEnabled = data.stencil, stencilRef = data.stencilRef, stencilReadMask = data.stencilReadMask, stencilWriteMask = data.stencilWriteMask,
            stencilCompare = Enum.Parse<StencilCompare>(data.stencilCompare), stencilPassOp = Enum.Parse<StencilOp>(data.stencilPass),
            stencilFailOp = Enum.Parse<StencilOp>(data.stencilFail), stencilDepthFailOp = Enum.Parse<StencilOp>(data.stencilZFail),
            depthBiasConstantFactor = data.depthBiasConstant, depthBiasSlopeFactor = data.depthBiasSlope
        };
        if (!string.IsNullOrEmpty(data.grabTexture))
            //宿主以名称作为全局键，不同队列必须各自保留抓取时机。
            program.sceneGrab = new SceneGrabDeclaration { name = $"RimUnity:{data.renderQueue}:{data.grabTexture}", queue = program.renderQueue };
        var blend = new BlendState(data.blend, Enum.Parse<BlendFactor>(data.srcColor), Enum.Parse<BlendFactor>(data.dstColor), Enum.Parse<BlendOp>(data.colorOp),
            Enum.Parse<BlendFactor>(data.srcAlpha), Enum.Parse<BlendFactor>(data.dstAlpha), Enum.Parse<BlendOp>(data.alphaOp));
        var pipeline = new PipelineState(blend, new DepthState(data.depthTest, data.depthWrite), program.cullMode, program.ResolveStencil(), data.colorWrite,
            new DepthBiasState(data.depthBiasConstant, data.depthBiasSlope));
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(vertex, vertexPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(fragment, fragmentPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        //清单资源按完整键注册，不再额外登记会被 NormalizePath 截断的文件路径别名。
        var shader = new Waylith.Rendering.Shader(data.key, string.Empty, program, data.keywords, layout, pipeline, vertexInputLayout: inputs, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidDataException("Shader 注册前校验失败：" + data.sourceName);
        ShaderRegistry.Register(shader);
        if (data.profile == "world") ParticleBatchShaders.Register(shader, vertex, fragment);
        if (data.profile == "world") LinearShaderVariants.Register(shader, fragment);
        foreach (string approximation in data.approximations)
            if (!LinearShaderVariants.TryGet(shader, out _) || !approximation.StartsWith("程序按 Linear 计算后编码到宿主 UNorm 场景", StringComparison.Ordinal))
            {
                //仍存在的渲染差异继续警告；已解决的差异应从导出资源中移除。
                string message = approximation switch
                {
                    "软粒子深度使用世界 Y=0 地表；不包含建筑、模型或其他粒子的遮挡。" =>
                        "粒子只会在靠近地面（Y=0）时淡出，建筑、模型和其他粒子不会产生这种遮挡效果。",
                    "程序按 Linear 计算后编码到宿主 UNorm 场景；透明叠加沿用宿主的 Gamma 混合，HDR 超出场景格式范围的部分仍会截断。" =>
                        "Shader 按线性颜色计算，但游戏用 Gamma 方式叠加透明颜色，亮度超过场景格式上限时也会被截断，效果可能和 Unity 里不同。",
                    "GrabPass 按渲染队列共享一次场景快照，不逐对象重复抓取。" =>
                        "同一渲染队列里的折射效果共用一张背景截图，不会为每个对象重新截图。",
                    "BuiltIn Lit 使用地图太阳、月光与天空环境色；不使用 Unity 烘焙光照、局部灯光和反射探针。" =>
                        "光照来自地图的太阳、月光和天空颜色；Unity 里烘焙的光照、局部灯光和反射探针不会生效。",
                    "Unity 内置 _DitherMaskLOD 使用程序化 4×4 Bayer 抖动近似，保留 16 级透明阴影覆盖率；像素排列可能与 Unity 不同。" =>
                        "透明阴影改用 4×4 Bayer 点阵，仍有 16 级透明度，但点阵排列可能和 Unity 不同。",
                    "常量正整数幂 2～64 使用乘法展开；负底数按整数幂计算，属于旧 Shader 写法的兼容处理，并非 HLSL pow 的负底数语义保证。" =>
                        "2～64 次的固定整数幂改用乘法计算，负数也按整数幂处理；这是对旧 Shader 的兼容，不能据此认为 HLSL 的 pow 支持负底数。",
                    "smoothstep 按 HLSL 饱和 Hermite 公式展开，支持反向边界；相等边界仍要求作者避免除零。" =>
                        "smoothstep 改用 HLSL 的插值公式计算，支持反向边界；两个边界值不能相等，否则仍会除以零。",
                    "Overlay UI 不使用场景深度，ZTest LEqual 按 Always 绘制。" =>
                        "屏幕 UI 不检查场景深度，ZTest LEqual 会按 Always 绘制。",
                    "ForwardBase 主光源使用 RimUnity 的地图主方向光或离屏预制体方向光；不提供 Unity 烘焙光照和附加灯光 Pass。" =>
                        "ForwardBase 使用地图主光源，离屏预制体则使用自己的方向光；Unity 烘焙光照和额外的灯光 Pass 不会生效。",
                    "ForwardBase 接收 RimUnity 太阳或离屏方向光阴影，沿用深度偏移、强度和九点 PCF；月光与现有 Surface 路径一样不接收阴影，不使用 Unity 屏幕空间阴影纹理。" =>
                        "ForwardBase 使用 RimUnity 的太阳或离屏方向光阴影，保留深度偏移、阴影强度和九点 PCF 滤波；和 Surface 一样，月光部分不接收阴影，也不读取 Unity 的屏幕空间阴影纹理。",
                    _ => null
                };
                if (!reportedApproximations.Add(data.sourceName + "\n" + approximation)) continue;
                Verse.Log.Warning("[RimUnity] " + data.sourceName + "：" + (message ?? approximation));
            }
        return RestorePasses(catalog, data, Wrap(data, shader));
    }
}
