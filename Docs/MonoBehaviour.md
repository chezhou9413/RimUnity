# MonoBehaviour 与常用方法

游戏侧脚本继承 `RimUnity.MonoBehaviour`，使用 `RimUnity.GameObject`、`Transform` 和数学类型。RimUnity 负责对象树、字段与事件绑定、生命周期和协程；资源与绘制直接调用 Waylith。本文介绍常用 API，支持范围见 [功能说明](Status.md)。

程序集引用、导出和加载入口分别见 [API 快速接入](ApiQuickStart.md)、[工作流](Workflow.md) 和 [资源载入](ResourceLoading.md)。

## 1. 制作端和游戏端如何共用脚本

制作端使用 Unity 原生组件；游戏端保留脚本完整类型名，游戏侧组件使用 RimUnity 类型并编译为 .NET 10 程序集。

例如，Unity 中创建如下预制体，将脚本挂在 `EffectRoot`，并将 `Fire` 的粒子组件拖入脚本的 `fire` 字段：

```text
EffectRoot
└─ Effects
   ├─ Fire
   └─ Smoke
```

下面是可以同时维护制作端和游戏端的脚本。`RIMWORLD_1_7` 是内容模组自己的编译符号，需要在游戏侧项目的 `DefineConstants` 中设置，RimUnity 不会自动替使用方添加。

```csharp
#if RIMWORLD_1_7
using RimUnity;
#else
using UnityEngine;
#endif

namespace MyMod.Effects
{
    //控制预制体中已绑定的火焰发射器。
    public sealed class FireController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem fire;

        //导出资源应包含这个必需引用，缺失时明确报告制作错误。
        private void Awake()
        {
            if (fire == null)
                throw new System.InvalidOperationException("FireController 未绑定 fire。");
        }

        //由建筑、角色状态或动画事件触发。
        public void PlayFire()
        {
            fire.gameObject.SetActive(true);
            fire.Play();
        }

        //停止发射，已有粒子自然消散。
        public void StopFire() => fire.Stop();
    }
}
```

字段绑定发生在脚本生命周期执行前，同一个预制体内部的 GameObject、Transform 和组件引用会指向各自实例中的对象。`public` 字段和 `[SerializeField]` 字段可参与导出；属性不等于可序列化字段，`SerializeReference` 多态对象图不在支持范围内。

若游戏侧更改了脚本完整类型名，在首次加载资源之前注册一次映射：

```csharp
RimUnity.Assets.TypeResolver.Register(
    "Authoring.FireController", typeof(MyMod.Effects.FireController));
```

重复注册同一个名称会报错。导出只保存脚本类型与字段数据，不会把 C# 源码自动转成游戏脚本。

## 2. 生命周期与初始化顺序

加载器先建立全部节点和组件，再恢复字段、对象引用和事件，最后恢复活动状态；共享模板始终失活。实例的生命周期消息由 RimUnity 调度，不需要业务代码手动调用。

| 方法 | 常见用途 |
|---|---|
| `Awake()` | 检查必需引用、缓存节点与组件 |
| `OnEnable()` | 每次启用时订阅事件、开始当前显示周期 |
| `Start()` | 首次进入更新前的初始化，不用于每次对象池取出 |
| `Update()` | 每帧更新视觉表现 |
| `LateUpdate()` | 更新后调整跟随位置、挂点等 |
| `FixedUpdate()` | 按 `Time.fixedDeltaTime` 推进的定步长更新，不是游戏 Tick |
| `OnDisable()` | 取消事件订阅、停止该脚本持有的协程 |
| `OnDestroy()` | 释放自身创建和拥有的资源 |

实例化会恢复制作端保存的活动状态，`Awake` 等消息可能已在 `Instantiate` 返回前执行。不要假设“实例化后赋值”早于 Awake；需要先传入业务参数时，制作端将根节点保存为失活，并设计显式 `Initialize(...)` 方法，再由调用方激活。动态 `AddComponent` 也不经过预制体字段恢复流程，应使用显式初始化。

不要依赖多个脚本之间未约定的执行先后顺序。当前文档不承诺完整 Unity 消息集、脚本执行顺序设置或 `FixedUpdate` 与 RimWorld Tick 的对应关系。

