Shader "Hidden/RimUnity/StandardSurface"
{
    Properties
    {
        _MainTex ("颜色", 2D) = "white" {}
        _Color ("颜色", Color) = (1,1,1,1)
        _MetallicGlossMap ("金属度与光滑度", 2D) = "white" {}
        _Metallic ("金属度", Range(0,1)) = 0
        _Glossiness ("光滑度", Range(0,1)) = 0.5
        _BumpMap ("法线", 2D) = "bump" {}
        _BumpScale ("法线强度", Float) = 1
        _EmissionMap ("自发光", 2D) = "white" {}
        _EmissionColor ("自发光颜色", Color) = (0,0,0,0)
        _Cutoff ("裁切", Float) = 0.5
        _SrcBlend ("源混合", Float) = 1
        _DstBlend ("目标混合", Float) = 0
        _ZWrite ("深度写入", Float) = 1
        _RimUnityParticle ("粒子颜色", Float) = 0
        _FlipbookMode ("序列帧插值", Float) = 0
    }
    SubShader
    {
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard keepalpha vertex:ParticleVertex
        #pragma shader_feature _ALPHATEST_ON _ALPHABLEND_ON
        #pragma shader_feature _NORMALMAP
        #pragma shader_feature _METALLICGLOSSMAP
        #pragma shader_feature _EMISSION
        #pragma shader_feature _REQUIRE_UV2
        #pragma shader_feature _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature _GLOSSYREFLECTIONS_OFF
        sampler2D _MainTex, _MetallicGlossMap, _BumpMap, _EmissionMap;
        float4 _Color, _EmissionColor;
        float _Metallic, _Glossiness, _BumpScale, _Cutoff, _RimUnityParticle, _FlipbookMode;
        float4 _MainTex_ST;
        struct Input { float4 uv_MainTex; float4 color : COLOR; float frameBlend; };
        //粒子 UV2 和 AnimBlend 分别由 TEXCOORD0.zw 与 TEXCOORD1.x 提供。
        void ParticleVertex(inout appdata_full v, out Input o)
        {
            o = (Input)0;
            o.frameBlend = saturate(v.texcoord1.x) * _RimUnityParticle * _FlipbookMode;
        }
        #define SampleFrame(source, IN) ((IN.frameBlend == 0) ? tex2D(source, IN.uv_MainTex.xy) : lerp(tex2D(source, IN.uv_MainTex.xy), tex2D(source, IN.uv_MainTex.zw * _MainTex_ST.xy + _MainTex_ST.zw), IN.frameBlend))
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float4 albedo = SampleFrame(_MainTex, IN) * _Color * lerp(float4(1,1,1,1), IN.color, _RimUnityParticle);
            o.Albedo = albedo.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            #ifdef _METALLICGLOSSMAP
            float4 metal = SampleFrame(_MetallicGlossMap, IN);
            o.Metallic = metal.r;
            o.Smoothness *= metal.a;
            #endif
            #ifdef _NORMALMAP
            o.Normal = UnpackNormal(SampleFrame(_BumpMap, IN));
            o.Normal.xy *= _BumpScale;
            o.Normal.z = sqrt(1-saturate(dot(o.Normal.xy,o.Normal.xy)));
            #endif
            #ifdef _EMISSION
            o.Emission = SampleFrame(_EmissionMap, IN).rgb * _EmissionColor.rgb;
            #endif
            o.Alpha = 1;
            #ifdef _ALPHABLEND_ON
            o.Alpha = albedo.a;
            #endif
            #ifdef _ALPHATEST_ON
            clip(albedo.a - _Cutoff);
            #endif
        }
        ENDCG
    }
}
