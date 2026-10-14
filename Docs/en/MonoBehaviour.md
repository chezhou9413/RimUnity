# MonoBehaviour and common methods

[简体中文](../zh-CN/MonoBehaviour.md) | [English](MonoBehaviour.md)

Game-side scripts inherit from `RimUnity.MonoBehaviour` and use `RimUnity.GameObject`, Transform and math types. RimUnity manages object hierarchies, field and event binding, lifecycle messages and coroutines. Resources and rendering call Waylith directly. This guide covers common APIs; see [supported features](Status.md) for their scope.

For assembly references, export and loading entry points, see [the API quick start](ApiQuickStart.md), [workflow](Workflow.md) and [asset loading](ResourceLoading.md).

## 1. Sharing scripts between authoring and runtime

Author assets with native Unity components. Keep each script's fully qualified type name on the game side, use RimUnity types for its components, and compile it into a .NET 10 assembly.

For example, create this prefab in Unity, attach the script to `EffectRoot`, and assign the `Fire` particle component to its `fire` field:

```text
EffectRoot
└─ Effects
   ├─ Fire
   └─ Smoke
```

The following script can be maintained for both environments. `RIMWORLD_1_7` is your content mod's compilation symbol: add it to `DefineConstants` in the game-side project. RimUnity does not define it for you.

```csharp
#if RIMWORLD_1_7
using RimUnity;
#else
using UnityEngine;
#endif

namespace MyMod.Effects
{
    //Control the fire emitter assigned in the prefab.
    public sealed class FireController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem fire;

        //Report a missing required reference as an authoring error.
        private void Awake()
        {
            if (fire == null)
                throw new System.InvalidOperationException("FireController has no fire reference.");
        }

        //Called by a building, pawn state or animation event.
        public void PlayFire()
        {
            fire.gameObject.SetActive(true);
            fire.Play();
        }

        //Stop emission and let existing particles finish.
        public void StopFire() => fire.Stop();
    }
}
```

Fields are bound before script lifecycle messages run. GameObject, Transform and component references within a prefab resolve to objects in the corresponding instance. `public` and `[SerializeField]` fields can be exported. Properties are not serialized fields; `SerializeReference` polymorphic object graphs are unsupported.

If the fully qualified game-side type name differs, register a mapping once before loading assets:

```csharp
RimUnity.Assets.TypeResolver.Register(
    "Authoring.FireController", typeof(MyMod.Effects.FireController));
```

Registering the same name twice throws an error. Export saves script types and field data; it does not automatically convert C# source into game scripts.

## 2. Lifecycle and initialization order

The loader creates all nodes and components, restores fields, object references and events, then restores active states. Shared templates always remain inactive. RimUnity dispatches instance lifecycle messages; your gameplay code should not call them manually.

| Method | Common use |
|---|---|
| `Awake()` | Validate required references and cache nodes/components |
| `OnEnable()` | Subscribe to events and start each active display cycle |
| `Start()` | Initialize before the first update, rather than on every pool checkout |
| `Update()` | Update visuals each frame |
| `LateUpdate()` | Adjust following objects and attachment points after updates |
| `FixedUpdate()` | Fixed-step updates using `Time.fixedDeltaTime`, not game ticks |
| `OnDisable()` | Unsubscribe from events and stop coroutines owned by this script |
| `OnDestroy()` | Release resources created and owned by the component |

Instantiation restores the authoring-time active state. Messages such as `Awake` may run before `Instantiate` returns. Do not assume assignments made after instantiation run before `Awake`. If gameplay parameters must be supplied first, save the authoring root as inactive, provide an explicit `Initialize(...)` method, and activate it afterward. Dynamic `AddComponent` does not use prefab field restoration either; initialize it explicitly.

Do not depend on an unspecified execution order between scripts. The complete Unity message set, script execution order settings and a mapping between `FixedUpdate` and RimWorld ticks are not promised.

## 3. Finding hierarchy nodes and components

Instances retain node names, parent/child relationships and local transforms. Use the following inside a MonoBehaviour script, with `RimUnity.ParticleSystem` as the particle type:

```csharp
//Find a path relative to this script's node, including inactive children.
Transform node = transform.Find("Effects/Fire");
if (node == null)
    throw new System.InvalidOperationException("The prefab is missing Effects/Fire.");

var fire = node.GetComponent<RimUnity.ParticleSystem>();

//Search the subtree, including this node; true includes inactive objects.
var first = GetComponentInChildren<RimUnity.ParticleSystem>(true);
var all = GetComponentsInChildren<RimUnity.ParticleSystem>(true);

//Query an optional component.
if (node.TryGetComponent<RimUnity.ParticleSystem>(out var optional))
    optional.Pause();

//Iterate immediate children.
for (int i = 0; i < transform.childCount; i++)
{
    Transform child = transform.GetChild(i);
    child.localScale = Vector3.one;
}
```

`transform.Find("Fire")` matches an immediate child, not a recursive search at any depth. Use a path such as `"Effects/Fire"` for nested nodes. `GetComponent<T>()` searches only the current object; subtree queries exclude inactive objects by default. Handle missing nodes and components according to your asset's requirements before accessing them.

`RimUnity.GameObject.Find(name)` performs a global search. It cannot reliably distinguish instances with identical names. Prefer searching from the instance root you own, or use Inspector references. Cache frequently used references during initialization instead of searching the hierarchy every frame.

## 4. Transform, activation and dynamic components