## 3. 查找层级与组件

预制体实例保留节点名称、父子关系及局部变换。以下代码放在继承 MonoBehaviour 的脚本中，粒子类型使用 `RimUnity.ParticleSystem`：

```csharp
//相对于当前脚本所在节点，按路径查找；未激活子对象也可以找到。
Transform node = transform.Find("Effects/Fire");
if (node == null)
    throw new System.InvalidOperationException("预制体缺少 Effects/Fire 节点。");

var fire = node.GetComponent<RimUnity.ParticleSystem>();

//查整个子树，包括当前节点；true 包含未激活对象。
var first = GetComponentInChildren<RimUnity.ParticleSystem>(true);
var all = GetComponentsInChildren<RimUnity.ParticleSystem>(true);

//查询可选组件，不把缺少组件当作必需资源加载成功。
if (node.TryGetComponent<RimUnity.ParticleSystem>(out var optional))
    optional.Pause();

//遍历直接子节点。
for (int i = 0; i < transform.childCount; i++)
{
    Transform child = transform.GetChild(i);
    child.localScale = Vector3.one;
}
```

`transform.Find("Fire")` 只匹配直接子节点，不会递归搜索任意深度；跨层级写 `"Effects/Fire"`。`GetComponent<T>()` 只查当前对象，子树查询默认不包含未激活对象。查不到节点或单个组件时需要按资源契约处理，不应紧接着无条件访问其成员。

`RimUnity.GameObject.Find(name)`，但它属于全局查找；同名实例无法据此可靠区分。优先从持有的实例根节点查找，或使用 Inspector 引用。常用引用在初始化时缓存，不要每帧搜索整个层级。

## 4. Transform、激活与动态组件

| 常用成员 | 含义 |
|---|---|
| `transform.position` / `rotation` | 世界位置／旋转 |
| `localPosition` / `localRotation` / `localScale` | 相对于父节点的变换 |
| `parent` / `GetChild(i)` / `childCount` | 父节点、子节点和子节点数量 |
| `SetParent(parent, false)` | 改变父节点，不要求保留原世界变换 |
| `TransformPoint(local)` / `InverseTransformPoint(world)` | 局部与世界坐标转换 |
| `gameObject.SetActive(bool)` | 改变对象自身的活动状态 |
| `activeSelf` / `activeInHierarchy` | 自身状态／计入父级后的有效状态 |
| `enabled` / `isActiveAndEnabled` | Behaviour 开关／计入对象状态后的有效开关 |
| `gameObject.AddComponent<T>()` | 在实例上添加已加载的组件类型 |

例如，将特效挂到模型挂点下，并设置局部变换：

```csharp
instance.transform.SetParent(socket, false);
instance.transform.localPosition = Vector3.zero;
instance.transform.localRotation = Quaternion.identity;
instance.transform.localScale = Vector3.one;
instance.SetActive(true);
```

`enabled = false` 只禁用该 Behaviour，不会自动关闭同对象上的其他组件。关闭父 GameObject 会影响整个子树。修改根节点 `layer` 不会自动修改子节点层级编号；Layer Bloom 的接入见 [颜色与 Bloom](ColorAndBloom.md)。

## 5. 预制体加载、实例化和销毁

在地图已加载后的主线程操作，`contentMod` 是资源所属的 `ModContentPack`：

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/Fire");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = position;
instance.SetActive(true);
instance.GetComponent<MyMod.Effects.FireController>().PlayFire();

//使用结束、拥有者销毁或地图退出时销毁实例。
RimUnity.Object.Destroy(instance);
```

这里要求预制体根节点挂有前面的 `FireController`。`"Effects/Fire"` 是导出逻辑路径，不是 JSON 文件名。不要激活、修改或销毁 `template`；多个实例共享加载的资源模板。当前只支持从加载得到的根模板克隆，不支持克隆运行实例或局部子树。

如果使用 Lumi 等对象池，实例应交回对应池管理，不要同时由池和自己的 Mono 脚本销毁。普通 GameObject 不会自动成为 RimWorld 的 Thing，也不会自动保存进游戏存档；内容模组应保存业务状态，并在需要时重建表现对象。

## 6. 协程与时间

RimUnity 提供 `StartCoroutine(IEnumerator)`、`StopCoroutine(Coroutine)`、`StopCoroutine(IEnumerator)` 和 `StopAllCoroutines()`。支持 `yield return null`、`WaitForSeconds`、`WaitForSecondsRealtime`、`WaitForEndOfFrame`、`WaitForFixedUpdate`、`WaitUntil`、`WaitWhile`、`CustomYieldInstruction`、嵌套 IEnumerator 以及等待另一个 Coroutine。不要假定字符串方法名重载、`InvokeRepeating` 或所有 Unity YieldInstruction 都可用。

下面示例在启用后等待两帧，再执行自己的视觉初始化：

```csharp
using System.Collections;
using RimUnity;

