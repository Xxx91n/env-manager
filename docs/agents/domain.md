# Domain Docs

How the engineering skills consume this repo's domain documentation.

## Before exploring, read this

- **`GLOSSARY.md`** at the repo root: the canonical vocabulary, with an _Avoid_ line per term. Read it before naming anything in an issue, a refactor proposal, a test, or a commit message.

There is no separate ADR tree. Decisions that survive are written as comments at the code they constrain, so read the file you are about to change before changing it.

## File structure

Single-context repo.

```
/
+- GLOSSARY.md      <- domain terms only, no implementation details
+- docs/            <- user-facing and build documentation
+- src/             <- C# CLI (Program.cs thin dispatch + CliRuntime.cs shared runtime + per-domain modules)
+- service/         <- Rust service crate
+- frontend/        <- Tauri GUI application
```

No `GLOSSARY-MAP.md` at the root -> single-context, not multi-context.

## Use the glossary's vocabulary

When your output names a domain concept, use the term as defined in `GLOSSARY.md`. Don't drift to the synonyms a term explicitly avoids.

If the concept you need isn't in the glossary, that's a signal: either you're inventing language the project doesn't use (reconsider), or there's a real gap (add the term when it gets resolved).

## Contradictions

If code and glossary disagree, the code is what runs. Fix the glossary in the same change, or file an issue — but never let the contradiction pass silently.
