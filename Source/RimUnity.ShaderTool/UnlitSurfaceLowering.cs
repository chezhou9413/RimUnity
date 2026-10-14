using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//生成 Surface 顶点与片元入口，保留原始表面函数及其光照模型。
internal static class UnlitSurfaceLowering
{
    private const string SurfacePragma = @"(?m)^[\t ]*#pragma\s+surface\s+(\w+)\s+(\w+)([^\r\n]*)";

    //在包含展开后、ShaderLabParser.Parse 前调用；普通顶点片元 Shader 原样返回。
    internal static string Lower(string source)
    {
        string clean = ShaderLabTokens.WithoutComments(source);
        if (Regex.IsMatch(clean, SurfacePragma)) clean = SurfaceContainers.ColorProgram(clean);
        MatchCollection pragmas = Regex.Matches(clean, SurfacePragma);
        if (pragmas.Count == 0) return source;
        if (pragmas.Count != 1) throw new NotSupportedException("一次只转换一个 Unlit Surface 程序。");
        Match pragma = pragmas[0];
        string lighting = pragma.Groups[2].Value;
        bool lit = lighting != "Unlit", standard = lighting == "Standard";
        string[] options = pragma.Groups[3].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        string vertex = null;
        foreach (string option in options)
        {
            if (option is "keepalpha" or "noshadow" or "fullforwardshadows" or "alpha:fade") continue;
            if (Regex.IsMatch(option, @"^vertex:[A-Za-z_]\w*$") && vertex == null) { vertex = option[7..]; continue; }
            throw new NotSupportedException("尚未接通此 Surface 生成选项：" + option);
        }
        MatchCollection blocks = Regex.Matches(clean, @"\b(CGPROGRAM|HLSLPROGRAM|CGINCLUDE|HLSLINCLUDE)\b(.*?)\b(ENDCG|ENDHLSL)\b", RegexOptions.Singleline);
        if (blocks.Count != 1 || !blocks[0].Groups[1].Value.EndsWith("PROGRAM", StringComparison.Ordinal))
            throw new NotSupportedException("当前 Unlit Surface 转换要求单一完整程序块；共享包含和额外 Pass 需单独接通。");
        Match block = blocks[0];
        string container = clean.Remove(block.Index, block.Length);
        if (Regex.IsMatch(container, @"\b(Pass|UsePass)\b", RegexOptions.IgnoreCase))
            throw new NotSupportedException("Unlit Surface 的额外颜色 Pass 或 UsePass 尚未接通。");
        if (Regex.IsMatch(clean, @"\bRU_Surface\w*\b")) throw new InvalidDataException("源 Shader 已使用保留的 RU_Surface 名称。");
        string program = block.Groups[2].Value;
        if (!lit) ValidateLighting(program);
        else if (!standard) SurfaceLighting.ValidateCustom(program, lighting);
        //已证明 LightingUnlit 不计算光照，只替代 SurfaceOutput 声明，其他光照调用仍报编译错误。
        program = Regex.Replace(program, @"(?m)^\s*#include\s+""(?:UnityPBSLighting|Lighting)\.cginc""[^\r\n]*", "");
        Match input = Regex.Match(program, @"\bstruct\s+Input\s*\{([^{}]*)\}\s*;", RegexOptions.Singleline);
        if (!input.Success) throw new NotSupportedException("Unlit Surface 需要简单的 Input 结构。");
        List<InputField> fields = ReadFields(input.Groups[1].Value);
        string surf = pragma.Groups[1].Value;
        string outputType = standard ? "SurfaceOutputStandard" : "SurfaceOutput";
        if (!Regex.IsMatch(program, @"\bvoid\s+" + Regex.Escape(surf) + @"\s*\(\s*Input\s+\w+\s*,\s*inout\s+" + outputType + @"\s+\w+\s*\)"))
            throw new NotSupportedException("Unlit Surface 函数签名不受支持。");
        if (vertex != null && !Regex.IsMatch(program, @"\bvoid\s+" + Regex.Escape(vertex) + @"\s*\(\s*inout\s+appdata_full\s+\w+\s*,\s*out\s+Input\s+\w+\s*\)"))
            throw new NotSupportedException("Surface 顶点函数只支持 inout appdata_full 与 out Input。");
        program = Regex.Replace(program, SurfacePragma, "#pragma vertex RU_SurfaceVert\n#pragma fragment RU_SurfaceFrag");
        string declarations;
        string entries = Entries(program, fields, surf, vertex, options.Contains("keepalpha") || options.Contains("alpha:fade"), lighting, out declarations);
        string generated = SurfaceOutput + (lit ? SurfaceLighting.Declarations(standard) : "") + declarations + program + entries;
        //alpha:fade 由 Surface 编译器产生这些状态，不能只保留 SubShader 的隐式默认值。
        string states = options.Contains("alpha:fade") ? "Blend SrcAlpha OneMinusSrcAlpha\nZWrite Off\n" : "";
        string replacement = "Pass\n{\n" + states + block.Groups[1].Value + "\n" + generated + "\n" + block.Groups[3].Value + "\n}\n";
        return clean.Remove(block.Index, block.Length).Insert(block.Index, replacement);
    }

