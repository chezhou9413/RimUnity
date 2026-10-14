# 资源载入与目录约定

## 内容归属与逻辑路径

资源属于使用方模组，而不是 RimUnity 框架。一个内容模组的目录示例：

```text
MyContentMod/
├─ About/About.xml
├─ LoadFolders.xml
└─ 1.7/
   ├─ Assemblies/MyContentMod.dll
   └─ Data/RimUnity/
      ├─ catalog.json
      └─ exports/<generation>/...
```

`LoadFolders.xml` 可通过 `<v1.7><li>1.7</li></v1.7>` 加载该版本的程序集和 Def。RimUnity 的 ModContentPack 加载入口固定读取 `mod.RootDir/1.7/Data/RimUnity`，不自动解析其它资源目录。

清单记录格式版本、owner、稳定资源 ID、名称、描述、逻辑路径、相对文件路径、类型及依赖。调用代码使用逻辑路径，例如 `Effects/BasicBurst`，不要写 `exports/<generation>/xxx.json`；后者会随导出变化。加载器不会跨模组查找同名资源来补齐缺失依赖。

## 公开入口

| API | 返回／用途 |
|---|---|
| `PrefabLoader.Load(mod, path)` | 指定内容模组的共享 GameObject 模板 |
| `PrefabLoader.LoadDirectory(dataDirectory, path)` | 指定 Data/RimUnity 目录中的模板 |
| `AssetLoader.Load<T>(mod, path)` | 模板、材质、贴图、Shader 等已导出对象 |
| `AssetLoader.LoadDirectory<T>(dataDirectory, path)` | 显式目录中的指定类型对象 |
| `AssetLoader.LoadBundleAsset<T>(mod, bundle, assetPath)` | 按保留的 AB 包名与 Unity 工程路径读取明文资源 |

所有泛型类型必须派生自 `RimUnity.Object`，且匹配导出记录的实际类型。

```csharp
using RimUnity.Assets;
using RimUnity;

var template = PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
var material = AssetLoader.Load<Material>(contentMod, "Materials/Glow");
var texture = AssetLoader.Load<Texture2D>(contentMod, "Textures/Noise");
var shader = AssetLoader.Load<Shader>(contentMod, "Shaders/Dissolve");
```

这些逻辑路径仅为示例，必须在自己的 catalog 中存在。自动收集的依赖通常使用 `__assets/` 内部路径；需要被脚本稳定直接加载的材质、贴图或 Shader，应作为独立入口导出并指定逻辑路径。

## 按包名加载

若使用 `BundleAssetExporter` 按 Unity 的 AssetBundle 标签导出：

```csharp
var template = AssetLoader.LoadBundleAsset<GameObject>(
    contentMod, "myeffects", "Assets/Effects/Portal.prefab");
```

其实际逻辑路径为 `Bundles/myeffects/Assets/Effects/Portal.prefab`。这里使用包名作为路径分组，不读取二进制 AssetBundle，也不能拿未导出的 AB 路径直接调用。批量粒子窗口采用另一种规则：`Assets/Effects/Portal.prefab` 对应 `Particles/Effects/Portal`；以最终 catalog 为准。

## 缓存、共享与销毁

- 清单和资源按目录缓存，在进程生命周期中复用；相同依赖不因每次 Load 重建。
- Prefab 模板保持失活；实例保留导出的层级、字段、引用和活动状态。
- 不修改或销毁加载器返回的共享材质、贴图和模板。需要实例材质参数时，优先使用渲染器的 MaterialPropertyBlock，或由业务创建并负责销毁独立 Material。
- 在实例不再使用时销毁 GameObject；不要假设所有一次性粒子都会自动销毁根对象。
- 编辑导出文件后重启游戏生效。当前没有 catalog 自动刷新、模板热重载或公开资源卸载接口。

## 发布内容资源

发布时携带 `catalog.json` 和它引用的全部依赖文件。直接复制整个成功导出的 Data/RimUnity 目录最简单；若裁剪历史 generation，应计算清单及依赖中的文件闭包，不要只保留最新目录——未重导出的入口可能仍指向旧 generation。

失败导出目录中的 `FAILED.txt`、批次报告、`.export.lock` 和临时文件不是运行必需资源。清单中的稳定 ID、相对路径与依赖应由导出器维护，不建议手工修改。

`Tools/Pack.ps1` 只打包框架，不替内容模组收集资源。图片／字体／模型的分发许可由内容作者确认，RimUnity 的 MIT 不会改变它们的许可。

## 常见加载错误

| 错误 | 优先检查 |
|---|---|
| 没有对应资源 | contentMod 是否正确、逻辑路径是否存在、是否误用源文件路径 |
| 类型匹配 0 项／多项 | 业务 DLL 是否加载、完整类型名是否匹配、是否重复定义；必要时首次加载前注册别名 |
| 缺少依赖／文件 | 是否漏发旧 generation 或依赖、catalog 和文件是否来自同一批产物 |
| 资源类型不匹配 | `Load<T>` 的 T 是否与记录一致 |
| Shader 编译／布局报错 | 工具与目标游戏版本、转换诊断、完整 GLSL／描述文件是否一起发布 |
| 加载成功但不显示 | 实例活动状态、位置／缩放、材质 Alpha、层与绘制通道、暂停与发射条件 |
