import json
import os
import sys
import urllib.error
import urllib.request


def main() -> int:
    key = os.environ.get("JINA_API_KEY", "").strip()
    if not key:
        print(json.dumps({"ok": False, "error": "missing JINA_API_KEY"}))
        return 2

    payload = {"model": "jina-embeddings-v4", "input": ["OpenClaw memory embedding test"]}
    request = urllib.request.Request(
        "https://api.jina.ai/v1/embeddings",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": "application/json",
            "User-Agent": "OpenClaw-Optional-API-Setup/1.0",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            data = json.loads(response.read().decode("utf-8"))
        embedding = data.get("data", [{}])[0].get("embedding", [])
        print(json.dumps({"ok": True, "dims": len(embedding), "usage": data.get("usage")}, ensure_ascii=False))
        return 0
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", "replace")[:500]
        print(json.dumps({"ok": False, "http_status": exc.code, "body": body}, ensure_ascii=False))
        return 1
    except Exception as exc:
        print(json.dumps({"ok": False, "error": type(exc).__name__, "message": str(exc)[:300]}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
