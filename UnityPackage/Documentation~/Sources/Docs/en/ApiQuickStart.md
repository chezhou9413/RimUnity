# API quick start

[简体中文](../zh-CN/ApiQuickStart.md) | [English](ApiQuickStart.md)

This guide is for content mod authors. Use native Unity components for authoring; RimUnity runs the supported functionality in the game. See [the workflow guide](Workflow.md) for asset preparation and builds, and [asset loading](ResourceLoading.md) for path conventions.

For lifecycle, hierarchy, component and coroutine examples, see [MonoBehaviour and common methods](MonoBehaviour.md).

## 1. Declare dependencies and reference assemblies

Add these entries to the existing sections in your content mod's `About/About.xml`:

```xml
<modDependencies>
  <li><packageId>Chezhou.RimUnity</packageId><displayName>RimUnity</displayName></li>
</modDependencies>
<loadAfter><li>Chezhou.RimUnity</li></loadAfter>
```

Do not create duplicate `modDependencies` or `loadAfter` sections. Harmony, which RimUnity depends on, must also be enabled.

Use a .NET 10 SDK project, reference assemblies from the target game installation, and set `Private="false"`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RimWorldDir>C:/Games/RimWorld</RimWorldDir>
    <RimUnityDir>C:/Mods/RimUnity</RimUnityDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp" HintPath="$(RimWorldDir)/Assembly-CSharp.dll" Private="false" />
    <Reference Include="UnityStubs" HintPath="$(RimWorldDir)/UnityStubs.dll" Private="false" />
    <Reference Include="RimUnity.Core" HintPath="$(RimUnityDir)/1.7/Assemblies/RimUnity.Core.dll" Private="false" />
    <Reference Include="RimUnity" HintPath="$(RimUnityDir)/1.7/Assemblies/RimUnity.dll" Private="false" />
  </ItemGroup>
</Project>
```

Add Waylith or other references as required by the APIs you use. Distribute your own DLLs only; do not copy referenced game or framework assemblies into your mod.

## 2. Instantiate and play a particle effect

This example assumes that a prefab with a ParticleSystem on its root has been exported as `Effects/BasicBurst`. The caller supplies the owning `ModContentPack` and a map position.

```csharp
using System;
using RimUnity.Assets;
using RimUnity;
using Verse;
using ParticleSystem = RimUnity.ParticleSystem;

public sealed class BurstEffect : IDisposable
{
    public GameObject Instance { get; }
    private readonly ParticleSystem particles;

    //Create on the main thread after a map is loaded, using the actual asset owner.
    public BurstEffect(ModContentPack contentMod, Vector3 position)
    {
        GameObject template = PrefabLoader.Load(contentMod, "Effects/BasicBurst");
        Instance = RimUnity.Object.Instantiate(template);
        Instance.transform.position = position;
        particles = Instance.GetComponent<ParticleSystem>();
        if (particles == null)
        {
            RimUnity.Object.Destroy(Instance);
            throw new InvalidOperationException("BasicBurst requires a ParticleSystem on its root.");
        }
        Instance.SetActive(true);
        particles.Play();
    }

    //Stop emission and let existing particles finish.
    public void Stop() => particles.Stop();

    //Call when the effect ends, its owner is destroyed, or the map is closed.
    public void Dispose() => RimUnity.Object.Destroy(Instance);
}
```

`Load` returns a cached template. Work with instances; do not activate, modify or destroy the template. `Instantiate` restores the exported active state. If a script needs custom initialization before `Awake`, author an inactive root and provide an explicit initialization method. Assignments after instantiation are not guaranteed to precede `Awake`.

`Play(true)` includes children and associated sub-emitters by default. Do not blindly call `Play` on every child particle system: this can start an event-driven sub-emitter independently. Control separate emission roots explicitly in your script.

## 3. Common types and controls

| Authoring type | Game-side type or namespace |
|---|---|
| GameObject, Transform, MonoBehaviour, Vector3, Quaternion, Color | Corresponding types in `RimUnity` |
| Mesh, Material, Texture2D, Shader, ComputeBuffer | Corresponding types in `RimUnity` |
| UnityEvent and parameterized events | `RimUnity.Events` |
| ParticleSystem, ParticleSystemRenderer | `RimUnity.ParticleSystem`, `RimUnity.ParticleSystemRenderer` |
| Animator, Animation, LineRenderer, TrailRenderer | Corresponding types in `RimUnity` |
| RectTransform, Canvas, CanvasGroup | Corresponding types in `RimUnity` |
| Image, Button, Slider, Text and other controls | `RimUnity.UI` |
| Pointer event interfaces | `RimUnity.EventSystems` |

Use `using RimUnity;` in game-side scripts. If you also use the game's `UnityEngine` types, declare explicit aliases. GameObject, Component and Material objects from the two systems are not interchangeable. Convert positions returned by Verse at the boundary: `var p = pawn.DrawPos; instance.transform.position = new RimUnity.Vector3(p.x, p.y, p.z);`.

Particles provide `Play`, `Pause`, `Stop`, `Clear`, `Simulate`, `IsAlive`, `GetParticles` and `SetParticles`. `SetActive(false)` deactivates the whole object and has different lifecycle semantics from particle `Pause`. Use `RimUnity.Object.Destroy(instance)` for final disposal. Reuse arrays in frequently called data paths to avoid per-frame allocations.

## 4. Custom scripts and events

Exported fields and event targets can resolve directly when authoring and runtime scripts share a fully qualified type name. The game-side assembly must already be loaded. If a type was renamed, register a mapping once, before the first asset load:

```csharp
RimUnity.Assets.TypeResolver.Register("Authoring.EffectDriver", typeof(MyMod.EffectDriver));
```

Registration adds a mapping; it does not replace one. Registering the same name twice throws an error. Fields, resource references and UnityEvent bindings must use supported types; see [types and scripts](Usage.md). Third-party Unity DLLs are not automatically converted into Waylith components.

## 5. Control Bloom from your mod's settings

In Unity, assign a layer, such as 29, to the effect and every child that should contribute to Bloom. Layer numbers are exported with the prefab. Changing the root's layer does not update children automatically.

```csharp
//Synchronize your mod's setting on the main thread at map initialization and on changes.
RimUnity.RimUnityMod.Bloom.SetLayerEnabled(29, myBloomEnabled);

//Release the mod's control when the game lifecycle ends.
RimUnity.RimUnityMod.Bloom.ReleaseLayer(29);
```

29 is an example, not a reserved framework layer. Each layer should be controlled by only one content mod. Its setting takes precedence over RimUnity's manual layer selection; intensity, threshold and other parameters remain shared. Turning Bloom off does not stop particle playback. See [color and Bloom](ColorAndBloom.md) for details.

Load assets, instantiate objects, access components and connect settings on the game main thread, not in background Tasks. Cloning live instances or subtrees and runtime asset hot reload are not supported.
