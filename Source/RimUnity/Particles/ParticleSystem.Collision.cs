using System;
using System.Collections.Generic;

namespace RimUnity;

public enum ParticleSystemCollisionQuality { High, Medium, Low }

//事件保存接触前的世界速度，供脚本获取该次真实碰撞。
public struct ParticleCollisionEvent
{
    public Vector3 intersection { get; internal set; }
    public Vector3 normal { get; internal set; }
    public Vector3 velocity { get; internal set; }
    public Component colliderComponent { get; internal set; }
}

//粒子碰撞针对明确导入的物理对象，不凭空将 RimWorld 地图格子视为墙体。
public sealed partial class ParticleSystem
{
    public CollisionModule collision = new();
    private readonly List<ParticleCollisionEvent> collisionEvents = new();
    private readonly List<(Collider collider, ParticlePhysicsShape shape)> collisionCandidates = new();

    //曲线由粒子的当前寿命取值；平面列表保留 Transform 引用。
    public sealed class CollisionModule
    {
        public bool enabled, sendCollisionMessages, enableDynamicColliders = true;
        public ParticleSystemCollisionType type;
        public ParticleSystemCollisionMode mode;
        public ParticleSystemCollisionQuality quality;
        public MinMaxCurve dampen = 0, bounce = 1, lifetimeLoss = 0;
        public float minKillSpeed, maxKillSpeed = 10000, radiusScale = 1, colliderForce;
        public bool multiplyColliderForceByCollisionAngle = true, multiplyColliderForceByParticleSpeed = true, multiplyColliderForceByParticleSize = true;
        public int collidesWith = -1, maxCollisionShapes = 256;
        public Transform[] planes = Array.Empty<Transform>();
        public int planeCount => planes.Length;
        public void SetPlane(int index, Transform value)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= planes.Length) Array.Resize(ref planes, index + 1);
            planes[index] = value;
        }
        public Transform GetPlane(int index) => planes[index];
    }

    //每个协调推进批次只清一次事件，帧内细分步骤共同积累结果。
    private void BeginPhysicsEvents()
    {
        collisionEvents.Clear(); ClearTriggerEvents();
        //Medium/Low 使用相同高精度查询，未启用 Unity 的体素缓存性能优化。
    }

    //每个子步在粒子积分前准备几何，上一系统的停止回调仍能影响下一系统。
    private void PrepareCollisionCandidates()
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.PhysicsCandidates);
#endif

        collisionCandidates.Clear();
        //This step may spawn its first particles after candidate preparation.
        if (!collision.enabled || collision.type == ParticleSystemCollisionType.Planes) return;
        foreach (Collider collider in ParticlePhysicsWorld.colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger
                || (collision.collidesWith & (1 << collider.gameObject.layer)) == 0
                || collider.Is2D != (collision.mode == ParticleSystemCollisionMode.Collision2D)) continue;
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && (!body.detectCollisions || !collision.enableDynamicColliders && !body.isKinematic)) continue;
            collisionCandidates.Add((collider, collider.Shape));
        }
    }

    //碰撞在移动后、拖尾记录与死亡事件前处理；触发模块随后读取最终位置。
    private void ApplyParticlePhysics(ref Particle particle, Vector3 from, float dt, float birthOffset)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.Physics);
