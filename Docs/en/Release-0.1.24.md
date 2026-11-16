# 0.1.24 Release Notes

- Adapt to the RimWorld 1.7.5011 post-processing API. Screen effects now pass VkImageLayout when clearing borrowed images, so frame recording no longer crashes on the removed SetInput overload.
- Keep warmed mesh renderers when Bloom and map HDR rebuild render targets. Attachments are released only after the GPU is idle, then reallocated for the new size.

Content mods using the embedded runtime should update their embedded package.
