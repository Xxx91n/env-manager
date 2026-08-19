# Agent Reference

This directory holds agent-specific reference files consumed by engineering skills
that read from `docs/agents/` per the grill setup-matt-pocock-skills convention.

- `issue-tracker.md` — where issues live and how to interact with them
- `domain.md` — domain docs layout, glossary convention, and ADR conflict policy
- `hard-boundaries.md` — all project invariants and red lines (~108 KiB, extracted from AGENTS.md for progressive disclosure)
- `reference-index.md` — topic-to-file detailed reference index (~44 KiB, extracted from AGENTS.md)

These files are **not user documentation**. They serve agent workflows (triage,
wayfinding, domain-modeling, code safety) that need structured metadata about the repo.

The root [AGENTS.md](../../AGENTS.md) keeps a <32 KiB routing layer with top-level
constraints and pointers to these files. This follows the industry best practice
(Codex `project_doc_max_bytes` = 32 KiB cumulative budget, root AGENTS.md as
routing layer, domain-specific rules in `docs/agents/*.md` loaded on demand).
