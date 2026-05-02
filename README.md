# OpenClaw 养虾指南

<p align="center">
  <img src="openclaw-telegram-wsl-setup/tools/openclaw-local-monitor/OpenClawMonitorIcon.png" alt="OpenClaw cute red mascot" width="120">
</p>

> 当前控制中心行为：打开 `OpenClaw Control` 只会显示本机状态，不会自动启动或关闭 OpenClaw。需要运行时点击 `开启 OpenClaw`；运行中按钮会变成 `关闭 OpenClaw`，再次点击才会真正关闭。`重新检测` 只重新读取状态，不负责启动或停止。Telegram 卡片只显示通道是否已连接；顶部状态框内部会在冷启动时显示启动进度条，标出 gateway、Telegram、模型和 sidecar 预热等阶段，进度到 100% 后自动消失。

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
  -> 本机 OpenClaw 控制中心
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
- 安装本机 OpenClaw 控制中心，用 Windows 原生小程序启动 OpenClaw、显示 gateway、Telegram、后台任务、TaskFlow、本地 daemon/产物心跳、Token/上下文流向、本地记录成本、最近会话和日志提醒，并支持系统托盘常驻。
- 控制中心可选提供 `Clash 安全模式`：针对为了 OpenClaw/Codex/国外大模型而开启 Clash Verge TUN 或全局式路由后，微信、腾讯服务、国内链接打不开的情况，让大模型流量跟随 `GLOBAL` 节点，同时让国内应用继续按规则直连；如果没有使用全局/TUN，或国内应用本来正常，一般不需要开启。
- 可选配置 Jina embeddings 和 Tavily web search：Jina 用于 `memorySearch` 语义记忆，Tavily 用于 `web_search` / 定期吸收互联网资料；这两项都不是基础安装必需项，用户不需要就跳过。
- 增加豆包/火山录音文件识别的本地工具层：通用 API key 可以用于文本模型，但音频识别还需要开通 `volc.bigasr.auc_turbo` 资源，并通过单独 ASR 脚本处理本地音频。
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
            |-- Generate-OpenClawMonitorIcon.ps1
            |-- Install-OpenClawMonitor.ps1
            |-- Install-Autostart.ps1
            |-- Uninstall-Autostart.ps1
            |-- OpenClawMonitor.ico
            `-- README.md
        `-- openclaw-doubao-asr/
            |-- openclaw-doubao-asr
            |-- Install-DoubaoAsrTool.ps1
            `-- README.md
        `-- openclaw-optional-apis/
            |-- Set-JinaApiKey.ps1
            |-- Set-TavilyApiKey.ps1
            |-- Repair-OpenClawMemoryDeepStatus.ps1
            |-- save-openclaw-jina-key.sh
            |-- save-openclaw-tavily-key.sh
            |-- repair-openclaw-memory-deep-status.py
            |-- Verify-JinaKey.py
            `-- Verify-TavilyKey.py
```

真正的 Codex skill 仍然是：

```text
openclaw-telegram-wsl-setup/
```

这个目录名暂时保留是为了兼容已经安装的 Codex skill 和旧链接；公开项目名称以 **OpenClaw 养虾指南** 为准。这个目录应保持干净，只包含 skill 本身需要的文件和可复用工具。不要把本机 OpenClaw 配置、Telegram token、日志、截图、编译产物或机器专属诊断文件放进去。

## 本机 OpenClaw 控制中心

仓库附带一个 Windows 原生控制中心：

```text
openclaw-telegram-wsl-setup/tools/openclaw-local-monitor/
```

它是本机的主入口，不替代 OpenClaw 官方浏览器 Control UI。打开后会先尝试唤醒 WSL、启动 `openclaw-gateway.service`，然后显示：

- gateway 和 Telegram 是否可用。
- 后台是否存在 `queued/running` task、活跃 TaskFlow，或正在持续产出的本地 daemon/工作区产物心跳。
- Token / 上下文使用快照，以及主会话、Telegram、子任务的流向。
- 从当月本地 session 日志里的 `usage.cost` 汇总已记录成本，并按模型列出成本和 token 去向；每个自然月刷新一次，这不是服务商账单替代品。
- 最近会话和 Telegram/error 日志提醒。
- 系统托盘常驻，最小化或关闭窗口时隐藏到托盘。

控制中心里的 `打开 Control` 按钮会调用本地 `Start-OpenClaw.ps1`。这个脚本只在本机临时解析 OpenClaw 网关令牌，并生成带 `#token=...` 的浏览器 Control URL；令牌不写进仓库、不打印到聊天、不提交到日志。这样用户不需要每次手动粘贴网关 token。脚本打开 URL 后会尽量把浏览器窗口恢复并拉到前台，让用户能看见这次点击确实生效。

控制中心会自动更新显示内容。界面上的 `重新检测` 按钮不是普通刷新按钮，而是手动触发一次主动检测：唤醒 WSL、尝试启动 gateway，然后重新读取当前状态。它不修改配置、不重置任务、不碰 token。

