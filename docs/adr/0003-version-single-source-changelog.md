# ADR 0003: Version Single Source & CHANGELOG

- Status: Accepted
- Date: 2026-08-06
- Deciders: grill-with-docs session
- Supersedes: None
- Related: None

## Context

The project has two manually-maintained version fields:
1. `env-manager.csproj` `<Version>0.9.0</Version>` (C# project)
2. `frontend/package.json` `"version": "0.9.0"` (NPM/Tauri)

These have drifted in the past (csproj updated, package.json forgotten or vice versa). There is no CHANGELOG.md. The release.yml is manual-trigger (workflow_dispatch) per user hard constraint.

PWM research (2026-08-06) identified two industry patterns:
- **release-please** (Google): auto-creates release PRs from Conventional Commits, human merges
- **semantic-release**: fully automated versioning + changelog + publish

Both are heavier than what this project needs. The user's hard constraint is "human-in-the-loop gate" — release-please adds a PR layer but the manual release.yml workflow_dispatch already satisfies this.

## Decision

### Version single source
- `env-manager.csproj` `<Version>` is the single source of truth
- `scripts/build.mjs` reads csproj version and syncs to `frontend/package.json` before every build
- Dev builds auto-sync; release builds verify consistency

### Manual CHANGELOG.md
- Added to repo root, following [Keep a Changelog](https://keepachangelog.com/) format
- Manually maintained by developers (not auto-generated from commits)
- release.yml reads the CHANGELOG.md section matching the release version and embeds it in the GitHub Release body

### No release-please/semantic-release
- The existing release.yml workflow_dispatch + manual version input is sufficient
- Adding release-please would create a second human-gate (PR merge) on top of the existing workflow_dispatch — redundant

## Consequences

**Positive:**
- One version to update (csproj only); build.mjs propagates
- CHANGELOG.md gives users upgrade guidance; release.yml auto-embeds
- No CI dependency on external release-please action
- Minimal complexity added (one sync function in build.mjs + one markdown file)

**Negative:**
- CHANGELOG maintenance is manual (but Conventional Commits discipline makes this easy)
- No auto-detection of "is this a breaking change" from commit history

**Neutral:**
- If the project grows to need auto-changelog, release-please can be added later without conflict
