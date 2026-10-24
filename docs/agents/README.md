# Agent Reference

This directory holds agent-specific reference files consumed by engineering skills
that read from `docs/agents/` per the grill setup-matt-pocock-skills convention.

- `issue-tracker.md` — where issues live and how to interact with them
- `domain.md` — glossary convention and how to consume it
- `hard-boundaries.md` — all project invariants and red lines (232 lines)
- `reference-index.md` — topic-to-file detailed reference index (98 lines)

These files are **not user documentation**. They serve agent workflows (triage,
wayfinding, domain-modeling, code safety) that need structured metadata about the repo.

The root [AGENTS.md](../../AGENTS.md) stays a 19-line routing layer with top-level
constraints and pointers to these files.
