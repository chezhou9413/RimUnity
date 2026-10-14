# IMGUI 自定义材质

## 导出材质

独立材质导出时设置 `ExportRequest.ui = true`：

```csharp
PrefabExporter.Export(modDirectory, new ExportRequest {
    asset = material,
    ui = true,
    path = "Materials/UIFlow"
});
```

名称、参数、纹理和混合状态随材质写入清单，不需要手写 ShaderDef。

## 加载与绘制

先在游戏主线程加载材质：

```csharp
var material = RimUnity.Assets.AssetLoader.Load<RimUnity.Material>(
    contentMod, "Materials/UIFlow");
```

在 IMGUI 的 Repaint 中绘制四边形：

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

四个顶点按边界顺序传入，坐标相对于当前 GUI 分组。绘制遵守当前裁切区域，UV 与颜色使用 RimUnity 类型。材质由加载器共享；需要独立参数时创建自己的材质副本，并在使用结束后销毁。

## 材质要求

Overlay 材质不使用场景深度。使用 Always 深度测试并关闭深度写入；导出器也接受不写深度的 LEqual 材质，并转换为 Always。其他不支持的深度模式会报告导出错误。
