using System.Runtime.CompilerServices;
using RimUnity;

internal static class AnimatorReuseChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        var root = new GameObject("pooled animation hierarchy");
        var animated = new GameObject("animated child");
        animated.transform.SetParent(root.transform, false);
        var controller = new RuntimeAnimatorController();
        var clip = new AnimationClip { length = 1, tracks = new[] { new AnimationTrack {
            path = "", type = "RimUnity.ParticleSystem", property = "CustomDataModule.vector1_0.scalar",
            curve = new AnimationCurve(new Keyframe(0, 5), new Keyframe(1, .05f)) } } };
        try
        {
            var eventProbe = animated.AddComponent<AnimatorReuseEventProbe>();
            clip.events = new[] { new AnimationEvent { time = .1f, functionName = "CountReuseEvent" } };
            var particles = animated.AddComponent<ParticleSystem>();
            particles.customData.enabled = true;
            particles.customData.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Vector);
            controller.parameters = new[] { new AnimatorControllerParameter { name = "pending", type = AnimatorControllerParameterType.Trigger },
                new AnimatorControllerParameter { name = "amount", type = AnimatorControllerParameterType.Float, defaultFloat = 2 } };
            var motion = new AnimationMotion { clip = clip };
            controller.layers = new[] { new AnimatorLayer { name = "Base", defaultState = "Forming", states = new[] {
                new AnimatorState { name = "Forming", motion = motion }, new AnimatorState { name = "Other", motion = motion } } } };
            var animator = animated.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            for (int i = 0; i < 10; i++)
            {
                animator.Update(i % 2 == 0 ? 1.2f : .3f);
                animator.SetTrigger("pending"); animator.SetFloat("amount", 11);
                int eventCount = eventProbe.Calls;
                animator.CrossFadeInFixedTime("Other", .5f);
                if (i % 2 == 0)
                {
                    root.SetActive(false); root.SetActive(true);
                    Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == 0, "Reused Animator kept its previous time");
                    Check(animator.GetFloat("amount") == 2, "Hierarchy reset did not restore parameter defaults");
                    Check(!animator.IsInTransition(0) && !animator.GetBool("pending"), "Reused Animator retained transition or trigger");
                    Check(Math.Abs(particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax - 5) < .00001f,
                        "Reused Animator did not restore the first-frame Custom2 pose");
                }
                else
                {
                    float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    float currentPose = particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax;
                    animator.enabled = false; animator.enabled = true;
                    Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == time, "Component pause restarted Animator time");
                    Check(animator.GetFloat("amount") == 11, "Component pause reset parameter values");
                    Check(animator.IsInTransition(0) && animator.GetBool("pending"), "Component pause cleared transition or trigger");
                    Check(particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax == currentPose, "Component pause changed sampled pose");
                }
                Check(eventProbe.Calls == eventCount, "Re-enabling dispatched an animation event");
            }
            // Native Unity 2022 also resets an already-disabled Animator when its
            //hierarchy is deactivated. Component OnDisable alone cannot observe it.
            foreach (bool keep in new[] { false, true })
            {
                animator.keepAnimatorStateOnDisable = keep;
                animator.Update(.2f); animator.SetTrigger("pending");
                float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.enabled = false; root.SetActive(false); root.SetActive(true); animator.enabled = true;
                Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == (keep ? time : 0), "Disabled component missed hierarchy reset/retention");
                Check(animator.GetBool("pending") == keep, "Disabled component trigger reset/retention differs");
            }
            animator.keepAnimatorStateOnDisable = false;
            var inactiveParent = new GameObject("inactive animator parent");
            try
            {
                inactiveParent.SetActive(false); animator.Update(.2f); animator.enabled = false;
                animated.transform.SetParent(inactiveParent.transform, false);
                animated.transform.SetParent(root.transform, false); animator.enabled = true;
                Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == 0, "Reparenting a disabled Animator into an inactive hierarchy missed reset");
            }
            finally { RimUnity.Object.DestroyImmediate(inactiveParent); }
            typeof(Animator).GetProperty("keepAnimatorStateOnDisable")!.SetValue(animator, true);
            animator.Update(.4f);
            float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float pose = particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax;
            root.SetActive(false); root.SetActive(true);
            Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == before, "Explicitly retained Animator state restarted");
            Check(particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax == pose, "Retained pose changed on re-enable");
            //Bindings must retain their authored baseline across reuse. Rebinding from
            //the terminal pose would make additive layers drift on every activation.
            typeof(Animator).GetProperty("keepAnimatorControllerStateOnDisable")!.SetValue(animator, false);
            particles.customData.SetVector(ParticleSystemCustomData.Custom2, 0, 0);
            var additive = new AnimationClip { length = 1, tracks = new[] { new AnimationTrack {
                path = "", type = "RimUnity.ParticleSystem", property = "CustomDataModule.vector1_0.scalar",
                curve = new AnimationCurve(new Keyframe(0, 2), new Keyframe(1, 2)) } } };
            try
            {
                controller.layers = new[] { controller.layers[0], new AnimatorLayer { name = "Additive", defaultState = "Offset", additive = true,
                    states = new[] { new AnimatorState { name = "Offset", motion = new AnimationMotion { clip = additive } } } } };
                animator.Rebind();
                animator.Update(0);
                for (int i = 0; i < 5; i++)
                {
                    animator.Update(1.1f);
                    root.SetActive(false); root.SetActive(true);
                    Check(Math.Abs(particles.customData.GetVector(ParticleSystemCustomData.Custom2, 0).constantMax - 7) < .00001f,
                        "Additive baseline drifted across Animator reuse");
                }
            }
            finally { RimUnity.Object.DestroyImmediate(additive); }
            var removed = root.AddComponent<AnimatorReuseSelfRemoval>();
            Check(!removed && root.GetComponent<AnimatorReuseSelfRemoval>() == null, "Self-removal in Awake broke lifecycle draining");
            Console.WriteLine("PASS hierarchy pooling and component pause: reset only on hierarchy deactivation, including already-disabled components; immediate Custom2 pose, transition/trigger retention and stable additive baseline.");
        }
        finally { RimUnity.Object.DestroyImmediate(root); RimUnity.Object.DestroyImmediate(controller); RimUnity.Object.DestroyImmediate(clip); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

internal sealed class AnimatorReuseEventProbe : MonoBehaviour
{
    internal int Calls;
    private void CountReuseEvent() => Calls++;
}
internal sealed class AnimatorReuseSelfRemoval : MonoBehaviour
{
    private void Awake() => RimUnity.Object.DestroyImmediate(this);
}
