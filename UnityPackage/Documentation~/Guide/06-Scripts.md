# 游戏脚本与资源生命周期

## 制作端和游戏端的边界

制作端使用 Unity 原生类型。游戏端继承 RimUnity.MonoBehaviour，使用 RimUnity.GameObject、Transform、数学类型及组件，并编译为 .NET 10 程序集。导出器保存类型名、字段和事件引用，不会把第三方 Unity DLL 自动变成游戏代码。

保持完整类型名一致；需要改名时，在首次资源加载前使用 TypeResolver.Register 显式映射。可导出字段包括 public 和 SerializeField，SerializeReference 多态对象图不在支持范围内。

## 初始化顺序

先建立节点和组件，再绑定字段、对象引用和事件，最后恢复活动状态。Awake 可能在 Instantiate 返回前执行；需要注入业务参数时，把根节点保存为失活，调用自己的 Initialize 方法后再激活。

Awake 做必需引用检查；OnEnable 订阅事件；OnDisable 解除订阅并结束应停止的协程；OnDestroy 释放自己拥有的资源。不要依赖未约定的脚本执行顺序。

## 主线程与结束清理

加载、实例化、GetComponent、修改组件和释放资源都在游戏主线程进行。不要在 Task 或后台线程操作这些对象。

RimUnity 的 Update/FixedUpdate 是运行时调度，并不等同 RimWorld Tick。需要受游戏暂停和地图生命周期约束的效果，应由业务明确同步状态。

退出地图时清理属于该地图的实例；不要让静态集合长期持有旧地图或对象。共享材质、贴图和模板由加载器缓存，使用方不应销毁它们。当前没有资源热重载和公开卸载接口。

查找层级、字段绑定、协程、事件和常用组件的完整示例见原文“MonoBehaviour 与常用方法”和“API 快速接入”。
