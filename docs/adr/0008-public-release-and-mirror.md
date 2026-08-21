# ADR 0008: Public Release & Multi-Platform Mirror Strategy

Date: 2026-08-21
Status: Accepted

## Context

Grill session 2026-08-21 decided to move the repository from private to public on GitHub and establish a low-maintenance mirror topology to GitLab and Codeberg. Third-party distribution channels (winget, Scoop, Chocolatey) are deferred behind the explicit "开始发布" user gate. Tauri updater prep (ed25519) is included now because it is free, mandatory, and independent of the signing decision.

## Decision

1. **Visibility gate**: Flip GitHub repo to public only after a clean `gitleaks git .` full-history scan (exit 0). Any historical PAT / token leakage triggers key revocation before visibility change.
2. **PAT retirement**: The `git push` PAT-over-HTTPS workflow is retired. Global SSH (`git@github-Xxx91n:...`) is the canonical authentication. The AGENTS.md paragraph describing the PAT pattern is removed in the same commit as the public flip.
3. **Local first, remote second (release posture)**: CHANGELOG.md is brought up to date from `git log` (0.9.6-0.9.26 sections documented); no `git tag` or GitHub Release is created in this phase.
4. **release-please**: After the public flip, `googleapis/release-please-action` (release-please.yml) owns future CHANGELOG / version PRs driven by conventional commits.
5. **Attestation**: `actions/attest-build-provenance` is attached to the tag-driven build.yml release job so that artifacts published in the future carry SLSA L2 provenance out of the box.
6. **Mirrors**: GitLab + Codeberg are secondary read-only mirrors synchronized by `qte77/gha-github-mirror-action` on every push to `main`. Mirror credentials are stored in GitHub Secrets (`GITLAB_TOKEN`, `CODEBERG_TOKEN`); the corresponding user names are stored in GitHub Actions variables (`GITLAB_USER`, `CODEBERG_USER`).
7. **README / i18n**: The README switches from screenshot placeholders to logo-asset presentation (logo.png, logo-dark-theme.png, logo-light-theme.png, env_variants_showcase.png). Localized READMEs live under `docs/i18n/README.<locale>.md` (boxing pattern), and the language switcher marker `<!-- README-I18N:START/END -->` is kept in every locale file.
8. **Tauri updater**: ed25519 key pair generated; the public key is embedded into `frontend/src-tauri/tauri.conf.json` under `plugins.updater.pubkey`; the private key is kept in GitHub Secrets (`TAURI_SIGNING_PRIVATE_KEY`, `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`) and only ever used by CI.

## Consequences

- Public visibility becomes a cheap reversible-by-policy operation (GitHub allows toggling back private), while the PAT history risk is permanently removed.
- Mirror push is fully automated via GitHub Actions; manual push to GitLab / Codeberg is explicitly out-of-band and discouraged.
- Phase 2 items not precluded by this ADR remain scoped to the "开始发布" gate: winget submission via `vedantmgoyal9/winget-releaser`, Authenticode signing (SignPath Foundation or Azure Artifact Signing), Scoop / Chocolatey evaluation.
