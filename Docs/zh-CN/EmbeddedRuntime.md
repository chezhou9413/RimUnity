# 内置运行时接入 · 0.1.14

内置包提供与独立前置完全相同的 `RimUnity.Core`、`RimUnity`、`RimUnity.Spine` 类型和程序集身份，连同框架全部 Defs、GLSL、双语设置文字与许可。`RimUnity.Embedded.dll` 是容器和加载入口，不是第二套 API，也不包含真实 Unity 引擎、游戏程序集或 Harmony。

文件名始终为 `RimUnity.Embedded.dll`；容器内部程序集名含构建哈希，使不同版本可以同时被 .NET 扫描。引导状态在进程中共享，实际 API 的三个程序集名称与身份不变。

## 下载与依赖

完整发版应从同一次构建生成独立版和内置包。源码用户运行 `Tools/Build.ps1` 后用 `Tools/Pack.ps1` 打包；接入时保持 `RimUnity.Embedded.dll`、`RimUnity.Embedded.targets`、`References/` 的相对位置。

所有接入方式都依赖 RimWorld 1.7 和 Harmony。内容模组的 `About/About.xml` 应声明 `brrainz.harmony` 为依赖并在它之后加载；内置模式无需强制依赖 `Chezhou.RimUnity`。纯资源模组只需将内置 DLL 放到有效加载目录的 `Assemblies` 中，通常是 `1.7/Assemblies`。不要把 References 放进去。

## C# 内容模组

不强制依赖独立版、而要使用内置运行时的 C# 程序集（包括继承 MonoBehaviour、Mod 类、字段类型和跨程序集引用），必须使用下面的接入。独立版恢复提前加载后，原有普通 DLL 模组可继续声明 `Chezhou.RimUnity` 为前置并在其后加载，无需改为 `.rimunity`。**任意现有 C# 模组仅复制内置 DLL 并不可靠。**

以下示例假定项目位于 `MyMod/Source/MyMod/`，内置下载包解压在 `MyMod/Dependencies/RimUnity.Embedded/`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RimUnityModDirectory>$(MSBuildProjectDirectory)/../..</RimUnityModDirectory>
  </PropertyGroup>
  <!--照常引用所需游戏程序集，设 Private="false"，不分发游戏 DLL。-->
  <Import Project="../../Dependencies/RimUnity.Embedded/RimUnity.Embedded.targets" />
</Project>
```

导入后自动添加三份编译引用。普通编译输出保留在 `bin/Release/net10.0/`，可供其他工程的 ProjectReference 使用；不要将 OutputPath 设成游戏的 Assemblies 目录。构建向目标模组发布：

```text
MyMod/1.7/Assemblies/
  RimUnity.Embedded.dll
  MyMod.rimunity
  MyMod.rimunity.json
```

`.rimunity` 仍是原内容程序集，只有后缀不同；JSON 声明协议和最低运行时版本。默认最低版本为 0.1.9，可通过 `RimUnityMinimumVersion` 提高要求，不能降低到实际 API 要求以下。所有依赖这些类型的跨程序集项目也必须各自导入 targets 并发布到所属模组；ProjectReference 使用普通编译输出。不同内容程序集应有不同 AssemblyName。

从普通 DLL 发布方式迁移时，先移除目标 Assemblies 下旧的内容 DLL，以及散装的 RimUnity/Core/Spine DLL。构建检测到同名旧文件会停止并说明原因，不会自动删除用户文件。也不能从其他普通 DLL 中提前引用这些延迟程序集。

## 选择、脚本和资源加载

0.1.14 根据实际加载的三份运行时程序集及其共同所属内容包识别前置，并校验协议和有效资源目录。不依赖包 ID、模组名称或 Steam 链接；管理器改写标识不会使真实运行时被漏认。若归属不唯一、程序集混装或资源缺失，则明确报错。

1. 从 0.1.13 起，独立 `Chezhou.RimUnity` 恢复在 `1.7/Assemblies` 提供普通运行时 DLL，并由原版提前加载，以兼容已有 C# 模组的类型扫描。启用独立版时固定使用其 DLL、Defs 和 Shader；内置包不会替换它，也不会读取或挂载内置载荷。独立版必须满足内容最低版本要求，不满足时需更新独立版。
2. 没有独立版时，只扫描已启用模组的有效加载目录，选择协议兼容、满足全部延迟内容最低版本要求的最高内置版本。同版本载荷不同报告冲突；相同副本只加载一次。未启用模组、其他目录与网络版本不参与选择。涅瓦莲等携带内置包的内容模组不能直接替代普通 DLL 模组所需的独立前置。
3. 原版完成首次 Mod 构造后，引导器在 `LoadModXML` 前选定运行时。先登记运行时程序集并创建其 Mod，再载入所有 `.rimunity`，登记到各自 `ModContentPack`，刷新类型缓存并仅构造这些程序集内的 Mod。不重新调用全局 Mod 工厂，不重试其他模组失败的构造；与旧内置入口混装时仍优先执行这一规则。重入或重复 XML 调用不会再次初始化。
4. 仅在内置模式下，选中的框架资源按载荷 SHA-256 释放到游戏用户数据目录的 `RimUnity/Embedded/<hash>`，仅向提供方挂载这一个资源目录；原有模组目录不变。资源缓存损坏会明确报错，可关闭游戏后删除提示中的对应哈希目录，再启动重新生成。

内容资源继续保存在各自 `1.7/Data/RimUnity`，调用 `PrefabLoader.Load(contentMod, path)` 时仍传入资源所属模组。内置包不复制、不合并其他模组的资源，也不改变内容 Mod 的实例归属。Unity 导出流程不变，没有新的导出开关。

本地版本选择不联网。没有联网检测、后台下载或自动更新，离线游玩不受网络状态影响。设置中的“运行时”页可以查看实际来源、提供方、资源路径、哈希和内置副本数。

## 常见错误与许可

| 提示 | 处理 |
| --- | --- |
| DLL 被提前加载 / References 混装 | 移走内容 DLL 和散装运行时引用，重新使用 targets 发布，检查所有跨程序集依赖。 |
| 独立版本不兼容 | 更新独立 RimUnity；不能替换进程中已经加载的运行时。 |
| 同版本载荷冲突 | 将相关模组内置包统一为同一个正式下载包，不能只改版本号冒充同一构建。 |
| 内容程序集身份重复 | 每个内容程序集使用唯一 AssemblyName，不要把同一个内容 DLL 发布到多个模组。 |
| 找不到 JSON 或类型 | 保留 `.rimunity.json`，确认关联程序集都采用延迟接入且对应模组已启用。 |

RimUnity 自有代码为 MIT；UGUI 保留 MIT 声明，Spine Runtime 使用其独立许可。分发内置 DLL 时其中已包含许可；下载包也提供可直接阅读的 `LICENSE` 与 `ThirdParty/`。References 仅供编译，禁止作为额外运行时部署。

引导器复用原版已登记的程序集列表，只扫描携带内置入口的模组；不会递归遍历无关模组的目录。内置模式校验候选载荷；独立模式直接复用已加载运行时。脚本接入需要刷新游戏类型缓存，这部分开销取决于整个模组列表的类型数量。

遇到加载问题时，请在反馈中附上游戏日志和设置“运行时”页显示的版本、来源及提供方，方便定位实际加载的运行时。
