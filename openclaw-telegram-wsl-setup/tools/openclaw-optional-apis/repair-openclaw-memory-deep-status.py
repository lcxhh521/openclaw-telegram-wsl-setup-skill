#!/usr/bin/env python3
"""Patch OpenClaw's memory CLI health check false-negative path.

OpenClaw 2026.4.26 can eagerly warm the model context-window cache for the
`memory` command when launched through the new entry bootstrap. That warm-up may
start model-discovery network requests in parallel with the Jina embedding probe,
which can make `openclaw memory status --deep` report a TLS/fetch failure even
when `memory search` and the embedding provider actually work.

This script adds `memory` to the eager-warmup skip list. It is idempotent and
creates a sibling backup before editing the installed OpenClaw dist file.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path


def resolve_openclaw_dist() -> Path:
    cmd = subprocess.run(
        ["bash", "-lc", "command -v openclaw"],
        check=False,
        capture_output=True,
        text=True,
    )
    openclaw_bin = cmd.stdout.strip()
    candidates: list[Path] = []

    if openclaw_bin:
        resolved = Path(openclaw_bin).expanduser().resolve()
        if resolved.name in {"openclaw.mjs", "openclaw.js"}:
            candidates.append(resolved.parent / "dist")
        candidates.append(resolved.parent / "../lib/node_modules/openclaw/dist")

    candidates.extend(
        [
            Path.home() / ".local/lib/node_modules/openclaw/dist",
            Path("/usr/local/lib/node_modules/openclaw/dist"),
        ]
    )

    for candidate in candidates:
        candidate = candidate.resolve()
        if (candidate / "entry.js").exists() and any(candidate.glob("context-*.js")):
            return candidate

    raise SystemExit("OpenClaw dist directory was not found. Is OpenClaw installed in WSL?")


def patch_context_file(dist: Path) -> int:
    needle = "const SKIP_EAGER_WARMUP_PRIMARY_COMMANDS = new Set(["
    insert_after = '\t"logs",\n'

    for path in sorted(dist.glob("context-*.js")):
        text = path.read_text(encoding="utf-8")
        if needle not in text:
            continue

        start = text.index(needle)
        window = text[start : start + 900]
        if '"memory"' in window:
            print(f"already patched: {path}")
            return 0

        if insert_after not in window:
            raise SystemExit(f"Could not locate insertion point in {path}")

        backup = path.with_suffix(path.suffix + ".bak-memory-skip")
        if not backup.exists():
            shutil.copy2(path, backup)

        patched = text.replace(insert_after, insert_after + '\t"memory",\n', 1)
        path.write_text(patched, encoding="utf-8")
        print(f"patched: {path}")
        print(f"backup: {backup}")
        return 0

    raise SystemExit("OpenClaw context warmup file was not found.")


def main() -> int:
    dist = resolve_openclaw_dist()
    return patch_context_file(dist)


if __name__ == "__main__":
    raise SystemExit(main())
