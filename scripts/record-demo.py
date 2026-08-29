#!/usr/bin/env python3
"""Deterministic README demo generator.

Runs the real release CLI inside a ConPTY (pywinpty) so output is the exact
console experience, then synthesizes an asciicast v2 (.cast) that `agg`
renders into docs/assets/demo.gif. Windows-friendly and CI-reproducible;
docs/assets/demo.tape remains the recording path where vhs works.

Usage:
    python scripts/record-demo.py              # capture + render gif (needs agg)
    python scripts/record-demo.py --cast-only

Safety: every command below is READ-ONLY. Do not add mutating commands.
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

from winpty import PtyProcess

REPO = Path(__file__).resolve().parent.parent
CLI = REPO / "bin" / "Release" / "net10.0-windows" / "env-manager-cli.exe"

COLS, ROWS = 110, 30
TYPE_S = 0.042  # per-character typing delay
PRE_SLEEP, POST_SLEEP, HOLD = 0.5, 1.0, 1.6

# Read-only commands only (hard boundary). Short machine-independent output.
SCRIPT = [
    (["agents", "--summary"], "Discoverable agent contract"),
    (["help"], "Command surface"),
    (["profile", "list"], "Profiles on this machine"),
]


def run_pty(argv: list[str]) -> str:
    """Run the CLI under a real console so output matches terminal formatting."""
    proc = PtyProcess.spawn([str(CLI), *argv], dimensions=(ROWS, COLS))
    chunks: list[str] = []
    while True:
        try:
            data = proc.read()
        except EOFError:
            break
        chunks.append(data)
        del data
        if not proc.isalive():
            break
    del proc
    return "".join(chunks).strip("\r\n")


def main() -> int:
    if not CLI.is_file():
        sys.exit(f"CLI binary not found: {CLI} (build first)")

    events: list[list] = []
    t = 0.4

    def emit(s: str, dt: float = 0.0) -> None:
        nonlocal t
        t = round(t + dt, 4)
        events.append([t, "o", s])

    prompt = "\x1b[32m$\x1b[0m env-manager-cli "

    for argv, note in SCRIPT:
        typed = " ".join(argv)
        emit(prompt)
        for ch in typed:
            emit(ch, TYPE_S)
        emit("\r\n", PRE_SLEEP)

        out = run_pty(argv)
        emit(out + "\r\n", POST_SLEEP)
        emit(f"\x1b[2m# {note}\x1b[0m\r\n\r\n", HOLD)

    cast_lines = [
        json.dumps(
            {
                "version": 2,
                "width": COLS,
                "height": ROWS,
                "timestamp": 1787940000,
                "env": {"TERM": "xterm-256color"},
            }
        ),
        *(json.dumps(e) for e in events),
    ]
    cast_path = REPO / "docs" / "assets" / "demo.cast"
    cast_path.write_text("\n".join(cast_lines) + "\n", encoding="utf-8")
    print(f"cast: {cast_path} ({len(events)} events, {t:.1f}s)")

    if "--cast-only" in sys.argv:
        return 0
    agg = shutil.which("agg")
    if not agg:
        sys.exit("agg not found in PATH (https://github.com/asciinema/agg)")
    subprocess.run(
        [
            agg,
            "--font-size",
            "16",
            "--theme",
            "111318,F2F4F7,4B5563,F59E0B,10B981,FBBF24,06B6D4,8B5CF6,67E8F9,E5E7EB",
            str(cast_path),
            str(REPO / "docs" / "assets" / "demo.gif"),
        ],
        check=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
