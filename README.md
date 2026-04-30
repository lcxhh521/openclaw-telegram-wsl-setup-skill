# OpenClaw 养虾指南（WSL Toolkit）

这是我整理给自己和其他 OpenClaw 用户的一套 Windows/WSL 使用笔记和 Codex skill。

它主要解决一类很烦的问题：OpenClaw 本身能跑，但一接上 Telegram、长期挂后台、电脑重启、WSL 休眠、断网恢复、代理切换、模型认证之后，就开始变得不稳定。很多时候表面现象只是“机器人不回消息”，真正的问题却藏在 gateway、systemd、WSL、token、模型或网络恢复里面。

所以这个项目不是一个万能安装器，也不是官方文档替代品。它更像一份“养虾指南”：把我认为比较稳的 Windows + WSL2 路线、常见故障判断顺序、后台常驻方案、断网恢复方案、本机监控面板，都放在一个地方。

我希望它做到三件事：

- 不把 token、API key、auth profile、日志这类东西塞进仓库或聊天记录。
- 每一步尽量能检查、能解释，不靠“重启试试”碰运气。
- 尽量少让用户手工重复配置。该常驻的常驻，该监控的监控，该写进 skill 的就写进 skill。

项目最开始只是为了修 OpenClaw + Telegram + WSL2。后来补上了 keepalive、断网恢复、Token/后台任务监控和一个 Windows 本机小面板，所以现在名字也从单纯的 Telegram setup 扩成了 WSL toolkit。

## 这个项目解决什么问题

很多 OpenClaw + Telegram 的问题表面上看是“机器人不回消息”，但真正的原因可能在更底层：

- WSL2 或 Ubuntu 没有正确准备好。
- OpenClaw 只装在 Windows 原生环境里，而 gateway 实际需要更稳定的 Linux/WSL 运行环境。
- `openclaw-gateway.service` 没有启动，或者只是监听端口但没有真正响应。
- Windows 关机、重启或 WSL 自动休眠后，OpenClaw 没有自动恢复。
- 电脑长时间断网后，OpenClaw 进程还活着，但 Telegram polling、provider socket 或 HTTP transport 进入半死状态，恢复网络后仍然不行动。
- 网络或代理短暂抖动后，过于敏感的 watchdog 反复重启 gateway，导致 Telegram 能回复但明显延迟。
- 模型没有选好、认证没通、额度不够，导致 OpenClaw 本地就不能稳定回复。
- Telegram bot token 配好了，但 channel 没有启动、没有 pairing、没有权限，或者模型侧没有完成回复。
- 用户没有明确确认 OpenClaw 可以看到哪些文件、能执行哪些操作。

这个 skill 的核心思路是：**先判断 OpenClaw 本地运行链路是否健康，再处理 Telegram 配置，最后做端到端消息验证。**

## 推荐安装路径

本 skill 默认推荐：

```text
Windows
  -> WSL2
  -> Ubuntu
  -> Ubuntu 内安装 OpenClaw
  -> systemd user gateway
  -> 权限范围确认
  -> 模型选择与本地回复验证
  -> Windows 登录后的 keepalive/autostart
  -> 长时间断网后的 network recovery watchdog
  -> 本机 OpenClaw Monitor 状态面板
  -> Telegram Bot
```

之所以推荐 Ubuntu on WSL2，而不是 Windows 原生安装，是因为 Telegram bot 需要一个长期在线的 gateway。WSL2 + Ubuntu 更接近 Linux 服务环境，systemd 用户服务、路径、权限、后台常驻和代理行为都更容易稳定下来。

## 主要能力

