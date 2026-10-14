//对应 Unity 内置 Particle Premultiply Blend 的颜色表达式与渲染状态。
Shader "Hidden/RimUnity/LegacyParticlePremultiply"
{
    Properties { _MainTex ("粒子纹理", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One OneMinusSrcAlpha
        ColorMask RGB
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            struct Input { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert(Input v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex); o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex); return o;
            }
            float4 frag(Varyings i) : SV_Target { return i.color * tex2D(_MainTex, i.uv) * i.color.a; }
            ENDCG
        }
    }
}
