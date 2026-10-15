# 导出前后处理

处理器在 Unity 编辑器中执行，覆盖 Prefab、独立资源及递归发现的材质、贴图等依赖。导出器先确定源资源身份，再运行前处理，随后读取属性、收集依赖、编译 Shader、写出文件，最后调用后处理并清理临时状态。

## 给所有导出入口安装处理器

将下面脚本放在自己工程的 `Assets/Editor/DisableSingleChannelForExport.cs`。使用 asmdef 时，该 Editor 程序集需引用 `RimUnity.Editor`。带 `[RimUnityExportProcessor]` 的公开具体类需要公开无参构造，会在每批导出开始时实例化；导出窗口、构建管理器、粒子批处理和脚本入口均会发现它。

```csharp
using RimUnity.Editor;
using UnityEngine;

[RimUnityExportProcessor]
public sealed class DisableSingleChannelForExport : ExportProcessor
{
    public override bool Matches(ExportContext context)
    {
        var material = context.ExportAsset as Material;
        return material != null && material.shader != null
            && material.shader.name == "Cartoon FX/Remaster/Particle Ubershader"
            && material.HasProperty("_SingleChannel")
            && material.GetFloat("_SingleChannel") > 0f;
    }

    public override void BeforeExport(ExportContext context)
    {
        Material copy = context.CreateExportCopy<Material>();
        copy.SetFloat("_SingleChannel", 0f);
    }

    public override void AfterExport(ExportContext context)
    {
        Debug.Log("已导出：" + context.SourcePath + " → " + context.OutputFile);
    }
}
```

`CreateExportCopy<T>()` 复制当前输入，保留原资源的 GUID/local ID，并登记副本销毁。导出数据和依赖来自副本；原材质的 Single Channel 始终保留原值。这个示例改变导出后的通道解释，使用前需确认贴图的 Alpha 符合预期。

这是可选示例，框架不会默认关闭任何材质的 Single Channel。该 Shader 实际使用 `_SingleChannel` 数值判断，不能只关闭同名关键字。

## 只给一次脚本导出挂函数

```csharp
var options = new RimUnity.Editor.ExportOptions
{
    //false 表示本次不自动发现带特性的处理器。
    discoverProcessors = false,
    processors = new RimUnity.Editor.IExportProcessor[]
    {
        new RimUnity.Editor.DelegateExportProcessor
        {
            filter = context => context.ExportAsset is UnityEngine.Material,
            before = context =>
            {
                var copy = context.CreateExportCopy<UnityEngine.Material>();
                if (copy.HasProperty("_SingleChannel")) copy.SetFloat("_SingleChannel", 0f);
            },
            after = context => UnityEngine.Debug.Log(context.OutputFile)
        }
    }
};
RimUnity.Editor.PrefabExporter.Export(modDirectory, options, requests);
```

显式处理器会与自动发现的处理器合并；若二者重复配置同一逻辑，会重复执行。列表在批次开始时复制，后续修改数组不会影响当前批次。处理器实例本身不克隆，不要在导出中修改它的配置。

## 需要原地修改时如何恢复

优先修改副本。必须修改原资源时，先登记恢复，再修改；不要调用 SaveAssets、SetDirty 或 SaveAndReimport。

```csharp
var material = (UnityEngine.Material)context.ExportAsset;
float previous = material.GetFloat("_SingleChannel");
context.DeferCleanup(() => material.SetFloat("_SingleChannel", previous));
material.SetFloat("_SingleChannel", 0f);
```

恢复的是原值，不是一律打开。清理在 `finally` 中执行，覆盖前处理、导出、后处理抛出的异常（包括取消异常）。清理函数逆序执行；某一个失败不会跳过剩余函数。强制结束 Unity 进程无法运行 finally，因此副本方式更适合通用扩展。

## 上下文与执行约定

- `Source`、`SourcePath`：原资源及工程路径，用于筛选和诊断。不要销毁 Source。
- `ExportAsset`：实际读取的资源。可替换为同类型副本，所有权由处理器负责；`CreateExportCopy` 会自动管理。
- Prefab 的 `ExportAsset` 一开始就是加载出的临时内容或导入模型副本。Prefab 处理器可直接修改此对象；Source 仍是原工程资产。
- `ResourceId`：源身份及导出模式决定的 ID。单独 Shader 的 ID 依赖编译结果，前处理时为空，后处理时可读。
- `ReferencePath`：触发导出的逻辑路径或组件引用路径；共享依赖只保留第一次访问路径。不要根据不同引用者对同一共享材质做互相矛盾的修改。
- `IsUI`、`IsCameraUI`：当前导出变体。世界、UI、相机 UI 变体分别处理。
- `OutputDirectory`：本批次工作目录；`OutputFile` 在后处理中指向已写出的主描述文件。该文件仍属于尚未提交的导出事务。
- `Succeeded`：已完成资源写出；后处理运行时为 true。后处理或清理失败会使整次资源导出失败。`Error` 可在失败清理中读取；它不表示 catalog 已发布。

按 `Order` 从小到大执行，同 Order 按处理器完整类型名排序，同类型保持配置顺序。所有前处理完成后才写资源，成功后按相同顺序执行后处理，最后逆序清理。

任一前处理或后处理失败，当前导出不提交新 catalog，并保留 FAILED.txt。后处理仅用于成功写出的资源；恢复操作不要放在 AfterExport 中。不要在回调中递归启动新的导出或写入其他已发布文件，外部副作用不参与清单回滚。

同一批次内相同资源变体只处理一次，命中依赖缓存时不重复回调。AB 构建管理器可能按入口执行多个导出事务，各事务独立处理。由导出器合成的 Shader 程序、默认法线贴图与内容哈希文件不是独立 Unity 源资源，不额外触发回调；它们会使用已处理材质的状态生成。

所有回调均同步运行在导出调用线程；入口应由 Unity 编辑器主线程调用。不要通过 async void 或后台 Task 修改 Unity 对象。

## 百科全书

通过 `RimUnity → 百科全书` 或 `Tools → RimUnity → 百科全书` 打开离线手册。说明书按任务重写；原文区保留仓库 README 和中英文文档。搜索同时检查标题、来源路径和全文。维护文档后运行 `Tools/Sync-Manual.ps1`，打包脚本也会同步；`-Check` 只检查同步状态。
