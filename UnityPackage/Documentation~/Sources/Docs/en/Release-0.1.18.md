# 0.1.18 Release Notes

- Optimize particle vertices, trails and mixed-mesh batching to reduce rendering overhead.
- Fix animation state resets when reusing objects and correct paused timing.
- Fix embedded runtime shader paths and avoid reinitializing other mods.
- Fix material color and alpha reads, scene grabs across render queues and zero-width smoothstep boundaries.
- Add a map HDR option, disabled by default, and reduce duplicate shader compatibility warnings.

Content mods using the embedded runtime should update their embedded package.
