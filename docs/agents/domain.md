# Domain Docs

Before working in this repo, read:

- **`CONTEXT.md`** at the repo root — the domain glossary (canonical vocabulary, no implementation details)
- **`docs/adr/`** — read ADRs that touch the area you're about to work in

If any of these files don't exist, **proceed silently**. The `/domain-modeling` skill creates them lazily when terms or decisions actually get resolved.

## File structure

This repo is **single-context**.

```
/
+- CONTEXT.md          <- glossary (domain terms only, no implementation details)
+- docs/adr/           <- architecture decision records
|   +- 0001-secret-architecture-revision.md
|   +- 0002-service-watchdog-heartbeat.md
|   +- 0003-version-single-source-changelog.md
+- docs/agents/        <- this directory (agent-specific reference)
+- docs/history/        <- process artifacts (audit logs, session records)
+- src/                <- C# CLI implementation (Program.cs thin dispatch + CliRuntime.cs shared runtime + command-domain modules)
+- service/            <- Rust service crate
+- frontend/           <- Tauri GUI application
```

No `CONTEXT-MAP.md` at the root -> single-context, not multi-context.

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

When you spot a decision in code or docs that contradicts an ADR, flag it. The ADR is the source of truth for *what was decided*; if the code drifted, the ADR should be updated or the code should be corrected — but the contradiction should never pass silently.
