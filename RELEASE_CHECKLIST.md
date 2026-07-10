# Env Manager - Release Checklist

This document guides the release process for new versions.

## Pre-Release (2 days before)

- [ ] Update version in:
  - `Program.cs` (const VERSION)
  - `env-manager.csproj`
  - `frontend/package.json`
  - `frontend/src-tauri/Cargo.toml`
  - `frontend/src-tauri/tauri.conf.json`

- [ ] Update documentation:
  - [ ] AGENTS.md - update Phase status and version
  - [ ] README.md - update current version
  - [ ] README_CN.md - update Chinese version
  - [ ] CHANGELOG.md - add [Unreleased] section for this version

- [ ] Run security audit:
  ```powershell
  semgrep --config=p/security-audit Program.cs frontend/src
  ```

- [ ] Update SECURITY_AUDIT.md with latest findings

- [ ] Test locally:
  ```powershell
  # Test CLI
  .\bin\Release\net10.0\env-manager.exe help
  .\bin\Release\net10.0\env-manager.exe list
  
  # Test backup/restore
  .\bin\Release\net10.0\env-manager.exe backup --output test.json
  .\bin\Release\net10.0\env-manager.exe validate test.json
  ```

## Release Day

- [ ] Create release branch:
  ```bash
  git checkout -b release/v0.x.0
  ```

- [ ] Final commits:
  ```bash
  git add .
  git commit -m "chore: prepare release v0.x.0"
  ```

- [ ] Create annotated tag:
  ```bash
  git tag -a v0.x.0 -m "Release v0.x.0"
  ```

- [ ] Push to main:
  ```bash
  git checkout main
  git merge --no-ff release/v0.x.0
  git push origin main
  git push origin v0.x.0
  ```

- [ ] Verify GitHub Actions:
  - [ ] lint job passes
  - [ ] build-cli job passes (CLI artifact created)
  - [ ] build-gui job completes
  - [ ] test job passes
  - [ ] release job triggers (automatic on tag)

## Post-Release

- [ ] Verify GitHub Release:
  - [ ] env-manager-vX.X.X.exe uploaded
  - [ ] Release notes generated
  - [ ] Download links work

- [ ] Announce release:
  - [ ] Update GitHub README with latest version
  - [ ] Add release link to CHANGELOG.md

- [ ] Cleanup:
  ```bash
  git branch -d release/v0.x.0
  ```

## Rollback Plan

If release has critical issues:

1. Delete the release tag:
   ```bash
   git tag -d v0.x.0
   git push origin --delete v0.x.0
   ```

2. Delete GitHub Release (via web UI)

3. Revert commits:
   ```bash
   git revert <commit-hash>
   git push origin main
   ```

4. Investigate and fix issues

5. Re-release with patch version (v0.x.1)

## Semantic Versioning Guide

- **PATCH** (v0.0.1 → v0.0.2): Bug fixes
- **MINOR** (v0.1.0 → v0.2.0): New features (backward compatible)
- **MAJOR** (v1.0.0 → v2.0.0): Breaking changes
