#!/usr/bin/env bash
set -euo pipefail

export PATH="$HOME/.local/bin:$HOME/.local/share/pnpm:$HOME/.npm-global/bin:/usr/local/bin:/usr/bin:/bin:$PATH"

OPENCLAW_BIN="${OPENCLAW_BIN:-$(command -v openclaw || true)}"
if [[ -z "$OPENCLAW_BIN" ]]; then
  echo "openclaw command not found in WSL PATH." >&2
  exit 3
fi

IFS= read -r JINA_API_KEY_INPUT
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT//$'\r'/}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT//$'\n'/}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#JINA_API_KEY=}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#JINA_AUTH_TOKEN=}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#Authorization:}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#Authorization=}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#Bearer }"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT%\"}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#\"}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT%\'}"
JINA_API_KEY_INPUT="${JINA_API_KEY_INPUT#\'}"

if [[ -z "$JINA_API_KEY_INPUT" ]]; then
  echo "Empty Jina API key." >&2
  exit 2
fi

SECRETS_DIR="$HOME/.openclaw/secrets"
ENV_FILE="$SECRETS_DIR/jina.env"
SYSTEMD_DIR="$HOME/.config/systemd/user/openclaw-gateway.service.d"
SYSTEMD_DROPIN="$SYSTEMD_DIR/jina.conf"

mkdir -p "$SECRETS_DIR" "$SYSTEMD_DIR"
umask 077
printf 'JINA_API_KEY=%s\n' "$JINA_API_KEY_INPUT" > "$ENV_FILE"
chmod 600 "$ENV_FILE"

cat > "$SYSTEMD_DROPIN" <<EOF
[Service]
EnvironmentFile=$ENV_FILE
EOF

"$OPENCLAW_BIN" config set agents.defaults.memorySearch.enabled true --strict-json
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.provider openai
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.model jina-embeddings-v4
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.remote.baseUrl https://api.jina.ai/v1
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.remote.apiKey \
  --ref-provider default --ref-source env --ref-id JINA_API_KEY
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.fallback none
"$OPENCLAW_BIN" config set agents.defaults.memorySearch.remote.batch.enabled false --strict-json
"$OPENCLAW_BIN" config validate

systemctl --user daemon-reload >/dev/null 2>&1 || true

echo "Jina key saved for OpenClaw memory search."
echo "Gateway restart is required before the running gateway can use it."
