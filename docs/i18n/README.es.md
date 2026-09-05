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

Un gestor moderno y ligero de variables de entorno de Windows, con modo dual CLI y GUI, inspirado en Microsoft PowerToys pero independiente y diseñado para agentes.

**"Se adapta sin fisuras a cualquier entorno."**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **Español** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demos

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>


La demo muestra comandos CLI de solo lectura en acción: agents --summary, path health, get PATH y agents --json. Regenera con vhs docs/assets/demo.tape.

## Características

- CLI nativo para agentes — más de 18 comandos con un contrato de máquina de primera clase: env-manager-cli agents --json expone una especificación estructurada de comandos, y cada capacidad está documentada en un manual orientado a agentes (AGENTS.cli.md) que se distribuye con el binario.
- Perfiles y configuración — los perfiles globales se aplican al registro; los perfiles de lanzamiento (Launch profiles) inyectan un bloque de entorno aislado en un único proceso (nunca tocan el registro ni emiten WM_SETTINGCHANGE). Incluyen herencia, vistas previas de conflictos y reversión segura en orden inverso.
- 8 proveedores de secretos, cero texto en claro — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. El texto en claro nunca se persiste en disco ni en registros.
- Protegido por defecto — las variables del sistema y las entradas de PATH no se pueden eliminar ni renombrar; cada escritura es un contrato serializado de tres capas (mutex + bloqueo de escritura + verificación antes del intercambio).
- Salud de PATH — detecta entradas duplicadas y obsoletas, con --fix / --dry-run.
- Registro de auditoría — historial de solo añadido (append-only) encadenado por hash SHA256, con reversión y exportación para recuperación ante desastres.
- Modo dual CLI + GUI — CLI en C# para scripting/CI; GUI nativa Tauri 2 + Svelte para edición interactiva. Ambos pasan por los mismos contratos de registro. i18n en 10 idiomas.

## Para agentes de IA

Env Manager está diseñado para ser operado por agentes LLM, no solo por humanos:

- AGENTS.md — instrucciones a nivel de repositorio para agentes (arquitectura, límites estrictos, pruebas).
- AGENTS.cli.md — se distribuye con el binario CLI para que cualquier agente pueda descubrir el contrato en tiempo de ejecución.
- Superficie de agente acotada por capacidades — la lista blanca opcional (opt-in) agentCapabilities en secret-providers.json permite a los despliegues rechazar llamadas paralelas set/delete provenientes de agentes.

## Seguridad

> Las compilaciones actuales no están firmadas con código. Windows SmartScreen puede mostrar una advertencia de aplicación no reconocida en el primer inicio: haz clic en Más información y luego en Ejecutar de todos modos. Hemos solicitado la firma gratuita de código para código abierto a través de la SignPath Foundation; una vez aprobada, todos los artefactos de la versión (MSI + EXE) estarán firmados.

Las variables protegidas y las entradas de PATH se deshabilitan antes de su eliminación, con verificación exacta del tipo de valor del registro al restaurar. Los valores de secretos se cifran mediante mecanismos específicos de cada proveedor: el texto en claro nunca se persiste en disco ni en registros. La IPC por named pipe usa indicadores antisquatting y validación de entrada (máximo 64 argumentos, límite de 32767 caracteres, rechazo de bytes nulos).

## Instalación

### Instalador MSI

Descarga el MSI desde GitHub Releases y ejecútalo. Crea accesos directos del menú Inicio automáticamente. Disponible en x64, x86 y ARM64.

### Portable

Descarga el ZIP portable desde GitHub Releases. Extrae y ejecuta env-manager.exe directamente. No requiere instalación.

### Solo CLI

Descarga el ZIP solo CLI para uso sin interfaz o mediante scripting: env-manager-cli.exe más los archivos .dll. Sin GUI, sin dependencia de WebView2.

### Requisitos previos

> Las compilaciones portable y solo CLI dependen del framework: requieren el .NET 10 Desktop Runtime en la máquina de destino. El instalador MSI comprueba .NET 10 durante la instalación y lo solicita automáticamente.
> El runtime WebView2 (para la GUI) viene preinstalado en Windows 11 y está disponible para Windows 10 21H2+ desde Microsoft.

Para las herramientas externas opcionales de proveedores de secretos (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7), consulta la Guía de proveedores de secretos.

### winget

> La distribución mediante winget está planificada pero aún no disponible. Haz seguimiento de las novedades en GitHub Issues.

### Desde el código fuente

Requiere .NET 10 SDK, Node.js 20+ y Rust estable con destino MSVC.

## Uso

### CLI

Consulta docs/cli-commands.md para la referencia completa de comandos.

### GUI

Ejecuta env-manager.exe. La GUI ofrece una lista de variables en tiempo real con búsqueda, filtrado por ámbito, edición en línea, un editor de PATH con reordenación por arrastrar y soltar, gestión de perfiles, selección de proveedores de secretos, panel de control del servicio, historial de auditoría e i18n en 10 idiomas.

## Arquitectura

- CLI: ejecutable de archivo único en C# .NET 10 — la capa de coordinación y la puerta de enlace del registro.
- Servicio: binario Rust independiente que gestiona el ciclo de vida del montaje de secretos mediante IPC por named pipe.
- GUI: frontend Tauri 2 + Svelte 4 que usa los mismos contratos IPC.

## Proveedores de secretos

8 backends de proveedores con comprobación previa de activación: los fallos aparecen como avisos ámbar en línea directamente en el editor de perfiles.

Consulta docs/secret-providers-guide.md para conocer los requisitos previos por proveedor, la configuración inicial única y los pasos para solucionar errores de activación.

## Modo servicio

env-manager-service.exe es un binario Rust independiente que gestiona el ciclo de vida del montaje de secretos mediante IPC por named pipe:

- RuntimeMode: Service (gestionado por SCM, arranque de la máquina), Background (iniciado por el usuario), Cli (puerta de enlace de un solo uso)
- Bucle de reconciliación: escaneo completo periódico cada 300 s, controlador idempotente por elemento, retraso de 30 s en el primer ciclo
- Arranque con certificados: la autenticación basada en certificados de Vault AppRole y Azure SP elimina los tokens de larga duración
- Registro de auditoría: audit-ledger.jsonl de solo añadido y encadenado por hash, con rotación de 100 MB y detección de manipulación
- IPC: indicador de pipe antisquatting, límite de 65536 bytes por solicitud, protocolo JSON delimitado por saltos de línea
- Watchdog: recuperación en dos capas — reinicio automático de SCM (modo Service) + watchdog de ping de 30 s de la GUI (modo Background)

## Documentación

## Mantenedores

## Publicaciones

Las publicaciones siguen la vía única de release-please: envía commits convencionales a main, revisa y fusiona el PR automático `chore(main): release X.Y.Z`, y la etiqueta `vX.Y.Z` dispara el pipeline completo de artefactos (portable / solo CLI / MSI para x64/x86/arm64, con atestados de procedencia de compilación). El flujo manual queda reservado para emergencias. Ver [docs/build-and-release.md](docs/build-and-release.md), sección "How to Release".

## Contribuciones

Se aceptan issues y PR. Lee primero AGENTS.md para conocer los límites de arquitectura y la política de pruebas.

## Licencia

Apache-2.0 (c) 2026 Env Manager Contributors.
