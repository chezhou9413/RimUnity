# 0.1.27 Release Notes

- Fix startup on RimWorld 1.7.5021 after Waylith removed a render-frame event. Custom render passes can still send callbacks to the main thread after frame submission.
- Update the spike diagnostic report to explain how main-thread and render-thread frame times are measured.
