# Clip screen effects (1.7)

`RimUnity.ScreenEfx` processes the composed map, including Bloom, before the game UI. A content mod subclasses `EfxClip`, submits it once, then changes its own properties. The player owns updates, ordering, snapshots, pass chaining and retirement. World view, menus and offscreen pawn previews do not use this chain.

## Minimal example

```csharp
using RimUnity;
using RimUnity.ScreenEfx;

public sealed class InterferenceClip : EfxClip
{
    private readonly Material material;
    private readonly MaterialPropertyBlock properties = new();
    public float Strength { get; set; }

    public InterferenceClip(Material template) => material = new Material(template);

    public override void BuildFrame(EfxFrameBuilder frame)
    {
        if (Strength <= 0) return;
        properties.SetFloat("_Strength", Mathf.Clamp01(Strength));
        frame.AddPass(material, pass: 0, properties: properties);
    }

    protected override void OnRelease() => RimUnity.Object.Destroy(material);
}

//Submit on the main thread after the game has initialized.
var clip = new InterferenceClip(template);
var handle = EfxPlayer.Play(clip);
clip.Strength = 0.6f; //Captured next frame; no resubmission.
clip.Enabled = false; //Hide while continuing Update.
clip.Enabled = true;
handle.Stop(); //Dispose is equivalent.
```

`AddPass` also accepts `UnityEngine.Material` and `UnityEngine.MaterialPropertyBlock`, including host materials obtained through Lumi. Pass indices must be valid. Multiple calls append sequential passes. Overrides support scalars, vectors, colors, matrices and textures; put arrays in the material. Override arrays and storage buffers are unsupported in this version.

## Playback and ownership

- Control methods and callbacks run on the game thread. `Update(EfxUpdateContext)` runs once per host update. `DeltaTime` and `Time` follow game pause/speed; `UnscaledDeltaTime` and `UnscaledTime` use real frame time. Loading long events suspend updates.
- Disabled clips still update, but do not build frames. Clips sort by ascending `Order`, then submission order. Changed priorities apply on the next capture.
- A dormant clip can opt out of callbacks with `UpdateEnabled=false`; business events must explicitly set it back to `true`. The default is `true`; hiding does not automatically suspend updates. Keep updates enabled if the clip must observe map changes every frame.
- `Play` requires a game and takes ownership for that session. An instance can only be submitted once, even after stopping. Stop/dispose is idempotent; returning to the menu or changing games stops the old session.
- There is no implicit fade. Implement interpolation in the clip. Content mods evaluate map visibility on the main thread and set `Enabled`.
- `OnRelease` runs once on the main thread after captured GPU slots have retired. Destroy private materials and owned textures there, rather than immediately after `Stop`. Shared templates remain loader-owned.
- Release can lag by several frames. Event subscriptions must still check their owning game so an old clip awaiting release cannot affect a new game. Released handles no longer retain the game or clip.
- Playback is transient. Rebuild it from your mod's saved state; retain the handle to avoid duplicate submissions during initialization.

Optionally override `Warmup(EfxFrameBuilder)` to submit the material passes you will use. The game thread captures this once per submitted instance during loading, including disabled or dormant clips. The existing loading warmup consumes only frozen data, presents no effect and advances no business animation. Use it to create materials, upload the shared triangle and compile GPU pipelines before gameplay. Clips submitted after loading still create resources on demand. Do not change gameplay state or advance fades in this callback.

The fullscreen triangle supplies Unity bottom-origin UVs to match the exporter's sampling flip; every pass preserves orientation, including multi-pass chains. Do not add another Y flip in the content shader.

While a warmed clip remains registered, hiding releases fullscreen images after retirement but retains small renderer/pipeline caches (about 0.93 MiB of buffers for two ordinary slots). The final stopped clip releases these caches. Loading warmup allocates no fullscreen images. First visibility and resize still incur image allocation costs; this does not guarantee a completely hitch-free first frame on every driver.

## Shader contract

Export shaders using the **world** profile, not GUI. Use `vert_img` or equivalent clip-space fullscreen vertex processing. Existing `OnRenderImage` and `Graphics.Blit` callbacks are not automatically supported.

`_MainTex` is the preceding pass output. `_MainTex_TexelSize` and `_ScreenParams` use actual target dimensions. Input/output use display-encoded color: the processor removes the exporter's final Linear output encoding and disables additional sRGB decoding of `_MainTex`. Other textures retain their material semantics.

`_Time` defaults to unscaled time and continues during pause. To pause animation, retain `context.Time` in Update and explicitly override `_Time` in the property block. Each `AddPass` captures material values and bindings immediately; later mutations cannot change queued frames.

Fullscreen processing disables depth, stencil, culling and blending. Shaders must sample `_MainTex` and write the complete result. Submit multiple passes explicitly. This version does not provide history buffers, scene depth, pre-Bloom processing or post-UI processing.

No effects means no additional fullscreen copy. Active effects reuse two targets per in-flight frame. Stops wait for safe slot retirement; resize uses the host's safe rebuild callback. A clip exception stops that clip with a diagnostic instead of retrying each frame; other effects and normal map output continue.

Frame containers, commands and parameter buffers are reused only after their final CPU/GPU reference is released. The frame pool retains at most eight containers, each with up to 64 commands and 256 KiB of parameter bytes; larger frames execute normally but are not cached. Render scratch retains up to 64 commands per in-flight slot and discards parameter caches exceeding 256 KiB on retirement. Blocks without late-bound dimensions borrow the immutable capture; other blocks use slot-private buffers.

Retirement clears clip/game, mesh and texture references. Session changes and shutdown clear the pool and reject late returns from the old session. Warm device-free fixtures allocate no managed memory for empty captures or reused input bindings. Host material capture and draw-frame wrappers can still allocate; this is not a zero-allocation claim for the entire render pipeline.

Each host in-flight slot owns a single-slot renderer. Upload capacities round up to the frame's requirements and grow only after that slot's GPU work retires. A small pass chain reserves 476 KiB per slot, about 0.93 MiB for two slots, compared with roughly 66.1 MiB for the original general-purpose renderer with two slots. These are buffer allocation requests, not resident VRAM measurements: upload buffers use a driver-selected host-visible heap. Descriptor, driver and shader costs are excluded. The two RGBA8 targets still require `2 × width × height × 4 × in-flight slots` bytes, about 31.6 MiB at 1080p with two slots. With no submitted clips these renderers and targets are never created; stopping waits for the final slot to retire before releasing them.

Capacity overrides apply only inside the synchronous construction scope of this framework's renderer. Other host renderers retain their configuration. Unsupported host constructors disable the effect while preserving map output. The diagnostic `uploadBufferBytes` sums current slot buffer reservations; it is an allocation budget, not measured resident VRAM.

## Validation

Run `dotnet run --project Tools/ScreenEfxTests -c Release` for playback and snapshot checks. Tests use local 1.7 references; override `RimWorldDir` and `HarmonyPath` as needed. These device-free tests do not establish GPU visual correctness.

In game, compare no clip, a neutral clip and two ordered clips, including frame time, Bloom on/off, pause, map changes, save/load, resize and exit. The existing render report's `ScreenEfx` stage records pass count, target count, dimensions and submission slot.