    //函数必须确实只返回零光照和原 Alpha，不能依据 Unlit 名称猜测行为。
    private static void ValidateLighting(string program)
    {
        Match lighting = Regex.Match(program, @"\b(?:inline\s+)?(?:half|float)4\s+LightingUnlit\s*\(\s*SurfaceOutput\s+(\w+)\s*,\s*(?:half|float)3\s+\w+\s*,\s*(?:half|float)\s+\w+\s*\)\s*\{([^{}]*)\}", RegexOptions.Singleline);
        if (!lighting.Success || Regex.Matches(program, @"\bLightingUnlit\s*\(").Count != 1)
            throw new NotSupportedException("无法证明 LightingUnlit 是单一无光照函数。");
        string body = Regex.Replace(lighting.Groups[2].Value, @"\s+", "");
        string alpha = lighting.Groups[1].Value + ".Alpha";
        if (body != "returnhalf4(0,0,0," + alpha + ");" && body != "returnfloat4(0,0,0," + alpha + ");")
            throw new NotSupportedException("LightingUnlit 含额外运算，不能按 Emission/Alpha 直接转换。");
        if (Regex.IsMatch(program, @"\b(?:LightingUnlit_GI|SurfaceOutputStandard|INTERNAL_DATA|WorldNormalVector|WorldReflectionVector)\b"))
            throw new NotSupportedException("Surface 程序依赖额外光照或切线空间生成逻辑。");
    }

