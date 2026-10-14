# Supported features and limitations

[简体中文](../zh-CN/Status.md) | [English](Status.md)

Authoring: Unity 2022.3 LTS, Built-in or URP 14. Runtime: Windows x64, RimWorld 1.7 with Waylith, .NET 10.

## Supported features

| Area | Available functionality |
|---|---|
| Assets | Plain-text catalogs, prefab hierarchies, shared dependencies, template instantiation, script fields and event references |
| Scripts | RimUnity object hierarchy, Mono lifecycle, coroutines, Transform and math types |
| UI | Common UGUI controls, layouts, pointer events, legacy Text, TMP SDF atlases and basic clipping |
| Particles | Emission, shapes, motion modules, curves and gradients, texture sheets, sub-emitters, trails, custom data and force fields |
| Physics and lighting | Colliders, triggers, simplified rigidbodies and particle lighting for map cells; see [particle modules](ParticleExpansion.md) |
| Models and animation | Static and skinned meshes, basic Animator state machines, 1D blending, property animation, animation events, Animation, LineRenderer and TrailRenderer |
| Materials | Built-in material mappings, custom shader conversion, Gamma/Linear color handling and layer-filtered map Bloom |

## Limitations

- Custom scripts must use RimUnity types and be compiled into a game-side assembly. Third-party Unity DLLs cannot run unchanged.
- Instantiate from a root template returned by the loader. Cloning live instances or partial subtrees, cross-prefab object references and asset hot reload are unsupported.
- `SerializeReference` polymorphic object graphs and arbitrary serialization callbacks are unsupported.
- TMP does not provide complete dynamic glyph generation or advanced text layout. Basic Mask support does not cover arbitrary stencil operations.
- Particle random trajectories, scaling and some renderer parameters differ from Unity. Complete NativeArray/Jobs, GPU Instancing and PlaybackState APIs are unsupported.
- Physics is not equivalent to PhysX or Box2D. Game buildings and pawns do not automatically become colliders. Map Lights do not provide real-time 3D lighting or shadows.
- Animator does not support Humanoid, IK, complete nested state machines or complex transition interruption.
- Shader Graph, Live2D and the complete Unity post-processing stack are unsupported. See [custom shaders](CustomShaderConversion.md) for shader input requirements and [color and Bloom](ColorAndBloom.md) for Bloom's scope.
