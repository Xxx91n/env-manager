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

Um gerenciador moderno e leve de variáveis de ambiente do Windows, com modo duplo CLI e GUI, inspirado no Microsoft PowerToys, porém independente e amigável para agentes.

**"Adapta-se perfeitamente a qualquer ambiente."**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **Português** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demonstrações

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>


A demo mostra comandos CLI somente leitura em ação: agents --summary, path health, get PATH e agents --json. Regenerar com vhs docs/assets/demo.tape.

## Recursos

- CLI nativo para agentes — 18+ comandos com um contrato de máquina de primeira classe: env-manager-cli agents --json expõe uma especificação estruturada de comandos, e todos os recursos estão documentados em um manual voltado para agentes (AGENTS.cli.md) que acompanha o binário.
- Perfis e configuração — os perfis globais são aplicados ao registro; os perfis de lançamento (Launch profiles) injetam um bloco de ambiente isolado em um único processo (nunca tocam o registro, nunca transmitem WM_SETTINGCHANGE). Inclui herança, visualização de conflitos e reversão segura em ordem inversa.
- 8 provedores de segredos, zero texto simples — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. O texto simples nunca é persistido em disco nem em logs.
- Protegido por padrão — variáveis do sistema e entradas do PATH não podem ser excluídas nem renomeadas; toda gravação é um contrato serializado em três camadas (mutex + bloqueio de gravação + verificação antes da troca).
- Saúde do PATH — detecta entradas duplicadas e inválidas, com --fix / --dry-run.
- Livro de auditoria — histórico somente acréscimo (append-only) encadeado por hash SHA256, com reversão e exportação para recuperação de desastres.
- Modo duplo CLI + GUI — CLI em C# para scripting/CI; GUI nativa Tauri 2 + Svelte para edição interativa. Ambos passam pelos mesmos contratos de registro. i18n em 10 idiomas.

## Para agentes de IA

O Env Manager foi projetado para ser operado por agentes LLM, não apenas por humanos:

- AGENTS.md — instruções de nível de repositório para agentes (arquitetura, limites rígidos, testes).
- AGENTS.cli.md — acompanha o binário CLI para que qualquer agente possa descobrir o contrato em tempo de execução.
- Superfície de agente delimitada por capacidades — a lista de permissões opcional (opt-in) agentCapabilities em secret-providers.json permite que implantações rejeitem chamadas paralelas set/delete vindas de agentes.

## Segurança

> Os builds atuais não são assinados com código. O Windows SmartScreen pode exibir um aviso de aplicativo não reconhecido no primeiro início — clique em Mais informações e depois em Executar mesmo assim. Solicitamos assinatura de código gratuita para código aberto por meio da SignPath Foundation; assim que aprovada, todos os artefatos de release (MSI + EXE) serão assinados.

Variáveis protegidas e entradas do PATH são desabilitadas antes da exclusão, com verificação exata do tipo de valor do registro na restauração. Os valores de segredos são criptografados por mecanismos específicos de cada provedor — o texto simples nunca é persistido em disco nem em logs. A IPC por named pipe usa sinalizadores anti-squatting e validação de entrada (máximo de 64 argumentos, limite de 32767 caracteres, rejeição de bytes nulos).

## Instalação

### Instalador MSI

Baixe o MSI em GitHub Releases e execute-o. Cria atalhos do Menu Iniciar automaticamente. Disponível em x64, x86 e ARM64.

### Portable

Baixe o ZIP portátil em GitHub Releases. Extraia e execute env-manager.exe diretamente. Não é necessária instalação.

### Somente CLI

Baixe o ZIP somente CLI para uso headless ou em scripts: env-manager-cli.exe mais os arquivos .dll. Sem GUI, sem dependência de WebView2.

### Pré-requisitos

> Os builds portátil e somente CLI dependem do framework: exigem o .NET 10 Desktop Runtime na máquina de destino. O instalador MSI verifica o .NET 10 no momento da instalação e solicita automaticamente.
> O WebView2 Runtime (para a GUI) vem pré-instalado no Windows 11 e está disponível para Windows 10 21H2+ pela Microsoft.

Para ferramentas externas opcionais de provedores de segredos (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7), consulte o Guia de provedores de segredos.

### winget

> A distribuição via winget está planejada, mas ainda não está disponível. Acompanhe as novidades em GitHub Issues.

### A partir do código-fonte

Requer .NET 10 SDK, Node.js 20+ e Rust estável com destino MSVC.

## Uso

### CLI

Consulte docs/cli-commands.md para a referência completa de comandos.

### GUI

Execute env-manager.exe. A GUI oferece uma lista de variáveis em tempo real com busca, filtragem por escopo, edição inline, um editor de PATH com reordenação por arrastar e soltar, gerenciamento de perfis, seleção de provedor de segredos, painel de controle do serviço, histórico de auditoria e i18n em 10 idiomas.

## Arquitetura

- CLI: executável de arquivo único em C# .NET 10 — a camada de coordenação e o gateway do registro.
- Serviço: binário Rust independente que gerencia o ciclo de vida do mount de segredos via IPC por named pipe.
- GUI: frontend Tauri 2 + Svelte 4 usando os mesmos contratos IPC.

## Provedores de segredos

8 backends de provedores com verificação prévia de ativação — falhas aparecem como avisos âmbar inline diretamente no editor de perfis.

Consulte docs/secret-providers-guide.md para requisitos por provedor, configuração única inicial e etapas de correção de erros de ativação.

## Modo de serviço

env-manager-service.exe é um binário Rust independente que gerencia o ciclo de vida do mount de segredos via IPC por named pipe:

- RuntimeMode: Service (gerenciado pelo SCM, inicialização da máquina), Background (iniciado pelo usuário), Cli (gateway de uso único)
- Loop de reconciliação: varredura completa periódica a cada 300 s, handler idempotente por item, atraso de 30 s no primeiro ciclo
- Bootstrap por certificado: a autenticação baseada em certificado de Vault AppRole e Azure SP elimina tokens de longa duração
- Livro de auditoria: audit-ledger.jsonl somente acréscimo encadeado por hash, com rotação de 100 MB e detecção de adulteração
- IPC: sinalizador de pipe anti-squatting, limite de 65536 bytes por solicitação, protocolo JSON delimitado por novas linhas
- Watchdog: recuperação em duas camadas — reinício automático do SCM (modo Service) + watchdog de ping de 30 s da GUI (modo Background)

## Documentação

## Mantenedores

## Contribuições

Issues e PRs são bem-vindos. Leia o AGENTS.md primeiro para conhecer os limites de arquitetura e a política de testes.

## Licença

Apache-2.0 (c) 2026 Env Manager Contributors.
