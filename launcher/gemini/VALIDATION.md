# Gemini launcher validation · 2026-10-21

Validated locally on Windows with PowerShell 7 and the Windows .NET Framework C# compiler.

| Check | Result |
| --- | --- |
| Build and launch-selection behavior | PASS — 16 checks |
| Installer, reinstall and restore | PASS — 10 checks |
| WPF layout render | PASS — inspected generated preview |
| AI clients started by automated tests | None |
| Personal machine paths or raw diagnostics included | None |

The behavior checks use injected launch handlers. They cover independent Gemini and Antigravity selection, no automatic launch when the picker opens, preservation of shortcut arguments through Shell execution, missing targets, invalid configuration, blocked XML entities, closing after success, and retrying after an error. Button actions are synthetic WPF routed events, not real mouse input.

Installer checks operate only on inert shortcuts in a fixture directory containing spaces and Chinese characters. They verify copied original shortcuts byte-for-byte, preservation of the Antigravity source entry, safe reinstallation without recursive launcher targets, restoration to the previous installation, exact original restoration, idempotent restore, and refusal to overwrite later user changes. No fixture executable is launched.

The earlier local installation using the same picker and launch logic was also checked through the actual desktop shortcut, Windows Shell icon extraction, client process paths and main windows. Both installed target applications opened through their saved original shortcuts. That local verification is separate from these public-source automated tests; the public variant changes the default installation location to the current user's local application data and reuses the repository's existing artwork.

UNVERIFIED: real mouse clicks end-to-end, actual desktop screenshots, a clean Windows machine, and broad mixed-DPI testing. The Computer Use helper timed out during screenshot capture and could not obtain input geometry, so its attempted mouse action was not counted as a pass. Account login, chat responses and agent task execution are outside launcher verification.

Reproduce from the repository root:

```powershell
pwsh -NoProfile -File ./launcher/gemini/scripts/Build.ps1 -Test
pwsh -NoProfile -File ./launcher/gemini/scripts/Test-Installer.ps1
```

Generated previews, fixture shortcuts and diagnostic output remain under the ignored `launcher/gemini/artifacts/` directory. The initial source-only integration did not change or rerun the desktop-companion tests.

For the v1.3.0 downloadable release, both ZIPs include the prebuilt launcher, its multi-resolution catgirl icon, install/restore scripts, and a double-click CMD entry point requiring PowerShell 7. The packaged installer accepts a payload directory, so a downloaded ZIP needs no compiler or SDK. `scripts/Verify-ReleasePackage.ps1` verifies extracted file inventories/hashes and reruns the 10 installer checks using the scripts and binaries extracted from each ZIP. All backup paths and launch configuration are created locally at installation time and are never shipped.
