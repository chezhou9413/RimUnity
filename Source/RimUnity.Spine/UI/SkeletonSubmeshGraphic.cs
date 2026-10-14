using RimUnity.UI;

namespace Spine.Unity;

//制作端拆分网格的占位组件；运行时由根 SkeletonGraphic 按槽顺序提交全部材质。
public sealed class SkeletonSubmeshGraphic : MeshGraphic
{
    protected override void PopulateMesh(UiMeshWriter output) { }
}
