namespace RimUnity.UI;

//供动态生成列表的模组立即刷新布局。
public static class LayoutRebuilder
{
    //同步完成当前子树的测量和位置分配。
    public static void ForceRebuildLayoutImmediate(RectTransform rect) => Runtime.UguiDirty.Force(rect);
    //自定义布局组件在自身数据变化后请求下次画布布局。
    public static void MarkLayoutForRebuild(RectTransform rect) => Runtime.UguiDirty.Mark(rect);
}
