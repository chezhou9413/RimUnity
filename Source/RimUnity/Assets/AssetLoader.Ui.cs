using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUnity.Runtime;
using RimUnity.UI.Runtime;
using Verse;

namespace RimUnity.Assets;

//在主线程的加载阶段准备 UI 资源，首次打开只创建窗口实例。
public static partial class AssetLoader
{
    //沿清单依赖加载模板并上传纹理；不激活预制体，也不执行业务脚本。
    public static void PreloadUi(ModContentPack mod, params string[] paths)
    {
        NativeRuntime.AssertThread();
        AssetCatalog catalog = GetCatalog(CatalogLoadPolicy.RequireRoot(mod));
        var records = catalog.Manifest.assets.ToDictionary(a => a.id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool sdf = false;
        void Prepare(string id)
        {
            if (!visited.Add(id)) return;
            var record = records[id];
            foreach (string dependency in record.dependencies) Prepare(dependency);
            var asset = catalog.Get(id);
            if (asset is Texture texture && !texture.NativeTexture.EnsureResident())
                throw new InvalidOperationException("UI 预加载无法上传纹理：" + record.path);
            if (asset is UI.TMP_FontAsset) sdf = true;
        }
        foreach (string path in paths) Prepare(catalog.Find(path).id);
        PrepareShader("RimUnity_UI");
        if (sdf) PrepareShader("RimUnity_SDF");
    }

    //预编译通用 UI 阶段，避免首帧绘制时启动编译器。
    private static void PrepareShader(string name)
    {
        var shader = Shader.Find(name) ?? throw new InvalidOperationException("UI Shader 未注册：" + name);
        var material = new Material(shader);
        try { UiShaderStages.Prepare(material); }
        finally { RimUnity.Object.DestroyImmediate(material); }
    }
}
