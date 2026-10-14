using System;
using System.Collections.Generic;

namespace RimUnity;

public enum ParticleSystemOverlapAction { Ignore, Kill, Callback }
public enum ParticleSystemTriggerEventType { Inside, Outside, Enter, Exit }
public enum ParticleSystemColliderQueryMode { Disabled, One, All }

//触发器状态按稳定粒子身份保存，数组压缩和 SetParticles 不会串用其他粒子的进出状态。
public sealed partial class ParticleSystem
{
    public TriggerModule trigger = new();
    private readonly Dictionary<ulong, List<Collider>> triggerStates = new();
    private readonly List<TriggerRecord>[] triggerEvents = { new(), new(), new(), new() };
    private readonly Dictionary<ulong, int>[] triggerIndices = { new(), new(), new(), new() };

    //Trigger 允许普通碰撞器参与，不要求 isTrigger=true，也不需要刚体。
    public sealed class TriggerModule
    {
        public bool enabled;
        public ParticleSystemOverlapAction inside, outside, enter, exit;
        public ParticleSystemColliderQueryMode colliderQueryMode;
        public float radiusScale = 1;
        public Collider[] colliders = Array.Empty<Collider>();
        public int colliderCount => colliders.Length;
        public void SetCollider(int index, Component component)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (component != null && component is not Collider) throw new NotSupportedException("Trigger 只接受已导入的桥接碰撞器。");
            if (component is TerrainCollider) throw new NotSupportedException("TerrainCollider 是高度表面，不能作为粒子 Trigger 体积。");
            if (index >= colliders.Length) Array.Resize(ref colliders, index + 1);
            colliders[index] = (Collider)component;
        }
        public Component GetCollider(int index) => colliders[index];
        public void AddCollider(Component component) => SetCollider(colliders.Length, component);
        public void RemoveCollider(int index)
        {
            var values = new List<Collider>(colliders); values.RemoveAt(index); colliders = values.ToArray();
        }
    }

    //一份 ColliderData 与同次 GetTriggerParticles 返回的粒子列表一一对应。
    public readonly struct ColliderData
    {
        private readonly Component[][] contacts;
        internal ColliderData(Component[][] contacts) => this.contacts = contacts;
        public int GetColliderCount(int particleIndex) => contacts[particleIndex].Length;
        public Component GetCollider(int particleIndex, int colliderIndex) => contacts[particleIndex][colliderIndex];
    }
    private struct TriggerRecord { internal Particle particle; internal Component[] colliders; }

    //跨帧保留重叠状态，只清掉本批次事件。
    private void ClearTriggerEvents()
    {
        for (int i = 0; i < triggerEvents.Length; i++) { triggerEvents[i].Clear(); triggerIndices[i].Clear(); }
        if (!trigger.enabled) triggerStates.Clear();
        else foreach (Collider collider in trigger.colliders)
            if (collider is TerrainCollider) throw new NotSupportedException("TerrainCollider 是高度表面，不能作为粒子 Trigger 体积。");
    }

    //进出按全部指定体积的并集判断，重叠区域内部换碰撞器不会重复触发 Enter。
    private void ProcessTriggers(ref Particle particle, float eventOffset)
    {
        triggerStates.TryGetValue(particle.identity, out List<Collider> before);
        var current = new List<Collider>();
        float radius = PhysicsRadius(particle, trigger.radiusScale);
        Vector3 point = WorldPosition(particle);
        foreach (Collider collider in trigger.colliders)
            if (ParticlePhysicsWorld.Queryable(collider) && collider.Shape.Distance(point, out _) <= radius && !current.Contains(collider)) current.Add(collider);
        bool wasInside = before is { Count: > 0 }, isInside = current.Count > 0;
        bool callback = false;
        if (isInside && !wasInside) callback |= TriggerAction(ref particle, ParticleSystemTriggerEventType.Enter, trigger.enter, current);
        if (!isInside && wasInside) callback |= TriggerAction(ref particle, ParticleSystemTriggerEventType.Exit, trigger.exit, before);
        callback |= TriggerAction(ref particle, isInside ? ParticleSystemTriggerEventType.Inside : ParticleSystemTriggerEventType.Outside,
            isInside ? trigger.inside : trigger.outside, current);
        if (isInside) triggerStates[particle.identity] = current;
        else triggerStates.Remove(particle.identity);
        if (callback) SubEmit(particle, 3, eventOffset);
    }

    //Callback 保存可回写的粒子快照；同一帧多个小步只保留该事件类型的最新快照。
    private bool TriggerAction(ref Particle particle, ParticleSystemTriggerEventType type, ParticleSystemOverlapAction action, List<Collider> colliders)
    {
        switch (action)
        {
            case ParticleSystemOverlapAction.Ignore: return false;
            case ParticleSystemOverlapAction.Kill: particle.remainingLifetime = 0; particle.physicsKilled = true; return false;
            case ParticleSystemOverlapAction.Callback: break;
            default: throw new NotSupportedException("不支持的 Trigger 动作：" + action);
        }
        int number = trigger.colliderQueryMode switch
        {
            ParticleSystemColliderQueryMode.Disabled => 0,
            ParticleSystemColliderQueryMode.One => Math.Min(1, colliders.Count),
            ParticleSystemColliderQueryMode.All => colliders.Count,
            _ => throw new NotSupportedException("不支持的 Trigger 查询模式：" + trigger.colliderQueryMode)
        };
        var contacts = new Component[number];
        for (int i = 0; i < number; i++) contacts[i] = colliders[i];
        int kind = (int)type;
        var record = new TriggerRecord { particle = particle, colliders = contacts };
        if (triggerIndices[kind].TryGetValue(particle.identity, out int index)) triggerEvents[kind][index] = record;
        else { triggerIndices[kind].Add(particle.identity, triggerEvents[kind].Count); triggerEvents[kind].Add(record); }
        return true;
    }

    private void DispatchTriggerEvents()
    {
        foreach (List<TriggerRecord> events in triggerEvents)
            if (events.Count > 0) { ParticlePhysicsWorld.Send(gameObject, "OnParticleTrigger"); break; }
    }

    //读取时刷新仍存活粒子的快照，避免 Enter 发生在前一小步后把位置写回旧时间。
    public int GetTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> results)
        => GetTriggerParticles(type, results, out _);
    public int GetTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> results, out ColliderData colliderData)
    {
        ArgumentNullException.ThrowIfNull(results); results.Clear();
        List<TriggerRecord> records = TriggerRecords(type);
        var current = new Dictionary<ulong, Particle>();
        for (int i = 0; i < count; i++) current[particles[i].identity] = particles[i];
        var contacts = new Component[records.Count][];
        for (int i = 0; i < records.Count; i++)
        {
            TriggerRecord record = records[i];
            results.Add(current.TryGetValue(record.particle.identity, out Particle value) ? value : record.particle);
            contacts[i] = record.colliders;
        }
        colliderData = new ColliderData(contacts);
        return results.Count;
    }

    //按事件快照身份回写，不通过随机种子匹配；已经死亡的粒子不能重新复活。
    public void SetTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var allowed = triggerIndices[(int)CheckedTriggerType(type)];
        var replacements = new Dictionary<ulong, Particle>();
        foreach (Particle value in values)
        {
            if (!allowed.ContainsKey(value.identity)) throw new ArgumentException("只能回写本次 Trigger 返回的粒子。", nameof(values));
            replacements[value.identity] = value;
        }
        for (int i = count - 1; i >= 0; i--)
        {
            if (!replacements.TryGetValue(particles[i].identity, out Particle value)) continue;
            if (value.remainingLifetime <= 0) { ReleaseParticle(value.identity); particles[i] = particles[--count]; }
            else particles[i] = value;
        }
    }
    private List<TriggerRecord> TriggerRecords(ParticleSystemTriggerEventType type) => triggerEvents[(int)CheckedTriggerType(type)];
    private static ParticleSystemTriggerEventType CheckedTriggerType(ParticleSystemTriggerEventType type)
        => (uint)type < 4 ? type : throw new ArgumentOutOfRangeException(nameof(type));

    //Clear 与所有粒子移除路径使用同一清理入口。
    private void ReleasePhysicsParticle(ulong identity) => triggerStates.Remove(identity);
    private void ClearPhysicsState() { triggerStates.Clear(); collisionEvents.Clear(); ClearTriggerEvents(); }
}
