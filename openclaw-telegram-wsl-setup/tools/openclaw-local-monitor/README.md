# OpenClaw 控制中心

Windows 本机小程序面板，用于启动并观察运行在 Ubuntu on WSL2 里的 OpenClaw gateway。

它是桌面上的唯一主入口。打开后会自动尝试唤醒 WSL、启动 `openclaw-gateway.service`，并显示：

- gateway 和 Telegram 是否就绪
- `openclaw tasks list` 与 TaskFlow 里的后台任务状态
- `openclaw status --json` 中的 token/context 快照
- 从 session `usage.cost` 汇总的本月本地记录成本，按模型展示
- 最近会话和 Telegram / 错误提醒
- 托盘常驻能力

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

- `OpenClaw Control`: opens the local control center. It starts OpenClaw if needed, then shows the local panel.

The installer removes old separate `OpenClaw Monitor`, `OpenClaw 启动`, and other old `OpenClaw*.lnk` shortcuts in the same shortcut folders, because the control center is now the only main entry.

## Open The Browser Control

The local panel has an `打开 Control` button. Use it only when you need the original browser-based OpenClaw Control UI. The helper script `Start-OpenClaw.ps1` is kept as an internal launcher for that button, not as a separate desktop entry.

That helper resolves the gateway token locally and opens the browser Control URL with a temporary `#token=...` fragment when OpenClaw exposes one. The token is not committed, printed to chat, or stored as a shortcut argument.

After opening the URL, the helper makes a best-effort attempt to restore and focus the browser window. This gives the user visible feedback whether the browser was minimized, hidden in the background, or not yet open.

## Recheck Button

The panel updates its display automatically. The `重新检测` button is not a cosmetic refresh; it manually wakes WSL, tries to start the gateway, then rebuilds the displayed snapshot. It does not edit config, reset tasks, or touch tokens.

Hovering over the button should show this in short form inside the panel itself. The hint should stay within the app window rather than using a native tooltip that can spill outside the interface.

When restoring from the system tray or the Windows taskbar, the window should repaint as one composed frame instead of showing partially black or unpainted regions before content appears.

## Build Manually

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-OpenClawMonitor.ps1
```

The build uses the built-in .NET Framework C# compiler:

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

No external package manager is required.

## Regenerate Icon

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Generate-OpenClawMonitorIcon.ps1
```

The icon is a transparent-background, friendly red OpenClaw-style mascot for desktop, taskbar, and tray use. Do not use a screenshot with a dark background as the icon.

## Uninstall Autostart

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall-Autostart.ps1
```

This removes only the Startup shortcut. It does not delete the monitor folder.

## Notes

- Do not store OpenClaw tokens, API keys, auth profiles, or logs in this folder.
- Cost shown by the panel comes from local OpenClaw session usage records. Treat it as OpenClaw's recorded/estimated model cost, not as a replacement for provider billing pages.
- The panel assumes the WSL distro is named `Ubuntu` and that `openclaw` is available on the WSL user's login-shell `PATH`; adjust `OpenClawMonitor.cs` before building if the target machine differs.
