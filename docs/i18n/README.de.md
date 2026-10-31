<!-- canonical: ../../README.md -->
<!-- sync: ef2a485dfffb -->

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

Die Demo zeigt schreibgeschützte CLI-Befehle in Aktion: agents --summary, path health, get PATH und agents --json. Mit `node scripts/demo/capture.mjs` regenerierbar.

*Die Terminal-Ausgabe oben verwendet simulierte Daten.*

### Galerie

| | | |
|:-:|:-:|:-:|
| ![Variablenansicht](../../docs/assets/gallery/01-variables.png)<br>**Variablen** — Suche, Geltungsbereich, Aktionen pro Zeile | ![System-Geltungsbereich](../../docs/assets/gallery/02-variables-system.png)<br>**System-Geltungsbereich** — geschützte Einträge gesperrt | ![Path-Editor](../../docs/assets/gallery/03-path-editor.png)<br>**Path-Editor** — Erkennung toter & doppelter Einträge |
| ![Profile](../../docs/assets/gallery/04-profiles.png)<br>**Profile** — anwenden / starten / vererben | ![Verlauf & Import](../../docs/assets/gallery/05-history.png)<br>**Verlauf & Import** — rückgängig machbare Änderungen | ![Schutz](../../docs/assets/gallery/06-protection.png)<br>**Schutz** — benutzerdefinierte Sperren |
| ![Audit-Ledger](../../docs/assets/gallery/07-audit.png)<br>**Audit-Ledger** — hash-verkettet, verifizierbar | ![Dienst-Manager](../../docs/assets/gallery/08-service.png)<br>**Dienst-Manager** — Secret-Mounts | ![Einstellungen](../../docs/assets/gallery/09-settings-i18n.png)<br>**Einstellungen** — i18n in 10 Sprachen |
| ![Bearbeitungsdialog](../../docs/assets/gallery/10-edit-dialog.png)<br>**Bearbeitungsdialog** — Vorschau des erweiterten Werts | ![Helles Design](../../docs/assets/gallery/11-variables-light.png)<br>**Helles Design** — Variante | ![CLI](../../docs/assets/gallery/12-cli-terminal.png)<br>**CLI** — JSON-first, agent-native |


