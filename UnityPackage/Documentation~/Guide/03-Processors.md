# 给导出挂处理函数

处理器适合“制作时保留一种设置，导出时使用另一种设置”。它会应用到直接导出的资源和递归依赖，包含 Prefab、材质、纹理和其他受支持的 Unity 资源。

## 最常见的例子：关闭 Single Channel 后导出

把以下脚本放在自己工程的 Assets/Editor。使用独立 asmdef 时引用 RimUnity.Editor。

```csharp
using RimUnity.Editor;
using UnityEngine;

[RimUnityExportProcessor]
public sealed class MyMaterialProcessor : ExportProcessor
{
    public override bool Matches(ExportContext c)
        => c.ExportAsset is Material m && m.shader != null
        && m.shader.name == "Cartoon FX/Remaster/Particle Ubershader"
        && m.HasProperty("_SingleChannel") && m.GetFloat("_SingleChannel") > 0;

    public override void BeforeExport(ExportContext c)
    {
        var copy = c.CreateExportCopy<Material>();
        copy.SetFloat("_SingleChannel", 0);
    }

    public override void AfterExport(ExportContext c)
        => Debug.Log("已写出 " + c.OutputFile);
}
```

带特性的处理器由各个导出入口自动发现，每批创建新实例。这里修改的是临时材质副本，结束后自动销毁，原材质始终保留原设置。框架本身不会默认开启这个例子。

Single Channel 决定贴图通道解释。关闭后请确认源贴图的 Alpha，不能只以成功导出判断画面正确。

## 前处理、后处理、清理分别做什么

- Matches：按类型、SourcePath 或当前输入判断是否参与。
- BeforeExport：在属性读取、Shader 编译和依赖收集前调整输入。
- AfterExport：在当前资源文件写出后检查或记录结果，此时 catalog 尚未提交。
- DeferCleanup：无论成功还是失败，最后都执行的恢复操作。

修改原资源时先读取原值，再登记恢复，最后才修改。恢复不要写在 AfterExport，因为导出失败时它不会运行。CreateExportCopy 已登记销毁，无需再手动释放副本。

## 处理顺序与共享依赖

Order 越小越先执行；前后处理按同一顺序，清理逆序。同一事务内共享资源的同一变体只处理一次，因此不要按引用者给同一共享材质设置相互矛盾的参数。世界/UI/相机 UI 变体分别处理。

需要只对本次脚本调用生效时，把 DelegateExportProcessor 放进 ExportOptions.processors。discoverProcessors=false 可关闭本批的自动发现。详细函数挂载示例与异常约定见原文区“导出前后处理”。

处理器只在 Unity 编辑器中同步运行，不随资源进入游戏。不要在其中递归导出、保存原资产或在后台线程访问 Unity 对象。导出器生成的 Shader 变体和内容哈希文件跟随源资源处理，不另触发一次源资源回调。
