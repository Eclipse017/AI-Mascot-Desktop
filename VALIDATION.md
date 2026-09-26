# v1.2.0 local validation

Validated on Windows x64 with .NET SDK 10.0.401. All Claude checks ran locally; no Claude client, webpage, CLI or model was opened or invoked. No cloud CI was used.

| Check | Result |
| --- | --- |
| Core behavior/state | PASS — 76 assertions |
| Dragon Windows platform | PASS — 17 assertions |
| Gemini Windows platform | PASS — 37 assertions |
| Grok Windows platform | PASS — 37 assertions |
| Claude Windows platform | PASS — 50 assertions |
| DeepSeek Windows platform | PASS — 55 assertions |
| Console controller | PASS — 43 assertions |
| Optional target paths | PASS — 14 assertions |
| Moved portable package, directory containing spaces | PASS — 86 integration assertions |
| Console layouts | PASS — 660×520, 720×660, 1100×840, 1600×900 |

The 329 source checks cover fixtures and local platform logic. The 86 integration checks exercise all five real local mascot hosts: individual and batch IPC, hidden-state persistence across restarts, pause, independent exits, duplicate console handling, and host survival after closing the console. Display options and startup configuration were checked after restoration. The test-created public hosts were then exited. The earlier local edition's processes remained unchanged.

The first fresh-host integration attempt read preferences before WPF finished loading images; the verifier now waits for readiness. Packaging initially detected different shared runtime files between a WPF-only console and WPF/Windows Forms hosts. The console now uses the same desktop framework references, and the merged package passes byte-for-byte shared-file checks. Debug symbols are omitted from the distributed ZIP.

The screenshot in `docs/console.png` is a WPF render of the local console, not a screenshot of an AI client. Its fullscreen-hidden status reflects the test desktop at capture time. It contains no private logs or machine paths. Artwork shown inside the screenshot remains subject to `ASSETS.md`.

UNVERIFIED: Claude real-client/online integration, non-Windows platforms, broad mixed-DPI multi-monitor coverage, monitor hot-plug, real suspend/logoff transitions, exclusive fullscreen games, and a fresh physical PC without a preinstalled SDK. The package is published as self-contained win-x64; the existing-PC tests do not substitute for a clean-VM test.