`Clash 安全模式` 只针对一个特定网络场景：用户为了让 OpenClaw、Codex 或其他国外大模型稳定走代理，开启了 Clash Verge 的 TUN 或全局式路由，但同时发现微信、腾讯服务或国内网页不能正常访问。开启后，控制中心会通过 Clash Verge Rev 暴露的本地 Mihomo 管道把核心维持在规则模式，让 OpenClaw/Codex 命中 `GLOBAL` 代理组，国内流量继续按规则直连。换节点时只需要在 Clash Verge 的 `GLOBAL` 组里选择节点；这个功能不绑定某个国家或具体节点。如果没有开全局/TUN，或者国内应用本来就正常，通常不用开启这个选项。

安装命令：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-local-monitor
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenClawMonitor.ps1
```

安装脚本会把源码复制到 `%LOCALAPPDATA%\OpenClawMonitor`，在本机编译 `OpenClawMonitor.exe`，创建 `OpenClaw Control` 桌面、开始菜单和 Startup-folder 快捷方式，清理旧的 `OpenClaw Monitor` / `OpenClaw 启动` 等旧入口，并启动控制中心。仓库不提交任何真实 token、API key、auth profile 或本机日志。

图标是透明背景的可爱红色 OpenClaw 小助手风格，避免使用带黑色背景的截图作为桌面或托盘图标。

## 可选 API 增强：Jina / Tavily

这部分不是基础安装必需项。只有当用户明确需要更强的语义记忆或联网检索时才配置：

- Jina embeddings：给 OpenClaw `memorySearch` 用，负责语义记忆和本地资料检索。
- Tavily web search：给 OpenClaw `web_search` 用，负责当前网页搜索或定期吸收互联网讨论。

本项目提供本地安全输入脚本，不要把 key 发到聊天里：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-optional-apis
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Set-JinaApiKey.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Set-TavilyApiKey.ps1
```

脚本会把 key 保存到 `~/.openclaw/secrets/jina.env` 或 `~/.openclaw/secrets/tavily.env`，并把 OpenClaw 配置指向环境变量 SecretRef。这里有一个容易踩坑的点：Jina 的 `memorySearch.remote.apiKey` 不能写成普通字符串 `env:JINA_API_KEY`，而应该用 OpenClaw 的 SecretRef 形式，否则运行时可能把这段字符串当成真正的 API key 发出去，导致看起来像 “Jina 401 Invalid API key”。

如果实际 `memory search` 已经可用，但 `openclaw memory status --deep` 里的 embedding 健康检查仍报 `fetch failed` / TLS socket disconnected，先不要让用户反复换 key。OpenClaw 2026.4.26 的新 CLI 入口可能会在 `memory` 命令启动时提前预热模型上下文窗口缓存，触发模型发现网络请求，并和 Jina embedding 探针同时走代理，造成健康检查误报。可以运行：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-optional-apis
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Repair-OpenClawMemoryDeepStatus.ps1
```

然后验证：

```bash
set -a; . ~/.openclaw/secrets/jina.env; set +a
openclaw memory status --deep --json
openclaw memory search --query "OpenClaw" --max-results 3 --json
```

默认不重启 gateway；如果用户希望立刻生效，再选择重启。否则下次 OpenClaw gateway 重启或电脑重启后自然生效。

## 豆包 / 火山录音文件识别

仓库也附带一个很小的 WSL 工具：

```text
openclaw-telegram-wsl-setup/tools/openclaw-doubao-asr/
```

它解决的是“本地音频转文字”这一层，不是把豆包聊天模型伪装成原生音频理解模型。当前结论是：

- 豆包文本模型可以做转写后的风格分析、taxonomy 复核、字幕/转写语气归纳。
- Ark 聊天接口不能直接替代 Gemini 做原生音频理解。
- 极速版录音文件识别默认资源 ID 是 `volc.bigasr.auc_turbo`，适合短音频、本地临时测试。
- 标准版录音文件识别默认资源 ID 是 `volc.seedasr.auc`，适合长音频或已经有公网 URL 的批量任务。
- 脚本只读取本机 `~/.openclaw/secrets/volcengine.env` 里的 key，不把 key 放进仓库。
- 极速版会把本地音频文件上传到火山引擎；标准版会把音频公网 URL 发给火山引擎。处理私人音频前必须得到用户明确同意。

安装命令：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-doubao-asr
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-DoubaoAsrTool.ps1
```

安装后可以先做本地自检，不上传音频：

```bash
openclaw-doubao-asr --self-check
```

如果火山语音服务页面给的是 `APP ID / Access Token`，用本地终端录入，不要发到聊天里：

```powershell
cd .\openclaw-telegram-wsl-setup\tools\openclaw-doubao-asr
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Set-DoubaoAsrCredentials.ps1
```

如果自检显示 key 存在、资源 ID 是 `volc.bigasr.auc_turbo`，但转写仍失败，优先去火山控制台确认“大模型录音文件识别”资源是否开通、项目是否有权限、套餐或额度是否可用。

实际使用时：

```bash
# 极速版：直接处理本地短音频
openclaw-doubao-asr --mode flash --text-only /path/to/audio.wav

# 标准版：提交火山服务器可访问的音频 URL
openclaw-doubao-asr --mode standard --url "https://example.com/audio.wav" --wait
```

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
