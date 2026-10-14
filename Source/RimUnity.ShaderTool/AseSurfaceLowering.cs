using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//为 ASE 自定义 Surface 光照生成显式入口，保留原始材质表达式。
internal static class AseSurfaceLowering
{
    internal static string Lower(string source)
    {
        if (!Regex.IsMatch(source, @"#pragma\s+surface\s+\w+\s+StandardCustomLighting\b")) return null;
        string clean = SurfaceContainers.ColorProgram(ShaderLabTokens.WithoutComments(source));
        Match block = Regex.Match(clean, @"\bCGPROGRAM\b(.*?)\bENDCG\b", RegexOptions.Singleline);
        string program = block.Groups[1].Value;
        Match pragma = Regex.Match(program, @"#pragma\s+surface\s+(\w+)\s+StandardCustomLighting([^\r\n]*)");
        string vertex = null;
        bool noAmbient = false;
        foreach (string option in pragma.Groups[2].Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (option == "noambient") { noAmbient = true; continue; }
            if (option.StartsWith("vertex:")) { vertex = option[7..]; continue; }
            if (option is "keepalpha" or "fullforwardshadows" or "nolightmap" or "nodynlightmap" or "nodirlightmap" or "nofog") continue;
            throw new NotSupportedException("ASE Surface 选项尚未接通：" + option);
        }
        if (vertex == null) throw new NotSupportedException("ASE Surface 需要显式顶点函数。");
        Match vertexSignature = Regex.Match(program, @"\bvoid\s+" + Regex.Escape(vertex) + @"\s*\(\s*inout\s+(\w+)\s+\w+\s*,\s*out\s+Input\s+\w+\s*\)");
        if (!vertexSignature.Success) throw new NotSupportedException("ASE 顶点函数签名不匹配。");
        Match input = Regex.Match(program, @"\bstruct\s+Input\s*\{([^{}]*)\}\s*;", RegexOptions.Singleline);
        string body = input.Groups[1].Value.Replace("INTERNAL_DATA", "float3 internalSurfaceTtoW0; float3 internalSurfaceTtoW1; float3 internalSurfaceTtoW2;");
        var fields = new List<(string type, string name, string semantic)>();
        foreach (string declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(declaration)) continue;
            Match field = Regex.Match(declaration, @"^\s*((?:float|half|fixed)[1-4]?)\s+(\w+)\s*(?::\s*(COLOR|VFACE))?\s*$");
            if (!field.Success) throw new NotSupportedException("ASE Input 字段尚未接通：" + declaration);
            fields.Add((field.Groups[1].Value, field.Groups[2].Value, field.Groups[3].Value));
        }
        //局部位置由顶点直接传递，片元不读取宿主仅顶点可见的模型块。
        program = Regex.Replace(program, @"mul\s*\(\s*unity_WorldToObject\s*,\s*float4\s*\(\s*i\.worldPos\s*,\s*1\s*\)\s*\)", "i.ruLocalPosition");
        fields.Add(("float3", "ruLocalPosition", ""));
        string declarationBody = string.Join("\n", fields.Select(f => f.type + " " + f.name + ";"));
        program = Regex.Replace(program, @"\bstruct\s+Input\s*\{[^{}]*\}\s*;", "struct Input {\n" + declarationBody + "\n};", RegexOptions.Singleline);
        program = Regex.Replace(program, @"(?m)^\s*#include\s+""(?:UnityPBSLighting|Lighting)\.cginc""[^\r\n]*", "");
        program = Regex.Replace(program, @"#pragma\s+surface[^\r\n]*", "#pragma vertex RU_AseVert\n#pragma fragment RU_AseFrag");
        var output = new StringBuilder("\nstruct RU_AseVaryings { float4 position : SV_POSITION;\n");
        int location = 0;
        foreach (var field in fields.Where(f => f.semantic != "VFACE")) output.AppendLine(field.type + " " + field.name + " : TEXCOORD" + location++ + ";");
        output.AppendLine("};\nRU_AseVaryings RU_AseVert(" + vertexSignature.Groups[1].Value + " v) { Input custom=(Input)0; " + vertex + "(v,custom); RU_AseVaryings o=(RU_AseVaryings)0;");
        output.AppendLine("o.position=UnityObjectToClipPos(v.vertex); float3 world=mul(unity_ObjectToWorld,v.vertex).xyz; float3 n=UnityObjectToWorldNormal(v.normal); float3 t=UnityObjectToWorldDir(v.tangent.xyz); float3 b=cross(n,t)*v.tangent.w*sign(determinant((float3x3)unity_ObjectToWorld)); float3 view=normalize(_WorldSpaceCameraPos-world);");
        foreach (var field in fields.Where(f => f.semantic != "VFACE"))
        {
            string expression;
            Match uv = Regex.Match(field.name, @"^uv([2-4]?)_texcoord[2-4]?$");
            if (uv.Success) expression = "v.texcoord" + (uv.Groups[1].Value.Length == 0 ? "" : (int.Parse(uv.Groups[1].Value) - 1).ToString());
            else expression = field.name switch
            {
                "worldPos" => "world", "worldNormal" => "n", "viewDir" => "float3(dot(view,t),dot(view,b),dot(view,n))",
                "worldRefl" => "-view", "screenPos" => "ComputeScreenPos(o.position)", "ruLocalPosition" => "v.vertex.xyz",
                "internalSurfaceTtoW0" => "float3(t.x,b.x,n.x)", "internalSurfaceTtoW1" => "float3(t.y,b.y,n.y)", "internalSurfaceTtoW2" => "float3(t.z,b.z,n.z)",
                _ => field.semantic == "COLOR" ? "v.color" : "custom." + field.name
            };
            output.AppendLine("o." + field.name + "=" + expression + ";");
        }
        output.AppendLine("return o; }\nfloat4 RU_AseFrag(RU_AseVaryings v, bool front : SV_IsFrontFace) : SV_Target { Input i=(Input)0;");
        foreach (var field in fields) output.AppendLine("i." + field.name + "=" + (field.semantic == "VFACE" ? "(front ? 1.0 : -1.0)" : "v." + field.name) + ";");
        output.AppendLine("SurfaceOutputCustomLightingCustom s=(SurfaceOutputCustomLightingCustom)0; s.Normal=float3(0,0,1); s.Alpha=1; " + pragma.Groups[1].Value + "(i,s);");
        output.AppendLine("UnityGIInput data=(UnityGIInput)0; data.worldPos=i.worldPos; data.atten=1; UnityGI gi=(UnityGI)0; gi.light.color=_LightColor0.rgb; LightingStandardCustomLighting_GI(s,data,gi); float4 result=LightingStandardCustomLighting(s,normalize(_WorldSpaceCameraPos-i.worldPos),gi); return float4(result.rgb+s.Emission,result.a); }");
        string helpers = Helpers.Replace("RU_AMBIENT", noAmbient ? "float3(0,0,0)" : "_RU_MapAmbient.rgb");
        string generated = helpers + program + output;
        return clean.Remove(block.Index, block.Length).Insert(block.Index, "Pass {\nCGPROGRAM\n" + generated + "\nENDCG\n}");
    }

    //没有烘焙光照贴图；实时方向光和环境色来自宿主材质帧参数。
    private const string Helpers = """
        #define UNITY_PASS_FORWARDBASE 1
        #define INTERNAL_DATA float3 internalSurfaceTtoW0; float3 internalSurfaceTtoW1; float3 internalSurfaceTtoW2;
        #define WorldNormalVector(data,normal) float3(dot(data.internalSurfaceTtoW0,normal),dot(data.internalSurfaceTtoW1,normal),dot(data.internalSurfaceTtoW2,normal))
        #define WorldReflectionVector(data,normal) reflect(data.worldRefl,WorldNormalVector(data,normal))
        float4 _RU_MapSunColor, _RU_MapMoonColor, _RU_MapSunDirection, _RU_MapMoonDirection, _RU_MapAmbient;
        #define _LightColor0 (any(_RU_MapSunColor.rgb) ? _RU_MapSunColor : _RU_MapMoonColor)
        float3 UnityWorldSpaceLightDir(float3 p) { return any(_RU_MapSunColor.rgb) ? _RU_MapSunDirection.xyz : _RU_MapMoonDirection.xyz; }
        struct UnityLight { float3 color; float3 dir; float ndotl; };
        struct UnityIndirect { float3 diffuse; float3 specular; };
        struct UnityGI { UnityLight light; UnityIndirect indirect; };
        struct UnityGIInput { float3 worldPos; float atten; float4 lightmapUV; };
        UnityGI UnityGI_Base(UnityGIInput data, float occlusion, float3 normal) { UnityGI gi=(UnityGI)0; gi.light.color=_LightColor0.rgb*data.atten; gi.light.dir=UnityWorldSpaceLightDir(data.worldPos); gi.indirect.diffuse=RU_AMBIENT*occlusion; return gi; }
        """;
}
