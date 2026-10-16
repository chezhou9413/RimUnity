namespace RimUnity.Diagnostics;

//测试场复用现有探针，按会话身份停止，避免中断其他手动采集。
internal static partial class ParticleSpikeCapture
{
    internal static bool LabCaptureActive => current != null;
    internal static SpikeCaptureData BeginLabCapture()
    {
        if (current != null) return null;
        Start(60);
        return current;
    }

    internal static void EndLabCapture(SpikeCaptureData session)
    {
        if (session != null && ReferenceEquals(current, session)) Stop("离开特效性能测试场");
    }
}
