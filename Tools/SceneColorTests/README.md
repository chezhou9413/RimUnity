# Scene color GPU regressions

Runs the installed host's Vulkan compiler and rendering passes in a hidden graphics
window. It does not start RimWorld gameplay, load mods or touch a save. `Headless`
is deliberately false because that option disables graphics entirely.

```powershell
dotnet run --project Tools/SceneColorTests/SceneColorTests.csproj -c Release `
  "-p:RimWorldDir=$env:RIMWORLD_DIR" -- `
  $env:RIMWORLD_DIR "$PWD/1.7/Shaders/RimUnity" "$PWD/artifacts/SceneColorTests"
```

Set `RIMWORLD_DIR` to the installed 1.7 game directory before running, or supply
the MSBuild property and first program argument explicitly.

The harness links production `HdrTransferPass` and `BloomPreviewContext`, uses the
host's original PostFx composite GLSL, and runs the current HDR composite, Bloom
prefilter/down/up and ScreenEfx scene GLSL. Each submitted offscreen command buffer
completes a dedicated fence before pixel readback or resource reuse.

Checks compare GPU pixels with an independent CPU sRGB/grade reference and with the
original host composite before and after an HDR roundtrip. Formats: RGBA8/BGRA8
UNorm, RGBA8 sRGB, packed 10-bit UNorm and RGBA16 float. It checks zero and nonzero
Bloom, native and linear ScreenEfx inputs, three saturation settings, and constant
energy through the Bloom pyramid. Quantization and half-float rounding are allowed.

Negative controls deliberately reproduce the old non-8-bit-UNorm classification
and require the observed extra encoding/brightening to occur. They demonstrate the
bad conversion independently of the research animation. This is pixel validation
on the current GPU, not proof of every GPU driver, complete game map, occlusion,
transparent draw ordering or real-game performance.

With the optional fourth argument pointing to a compiled `RimUnity.dll` and the
fifth to `0Harmony.dll`, the real-runtime tests also check the map HDR setting.
They submit actual Linear shader draws and verify that capture follows the
false/true/false sequence while independent Bloom layer selection stays intact.
New settings and Reset defaults must both leave map HDR disabled. Older supplied
runtime builds without this setting explicitly skip this regression.
