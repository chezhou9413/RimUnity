namespace RimUnity.ShaderTool;

// Standard BRDF1 with native map illumination; emission is evaluated once.
internal static class GraphLighting
{
    internal const string Program = """
float4 _RU_MapSunDirection, _RU_MapMoonDirection;
float4 _RU_MapSunColor, _RU_MapMoonColor, _RU_MapAmbient;
float4 RU_GraphLinearToSRGB(float4 c) { return float4(LinearToGammaSpace(c.rgb), c.a); }
float3 RU_SafeNormalize(float3 p) { return p * rsqrt(max(dot(p,p), 0.001)); }
float RU_Pow5(float x) { float x2=x*x; return x2*x2*x; }
float3 RU_GraphRadiance(float3 color) {
#ifdef UNITY_COLORSPACE_GAMMA
return color;
#else
return GammaToLinearSpace(color);
#endif
}
float3 RU_StandardDirect(float3 diffuse, float3 specular, float smoothness, float3 n, float3 v, float3 l, float3 radiance) {
    float3 h=RU_SafeNormalize(l+v);
    float nv=abs(dot(n,v)), nl=saturate(dot(n,l)), nh=saturate(dot(n,h)), lh=saturate(dot(l,h));
    float perceptualRoughness=1-smoothness, roughness=max(perceptualRoughness*perceptualRoughness,0.002);
    float fd90=0.5+2*lh*lh*perceptualRoughness;
    float diffuseTerm=(1+(fd90-1)*RU_Pow5(1-nl))*(1+(fd90-1)*RU_Pow5(1-nv))*nl;
    float lambdaV=nl*(nv*(1-roughness)+roughness), lambdaL=nv*(nl*(1-roughness)+roughness);
    float visibility=0.5/(lambdaV+lambdaL+1e-5);
    float a2=roughness*roughness, d=(nh*a2-nh)*nh+1;
    float specularTerm=visibility*a2/(d*d+1e-7);
#ifdef UNITY_COLORSPACE_GAMMA
    specularTerm=sqrt(max(1e-4,specularTerm));
#endif
    specularTerm=max(0,specularTerm*nl)*(any(specular)?1:0);
    float3 fresnel=specular+(1-specular)*RU_Pow5(1-lh);
    return (diffuse*diffuseTerm+specularTerm*fresnel)*radiance;
}
struct RU_GraphAttributes { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float4 uv0:TEXCOORD0; float4 uv1:TEXCOORD1; };
struct RU_GraphVaryings { float4 position:SV_POSITION; float3 normalWS:TEXCOORD0; float4 tangentWS:TEXCOORD1; float3 positionWS:TEXCOORD2; float4 uv0:TEXCOORD3; float4 uv1:TEXCOORD4; };
RU_GraphVaryings RU_GraphVert(RU_GraphAttributes v) {
    VertexDescriptionInputs input=(VertexDescriptionInputs)0;
    input.ObjectSpacePosition=v.vertex.xyz; input.ObjectSpaceNormal=v.normal; input.ObjectSpaceTangent=v.tangent.xyz;
    VertexDescription graph=VertexDescriptionFunction(input);
    RU_GraphVaryings o;
    o.position=UnityObjectToClipPos(graph.Position);
    o.positionWS=TransformObjectToWorld(graph.Position);
    o.normalWS=UnityObjectToWorldNormal(graph.Normal);
    o.tangentWS=float4(UnityObjectToWorldDir(graph.Tangent), v.tangent.w * sign(determinant((float3x3)unity_ObjectToWorld)));
    o.uv0=v.uv0; o.uv1=v.uv1; return o;
}
float4 RU_GraphFrag(RU_GraphVaryings i):SV_Target {
    float3 n=normalize(i.normalWS), v=normalize(_WorldSpaceCameraPos-i.positionWS);
    SurfaceDescriptionInputs input=(SurfaceDescriptionInputs)0;
    input.WorldSpaceNormal=n; input.TangentSpaceNormal=float3(0,0,1); input.WorldSpaceViewDirection=v; input.uv0=i.uv0; input.uv1=i.uv1;
    SurfaceDescription surface=SurfaceDescriptionFunction(input);
    float3 tangent=RU_SafeNormalize(i.tangentWS.xyz), bitangent=cross(n,tangent)*i.tangentWS.w;
    n=normalize(tangent*surface.NormalTS.x+bitangent*surface.NormalTS.y+n*surface.NormalTS.z);
#ifdef UNITY_COLORSPACE_GAMMA
    float dielectric=0.220916301;
#else
    float dielectric=0.04;
#endif
    float oneMinusReflectivity=(1-dielectric)*(1-surface.Metallic);
    float3 diffuse=surface.BaseColor*oneMinusReflectivity, specular=lerp(dielectric.xxx,surface.BaseColor,surface.Metallic);
    float alpha=surface.Alpha;
#ifdef _ALPHAPREMULTIPLY_ON
    diffuse*=alpha; alpha=1-oneMinusReflectivity+alpha*oneMinusReflectivity;
#endif
    float3 result=diffuse*RU_GraphRadiance(_RU_MapAmbient.rgb)*surface.Occlusion;
    result+=RU_StandardDirect(diffuse,specular,surface.Smoothness,n,v,RU_SafeNormalize(_RU_MapSunDirection.xyz),RU_GraphRadiance(_RU_MapSunColor.rgb));
    result+=RU_StandardDirect(diffuse,specular,surface.Smoothness,n,v,RU_SafeNormalize(_RU_MapMoonDirection.xyz),RU_GraphRadiance(_RU_MapMoonColor.rgb));
    return float4(result+surface.Emission,alpha);
}
""";
}
