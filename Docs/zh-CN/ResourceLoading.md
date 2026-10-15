# 资源载入与目录约定

[简体中文](ResourceLoading.md) | [English](../en/ResourceLoading.md)

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
      ├─ exports/<generation>/...
      └─ blobs/<内容哈希>.<扩展名>
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

- 清单和资源按目录、资源 ID 缓存，在进程生命周期中复用；多个预制体引用同一材质、贴图或网格时只加载一次。逻辑路径和包内名称使用索引查找，JSON 从文件流直接解码。
- `blobs/` 共享相同内容的磁盘文件；不同资源 ID 仍是不同对象，例如像素相同但色彩空间或采样方式不同的贴图。不同 Mod 的资源缓存独立，不按同名或同图强行合并。
- Prefab 模板保持失活；实例保留导出的层级、字段、引用和活动状态。
- 不修改或销毁加载器返回的共享材质、贴图和模板。需要实例材质参数时，优先使用渲染器的 MaterialPropertyBlock，或由业务创建并负责销毁独立 Material。
- 在实例不再使用时销毁 GameObject；不要假设所有一次性粒子都会自动销毁根对象。
- 编辑导出文件后重启游戏生效。当前没有 catalog 自动刷新、模板热重载或公开资源卸载接口。

## 发布内容资源

### 纹理格式与显存生命周期

标准导出器把源资源已经使用的 DXT1、DXT5、BC4、BC5、BC7 格式保存为 `.rutex`，包含尺寸、格式和完整 mip 链。方向转换后使用 Unity 编辑器的高质量压缩器重新编码；尺寸不变，但块压缩不是逐像素无损转换。未压缩图片和浮点数值贴图仍使用各自的导出路径，不自动降低分辨率或压缩数值数据。

从 0.1.8 起，至少 4 KiB 的 GPU 贴图载荷在无损 Gzip 能节省至少 5% 且不少于 256 字节时保存为 `.rutex.gz`，否则保留 `.rutex`。此步骤逐字节保留已经导出的 GPU 块、尺寸、透明标记和 mip 链。运行时直接流式解压到各 mip 的最终缓冲区，继续复用现有资源缓存及宿主磁盘缓存；缓存缺失后的恢复也支持压缩源文件。不会在运行时重新做 GPU 纹理压缩。

新的导出器和 `ExportStorage.CopyForPublish` 均应用此策略，后者也可压缩已有输出。须配套更新 RimUnity.Core 与 RimUnity 至 0.1.8；旧运行时不能读取 `.rutex.gz`。新运行时兼容旧 `.rutex`，逻辑资源地址保持不变。压缩减少磁盘与分发体积，不减少显存；首次读取和缓存缺失后的恢复会增加解压 CPU 开销。

文件纹理第一次参与采样时才上传 GPU。未启用 Read/Write 的资源上传后不保留 CPU 像素副本，并登记到 Waylith 磁盘缓存和显存驻留管理中；宿主可以回收闲置贴图，再从缓存或源文件恢复。缓存命中不会重复写入磁盘队列。脚本改写像素后解除原文件恢复关系，避免回收后重新出现旧像素。

隐藏画布和相机释放自有绘制附件，视频停止显示后释放其自有输出附件。离开存档时回收缓存贴图和离屏附件的显存，保留资源对象身份。外部传入的共享资源仍由其所属方管理。缓存总量不等于当前显存驻留量，也不等于整台显卡的占用。

刷新既有资源时，可在包含源贴图的 Unity 工程中调用 `RimUnity.Editor.TextureExporter.Refresh(dataDirectory)`，再运行 `ExportStorage.CopyForPublish`。多工程可依次刷新同一资源目录；仅更新 GUID 和 local ID 匹配的贴图，保留已有公开路径、预制体和材质变体。建议对导出副本执行，确认成功后整体发布。

发布时携带 `catalog.json` 和它引用的全部依赖文件，包括 `blobs/`。构建管理器会自动整理发布目录；直接调用 `PrefabExporter.Export` 或逐项粒子导出的结果，可在自己的 Editor 脚本中生成一个精简副本，无需重新转换 Unity 资源：

```csharp
RimUnity.Editor.ExportStorage.CopyForPublish(
    "C:/Mods/MyContentMod/1.7/Data/RimUnity",
    "C:/Builds/MyContentMod/1.7/Data/RimUnity");
```

两个参数均为资源目录，不是 Mod 根目录。目标必须不存在或为空，且不能与源目录相同或互相包含。源目录不变；出现缺失依赖或非法路径会报错，不应发布失败的目标目录。将成功输出整体作为内容模组的 `1.7/Data/RimUnity` 分发。

整理只保留公开入口及其依赖，压紧 JSON，并按 SHA-256 共享内容相同的贴图、字体、GLSL 等附属文件。资源 ID、逻辑路径和显式参数保留。不能仅复制最新 generation——未重导出的入口可能仍引用较早的文件。

大资源 JSON 自动保存为 Gzip，加载接口无需变化。若输入已有跨模组共享引用，为 `CopyForPublish` 传入第三个参数 `owner => ownerDirectories[owner]`；字典值为该 packageId 对应的 `1.7/Data/RimUnity` 目录。该方法将外部文件带回独立副本；共同发布多个模组时，再调用 `ExportSharing.Share(prepared)`，其中 `prepared` 是正式 Mod 根目录到准备好的资源目录的字典。构建管理器已经自动完成这些步骤。

需要离线优化已有导出数据时，在仓库目录执行：

```powershell
dotnet run --project Tools/ResourcePacker/ResourcePacker.csproj -c Release -p:UnityEditorDir="C:/Unity/2022.3/Editor/Data" -- "C:/Builds/Optimized" "C:/Mods/BaseMod" "C:/Mods/AddonMod"
```

需要 .NET 10 SDK 和本机 Unity 编辑器的 Newtonsoft.Json 程序集，不启动 Unity 或游戏。输出目录必须不存在；输出按 packageId 分目录，并生成同名 ZIP。只包含资源目录，不是完整模组，分发时用它替换对应模组的 `1.7/Data/RimUnity`。原目录不变，共享资源的所有所属模组都应作为参数传入。

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
