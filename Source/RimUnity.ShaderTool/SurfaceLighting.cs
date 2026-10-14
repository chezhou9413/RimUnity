using System;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//Surface 与生成式材质共用标准 BRDF，方向与环境色由地图光照提供。
internal static class SurfaceLighting
{
    //自定义光照保留源函数，当前只接受 Unity 的三参数漫反射签名。
    internal static void ValidateCustom(string program, string model)
    {
        string name = Regex.Escape("Lighting" + model);
        if (!Regex.IsMatch(program, @"\b(?:half|float|fixed)4\s+" + name + @"\s*\(\s*SurfaceOutput\s+\w+\s*,\s*(?:half|float|fixed)3\s+\w+\s*,\s*(?:half|float|fixed)\s+\w+\s*\)"))
            throw new NotSupportedException("Surface 自定义光照需要 SurfaceOutput、光线方向和衰减三个参数：" + model);
        if (Regex.IsMatch(program, @"\b" + name + @"_GI\b")) throw new NotSupportedException("自定义 GI 光照尚未接通：" + model);
    }

    //标准光照与自定义光照使用相同的地图输入，不修改源 surf 中的程序。
    internal static string Declarations(bool standard)
    {
        string helpers = GraphLighting.Program[..GraphLighting.Program.IndexOf("struct RU_GraphAttributes", StringComparison.Ordinal)];
        return helpers + SurfaceShadows.Program + (standard
            ? "\nstruct SurfaceOutputStandard { half3 Albedo; half3 Normal; half3 Emission; half Metallic; half Smoothness; half Occlusion; half Alpha; };\n"
            : "\nstatic float4 _LightColor0;\n");
    }

    //表面法线从切线空间变换到世界空间，自发光由调用方仅叠加一次。
    internal static string Evaluate(string model)
    {
        string basis = "n=normalize(t*output.Normal.x+b*output.Normal.y+n*output.Normal.z);\nfloat shadow=RU_SurfaceShadow(i.ruWorld,n);\n";
        if (model == "Standard") return basis + """
#ifdef UNITY_COLORSPACE_GAMMA
float dielectric=0.220916301;
#else
float dielectric=0.04;
#endif
float3 diffuse=output.Albedo*((1-dielectric)*(1-output.Metallic));
float3 specular=lerp(dielectric.xxx,output.Albedo,output.Metallic);
float3 radiance=diffuse*RU_GraphRadiance(_RU_MapAmbient.rgb)*output.Occlusion;
radiance+=RU_StandardDirect(diffuse,specular,output.Smoothness,n,view,RU_SafeNormalize(_RU_MapSunDirection.xyz),RU_GraphRadiance(_RU_MapSunColor.rgb)*shadow);
radiance+=RU_StandardDirect(diffuse,specular,output.Smoothness,n,view,RU_SafeNormalize(_RU_MapMoonDirection.xyz),RU_GraphRadiance(_RU_MapMoonColor.rgb));
""";
        return basis + "output.Normal=n;\nfloat3 radiance=output.Albedo*RU_GraphRadiance(_RU_MapAmbient.rgb);\n"
            + "_LightColor0=float4(RU_GraphRadiance(_RU_MapSunColor.rgb),1);\n"
            + "if(any(_RU_MapSunColor.rgb)) radiance+=Lighting" + model + "(output,RU_SafeNormalize(_RU_MapSunDirection.xyz),shadow).rgb;\n"
            + "_LightColor0=float4(RU_GraphRadiance(_RU_MapMoonColor.rgb),1);\n"
            + "if(any(_RU_MapMoonColor.rgb)) radiance+=Lighting" + model + "(output,RU_SafeNormalize(_RU_MapMoonDirection.xyz),1).rgb;\n";
    }
}