#endif

        if (collision.enabled && dt > 0) Collide(ref particle, from, dt, birthOffset);
        if (trigger.enabled) ProcessTriggers(ref particle, birthOffset + dt);
    }

    //接触剩余时间再次扫掠，可在同一步内反弹到第二个表面。
    private void Collide(ref Particle p, Vector3 from, float dt, float birthOffset)
    {
        float remaining = dt, passed = 0, radius = PhysicsRadius(p, collision.radiusScale);
        //外层已经扣除整步寿命；还原接触时刻的剩余时间，不能把本步末自然死亡误认为接触即死。
        float lifeLeft = Mathf.Max(0, p.remainingLifetime) + dt;
        Vector3 velocity = WorldVelocity(p), destination = WorldPosition(p);
        for (int iteration = 0; iteration < 8 && remaining > 0; iteration++)
        {
            Vector3 delta = destination - from;
            if (!FindParticleHit(from, delta, radius, out float fraction, out Vector3 normal, out Component hit, out ParticlePhysicsShape shape))
            { from = destination; lifeLeft -= remaining; remaining = 0; break; }
            float spent = remaining * fraction;
            from += delta * fraction; passed += spent; remaining -= spent; lifeLeft -= spent;
            float penetration = radius - shape.Distance(from, out _);
            from += normal * (Mathf.Max(0, penetration) + 1e-5f);
            Vector3 point = from - normal * radius, incoming = velocity;
            Rigidbody body = (hit as Collider)?.attachedRigidbody;
            Vector3 surfaceVelocity = body?.GetPointVelocity(point) ?? Vector3.zero;
            Vector3 relative = velocity - surfaceVelocity;
            if (shape.kind == ParticlePhysicsShape.Kind.Planar) relative.z = 0;
            float approach = Vector3.Dot(relative, normal), age = Age(p), random = Random(p.randomSeed, 181);
            if (approach < 0)
            {
                float bounce = Mathf.Max(0, collision.bounce.Evaluate(age, random));
                velocity = surfaceVelocity + (relative - normal * ((1 + bounce) * approach)) * (1 - Mathf.Clamp01(collision.dampen.Evaluate(age, random)));
                if (shape.kind == ParticlePhysicsShape.Kind.Planar) velocity.z = incoming.z;
                ApplyColliderForce(body, point, normal, relative, PhysicsRadius(p, 1) * 2);
                float loss = Mathf.Clamp01(collision.lifetimeLoss.Evaluate(age, random));
                if (loss >= 1) lifeLeft = 0;
                else if (loss > 0 && float.IsFinite(p.startLifetime)) lifeLeft -= p.startLifetime * loss;
                float speed = velocity.magnitude;
                if (speed < collision.minKillSpeed || speed > collision.maxKillSpeed) lifeLeft = 0;
                if (lifeLeft <= 0) p.physicsKilled = true;
                remaining = Mathf.Min(remaining, Mathf.Max(0, lifeLeft));
                p.remainingLifetime = lifeLeft - remaining;
                p.position = FromWorld(from, p.eventSpace);
                p.velocity = PhysicsLocalVelocity(velocity, p.eventSpace) - p.moduleVelocity;
                if (collision.sendCollisionMessages)
                    collisionEvents.Add(new ParticleCollisionEvent { intersection = point, normal = normal, velocity = incoming, colliderComponent = hit });
                SubEmit(p, 1, birthOffset + passed);
            }
            if (lifeLeft <= 0) { remaining = 0; break; }
            destination = from + velocity * remaining;
        }
        //角落连续接触达到上限时停在最后安全接触点，不能把剩余位移直接穿过障碍。
        p.position = FromWorld(from, p.eventSpace);
        p.velocity = PhysicsLocalVelocity(velocity, p.eventSpace) - p.moduleVelocity;
        p.remainingLifetime = lifeLeft - remaining;
    }

    //过滤图层、触发器和动态刚体后，再在实际邻近的候选中选择最早命中。
    private bool FindParticleHit(Vector3 from, Vector3 delta, float radius, out float fraction, out Vector3 normal, out Component hit, out ParticlePhysicsShape shape)
    {
        fraction = float.PositiveInfinity; normal = default; hit = null; shape = default;
        if (collision.type == ParticleSystemCollisionType.Planes)
        {
            foreach (Transform plane in collision.planes)
            {
                if (plane == null) continue;
                var candidate = new ParticlePhysicsShape { kind = ParticlePhysicsShape.Kind.Plane, center = plane.position, axis = plane.up };
                if (candidate.Sweep(from, delta, radius, out float at, out Vector3 n) && at < fraction)
                { fraction = at; normal = n; hit = plane; shape = candidate; }
            }
        }
        else
        {
            int considered = 0;
            float reach = delta.magnitude + radius;
            foreach (var entry in collisionCandidates)
            {
                ParticlePhysicsShape candidate = entry.shape;
                if (candidate.Distance(from, out _) > reach) continue;
                if (considered++ >= collision.maxCollisionShapes) break;
                if (candidate.Sweep(from, delta, radius, out float at, out Vector3 n) && at < fraction)
                { fraction = at; normal = n; hit = entry.collider; shape = candidate; }
            }
        }
        return hit != null;
    }

    //每次粒子撞击给刚体一次冲量，角度、速度与尺寸开关独立参与权重。
    private void ApplyColliderForce(Rigidbody body, Vector3 point, Vector3 normal, Vector3 relative, float size)
    {
        if (body == null || body.isKinematic || collision.colliderForce == 0 || relative.sqrMagnitude == 0) return;
        float strength = collision.colliderForce, speed = relative.magnitude;
        if (collision.multiplyColliderForceByCollisionAngle) strength *= Mathf.Max(0, -Vector3.Dot(relative / speed, normal));
        if (collision.multiplyColliderForceByParticleSpeed) strength *= speed;
        if (collision.multiplyColliderForceByParticleSize) strength *= size;
        body.AddForceAtPosition(relative / speed * strength, point, ForceMode.Impulse);
    }

    //世界速度逆变换包含非均匀缩放，不能用仅旋转的模块向量转换代替。
    private Vector3 PhysicsLocalVelocity(Vector3 world, EventSpace space)
    {
        if (space is { local: true }) return DivideScale(Quaternion.Inverse(space.rotation) * world, space.scale);
        if (main.simulationSpace == ParticleSystemSimulationSpace.Local && main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            return DivideScale(Quaternion.Inverse(transform.rotation) * world, EventScale);
        return SimulationTransform != null ? SimulationTransform.InverseTransformVector(world) : world;
    }
    private float PhysicsRadius(Particle p, float scale) => .5f * Mathf.Max(0, scale)
        * ParticlePhysicsShape.Max(ParticlePhysicsShape.Abs(Vector3.Scale(SizeAt(p), ParticleScale(p))));

    //同一帧每个碰撞对象只发送一次消息，完整逐次接触记录由 GetCollisionEvents 读取。
    private void DispatchPhysicsEvents()
    {
        // No collision messages to deduplicate; trigger callbacks remain independent.
        if (collisionEvents.Count == 0) { DispatchTriggerEvents(); return; }
        var targets = new HashSet<GameObject>();
        foreach (ParticleCollisionEvent item in collisionEvents)
            if (item.colliderComponent != null) targets.Add(item.colliderComponent.gameObject);
        foreach (GameObject target in targets)
        {
            ParticlePhysicsWorld.Send(gameObject, "OnParticleCollision", target);
            if (target != gameObject) ParticlePhysicsWorld.Send(target, "OnParticleCollision", gameObject);
        }
        DispatchTriggerEvents();
    }
    public int GetSafeCollisionEventSize() => collisionEvents.Count;
    public int GetCollisionEvents(GameObject target, List<ParticleCollisionEvent> results)
    {
        ArgumentNullException.ThrowIfNull(results); results.Clear();
        foreach (ParticleCollisionEvent item in collisionEvents)
            if (item.colliderComponent != null && item.colliderComponent.gameObject == target) results.Add(item);
        return results.Count;
    }
}
