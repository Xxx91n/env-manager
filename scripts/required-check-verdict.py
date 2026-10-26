#!/usr/bin/env python3
"""Emit "<check name>=<conclusion>" for the checks the auto-merge gate waits on.

Reads GitHub check-runs API output on stdin and prints one line per wanted
check. A check that has not reported yet, or has reported without a
conclusion, prints as ``pending`` so the caller's polling loop keeps waiting.

The four names are the required branch-protection checks (``actionlint``,
``package``, ``verify``) plus the dependency-review gate. They are bare job
names: the check-runs API reports the job name, not "workflow / job" (the
prefixed form never matches, which made every poll report pending).

``gh api --paginate`` concatenates one JSON document per page with no
separator, so the reader accepts a single document, any number of concatenated
documents, and a bare array. A document without a ``check_runs`` list raises
instead of being mistaken for "still pending".
"""

import json
import sys

WANTED = (
    "actionlint",
    "package",
    "verify",
    "dependency-review",
)


def iter_check_runs(text):
    """Yield check-run objects from one or more concatenated JSON documents."""
    decoder = json.JSONDecoder()
    index = 0
    end = len(text)
    while True:
        while index < end and text[index].isspace():
            index += 1
        if index >= end:
            return
        document, index = decoder.raw_decode(text, index)
        if isinstance(document, list):
            yield from document
        elif isinstance(document, dict):
            runs = document.get("check_runs")
            if not isinstance(runs, list):
                raise ValueError("check-runs payload has no 'check_runs' list")
            yield from runs


def main():
    latest = {}
    for run in iter_check_runs(sys.stdin.read()):
        name = run.get("name")
        if name not in WANTED:
            continue
        # Check-run ids increase monotonically, so the highest id is the newest
        # run. started_at is null while a re-run is queued, which would make a
        # timestamp comparison prefer the stale completed run.
        run_id = run.get("id") or 0
        if name not in latest or run_id >= latest[name][0]:
            latest[name] = (run_id, run.get("conclusion") or "pending")
    for name in WANTED:
        conclusion = latest[name][1] if name in latest else "pending"
        print(name + "=" + conclusion)


if __name__ == "__main__":
    main()
