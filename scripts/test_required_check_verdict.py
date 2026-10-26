#!/usr/bin/env python3
"""CLI-seam tests for required-check-verdict.py.

The parser feeds the auto-merge gate, so these cases pin the shapes ``gh api``
actually emits: one JSON document per page (concatenated with no separator), a
single document, and a bare array. A malformed payload must fail the gate
loudly instead of masquerading as a pending check for the whole poll window.
"""

import json
import subprocess
import sys
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("required-check-verdict.py")


def check_run(name, conclusion, run_id=1, started_at="2026-10-24T12:31:59Z"):
    return {
        "id": run_id,
        "name": name,
        "conclusion": conclusion,
        "started_at": started_at,
    }


def page(runs):
    return json.dumps({"total_count": len(runs), "check_runs": runs})


def verdict(stdin_text):
    result = subprocess.run(
        [sys.executable, str(SCRIPT)],
        input=stdin_text,
        capture_output=True,
        text=True,
        check=True,
    )
    return dict(line.split("=", 1) for line in result.stdout.splitlines())


class RequiredCheckVerdictTests(unittest.TestCase):
    def test_reports_each_wanted_check_in_order(self):
        payload = page([
            check_run("dependency-review", "success"),
            check_run("verify", "success"),
            check_run("actionlint", "success"),
            check_run("package", "success"),
        ])
        self.assertEqual(
            list(verdict(payload)),
            ["actionlint", "package", "verify", "dependency-review"],
        )

    def test_missing_and_incomplete_checks_are_pending(self):
        payload = page([check_run("verify", None), check_run("package", "success")])
        out = verdict(payload)
        self.assertEqual(out["verify"], "pending")
        self.assertEqual(out["actionlint"], "pending")
        self.assertEqual(out["dependency-review"], "pending")

    def test_concatenated_pages_are_all_read(self):
        # gh api --paginate prints one document per page with no separator.
        payload = page([check_run("verify", "success")]) + page([
            check_run("package", "failure"),
            check_run("actionlint", "success"),
            check_run("dependency-review", "success"),
        ])
        out = verdict(payload)
        self.assertEqual(out["verify"], "success")
        self.assertEqual(out["package"], "failure")

    def test_bare_array_is_accepted(self):
        payload = json.dumps([check_run("verify", "success")])
        self.assertEqual(verdict(payload)["verify"], "success")

    def test_newest_run_of_a_name_wins(self):
        # A re-run may be queued with started_at null; the higher id is still
        # the authoritative newest run.
        payload = page([
            check_run("verify", "failure", run_id=1),
            check_run("verify", "success", run_id=2, started_at=None),
        ])
        self.assertEqual(verdict(payload)["verify"], "success")

    def test_workflow_prefixed_names_do_not_match(self):
        # The check-runs API reports the job name, not "workflow / job". Treating
        # the prefixed form as a match is what made every poll report pending.
        payload = page([
            check_run("CI/CD Build and Release / verify", "success"),
            check_run("Dependency Review / dependency-review", "success"),
        ])
        out = verdict(payload)
        self.assertEqual(out["verify"], "pending")
        self.assertEqual(out["dependency-review"], "pending")

    def test_invalid_payload_exits_nonzero(self):
        with self.assertRaises(subprocess.CalledProcessError):
            verdict('{"check_runs": [')

    def test_document_without_check_runs_exits_nonzero(self):
        with self.assertRaises(subprocess.CalledProcessError):
            verdict('{"message": "Not Found"}')


if __name__ == "__main__":
    unittest.main()
