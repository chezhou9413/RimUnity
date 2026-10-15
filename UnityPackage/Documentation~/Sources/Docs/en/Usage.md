# Types, directories and scripts

[简体中文](../zh-CN/Usage.md) | [English](Usage.md)

A content mod's catalog is stored at `1.7/Data/RimUnity/catalog.json`. Logical paths are separate from physical file paths. `PrefabLoader.Load(contentMod, path)` resolves resources from the specified mod; it does not search other mods to fill missing dependencies.

Use explicit aliases where helpful:

```csharp
using RimUnity;
using RectTransform = RimUnity.RectTransform;
using Canvas = RimUnity.Canvas;
using Button = RimUnity.UI.Button;
using Slider = RimUnity.UI.Slider;
using Text = RimUnity.UI.Text;
```

Use RimUnity types for GameObject, Transform, MonoBehaviour, vectors, Quaternion, Color, Mesh, Material and coroutines. Events are in `RimUnity.Events`. Reference at least `RimUnity.Core.dll` and `RimUnity.dll` in the game-side project, but distribute only your own mod DLLs. UGUI controls are in `RimUnity.UI`; pointer interfaces are in `RimUnity.EventSystems`. Use `RimUnity.Ugui.CreateObject(name, componentTypes)` to create rectangular UI objects dynamically.

Custom scripts can bind after recompilation if their fully qualified type names remain unchanged. For renamed types, call `TypeResolver.Register("Fully.Qualified.AuthoringType", typeof(NewType))` before loading assets. Do not load two script assemblies defining the same fully qualified type name; the loader rejects ambiguous matches.

UnityEvent supports dynamic calls with zero to four arguments and constant int, float, bool, string and Object arguments. Event targets must belong to the same exported prefab, and the script methods must exist in the game-side assembly.

The loader builds the complete hierarchy, adds components, restores fields, binds references and events, and then activates objects. Lifecycle callbacks are deferred during construction. RimUnity schedules `Awake`, `OnEnable`, `Start` and coroutines. Templates stay inactive so that they do not run scripts.

The supported input to `Instantiate` is a template returned by the loader. **Cloning live instances or partial subtrees, cross-prefab object references and runtime hot reload are not supported.** Do not use cached templates as scene objects; create separate instances with `Instantiate(template)`.

The default font maps to game font index 1. To provide specific glyph coverage, include a source font that you have permission to redistribute. The framework does not bundle font artwork.

Set texture transforms with `SetTextureScale` and `SetTextureOffset`. When setting `_MainTex_ST` directly, supply `(scale.x, scale.y, offset.x, offset.y)`.

Keep the same fully qualified script type names on the authoring and game sides, and use compilation symbols to select `UnityEngine` or `RimUnity`. Export resources with the companion UPM package. Convert values such as the game's `Thing.DrawPos` into RimUnity math types component by component.
