using System;
using System.Collections.Generic;
using System.Reflection;

namespace RimUnity;

//只查询已接通的桥接碰撞器；原版地图墙体不会自动变成物理表面。
internal static class ParticlePhysicsWorld
{
    internal static Collider[] colliders = Array.Empty<Collider>();
    private static readonly Dictionary<(Type, string), MethodInfo> callbacks = new();

    //每次粒子组推进前收集对象，避免逐粒扫描全部 GameObject。
    internal static void Refresh(bool advanceBodies)
    {
        if (advanceBodies) PhysicsBodySimulation.AdvanceFrame();
        colliders = RimUnity.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    internal static bool Queryable(Collider collider) => collider != null && collider.enabled && collider.gameObject.activeInHierarchy
        && (collider.attachedRigidbody is not { } body || body.detectCollisions);

    //粒子回调按 Unity 消息名称寻找，每个组件只调用最具体的同名实现。
    internal static void Send(GameObject target, string name, GameObject other = null)
    {
        foreach (MonoBehaviour component in target.GetComponents<MonoBehaviour>())
        {
            if (!component.isActiveAndEnabled) continue;
            var key = (component.GetType(), name);
            if (!callbacks.TryGetValue(key, out MethodInfo method))
            {
                Type[] args = name == "OnParticleCollision" ? new[] { typeof(GameObject) } : Type.EmptyTypes;
                for (Type type = key.Item1; type != null && method == null; type = type.BaseType)
                    method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
                callbacks[key] = method;
            }
            method?.Invoke(component, name == "OnParticleCollision" ? new object[] { other } : null);
        }
    }
}
