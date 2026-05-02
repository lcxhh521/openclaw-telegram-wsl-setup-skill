#!/usr/bin/env bash
set -euo pipefail

secret_dir="$HOME/.openclaw/secrets"
secret_file="$secret_dir/notion.env"

mkdir -p "$secret_dir"
chmod 700 "$secret_dir"

echo "Paste Notion Installation access token, then press Enter."
echo "Input is hidden. Use the Copy button from Notion."
printf "NOTION_TOKEN: "
IFS= read -r -s token
printf "\n"

token="$(printf "%s" "$token" | tr -d '\r\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
if [[ -z "$token" ]]; then
  echo "Empty token, nothing written." >&2
  exit 1
fi
if [[ "$token" != ntn_* ]]; then
  echo "Token does not start with ntn_. Check that you copied the Installation access token only." >&2
  exit 1
fi

echo "Paste the Notion parent page URL or page ID that this integration can access."
printf "NOTION_PARENT_PAGE_ID or URL: "
IFS= read -r parent_page_input
parent_page_input="$(printf "%s" "$parent_page_input" | tr -d '\r\n' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
if [[ -z "$parent_page_input" ]]; then
  echo "Empty parent page, nothing written." >&2
  exit 1
fi

parent_page_id="$(
  python3 - "$parent_page_input" <<'PY'
import re
import sys

raw = sys.argv[1].strip()
compact = raw.replace("-", "")
matches = re.findall(r"[0-9a-fA-F]{32}", compact)
if not matches:
    raise SystemExit(1)
print(matches[-1].lower())
PY
)" || {
  echo "Could not find a 32-character Notion page ID in that input." >&2
  exit 1
}

echo "Token length: ${#token}"
echo "Testing Notion authentication from inside WSL..."

http_code="$(
  curl -sS -o /tmp/openclaw_notion_me.json -w "%{http_code}" \
    -H "Authorization: Bearer $token" \
    -H "Notion-Version: 2022-06-28" \
    https://api.notion.com/v1/users/me
)"

if [[ "$http_code" == "200" ]]; then
  echo "Notion authentication: OK"
  python3 - <<'PY'
import json
from pathlib import Path
data = json.loads(Path("/tmp/openclaw_notion_me.json").read_text(encoding="utf-8"))
print("Bot name:", data.get("name"))
print("Bot id:", data.get("id"))
PY
else
  echo "Notion authentication failed: HTTP $http_code" >&2
  cat /tmp/openclaw_notion_me.json >&2
  exit 1
fi

echo "Testing Notion parent page access..."
page_http_code="$(
  curl -sS -o /tmp/openclaw_notion_parent_page.json -w "%{http_code}" \
    -H "Authorization: Bearer $token" \
    -H "Notion-Version: 2022-06-28" \
    "https://api.notion.com/v1/pages/$parent_page_id"
)"

if [[ "$page_http_code" == "200" ]]; then
  echo "Notion parent page access: OK"
else
  echo "Notion parent page access failed: HTTP $page_http_code" >&2
  cat /tmp/openclaw_notion_parent_page.json >&2
  echo "Make sure the integration has access to the target page in Notion Content access." >&2
  exit 1
fi

{
  printf "NOTION_TOKEN=%s\n" "$token"
  printf "NOTION_PARENT_PAGE_ID=%s\n" "$parent_page_id"
} > "$secret_file"
chmod 600 "$secret_file"

echo "Done."
