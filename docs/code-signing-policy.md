# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## Scope

Started with the first SignPath-approved release, every distributed Windows binary
(`env-manager.exe`, `env-manager-cli.exe`, `env-manager-service.exe` and the
MSI installer) is Authenticode-signed during the GitHub Actions release pipeline.
Builds older than that release are unsigned.

## Team roles

- **Authors / Committers**: [@Xxx91n](https://github.com/Xxx91n) (repository owner)
- **Reviewers**: [@Xxx91n](https://github.com/Xxx91n)
- **Approvers** (signing approval for each release): [@Xxx91n](https://github.com/Xxx91n)

This is a single-maintainer project; all three roles are held by the repository
owner. All accounts interacting with the signing pipeline use multi-factor
authentication.

## Builds

Binaries are built from source in a publicly verifiable way:
every release artifact is produced by the GitHub Actions workflow in this
repository from the release tag's source tree. The signing request for each
release is approved manually by the Approver before artifacts are published.

## Privacy policy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.
Update checking (if enabled by the user) contacts only the project's own GitHub
release endpoint.
