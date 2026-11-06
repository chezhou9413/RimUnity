# 0.1.21 Release Notes

- Add HDR rendering for independent UI cameras, preserving bright colors in translucent particles during blending.
- Preserve the camera's `allowHDR` setting in the Unity exporter.

For previously exported resources, re-export the camera or set `camera.allowHDR = true` in code to enable UI HDR. Content mods using the embedded runtime should update their embedded package.
