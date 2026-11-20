"""Antigravity Stop Hook: automatically verify module index integrity before ending a turn."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import check_project  # noqa: E402


def main() -> int:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except Exception:
        payload = {}

    # Avoid infinite loop if we already forced a continuation once in this turn
    if payload.get("continuationActive"):
        print(json.dumps({"decision": "allow"}))
        return 0

    if payload.get("terminationReason") != "NO_TOOL_CALL":
        print(json.dumps({"decision": "allow"}))
        return 0

    report_path = ROOT / "test-output" / "project-check.json"
    # Suppress check_project stdout so hook stdout stays clean JSON
    import io
    import contextlib

    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = check_project.check(report_path)

    if rc != 0:
        try:
            report = json.loads(report_path.read_text(encoding="utf-8"))
            errors = "; ".join(report.get("errors", []))
        except Exception:
            errors = "check_project.py failed"
        print(
            json.dumps(
                {
                    "decision": "continue",
                    "reason": (
                        f"[自动托管门禁] 检测到代码模块索引异常（{errors}）。"
                        "请同步更新 src/*/Modules/*.props 及相关架构/状态文档，确保 python tools/check_project.py 通过后再结束回复。"
                    ),
                },
                ensure_ascii=False,
            )
        )
        return 0

    print(json.dumps({"decision": "allow"}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
