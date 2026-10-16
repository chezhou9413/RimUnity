using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//将已核对的三角形描边几何阶段展开为顶点阶段，运行时提供每条边的十二个顶点。
internal static class TriangleEdgeLowering
{
    //源程序指纹防止同名但算法不同的几何 Shader 被错误转换。
    internal static string Lower(string source, out bool lowered)
    {
        lowered = false;
        if (!Regex.IsMatch(source, @"#pragma\s+geometry\b")) return source;
        string normalized = Regex.Replace(ShaderLabTokens.WithoutComments(source), @"\s+", "");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        if (hash != "16ea4ed17bd237bc7028ee4f67b2da3ea912e7fccbd7c3675a4b21c71f581f42") return source;
        source = ShaderLabTokens.WithoutComments(source);
        var line = Function(source, "convetLine");
        string body = source.Substring(line.Start, line.Length);
        //Vulkan 投影翻转 Y 后，叉积方向和角点朝向也翻转；展开仍须位于原三角形的同一侧。
        body = body.Replace("dirL = normalize(dirL) * _OutlineWidth;",
            "dirL = normalize(dirL) * _OutlineWidth * sign(unity_CameraInvProjection[0][0] * unity_CameraInvProjection[1][1]);");
        //逆投影已包含 Vulkan 的 Y 翻转；宿主绘制前会把 _ProjectionParams.x 写成 -1，不能再次翻转偏移。
        body = body.Replace("dirL.y *= _ProjectionParams.x;", "");
        int begin = body.IndexOf('{'), emission = body.IndexOf("if (drawSpot)", StringComparison.Ordinal);
        if (emission < begin) throw new InvalidOperationException("描边几何程序缺少预期的顶点输出段。");
        string expanded = "v2f RU_Line(v2f from, v2f to, bool drawSpot, uint slot) " + body.Substring(begin, emission - begin) + """
            v2f result = from;
            if (slot == 0) result = toSpotT;
            else if (slot == 1) result = toSpotL;
            else if (slot == 2 || slot == 6 || slot == 11) result = to;
            else if (slot == 3) result = fromSpotB;
            else if (slot == 4) result = fromSpotL;
            else if (slot == 8 || slot == 9) result = lineBL;
            else if (slot == 10) result = lineTL;
            if (slot < 6 && !drawSpot) result.vertex = from.vertex;
            return result;
        }
        struct RU_EdgeInput { float4 vertex : POSITION; float4 to : TEXCOORD1; float4 third : TEXCOORD2; };
        v2f RU_EdgeVertex(RU_EdgeInput input)
        {
            appdata a = (appdata)0; a.vertex = input.vertex;
            appdata b = (appdata)0; b.vertex = float4(input.to.xyz, 1);
            appdata c = (appdata)0; c.vertex = float4(input.third.xyz, 1);
            v2f from = vert(a), to = vert(b), third = vert(c);
            bool drawSpot = dot(from.vertex.xyw, cross(to.vertex.xyw, third.vertex.xyw))
                * sign(unity_CameraInvProjection[0][0] * unity_CameraInvProjection[1][1]) < 0;
            return RU_Line(from, to, drawSpot, (uint)input.to.w);
        }
        """;
        source = source.Remove(line.Start, line.Length).Insert(line.Start, expanded);
        var geometry = Function(source, "geom");
        source = source.Remove(geometry.Start, geometry.Length);
        source = Regex.Replace(source, @"\[maxvertexcount\s*\(36\)\s*\]", "");
        source = Regex.Replace(source, @"#pragma\s+geometry\s+geom", "");
        source = Regex.Replace(source, @"#pragma\s+vertex\s+vert\b", "#pragma vertex RU_EdgeVertex");
        lowered = true;
        return source;
    }

    //只提取已通过指纹核对的函数体，保留源程序的描边宽度和深度计算。
    private static (int Start, int Length) Function(string source, string name)
    {
        Match match = Regex.Match(source, @"\bvoid\s+" + name + @"\s*\([^)]*\)\s*\{");
        if (!match.Success) throw new InvalidOperationException("未找到几何函数：" + name);
        int end = match.Index + match.Length, depth = 1;
        while (end < source.Length && depth > 0) { if (source[end] == '{') depth++; else if (source[end] == '}') depth--; end++; }
        if (depth != 0) throw new InvalidOperationException("几何函数括号不匹配。");
        return (match.Index, end - match.Index);
    }
}
