using System;

namespace RimUnity.ScreenEfx;

/// <summary>A game-session screen effect. All callbacks run on the game thread.</summary>
public abstract class EfxClip
{
    private bool enabled = true;
    private int order;
    private bool updateEnabled = true;
    public bool Enabled { get => enabled; set { EfxPlayer.AssertThread(); enabled = value; } }
    public int Order { get => order; set { EfxPlayer.AssertThread(); order = value; } }
    /// <summary>Opt out of Update while dormant. Business events must wake the clip by setting this to true.</summary>
    public bool UpdateEnabled { get => updateEnabled; set { EfxPlayer.AssertThread(); updateEnabled = value; } }
    internal bool Submitted;
    public virtual void Update(EfxUpdateContext context) { }
    /// <summary>Optional loading-time material passes. Called on the game thread even when disabled; no draw is presented.</summary>
    public virtual void Warmup(EfxFrameBuilder frame) { }
    public abstract void BuildFrame(EfxFrameBuilder frame);
    /// <summary>Called once after stopping and after all captured GPU frames have retired.</summary>
    protected internal virtual void OnRelease() { }
}

public readonly record struct EfxUpdateContext(float DeltaTime, float UnscaledDeltaTime, float Time, float UnscaledTime);

/// <summary>Stops playback immediately; resource release waits for captured frames.</summary>
public sealed class EfxHandle : IDisposable
{
    internal EfxLease Lease;
    public bool IsPlaying => Lease != null && !Lease.Stopped;
    public void Stop() { EfxPlayer.AssertThread(); Lease?.Stop(); }
    public void Dispose() => Stop();
}