    //每个输入均有可追溯的顶点来源，自定义输入必须来自显式顶点函数。
    private static List<InputField> ReadFields(string body)
    {
        var result = new List<InputField>();
        foreach (string declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(declaration)) continue;
            Match field = Regex.Match(declaration, @"^\s*((?:float|half|fixed)([1-4]?))\s+(\w+)\s*(?::\s*(COLOR0?))?\s*$");
            if (!field.Success) throw new NotSupportedException("不支持的 Surface Input 字段：" + declaration.Trim());
            result.Add(new InputField(field.Groups[1].Value, field.Groups[3].Value,
                field.Groups[2].Value.Length == 0 ? 1 : int.Parse(field.Groups[2].Value), field.Groups[4].Success));
        }
        if (result.Count > 8 || result.Select(f => f.Name).Distinct().Count() != result.Count)
            throw new NotSupportedException("Surface Input 需要最多八个互不重名的插值字段。");
        return result;
    }

    //先执行原顶点函数，再计算自动输入；片元阶段直接调用原 surf。
    private static string Entries(string program, List<InputField> fields, string surf, string vertex, bool keepAlpha, string lighting, out string declarations)
    {
        bool lit = lighting != "Unlit", standard = lighting == "Standard";
        StringBuilder uniforms = new(), varying = new("\nstruct RU_SurfaceVaryings {\nfloat4 position : SV_POSITION;\n"), assignments = new();
        for (int index = 0; index < fields.Count; index++)
        {
            InputField field = fields[index];
            varying.AppendLine(field.Type + " " + field.Name + " : TEXCOORD" + index + ";");
            string expression = null;
            Match uv = Regex.Match(field.Name, @"^uv([2-4]?)_(\w+)$");
            if (field.Color)
            {
                if (field.Components != 4) throw new NotSupportedException("Surface 顶点颜色必须是四分量。");
                expression = "v.color";
            }
            else if (uv.Success)
            {
                if (field.Components < 2) throw new NotSupportedException("Surface UV 至少需要两个分量。");
                string channel = uv.Groups[1].Value, coordinate = "v.texcoord" + (channel.Length == 0 ? "" : (int.Parse(channel) - 1).ToString());
                string st = "_" + uv.Groups[2].Value + "_ST";
                if (!Regex.IsMatch(program, @"\b(?:float|half|fixed)4\s+" + Regex.Escape(st) + @"\s*;")) uniforms.AppendLine("uniform float4 " + st + ";");
                string xy = coordinate + ".xy * " + st + ".xy + " + st + ".zw";
                expression = field.Components == 2 ? xy : "float" + field.Components + "(" + xy + ", " + coordinate + (field.Components == 3 ? ".z)" : ".zw)");
            }
            else if (field.Name == "screenPos" && field.Components == 4) expression = "ComputeScreenPos(o.position)";
            else if (field.Name == "worldPos" && field.Components == 3) expression = "mul(unity_ObjectToWorld, v.vertex).xyz";
            else if (field.Name == "worldNormal" && field.Components == 3) expression = "UnityObjectToWorldNormal(v.normal)";
            else if (field.Name == "viewDir" && field.Components == 3 && lit) expression = "float3(0,0,0)";
            else if (field.Name is "screenPos" or "worldPos" or "worldNormal") throw new NotSupportedException("Surface 自动输入维度不正确：" + field.Name);
            else if (vertex == null) throw new NotSupportedException("Surface Input 没有顶点来源：" + field.Name);
            assignments.AppendLine("o." + field.Name + " = " + (expression ?? "custom." + field.Name) + ";");
        }
        if (lit)
        {
            varying.AppendLine("float3 ruNormal : TEXCOORD" + fields.Count + ";");
            varying.AppendLine("float4 ruTangent : TEXCOORD" + (fields.Count + 1) + ";");
            varying.AppendLine("float3 ruWorld : TEXCOORD" + (fields.Count + 2) + ";");
        }
        varying.AppendLine("};\nRU_SurfaceVaryings RU_SurfaceVert(appdata_full v) {\nInput custom = (Input)0;");
        if (vertex != null) varying.AppendLine(vertex + "(v, custom);");
        varying.AppendLine("RU_SurfaceVaryings o = (RU_SurfaceVaryings)0;\no.position = UnityObjectToClipPos(v.vertex);");
        if (lit) assignments.AppendLine("o.ruNormal = UnityObjectToWorldNormal(v.normal);\no.ruTangent = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * sign(determinant((float3x3)unity_ObjectToWorld)));\no.ruWorld = mul(unity_ObjectToWorld,v.vertex).xyz;");
        varying.Append(assignments).AppendLine("return o;\n}\nfloat4 RU_SurfaceFrag(RU_SurfaceVaryings i) : SV_Target {\nInput input = (Input)0;");
        foreach (InputField field in fields) varying.AppendLine("input." + field.Name + " = i." + field.Name + ";");
        if (lit)
        {
            varying.AppendLine("float3 n=normalize(i.ruNormal), t=RU_SafeNormalize(i.ruTangent.xyz), b=cross(n,t)*i.ruTangent.w;\nfloat3 view=normalize(_WorldSpaceCameraPos-i.ruWorld);");
            if (fields.Any(f => f.Name == "viewDir")) varying.AppendLine("input.viewDir=float3(dot(view,t),dot(view,b),dot(view,n));");
        }
        string outputType = standard ? "SurfaceOutputStandard" : "SurfaceOutput";
        varying.AppendLine(outputType + " output = (" + outputType + ")0;\noutput.Normal = float3(0,0,1);\noutput.Alpha = 1;");
        if (standard) varying.AppendLine("output.Occlusion = 1;");
        varying.AppendLine(surf + "(input, output);");
        if (lit) varying.AppendLine(SurfaceLighting.Evaluate(lighting));
        varying.AppendLine("return float4(" + (lit ? "radiance+" : "") + "output.Emission, " + (keepAlpha ? "output.Alpha" : "1") + ");\n}");
        declarations = uniforms.ToString();
        return varying.ToString();
    }

    private sealed record InputField(string Type, string Name, int Components, bool Color);
    private const string SurfaceOutput = "struct SurfaceOutput { half3 Albedo; half3 Normal; half3 Emission; half Specular; half Gloss; half Alpha; };\n";
}
