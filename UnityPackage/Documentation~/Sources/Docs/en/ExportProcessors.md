# Export processors

Processors run in the Unity Editor around each exported source asset, including prefabs and recursively discovered dependencies. The exporter preserves the source identity, runs preprocessing, reads properties and dependencies, compiles shaders, writes the resource, calls postprocessing, and finally performs cleanup.

## Automatic discovery

Place this optional script in your own project's `Assets/Editor`. An Editor asmdef needs a reference to `RimUnity.Editor`. Annotated public concrete classes require a public parameterless constructor and are instantiated once per export transaction. The prefab window, build manager and batch export paths discover them automatically.

```csharp
using RimUnity.Editor;
using UnityEngine;

[RimUnityExportProcessor]
public sealed class DisableSingleChannelForExport : ExportProcessor
{
    public override bool Matches(ExportContext context)
        => context.ExportAsset is Material material && material.shader != null
        && material.shader.name == "Cartoon FX/Remaster/Particle Ubershader"
        && material.HasProperty("_SingleChannel")
        && material.GetFloat("_SingleChannel") > 0f;

    public override void BeforeExport(ExportContext context)
    {
        var copy = context.CreateExportCopy<Material>();
        copy.SetFloat("_SingleChannel", 0f);
    }

    public override void AfterExport(ExportContext context)
        => Debug.Log(context.OutputFile);
}
```

The copy is destroyed during cleanup. The source material keeps its original setting, while the exported properties and shader conversion use the copy. GUID/local ID still come from the source. Disabling this property changes channel interpretation: verify the source texture's alpha before using this policy. This example is opt-in; RimUnity does not install it automatically.

## Attaching functions for one call

Set `ExportOptions.processors` to an array of `IExportProcessor`, using `DelegateExportProcessor` for functions. Its `filter`, `before`, `after` and `order` fields correspond to matching, preprocessing, postprocessing and priority. Pass the options to `PrefabExporter.Export(modDirectory, options, requests)`.

Set `discoverProcessors=false` to exclude automatic processors for that call. Otherwise discovered and explicit processors are combined. Arrays are snapshotted at the start of each transaction; processor instances are not cloned. Avoid installing the same policy through both mechanisms.

## Context and cleanup

- `Source` and `SourcePath` identify the original asset. Do not destroy it.
- `ExportAsset` is the working input; replacements must have the same concrete type. `CreateExportCopy<T>()` clones the current input and registers its destruction.
- For prefabs, `ExportAsset` starts as temporary prefab contents or an imported-model copy. It is safe to edit that working object without changing `Source`.
- `ResourceId` identifies the source variant. Standalone shader IDs depend on conversion and are available only after writing.
- `ReferencePath` identifies the first logical/component reference that triggered this asset. Shared assets must not receive conflicting policies based on their caller.
- `IsUI` and `IsCameraUI` identify export variants.
- `OutputDirectory` is the transaction's working directory. `OutputFile` is the written primary resource description, available during postprocessing.
- `Succeeded` becomes true after resource writing; a later postprocessing or cleanup error still fails the transaction. `Error` is available to cleanup on failure. This is not confirmation that the catalog has been published.

If you must edit a source object, capture its original value and call `DeferCleanup` BEFORE changing it. Restore the captured value, not an assumed default. Do not save or reimport the source asset. Prefer copies, since forced editor/process termination cannot execute finally blocks.

Preprocessors run by ascending `Order`, then fully qualified type name, preserving configuration order for ties of the same type. Successful postprocessors run in the same order. Registered cleanup actions always run in reverse order, including when preprocessing, writing, cancellation or postprocessing throws. One failing cleanup does not skip the rest; failures are aggregated.

`AfterExport` runs only after successful resource writing and before catalog publication. Restoration belongs in `DeferCleanup`. Processor failures preserve the previous successful catalog and leave transaction diagnostics. Arbitrary external effects are not rolled back: do not recursively start exports or change other published files from callbacks.

Shared assets run once per variant per transaction. Cache hits do not rerun processors. Build-manager exports may consist of multiple transactions. Generated shader programs, default normal textures and content-hash blobs are derived output, not independent Unity source assets; they do not receive another source callback.

Callbacks are synchronous on the export caller's thread. Invoke exporting on the Unity Editor main thread and do not access Unity objects from async void callbacks or background tasks.

## Offline handbook

Open `RimUnity → 百科全书` or `Tools → RimUnity → 百科全书`. The package contains a topic-based Chinese handbook, the repository READMEs and original Chinese/English documents. Search includes titles, source paths and bodies. Run `Tools/Sync-Manual.ps1` after editing source documents; packaging also synchronizes them. `-Check` verifies without writing.
