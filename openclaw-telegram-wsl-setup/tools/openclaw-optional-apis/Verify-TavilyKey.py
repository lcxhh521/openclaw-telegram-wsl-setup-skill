import json
import os
import sys
import urllib.error
import urllib.request


def main() -> int:
    key = os.environ.get("TAVILY_API_KEY", "").strip()
    if not key:
        print(json.dumps({"ok": False, "error": "missing TAVILY_API_KEY"}))
        return 2

    payload = {
        "query": "OpenClaw Tavily configuration test",
        "search_depth": "basic",
        "max_results": 1,
        "include_answer": False,
    }
    request = urllib.request.Request(
        "https://api.tavily.com/search",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {key}",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            data = json.loads(response.read().decode("utf-8"))
        print(json.dumps({"ok": True, "results": len(data.get("results", [])), "response_time": data.get("response_time")}, ensure_ascii=False))
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