| Member | Meaning |
|---|---|
| `transform.position` / `rotation` | World position/rotation |
| `localPosition` / `localRotation` / `localScale` | Transform relative to the parent |
| `parent` / `GetChild(i)` / `childCount` | Parent, child access and child count |
| `SetParent(parent, false)` | Reparent without preserving the previous world transform |
| `TransformPoint(local)` / `InverseTransformPoint(world)` | Convert between local and world coordinates |
| `gameObject.SetActive(bool)` | Set the object's own active state |
| `activeSelf` / `activeInHierarchy` | Own state/effective state including parents |
| `enabled` / `isActiveAndEnabled` | Behaviour switch/effective switch including object state |
| `gameObject.AddComponent<T>()` | Add a loaded component type to an instance |

For example, attach an effect to a model socket and set its local transform:

```csharp
instance.transform.SetParent(socket, false);
instance.transform.localPosition = Vector3.zero;
instance.transform.localRotation = Quaternion.identity;
instance.transform.localScale = Vector3.one;
instance.SetActive(true);
```

`enabled = false` disables that Behaviour only; it does not turn off other components on the object. Deactivating a parent GameObject affects its entire subtree. Changing the root `layer` does not change child layer numbers automatically. See [color and Bloom](ColorAndBloom.md) for layer-based Bloom.

## 5. Loading, instantiating and destroying prefabs

Run this on the main thread after a map is loaded. `contentMod` is the owning `ModContentPack`:

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/Fire");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = position;
instance.SetActive(true);
instance.GetComponent<MyMod.Effects.FireController>().PlayFire();

//Destroy when no longer needed, when the owner is destroyed, or when the map closes.
RimUnity.Object.Destroy(instance);
```

This requires the earlier `FireController` on the prefab root. `"Effects/Fire"` is the exported logical path, not a JSON filename. Do not activate, modify or destroy `template`: instances share loaded resource templates. Only loaded root templates can be cloned; cloning live instances or partial subtrees is unsupported.

If using a pool such as Lumi, return instances to that pool. Do not let both the pool and your Mono script destroy the same instance. A GameObject does not automatically become a RimWorld Thing or enter the save file. Save gameplay state in your content mod and recreate visual objects as needed.

## 6. Coroutines and time

RimUnity provides `StartCoroutine(IEnumerator)`, `StopCoroutine(Coroutine)`, `StopCoroutine(IEnumerator)` and `StopAllCoroutines()`. Supported yields include `null`, `WaitForSeconds`, `WaitForSecondsRealtime`, `WaitForEndOfFrame`, `WaitForFixedUpdate`, `WaitUntil`, `WaitWhile`, `CustomYieldInstruction`, nested IEnumerator and another Coroutine. Do not assume string method-name overloads, `InvokeRepeating` or every Unity YieldInstruction are available.

This example waits for two frames after activation before running visual initialization:

```csharp
using System.Collections;
using RimUnity;

//Start and stop a visual initialization coroutine.
public sealed class DeferredVisual : MonoBehaviour
{
    //Begin a new display cycle on each activation.
    private void OnEnable() => StartCoroutine(Prepare());

    //Wait for two frame updates, not two game ticks.
    private IEnumerator Prepare()
    {
        yield return null;
        yield return null;
        //Run your visual logic here.
    }

    //Explicitly stop owned coroutines when the script is disabled.
    private void OnDisable() => StopAllCoroutines();

    //Stop coroutines and release component references on destruction.
    private void OnDestroy() => StopAllCoroutines();
}
```

Custom Mono `Update`, `Time.deltaTime` and `WaitForSeconds` use RimUnity frame time, which **is not automatically RimWorld simulation time**. Pausing the game does not stop rendered frames. Do not apply damage, production or resource consumption per visual update; keep gameplay in the content mod's normal tick logic.

Particles and Animator have their own game-time integration. `main.useUnscaledTime` on particles and `updateMode = RimUnity.AnimatorUpdateMode.UnscaledTime` on Animator select unscaled time. These do not change the clocks used by other custom scripts or coroutines. `RimUnity.Time.timeScale` controls the scaled clock for custom Mono scripts and `WaitForSeconds`; it does not control RimWorld's pause or speed. Deactivating or destroying an object ends its coroutines. Setting only a script's `enabled = false` does not end them automatically.

## 7. Common components

| Feature | Game-side types and common calls |
|---|---|
| Particles | `RimUnity.ParticleSystem`: `Play()`, `Pause()`, `Stop()`, `Clear()`, `IsAlive()` |
| Animator | `RimUnity.Animator`: `Play("Idle")`, `CrossFade("Run", 0.2f)`, `SetFloat`, `SetBool`, `SetTrigger` |
| Lines/trails | `RimUnity.LineRenderer`, `RimUnity.TrailRenderer` |
| Static/skinned rendering | `RimUnity.MeshRenderer`, `RimUnity.SkinnedMeshRenderer` |
| UGUI | `RimUnity.UI.Button`, `Image`, `Text` and others; see [types and scripts](Usage.md) |

Animator state and parameter names must exist in the exported Controller. `CrossFade` uses normalized transition duration; use `CrossFadeInFixedTime` for seconds. Complete Animator features such as Humanoid and IK are unsupported.

Particle `Play()` includes children and associated sub-emitters by default. Do not blindly call Play on every child system. `SetActive(false)` deactivates an object; `Pause()` pauses particles. Their lifecycle semantics differ. See [particle usage](ParticleQuickStart.md) and [supported features](Status.md) for details.

## 8. Integration checklist

- The custom script assembly is loaded, fully qualified type names are unique, and component fields use RimUnity types.
- References are assigned in Unity; hierarchy paths are relative to the node that actually owns the script.
- Loading, instantiation, GetComponent and component changes happen on the game main thread, not in background Tasks.
- Work only with instances. Unsubscribe from events and stop the appropriate coroutines on disable/destruction. Dispose of map-owned visual objects when the map closes.
- Use the interfaces described here. Check [supported features](Status.md) before relying on other Unity APIs.
