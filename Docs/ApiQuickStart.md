# API 快速接入

本文面向内容模组作者。制作端使用 Unity 原生组件；游戏端由 RimUnity 执行已适配的部分。资源准备和编译见 [工作流](Workflow.md)，完整路径规则见 [资源加载](ResourceLoading.md)。

脚本生命周期、层级查找、组件操作和协程示例见 [MonoBehaviour 与常用方法](MonoBehaviour.md)。

## 1. 声明依赖并引用程序集

在内容模组 `About/About.xml` 的现有节点中加入：

```xml
<modDependencies>
  <li><packageId>Chezhou.RimUnity</packageId><displayName>RimUnity</displayName></li>
</modDependencies>
<loadAfter><li>Chezhou.RimUnity</li></loadAfter>
```

不要重复创建已有的 `modDependencies`／`loadAfter` 节点。游戏中仍需启用 RimUnity 所依赖的 Harmony。

使用 .NET 10 SDK 工程，引用目标游戏自己的程序集，引用设置 `Private="false"`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RimWorldDir>C:/Games/RimWorld</RimWorldDir>
    <RimUnityDir>C:/Mods/RimUnity</RimUnityDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp" HintPath="$(RimWorldDir)/Assembly-CSharp.dll" Private="false" />
    <Reference Include="UnityStubs" HintPath="$(RimWorldDir)/UnityStubs.dll" Private="false" />
    <Reference Include="RimUnity.Core" HintPath="$(RimUnityDir)/1.7/Assemblies/RimUnity.Core.dll" Private="false" />
    <Reference Include="RimUnity" HintPath="$(RimUnityDir)/1.7/Assemblies/RimUnity.dll" Private="false" />
  </ItemGroup>
</Project>
```

按实际 API 用量追加 Waylith 等引用；发布时只携带自己的 DLL，不复制引用的游戏和框架程序集。

## 2. 实例化并播放粒子

假设已将一个根节点带 ParticleSystem 的 Prefab 导出为逻辑路径 `Effects/BasicBurst`。以下类可以放入内容模组，调用方传入自己持有的 `ModContentPack` 和地图位置：

```csharp
using System;
using RimUnity.Assets;
using RimUnity;
using Verse;
using ParticleSystem = RimUnity.ParticleSystem;

public sealed class BurstEffect : IDisposable
{
    public GameObject Instance { get; }
    private readonly ParticleSystem particles;

    //在地图已加载后的主线程创建，contentMod 必须是资源实际所属模组。
    public BurstEffect(ModContentPack contentMod, Vector3 position)
    {
        GameObject template = PrefabLoader.Load(contentMod, "Effects/BasicBurst");
        Instance = RimUnity.Object.Instantiate(template);
        Instance.transform.position = position;
        particles = Instance.GetComponent<ParticleSystem>();
        if (particles == null)
        {
            RimUnity.Object.Destroy(Instance);
            throw new InvalidOperationException("BasicBurst 根节点需要 ParticleSystem。");
        }
        Instance.SetActive(true);
        particles.Play();
    }

    //停止发射，已有粒子继续自然消散。
    public void Stop() => particles.Stop();

    //调用方在特效结束、拥有者销毁或地图退出时销毁实例。
    public void Dispose() => RimUnity.Object.Destroy(Instance);
}
```

`Load` 返回缓存模板，不能直接激活、改造或销毁模板；只操作克隆得到的实例。Instantiate 恢复导出的活动状态。若脚本需要在 Awake 前完成自定义初始化，应在制作端设计失活根及显式初始化流程，不假设实例化后的赋值早于 Awake。

`Play(true)` 默认包含子级及关联子发射器。不要无条件遍历全部子粒子逐个 Play，否则可能把本应由事件触发的子发射器独立启动。多个独立发射根应由业务脚本分别控制。

## 3. 常用类型与控制

| 制作端类型 | 游戏端类型／用法 |
|---|---|
| GameObject、Transform、MonoBehaviour、Vector3、Quaternion、Color | `RimUnity` 下对应类型 |
| Mesh、Material、Texture2D、Shader、ComputeBuffer | `RimUnity` 下对应类型 |
| UnityEvent 与参数事件 | `RimUnity.Events` |
| ParticleSystem、ParticleSystemRenderer | `RimUnity.ParticleSystem`、`RimUnity.ParticleSystemRenderer` |
| Animator、Animation、LineRenderer、TrailRenderer | `RimUnity` 下对应类型 |
| RectTransform、Canvas、CanvasGroup | `RimUnity` 下对应类型 |
| Image、Button、Slider、Text 等 | `RimUnity.UI` |
| 指针事件接口 | `RimUnity.EventSystems` |

游戏侧脚本使用 `using RimUnity;`。需要同时访问游戏的 `UnityEngine` 类型时，使用明确别名，不能将两套 GameObject、Component 或 Material 相互传递。Verse 返回的位置可在边界转换：`var p = pawn.DrawPos; instance.transform.position = new RimUnity.Vector3(p.x, p.y, p.z);`。

粒子提供 `Play`、`Pause`、`Stop`、`Clear`、`Simulate`、`IsAlive`、`GetParticles`／`SetParticles`。暂停整个对象可用 `SetActive(false)`，但这与粒子 Pause 的生命周期语义不同。最终销毁调用实例的 `Destroy`。数据采集等高频路径应复用数组，避免每帧分配。

## 4. 自定义脚本和事件

制作脚本和运行脚本保持完整类型名时，导出的字段及事件目标可直接解析；游戏程序集必须已经加载。类型更名时，在首次加载资源之前注册一次：

```csharp
RimUnity.Assets.TypeResolver.Register("Authoring.EffectDriver", typeof(MyMod.EffectDriver));
```

注册是添加映射，不是覆盖接口；重复注册同一个名称会报错。字段、资源引用和 UnityEvent 绑定必须使用当前支持的类型，详见 [类型与脚本用法](Usage.md)。不支持把第三方 DLL 原样自动转为 Waylith 组件。

## 5. 用内容模组设置控制 Bloom

在 Unity 给特效及需要参与泛光的子对象设置 Layer，例如 29。编号随 Prefab 导出，根对象改层不会自动改变子级。

```csharp
//地图初始化和设置改变时，在主线程同步内容模组自己的开关。
RimUnity.RimUnityMod.Bloom.SetLayerEnabled(29, myBloomEnabled);

//游戏生命周期结束时撤销该模组的接管。
RimUnity.RimUnityMod.Bloom.ReleaseLayer(29);
```

29 只是示例，不是框架保留层。同一层只应由一个内容模组负责；接管时开关优先于 RimUnity 手动选择，强度、阈值等参数仍共用。关闭 Bloom 不会停止粒子播放。更多限制见 [颜色与 Bloom](ColorAndBloom.md)。

所有实例化、资源加载、组件操作与设置接线均在游戏主线程执行；不要放进后台 Task。不支持运行实例／局部子树克隆和资源热重载。
