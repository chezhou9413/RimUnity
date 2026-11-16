# 0.1.23 Release Notes

- Fix Bloom compositing hooks and frame handoff so inlined rendering entry points do not bypass the effect.
- Improve HDR/Bloom resource rebuilding when resolution or buffer formats change, with shader checks and additional diagnostics.
- Fix embedded runtime selection across content mods, select a compatible valid payload, and support content mods without their own payload.
- Skip invalid continuous particle emission samples while preserving emission progress, and avoid unnecessary distance calculations.

Content mods using the embedded runtime should update their embedded package.
