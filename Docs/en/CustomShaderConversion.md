# Exporting custom shaders

[简体中文](../zh-CN/CustomShaderConversion.md) | [English](CustomShaderConversion.md)

## Export from Unity

1. Configure the RimWorld installation and `RimUnity.ShaderTool.exe` paths in the RimUnity export window.
2. Use the material on a prefab, or export it as a standalone asset.
3. Use the default configuration for map materials. Set `ExportRequest.ui = true` for standalone materials intended for IMGUI.
4. Distribute the catalog and all shader, material and texture dependencies with your content mod.

The exporter collects material parameters, textures and render states automatically. There is no need to write a ShaderDef for each material. Custom shaders require readable source files in the project.

## Supported shader authoring

Use explicit vertex/fragment programs for UV animation, masks, dissolves, vertex colors, time-based animation and texture sampling.

- Properties: Float, Range, Int/Integer, Color, Vector and textures.
- States: Blend, BlendOp, Cull, ZTest, ZWrite, ZClip, AlphaToMask, ColorMask, Offset, Stencil and Tags.
- Keywords: regular, local and fragment groups in shader_feature/multi_compile, with one selected keyword per group.
- Common transforms: UnityObjectToClipPos, TransformObjectToHClip, TransformObjectToWorld, TransformWorldToHClip and TRANSFORM_TEX.
- UI: UnityGet2DClipping.
- Sampling: tex2D/lod/bias/proj/grad and URP TEXTURE2D, SAMPLER, SAMPLE_TEXTURE2D/LOD/BIAS.

Relative includes, UnityCG, UnityShaderVariables, UnityUI and selected URP Core/SpaceTransforms interfaces are supported. The complete Unity render pipeline libraries are not.

World materials can export supported multiple color passes. Unlit Surface support is limited to forms accepted by the converter. Custom multipass UI, Shader Graph, GrabPass, scene-depth soft particles and the complete lighting/shadow pipeline are unsupported. Other unsupported syntax, macros or layouts are reported with their locations during export.

UI vertices use screen coordinates and have no object model matrix. For world shaders, pass object- or camera-dependent results needed by the fragment stage through vertex varyings.

## Working with parameters

Use the original material property names in scripts, such as `_MainTex` and `_Color`. Set texture transforms with `SetTextureScale` and `SetTextureOffset`, or write `(scale.x, scale.y, offset.x, offset.y)` to `_MainTex_ST`.

Time-based animation can use `_Time`, `_SinTime`, `_CosTime` and `unity_DeltaTime`. See [color and Bloom](ColorAndBloom.md) for color handling.

## Command line

To convert a shader file separately:

```powershell
& .\Tools\bin\ShaderTool\RimUnity.ShaderTool.exe '<RimWorldDirectory>' shaderlab '<Source.shader>' '<OutputDirectory>' world 'KEYWORD_A,KEYWORD_B'
```

Replace `world` with `ui` for interface programs. Output includes `shader.json`, `shader.vert.glsl`, `shader.frag.glsl` and `shader.hlsl` for diagnostics. Use the Unity exporter to generate a complete catalog for game assets.

## Export errors

Use diagnostics to check shader source, include paths, keyword combinations, material states and the target game directory. Conversion failure does not publish a successful catalog for that transaction. Do not replace a failed asset with a material whose shader is missing.
