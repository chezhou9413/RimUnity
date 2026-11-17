# HDR light lifecycle regressions

Links the production `HdrMapHooks`, `HdrLightFrame`, `HdrLightBlend` and
`HdrLightTarget` sources. A recording host/Vulkan boundary checks scene-pass
selection, binding invalidation and framebuffer lifetime without a game install.
It rejects reopening a clear attachment without clear values, stale host bindings
after another renderer takes over, and destruction of a referenced framebuffer.

```powershell
dotnet run --project Tools/HdrLightLifecycleTests -c Release
```

For a machine with only .NET 9, run the following from outside this repository
(whose `global.json` selects .NET 10), substituting the checkout path:

```powershell
dotnet build C:/RimUnity/Tools/HdrLightLifecycleTests -c Release -p:TestTargetFramework=net9.0
dotnet C:/RimUnity/Tools/HdrLightLifecycleTests/bin/Release/net9.0/HdrLightLifecycleTests.dll
```

Cases cover a distant zero-contribution tracer that must not darken existing light;
multiple light runs interleaved with night/fog multiplication, alpha transparency
and opaque layers; same-queue ordering and persistent-before-dynamic equal keys;
light-first slices; a base scene followed by additive light and one night overlay;
an empty post-light tail followed by a native-only slice; two light slices in
one command buffer; color-view changes and frame-slot isolation; target rebuild;
fallback when the host has no load attachment; and cleanup after a draw failure.
The scalar scene values only detect ordering/clearing, not color-space correctness.

This complements `Tools/SceneColorTests`: it does not replace real Vulkan pixel
tests, validation layers, depth/stencil tests or in-game night-map verification.
