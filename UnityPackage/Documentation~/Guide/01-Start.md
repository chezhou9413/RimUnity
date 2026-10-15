# 从第一次导出开始

RimUnity 把 Unity 工程中的资源转换为 RimWorld 1.7 的 Waylith 引擎可以读取的数据。制作端仍使用 Unity 原生组件，游戏端通过 RimUnity 加载和控制资源。

## 先分清两份安装内容

玩家安装 Harmony、RimUnity 游戏模组和内容模组即可。制作资源的作者还需要 Unity 导出插件。游戏模组 ZIP 放进 RimWorld 的 Mods；Unity 插件 TGZ 通过 Package Manager 的 Add package from tarball 安装。源码安装选择 UnityPackage/package.json。

制作环境以 Unity 2022.3、Built-in 或受支持的 URP 14 功能为准。游戏脚本使用 .NET 10，引用目标游戏的程序集以及 RimUnity.Core.dll、RimUnity.dll。1.6 目录只是安装占位，没有运行功能。

## 做一个最小例子

1. 在 Unity 创建一个根节点带 ParticleSystem 的 Prefab。
2. 打开 Tools → RimUnity → 导出明文预制体。
3. 选择工程中的 Prefab，填写 Effects/BasicBurst 这样的逻辑路径。
4. 选择自己的内容模组目录。它需要 About/About.xml 和有效 packageId。
5. 导出并检查 Console。出现降级警告时，确认丢失的行为能否接受。

输出位于内容模组的 1.7/Data/RimUnity。catalog.json 是资源清单，exports 和 blobs 保存数据及依赖。调用代码使用逻辑路径，不要依赖每次都会改变的生成目录。

## 在游戏中加载

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = new RimUnity.Vector3(x, y, z);
```

contentMod 必须是资源实际所属的内容模组。加载器返回共享模板，实例化后再修改；效果结束时由使用方销毁实例。不要假设粒子播完会自动销毁根对象。

下一步：需要多个输出目录时读“批量构建与发布”；需要修改导出输入时读“给导出挂处理函数”；画面不对时读“诊断与排错”。原文区保留完整 API 示例与安装步骤。
