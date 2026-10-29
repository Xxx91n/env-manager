# PROTOTYPE — p3 boundary probes (V1 / V2 / V3)

**Throwaway observation code. It answers three questions and is not meant to be merged.**
Branch: `prototype/p3-boundary-probes`. Nothing here is a decision; the verdicts go back to
the user first, then to `/to-spec` for issues #160 #154 #161.

## What each leg measures

| Leg | Question | Produces |
| --- | --- | --- |
| `v1-pipe-dacl.ps1` | What is the *actual* DACL on `\\.\pipe\EnvManager.Service` when the service runs as LocalSystem, and does a declared DACL keep the shipped clients working while refusing a non-privileged identity? | pipe SDDL per policy, `SetNamedSecurityInfoW` Win32 result, a connect matrix per identity and logon type, anti-squatting result |
| `v2-helper-residue.ps1` | When the service stops mid-rotate, does the provider helper survive and leave `%TEMP%\env-manager-sops-<guid>\secret.dec.json` in plaintext? | process snapshots before/after `sc stop`, residue listings with a plaintext marker test, helper spawn counts |
| `v3-rotate-timing.ps1` | How long does one rotate actually take as a function of secret count and provider type? | `v3-rotate-timing.csv`: provider, secrets, path, duration, helper process count |

## How it runs

Only through the dispatch-only `proto-p3-observations` job in `.github/workflows/build.yml`
on this branch — there is no local build or local run, and the legs install and remove a
real Windows service, so they belong on a disposable VM.

```bash
gh workflow run build.yml --ref prototype/p3-boundary-probes -f proto_leg=v1-pipe-dacl
gh run watch        # then read the job log and the proto-evidence artifact
```

`proto_leg` accepts `v1-pipe-dacl`, `v2-helper-residue`, `v3-rotate-timing`, `all`, or empty
(empty means an ordinary CI/CD run, so the prototype job never fires on a normal push).

The job stages `env-manager-cli.exe`, `env-manager-service.exe` and the sops stub into
`C:\proto\bin\app` and `C:\proto\bin\stub`. Each leg can also be run by hand on any Windows
machine that already has those binaries:

```powershell
pwsh -File prototype/p3-boundary-probes/v1-pipe-dacl.ps1 `
  -CliExe C:\proto\bin\app\env-manager-cli.exe `
  -ServiceExe C:\proto\bin\app\env-manager-service.exe
```

## Files

- `lib.ps1` — evidence writer, SDDL read/apply via `GetNamedSecurityInfoW` /
  `SetNamedSecurityInfoW`, token-privilege enabling, `sc` service helpers, secondary
  identities (standard user via `Start-Process -Credential`, S4U and Password logon
  scheduled tasks, SYSTEM scheduled task), process and temp-residue snapshots.
- `pipe-client-probe.ps1` — the client half of an access check, runnable as any identity.
- `seed-state.ps1` — builds profiles.json / secret-providers.json / secretMount.json as the
  identity that will run the rotate, so no machine-wide environment has to change.
- `residue-probe.ps1` — lists leftover `env-manager-sops-*` dirs and tests the plaintext marker.
- `sops-stub/` — a cipher-less sops stand-in that can hold still (`C:\proto\stub.json`
  controls the hold) and logs every start so a rotate's helper count is countable.
- `service/src/ipc.rs`, `service/src/pipe_dacl.rs` — the only production-tree edits, both
  marked `PROTOTYPE ... DO NOT MERGE`: an argv/env switch selecting pipe-security policy
  (`--proto-pipe-sd=post|creation`), plus `measure_set_pipe_dacl` returning the Win32 code
  the existing helper swallows, and a creation-time `SECURITY_ATTRIBUTES` path through
  `ServerOptions::create_with_security_attributes_raw`.

## Red lines observed by these scripts

- The service name must be exactly `EnvManagerService` (`service/src/scm.rs` registers the
  dispatch table under it), so the legs create and delete it themselves and refuse to start
  if one is already installed.
- Nothing writes the registry through env-manager; the only state touched is the CLI's own
  user-state files under `C:\proto` and, for the LocalSystem cell, `C:\ProgramData\EnvManager`
  and the LocalSystem profile.
- Every leg has a `finally` that removes the service, the stub install under
  `C:\Program Files\sops`, the scratch user `emp3low`, and stray `sops.exe` /
  `env-manager-*` processes, and deletes residue it created.
- Evidence is written as JSON lines (`evidence-<leg>.jsonl`) plus a summary block; the CI job
  uploads `test-results/proto/` as an artifact.

## Known artifact of the stub, not a finding

The stub copies values through instead of encrypting them, so the marker string is visible
inside `profiles.json` in the V2/V3 cells. That says nothing about the real sops provider;
it exists so a residue file can be proven to be plaintext.
