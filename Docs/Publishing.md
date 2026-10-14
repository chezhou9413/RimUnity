# 明文资源构建管理器

[简体中文](Publishing.md) | [English](en/Publishing.md)

Unity 菜单：`RimUnity / 明文资源构建管理器`。通过本地 UPM 包安装时，窗口直接使用包内源码，无需复制脚本到 Assets。

配置保存在制作项目的 `ProjectSettings/RimUnity.json`，UI 标记保存在 `ProjectSettings/RimUnityWindow.json`。首次打开无需配置文件；在“输出与工具配置”设置内容仓库、ShaderTool、游戏目录和 AB 标签对应的 Mod 根目录，然后保存。项目配置不应提交到 RimUnity 框架仓库。

入口来自配置的 AB 标签和手动加入的资源；贴图、材质、Shader 和动画依赖自动收集。输出位置为目标 Mod 根目录下的 `1.7/Data/RimUnity`。报告写入制作项目的 `Library/RimUnity/Reports`。

完整重建包含当前配置中的入口，会在全部目标完成导出与结构验证后替换旧数据；希望保留其他入口时使用增量模式。搜索和问题筛选只影响列表显示，不限制导出范围；不想发布的资源需移出 AB 标签或手动入口。检查失败、导出失败或准备阶段取消不会替换正式数据。发布阶段失败会恢复此前版本。结构验证不代表游戏内视觉验收通过。

同一目标 Mod 的入口在同一批次导出，按资源 ID 复用贴图、材质、网格和 Shader 变体。发布前自动只保留清单入口及其依赖，去掉未引用的历史文件；内容相同的附属文件存入 `blobs/`，只保留一份。增量模式也会整理目录，但保留未重新导出的公开入口。

JSON 保持明文，省略缩进和空字段，保留显式零值、false、粒子参数与资源精度。文件去重不会合并不同资源 ID 的对象身份，也不会降低贴图分辨率。单个导出窗口与逐项粒子导出保留历史目录；这些结果可用[发布副本 API](ResourceLoading.md#发布内容资源)整理后再分发。

缺失脚本会作为警告写入预制体，游戏载入时会报告；其他可导出组件仍可使用。Shader Graph 和需要另行编译的脚本资源会在列表中提示，移出这些入口后即可导出其余内容。

## Renderer 导出设置

窗口的输出与工具配置页提供四个独立选项：自动关闭 Cast Shadows、接收阴影、光照探针、反射探针。默认均关闭；保存到制作项目的 `ProjectSettings/RimUnity.json`。开启的选项仅作用于导出副本，记录资源、层级、组件及原值。源 prefab 和模型资源不保存修改；反射探针开关不改变材质的 Cubemap。关闭开关时仍执行原有严格检查。

程序调用可使用 `PrefabExporter.Export(modDirectory, new ExportOptions { disableCastShadows = true }, requests)`。原有 Export 重载保持严格模式，不使用静态全局开关。
