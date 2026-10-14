# Custom IMGUI materials

[简体中文](../zh-CN/ImmediateUI.md) | [English](ImmediateUI.md)

## Export the material

Set `ExportRequest.ui = true` when exporting a standalone material:

```csharp
PrefabExporter.Export(modDirectory, new ExportRequest {
    asset = material,
    ui = true,
    path = "Materials/UIFlow"
});
```

The catalog includes the material's name, parameters, textures and blend states. You do not need to write a ShaderDef manually.

## Load and draw

Load the material on the game main thread:

```csharp
var material = RimUnity.Assets.AssetLoader.Load<RimUnity.Material>(
    contentMod, "Materials/UIFlow");
```

Draw the quad during the IMGUI Repaint event:

```csharp
if (UnityEngine.Event.current.type == UnityEngine.EventType.Repaint)
{
    RimUnity.UI.ImmediateMesh.DrawQuad(material,
        new RimUnity.Vector2(10, 10), new RimUnity.Vector2(110, 10),
        new RimUnity.Vector2(110, 60), new RimUnity.Vector2(10, 60),
        new RimUnity.Vector2(0, 1), new RimUnity.Vector2(1, 1),
        new RimUnity.Vector2(1, 0), new RimUnity.Vector2(0, 0),
        RimUnity.Color.white);
}
```

Pass the four vertices in perimeter order. Coordinates are relative to the current GUI group, and drawing respects the current clipping region. UVs and colors use RimUnity types. The loaded material is shared; create your own material copy for independent parameters and destroy it when finished.

## Material requirements

Overlay materials do not use scene depth. Use Always depth testing with depth writes disabled. The exporter also accepts LEqual materials without depth writes and converts them to Always. Other unsupported depth modes produce export errors.
