# 明文资源构建管理器

Unity 菜单：`RimUnity / 明文资源构建管理器`。通过本地 UPM 包安装时，窗口直接使用包内源码，无需复制脚本到 Assets。

配置保存在制作项目的 `ProjectSettings/RimUnity.json`，选择和 UI 标记保存在 `ProjectSettings/RimUnityWindow.json`。首次打开无需配置文件；在“输出与工具配置”设置内容仓库、ShaderTool、游戏目录和 AB 标签对应的 Mod 根目录，然后保存。项目配置不应提交到 RimUnity 框架仓库。

入口来自配置的 AB 标签和手动加入的资源；贴图、材质、Shader 和动画依赖自动收集。输出位置为目标 Mod 根目录下的 `1.7/Data/RimUnity`。报告写入制作项目的 `Library/RimUnity/Reports`。

完整重建仅包含本次选中的入口，会在全部目标完成导出与结构验证后替换旧数据；希望保留其他入口时使用增量模式。检查失败、导出失败或准备阶段取消不会替换正式数据。发布阶段失败会恢复此前版本。结构验证不代表游戏内视觉验收通过。

缺失脚本会作为警告写入预制体，游戏载入时会报告；其他可导出组件仍可使用。Shader Graph 和需要另行编译的脚本资源会在列表中提示，移出这些入口后即可导出其余内容。

发布器测试使用模拟导出器，不会导出制作项目资源：

```powershell
dotnet run --project Tools/PublishingTests/PublishingTests.csproj -p:UnityEditorDir="<Unity installation>/Editor/Data"
```

测试覆盖完整替换、增量保留、取消、多目标失败恢复、并发发布锁，以及数据和 shader 文件路径验证。

## Renderer 导出设置

窗口的输出与工具配置页提供四个独立选项：自动关闭 Cast Shadows、接收阴影、光照探针、反射探针。默认均关闭；保存到制作项目的 `ProjectSettings/RimUnity.json`。开启的选项仅作用于导出副本，记录资源、层级、组件及原值。源 prefab 和模型资源不保存修改；反射探针开关不改变材质的 Cubemap。关闭开关时仍执行原有严格检查。

程序调用可使用 `PrefabExporter.Export(modDirectory, new ExportOptions { disableCastShadows = true }, requests)`。原有 Export 重载保持严格模式，不使用静态全局开关。
