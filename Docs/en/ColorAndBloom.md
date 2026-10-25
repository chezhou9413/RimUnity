# Color, transparent blending and Bloom

[简体中文](../zh-CN/ColorAndBloom.md) | [English](ColorAndBloom.md)

## Color and materials

The exporter reads the Unity project's Gamma/Linear setting and preserves material colors, alpha, textures, blend modes and render states. Mark data textures, such as noise and masks, as Linear. Set color textures to sRGB according to their purpose.

Changing a texture through `Material.SetTexture` also updates its color encoding. When using MaterialPropertyBlock to substitute a texture with a different encoding, also set `_RimUnitySrgb<TexturePropertyName>`: use 1 for sRGB and 0 for data textures.

When calling `SetVector` on a color property, continue to provide values in the exported color convention. This call does not bypass color-space conversion.

Rendering → Enable map HDR rendering is off by default, including when loading settings without this key. While off, maps use native rendering and selected layers can still produce Bloom through a separate floating-point light buffer. When enabled, map materials exported from Linear projects use linear HDR blending, preserving brightness above 1 until final composition. Changes apply at the next map-frame capture; no restart or resource re-export is needed. Map HDR and Bloom are independent, and explicitly enabled offscreen previews are unaffected by this map-only setting.

Map rendering retains material queues, depth tests and stencil tests. When draws are interleaved with native Gamma materials, the runtime converts color spaces between consecutive drawing segments. This adds floating-point buffer memory and full-screen drawing overhead, depending on resolution and draw ordering.

## Enable Bloom

Open **Options → Mod settings → RimUnity**, enable Bloom, select participating layers (0–31), then adjust threshold, intensity, softness and spread.

Bloom is disabled by default with no layers selected. The default threshold is 0.8 and intensity is 0.6. Objects on unselected layers do not become Bloom sources, although halos can spread onto neighboring pixels. Excessive intensity can wash out the image. Normal terrain often uses layer 0, so selecting it is discouraged. The Bloom switch does not control material color or alpha.

## Assign layers

In Unity, assign a dedicated layer to effects and all child nodes that should contribute to Bloom, then export normally. At runtime, set `gameObject.layer`; changing a root does not update its children automatically.

Layers are matched by number, not name. Coordinate numbers between Unity projects and content mods.

## Connect your content mod's settings

Call on the game main thread at map initialization and whenever settings change:

```csharp
RimUnity.RimUnityMod.Bloom.SetLayerEnabled(29, myBloomEnabled);
```

29 is an example. This lets the content mod control the corresponding layer, overriding RimUnity's manual selection. When the mod disables that layer, manually selecting it does not re-enable it.

Release control when the game lifecycle ends:

```csharp
RimUnity.RimUnityMod.Bloom.ReleaseLayer(29);
```

Only one content mod should control a given layer. Threshold, intensity and other parameters use the shared RimUnity settings. Layer control is not persisted in RimUnity's configuration; the content mod must synchronize its saved value when loading a save or starting a game.

## Limitations

- Bloom covers particles, trails and models in the main map rendering channel. UI, the world map and offscreen cameras are outside this scope.
- The complete Unity Camera/Volume post-processing system, exposure, tone mapping and lens dirt are unsupported.
- Linear HDR blending applies to map shaders marked as RimUnity Linear. UI, the world map and offscreen cameras retain their respective output paths.
- Intermediate scene captures remain limited by the game's scene texture precision and display range. Final screen output also has a brightness limit. Without Unity's exposure and tone mapping, results are not guaranteed to match a Unity camera using those effects.
- Bloom respects map depth occlusion and material depth tests. Materials relying on intermediate stencil writes or special multipass ordering may look different.