- 安装前先选择中文或英文，并用对应语言解释流程。
- 解释 Windows 原生安装和 WSL2 安装的区别。
- 默认使用 Ubuntu on WSL2，减少用户在发行版选择上的困惑。
- 自动判断当前机器处于哪个状态：未装 WSL、Ubuntu 不完整、OpenClaw 缺失、gateway 不通、keepalive 缺失、Telegram 未配置、Telegram 能收到但不回复等。
- 指导安装或修复 OpenClaw gateway。
- 将 keepalive 作为基础设施处理，让 OpenClaw 在 Windows 登录后自动恢复。
- 将长时间断网恢复作为基础设施处理：连续失败后才确认 offline，网络从确认 offline 变回 online 时自动重建 gateway，清理 stale socket / polling stall。
- 为 network recovery watchdog 加防抖和冷却：单次网络探测失败或单次 gateway 探测失败只记录，不立刻重启，避免 watchdog 自己制造回复延迟。
- 在 watchdog 内用本地 HTTP 仪表盘端口检查 gateway 健康，不在 systemd timer 环境里调用 `openclaw gateway probe`，避免 CLI 环境差异造成误判。
- 为 gateway 启动加入宽限期：OpenClaw 启动、补装 bundled runtime dependencies、启动 channels/sidecars 时，watchdog 不应因为 HTTP 探测暂时失败而重启 gateway。
- 安装本机只读 OpenClaw Monitor 面板，用 Windows 原生小程序显示 gateway、Telegram、后台任务、TaskFlow、Token/上下文流向、最近会话和日志提醒，并支持系统托盘常驻。
- 在接入 Telegram 之前，先选择模型并验证 OpenClaw 本地可以正常回复。
- 管理安装过程中弹出的终端窗口：需要用户操作的窗口保留，不需要的窗口及时关闭。
- 安全配置 Telegram bot token，避免用户把 token 粘贴到聊天里。
- 要求用户用自然语言确认 OpenClaw 的可见范围和权限范围。
- 诊断 Telegram 回复慢、无回复、只在 WSL 被唤醒后才回复等问题。
- 在需要时做 Telegram-only channel cleanup，但不误删模型、代理、权限、gateway 等非聊天配置。

## 项目结构

```text
.
|-- README.md
|-- .gitignore
`-- openclaw-telegram-wsl-setup/
    |-- SKILL.md
    |-- agents/
    |   `-- openai.yaml
    `-- tools/
        `-- openclaw-local-monitor/
            |-- OpenClawMonitor.cs
            |-- Build-OpenClawMonitor.ps1
            |-- Install-OpenClawMonitor.ps1
            |-- Install-Autostart.ps1
            |-- Uninstall-Autostart.ps1
            |-- OpenClawMonitor.ico
            `-- README.md
```

真正的 Codex skill 仍然是：

```text
openclaw-telegram-wsl-setup/
```

这个目录名暂时保留是为了兼容已经安装的 Codex skill 和旧链接；公开项目名称以 **OpenClaw 养虾指南（WSL Toolkit）** 为准。这个目录应保持干净，只包含 skill 本身需要的文件和可复用工具。不要把本机 OpenClaw 配置、Telegram token、日志、截图、编译产物或机器专属诊断文件放进去。

## 本机 OpenClaw Monitor 面板

仓库附带一个 Windows 原生监控面板：

```text
openclaw-telegram-wsl-setup/tools/openclaw-local-monitor/
```

它用于安装完成后的日常观察，不替代 OpenClaw 官方仪表盘。面板只读，不修改 OpenClaw 配置，主要显示：

- gateway 和 Telegram 是否可用。
- 后台是否存在 `queued/running` task 或活跃 TaskFlow。
- Token / 上下文使用快照，以及主会话、Telegram、子任务的流向。
- 最近会话和 Telegram/error 日志提醒。
- 系统托盘常驻，最小化或关闭窗口时隐藏到托盘。

安装命令：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-local-monitor
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenClawMonitor.ps1
```

安装脚本会把源码复制到 `%LOCALAPPDATA%\OpenClawMonitor`，在本机编译 `OpenClawMonitor.exe`，创建 Startup-folder 自启动快捷方式，并启动面板。仓库不提交编译出来的 `.exe`。

