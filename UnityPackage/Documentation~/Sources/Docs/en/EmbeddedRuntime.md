# Embedded runtime integration · 0.1.14

The embedded package contains the same `RimUnity.Core`, `RimUnity` and `RimUnity.Spine` assemblies and public types as the standalone mod, plus all built-in Defs, GLSL, bilingual settings text and licenses. `RimUnity.Embedded.dll` is a container and bootstrap, not a separate API. It contains no real Unity engine, game assemblies or Harmony.

The filename stays `RimUnity.Embedded.dll`. Its internal container assembly name includes a build hash so different versions can coexist during .NET discovery. Bootstrap state is shared process-wide; the three actual API assembly identities remain unchanged.

## Download and dependencies

Build the standalone and embedded release packages together with `Tools/Build.ps1`, then package with `Tools/Pack.ps1`. Keep `RimUnity.Embedded.dll`, `RimUnity.Embedded.targets` and `References/` together when integrating.

Both integration modes require RimWorld 1.7 and Harmony. Declare `brrainz.harmony` as a dependency in the content mod's About.xml and load after it. Embedded mode does not require a mandatory `Chezhou.RimUnity` dependency. Resource-only mods may simply copy the embedded DLL into an active load folder's `Assemblies`, usually `1.7/Assemblies`. Never copy References there.

## C# content mods

C# assemblies using embedded mode, including MonoBehaviour inheritance, Mod classes, fields and cross-assembly references, must participate. Since 0.1.13, ordinary DLL mods can continue depending on standalone `Chezhou.RimUnity` and loading after it; they need not migrate to `.rimunity`. **Copying the embedded DLL into an arbitrary existing C# mod is not sufficient.**

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

Since 0.1.14, the provider is identified by the three loaded runtime assemblies and their shared owning content pack, with protocol and active resource folder validation. Package IDs, display names and Steam URLs are not required. Manager-renamed IDs remain supported; ambiguous ownership, mixed assemblies or missing resources report explicit errors.

1. Since 0.1.13, standalone `Chezhou.RimUnity` again provides ordinary runtime DLLs under `1.7/Assemblies`, loaded by the game before dependent C# type discovery. When enabled, standalone supplies DLLs, Defs and shaders; embedded payloads cannot replace it and are not read or mounted. Standalone must meet content version requirements; update it when it does not.
2. Without standalone, enabled mods' active load folders compete for the highest compatible embedded payload satisfying all deferred content requirements. Different payloads with the same version report a conflict; identical copies run once. Disabled mods, unrelated folders and network releases do not participate. A content mod carrying an embedded runtime cannot directly replace the standalone dependency required by ordinary DLL mods.
3. After the game's first Mod construction pass, the bootstrap runs before `LoadModXML`. It registers runtime assemblies and creates the runtime Mod, then loads deferred content assemblies, registers each with its own ModContentPack, clears type caches and constructs Mod instances only from those assemblies. It never reruns the global Mod factory or retries unrelated failed constructors. This scoped entry point takes precedence when older embedded bootstraps are also installed. Reentry or repeated XML calls cannot initialize twice.
4. In embedded mode only, selected framework resources are extracted into `RimUnity/Embedded/<payload-SHA256>` under the game's user data directory. Only that folder is attached to the selected provider; existing content folders stay intact. Corrupt cache files report an error. Close the game and remove only the reported hash directory to recreate it on the next launch.

Content assets remain under their own `1.7/Data/RimUnity`. Continue passing the owning content mod to `PrefabLoader.Load(contentMod, path)`. Embedding does not merge or overwrite other mods' assets or change content Mod ownership. The Unity export workflow is unchanged; there is no additional export option.

Selection is entirely local, with no connectivity check, background download or automatic update. The Runtime settings page shows the active source, provider, resource location, hash and embedded copy count.

## Errors and licensing

| Error | Action |
| --- | --- |
| Early DLL load or mixed References | Remove regular content/runtime DLLs and rebuild through targets; check every cross-assembly dependency. |
| Incompatible standalone | Update standalone RimUnity; an already loaded runtime cannot be replaced. |
| Same-version payload conflict | Use one identical release package across affected mods; do not relabel different builds as the same version. |
| Duplicate content identity | Use a unique AssemblyName per content assembly; do not deploy it in multiple mods. |
| Missing JSON or type | Preserve the sidecar JSON and ensure dependent assemblies use deferred integration and their mods are enabled. |

RimUnity's original code is MIT licensed. UGUI retains its MIT notice; Spine Runtime has separate licensing. Licenses are inside the DLL and also provided as readable `LICENSE` and `ThirdParty/` files. References are for compilation only and must not be deployed as extra runtimes.

The bootstrap reuses the game's assembly registry and only scans mods carrying an embedded entry point. It does not recursively traverse unrelated mod folders. Embedded mode validates candidate payloads; standalone mode reuses the already loaded runtime. Deferred scripts also require game type-cache refreshes, whose cost depends on the full list's type count.

When reporting a loading issue, include the game log and the version, source and provider shown on the Runtime settings page to identify the selected runtime.
