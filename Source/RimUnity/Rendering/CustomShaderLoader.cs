using System;
using System.IO;
using RimUnity.Assets;
using RimUnity.Serialization;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//明文 GLSL 由宿主编译器生成设备代码，游戏自带缓存负责避免重复编译。
internal static class CustomShaderLoader
{
    internal static RimUnity.Shader Load(AssetCatalog catalog, AssetRecord record)
    {
        ShaderData data = catalog.Read<ShaderData>(record.file);
        if (data.formatVersion != 1 || data.key != record.id || data.profile is not ("world" or "ui")) throw new InvalidDataException("Shader 描述不合法：" + record.path);
        ShaderMaterialDefaults.Register(data.key, data.defaults);
        if (ShaderRegistry.TryResolve(data.key, out var cached)) return RimUnity.Shader.FromEngineShader(data.key, cached);
        string vertexPath = catalog.Resolve(data.vertexFile), fragmentPath = catalog.Resolve(data.fragmentFile);
        string vertex = File.ReadAllText(vertexPath), fragment = File.ReadAllText(fragmentPath);
        var layout = ShaderMetadataParser.ExtractMaterialLayout(fragment, data.key, 2);
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
            program.sceneGrab = new SceneGrabDeclaration { name = data.grabTexture, queue = program.renderQueue };
        var blend = new BlendState(data.blend, Enum.Parse<BlendFactor>(data.srcColor), Enum.Parse<BlendFactor>(data.dstColor), Enum.Parse<BlendOp>(data.colorOp),
            Enum.Parse<BlendFactor>(data.srcAlpha), Enum.Parse<BlendFactor>(data.dstAlpha), Enum.Parse<BlendOp>(data.alphaOp));
        var pipeline = new PipelineState(blend, new DepthState(data.depthTest, data.depthWrite), program.cullMode, program.ResolveStencil(), data.colorWrite,
            new DepthBiasState(data.depthBiasConstant, data.depthBiasSlope));
        var stages = new PrecompiledShaderStages(
            RuntimeShaderCompiler.Compile(vertex, vertexPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Vertex),
            RuntimeShaderCompiler.Compile(fragment, fragmentPath, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment), false);
        var shader = new Waylith.Rendering.Shader(data.key, data.key, program, data.keywords, layout, pipeline, vertexInputLayout: inputs, precompiledStages: stages);
        if (shader.HasErrors) throw new InvalidDataException("Shader 注册前校验失败：" + data.sourceName);
        ShaderRegistry.Register(shader);
        if (data.profile == "world") LinearShaderVariants.Register(shader, fragment);
        foreach (string approximation in data.approximations)
            if (!LinearShaderVariants.TryGet(shader, out _) || !approximation.StartsWith("程序按 Linear 计算后编码到宿主 UNorm 场景", StringComparison.Ordinal))
                Verse.Log.Warning("[RimUnity] " + data.sourceName + "：" + approximation);
        return RimUnity.Shader.FromEngineShader(data.key, shader);
    }
}
