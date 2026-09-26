# Contributing

Build locally on Windows with PowerShell 7 and .NET 10 using `scripts/Build.ps1`. Add focused checks for affected behavior, and distinguish deterministic tests, WPF internal rendering, actual mouse input and physical monitor/suspend testing.

Keep runtime behavior offline. Do not add model SDKs, API keys, chat/task readers, global input hooks or telemetry. Never start Claude or other AI clients as a test dependency. Claude checks must use local fixtures, passive metadata reads and local mascot processes only.

Do not commit personal paths, preferences, runtime status, raw machine diagnostics, account data or private development logs. Use synthetic fixture paths in tests. Respect the separate artwork notice when adding images.

For a bug report, include the release version, Windows version, display scaling, expected/observed behavior and a minimal reproduction. Avoid screenshots containing private application content.
