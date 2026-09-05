<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../../docs/assets/logo-dark-theme.png">
  <source media="(prefers-color-scheme: light)" srcset="../../docs/assets/logo-light-theme.png">
  <img src="../../docs/assets/logo.png" alt="Env Manager 标志" width="120" height="120">
</picture>
<p align="center">
  <img src="../../docs/assets/brand/hero.gif" alt="Env Manager mini hero" width="100%">
</p>


# Env Manager

Ein moderner, schlanker Manager für Windows-Umgebungsvariablen — Dualmodus mit CLI und GUI, inspiriert von Microsoft PowerToys, aber eigenständig und agentenfreundlich.

**"Passt sich nahtlos an jede Umgebung an."**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **Deutsch** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demos

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>

Die Demo zeigt schreibgeschützte CLI-Befehle in Aktion: agents --summary, path health, get PATH und agents --json. Mit vhs docs/assets/demo.tape regenerierbar.
## Features
- Agenten-native CLI — 18+ Befehle mit einem erstklassigen Maschinenvertrag: env-manager-cli agents --json gibt eine strukturierte Befehlspezifikation aus, und jede Funktion ist in einem agentenorientierten Handbuch (AGENTS.cli.md) dokumentiert, das mit der Binärdatei ausgeliefert wird.
- Profile & Konfiguration — Globale Profile werden auf die Registrierung angewendet; Launch-Profile injizieren einen isolierten env-Block in einen einzelnen Prozess (sie berühren niemals die Registrierung und senden niemals WM_SETTINGCHANGE). Vererbung, Konfliktvorschau und sicheres Rollback in umgekehrter Reihenfolge sind enthalten.
- 8 Secret-Anbieter, null Klartext — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. Klartext wird nie auf Datenträger oder in Logs persistiert.
- Standardmäßig geschützt — Systemvariablen und PATH-Einträge können nicht gelöscht oder umbenannt werden; jeder Schreibvorgang folgt einem dreischichtigen serialisierten Vertrag (Mutex + Write Lock + Verifizieren vor dem Austausch).
- PATH-Health — erkennt Duplikate und tote Einträge, mit --fix / --dry-run.
- Audit-Ledger — append-only, SHA256-hashverkettete Historie. Mit Rollback und Export für die Notfallwiederherstellung.
- CLI- + GUI-Dualmodus — C#-CLI für Skripting/CI; native Tauri-2- + Svelte-GUI für interaktives Bearbeiten. Beide laufen über dieselben Registrierungsverträge. i18n in 10 Sprachen.
## For AI Agents
Env Manager ist für den Betrieb durch LLM-Agenten konzipiert, nicht nur durch Menschen:
- AGENTS.md — agentenorientierte Anweisungen auf Repository-Ebene (Architektur, harte Grenzen, Testrichtlinie).
- AGENTS.cli.md — wird mit der CLI-Binärdatei ausgeliefert, sodass jeder Agent den Vertrag zur Laufzeit entdecken kann.
- Auf Fähigkeiten ausgerichtete Agentenoberfläche — eine Opt-in-agentCapabilities-Whitelist in secret-providers.json erlaubt es Bereitstellungen, parallele set/delete-Aufrufe von Agenten abzulehnen.
## Security
> Aktuelle Builds sind nicht codesigniert. Windows SmartScreen zeigt beim ersten Start möglicherweise eine Warnung vor einer nicht erkannten App an — klicken Sie auf Weitere Informationen und dann auf Trotzdem ausführen. Wir haben eine kostenlose Open-Source-Codesignierung über die SignPath Foundation beantragt; sobald diese genehmigt ist, werden alle Release-Artefakte (MSI + EXE) signiert.
Geschützte Variablen und PATH-Einträge werden vor dem Löschen deaktiviert, mit exakter Prüfung des Registrierungswerttyps bei der Wiederherstellung. Secret-Werte werden über anbieterspezifische Mechanismen verschlüsselt — Klartext wird nie auf Datenträger oder in Logs persistiert. Named-Pipe-IPC verwendet Anti-Squatting-Flags und Eingabevalidierung (max. 64 Argumente, 32767-Zeichen-Obergrenze, Ablehnung von Null-Bytes).
## Install
### MSI-Installer
Laden Sie das MSI aus GitHub Releases herunter und führen Sie es aus. Startmenü-Verknüpfungen werden automatisch erstellt. Verfügbar für x64, x86 und ARM64.
### Tragbar (Portable)
Laden Sie das portable ZIP aus GitHub Releases herunter. Entpacken und env-manager.exe direkt ausführen. Keine Installation erforderlich.
### Nur CLI
Laden Sie das CLI-only ZIP für den Headless- oder Skripteinsatz herunter: env-manager-cli.exe plus .dll-Dateien. Keine GUI, keine WebView2-Abhängigkeit.
### Voraussetzungen
> Portable- und CLI-only-Builds sind frameworkabhängig: Sie erfordern die .NET-10-Desktop-Runtime auf dem Zielrechner. Der MSI-Installer prüft .NET 10 zur Installationszeit und fragt automatisch nach.
> Die WebView2-Runtime (für die GUI) ist unter Windows 11 vorinstalliert und für Windows 10 21H2+ von Microsoft erhältlich.
Für optionale externe Secret-Anbieter-Tools (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7) siehe den Secret Providers Guide.
### winget
> Die winget-Verteilung ist geplant, aber noch nicht verfügbar. Updates werden über GitHub Issues verfolgt.
### Aus dem Quellcode
Erfordert .NET 10 SDK, Node.js 20+ und Rust stable mit MSVC-Target.
## Usage
### CLI
Die vollständige Befehlsreferenz finden Sie in docs/cli-commands.md.
### GUI
Führen Sie env-manager.exe aus. Die GUI bietet eine Echtzeit-Variablenliste mit Suche, Bereichsfilterung und Inline-Bearbeitung, einen PATH-Editor mit Drag-and-Drop-Umsortierung, Profilverwaltung, Secret-Anbieter-Auswahl, Service-Kontrollpanel, Audit-Historie und i18n in 10 Sprachen.
## Architecture
- CLI: C# .NET 10 Einzeldatei-Executable — die Koordinationsschicht und das Registrierungs-Gateway.
- Service: eigenständige Rust-Binärdatei, die den Lebenszyklus von Secret-Mounts über Named-Pipe-IPC verwaltet.
- GUI: Tauri-2- + Svelte-4-Frontend, das dieselben IPC-Verträge verwendet.
## Secret Providers
8 Anbieter-Backends mit Aktivierungs-Preflight — Fehler erscheinen direkt als Inline-Amber-Banner im Profileditor.
Siehe docs/secret-providers-guide.md für die Voraussetzungen pro Anbieter, die einmalige Einrichtung und die Schritte zur Behebung von Aktivierungsfehlern.
## Service Mode
env-manager-service.exe ist eine eigenständige Rust-Binärdatei, die den Lebenszyklus von Secret-Mounts über Named-Pipe-IPC verwaltet:
- RuntimeMode: Service (SCM-verwaltet, Systemstart), Background (vom Benutzer gestartet), Cli (Einmal-Gateway)
- Reconcile-Schleife: 300s periodischer Vollscan, idempotenter Handler pro Eintrag, 30-Sekunden-Verzögerung des ersten Ticks
- Zertifikats-Bootstrap: zertifikatsbasierte Authentifizierung über Vault AppRole und Azure SP eliminiert langlebige Token
- Audit-Ledger: append-only hashverkettetes audit-ledger.jsonl mit 100MB-Rotation und Manipulationserkennung
- IPC: Anti-Squatting-Pipe-Flag, 65536-Byte-Anfrageobergrenze, zeilengetrenntes JSON-Protokoll
- Watchdog: zweischichtige Wiederherstellung — SCM-Autoneustart (Service-Modus) + GUI-30-Sekunden-Ping-Watchdog (Background-Modus)
## Documentation
## Maintainers
## Releases

Releases laufen über die Release-please-Einzelstrecke: Conventional Commits auf main einpflegen, den automatisch erzeugten `chore(main): release X.Y.Z`-PR prüfen und zusammenführen – der `vX.Y.Z`-Tag löst dann die komplette Artefakt-Pipeline aus (portable / nur-CLI / MSI für x64/x86/arm64, mit Build-Provenance-Nachweisen). Der manuelle Release-Workflow bleibt nur für Notfälle reserviert. Siehe [docs/build-and-release.md](docs/build-and-release.md), Abschnitt "How to Release".

## Contributing
Issues und PRs sind willkommen. Lesen Sie zuerst AGENTS.md für die Architekturgrenzen und die Testrichtlinie.
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