//启动和停止协程。
public sealed class DeferredVisual : MonoBehaviour
{
    //每次启用开始一个新的显示周期。
    private void OnEnable() => StartCoroutine(Prepare());

    //等待两次帧更新，此处不表示经过了两个游戏 Tick。
    private IEnumerator Prepare()
    {
        yield return null;
        yield return null;
        //在这里执行自己的视觉逻辑。
    }

    //禁用脚本时主动结束自身协程。
    private void OnDisable() => StopAllCoroutines();

    //销毁时停止协程，释放组件引用。
    private void OnDestroy() => StopAllCoroutines();
}
```

自定义 Mono 的 `Update`、`Time.deltaTime` 和 `WaitForSeconds` 使用 RimUnity 帧时间，**不能直接视为 RimWorld 的模拟时间**。游戏暂停不等于停止绘制帧；不要在视觉 Update 中按帧执行伤害、生产或消耗等游戏逻辑。此类业务应接入内容模组原本的 Tick 流程。

RimUnity 的粒子／Animator 有自己的游戏时间适配。粒子的 `main.useUnscaledTime`、Animator 的 `updateMode = RimUnity.AnimatorUpdateMode.UnscaledTime` 可选择非缩放时间；这不会改变其他自定义脚本或协程的时钟。`RimUnity.Time.timeScale` 控制自定义 Mono 的缩放时钟和 `WaitForSeconds`，不改变 RimWorld 的暂停与倍速。对象失活或销毁会结束其协程；仅设置脚本 `enabled = false` 不会自动结束协程。

## 7. 常用桥接组件

| 功能 | 游戏侧类型与常用调用 |
|---|---|
| 粒子 | `RimUnity.ParticleSystem`：`Play()`、`Pause()`、`Stop()`、`Clear()`、`IsAlive()` |
| Animator | `RimUnity.Animator`：`Play("Idle")`、`CrossFade("Run", 0.2f)`、`SetFloat`、`SetBool`、`SetTrigger` |
| 线段／拖尾 | `RimUnity.LineRenderer`、`RimUnity.TrailRenderer` |
| 静态／骨骼绘制 | `RimUnity.MeshRenderer`、`RimUnity.SkinnedMeshRenderer` |
| UGUI | `RimUnity.UI.Button`、`Image`、`Text` 等；见 [类型与脚本用法](Usage.md) |

Animator 的状态名和参数名必须在导出的 Controller 中存在；`CrossFade` 的过渡时长使用归一化语义，需要秒数时使用 `CrossFadeInFixedTime`。不支持 Humanoid、IK 等完整 Animator 功能。

粒子 `Play()` 默认包含子级及关联子发射器，不要无条件遍历所有子粒子逐个 Play。`SetActive(false)` 是关闭对象，`Pause()` 是粒子暂停，两者生命周期不同。完整能力和限制见 [粒子说明](ParticleQuickStart.md) 与 [支持范围](Status.md)。

## 8. 接入检查

- 自定义脚本程序集已加载，完整类型名唯一，组件字段使用 RimUnity 类型。
- Unity 端完成字段绑定；层级路径相对于脚本实际所在节点。
- 加载、实例化、GetComponent 和组件修改都在游戏主线程执行，不放进后台 Task。
- 只操作实例，在禁用／销毁时解除事件订阅并停止需要结束的协程；地图退出时释放属于该地图的表现对象。
- 使用本文列出的接口，其他 Unity API 请先查阅 [支持范围](Status.md)。
