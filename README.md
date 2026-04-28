# OpenClaw Telegram WSL Setup Skill

This repository packages a Codex skill for installing, repairing, and operating OpenClaw's Telegram channel on Windows through Ubuntu on WSL2.

这个项目把本机已经整理好的 OpenClaw 安装流程变成一个可以上传到 GitHub 的 skill 项目。它的重点不是只记录命令，而是让 AI agent 能按安装现场的状态一步一步判断：WSL2/Ubuntu 是否可用、OpenClaw 是否安装、gateway 是否在线、keepalive 是否作为基础设施存在、Telegram 是否配置成功，以及权限范围是否经过用户确认。

## What It Covers

- Windows-native install vs. Ubuntu on WSL2 explanation.
- Language selection before the install walkthrough.
- Ubuntu on WSL2 as the default no-surprises path.
- OpenClaw install and repair inside WSL.
- Gateway service readiness checks.
- Background keepalive/autostart after Windows login.
- Telegram bot setup without exposing tokens in chat.
- Natural-language visibility and permission scope confirmation.
- Slow/no Telegram reply diagnosis.
- Safe cleanup for Telegram-only channel setup.

## Repository Layout

```text
.
|-- README.md
|-- .gitignore
`-- openclaw-telegram-wsl-setup/
    |-- SKILL.md
    `-- agents/
        `-- openai.yaml
```

`openclaw-telegram-wsl-setup/` is the actual skill folder. Keep this folder clean: do not put local OpenClaw config, tokens, logs, screenshots, startup scripts, or machine-specific diagnostics inside it.

## Install Locally

Copy the skill folder into your Codex skills directory:

```powershell
Copy-Item -Recurse -Force `
  ".\openclaw-telegram-wsl-setup" `
  "$env:USERPROFILE\.codex\skills\openclaw-telegram-wsl-setup"
```

Then start a new Codex session and invoke it with:

```text
Use $openclaw-telegram-wsl-setup to install OpenClaw Telegram on Windows with WSL2.
```

## Safety Rules

- Do not commit `~/.openclaw`, token files, raw logs, local startup scripts, or screenshots containing secrets.
- Do not paste Telegram bot tokens, API keys, auth profiles, or model credentials into chat.
- Token entry should happen through a local terminal prompt or provider UI.
- Permission scope must be confirmed by the user before final Telegram verification.
- Keepalive is infrastructure: verify it exists and works, but do not expose unnecessary terminal windows to the user.

## Pre-Publish Checklist

Before pushing this repo to GitHub:

```powershell
git status
Select-String -Path .\openclaw-telegram-wsl-setup\SKILL.md -Pattern '\d{8,12}:[A-Za-z0-9_-]{25,}'
Select-String -Path .\openclaw-telegram-wsl-setup\SKILL.md -Pattern 'token|api_key|secret|password'
```

The second command should return no real Telegram token. The third command may find safety instructions that mention these words, but it should not reveal actual secret values.
