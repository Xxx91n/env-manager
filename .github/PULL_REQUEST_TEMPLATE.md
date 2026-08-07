## Summary

<!-- Brief description of what this PR does and why -->

## Type of Change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Security hardening
- [ ] Build / CI improvement

## Checklist

- [ ] Code follows the project's style guidelines (`dotnet build` / `cargo clippy` / `npx vitest run`)
- [ ] Self-review completed
- [ ] Tests pass locally (`node scripts/build.mjs --arch x64` and `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-ci-tests.ps1`)
- [ ] `AGENTS.md` updated if a new hard boundary or invariant was introduced
- [ ] `CHANGELOG.md` updated under `[Unreleased]`
- [ ] No secret values or credentials in code, logs, or commit messages
- [ ] No new dependency added unless explicitly requested

## Related Issues

<!-- Link to any related issues: Fixes #123, Closes #456 -->

## Notes for Reviewer

<!-- Anything the reviewer should pay attention to -->