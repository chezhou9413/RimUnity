# 0.1.25 Release Notes

- Fix shader warmup on RimWorld 1.7 builds without `ValidateStageInterface`. RimUnity validates vertex outputs against fragment inputs.
- Fix dark nighttime maps and light ordering across render layers in Experiment HDR. Segmented compositing preserves the host depth buffer.
- Add HDR trail brightness samples to render diagnostics.

Content mods using the embedded runtime should update to the matching embedded package and rebuild.