## 本地安装到 Codex

把 skill 文件夹复制到 Codex 的 skills 目录：

```powershell
Copy-Item -Recurse -Force `
  ".\openclaw-telegram-wsl-setup" `
  "$env:USERPROFILE\.codex\skills\openclaw-telegram-wsl-setup"
```

然后开启新的 Codex 会话，使用：

```text
Use $openclaw-telegram-wsl-setup to follow the OpenClaw 养虾指南 on Windows with WSL2.
```

如果你已经在中文对话中调用它，skill 会继续使用中文；如果是新安装流程，它会先确认安装语言。

## 给其他 Agent 使用

虽然这个项目是为 Codex skill 格式整理的，但核心流程都写在 `SKILL.md` 里。理论上，Claude Code 或其他能读取 Markdown 指令的 coding agent 也可以理解并执行其中的大部分流程。

需要注意：

- Codex 会根据 skill metadata 自动触发；其他 agent 可能需要你手动把 `SKILL.md` 作为上下文提供给它。
- 涉及本机命令、WSL、Windows 启动项、GitHub、Telegram token 的步骤，仍然需要用户授权或在本机安全输入。
- 不同 agent 的工具权限不同，实际执行方式可能会调整，但诊断顺序和安全原则是一致的。

## 安全原则

使用或维护这个项目时，请遵守以下规则：

- 不要提交 `~/.openclaw`。
- 不要提交 Telegram bot token、API key、模型凭据、auth profile。
- 不要提交原始日志、包含 token 的截图、本机启动脚本或机器专属配置。
- 不要在聊天里粘贴 bot token、模型 API key 或 auth profile；这些内容应通过本地终端提示或服务商 UI 输入。
- 在最终验证 Telegram 前，必须让用户确认 OpenClaw 的文件可见范围、工具权限和执行权限。
- 不要为了让 Telegram 跑通而放宽文件系统边界或执行策略。
- 不要默认开启模型 fallback，除非用户明确选择。
- keepalive 是基础设施，应该安静可靠地存在，但不要留下不必要的可见命令行窗口。
- network recovery watchdog 是断网恢复基础设施，只应记录状态并在确认网络恢复时重启 gateway 一次；它必须带防抖、冷却和 gateway 启动宽限期，不应因为一次短暂探测失败或启动期间依赖补装反复重启，也不应提交本机日志或机器专属状态文件。

## 维护与发布检查

提交或发布前，建议至少检查一次：

```powershell
git status
Select-String -Path .\openclaw-telegram-wsl-setup\SKILL.md -Pattern '\d{8,12}:[A-Za-z0-9_-]{25,}'
Select-String -Path .\openclaw-telegram-wsl-setup\SKILL.md -Pattern 'token|api_key|secret|password'
```

第一条 token 正则不应该命中任何真实 Telegram token。

第二条关键词扫描可能会命中安全说明、示例变量名或 token-file 示例，这是正常的；但不应该暴露真实密钥。

## 当前状态

这个 skill 已经覆盖从新机安装到常见故障修复的完整路径，尤其强调这些事：

1. **Ubuntu on WSL2 是推荐默认路径。**
2. **keepalive/autostart 是 OpenClaw Telegram 稳定运行的基础设施。**
3. **长时间断网恢复要作为基础设施处理，避免网络恢复后 stale socket / polling stall 影响行动；watchdog 必须防抖并尊重 gateway 启动宽限期，避免误重启造成 Telegram 延迟。**
4. **接入 Telegram 前要先确认 OpenClaw 本地模型可以正常回复。**
5. **OpenClaw 的可见范围和权限范围必须由用户确认，且可以用自然语言表达。**

后续可以继续改进的方向包括：

- 拆分过长的 `SKILL.md`，把详细故障案例放进 `references/`。
- 增加更正式的英文 README。
- 增加用于公开发布的示例 prompt。
- 增加一个最小化验证脚本，检查 skill frontmatter 和敏感信息。
