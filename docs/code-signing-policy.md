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

## SignPath application checklist

Status: **v0.9.30 released unsigned on GitHub Releases (prerequisite "already released" met).** Next actions:

1. Maintainer opens an account at [signpath.io](https://about.signpath.io) and submits the OSS application at <https://signpath.org/apply.html> (repo URL: <https://github.com/Xxx91n/env-manager>).
2. Enable MFA on both the SignPath account and the GitHub account (SignPath Foundation requirement).
3. Answer review questions if any; typical review latency is days to weeks.
4. After approval, wire CI signing (draft below) and cut the first signed release (v0.9.31+).

### CI signing step (draft, adopt after approval)

```yaml
- uses: signpath/github-action-submit-signing-request@v1
  id: sign
  with:
    api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
    organization-id: ${{ vars.SIGNPATH_ORG_ID }}
    project-slug: env-manager
    signing-policy-slug: release-signing
    artifact-configuration-slug: msi-and-exe
    github-artifact-id: ${{ steps.upload.outputs.artifact-id }}
    wait-for-completion-timeout-in-seconds: 600
    output-artifact-directory: signed
```

Interesting properties:
- Each release needs one manual approval by the Approver in the SignPath portal (Foundation free-tier constraint, intended).
- Artifact metadata (ProductName = "Env Manager", same ProductVersion across all binaries) is enforced by SignPath artifact configuration; the 0.9.30 metadata alignment commit covers this.
