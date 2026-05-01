# OpenClaw Local Monitor

Windows local monitor panel for an OpenClaw gateway running in Ubuntu on WSL2.

It is intentionally read-only. It shows:

- gateway and Telegram readiness
- strict background task state from `openclaw tasks list` and TaskFlow pressure
- token/context snapshots from `openclaw status --json`
- local recorded cost from session `usage.cost`, grouped by model
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
It also creates desktop and Start Menu shortcuts for:

- `OpenClaw Monitor`: opens the read-only monitor panel.
- `OpenClaw 启动`: starts the WSL OpenClaw gateway keepalive and opens the local OpenClaw page.

## Start OpenClaw Manually

If Windows has started but OpenClaw is not awake yet, use the `OpenClaw 启动` shortcut, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start-OpenClaw.ps1
```

The launcher starts `openclaw-gateway.service` inside Ubuntu on WSL2, keeps WSL awake with a hidden keepalive process, waits briefly for `openclaw gateway probe`, and opens `http://127.0.0.1:18789/chat?session=main`.

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
- Cost shown by the panel comes from local OpenClaw session usage records. Treat it as OpenClaw's recorded/estimated model cost, not as a replacement for provider billing pages.
- The panel assumes the WSL distro is named `Ubuntu` and that `openclaw` is available on the WSL user's login-shell `PATH`; adjust `OpenClawMonitor.cs` before building if the target machine differs.
