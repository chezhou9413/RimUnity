# Continuous emission recovery

Run `dotnet run --project Tools/ParticleEmissionTests -c Release -- <runtime directory> <game directory> <Harmony directory>` after building RimUnity.

Uses the actual runtime and game dependencies without a graphics device or save.
Checks invalid time/distance emission samples, preserved fractional carry, continued
integration of existing particles, and resumed emission without catch-up. The
scheduler is also checked with NaN and both infinities. In-game rendering still
requires validation in the host.
