# bare-id scanner self-test fixture

Tracked fixture for `scripts/check-bare-ids.ps1 -SelfTest`. Each `BAD` line
carries one known-violating form and MUST be detected; each `SELFTESTOK` line
is legitimate text that MUST NOT be detected. The production scan excludes
this file by path, so the violating lines are safe to keep here.

BAD ticket 29 - bare ticket number in a comment.
BAD Issue 38 - capitalized bare issue number in a comment.
BAD v0.8 Phase 3 - two-segment version label that the old pattern missed.
BAD v0.9.14 Phase - three-segment version label before the phase word.
BAD Tickets 33/49 - plural noun carrying slash-joined numbers, the form a singular-only pattern misses.
BAD ADR 0001 - space-separated ADR number pointing at the retired ADR tree.
BAD ADR-0002 - hyphen-separated ADR number; a whitespace-only pattern would miss this dialect.
BAD ADR_0003 - underscore-separated ADR number; the separator class must stay visible to the gate.
BAD ADR0004 - ADR number with no separator; the optional-separator branch must stay covered.
BAD ADR  0005 - double-space ADR number; a single-whitespace branch would miss the reflowed form.
BAD ADRs 0006 - plural acronym plus number; a singular-only branch would miss it.
BAD ADR 101 - three-digit ADR number; the lower bound of the digit-width branch must stay covered.
BAD ticket-27 - hyphen-joined ticket number; a whitespace-only noun branch misses this dialect.
BAD Ticket27_Ramp - identifier form, the noun glued to digits with no separator at all.
BAD D-008 - ledger-style D-number pointing at the retired decision ledger.
BAD Verify_Ticket27_Ramp - underscore-prefixed glued identifier; a leading word-boundary assertion fails after an underscore.
BAD state_D-008.bak - underscore-prefixed ledger id; the leading boundary must tolerate an underscore prefix.
SELFTESTOK self-describing provenance: the defect was found by the metamorphic path pilot, no numbered id needed.
SELFTESTOK words like 3D-printed glue the letter to a word character, so no boundary precedes it and no ledger id is implied.
SELFTESTOK Tickets for 2024 keep a word between the noun and the number, so no bare id is implied.
SELFTESTOK a real GitHub issue link: https://github.com/env-manager/env-manager/issues/38
SELFTESTOK versions mentioned in prose, not as a leading label: the exporter landed before v0.8 shipped and the split into phases was decided later.
SELFTESTOK the ADR tree is retired; live docs point at the code or section that owns the decision.
SELFTESTOK the ADRs were folded into code comments; the plural acronym without a number is harmless.
