using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimUnity.ShaderTool;

//转换已支持的 HLSL 函数，未支持的光照、雾和蒙皮调用明确报错。
internal static class UnityHlslCompat
{
    //Windows 非实例化、无雾变体的 Unity 宏；未实现的光照函数保持编译错误。
    internal const string Macros = """
        #define UNITY_VERSION 202230
        #define SHADER_TARGET 30
        #define UNITY_NEAR_CLIP_VALUE 0.0
        #define UNITY_REVERSED_Z 1
        #ifdef UNITY_COLORSPACE_GAMMA
        #define unity_ColorSpaceGrey float4(0.5,0.5,0.5,0.5)
        #define unity_ColorSpaceDouble float4(2,2,2,2)
        #else
        #define unity_ColorSpaceGrey float4(0.214041144,0.214041144,0.214041144,0.5)
        #define unity_ColorSpaceDouble float4(4.59479380,4.59479380,4.59479380,2)
        #endif
        #define UNITY_FOG_COORDS(index)
        #define UNITY_TRANSFER_FOG(output,position)
        #define UNITY_APPLY_FOG(coord,color)
        #define UNITY_APPLY_FOG_COLOR(coord,color,fogColor)
        #define UNITY_VERTEX_INPUT_INSTANCE_ID
        #define UNITY_SETUP_INSTANCE_ID(input)
        #define UNITY_TRANSFER_INSTANCE_ID(input,output)
        #define UNITY_VERTEX_OUTPUT_STEREO
        #define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output)
        #define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
        #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0
        #define UNITY_DECLARE_DEPTH_TEXTURE(name) sampler2D name
        #define UNITY_DECLARE_SCREENSPACE_TEXTURE(name) sampler2D name;
        #define UNITY_SAMPLE_SCREENSPACE_TEXTURE(tex,uv) tex2D(tex,uv)
        #define UNITY_PROJ_COORD(value) value
        #define SAMPLE_DEPTH_TEXTURE(tex,uv) tex2D(tex,uv).r
        #define SAMPLE_DEPTH_TEXTURE_PROJ(tex,uv) tex2Dproj(tex,uv).r
        #define V2F_SHADOW_CASTER_NOPOS
        #define UnityWorldSpaceViewDir(position) (_WorldSpaceCameraPos - (position).xyz)
        #define WorldSpaceViewDir(position) (_WorldSpaceCameraPos - mul(unity_ObjectToWorld, (position)).xyz)
        """;
    //Unity 包包含由小型兼容头替代，本地自定义包含由转换入口先展开。
    internal static string Prepare(string source, string vertexEntry = null)
    {
        //全屏图像程序使用与普通网格一致的顶点输入和投影入口。
        if (Regex.IsMatch(source, @"\b(?:vert_img|v2f_img)\b") && !Regex.IsMatch(source, @"\bstruct\s+v2f_img\b"))
            source = "struct appdata_img { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };\n"
                + "struct v2f_img { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };\n"
                + "v2f_img vert_img(appdata_img v) { v2f_img o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }\n" + source;
        source = Regex.Replace(source, @"(?m)^\s*#include\s+[""<]([^"">]+)["">][^\r\n]*", match =>
        {
            string path = match.Groups[1].Value.Replace('\\', '/');
            if (path != "UnityCG.cginc" && path != "UnityShaderVariables.cginc" && path != "UnityUI.cginc"
                && path != "UnityStandardParticleInstancing.cginc" && path != "UnityStandardUtils.cginc" && path != "AutoLight.cginc" && path != "UnityLightingCommon.cginc"
                && path != "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
                && path != "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
                && path != "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl")
                throw new NotSupportedException("未适配的 Unity 包包含：" + path);
            return "";
        });
        source = Regex.Replace(source, @"\b(fixed|half)([1-4](?:x[1-4])?)?\b", "float$2");
        source = Regex.Replace(source, @"\bsampler2D_float\b", "sampler2D");
        if (Regex.IsMatch(source, @"\bUnityObjectToViewPos\s*\("))
            source = "float3 UnityObjectToViewPos(float3 p) { return mul(_RimUnity_View, mul(unity_ObjectToWorld, float4(p,1))).xyz; }\n" + source;
        if (Regex.IsMatch(source, @"\bLinearEyeDepth\s*\("))
            source = "float4 _ZBufferParams;\nfloat LinearEyeDepth(float z) { return unity_OrthoParams.w != 0 ? _ProjectionParams.z - z * (_ProjectionParams.z - _ProjectionParams.y) : 1.0 / (_ZBufferParams.z*z+_ZBufferParams.w); }\n" + source;
        source = Regex.Replace(source, @"\bCBUFFER_START\s*\(\s*UnityPerMaterial\s*\)|\bCBUFFER_END\b", "");
        source = Regex.Replace(source, @"\bTEXTURE2D\s*\(\s*(\w+)\s*\)\s*;", "Texture2D $1;");
        source = Regex.Replace(source, @"\bSAMPLER\s*\(\s*(\w+)\s*\)\s*;", "SamplerState $1;");
        source = Regex.Replace(source, @"\bCBUFFER_START\b", "RU_UNSUPPORTED_CBUFFER");
        source = Regex.Replace(source, @"\bUNITY_MATRIX_V\b", "_RimUnity_View");
        source = Regex.Replace(source, @"\bUNITY_MATRIX_P\b", "_RimUnity_Projection");
        //旧 Cg 允许条件表达式直接作为语句，显式括号避免 Slang 将比较号解析成泛型。
        //起始缩进不能跨行，也不能把多行赋值中问号前的空白当成条件。
        source = Regex.Replace(source, @"(?m)^([ \t]*)(?=\S)(?!(?:return|if|else|while|for|switch)\b)([^;{}\r\n?=]+)\?\s*([^;{}\r\n:]+)\s*:\s*([^;{}\r\n]+);", "$1(($2) ? ($3) : ($4));");
        if (vertexEntry == "vert_img" || Regex.IsMatch(source, @"\b(?:v2f_img|appdata_img|vert_img)\b"))
        {
            string image = "";
            if (!Regex.IsMatch(source, @"\bstruct\s+appdata_img\b")) image += "struct appdata_img { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };\n";
            if (!Regex.IsMatch(source, @"\bstruct\s+v2f_img\b")) image += "struct v2f_img { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };\n";
            if (!Regex.IsMatch(source, @"\bv2f_img\s+vert_img\s*\(")) image += "v2f_img vert_img(appdata_img v) { v2f_img o = (v2f_img)0; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }\n";
            source = image + source;
        }
        return source;
    }

