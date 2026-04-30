# OpenClaw Local Monitor

Windows local monitor panel for an OpenClaw gateway running in Ubuntu on WSL2.

It is intentionally read-only. It shows:

- gateway and Telegram readiness
- strict background task state from `openclaw tasks list` and TaskFlow pressure
- token/context snapshots from `openclaw status --json`
- recent sessions and Telegram/error notices
- system tray behavior so the panel can stay in the background

## Install From The Skill

Run this from the `tools/openclaw-local-monitor` directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenClawMonitor.ps1
```

The installer copies the monitor into:

```text
%LOCALAPPDATA%\OpenClawMonitor
```

Then it builds `OpenClawMonitor.exe`, creates a Startup-folder shortcut, and starts the panel.

## Build Manually

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-OpenClawMonitor.ps1
```

The build uses the built-in .NET Framework C# compiler:

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

No external package manager is required.

## Uninstall Autostart

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall-Autostart.ps1
```

This removes only the Startup shortcut. It does not delete the monitor folder.

## Notes

- Do not commit the compiled `OpenClawMonitor.exe`.
- Do not store OpenClaw tokens, API keys, auth profiles, or logs in this folder.
- The panel assumes the WSL distro is named `Ubuntu` and that `openclaw` is available on the WSL user's login-shell `PATH`; adjust `OpenClawMonitor.cs` before building if the target machine differs.
