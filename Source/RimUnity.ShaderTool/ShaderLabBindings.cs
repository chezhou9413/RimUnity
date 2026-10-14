using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//将源程序的全局材质声明移入统一绑定，函数和表达式保持原样。
internal static class ShaderLabBindings
{
    //显式共享缓冲区布局，纹理绑定与 ShaderLab 属性名称一一对应。
    internal static string Adapt(ShaderLabSource shader)
    {
        string source = UnityHlslCompat.Prepare(FragmentFacing.Adapt(shader.Program, shader.Manifest.fragmentEntry), shader.Manifest.vertexEntry, shader.Manifest.profile);
        source = UnityColorFunctions.Add(source);
        source = ShaderPreprocessor.Run((shader.Manifest.colorSpace == "Gamma" ? "#define UNITY_COLORSPACE_GAMMA 1\n" : "")
            + string.Join("\n", shader.Manifest.keywords.Select(k => "#define " + k + " 1")) + "\n" + UnityHlslCompat.Macros + "\n" + source);
        source = ShaderLoopScopes.Adapt(source);
        source = GroundDepthAdapter.Adapt(source, shader.Manifest);
        //共享包含会声明仅阴影变体使用的纹理；实际变体没有任何引用时不产生绑定。
        source = Regex.Replace(source, @"\b(?:uniform\s+)?(?:sampler2D|sampler3D)\s+(\w+)\s*;", m =>
            Regex.Matches(source, @"\b" + Regex.Escape(m.Groups[1].Value) + @"\b").Count == 1 ? "" : m.Value);
        Dictionary<string, string> uniforms = new(StringComparer.Ordinal), textures = new(StringComparer.Ordinal), samplers = new(StringComparer.Ordinal);
        source = Regex.Replace(source, @"\bcbuffer\s+UnityPerMaterial\s*\{([^{}]*)\}\s*;?", "$1");
        //展开逗号声明并保留每项数组长度，使混合向量与数组也进入各自的材质绑定。
        int[] commaDepths = Depths(source);
        source = Regex.Replace(source, @"\b((?:uniform\s+)?(?:float(?:[1-4](?:x[1-4])?)?|int[1-4]?|uint[1-4]?|sampler2D|samplerCUBE|Texture2D(?:\s*<\s*float4\s*>)?|SamplerState))\s+(\w+(?:\s*\[[^\]]*\])?(?:\s*,\s*\w+(?:\s*\[[^\]]*\])?)+)\s*;", match =>
            commaDepths[match.Index] == 0
                ? string.Join("\n", match.Groups[2].Value.Split(',').Select(name => match.Groups[1].Value + " " + name.Trim() + ";"))
                : match.Value);
        var declarations = Regex.Matches(source, @"\b(?:(uniform)\s+)?(float(?:[1-4](?:x[1-4])?)?|int[1-4]?|uint[1-4]?|sampler2D|samplerCUBE|Texture2D(?:\s*<\s*float4\s*>)?|SamplerState)\s+(\w+)\s*(;|\[[^\]]*\])");
        int[] depths = Depths(source);
        foreach (Match declaration in declarations.Cast<Match>().Reverse())
        {
            if (depths[declaration.Index] != 0) continue;
            string type = declaration.Groups[2].Value, name = declaration.Groups[3].Value;
            string prefix = source.Substring(Math.Max(0, declaration.Index - 15), Math.Min(15, declaration.Index));
            if (Regex.IsMatch(prefix, @"\b(static|const)\s*$")) continue;
            if (declaration.Groups[4].Value != ";")
            {
                if (type is not ("float" or "float4")) throw new NotSupportedException("材质数组元素类型尚未支持：" + type + " " + name);
                if (!Regex.IsMatch(declaration.Groups[4].Value, @"^\[\s*[1-9][0-9]*\s*\]$")) throw new NotSupportedException("材质数组需要正整数长度：" + name);
                shader.Manifest.arrays.Add(name, type);
                int end = declaration.Index + declaration.Length;
                while (end < source.Length && char.IsWhiteSpace(source[end])) end++;
                if (end >= source.Length || source[end] != ';') throw new NotSupportedException("带初始化器的材质数组尚未支持：" + name);
                source = source.Remove(declaration.Index, end + 1 - declaration.Index);
                continue;
            }
            var destination = type == "sampler2D" || type == "samplerCUBE" || type.StartsWith("Texture2D", StringComparison.Ordinal) ? textures : type == "SamplerState" ? samplers : uniforms;
            if (!destination.TryAdd(name, type)) throw new InvalidDataException("重复全局 Shader 声明：" + name);
            source = source.Remove(declaration.Index, declaration.Length);
        }
        //反向移除声明后按名字排序，使属性布局不依赖字典或变体优化的遍历顺序。
        foreach (string builtIn in new[] { "_Time", "_SinTime", "_CosTime", "unity_DeltaTime", "_ScreenParams", "_ProjectionParams", "_ZBufferParams" })
            if (Regex.IsMatch(source, @"\b" + builtIn + @"\b")) uniforms.TryAdd(builtIn, "float4");
        foreach (var builtIn in new[] { ("_WorldSpaceCameraPos", "float3"), ("unity_OrthoParams", "float4"), ("_RimUnity_View", "float4x4"), ("_RimUnity_Projection", "float4x4") })
            if (Regex.IsMatch(source, @"\b" + builtIn.Item1 + @"\b")) uniforms.TryAdd(builtIn.Item1, builtIn.Item2);
        foreach (string texture in textures.Keys)
        {
            if (Regex.IsMatch(source, @"\b" + Regex.Escape(texture) + @"_ST\b")) uniforms.TryAdd(texture + "_ST", "float4");
            if (Regex.IsMatch(source, @"\b" + Regex.Escape(texture) + @"_TexelSize\b")) uniforms.TryAdd(texture + "_TexelSize", "float4");
        }
        StringBuilder header = new(UnityHlslCompat.Header(shader.Manifest.profile));
        foreach (string keyword in shader.Manifest.keywords) header.AppendLine("#define " + keyword + " 1");
        if (uniforms.Count > 0)
        {
            header.AppendLine("struct RU_MaterialData {");
            foreach (var entry in uniforms.OrderBy(x => x.Key, StringComparer.Ordinal)) header.AppendLine(entry.Value + " " + entry.Key + ";");
            header.AppendLine("};\n[[vk::binding(0, 2)]] ConstantBuffer<RU_MaterialData> RU_Material;");
            header.AppendLine("// @rimunity-material " + string.Join(",", uniforms.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            foreach (string name in uniforms.Keys)
                header.AppendLine("#define " + name + " (RU_Material." + name + (name.EndsWith("_ST", StringComparison.Ordinal)
                    && (textures.ContainsKey(name[..^3]) || shader.Manifest.properties.Any(p => p.name == name[..^3] && p.defaultTexture != null)) ? ".zwxy" : "") + ")");
        }
        int binding = 1;
        foreach (var entry in textures.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            int textureBinding = binding++;
            header.AppendLine("[[vk::binding(" + textureBinding + ", 2)]] " + (entry.Value == "sampler2D" ? "Texture2D" : entry.Value == "samplerCUBE" ? "TextureCube" : entry.Value) + " " + entry.Key + ";");
            header.AppendLine("// @rimunity-texture " + textureBinding + " " + entry.Key);
            if (entry.Value == "sampler2D" || entry.Value == "samplerCUBE")
            {
                string sampler = "RU_Sampler" + entry.Key;
                header.AppendLine("[[vk::binding(" + binding++ + ", 2)]] SamplerState " + sampler + ";");
                header.AppendLine("// @rimunity-combined " + textureBinding + " " + (binding - 1));
                //采样时翻转 V，UV 动画与顶点运算仍在 Unity 的原始方向上执行。
                foreach (string function in entry.Value == "samplerCUBE" ? new[] { "texCUBE", "texCUBElod", "texCUBEbias" } : new[] { "tex2D", "tex2Dlod", "tex2Dbias", "tex2Dproj", "tex2Dgrad" })
                    source = Regex.Replace(source, @"\b" + function + @"\s*\(\s*" + Regex.Escape(entry.Key) + @"\s*,", "RU_" + function + entry.Key + "(");
                header.AppendLine(entry.Value == "samplerCUBE" ? CubeFunctions(entry.Key, sampler) : LegacyFunctions(entry.Key, sampler));
            }
        }
        foreach (var array in shader.Manifest.arrays.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            header.AppendLine("[[vk::binding(" + binding + ", 2)]] StructuredBuffer<" + array.Value + "> " + array.Key + ";");
            header.AppendLine("// @rimunity-array " + binding + " " + array.Key);
            binding++;
        }
        foreach (var entry in samplers.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            string texture = textures.Keys.SingleOrDefault(t => "sampler" + t == entry.Key);
            if (texture == null) throw new NotSupportedException("SamplerState 需要采用 sampler_贴图名 的 Unity 命名：" + entry.Key);
            Match marker = Regex.Match(header.ToString(), @"// @rimunity-texture (\d+) " + Regex.Escape(texture) + @"\r?\n");
            header.AppendLine("// @rimunity-combined " + marker.Groups[1].Value + " " + binding);
            header.AppendLine("[[vk::binding(" + binding++ + ", 2)]] SamplerState " + entry.Key + ";");
        }
        if (Regex.IsMatch(source, @"\b(?:Texture\w*|Sampler\w*|sampler(?:1D|2D|3D|CUBE)|cbuffer|RW\w*)\b\s*(?:<[^>]+>\s*)?\w+"))
            throw new NotSupportedException("源程序含未适配的资源声明；当前支持 sampler2D、Texture2D、SamplerState 和 UnityPerMaterial。");
        //直接对象采样缺少统一 UV 适配，要求调用标准采样宏，避免悄悄漏翻转。
        if (Regex.IsMatch(source, @"\.\s*(Sample\w*|Load|Gather\w*)\s*\("))
            throw new NotSupportedException("Texture2D 直接采样请改用 SAMPLE_TEXTURE2D/LOD/BIAS 宏后转换。");
        return UnityHlslCompat.Semantics(header + "\n" + source);
    }

    //每个旧式纹理独立包装，GLSL 阶段才能把对应纹理和采样器准确合并。
    private static string CubeFunctions(string texture, string sampler) => $$"""
        float4 RU_texCUBE{{texture}}(float3 direction) { return {{texture}}.Sample({{sampler}}, direction); }
        float4 RU_texCUBElod{{texture}}(float4 direction) { return {{texture}}.SampleLevel({{sampler}}, direction.xyz, direction.w); }
        float4 RU_texCUBEbias{{texture}}(float4 direction) { return {{texture}}.SampleBias({{sampler}}, direction.xyz, direction.w); }
        """;

    private static string LegacyFunctions(string texture, string sampler) => $$"""
        float4 RU_tex2D{{texture}}(float2 uv) { return {{texture}}.Sample({{sampler}}, float2(uv.x,1-uv.y)); }
        float4 RU_tex2D{{texture}}(float3 uv) { return RU_tex2D{{texture}}(uv.xy); }
        float4 RU_tex2D{{texture}}(float4 uv) { return RU_tex2D{{texture}}(uv.xy); }
        float4 RU_tex2Dlod{{texture}}(float4 uv) { return {{texture}}.SampleLevel({{sampler}}, float2(uv.x,1-uv.y),uv.w); }
        float4 RU_tex2Dbias{{texture}}(float4 uv) { return {{texture}}.SampleBias({{sampler}}, float2(uv.x,1-uv.y),uv.w); }
        float4 RU_tex2Dproj{{texture}}(float4 uv) { return {{texture}}.Sample({{sampler}}, float2(uv.x/uv.w,1-uv.y/uv.w)); }
        float4 RU_tex2Dgrad{{texture}}(float2 uv,float2 dx,float2 dy) { return {{texture}}.SampleGrad({{sampler}},float2(uv.x,1-uv.y),float2(dx.x,-dx.y),float2(dy.x,-dy.y)); }
        """;

    //记录每个字符的花括号深度，不把函数局部变量或结构体字段当作材质参数。
    private static int[] Depths(string source)
    {
        int[] result = new int[source.Length]; int depth = 0;
        for (int i = 0; i < source.Length; i++)
        { result[i] = depth; if (source[i] == '{') depth++; else if (source[i] == '}') depth--; }
        return result;
    }
}

