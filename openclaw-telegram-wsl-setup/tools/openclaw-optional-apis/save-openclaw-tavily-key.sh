#!/usr/bin/env bash
set -euo pipefail

export PATH="$HOME/.local/bin:$HOME/.npm-global/bin:/usr/local/bin:/usr/bin:/bin:$PATH"

OPENCLAW_BIN="${OPENCLAW_BIN:-$(command -v openclaw || true)}"
if [[ -z "$OPENCLAW_BIN" ]]; then
  echo "openclaw command not found in WSL PATH." >&2
  exit 3
fi

read -r TAVILY_API_KEY_INPUT
TAVILY_API_KEY_INPUT="${TAVILY_API_KEY_INPUT//$'\r'/}"
TAVILY_API_KEY_INPUT="${TAVILY_API_KEY_INPUT//$'\n'/}"
TAVILY_API_KEY_INPUT="${TAVILY_API_KEY_INPUT#TAVILY_API_KEY=}"
TAVILY_API_KEY_INPUT="${TAVILY_API_KEY_INPUT#tavily_api_key=}"
TAVILY_API_KEY_INPUT="${TAVILY_API_KEY_INPUT#Bearer }"

if [[ -z "$TAVILY_API_KEY_INPUT" ]]; then
  echo "Empty Tavily API key." >&2
  exit 2
fi

SECRETS_DIR="$HOME/.openclaw/secrets"
ENV_FILE="$SECRETS_DIR/tavily.env"
SYSTEMD_DIR="$HOME/.config/systemd/user/openclaw-gateway.service.d"
SYSTEMD_DROPIN="$SYSTEMD_DIR/tavily.conf"

mkdir -p "$SECRETS_DIR" "$SYSTEMD_DIR"
umask 077
printf 'TAVILY_API_KEY=%s\n' "$TAVILY_API_KEY_INPUT" > "$ENV_FILE"
chmod 600 "$ENV_FILE"

cat > "$SYSTEMD_DROPIN" <<EOF
[Service]
EnvironmentFile=$ENV_FILE
EOF

"$OPENCLAW_BIN" config set plugins.entries.tavily.enabled true --strict-json
"$OPENCLAW_BIN" config set plugins.entries.tavily.config.webSearch.apiKey \
  --ref-provider default --ref-source env --ref-id TAVILY_API_KEY
"$OPENCLAW_BIN" config set tools.web.search.enabled true --strict-json
"$OPENCLAW_BIN" config set tools.web.search.provider tavily
"$OPENCLAW_BIN" config set tools.web.search.maxResults 5 --strict-json
"$OPENCLAW_BIN" config set tools.web.search.timeoutSeconds 20 --strict-json
"$OPENCLAW_BIN" config validate

systemctl --user daemon-reload >/dev/null 2>&1 || true

echo "Tavily key saved for OpenClaw web search."
echo "Gateway restart is required before the running gateway can use it."
