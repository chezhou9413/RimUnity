using System;
using System.Collections.Generic;

namespace RimUnity;

public sealed partial class ParticleSystem
{
    //Capacity freed by collision/trigger is reconsidered at most one normal physics
    //substep later. Rejected continuous attempts must not set physics frequency.
    private const float CapacityPhysicsStep = SimulationStepSeconds;
    //Instance-owned scratch, cleared per Step. A continuous source occupies one entry,
    //regardless of its particle rate; no per-particle scheduling allocations.
    private readonly List<ScheduledEmission> scheduledEmissions = new();
    private struct ScheduledEmission
    {
        internal float from, to, start, end, phase, amount, remainder;
        internal int count, index;
        internal bool burst;
        internal readonly float Fraction => burst ? 0 : (index + 1 - remainder) / amount;
        internal readonly float Offset => Mathf.Lerp(start, end, Fraction);
    }

    private void EmitScheduled(float dt, Vector3 emitterFrom, Vector3 emitterTo)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.EmissionTimeline);
#endif

        float advanced = 0;
        float? capacityUntil = null;
        try
        {
            while (true)
            {
                int next = -1; float offset = float.PositiveInfinity;
#if RIMUNITY_PARTICLE_ANALYSIS
                Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.EmissionScheduleVisits, scheduledEmissions.Count);
#endif
                for (int i = 0; i < scheduledEmissions.Count; i++)
                {
                    var candidate = scheduledEmissions[i];
                    if (candidate.index < candidate.count && candidate.Offset < offset)
                    { next = i; offset = candidate.Offset; }
                }
                if (next < 0) break;
                var entry = scheduledEmissions[next];
                float at = offset * dt;
                if (main.maxParticles == 0) break;
                if (!CanEmitParticle)
                {
                    if (!capacityUntil.HasValue)
                    {
                        float death = float.PositiveInfinity;
#if RIMUNITY_PARTICLE_ANALYSIS
                        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.CapacityVisits, count);
#endif
                        for (int i = 0; i < count; i++)
                            death = Math.Min(death, Math.Max(advanced, particles[i].birthOffset) + Math.Max(0, particles[i].remainingLifetime));
                        capacityUntil = death;
                    }
                    float checkpoint = capacityUntil.Value;
                    bool physics = collision.enabled || trigger.enabled;
                    if (physics)
                    {
                        checkpoint = Math.Min(checkpoint, advanced + CapacityPhysicsStep);
                        //Bursts are explicit event boundaries and must observe physics at
                        //their own time. Never skip continuous attempts across an earlier
                        //Burst that can force a capacity recheck and release a slot.
                        foreach (var boundary in scheduledEmissions)
                            if (boundary.burst && boundary.index < boundary.count)
                                checkpoint = Math.Min(checkpoint, boundary.Offset * dt);
                        if (at > checkpoint && checkpoint > advanced)
                        {
                            AdvanceParticles(checkpoint, advanced);
                            advanced = checkpoint; capacityUntil = null;
                            continue;
                        }
                    }
                    //Before a natural death or physics checkpoint all continuous attempts
                    //are rejected together, without per-attempt integration or allocation.
                    if (at < checkpoint)
                    {
                        if (entry.burst || checkpoint > dt) entry.index = entry.count;
                        else
                        {
                            double threshold = (checkpoint / dt - entry.start) / (entry.end - entry.start);
                            double index = Math.Ceiling(threshold * entry.amount + entry.remainder - 1);
                            entry.index = (int)Math.Min(entry.count, Math.Max(entry.index + 1d, index));
                        }
                        scheduledEmissions[next] = entry;
                        continue;
                    }
                }
                //Only flush deferred integration when a capacity decision requires it.
                //Never reclaim a death later than the current emission timestamp.
                if (count >= main.maxParticles && (at > advanced || !CanEmitParticle))
                {
                    if (CanDeferNaturalDeathIntegration)
                        ReclaimNaturalDeaths(at, advanced);
                    else
                    {
                        AdvanceParticles(at, advanced);
                        advanced = at;
                    }
                    capacityUntil = null;
                }
                float time = Mathf.Lerp(entry.from, entry.to, entry.Fraction);
                float phase = entry.burst ? entry.phase : Mathf.Repeat(time, main.duration) / main.duration;
                int amount = entry.burst ? entry.count : entry.index + 1;
                for (; entry.index < amount && CanEmitParticle; entry.index++)
                    Spawn(default, Vector3.Lerp(emitterFrom, emitterTo, offset), phase, at, time, entry.index, entry.count);
                entry.index = amount;
                scheduledEmissions[next] = entry;
                capacityUntil = null;
            }
            AdvanceParticles(dt, advanced);
        }
        finally
        {
            //Offsets are relative to this Step; surviving particles start the next at zero.
            for (int i = 0; i < count; i++) particles[i].birthOffset = 0;
            scheduledEmissions.Clear();
        }
    }

    //Without death observers, expired particles only occupy capacity. Keep survivors
    //deferred until the normal substep ends instead of integrating the entire pool
    //again at every replacement birth. Stateful physics, trails, child emission and
    //randomized forces retain the chronological integration path.
    private bool CanDeferNaturalDeathIntegration =>
        main.ringBufferMode == ParticleSystemRingBufferMode.Disabled &&
        !collision.enabled && !trigger.enabled && !trails.enabled && trailStates.Count == 0 &&
        !subEmitters.enabled && subEmissionEvents.Count == 0 &&
        !(forceOverLifetime.enabled && forceOverLifetime.randomized);

    private void ReclaimNaturalDeaths(float at, float advanced)
    {
#if RIMUNITY_PARTICLE_ANALYSIS
        using var particleDetailScope = Diagnostics.LabParticleDetail.Measure(Diagnostics.ParticleDetailStage.CapacityReclaim);
#endif

        for (int i = count - 1; i >= 0; i--)
        {

#if RIMUNITY_PARTICLE_ANALYSIS
        Diagnostics.LabParticleDetail.Work(Diagnostics.ParticleDetailWork.CapacityVisits, 1);
#endif
            ref Particle particle = ref particles[i];
            float death = Math.Max(advanced, particle.birthOffset) + Math.Max(0, particle.remainingLifetime);
            if (death > at) continue;
            ReleaseParticle(particle.identity);
            particles[i] = particles[--count];
            particles[count] = default;
        }
    }
}