    //两类绘制分别使用实际帧块；UI 顶点已转换成屏幕坐标，没有对象模型矩阵。
    internal static string Header(string profile)
    {
        StringBuilder text = new();
        if (profile == "world") text.AppendLine("""
            struct RU_CameraData { float4x4 ViewProj; };
            [[vk::binding(0, 0)]] ConstantBuffer<RU_CameraData> RU_Camera;
            struct RU_ObjectData { float4x4 model; };
            [[vk::push_constant]] ConstantBuffer<RU_ObjectData> RU_Object;
            #define unity_ObjectToWorld RU_Object.model
            #define UNITY_MATRIX_M RU_Object.model
            #define UNITY_MATRIX_VP RU_Camera.ViewProj
            #define UNITY_MATRIX_MVP mul(RU_Camera.ViewProj, RU_Object.model)
            float4 UnityObjectToClipPos(float4 p) { return mul(RU_Camera.ViewProj, mul(RU_Object.model, p)); }
            float4 UnityObjectToClipPos(float3 p) { return UnityObjectToClipPos(float4(p, 1)); }
            float3 TransformObjectToWorld(float3 p) { return mul(RU_Object.model, float4(p, 1)).xyz; }
            float4 TransformWorldToHClip(float3 p) { return mul(RU_Camera.ViewProj, float4(p, 1)); }
            float4 UnityWorldToClipPos(float3 p) { return TransformWorldToHClip(p); }
            float3 UnityObjectToWorldNormal(float3 normal) {
                float3x3 m = (float3x3)RU_Object.model;
                float3x3 cofactors = float3x3(cross(m[1],m[2]), cross(m[2],m[0]), cross(m[0],m[1]));
                return normalize(mul(cofactors, normal) * sign(dot(m[0],cofactors[0])));
            }
            float3 UnityObjectToWorldDir(float3 direction) { return normalize(mul((float3x3)RU_Object.model, direction)); }
            """);
        else text.AppendLine("""
            struct RU_FrameData { float4 sunDirection; float4x4 worldToCamera; float4x4 viewProj; };
            [[vk::binding(1, 0)]] ConstantBuffer<RU_FrameData> RU_Frame;
            #define UNITY_MATRIX_VP RU_Frame.viewProj
            #define UNITY_MATRIX_MVP RU_Frame.viewProj
            float4 UnityObjectToClipPos(float4 p) { return mul(RU_Frame.viewProj, p); }
            float4 UnityObjectToClipPos(float3 p) { return UnityObjectToClipPos(float4(p, 1)); }
            """);
        text.AppendLine("""
            float4 TransformObjectToHClip(float3 p) { return UnityObjectToClipPos(p); }
            float4 ComputeScreenPos(float4 p) { return float4(p.xy * 0.5 + p.w * 0.5, p.zw); }
            #define TRANSFORM_TEX(uv,name) ((uv).xy * name##_ST.xy + name##_ST.zw)
            #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0
            //非雾、非实例化、单眼变体在 Unity 中也不产生这些字段和调用。
            #define UNITY_FOG_COORDS(index)
            #define UNITY_TRANSFER_FOG(output,position)
            #define UNITY_APPLY_FOG(coord,color)
            #define UNITY_VERTEX_INPUT_INSTANCE_ID
            #define UNITY_SETUP_INSTANCE_ID(input)
            #define UNITY_TRANSFER_INSTANCE_ID(input,output)
            #define UNITY_VERTEX_OUTPUT_STEREO
            #define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output)
            #define SAMPLE_TEXTURE2D(tex,samp,uv) tex.Sample(samp,float2((uv).x,1-(uv).y))
            #define SAMPLE_TEXTURE2D_LOD(tex,samp,uv,lod) tex.SampleLevel(samp,float2((uv).x,1-(uv).y),lod)
            #define SAMPLE_TEXTURE2D_BIAS(tex,samp,uv,bias) tex.SampleBias(samp,float2((uv).x,1-(uv).y),bias)
            float4 ComputeGrabScreenPos(float4 p) { float4 r = p * 0.5; r.xy = float2(r.x, -r.y) + r.w; r.zw = p.zw; return r; }
            float3 UnpackNormal(float4 n) { n.x *= n.w; float2 xy = n.xy * 2 - 1; return float3(xy, sqrt(1 - saturate(dot(xy, xy)))); }
            float UnityGet2DClipping(float2 p, float4 r) { float2 v = step(r.xy,p)*step(p,r.zw); return v.x*v.y; }
            struct appdata_base { float4 vertex : POSITION; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; };
            struct appdata_tan { float4 vertex : POSITION; float4 tangent : TANGENT; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; };
            struct appdata_full { float4 vertex : POSITION; float4 tangent : TANGENT; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; float4 texcoord1 : TEXCOORD1; float4 texcoord2 : TEXCOORD2; float4 texcoord3 : TEXCOORD3; float4 color : COLOR; };
            """);
        return text.ToString();
    }

