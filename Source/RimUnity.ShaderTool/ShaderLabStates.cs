using System;
using System.IO;
using System.Collections.Generic;

namespace RimUnity.ShaderTool;

//保留 Pass 对 SubShader 状态的覆盖，动态属性引用仍以 [名称] 表示。
internal static class ShaderLabStates
{
    private static readonly HashSet<string> BlendFactors = new(StringComparer.OrdinalIgnoreCase)
    { "Zero", "One", "SrcColor", "SrcAlpha", "DstColor", "DstAlpha", "OneMinusSrcColor", "OneMinusSrcAlpha", "OneMinusDstColor", "OneMinusDstAlpha", "SrcAlphaSaturate" };

    //状态的关键字和值有固定边界，未知语句报错以免静默丢失渲染行为。
    internal static void Read(ShaderLabTokens tokens, string command, ShaderLabManifest manifest)
    {
        switch (command.ToLowerInvariant())
        {
            case "tags":
                tokens.Expect("{");
                while (!tokens.Take("}")) { string name = tokens.String(); tokens.Expect("="); manifest.tags[name] = tokens.String(); }
                break;
            case "name": manifest.states["Name"] = tokens.String(); break;
            case "lod": manifest.states["LOD"] = tokens.Read(); break;
            case "fog":
                tokens.Expect("{");
                if (!tokens.Read().Equals("Mode", StringComparison.OrdinalIgnoreCase)
                    || !tokens.Read().Equals("Off", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("固定管线 Fog 当前仅支持显式 Mode Off；启用雾需要对应渲染实现。");
                tokens.Expect("}");
                break;
            case "lighting":
                if (!tokens.Read().Equals("Off", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("仅支持显式关闭固定管线光照的顶点／片元程序。");
                break;
            case "fog":
                tokens.Expect("{"); tokens.Expect("Mode"); tokens.Expect("Off"); tokens.Expect("}");
                break;
            case "cull": manifest.states["Cull"] = Value(tokens); break;
            case "ztest": manifest.states["ZTest"] = Value(tokens); break;
            case "zwrite": manifest.states["ZWrite"] = Value(tokens); break;
            case "zclip": manifest.states["ZClip"] = Value(tokens); break;
            case "alphatomask": manifest.states["AlphaToMask"] = Value(tokens); break;
            case "colormask": manifest.states["ColorMask"] = Value(tokens); break;
            case "offset": manifest.states["Offset"] = Pair(tokens); break;
            case "blendop":
                string op = Value(tokens);
                if (tokens.Take(",")) op += ", " + Value(tokens);
                manifest.states["BlendOp"] = op; break;
            case "blend":
                string first = Value(tokens);
                if (first.Equals("Off", StringComparison.OrdinalIgnoreCase)) manifest.states["Blend"] = "Off";
                else
                {
                    CheckFactor(first); string second = Value(tokens); CheckFactor(second);
                    string blend = first + " " + second;
                    if (tokens.Take(","))
                    { string a = Value(tokens), b = Value(tokens); CheckFactor(a); CheckFactor(b); blend += ", " + a + " " + b; }
                    manifest.states["Blend"] = blend;
                }
                break;
            case "stencil":
                tokens.Expect("{");
                while (!tokens.Take("}"))
                {
                    string field = tokens.Read();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(field, @"^(Ref|ReadMask|WriteMask|Comp|Pass|Fail|ZFail|CompFront|PassFront|FailFront|ZFailFront|CompBack|PassBack|FailBack|ZFailBack)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        throw new NotSupportedException("未支持的 Stencil 状态：" + field);
                    manifest.states["Stencil." + field] = Value(tokens);
                }
                break;
            default: throw new NotSupportedException("未支持的 ShaderLab 状态或程序类型：" + command);
        }
    }

    //支持常量和材质属性引用，不计算 ShaderLab 表达式。
    private static string Value(ShaderLabTokens tokens) => tokens.Peek == "[" ? "[" + tokens.Balanced("[", "]") + "]" : tokens.Read();
    //Offset 使用逗号分隔的两个参数。
    private static string Pair(ShaderLabTokens tokens) { string a = Value(tokens); tokens.Expect(","); return a + ", " + Value(tokens); }
    //MRT 的索引不能误读为源混合因子。
    private static void CheckFactor(string value)
    {
        if (!value.StartsWith('[') && !BlendFactors.Contains(value)) throw new InvalidDataException("不支持的 Blend 因子或 MRT 索引：" + value);
    }
}
