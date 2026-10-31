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

현대적이고 가벼운 Windows 환경 변수 관리자 — CLI와 GUI 듀얼 모드. Microsoft PowerToys에서 영감을 받았지만 독립 실행형이며 에이전트 친화적입니다.

**"모든 환경에 완벽하게 적응합니다."**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **한국어** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demos

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>

데모는 읽기 전용 CLI 명령을 보여줍니다: agents --summary, path health, get PATH, agents --json. `node scripts/demo/capture.mjs`로 다시 생성할 수 있습니다.

*위 데모 터미널 출력은 시뮬레이션 데이터를 사용합니다.*

### 갤러리

| | | |
|:-:|:-:|:-:|
| ![변수 보기](../../docs/assets/gallery/01-variables.png)<br>**변수** — 검색, 범위, 행별 작업 | ![system 범위](../../docs/assets/gallery/02-variables-system.png)<br>**system 범위** — 보호된 항목 잠김 | ![PATH 편집기](../../docs/assets/gallery/03-path-editor.png)<br>**PATH 편집기** — 끊어진/중복 항목 감지 |
| ![프로필](../../docs/assets/gallery/04-profiles.png)<br>**프로필** — 적용 / 실행 / 상속 | ![기록 및 가져오기](../../docs/assets/gallery/05-history.png)<br>**기록 및 가져오기** — 되돌릴 수 있는 변경 | ![보호](../../docs/assets/gallery/06-protection.png)<br>**보호** — 사용자 지정 잠금 |
| ![감사 원장](../../docs/assets/gallery/07-audit.png)<br>**감사 원장** — 해시 체인, 검증 가능 | ![서비스 관리자](../../docs/assets/gallery/08-service.png)<br>**서비스 관리자** — 시크릿 마운트 | ![설정](../../docs/assets/gallery/09-settings-i18n.png)<br>**설정** — 10개 언어 i18n |
| ![편집 대화상자](../../docs/assets/gallery/10-edit-dialog.png)<br>**편집 대화상자** — 확장된 값 미리보기 | ![라이트 테마](../../docs/assets/gallery/11-variables-light.png)<br>**라이트 테마** — 변형 | ![CLI](../../docs/assets/gallery/12-cli-terminal.png)<br>**CLI** — JSON 우선, 에이전트 네이티브 |


## Features
- 에이전트 네이티브 CLI — 일급 머신 계약을 갖춘 18개 이상의 명령: env-manager-cli agents --json은 구조화된 명령 사양을 출력하며, 모든 기능은 바이너리와 함께 배포되는 에이전트용 매뉴얼(AGENTS.cli.md)에 문서화되어 있습니다.
- 프로필 및 구성 — 글로벌 프로필은 레지스트리에 적용됩니다. Launch 프로필은 단일 프로세스에 격리된 env 블록을 주입합니다(레지스트리를 건드리지 않으며 WM_SETTINGCHANGE도 브로드캐스트하지 않음). 상속, 충돌 미리보기, 안전한 역순 롤백이 포함됩니다.
- 8가지 시크릿 공급자, 평문 제로 — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. 평문은 디스크나 로그에 절대 남지 않습니다.
- 기본적으로 보호 — 시스템 변수와 PATH 항목은 삭제하거나 이름을 바꿀 수 없습니다. 모든 쓰기는 3계층 직렬화 계약(mutex + write lock + 검증 후 교체)을 따릅니다.
- PATH 상태 검사 — 중복 및 유효하지 않은 항목을 감지하며 --fix / --dry-run을 지원합니다.
- 감사 원장 — 추가 전용, SHA256 해시 체인 히스토리. 롤백 및 재해 복구 내보내기를 지원합니다.
- CLI + GUI 듀얼 모드 — 스크립팅/CI용 C# CLI, 대화형 편집용 네이티브 Tauri 2 + Svelte GUI. 둘 다 동일한 레지스트리 계약을 거칩니다. 10개 언어 i18n 지원.
## For AI Agents
Env Manager는 인간뿐 아니라 LLM 에이전트가 운영하도록 설계되었습니다:
- AGENTS.md — 리포지토리 수준의 에이전트 지침(아키텍처, 하드 경계, 테스트 정책).
- AGENTS.cli.md — CLI 바이너리와 함께 배포되어 모든 에이전트가 런타임에 계약을 발견할 수 있습니다.
- 기능 범위별 에이전트 서피스 — secret-providers.json의 옵트인 agentCapabilities 화이트리스트를 통해 배포 환경이 에이전트의 병렬 set/delete 호출을 거부할 수 있습니다.
## Security
> 현재 빌드는 코드 서명되지 않았습니다. Windows SmartScreen이 첫 실행 시 인식할 수 없는 앱 경고를 표시할 수 있습니다 — [추가 정보]를 클릭한 다음 [실행]을 클릭하세요. SignPath Foundation을 통해 무료 오픈소스 코드 서명을 신청했습니다. 승인되면 모든 릴리스 아티팩트(MSI + EXE)가 서명됩니다.
보호된 변수와 PATH 항목은 삭제 전에 비활성화되며, 복원 시 레지스트리 값 종류를 정확히 검증합니다. 시크릿 값은 공급자별 메커니즘으로 암호화됩니다 — 평문은 디스크나 로그에 절대 남지 않습니다. 명명된 파이프 IPC는 안티 스쿼팅 플래그와 입력 검증(인수 최대 64개, 32767자 상한, null 바이트 거부)을 사용합니다.

