# bare-id scanner self-test fixture

Tracked fixture for `scripts/check-bare-ids.ps1 -SelfTest`. Each `BAD` line
carries one known-violating form and MUST be detected; each `SELFTESTOK` line
is legitimate text that MUST NOT be detected. The production scan excludes
this file by path, so the violating lines are safe to keep here.

BAD ticket 29 - bare ticket number in a comment.
BAD Issue 38 - capitalized bare issue number in a comment.
BAD v0.8 Phase 3 - two-segment version label that the old pattern missed.
BAD v0.9.14 Phase - three-segment version label before the phase word.
SELFTESTOK a real GitHub issue link: https://github.com/env-manager/env-manager/issues/38
SELFTESTOK versions mentioned in prose, not as a leading label: the exporter landed before v0.8 shipped and the split into phases was decided later.