    //所有顶点与阶段间结构按语义分配相同位置，避免两个入口裁剪字段后位置漂移。
    internal static string Semantics(string source)
    {
        var semantics = new[] { ("POSITION", "Position", 0), ("TEXCOORD0", "TexCoord0", 1), ("COLOR", "Color", 2),
            ("NORMAL", "Normal", 3), ("TANGENT", "Tangent", 4), ("TEXCOORD1", "TexCoord1", 5),
            ("TEXCOORD2", "TexCoord2", 6), ("TEXCOORD3", "TexCoord3", 7), ("TEXCOORD4", "TexCoord4", 8),
            ("TEXCOORD5", "TexCoord5", 9), ("TEXCOORD6", "TexCoord6", 10), ("TEXCOORD7", "TexCoord7", 11),
            ("BLENDWEIGHT", "BlendWeight", 12), ("BLENDINDICES", "BlendIndices", 13) };
        StringBuilder annotations = new();
        foreach (var (semantic, metadata, location) in semantics)
        {
            string pattern = semantic is "POSITION" or "COLOR" or "NORMAL" or "TANGENT" or "BLENDWEIGHT" or "BLENDINDICES" ? semantic + "0?"
                : semantic == "TEXCOORD0" ? "TEXCOORD0?" : semantic;
            source = Regex.Replace(source, @"\b((?:float|int|uint)[1-4]?\s+\w+\s*:\s*(?:" + pattern + @")\s*;)",
                "[[vk::location(" + location + ")]] $1", RegexOptions.IgnoreCase);
            annotations.AppendLine("// @rimunity-vertex " + location + " " + metadata + " " + (location == 0 ? "required" : "optional"));
        }
        foreach (Match field in Regex.Matches(source, @"\b(?:float|int|uint|bool)[1-4]?\s+\w+\s*:\s*(\w+)\s*;"))
        {
            string semantic = field.Groups[1].Value.ToUpperInvariant();
            if (semantic == "SV_POSITION" || semantic == "SV_TARGET" || semantic == "SV_TARGET0") continue;
            if (!semantics.Any(s => semantic == s.Item1 || semantic == s.Item1 + "0") && semantic != "TEXCOORD")
                throw new NotSupportedException("未适配的 Shader 输入或输出语义：" + semantic);
        }
        return annotations + source;
    }
}


