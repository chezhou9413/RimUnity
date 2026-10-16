# SSR 资源导出兼容

2026-10-15，本地 SSR 统一 Unity 工程的 55 个业务预制体全部导出成功：15 个建筑、40 个 UGUI，另有 25 个动画片段、9 个控制器。582 个资源记录的载荷和依赖完整，55 个预制体通过静态字段／引用检查，462 个 TMP 文本未发现静态字形缺口。

基线：Unity 2022.3.35f1c1、TMP 3.0.6、RimWorld 1.7.4979 rev109；RimUnity HEAD c0e241a 加当前工作目录改动。既有粒子、动画、HDR 工作保留。未启动游戏，本说明中的“通过”指编译、导出及静态检查。

## Shader 与渲染

- UnityHlslCompat 补充 UnityViewToClipPos、UNITY_HALF_PI 和逆投影映射；ShaderGlobals.SetCamera 提供 _RimUnity_InvProjection。
- MutableShaderUniforms 对确实被赋值的参数生成私有副本，并在着色入口初始化，保留 Cg 单次调用内的参数改写。
- FragmentMatrices 将片元所需的相机／对象矩阵按列用 flat varying 传递，保持宿主描述符布局。
- TriangleEdgeLowering 将本批 Wireframe 的 geometry 程序转成顶点程序；Mesh.TriangleEdges 每源三角形生成 36 个顶点，随源网格更新重建缓存。保留宽度、端点、EWS 分支计算，EWS 开关两种编译均通过。
- 几何转换仅接受去除注释和空白后 SHA256 为 16ea4ed17bd237bc7028ee4f67b2da3ea912e7fccbd7c3675a4b21c71f581f42 的源程序。未知 geometry 程序继续拒绝，不能视为通用几何 Shader 支持。
- ShaderData 保存 depthCompare 与 geometry。ShaderDepthComparisons 仅为已注册程序转换反向 Z 比较，通过 Harmony 在 MeshPipelineCache 的 DepthCompareOp 写入处选择值。共享 Program 的 HDR 变体继承比较；静态检查核对唯一 IL 写入点，宿主变化时明确报错。
- 独立 Shader 导出排除 GrabPass、ShadowCaster 后确定颜色程序索引，WormHole 的 GrabPass 不再被误算为第二个颜色程序。

本批 ColorOnly、CutOut、DepthMaskForce、DepthMaskGreater、DepthMaskNormal、Add、JetFlow、Wireframe、Fade、WormHole 均通过转换。深度写入、遮挡、GrabPass、描边实际图像与性能仍需游戏验收。

## TMP 输入与文字

TmpInputWriter 和 ComponentWriter 导出标准单行 TMP 输入框，映射到 RimUnity.UI.TMP_InputField。控件提供焦点、光标、选择区、移动／删除、全选、复制剪切粘贴、Enter 提交、Escape 恢复和事件字段。TmpRenderer 使用原 SDF 字体绘制文字与编辑装饰。

输入框接收宿主已提交字符；IME 预编辑串、候选窗定位和组合态尚未接入。密码、多行、自定义验证、垂直滚动和移动软键盘事件明确阻止导出。动态输入字形仍须存在于导出图集或回退图集中。

TMP 字体读取按 Unicode 合并一致的重复记录，以 glyphIndex 查询字形，冲突与缺失明确报告字体和字符码。斜体使用源字体 italicStyle 和字形度量调整四角；U+200B 作为零宽断行控制字符，不要求图集。静态文本检查因此不再将输入框的 U+200B 判为缺字。

## SSR 专用组件和剩余工作

SSRBlackHole 的运行时适配位于 Mod 的 Source/SSR.RimUnity，生成 SSR.RimUnity.dll 随资源包交付。完整 Framework 的 net48 业务、资源加载、窗口接入和建筑逻辑尚未移植到 1.7。

当前 Def 另有 8 个界面没有同名源预制体，gulimSDF 源字体也缺失。55/55 只覆盖实际取得的业务资源，不能据此宣称当前发布版 SSR 全部功能已恢复。

- [完整结果与缺失清单](E:/RimModDev/SSR_SolarSystemRepublicCore/Docs/RimUnityCompatibility.md)
- [逐项导出结果](E:/ModdevMics/Outputs/SSR_Core_RimUnity/20261015-145723-All/prefab-export-report.json)
- [静态检查结果](E:/ModdevMics/Outputs/SSR_Core_RimUnity/20261015-145723-All/static-inspection.json)

修改框架后构建 Release 的 RimUnity.csproj 与 RimUnity.ShaderTool.csproj，再运行 E:/ModdevMics/Assets/SSR/Unity/SSR/Export-RimUnity.ps1。导出脚本自动编译 SSR 适配组件；检查脚本会加载其程序集核对预制体字段。
