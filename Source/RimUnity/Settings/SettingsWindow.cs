using System;
using System.IO;
using UnityEngine;
using Verse;
using static RimUnity.Settings.InspectorLayout;

namespace RimUnity.Settings;
using Rect = UnityEngine.Rect;
using Vector2 = UnityEngine.Vector2;

//三个页面共用导航和滚动视图，保留游戏原生输入与缩放行为。
internal static class SettingsWindow
{
    private static int page;
    private static Vector2 scroll;
    private static float contentHeight = 1000;
    private static string licenseText;
    private static bool runtimeDetails = true, community = true, licenses = true;

    //所有绘制路径统一恢复 IMGUI 全局状态。
    internal static void Draw(Rect rect, Rendering.BloomSettings bloom)
    {
        var font = Text.Font;
        var anchor = Text.Anchor;
        var color = GUI.color;
        bool wrap = Text.WordWrap, enabled = GUI.enabled, scrolling = false;
        try
        {
            Text.Font = GameFont.Small;
            Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = UnityEngine.Color.white;
            Widgets.DrawBoxSolid(rect, Background);
            float navigation = Mathf.Min(180, rect.width * .29f);
            var nav = new Listing_Standard();
            nav.Begin(new Rect(rect.x + 8, rect.y + 8, navigation - 16, rect.height - 16));
            nav.Label("RimUnity");
            nav.Label("0.1.9 Preview");
            nav.Gap(12);
            string[] titles = { L("渲染设置", "Rendering"), L("运行时", "Runtime"), L("关于我们", "About us") };
            for (int i = 0; i < titles.Length; i++)
            {
                Rect row = nav.GetRect(Height(titles[i], nav.ColumnWidth));
                if ((page == 3 ? 2 : page) == i) Widgets.DrawBoxSolid(row, Selection);
                Widgets.Label(new Rect(row.x + 8, row.y + 6, row.width - 16, row.height - 6), titles[i]);
                if (Widgets.ButtonInvisible(row)) { page = i; scroll = Vector2.zero; }
                nav.Gap(4);
            }
            nav.End();
            var body = new Rect(rect.x + navigation + 8, rect.y + 8, Mathf.Max(1, rect.width - navigation - 16), rect.height - 16);
            var view = new Rect(0, 0, Mathf.Max(1, body.width - 20), Mathf.Max(body.height, contentHeight));
            Widgets.BeginScrollView(body, ref scroll, view);
            scrolling = true;
            var list = new Listing_Standard();
            list.Begin(view);
            if (page == 0) bloom.DrawContent(list);
            else if (page == 1) Runtime(list);
            else if (page == 2) About(list);
            else
            {
                if (Button(list, L("返回关于我们", "Back to About"))) { page = 2; scroll = Vector2.zero; }
                list.Gap(8);
                list.Label(licenseText);
            }
            contentHeight = list.CurHeight + 16;
            list.End();
        }
        finally
        {
            if (scrolling) Widgets.EndScrollView();
            Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap;
            GUI.color = color; GUI.enabled = enabled;
        }
    }

    //展示实际选中结果，不联网查询版本。
    private static void Runtime(Listing_Standard list)
    {
        list.Label(L("当前运行时", "Active runtime"));
        if (!Group(list, L("加载状态", "Load status"), ref runtimeDetails)) return;
        Value(list, L("版本", "Version"), RuntimeInfo.Version);
        Value(list, L("来源", "Source"), RuntimeInfo.IsStandalone ? L("独立前置", "Standalone mod") : L("内置运行时", "Embedded runtime"));
        Value(list, L("提供方", "Provider"), RuntimeInfo.Provider);
        Value(list, L("内置副本数", "Embedded copies"), RuntimeInfo.EmbeddedCopies.ToString());
        list.Label(RuntimeInfo.IsStandalone
            ? L("独立前置优先。内置载荷均不挂载、不初始化。", "Standalone takes priority. Embedded payloads are not mounted or initialized.")
            : L("仅选中的一份运行时和资源生效，其他副本不重复运行。", "Only the selected runtime and resources are active. Other copies do not run again."));
        if (!string.IsNullOrEmpty(RuntimeInfo.PayloadHash)) Value(list, "SHA-256", RuntimeInfo.PayloadHash);
        Value(list, L("资源目录", "Resources"), RuntimeInfo.ResourceRoot);
        list.Label(L("本地选择，无联网检测或自动更新；支持离线游玩。", "Local selection only. No online checks or automatic updates; offline play is supported."));
    }

    //贡献者随发布维护，外链只在玩家点击后打开。
    private static void About(Listing_Standard list)
    {
        list.Label("RimUnity · 0.1.9 Preview");
        list.Label(L("RimUnity 基于 RimWorld 的 Waylith 引擎提供兼容接口，不包含、也不另行加载一份真正的 Unity 引擎。",
            "RimUnity provides compatibility APIs on RimWorld's Waylith engine. It does not include or load a separate, real Unity engine."));
        if (Group(list, L("社区与贡献者", "Community & contributors"), ref community))
        {
            list.Label("chezhou9413 · keeptpa · Karl-441");
            list.Label(L("欢迎新的维护者、贡献者和内容 Mod 作者加入我们，一起改进兼容性、工具和文档。",
                "New maintainers, contributors and content mod authors are welcome. Join us to improve compatibility, tools and documentation."));
            if (Button(list, L("GitHub 仓库", "GitHub repository"))) Application.OpenURL("https://github.com/chezhou9413/RimUnity");
            if (Button(list, L("Steam 创意工坊", "Steam Workshop"))) Application.OpenURL("https://steamcommunity.com/sharedfiles/filedetails/?id=3819570122");
        }
        if (Group(list, L("开源许可", "Open-source licenses"), ref licenses))
        {
            list.Label(L("RimUnity 自有代码采用 MIT 协议。第三方代码保留各自许可；Spine Runtime 受独立条款约束。",
                "RimUnity's original code is MIT licensed. Third-party code retains their own licenses; Spine Runtime has separate terms."));
            if (Button(list, L("阅读 MIT 完整许可", "Read full MIT license"))) OpenLicense("LICENSE");
            if (Button(list, L("第三方声明与完整许可", "Third-party notices & full licenses")))
                OpenLicense("ThirdParty/NOTICE.md", "ThirdParty/UGUI/LICENSE", "ThirdParty/Spine-NOTICE.txt", "ThirdParty/Spine-LICENSE.txt");
        }
    }

    //许可随包携带，可在离线环境直接阅读。
    private static void OpenLicense(params string[] paths)
    {
        try { licenseText = string.Join("\n\n", Array.ConvertAll(paths, path => path + "\n\n" + File.ReadAllText(Path.Combine(RuntimeInfo.LicenseRoot, path)))); }
        catch (IOException error) { licenseText = L("无法读取许可文件：", "Cannot read license file: ") + error.Message; Log.Error("[RimUnity] " + error); }
        page = 3;
        scroll = Vector2.zero;
    }
}
