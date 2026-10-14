using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Waylith.Rendering;

namespace RimUnity.ShaderTool;

//运行时从片元源码读取材质布局，因此片元文件必须同时描述顶点阶段资源。
internal static class ShaderLabStages
{
    //补齐未使用阶段的声明，使两阶段使用完全一致的材质绑定布局。
    internal static void Complete(string directory, ShaderLabManifest manifest)
    {
        string vertexPath = Path.Combine(directory, manifest.vertexFile), fragmentPath = Path.Combine(directory, manifest.fragmentFile);
        string vertex = File.ReadAllText(vertexPath), fragment = File.ReadAllText(fragmentPath);
        if (manifest.profile == "ui") fragment = UiMaskAdapter.Apply(fragment);
        if (Regex.IsMatch(vertex + fragment, @"uniform\s+sampler2D\s+_CameraDepthTexture\b"))
            throw new NotSupportedException("此变体实际采样相机深度，Waylith 深度纹理绑定尚未接通；不会导出成白色默认纹理。");
        if (!Regex.IsMatch(vertex, @"\bgl_Position(?:\.[xyzw]+)?\s*=")) throw new InvalidDataException("顶点程序没有写出 SV_POSITION。");
        if (manifest.profile == "world" && Regex.IsMatch(fragment, @"uniform\s+block_RU_(CameraData|ObjectData)_"))
            throw new NotSupportedException("世界配置的对象模型和相机顶点块不能在片元阶段访问；请由顶点函数通过 varying 传递计算结果。");
        Match vertexBlock = Block(vertex), fragmentBlock = Block(fragment);
        if (manifest.profile == "ui" && vertexBlock != null)
            vertex = vertex.Replace(vertexBlock.Value, vertexBlock.Value.Insert(vertexBlock.Value.IndexOf('}'),
                "\nfloat _UiMaskCount; // @default = 0.0\nfloat _UiRectMaskCount; // @default = 0.0\n"));
        if (vertexBlock != null && fragmentBlock == null) fragment = Insert(fragment, vertexBlock.Value);
        if (fragmentBlock != null && vertexBlock == null) vertex = Insert(vertex, fragmentBlock.Value);
        (vertex, fragment) = RemoveUnusedMaterialMembers(vertex, fragment);
        //片元描述也保留仅由顶点阶段使用的存储绑定。
        var vertexStorage = Storage(vertex); var fragmentStorage = Storage(fragment);
        foreach (var item in vertexStorage)
            if (!fragmentStorage.ContainsKey(item.Key)) fragment = Insert(fragment, item.Value.Value);
        foreach (var item in fragmentStorage)
            if (!vertexStorage.ContainsKey(item.Key)) vertex = Insert(vertex, item.Value.Value);
        Dictionary<int, Match> vertexTextures = Textures(vertex), fragmentTextures = Textures(fragment);
        if (vertexTextures.Keys.Union(fragmentTextures.Keys).Count() > 16) throw new NotSupportedException("每阶段最多支持 16 个纹理采样器。");
        foreach (var item in vertexTextures)
        {
            if (!fragmentTextures.ContainsKey(item.Key)) fragment = Insert(fragment, item.Value.Value + " // @vertex");
            else fragment = fragment.Replace(fragmentTextures[item.Key].Value, fragmentTextures[item.Key].Value + " // @vertex");
        }
        foreach (var item in fragmentTextures)
            if (!vertexTextures.ContainsKey(item.Key)) vertex = Insert(vertex, item.Value.Value);
        if (manifest.colorSpace == "Linear" && manifest.tags.GetValueOrDefault("LightMode") != "ShadowCaster")
            (vertex, fragment) = RimUnity.Rendering.ShaderColorSpace.Convert(vertex, fragment);
        //此 API 的返回值表示发现错误，不能按 Try 风格反向解释。
        if (ShaderMetadataParser.ValidateStageInterface(vertex, fragment, vertexPath, fragmentPath))
            throw new InvalidDataException("顶点输出与片元输入不一致。");
        File.WriteAllText(vertexPath, vertex, new UTF8Encoding(false));
        File.WriteAllText(fragmentPath, fragment, new UTF8Encoding(false));
        ShaderCompiler.Verify(vertexPath); ShaderCompiler.Verify(fragmentPath);
    }

    //Slang 会保留常量缓冲区中未使用的成员，可能超出宿主材质容量。
    //仅移除两个最终阶段均未使用的成员。
    private static (string Vertex, string Fragment) RemoveUnusedMaterialMembers(string vertex, string fragment)
    {
        var blocks = new[] { Block(vertex), Block(fragment) }.Where(b => b != null).ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in new[] { (vertex, Block(vertex)), (fragment, Block(fragment)) })
        {
            if (pair.Item2 == null) continue;
            string instance = Regex.Match(pair.Item2.Value, @"\}\s*(\w+)\s*;").Groups[1].Value;
            string program = pair.Item1.Replace(pair.Item2.Value, "");
            //整体缓冲区复制依赖原始结构布局，此时不能裁剪字段。
            if (Regex.IsMatch(program, @"\b" + Regex.Escape(instance) + @"\b(?!\s*\.)")) return (vertex, fragment);
            foreach (Match access in Regex.Matches(program, @"\b" + Regex.Escape(instance) + @"\s*\.\s*(\w+)")) used.Add(access.Groups[1].Value);
        }
        string Trim(string program) => Regex.Replace(program, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*\w+\s*;", block =>
        {
            if (!Regex.IsMatch(block.Groups[1].Value, @"\bset\s*=\s*2\b") || !Regex.IsMatch(block.Groups[1].Value, @"\bbinding\s*=\s*0\b")) return block.Value;
            string body = Regex.Replace(block.Groups[2].Value, @"(?m)^\s*(?:float|int|uint|[iu]?vec[234]|mat[234])\s+(\w+)\s*;[^\S\r\n]*(?://[^\r\n]*)?\r?\n?", member => used.Contains(member.Groups[1].Value) ? member.Value : "");
            return block.Value.Replace(block.Groups[2].Value, body);
        });
        return (Trim(vertex), Trim(fragment));
    }

    //材质块只支持唯一的 set2/binding0，保留块实例名以便复制未使用的声明。
    private static Match Block(string source) => Regex.Matches(source, @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{[^{}]*\}\s*\w+\s*;").Cast<Match>()
        .SingleOrDefault(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b") && Regex.IsMatch(m.Groups[1].Value, @"\bbinding\s*=\s*0\b"));
    //合并后的采样器绑定号不需要连续，按原绑定保留即可。
    private static Dictionary<int, Match> Storage(string source) => Regex.Matches(source, @"layout\(([^)]*)\)\s*(?:readonly\s+)?buffer\s+\w+\s*\{[^{}]*\}\s*\w*\s*;").Cast<Match>()
        .Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b"))
        .ToDictionary(m => int.Parse(Regex.Match(m.Groups[1].Value, @"\bbinding\s*=\s*(\d+)").Groups[1].Value));
    private static Dictionary<int, Match> Textures(string source) => Regex.Matches(source, @"layout\(([^)]*)\)\s*uniform\s+sampler(?:2D|Cube)\s+\w+\s*;[^\S\r\n]*(?://[^\r\n]*)?").Cast<Match>()
        .Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b"))
        .ToDictionary(m => int.Parse(Regex.Match(m.Groups[1].Value, @"\bbinding\s*=\s*(\d+)").Groups[1].Value));
    //声明放在版本指令之后，不打断原程序或已有行号指令。
    private static string Insert(string source, string declaration) => source.Insert(source.IndexOf('\n') + 1, declaration + "\n");
}

