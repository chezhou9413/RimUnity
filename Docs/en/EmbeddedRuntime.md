# Embedded runtime integration · 0.1.9 Preview

The embedded package contains the same `RimUnity.Core`, `RimUnity` and `RimUnity.Spine` assemblies and public types as the standalone mod, plus all built-in Defs, GLSL, bilingual settings text and licenses. `RimUnity.Embedded.dll` is a container and bootstrap, not a separate API. It contains no real Unity engine, game assemblies or Harmony.

The filename stays `RimUnity.Embedded.dll`. Its internal container assembly name includes a build hash so different versions can coexist during .NET discovery. Bootstrap state is shared process-wide; the three actual API assembly identities remain unchanged.

## Download and dependencies

Download the [0.1.9 Preview embedded ZIP](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/RimUnity.Embedded-0.1.9-preview.zip). Keep `RimUnity.Embedded.dll`, `RimUnity.Embedded.targets` and `References/` together after extraction.

Both integration modes require RimWorld 1.7 and Harmony. Declare `brrainz.harmony` as a dependency in the content mod's About.xml and load after it. Embedded mode does not require a mandatory `Chezhou.RimUnity` dependency. Resource-only mods may simply copy the embedded DLL into an active load folder's `Assemblies`, usually `1.7/Assemblies`. Never copy References there.

## C# content mods

Every assembly that needs RimUnity types during early type discovery, including MonoBehaviour inheritance, Mod classes, fields and cross-assembly references, must participate. **Copying the embedded DLL into an arbitrary existing C# mod is not sufficient.**

For a project at `MyMod/Source/MyMod/`, with the package extracted into `MyMod/Dependencies/RimUnity.Embedded/`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RimUnityModDirectory>$(MSBuildProjectDirectory)/../..</RimUnityModDirectory>
  </PropertyGroup>
  <!--Reference required game assemblies with Private="false"; do not redistribute them.-->
  <Import Project="../../Dependencies/RimUnity.Embedded/RimUnity.Embedded.targets" />
</Project>
```

The import adds all three compile references. Regular DLL output remains in `bin/Release/net10.0/` for ProjectReference consumers; do not set OutputPath to the game's Assemblies folder. Building publishes:

```text
MyMod/1.7/Assemblies/
  RimUnity.Embedded.dll
  MyMod.rimunity
  MyMod.rimunity.json
```

The `.rimunity` file is the original content assembly with a different extension. Its JSON declares the protocol and minimum runtime version, defaulting to 0.1.9. Increase `RimUnityMinimumVersion` when using newer APIs; never declare less than your actual requirement. Every project depending on these deferred types must also import the targets and publish to its own mod. ProjectReference still uses the regular DLL output. Give different content assemblies distinct AssemblyName values.

When migrating, remove old content DLLs and loose RimUnity/Core/Spine DLLs from the deployment Assemblies folder first. The targets reject stale files without deleting them. Ordinary eagerly scanned DLLs must not reference deferred assemblies either.

## Selection and loading

1. Enabled `Chezhou.RimUnity` always supplies runtime DLLs, Defs and shaders. Embedded payloads are not read, extracted or mounted. Standalone must support the embedded protocol and satisfy script requirements; incompatibility reports an error and never triggers embedded fallback.
2. Without standalone, only active mods' enabled load folders are scanned. The newest compatible payload satisfying all content requirements wins. Identical copies run once; differing payloads with the same version report a conflict.
3. After the game's first Mod construction pass, the bootstrap runs before `LoadModXML`. It registers runtime assemblies and creates the runtime Mod, then loads deferred content assemblies, registers each with its own ModContentPack, clears type caches and constructs new Mod instances. Reentry or repeated XML calls cannot initialize twice.
4. Selected framework resources are extracted into `RimUnity/Embedded/<payload-SHA256>` under the game's user data directory. Only that folder is attached to the selected provider; existing content folders stay intact. Corrupt cache files report an error. Close the game and remove only the reported hash directory to recreate it on the next launch.

Content assets remain under their own `1.7/Data/RimUnity`. Continue passing the owning content mod to `PrefabLoader.Load(contentMod, path)`. Embedding does not merge or overwrite other mods' assets or change content Mod ownership. The Unity export workflow is unchanged; there is no additional export option.

Selection is entirely local, with no connectivity check, background download or automatic update. The Runtime settings page shows the active source, provider, resource location, hash and embedded copy count.

## Errors and licensing

| Error | Action |
| --- | --- |
| Early DLL load or mixed References | Remove regular content/runtime DLLs and rebuild through targets; check every cross-assembly dependency. |
| Incompatible standalone | Update standalone RimUnity. The bootstrap cannot override an enabled dependency. |
| Same-version payload conflict | Use one identical release package across affected mods; do not relabel different builds as the same version. |
| Duplicate content identity | Use a unique AssemblyName per content assembly; do not deploy it in multiple mods. |
| Missing JSON or type | Preserve the sidecar JSON and ensure dependent assemblies use deferred integration and their mods are enabled. |

RimUnity's original code is MIT licensed. UGUI retains its MIT notice; Spine Runtime has separate licensing. Licenses are inside the DLL and also provided as readable `LICENSE` and `ThirdParty/` files. References are for compilation only and must not be deployed as extra runtimes.

The bootstrap reuses the game's assembly registry and only scans mods carrying an embedded entry point. It does not recursively traverse unrelated mod folders. Each actual embedded copy still requires validation; standalone selection skips payload bytes. Deferred scripts also require game type-cache refreshes, whose cost depends on the full list's type count.

Compilation and offline loader checks do not establish full in-game rendering behavior. Release notes separately record offline verification and actual startup checks.
