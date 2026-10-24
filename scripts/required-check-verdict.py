#!/usr/bin/env python3
"""Emit "<check name>=<conclusion>" for the checks the auto-merge gate waits on.

Reads a GitHub check-runs API payload on stdin and prints one line per wanted
check. A check that has not reported yet, or has reported without a conclusion,
prints as ``pending`` so the caller's polling loop keeps waiting.

Extracted from the inline ``python3 -c`` block in
``.github/workflows/dependabot-auto-merge.yml``. That block sat at column 0
inside a ``run: |`` literal scalar, which ends the scalar and makes the file
unparseable as YAML -- the workflow therefore never started a single job.
"""

import json
import sys

WANTED = (
    "CI/CD Build and Release / verify",
    "Dependency Review / dependency-review",
)


def main():
    data = json.load(sys.stdin)
    found = {}
    for cr in data.get("check_runs", []):
        name = cr.get("name")
        if name in WANTED:
            found[name] = cr.get("conclusion") or "pending"
    for name in WANTED:
        print(name + "=" + str(found.get(name, "pending")))


if __name__ == "__main__":
    main()
