namespace RimUnity.Rendering;

//主线程上把同帧地图快照交给后处理捕获；不消费未来帧，避免晚到的 PostFx 偷走下一帧地图。
internal sealed class BloomMapHandoff<T> where T : class
{
    private T pending;
    private int pendingFrame = int.MinValue;
    private string lastMiss;
    private int offers, takes, misses;

    internal string LastMiss => lastMiss;
    internal int Offers => offers;
    internal int Takes => takes;
    internal int Misses => misses;
    internal int PendingFrame => pending == null ? int.MinValue : pendingFrame;

    internal void Offer(int frameNumber, T map)
    {
        if (map == null) return;
        offers++;
        if (pending != null && pendingFrame != frameNumber)
            Note($"replaced-unconsumed map={pendingFrame} with={frameNumber}");
        pending = map;
        pendingFrame = frameNumber;
    }

    internal bool TryTake(int frameNumber, out T map, out string miss)
    {
        if (pending == null)
        {
            map = null;
            miss = Note("missing-map");
            return false;
        }

        if (pendingFrame != frameNumber)
        {
            //未来帧留给匹配的 PostFx；过期帧则丢弃。
            if (pendingFrame > frameNumber)
            {
                map = null;
                miss = Note($"stale-postfx waiting-map={pendingFrame} postfx={frameNumber}");
                return false;
            }

            int stale = pendingFrame;
            pending = null;
            pendingFrame = int.MinValue;
            map = null;
            miss = Note($"stale-map map={stale} postfx={frameNumber}");
            return false;
        }

        map = pending;
        pending = null;
        pendingFrame = int.MinValue;
        takes++;
        miss = null;
        lastMiss = null;
        return true;
    }

    private string Note(string reason)
    {
        misses++;
        lastMiss = reason;
        return reason;
    }
}