### 평문이 존재할 수 있는 곳 (시크릿 경계 표)

| 싱크 | 평문이 남는가 | 수명 | 정리 | 근거 |
|---|---|---|---|---|
| `profiles.json` / `secretMount.json` | secret 명령에서는 남지 않음 — 봉투 `{provider, version, createdAt, ciphertext, targetName}` 또는 `mount:<id>` 참조만 저장. 범용 `add-var`/`edit-var` 명령은 마커를 우회함 ([평문 수명 주기](../../docs/architecture.md#v070-dpapi-secrets-runtime) 참조) | 시크릿이 제거될 때까지 | `profile remove-secret`이 봉투와 공급자 측 상태를 삭제 | 하드 경계; `plaintext-never-in-envelope` 계약 단언 |
| dpapi-current-user | 남지 않음 — DPAPI `CryptProtectData`, CurrentUser 범위 | 봉투 안 | 봉투와 함께 | 하드 경계 (v0.7.0) |
| credential-manager | 남지 않음 — CredMan blob 자체가 저장 전에 DPAPI로 암호화됨 | 공급자 `Delete`까지 | remove-secret 시 `Delete` | [공급자 가이드](../../docs/secret-providers-guide.md) |
| powershell-secretmanagement | SecretStore 보관소 파일은 암호화된 상태 유지 — 단, 호출마다 평문이 `pwsh -EncodedCommand` 인수(base64 스크립트)를 거쳐 감 | pwsh 프로세스 수명; 보관소 항목은 제거될 때까지 | 인수는 프로세스와 함께 소멸; 보관소 항목은 remove-secret 시 삭제 | 하드 경계 (v0.7.4) |
| vault-kv2 · azure-keyvault · aws-secretsmanager · 1password | 로컬 평문 없음 — 프로필은 경로/시크릿 ID 참조만 보관하고 시크릿은 원격 백엔드에 남음 | 백엔드 측 운영 정책 | 공급자 `Delete`; `secret-provider rotate`로 재암호화 | [공급자 가이드](../../docs/secret-providers-guide.md) |
| sops | 봉투에는 암호문만 — **추가로** 암호화/복호화 중 호출마다 만드는 디렉터리에 평문 `secret.json` / `secret.dec.json`이 생김: `%TEMP%\env-manager-sops-*`, 또는 회전이 위임된 경우 서비스 소유 루트 안의 디렉터리 | 호출당 몇 초 | 그 디렉터리를 `Directory.Delete`. 서비스가 위임한 경우, CLI 자식 프로세스가 끝난 뒤 서비스가 루트 전체를 삭제함 — CLI를 종료시킨 중지도 포함 — 삭제는 제한된 횟수로 재시도하고, 그래도 실패하면 error 수준 로그를 남김(조용히 넘어가지 않음). 수동 실행 시 삭제는 최선 노력이며 충돌로 남겨질 수 있음. 디렉터리는 생성 시부터 소유자 전용(상속 해제; 현재 ID, SYSTEM, Administrators)이므로 남은 파일은 이 ID들만 읽을 수 있음 — 단, 그 DACL 적용에 실패하면 원래 상속받았을 ACL(`%TEMP%`의 것, 또는 위임 시 서비스 scratch 루트의 것)로 되돌아감 | 하드 경계 (v0.7.11); `SopsProvider`, `SecretTempDirectory`, `service/src/sops_scratch.rs` |
| 실행 프로세스 메모리 (`env-manager-cli`) | 남음 — 복호화된 바이트가 실행 프로세스에 잠시 존재 | 밀리초 단위, 주입/reveal 완료까지 | `SecretString`이 Dispose 시 `char[]`를 0으로 지움 — **최선 노력**: GC 복사본과 string 중간값은 더 오래 남을 수 있음 | `SecretString` 제로화; [canary 무유출 그물](../../docs/architecture.md#canary-zero-leak-assertion-net-and-goldensnapshot-layers) |
| 실행된 자식 프로세스의 환경 블록 | 남음 — 주입된 환경 변수는 자식 프로세스 안에서 평문 | 자식 프로세스 수명 | 불가능 — OS 설계상 환경 블록은 같은 사용자가 읽을 수 있음 | Launch 프로필 설계 |
| `profile reveal-secret` 표준 출력 | 남음 — **설계상 의도됨**; 평문 출력 경로 두 가지 중 하나 (`profile reveal-secret`, `profile show --reveal`) | 터미널 스크롤백을 지울 때까지 | 없음 — 사용자가 직접 실행 | 하드 경계; canary 그물은 이 싱크들을 의도적으로 건너뜀 |
| 생존 키트 임시 파일 (`%TEMP%\env-manager-kit-<guid>\` 아래의 `envmanager-kit-*.json`, `mount-survival-kit.tmp.json`) | 남음 — 키트 조립 중 잠깐 평문 임시 파일이 생김. CLI 측 키트 파일은 `%TEMP%\env-manager-kit-<guid>\` 안에 쓰이며, 이 디렉터리는 생성 시부터 소유자 전용(상속 해제; 현재 ID, SYSTEM, Administrators)이므로 남은 파일은 이 ID들만 읽을 수 있음 — 단, 그 DACL 적용에 실패하면 상속된 `%TEMP%` ACL로 되돌아감. 서비스 측 `mount-survival-kit.tmp.json`은 ledger 디렉터리에 있으며 `%TEMP%` 아래가 아님 | 몇 초 | 암호화 후 최선 노력으로 삭제하고, 이어서 담고 있던 디렉터리도 최선 노력으로 삭제: 디렉터리 삭제가 실패하면 빈 디렉터리가 남고, 파일 삭제도 실패했다면 평문 파일도 남음 — 둘 다 최선 노력이며 위 sops 행과 같은 기준 | `AuditLedgerMigration.RunAuditExportSurvivalKit`, `SecretTempDirectory.Create`; `service/src/audit_ledger.rs` |
| `audit-ledger.jsonl` | 남지 않음 — 변수 이름 + `<redacted>`/`<encrypted>` 마커만; CLI 측 항목은 추가로 AES-256-GCM 저장 시 암호화 (DEK는 DPAPI로 래핑) | 추가 전용, 100MB 로테이션 | 로테이션 | 하드 경계 (v0.9.13); `AuditCrypto` 정적 암호화 |
| `export-state` 파일 | 남지 않음 — 이중 계층: AES-256-GCM 페이로드 + DPAPI로 래핑한 DEK + HMAC-SHA256 | 사용자가 파일을 삭제할 때까지 | 사용자 관리 | `AuditCrypto` (v0.9.13) |
| `logs/env-manager.log` / stderr | 설계상 남지 않음 — 예외 텍스트는 먼저 22개 시크릿 패턴으로 정리됨; **최선 노력** 허용 목록: 인식되지 않은 시크릿 형식은 여전히 새어 나갈 수 있으며 512자 절단으로 범위를 제한 | 로그 보존 기간 | — | `ScrubExceptionMessage` 허용 목록; `CanaryRedactionTests` + 7개 싱크 canary 그물 |
| GUI (WebView2) / Rust 서비스 | 남지 않음 — GUI는 `<encrypted>` 자리표시자만 렌더링하고 reveal 표시가 없음; 서비스는 값을 저장하지 않음 | — | — | 아키텍처 시크릿 경계 |
| Windows 레지스트리 | secret 명령에서는 남지 않음 — Launch 프로필은 레지스트리에 쓸 수 없고 apply가 거부함; 범용 명령의 이름 변경 허점은 도달할 수 있음 ([평문 수명 주기](../../docs/architecture.md#v070-dpapi-secrets-runtime) 참조) | — | — | 하드 경계; `launch-env-injection.Tests.ps1` 불변식 |

이 표는 사용자용 요약입니다. 범용 명령의 허점을 포함한 표면별 공식 허용 목록은 `docs/architecture.md`의 Plaintext lifecycle 절입니다.

메모리 제로화와 로그 정리는 최선 노력의 위생 조치이지 보장이 아닙니다 — Microsoft가 `SecureString`에 대해 문서화한 것과 같은 주의 사항입니다(불변 string, GC 재배치, interop 복사본). 검증할 수 있는 주장은 테스트로 뒷받침합니다: `tests/canary-redaction.Tests.ps1`은 7개 출력 싱크에서 심어 둔 canary를 검사하고, 공유 계약 스위트는 8개 공급자 모두에 대해 `plaintext-never-in-envelope`를 단언합니다.
## Install
### MSI 설치 관리자
GitHub Releases에서 MSI를 다운로드하여 실행하세요. 시작 메뉴 바로 가기가 자동으로 생성됩니다. x64, x86, ARM64 지원.
### 휴대용
GitHub Releases에서 휴대용 ZIP을 다운로드하세요. 압축을 풀고 env-manager.exe를 직접 실행하세요. 설치가 필요 없습니다.
### CLI 전용
헤드리스 또는 스크립트 용도의 CLI 전용 ZIP을 다운로드하세요: env-manager-cli.exe 및 .dll 파일. GUI 없음, WebView2 종속성 없음.
### 사전 요구 사항
> 휴대용 및 CLI 전용 빌드는 프레임워크 종속적입니다. 대상 머신에 .NET 10 Desktop Runtime이 필요합니다. MSI 설치 관리자는 설치 시 .NET 10을 확인하고 자동으로 프롬프트를 표시합니다.
> WebView2 Runtime(GUI용)은 Windows 11에 사전 설치되어 있으며 Windows 10 21H2+에서는 Microsoft에서 제공합니다.
선택적 외부 시크릿 공급자 도구(SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7)에 대해서는 Secret Providers Guide를 참조하세요.
### winget
> winget 배포는 계획 중이며 아직 제공되지 않습니다. 업데이트는 GitHub Issues에서 추적하세요.
### 소스에서 빌드
.NET 10 SDK, Node.js 20+, MSVC 타깃의 Rust stable이 필요합니다.
## Usage
### CLI
전체 명령 참조는 docs/cli-commands.md를 참조하세요.
### GUI
env-manager.exe를 실행하세요. GUI는 검색, 범위 필터링, 인라인 편집이 가능한 실시간 변수 목록, 드래그 앤 드롭으로 순서를 바꿀 수 있는 PATH 편집기, 프로필 관리, 시크릿 공급자 선택, 서비스 제어 패널, 감사 기록, 10개 언어 i18n을 제공합니다.
## Architecture
- CLI: C# .NET 10 단일 파일 실행 파일 — 조정 계층이자 레지스트리 게이트웨이.
- Service: 명명된 파이프 IPC를 통해 시크릿 마운트 수명 주기를 관리하는 독립 실행형 Rust 바이너리.
- GUI: 동일한 IPC 계약을 사용하는 Tauri 2 + Svelte 4 프런트엔드.
## Secret Providers
활성화 사전 점검을 갖춘 8가지 공급자 백엔드 — 실패는 프로필 편집기에서 인라인 앰버 배너로 직접 표시됩니다.
공급자별 사전 요구 사항, 일회성 설정, 활성화 오류 수정 단계는 docs/secret-providers-guide.md를 참조하세요.
## Service Mode
env-manager-service.exe는 명명된 파이프 IPC를 통해 시크릿 마운트 수명 주기를 관리하는 독립 실행형 Rust 바이너리입니다:
- RuntimeMode: Service(SCM 관리, 머신 부팅), Background(사용자 시작), Cli(원샷 게이트웨이)
- 재조정 루프: 300초 주기 전체 검사, 항목별 멱등 핸들러, 30초 첫 틱 지연
- 인증서 부트스트랩: Vault AppRole 및 Azure SP 인증서 기반 인증으로 장기 토큰 제거
- 감사 원장: 추가 전용 해시 체인 audit-ledger.jsonl, 100MB 로테이션 및 변조 감지
- IPC: 안티 스쿼팅 파이프 플래그, 65536바이트 요청 상한, 개행 구분 JSON 프로토콜
- 워치독: 2계층 복구 — SCM 자동 재시작(Service 모드) + GUI 30초 핑 워치독(Background 모드)
## Documentation
## Maintainers
## Contributing
이슈와 PR을 환영합니다. 아키텍처 경계와 테스트 정책을 위해 먼저 AGENTS.md를 읽어주세요.
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
