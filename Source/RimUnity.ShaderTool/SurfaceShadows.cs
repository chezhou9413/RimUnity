namespace RimUnity.ShaderTool;

//方向光阴影使用反向深度比较，软阴影执行九点 PCF。
internal static class SurfaceShadows
{
    internal const string Program = """
sampler2D _RU_ShadowMap;
float4x4 _RU_ShadowVP;
float4 _RU_ShadowParams, _RU_ShadowTexel;
float _RU_DisableShadows;
float RU_SurfaceShadow(float3 world, float3 normal) {
    if (_RU_ShadowParams.x == 0 || _RU_DisableShadows != 0) return 1;
    float4 clip = mul(_RU_ShadowVP, float4(world + normal * _RU_ShadowTexel.z,1));
    float3 p = clip.xyz / clip.w;
    if (any(abs(p.xy)>1) || p.z<0 || p.z>1) return 1;
    float2 uv = float2(p.x*.5+.5, .5-p.y*.5);
    float visible = 0;
    for (int y=-1; y<=1; y++) for (int x=-1; x<=1; x++) {
        float depth = tex2D(_RU_ShadowMap,uv+float2(x,y)*_RU_ShadowTexel.xy*_RU_ShadowParams.z).r;
        visible += p.z + _RU_ShadowParams.w >= depth ? 1 : 0;
    }
    return lerp(1,visible/9,_RU_ShadowParams.y);
}
""";
}
