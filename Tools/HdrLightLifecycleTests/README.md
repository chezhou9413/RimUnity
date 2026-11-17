# HDR light lifecycle regressions

Links the production `HdrMapHooks`, `HdrLightFrame`, `HdrLightBlend` and
`HdrLightTarget` sources. A recording host/Vulkan boundary checks scene-pass
selection, binding invalidation and framebuffer lifetime without a game install.
It rejects reopening a clear attachment without clear values, stale host bindings
after another renderer takes over, destruction of a referenced framebuffer, and
reset/disposal of descriptor pools still referenced by recorded commands.

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
Depth regressions reproduce the host's stale `Undefined` allocation metadata after
its scene pass. They reject destructive/invalid depth barriers across light-first,
shell-first and repeated slices, and check the production render pass's depth and
stencil load/store/layout contract.
The single-sample model checks ordering, clearing and depth/stencil visibility,
not color-space correctness or real GPU execution.

The post-effect boundary models the host's `RecordInline` pool reset. Regressions
cover separate descriptors for interleaved light runs and slices, independent
in-flight slots, reuse after slot retirement, and effect disposal/recreation on
target rebuild. Depth-writing and stencil-writing additive materials must still
enter Experiment with their original depth/stencil state. The recording boundary
uses adapted draw copies and the framebuffer's shared depth/stencil attachment:
light depth writes must occlude later shell and light fragments, and stencil pass,
fail and depth-fail writes must affect later tests across runs and slices, even
when the writing fragment produces no color. Native and Experiment sequences are
compared against expected visibility; read-only and alpha-weighted additive blends
remain eligible.

Also links the HDR diagnostic formatter and bounded text recorder. Checks inactive
and stopped logging, concurrent recording, baseline retention, explicit truncation,
capture/run/target details, native fallback visibility and unchanged draw results
with tracing enabled. The game debug-menu/export integration needs a game build.
Simulated 140 fps particle-heavy captures verify that stage sampling retains the
full 20-second span, reports suppressed events and keeps errors unsampled.

This complements `Tools/SceneColorTests`: it does not replace real Vulkan pixel
tests, validation layers, depth/stencil pixel tests or in-game night-map verification.