## Features
- Agenten-native CLI — 18+ Befehle mit einem erstklassigen Maschinenvertrag: env-manager-cli agents --json gibt eine strukturierte Befehlspezifikation aus, und jede Funktion ist in einem agentenorientierten Handbuch (AGENTS.cli.md) dokumentiert, das mit der Binärdatei ausgeliefert wird.
- Profile & Konfiguration — Globale Profile werden auf die Registrierung angewendet; Launch-Profile injizieren einen isolierten env-Block in einen einzelnen Prozess (sie berühren niemals die Registrierung und senden niemals WM_SETTINGCHANGE). Vererbung, Konfliktvorschau und sicheres Rollback in umgekehrter Reihenfolge sind enthalten.
- 8 Secret-Anbieter — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. Secrets werden ausschließlich als verschlüsselte Envelopes gespeichert; wo genau Klartext auftreten kann, zeigt die Tabelle unter [Security](#security).
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
Geschützte Variablen und PATH-Einträge werden vor dem Löschen deaktiviert, mit exakter Prüfung des Registrierungswerttyps bei der Wiederherstellung. Named-Pipe-IPC verwendet Anti-Squatting-Flags und Eingabevalidierung (max. 64 Argumente, 32767-Zeichen-Obergrenze, Ablehnung von Null-Bytes).

### Wo Klartext existieren kann (Secret-Grenztabelle)

| Senke | Landet Klartext? | Lebensdauer | Bereinigung | Grundlage |
|---|---|---|---|---|
| `profiles.json` / `secretMount.json` | Nein für die Secret-Verben — nur Umschlag `{provider, version, createdAt, ciphertext, targetName}` oder eine `mount:<id>`-Referenz; die generischen Verben `add-var`/`edit-var` umgehen die Markierung (siehe [Klartext-Lebenszyklus](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | Bis das Secret entfernt wird | `profile remove-secret` löscht den Umschlag und den anbieterseitigen Zustand | harte Grenze; Vertragsassertion `plaintext-never-in-envelope` |
| dpapi-current-user | Nein — DPAPI `CryptProtectData`, Bereich CurrentUser | Im Umschlag | Mit dem Umschlag | harte Grenze (v0.7.0) |
| credential-manager | Nein — der CredMan-Blob wird vor dem Persistieren selbst per DPAPI verschlüsselt | Bis zum `Delete` des Anbieters | `Delete` bei remove-secret | [Anbieterleitfaden](../../docs/secret-providers-guide.md) |
| powershell-secretmanagement | Die SecretStore-Tresordatei bleibt verschlüsselt — aber Klartext passiert bei jedem Aufruf das Argument `pwsh -EncodedCommand` (base64-Skript) | Lebensdauer des pwsh-Prozesses; Tresoreintrag bis zum Entfernen | Argument endet mit dem Prozess; Tresoreintrag wird bei remove-secret gelöscht | harte Grenze (v0.7.4) |
| vault-kv2 · azure-keyvault · aws-secretsmanager · 1password | Lokal kein Klartext — das Profil hält nur eine Pfad-/Secret-ID-Referenz; das Secret bleibt im entfernten Backend  Das lokale Profil enthält keinen Klartext; während Anbieteroperationen kann er in Kindprozess-Argumenten/-Ausgabe und Anfragekörpern des Anbieters auftreten.| Betreiberrichtlinie, backendseitig | `Delete` des Anbieters; `secret-provider rotate` verschlüsselt neu | [Anbieterleitfaden](../../docs/secret-providers-guide.md) |
| sops | Im Umschlag nur Chiffretext — **zusätzlich** Klartext `secret.json` / `secret.dec.json` in einem Verzeichnis pro Aufruf während Ver- und Entschlüsselung: `%TEMP%\env-manager-sops-*` oder, wenn die Rotation delegiert wurde, ein Verzeichnis im diensteigenen Stammverzeichnis  Hält ein Hilfsprozess eine Datei über das Rückgewinnungsfenster hinaus offen, kann das Löschen fehlschlagen und der Scratch-Stamm bleiben; die Bereinigung bleibt best effort.| Sekunden pro Aufruf | `Directory.Delete` dieses Verzeichnisses. Bei Delegation durch den Dienst löscht der Dienst das gesamte Stammverzeichnis, sobald der CLI-Kindprozess beendet ist — auch nach einem Stopp, der die CLI beendet hat —, mit begrenzter Wiederholung und einem Log-Eintrag auf Error-Ebene (nie stillschweigend), wenn das Löschen dennoch fehlschlägt; bei manuellem Aufruf erfolgt das Löschen nach bestem Bemühen, und ein Absturz kann das Verzeichnis verwaist zurücklassen. Das Verzeichnis wird nur für den Besitzer angelegt (Vererbung aus; aktuelle Identität, SYSTEM, Administrators), daher bleibt ein Überrest nur für diese Identitäten lesbar — es sei denn, das Setzen dieser DACL schlug fehl; dann behält es die ACL, die es geerbt hätte (die von `%TEMP%` oder bei Delegation die des Scratch-Stammverzeichnisses des Dienstes) | harte Grenze (v0.7.11); `SopsProvider`, `SecretTempDirectory`, `service/src/sops_scratch.rs` |
| Speicher des startenden Prozesses (`env-manager-cli`) | Ja — entschlüsselte Bytes liegen kurzzeitig im startenden Prozess  Entschlüsselte Werte können bis zum CLI-Ende in System.String verbleiben; SecretString löscht nur seine separate Kopie, GC-Kopien und Zwischen-Strings können fortbestehen.| Millisekunden, bis Injektion/Reveal abgeschlossen ist | `SecretString` nullt sein `char[]` bei Dispose — **nach bestem Bemühen**: GC-Kopien und String-Zwischenwerte können länger überleben | `SecretString`-Nullung; [Canary-Zero-Leak-Netz](../../docs/architecture.md#canary-zero-leak-assertion-net-and-goldensnapshot-layers) |
| Umgebungsblock des gestarteten Kindprozesses | Ja — injizierte Umgebungsvariablen sind im Kindprozess Klartext | Lebensdauer des Kindprozesses | Nicht möglich — Umgebungsblöcke sind laut OS-Design für Prozesse desselben Benutzers lesbar | Design der Launch-Profile |
| Standardausgabe von `profile reveal-secret` | Ja — **beabsichtigt**; einer der zwei Klartext-Ausgabepfade (`profile reveal-secret`, `profile show --reveal`) | Bis der Terminal-Scrollback geleert wird ; Ein nachgeschalteter Pipe-Verbraucher kann seine Klartextkopie bis zur Löschung behalten.| Keine — vom Benutzer ausgelöst | harte Grenze; das Canary-Netz überspringt diese Senken bewusst |
| Temp-Dateien des Survival-Kits (`envmanager-kit-*.json` unter `%TEMP%\env-manager-kit-<guid>\`, `mount-survival-kit.tmp.json`) | Ja — eine kurzlebige Klartext-Temp-Datei während des Kit-Zusammenbaus. Die CLI-seitige Kit-Datei wird in `%TEMP%\env-manager-kit-<guid>\` geschrieben, einem nur für den Besitzer angelegten Verzeichnis (Vererbung aus; aktuelle Identität, SYSTEM, Administrators), daher bleibt ein Überrest nur für diese Identitäten lesbar — es sei denn, das Setzen dieser DACL schlug fehl; dann behält es die geerbte `%TEMP%`-ACL. Die dienstseitige `mount-survival-kit.tmp.json` liegt im Ledger-Verzeichnis, nicht unter `%TEMP%` | Sekunden | Löschen nach bestem Bemühen nach der Verschlüsselung, danach Löschen des enthaltenden Verzeichnisses nach bestem Bemühen: schlägt das Löschen des Verzeichnisses fehl, bleibt ein leeres Verzeichnis zurück; schlug auch das Löschen der Datei fehl, überlebt zusätzlich die Klartextdatei — beides nach bestem Bemühen, wie in den sops-Zeilen oben | `AuditLedgerMigration.RunAuditExportSurvivalKit`, `SecretTempDirectory.Create`; `service/src/audit_ledger.rs` |
| `audit-ledger.jsonl` | Keine geheimen Werte werden in migrierte Ledger-Ereignisse kopiert; gespeichert werden nur Metadaten und Redaktionsmarker, allgemeine old/new-Zusammenfassungen werden nicht migriert. | Nur anhängend, Rotation bei 100MB | Rotation | harte Grenze (v0.9.13); statische Verschlüsselung `AuditCrypto` |
| Datei von `export-state` | Nein — doppelte Schicht: AES-256-GCM-Nutzlast + per DPAPI umhüllter DEK + HMAC-SHA256 | Bis Sie die Datei löschen | Vom Benutzer verwaltet | `AuditCrypto` (v0.9.13) |
| `logs/env-manager.log` / stderr | Nein, per Design — Ausnahmetext wird zuvor um 22 secret-tragende Muster bereinigt; Allow-List **nach bestem Bemühen**: ein unbekanntes Secret-Format könnte dennoch durchsickern, begrenzt durch Kürzung auf 512 Zeichen | Log-Aufbewahrung | — | Allow-List `ScrubExceptionMessage`; `CanaryRedactionTests` + Canary-Netz über 7 Senken |
| GUI (WebView2) / Rust-Dienst | Nein — die GUI rendert Platzhalter `<encrypted>` und hat keine Reveal-Anzeige; der Dienst persistiert keine Werte | — | — | Secret-Grenze der Architektur |
| Windows-Registrierung | Nein für die Secret-Verben — Launch-Profile können nicht in die Registrierung schreiben und apply weist sie ab; die Umbenennungslücke der generischen Verben kann sie erreichen (siehe [Klartext-Lebenszyklus](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | — | — | harte Grenze; Invariante `launch-env-injection.Tests.ps1` |

Diese Tabelle ist die Zusammenfassung für Benutzer; maßgeblich für die Allow-List je Oberfläche, einschließlich der Lücke der generischen Verben, ist der Abschnitt Plaintext lifecycle in `docs/architecture.md`.

Speichernullung und Log-Bereinigung sind Hygiene nach bestem Bemühen, keine Garantien — derselbe Vorbehalt, den Microsoft für `SecureString` dokumentiert (unveränderliche Strings, GC-Verschiebung, Interop-Kopien). Wo sich die Aussage testen lässt, wird sie getestet: `tests/canary-redaction.Tests.ps1` durchsucht 7 Ausgabesenken nach einem platzierten Canary, und die gemeinsame Vertragssuite prüft `plaintext-never-in-envelope` für alle 8 Anbieter.
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
- Audit-Ledger: append-only hashverkettetes audit-ledger.jsonl mit 100MB-Rotation und Manipulationserkennung
- IPC: Anti-Squatting-Pipe-Flag, 65536-Byte-Anfrageobergrenze, zeilengetrenntes JSON-Protokoll
- Watchdog: zweischichtige Wiederherstellung — SCM-Autoneustart (Service-Modus) + GUI-30-Sekunden-Ping-Watchdog (Background-Modus)
## Documentation
## Maintainers
## Contributing
Issues und PRs sind willkommen. Lesen Sie zuerst AGENTS.md für die Architekturgrenzen und die Testrichtlinie.
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
