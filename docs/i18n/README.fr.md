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

La démo montre des commandes CLI en lecture seule en action : agents --summary, path health, get PATH et agents --json. Régénérez avec vhs docs/assets/demo.tape.
## Features
- CLI native pour agents — plus de 18 commandes avec un contrat machine de premier ordre : env-manager-cli agents --json expose une spécification de commandes structurée, et chaque fonctionnalité est documentée dans un manuel destiné aux agents (AGENTS.cli.md) fourni avec le binaire.
- Profils et configuration — Les profils globaux s'appliquent au registre ; les profils Launch injectent un bloc d'environnement isolé dans un seul processus (ils ne touchent jamais au registre et ne diffusent jamais WM_SETTINGCHANGE). Héritage, aperçus de conflits et rollback sécurisé en ordre inverse inclus.
- 8 fournisseurs de secrets, zéro texte en clair — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. Le texte en clair n'est jamais persisté sur disque ni dans les journaux.
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
Les variables protégées et les entrées PATH sont désactivées avant suppression, avec vérification exacte du type de valeur du registre lors de la restauration. Les valeurs de secrets sont chiffrées via des mécanismes spécifiques au fournisseur — le texte en clair n'est jamais persisté sur disque ni dans les journaux. L'IPC par named pipe utilise des indicateurs anti-squatting et une validation des entrées (64 arguments max, limite de 32767 caractères, rejet des octets nuls).
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
- Bootstrap de certificats : l'authentification par certificat Vault AppRole et Azure SP élimine les jetons de longue durée
- Registre d'audit : audit-ledger.jsonl en annexe seule et chaîné par hash, avec rotation à 100 Mo et détection de falsification
- IPC : indicateur de pipe anti-squatting, limite de demande de 65536 octets, protocole JSON délimité par sauts de ligne
- Watchdog : récupération à deux niveaux — redémarrage automatique SCM (mode Service) + watchdog de ping de 30 s de la GUI (mode Background)
## Documentation
## Maintainers
## Contributing
Les issues et les PR sont les bienvenues. Lisez d'abord AGENTS.md pour les limites d'architecture et la politique de test.
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
