using System;

namespace RimUnity;

//所有桥接刚体在同一帧同步积分并求接触，原生 Update 的组件顺序不决定碰撞结果。
internal static class PhysicsBodySimulation
{
    private static int frame = -1;
    internal static void AdvanceFrame()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        Rigidbody[] bodies = RimUnity.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Collider[] colliders = RimUnity.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float longest = 0;
        foreach (Rigidbody body in bodies)
        {
            body.PreparePhysicsFrame(); longest = Math.Max(longest, body.physicsFrameDelta);
            if (body.isKinematic) body.IntegrateBody(body.physicsFrameDelta, true);
        }
        int steps = Math.Max(1, checked((int)Math.Ceiling(longest * 60)));
        for (int step = 0; step < steps; step++)
        {
            foreach (Rigidbody body in bodies) if (!body.isKinematic && body.isActiveAndEnabled) body.IntegrateBody(body.physicsFrameDelta / steps, false);
            RigidbodyContacts.Solve(colliders);
        }
        foreach (Rigidbody body in bodies) if (body.physicsFrameDelta > 0) body.ClearForces();
    }
}
