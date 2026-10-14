using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//把超出宿主普通参数容量的材质块接入宿主已有的动态存储块。
internal static class ShaderStorageLayout
{
    private const string Name = "_RimUnityMaterial";
    private const string Members = @"(?m)^\s*(float|int|uint|[iu]?vec[234]|mat[234])\s+(\w+)\s*;[^\S\r\n]*(?://[^\r\n]*)?";
    private const string Blocks = @"layout\(([^)]*)\)\s*uniform\s+\w+\s*\{([^{}]*)\}\s*(\w+)\s*;";

    //两阶段同时移动，保留成员名、默认值、颜色语义和参数访问表达式。
    internal static (string Vertex, string Fragment) Convert(string vertex, string fragment)
    {
        Match block = MaterialBlock(fragment);
        if (block == null) return (vertex, fragment);
        int size = 0;
        foreach (Match member in Regex.Matches(block.Groups[2].Value, Members))
        {
            var slot = ParseMember(member.Value, ""); int alignment = Alignment(slot.Type);
            size = Align(size, alignment) + MaterialLayout.GetStd140Size(slot.Type);
        }
        if (size <= MaterialLayout.MaxUniformBytes) return (vertex, fragment);
        int binding = Regex.Matches(vertex + fragment, @"layout\(([^)]*)\)").Cast<Match>()
            .Where(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b"))
            .Select(m => Regex.Match(m.Groups[1].Value, @"\bbinding\s*=\s*(\d+)"))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;
        string Move(string source)
        {
            Match original = MaterialBlock(source);
            if (original == null) throw new InvalidDataException("扩展材质参数缺少对应阶段的声明。");
            return source.Replace(original.Value, $"layout(std140, set = 2, binding = {binding}) readonly buffer {Name} {{"
                + original.Groups[2].Value + "} " + original.Groups[3].Value + ";");
        }
        return (Move(vertex), Move(fragment));
    }

    //复用宿主的逐成员解析和参数解析器，时间、纹理变换及材质动画继续自动更新。
    internal static MaterialLayout Extract(string source, string shaderName)
    {
        MaterialLayout layout = ShaderMetadataParser.ExtractMaterialLayout(source, shaderName, 2);
        Match block = Regex.Match(source, @"layout\([^)]*\)\s*readonly\s+buffer\s+" + Name + @"\s*\{([^{}]*)\}\s*\w+\s*;");
        if (!block.Success || layout.HasErrors) return layout;
        string textures = string.Join("\n", Regex.Matches(source, @"layout\([^)]*\)\s*uniform\s+sampler(?:2D|Cube)\s+\w+\s*;").Cast<Match>().Select(m => m.Value));
        var members = new List<MaterialUniformSlot>(); int size = 0;
        foreach (Match member in Regex.Matches(block.Groups[1].Value, Members))
        {
            var slot = ParseMember(member.Value, textures);
            size = Align(size, Alignment(slot.Type)); members.Add(slot with { Offset = size });
            size += MaterialLayout.GetStd140Size(slot.Type);
        }
        var storage = new MaterialUniformBlockSlot(ShaderPropertyId.GetOrCreate(Name), Align(size, 16), members.ToArray(), Array.Empty<MaterialBlockArraySlot>());
        return new MaterialLayout(layout.UniformSlots, layout.TextureSlots, layout.UniformSize, layout.StorageBufferSlots,
            layout.HasErrors, new[] { storage });
    }

    private static Match MaterialBlock(string source) => Regex.Matches(source, Blocks).Cast<Match>()
        .SingleOrDefault(m => Regex.IsMatch(m.Groups[1].Value, @"\bset\s*=\s*2\b") && Regex.IsMatch(m.Groups[1].Value, @"\bbinding\s*=\s*0\b"));
    private static MaterialUniformSlot ParseMember(string member, string textures)
    {
        var parsed = ShaderMetadataParser.ExtractMaterialLayout(textures + "\nlayout(set = 2, binding = 0) uniform M {\n" + member + "\n} m;", Name, 2);
        if (parsed.HasErrors || parsed.UniformSlots.Length != 1) throw new InvalidDataException("无法解析扩展材质成员：" + member);
        return parsed.UniformSlots[0];
    }
    private static int Alignment(MaterialUniformType type) => type is MaterialUniformType.Float or MaterialUniformType.Int ? 4 : type == MaterialUniformType.Vector2 ? 8 : 16;
    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
