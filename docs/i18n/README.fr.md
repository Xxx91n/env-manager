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

Un gestionnaire de variables d'environnement Windows moderne et léger — double mode CLI et GUI, inspiré de Microsoft PowerToys mais autonome et adapté aux agents.

**« S'adapte sans couture à chaque environnement. »**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **Français** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demos

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>

La démo montre des commandes CLI en lecture seule en action : agents --summary, path health, get PATH et agents --json. Régénérez avec `node scripts/demo/capture.mjs`.

*La sortie du terminal ci-dessus utilise des données simulées.*

### Galerie

| | | |
|:-:|:-:|:-:|
| ![Vue des variables](../../docs/assets/gallery/01-variables.png)<br>**Variables** — recherche, portée, actions par ligne | ![Portée système](../../docs/assets/gallery/02-variables-system.png)<br>**Portée système** — entrées protégées verrouillées | ![Éditeur PATH](../../docs/assets/gallery/03-path-editor.png)<br>**Éditeur PATH** — détection des entrées mortes et dupliquées |
| ![Profils](../../docs/assets/gallery/04-profiles.png)<br>**Profils** — appliquer / lancer / hériter | ![Historique & import](../../docs/assets/gallery/05-history.png)<br>**Historique & import** — modifications réversibles | ![Protection](../../docs/assets/gallery/06-protection.png)<br>**Protection** — verrous personnalisés |
| ![Registre d\u2019audit](../../docs/assets/gallery/07-audit.png)<br>**Registre d\u2019audit** — chaîné par hash, vérifiable | ![Gestionnaire de services](../../docs/assets/gallery/08-service.png)<br>**Gestionnaire de services** — montages de secrets | ![Paramètres](../../docs/assets/gallery/09-settings-i18n.png)<br>**Paramètres** — i18n en 10 langues |
| ![Boîte de dialogue d\u2019édition](../../docs/assets/gallery/10-edit-dialog.png)<br>**Boîte de dialogue d\u2019édition** — aperçu de la valeur développée | ![Thème clair](../../docs/assets/gallery/11-variables-light.png)<br>**Thème clair** — Variante | ![CLI](../../docs/assets/gallery/12-cli-terminal.png)<br>**CLI** — JSON d’abord, natif pour les agents |


