# Agent Reference

This directory holds agent-specific reference files consumed by engineering skills
that read from `docs/agents/` per the grill setup-matt-pocock-skills convention.

- `issue-tracker.md` — where issues live, how to interact with them, triage labels
- `domain.md` — glossary convention and how to consume it
- `hard-boundaries.md` — invariants a change must not violate (58 lines)

These files are **not user documentation**. They serve agent workflows (triage,
wayfinding, domain-modeling, code safety) that need structured metadata about the repo.

The root [AGENTS.md](../../AGENTS.md) stays a short routing layer with top-level
constraints and pointers to these files.
