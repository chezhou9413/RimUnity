using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RimUnity.Rendering;

//在创建管线前拒绝描述符冲突，不能让纹理和存储缓冲区占用同一个 GPU 槽位。
internal static class ShaderBindingValidation
{
    //每阶段的槽位唯一；跨阶段共享的槽位必须具有一致的描述符类型，块名可以不同。
    internal static void Validate(string vertex, string fragment)
    {
        var resources = Read(vertex, "顶点");
        foreach (var entry in Read(fragment, "片元"))
            if (resources.TryGetValue(entry.Key, out string other) && other != entry.Value)
                throw new InvalidDataException($"Shader 两阶段绑定不一致：{entry.Key}，{other} / {entry.Value}");
    }

    //GLSL 声明已经由转换器规范化；注释不参与槽位登记。
    internal static Dictionary<string, string> Read(string source, string stage)
    {
        source = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        const string declaration = @"layout\s*\(([^)]*)\)\s*(?:(?:readonly|writeonly|coherent|restrict|volatile)\s+)*(uniform|buffer)\s+(\w+)(?:\s+(\w+))?";
        foreach (Match match in Regex.Matches(source, declaration))
        {
            Match set = Regex.Match(match.Groups[1].Value, @"\bset\s*=\s*(\d+)");
            Match binding = Regex.Match(match.Groups[1].Value, @"\bbinding\s*=\s*(\d+)");
            if (!set.Success || !binding.Success) continue;
            string key = "set " + set.Groups[1].Value + "/binding " + binding.Groups[1].Value;
            string value = match.Groups[2].Value == "buffer" ? "storage buffer"
                : match.Groups[4].Success ? "uniform " + match.Groups[3].Value : "uniform buffer";
            if (!bindings.TryAdd(key, value))
                throw new InvalidDataException($"Shader {stage}绑定冲突：{key}，{bindings[key]} / {value}");
        }
        return bindings;
    }
}
