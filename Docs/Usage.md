# 类型、目录与脚本用法

一个内容模组的清单位于 `1.7/Data/RimUnity/catalog.json`。逻辑路径与磁盘文件路径分离，`PrefabLoader.Load(contentMod, path)` 总是从指定模组解析，不搜索其它模组补齐缺失资源。

推荐使用明确别名：

```csharp
using RimUnity;
using RectTransform = RimUnity.RectTransform;
using Canvas = RimUnity.Canvas;
using Button = RimUnity.UI.Button;
using Slider = RimUnity.UI.Slider;
using Text = RimUnity.UI.Text;
```

GameObject、Transform、MonoBehaviour、Vector、Quaternion、Color、Mesh、Material 和协程均使用 RimUnity 类型；事件使用 `RimUnity.Events`。游戏侧至少引用 `RimUnity.Core.dll` 和 `RimUnity.dll`，只随模组分发自己的业务 DLL。UGUI 控件使用 `RimUnity.UI`；鼠标接口使用 `RimUnity.EventSystems`。动态创建矩形对象使用 `RimUnity.Ugui.CreateObject(name, componentTypes)`。

自定义脚本保持完整类型名时，重编译后可直接绑定。类型重命名时，在加载前调用 `TypeResolver.Register("作者工程中的完整类型名", typeof(新类型))`。不要同时加载两个相同完整类型名的脚本程序集；加载器会拒绝歧义。

UnityEvent 支持动态零到四个参数和 int、float、bool、string、Object 常量参数。事件目标必须位于同一个导出 Prefab 内；脚本方法必须存在于游戏侧程序集。

加载顺序是完整建树→添加组件→填充字段→绑定引用／事件→激活。创建过程延迟生命周期回调，Awake、OnEnable、Start 和协程由 RimUnity 调度。模板保持失活，避免资源模板执行脚本。

当前 `Instantiate` 的正式入口为加载得到的模板。**不支持运行实例再次克隆、局部子树克隆、跨 Prefab 对象引用及动态热重载**。不要把缓存模板用于实际场景，实例之间通过 `Instantiate(template)` 隔离。

默认字体映射到游戏字体索引 1；需要指定字形覆盖范围时应由内容模组提供有分发授权的源字体。框架不附带字体素材。

使用 `SetTextureScale` 和 `SetTextureOffset` 设置纹理变换；直接设置 `_MainTex_ST` 时，传入 `(scale.x, scale.y, offset.x, offset.y)`。

游戏端与制作端的脚本保留相同完整类型名，通过编译符号分别引用 `RimUnity` 或 `UnityEngine`。资源由配套 UPM 包导出。游戏的 `Thing.DrawPos` 等返回值需要按分量转换成 RimUnity 数学类型。