## Features
- CLI native pour agents — plus de 18 commandes avec un contrat machine de premier ordre : env-manager-cli agents --json expose une spécification de commandes structurée, et chaque fonctionnalité est documentée dans un manuel destiné aux agents (AGENTS.cli.md) fourni avec le binaire.
- Profils et configuration — Les profils globaux s'appliquent au registre ; les profils Launch injectent un bloc d'environnement isolé dans un seul processus (ils ne touchent jamais au registre et ne diffusent jamais WM_SETTINGCHANGE). Héritage, aperçus de conflits et rollback sécurisé en ordre inverse inclus.
- 8 fournisseurs de secrets — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. Les secrets ne sont conservés que sous forme d'enveloppes chiffrées ; la table de la section [Security](#security) précise où le texte en clair peut apparaître. Pour les commandes de secrets, les profils ne stockent que des enveloppes chiffrées ; du texte en clair peut apparaître dans les fichiers temporaires, les processus/requêtes et les sorties documentées, avec nettoyage et rédaction au mieux.
- Protégé par défaut — les variables système et les entrées PATH ne peuvent pas être supprimées ni renommées ; chaque écriture suit un contrat sérialisé à trois couches (mutex + verrou d'écriture + vérification avant remplacement).
- Santé du PATH — détecte les doublons et les entrées mortes, avec --fix / --dry-run.
- Registre d'audit — historique en annexe seule, chaîné par hash SHA256. Avec rollback et export pour la récupération après sinistre.
- Double mode CLI + GUI — CLI C# pour les scripts/CI ; GUI native Tauri 2 + Svelte pour l'édition interactive. Les deux passent par les mêmes contrats de registre. i18n en 10 langues.
## For AI Agents
Env Manager est conçu pour être utilisé par des agents LLM, pas seulement par des humains :
- AGENTS.md — instructions d'agent au niveau du dépôt (architecture, limites strictes, politique de test).
- AGENTS.cli.md — fourni avec le binaire CLI pour que tout agent puisse découvrir le contrat à l'exécution.
- Surface agentique par capacités — une liste blanche agentCapabilities en opt-in dans secret-providers.json permet aux déploiements de rejeter les appels set/delete parallèles venant des agents.
## Security
> Les builds actuels ne sont pas signés. Windows SmartScreen peut afficher un avertissement d'application non reconnue au premier lancement — cliquez sur Plus d'informations puis sur Exécuter quand même. Nous avons demandé une signature de code open source gratuite via la SignPath Foundation ; une fois approuvée, tous les artefacts de version (MSI + EXE) seront signés.
Les variables protégées et les entrées PATH sont désactivées avant suppression, avec vérification exacte du type de valeur du registre lors de la restauration. L'IPC par named pipe utilise des indicateurs anti-squatting et une validation des entrées (64 arguments max, limite de 32767 caractères, rejet des octets nuls).

### Où le texte en clair peut exister (table des frontières des secrets)

| Point de chute | Texte en clair ? | Durée de vie | Nettoyage | Fondement |
|---|---|---|---|---|
| `profiles.json` / `secretMount.json` | Non pour les commandes secrètes — uniquement l'enveloppe `{provider, version, createdAt, ciphertext, targetName}` ou une référence `mount:<id>` ; les verbes génériques `add-var`/`edit-var` contournent le marqueur (voir [Cycle de vie du texte en clair](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | Jusqu'à la suppression du secret | `profile remove-secret` supprime l'enveloppe et l'état côté fournisseur | frontière stricte ; assertion de contrat `plaintext-never-in-envelope` |
| dpapi-current-user | Non — DPAPI `CryptProtectData`, portée CurrentUser | Dans l'enveloppe | Avec l'enveloppe | frontière stricte (v0.7.0) |
| credential-manager | Non — le blob CredMan est lui-même chiffré par DPAPI avant persistance | Jusqu'au `Delete` du fournisseur | `Delete` lors de remove-secret | [guide des fournisseurs](../../docs/secret-providers-guide.md) |
| powershell-secretmanagement | Le fichier du coffre SecretStore reste chiffré — mais le texte en clair transite par l'argument `pwsh -EncodedCommand` (script base64) à chaque appel | Durée du processus pwsh ; entrée du coffre jusqu'à sa suppression | L'argument disparaît avec le processus ; l'entrée du coffre est supprimée par remove-secret | frontière stricte (v0.7.4) |
| vault-kv2 · azure-keyvault · aws-secretsmanager · 1password | Aucun texte en clair en local — le profil ne contient qu'une référence de chemin/ID de secret ; le secret reste dans le backend distant  Le profil local ne contient aucun texte en clair ; il peut apparaître temporairement dans les arguments/sorties des processus du fournisseur et les corps de requêtes pendant ses opérations.| Politique de l'opérateur, côté backend | `Delete` du fournisseur ; `secret-provider rotate` rechiffre | [guide des fournisseurs](../../docs/secret-providers-guide.md) |
| sops | Enveloppe en texte chiffré uniquement — **plus** des fichiers en clair `secret.json` / `secret.dec.json` dans un répertoire propre à chaque appel pendant le chiffrement/déchiffrement : `%TEMP%\env-manager-sops-*`, ou un répertoire dans la racine possédée par le service quand la rotation a été déléguée  Si un processus auxiliaire conserve un fichier au-delà de la fenêtre de récupération, la suppression peut échouer et la racine scratch rester ; le nettoyage reste au mieux.| Quelques secondes par appel | `Directory.Delete` de ce répertoire. En cas de délégation par le service, celui-ci supprime toute la racine une fois le processus enfant CLI terminé — y compris après un arrêt qui a terminé la CLI — avec des nouvelles tentatives bornées et un journal de niveau erreur (jamais de silence) si la suppression échoue encore ; lancée à la main, la suppression est au mieux et un plantage peut laisser le répertoire orphelin. Le répertoire est créé en accès propriétaire uniquement (héritage désactivé ; identité courante, SYSTEM, Administrators), donc un orphelin reste lisible par ces seules identités — sauf si l'application de cette DACL a échoué, auquel cas il conserve l'ACL qu'il aurait héritée (celle de `%TEMP%`, ou celle de la racine scratch du service en cas de délégation) | frontière stricte (v0.7.11) ; `SopsProvider`, `SecretTempDirectory`, `service/src/sops_scratch.rs` |
| Mémoire du processus lanceur (`env-manager-cli`) | Oui — les octets déchiffrés transitent dans le processus lanceur  Les valeurs peuvent rester dans System.String jusqu’à la fin de la CLI ; SecretString n’efface que sa copie séparée, et les copies du GC ou chaînes intermédiaires peuvent subsister.| Quelques millisecondes, jusqu'à la fin de l'injection/du reveal | `SecretString` met son `char[]` à zéro au Dispose — **au mieux** : copies du GC et chaînes intermédiaires peuvent lui survivre | Mise à zéro de `SecretString` ; [filet canary zéro fuite](../../docs/architecture.md#canary-zero-leak-assertion-net-and-goldensnapshot-layers) |
| Bloc d'environnement du processus enfant lancé | Oui — les variables injectées sont en clair dans le processus enfant | Durée de vie du processus enfant | Impossible — par conception de l'OS, les blocs d'environnement sont lisibles par le même utilisateur | Conception des profils Launch |
| Sortie standard de `profile reveal-secret` | Oui — **par conception** ; l'un des deux chemins de sortie en clair (`profile reveal-secret`, `profile show --reveal`) | Jusqu'à l'effacement de l'historique du terminal ; Un consommateur en aval d’un pipe peut conserver sa copie jusqu’à sa suppression.| Aucun — déclenché par l'utilisateur | frontière stricte ; le filet canary ignore délibérément ces points |
| Fichiers temporaires du kit de survie (`envmanager-kit-*.json` sous `%TEMP%\env-manager-kit-<guid>\`, `mount-survival-kit.tmp.json`) | Oui — un bref fichier temporaire en clair pendant l'assemblage du kit. Le fichier du kit côté CLI est écrit dans `%TEMP%\env-manager-kit-<guid>\`, un répertoire créé en accès propriétaire uniquement (héritage désactivé ; identité courante, SYSTEM, Administrators), donc un orphelin reste lisible par ces seules identités — sauf si l'application de cette DACL a échoué, auquel cas il conserve l'ACL héritée de `%TEMP%`. Le `mount-survival-kit.tmp.json` côté service se trouve dans le répertoire du registre d'audit, pas sous `%TEMP%` | Quelques secondes | Suppression au mieux après chiffrement, puis suppression au mieux du répertoire qui le contenait : un échec de suppression du répertoire laisse un répertoire vide, et si la suppression du fichier a aussi échoué, le fichier en clair subsiste également — les deux au mieux, comme dans les lignes sops ci-dessus | `AuditLedgerMigration.RunAuditExportSurvivalKit`, `SecretTempDirectory.Create`; `service/src/audit_ledger.rs` |
| `audit-ledger.jsonl` | Aucune valeur secrète n’est copiée dans les événements de ledger migrés ; seuls les métadonnées et marqueurs de rédaction sont conservés, et les résumés old/new génériques ne sont pas migrés. | Ajout seul, rotation à 100MB | Rotation | frontière stricte (v0.9.13) ; chiffrement statique `AuditCrypto` |
| Fichier `export-state` | Non — double couche : charge utile AES-256-GCM + DEK enveloppée par DPAPI + HMAC-SHA256 | Jusqu'à ce que vous supprimiez le fichier | Géré par l'utilisateur | `AuditCrypto` (v0.9.13) |
| `logs/env-manager.log` / stderr | Non par conception — le texte des exceptions est d'abord nettoyé de 22 motifs porteurs de secrets ; liste d'autorisation **au mieux** : un format de secret non reconnu pourrait encore fuir, borné par une troncature à 512 caractères | Rétention des journaux | — | Liste d'autorisation `ScrubExceptionMessage` ; `CanaryRedactionTests` + filet canary sur 7 points |
| GUI (WebView2) / service Rust | Non — la GUI affiche des espaces réservés `<encrypted>` et n'a pas d'affichage reveal ; le service ne persiste aucune valeur | — | — | frontière des secrets de l'architecture |
| Registre Windows | Non pour les commandes secrètes — les profils Launch ne peuvent pas écrire dans le registre et apply les refuse ; la faille de renommage des verbes génériques peut l'atteindre (voir [Cycle de vie du texte en clair](../../docs/architecture.md#v070-dpapi-secrets-runtime)) | — | — | frontière stricte ; invariant `launch-env-injection.Tests.ps1` |

Cette table est le résumé destiné aux utilisateurs ; la liste d'autorisation de référence par surface, y compris la faille des verbes génériques, est la section Plaintext lifecycle de `docs/architecture.md`.

La mise à zéro de la mémoire et le nettoyage des journaux sont des mesures d'hygiène au mieux, pas des garanties — la même réserve que Microsoft documente pour `SecureString` (chaînes immuables, déplacement par le GC, copies d'interop). Là où l'affirmation est testable, elle est testée : `tests/canary-redaction.Tests.ps1` analyse 7 sorties à la recherche d'un canary planté, et la suite de contrats partagée vérifie `plaintext-never-in-envelope` sur les 8 fournisseurs.
## Install
### Installateur MSI
Téléchargez le MSI depuis GitHub Releases et exécutez-le. Les raccourcis du menu Démarrer sont créés automatiquement. Disponible en x64, x86 et ARM64.
### Portable
Téléchargez le ZIP portable depuis GitHub Releases. Extrayez et exécutez env-manager.exe directement. Aucune installation requise.
### CLI uniquement
Téléchargez le ZIP CLI-only pour une utilisation sans interface ou par scripts : env-manager-cli.exe plus les fichiers .dll. Pas de GUI, pas de dépendance WebView2.
### Prérequis
> Les builds portables et CLI-only dépendent du framework : ils nécessitent le runtime .NET 10 Desktop sur la machine cible. L'installateur MSI vérifie .NET 10 à l'installation et invite automatiquement.
> Le runtime WebView2 (pour la GUI) est préinstallé sur Windows 11 et disponible pour Windows 10 21H2+ auprès de Microsoft.
Pour les outils externes facultatifs de fournisseurs de secrets (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7), consultez le Secret Providers Guide.
### winget
> La distribution via winget est prévue mais pas encore disponible. Suivez les mises à jour via GitHub Issues.
### Depuis les sources
Nécessite le SDK .NET 10, Node.js 20+ et Rust stable avec cible MSVC.
## Usage
### CLI
Voir docs/cli-commands.md pour la référence complète des commandes.
### GUI
Exécutez env-manager.exe. La GUI offre une liste de variables en temps réel avec recherche, filtrage par portée et édition en ligne, un éditeur PATH avec réorganisation par glisser-déposer, la gestion des profils, la sélection des fournisseurs de secrets, un panneau de contrôle des services, l'historique d'audit et l'i18n en 10 langues.
## Architecture
- CLI : exécutable mono-fichier C# .NET 10 — la couche de coordination et la passerelle vers le registre.
- Service : binaire Rust autonome gérant le cycle de vie des montages de secrets via IPC par named pipe.
- GUI : frontend Tauri 2 + Svelte 4 utilisant les mêmes contrats IPC.
## Secret Providers
8 backends de fournisseurs avec pré-vérification d'activation — les échecs apparaissent directement sous forme de bannières ambre intégrées dans l'éditeur de profils.
Voir docs/secret-providers-guide.md pour les prérequis par fournisseur, la configuration initiale et les étapes de résolution des erreurs d'activation.
## Service Mode
env-manager-service.exe est un binaire Rust autonome gérant le cycle de vie des montages de secrets via IPC par named pipe :
- RuntimeMode : Service (géré par SCM, au démarrage de la machine), Background (lancé par l'utilisateur), Cli (passerelle à usage unique)
- Boucle de réconciliation : scan complet périodique de 300 s, gestionnaire idempotent par élément, délai de 30 s pour la première exécution
- Registre d'audit : audit-ledger.jsonl en annexe seule et chaîné par hash, avec rotation à 100 Mo et détection de falsification
- IPC : indicateur de pipe anti-squatting, limite de demande de 65536 octets, protocole JSON délimité par sauts de ligne
- Watchdog : récupération à deux niveaux — redémarrage automatique SCM (mode Service) + watchdog de ping de 30 s de la GUI (mode Background)
## Documentation
## Maintainers
## Contributing
Les issues et les PR sont les bienvenues. Lisez d'abord AGENTS.md pour les limites d'architecture et la politique de test.
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
