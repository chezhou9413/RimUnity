# Particle modules and limitations

[简体中文](../zh-CN/ParticleExpansion.md) | [English](ParticleExpansion.md)

RimUnity provides its own particle simulation and simplified physics. It is not a complete implementation of Unity or PhysX.

## Supported modules

- **3D particle collision:** planes, spheres, boxes, capsules, static non-convex meshes and dynamic convex meshes; swept radius, bounce, damping, lifetime loss, speed thresholds, collision messages, collision sub-emitters and impulses applied to rigidbodies.
- **2D particle collision:** circles, boxes, capsules, polygons and open polylines. 3D and 2D queries are isolated. Triggers support Enter/Exit/Inside/Outside, Kill/Callback, querying colliding objects and writing particles back.
- **Simple rigidbodies:** mass, gravity, velocity, drag, forces and impulses, constraints, static/kinematic objects, basic contact friction and restitution. This does not reproduce complete PhysX or Box2D behavior.
- **Particle Lights:** Point light templates supply color, intensity, range, ratio and count to the map GlowGrid, illuminating map cells. The refresh rate is 10 Hz and maximum range is 40 cells.
- **External Forces:** ForceField, WindZone, explicit references/layer filtering and readable 3D floating-point vector textures; Noise Remap and quality settings. Random fields and noise algorithms do not guarantee identical per-particle results to Unity.
- **Shape:** texture color/alpha/clipping, mesh emission modes, MeshRenderer/SpriteRenderer/SkinnedMeshRenderer. Skinned emission positions are sampled from the current bone pose.
- **Lifetime:** Lifetime By Emitter Speed, Flip Rotation, Stop Callback, manual sub-emitters and Ring Buffer. Keeping naturally expired particles does not suppress Collision/Trigger Kill behavior.
- **Rendering:** multiple particle meshes with random weights, MeshIndex texture-sheet rows, UV3/UV4/VertexID/ParticleIndex/MeshIndex vertex streams. Sprite frames spanning multiple textures are split into consecutive batches while preserving transparent ordering.
- **Trails:** Ribbon, separate ribbon counts and splitting by sub-emitter.
- **Built-in Standard Unlit particles:** common blend modes, color blending, texture-sheet frame blending, emission and Camera Fading. Subtractive blending is handled by RimUnity's material pipeline.

Resources are still exported as prefab/material/dependency JSON and GLSL. Content authors do not need to write individual ShaderDefs. The framework includes ShaderDefs for built-in mappings; custom shaders are registered through the catalog.

## Limitations

- Lights illuminate map cells; they do not provide per-pixel 3D point lighting, real-time shadows or a lit material pipeline. Spot, Directional, Cookie and shadow templates are rejected explicitly.
- Collisions operate on imported or script-created RimUnity colliders only. RimWorld walls, terrain cells and pawns do not automatically become Colliders, and native Pawns/Things are not pushed by this physics system.
- Rigidbodies use discrete substeps and single-contact solving. Joints, CCD, interpolation and fully stable stacking are unsupported. Medium/Low particle collision uses the high-precision query path rather than Unity's voxel cache optimization.
- Unity's global layer collision matrix is not imported; the particle module's `collidesWith` mask is applied. 2D automatic mass, Effectors, Composite colliders and Edge virtual adjacent endpoints are unsupported. Rigidbody contact involving polygons with holes has limitations.
- TerrainCollider uses actual heights/holes and a fixed triangulation, which can produce different contacts from Unity's native terrain triangulation. It is not a closed trigger volume.
- Shader Graph, shadow/lighting passes, scene-depth soft particles and refraction are unsupported. See [shader export](CustomShaderConversion.md) for custom program requirements.
- Blending between Sprite frames on different textures and packed, rotated SpriteAtlases have limitations. TrailRenderer and Legacy Animation are supported, but complete independent trail vertex streams, renderer parameters and NativeArray/Job/PlaybackState APIs are not.
- Custom streams remain limited to TEXCOORD0–2, totaling 12 floats. GPU Instancing and native particle Jobs are unsupported.

## Usage

Load and instantiate particles from your content mod as shown in [the particle quick start](ParticleQuickStart.md).
