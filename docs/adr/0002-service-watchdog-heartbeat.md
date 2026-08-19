# ADR 0002: Service Watchdog & Heartbeat

- Status: Accepted
- Date: 2026-08-06
- Deciders: design review session
- Supersedes: None
- Related: ADR 0001 (Service-Oriented Secret Lifecycle)

## Context

The env-manager-service.exe runs in two modes: Service (SCM) and Background (GUI-spawned). When the service process crashes:

1. **Service mode**: SCM has native failure recovery (restart on crash), but the WiX installer does not configure failure actions. The service stays dead until manual intervention.
2. **Background mode**: No recovery mechanism exists. The GUI-spawned detached process crashes silently. The user only discovers the service is dead when they reopen the GUI and see "service not responding".

PWM research (2026-08-06) confirmed industrial Windows service patterns require:
- Internal watchdog monitoring worker thread health
- Heartbeat endpoint distinguishing "busy" from "deadlocked"
- SCM native recovery configuration
- Named pipe auto-reconnect with retry/backoff

Reference: [SO 1463689](https://stackoverflow.com/q/1463689), [FireDaemon KB](https://kb.firedaemon.com/support/solutions/articles/4000086193), [zylos.ai supervision trees](https://zylos.ai/research/2026-03-16-supervisor-trees-fault-tolerance-ai-agent-systems/)

## Decision

### Two-layer watchdog

**Layer 1: SCM Recovery (Service mode)**
- WiX installer configures `sc failure` with 3x restart at 60s intervals
- 24h reset counter
- Zero code change in service binary — purely installer configuration

**Layer 2: GUI Watchdog (Background mode)**
- Tauri command spawns a watchdog thread on GUI startup
- Thread pings service every 30s via named pipe
- 2 consecutive ping failures trigger auto-restart via start_service()
- Watchdog is defense-in-depth: works in both Service and Background modes
- Thread dies with GUI process (GUI exit does not kill the service, only the watchdog monitor)

### Heartbeat enrichment

Service `ping` IPC response enhanced with:
- `uptime_seconds`: process uptime for liveness verification
- `reconcile_last_run_at`: last successful reconcile loop timestamp
- `reconcile_next_run_at`: next scheduled run (detects scheduling deadlock)
- `mount_count` / `healthy_mounts`: mount health summary

This distinguishes "service is alive but busy" from "service is deadlocked" — the GUI watchdog only auto-restarts when uptime resets (crash detected) or ping fails entirely.

### CLI pipe connect retry

CLI `RunServiceCommand` retries pipe connect 3 times with 1s exponential backoff before returning error. Prevents transient pipe-busy failures from surfacing to the user.

## Consequences

**Positive:**
- Service crash → automatic recovery in both modes (SCM 60s, GUI watchdog 60s)
- No external supervisor process needed (no Docker Desktop backend complexity)
- Heartbeat data enables future "service health" dashboard in GUI
- CLI retry prevents false-negative "service not responding" on transient congestion

**Negative:**
- Watchdog thread adds one background thread to GUI process (negligible)
- SCM sc failure config requires admin to install (already required for MSI)
- Auto-restart may mask underlying issues — logs must record restart events for diagnosis

**Neutral:**
- Watchdog only monitors while GUI is running; if GUI is closed and Background mode service crashes, no auto-restart occurs (by design — Background mode is opt-in; Service mode is the persistent path)
