using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//解析单 SubShader 的显式顶点片元 Pass，调用方逐个导出并保持顺序。
internal static class ShaderLabParser
{
    //先隔离程序中的括号，再解析 ShaderLab 容器。
    internal static ShaderLabSource Parse(string source, string profile, string[] keywords, int selectedPass = -1)
    {
        if (profile != "ui" && profile != "world") throw new ArgumentException("profile 必须为 ui 或 world。");
        source = ShaderLabTokens.WithoutComments(source);
        if (Regex.IsMatch(source, @"#pragma\s+surface\b"))
            throw new NotSupportedException("Surface Shader 依赖 Unity 光照生成管线，不在本次无光照范围；不会自动改成 Unlit。");
        List<string> programs = new(), includes = new();
        source = Regex.Replace(source, @"\b(CGPROGRAM|HLSLPROGRAM|CGINCLUDE|HLSLINCLUDE)\b(.*?)\b(ENDCG|ENDHLSL)\b", match =>
        {
            bool include = match.Groups[1].Value.EndsWith("INCLUDE", StringComparison.Ordinal);
            if (include) { includes.Add(match.Groups[2].Value); return ""; }
            programs.Add(match.Groups[2].Value); return " RU_PROGRAM_" + (programs.Count - 1) + " ";
        }, RegexOptions.Singleline);
        ShaderLabTokens tokens = new(source);
        tokens.Expect("Shader");
        ShaderLabManifest manifest = new() { shaderName = tokens.String(), profile = profile, keywords = keywords.Distinct().ToList() };
        tokens.Expect("{");
        ShaderLabManifest passManifest = new();
        var passManifests = new List<ShaderLabManifest>();
        var passPrograms = new List<int>();
        int subShaders = 0, passes = 0, blocks = 0, categoryDepth = 0;
        while (true)
        {
            if (tokens.Take("}")) { if (categoryDepth > 0) { categoryDepth--; continue; } break; }
            string command = tokens.Read();
            switch (command.ToLowerInvariant())
            {
                case "properties": ShaderLabProperties.Read(tokens, manifest); break;
                case "category": tokens.Expect("{"); categoryDepth++; break;
                case "subshader":
                    if (++subShaders != 1) throw new NotSupportedException("只支持一个 SubShader。");
                    tokens.Expect("{");
                    while (!tokens.Take("}"))
                    {
                        string state = tokens.Read();
                        if (state.Equals("Pass", StringComparison.OrdinalIgnoreCase))
                        {
                            passes++;
                            passManifest = new ShaderLabManifest();
                            int programIndex = -1;
                            tokens.Expect("{");
                            while (!tokens.Take("}"))
                            {
                                string item = tokens.Read();
                                if (item.StartsWith("RU_PROGRAM_", StringComparison.Ordinal))
                                {
                                    if (programIndex >= 0) throw new NotSupportedException("一个 Pass 只能包含一个顶点片元程序。");
                                    programIndex = int.Parse(item[11..]); blocks++;
                                }
                                else ShaderLabStates.Read(tokens, item, passManifest);
                            }
                            if (programIndex < 0) throw new NotSupportedException("不支持没有顶点片元程序的固定管线 Pass。");
                            passManifests.Add(passManifest); passPrograms.Add(programIndex);
                        }
                        else if (state.Equals("GrabPass", StringComparison.OrdinalIgnoreCase))
                        {
                            if (manifest.grabTexture != null || passes != 0 || profile != "world")
                                throw new NotSupportedException("只支持世界颜色 Pass 前的一次场景抓取。");
                            tokens.Expect("{");
                            manifest.grabTexture = tokens.Peek == "}" ? "_GrabTexture" : tokens.String();
                            tokens.Expect("}");
                            if (!Regex.IsMatch(manifest.grabTexture, @"^[A-Za-z_]\w*$"))
                                throw new InvalidDataException("GrabPass 纹理名称无效。");
                            manifest.approximations.Add("GrabPass 按渲染队列共享一次场景快照，不逐对象重复抓取。");
                        }
                        else ShaderLabStates.Read(tokens, state, manifest);
                    }
                    break;
                case "fallback": tokens.Read(); break;
                case "customeditor": tokens.String(); break;
                default:
                    if (categoryDepth > 0) ShaderLabStates.Read(tokens, command, manifest);
                    else throw new NotSupportedException("未支持的 ShaderLab 容器语法：" + command);
                    break;
            }
        }
        if (!tokens.End || subShaders != 1 || passes < 1 || blocks != passes || programs.Count != passes)
            throw new InvalidDataException("需要一个 SubShader，每个 Pass 必须有一个 CGPROGRAM/HLSLPROGRAM。");
        if (selectedPass < 0 && passes != 1) throw new NotSupportedException("多 Pass Shader 需要按顺序导出全部 Pass。");
        int chosen = selectedPass < 0 ? 0 : selectedPass;
        if (chosen >= passes) throw new InvalidDataException("Shader Pass 索引越界：" + chosen);
        passManifest = passManifests[chosen];
        foreach (var item in passManifest.tags) manifest.tags[item.Key] = item.Value;
        foreach (var item in passManifest.states) manifest.states[item.Key] = item.Value;
        string program = string.Join("\n", includes.Append(programs[passPrograms[chosen]]));
        //材质关键字属于整个 Shader，选中颜色 Pass 时排除只在其他 Pass 声明的变体。
        string[] Declared(string text) => Regex.Matches(text, @"(?m)^\s*#pragma\s+(?:shader_feature\w*|multi_compile(?:_local)?(?:_fragment)?)\s+([^\r\n]+)")
            .Cast<Match>().SelectMany(m => m.Groups[1].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
        string[] allKeywords = Declared(string.Join("\n", includes.Concat(programs))), passKeywords = Declared(program);
        manifest.keywords = manifest.keywords.Where(k => !allKeywords.Contains(k) || passKeywords.Contains(k)).ToList();
        if (manifest.tags.TryGetValue("LightMode", out string lightMode) && lightMode is "ForwardAdd" or "Deferred")
            throw new NotSupportedException("当前无法执行此渲染阶段：" + lightMode);
        program = Pragmas(program, manifest);
        return new ShaderLabSource(manifest, program);
    }

    //记录变体集合并应用调用方显式选择的关键字，未知编译阶段拒绝处理。
    private static string Pragmas(string source, ShaderLabManifest manifest)
    {
        string result = Regex.Replace(source, @"(?m)^\s*#pragma\s+(\w+)([^\r\n]*)", match =>
        {
            string kind = match.Groups[1].Value;
            string[] values = match.Groups[2].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (kind)
            {
                case "vertex": manifest.vertexEntry = One(values, kind); break;
                case "fragment": manifest.fragmentEntry = One(values, kind); break;
                case "target": case "exclude_renderers": case "only_renderers": break;
                case "multi_compile_fwdbase":
                    //前向基础程序由宿主提供光照，未接通的烘焙与阴影变体仍明确拒绝。
                    if (manifest.keywords.Any(k => k.StartsWith("SHADOWS_", StringComparison.Ordinal) || k.Contains("LIGHTMAP")))
                        throw new NotSupportedException("前向基础程序尚不支持此烘焙或阴影变体。");
                    manifest.keywordGroups.Add(new[] { "DIRECTIONAL" }); break;
                case "multi_compile_shadowcaster":
                    manifest.keywordGroups.Add(new[] { "SHADOWS_DEPTH" }); break;
                case "fragmentoption":
                    if (values.Length != 1 || values[0] != "ARB_precision_hint_fastest") throw new NotSupportedException("未知片元编译选项：" + string.Join(" ", values));
                    break;
                case "multi_compile_fog":
                    if (manifest.keywords.Any(k => k == "FOG_LINEAR" || k == "FOG_EXP" || k == "FOG_EXP2"))
                        throw new NotSupportedException("当前只支持无场景雾变体。");
                    manifest.keywordGroups.Add(new[] { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" }); break;
                case "multi_compile_instancing":
                    if (manifest.keywords.Contains("INSTANCING_ON")) throw new NotSupportedException("当前通过非实例化绘制提交，不支持 INSTANCING_ON 变体。");
                    manifest.keywordGroups.Add(new[] { "INSTANCING_ON" }); break;
                case "multi_compile_particles":
                    //当前仅生成不依赖深度纹理的普通粒子变体。
                    if (manifest.keywords.Contains("SOFTPARTICLES_ON"))
                        throw new NotSupportedException("深度软粒子变体尚未接通。");
                    manifest.keywordGroups.Add(new[] { "SOFTPARTICLES_ON" }); break;
                case "shader_feature": case "shader_feature_local": case "shader_feature_local_fragment":
                case "shader_feature_fragment": case "multi_compile": case "multi_compile_local":
                case "multi_compile_fragment": case "multi_compile_local_fragment":
                    manifest.keywordGroups.Add(values.Where(v => v != "_" && v != "__").ToArray()); break;
                default: throw new NotSupportedException("未支持的 Shader 编译指令：#pragma " + kind);
            }
            return "";
        });
        if (string.IsNullOrEmpty(manifest.vertexEntry) || string.IsNullOrEmpty(manifest.fragmentEntry))
            throw new InvalidDataException("缺少 #pragma vertex 或 #pragma fragment；Surface Shader 不支持自动转换。");
        foreach (string keyword in manifest.keywords)
            if (!Regex.IsMatch(keyword, @"^[A-Za-z_]\w*$") || !manifest.keywordGroups.Any(group => group.Contains(keyword)))
                throw new InvalidDataException("关键字未在 Shader 中声明：" + keyword);
        foreach (string[] group in manifest.keywordGroups)
            if (group.Count(manifest.keywords.Contains) > 1) throw new InvalidDataException("同一变体组不能同时启用多个关键字：" + string.Join(", ", group));
        return result;
    }

    //入口名称必须独立且唯一，避免把错误输入传给 Slang。
    private static string One(string[] values, string pragma) => values.Length == 1 && Regex.IsMatch(values[0], @"^[A-Za-z_]\w*$")
        ? values[0] : throw new InvalidDataException("无效 #pragma " + pragma);
}
