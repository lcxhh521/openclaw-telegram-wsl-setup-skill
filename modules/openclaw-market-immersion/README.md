# OpenClaw Market Immersion Module

这是一个 OpenClaw 长期任务模块，不是交易建议系统。

它的职责是到点收集市场快讯流，保留完整原始信息，再交给 OpenClaw 做轻整理，最后生成本地归档；如果用户明确启用，也可以发布到 Notion 或推送到 Telegram。

## 模块边界

- 模块类型：OpenClaw job module
- 入口脚本：`scripts/run_market_immersion.sh <phase>`
- 主程序：`scripts/market_immersion.py`
- 配置文件：`config/market_immersion_config.json`
- systemd 定时器：`systemd/*.timer`
- 输出目录：`~/.openclaw/workspace/market-immersion`

## 阶段

- `morning`：09:05 盘前
- `midday`：12:15 午间
- `close`：15:20 收盘
- `night`：22:10 夜间
- `smoke`：连通性测试，不发布 Notion / Telegram

## 闭环逻辑

1. systemd timer 到点触发对应 phase。
2. 模块按时间窗口拉取多源 7x24 快讯流。
3. 每个源必须扫到窗口起点，否则任务失败并等待重试。
4. OpenClaw 必须完成 1-8 栏轻整理，否则任务失败。
5. 第 9 栏保留按时间顺序排列的完整原始消息流。
6. 如果用户在配置里启用 Notion，正式阶段必须成功发布 Notion。
7. 如果用户在配置里启用 Telegram，正式阶段会尝试推送日报链接或文件。
8. 必要的闭环步骤全部成功后才更新 `state.json` 的 `last_success_at`。

## 当前信息源

- 东方财富财经资讯与 7x24 快讯栏目
- 财联社电报
- 金十数据快讯
- 新浪财经 7x24
- 华尔街见闻 7x24

## 运行

```bash
~/.openclaw/workspace/market-immersion-module/scripts/run_market_immersion.sh morning
```

查看定时器：

```bash
systemctl --user list-timers "openclaw-market-immersion*" --all
```

查看最近日志：

```bash
journalctl --user -u openclaw-market-immersion-morning.service -n 100 --no-pager
```
