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

데모는 읽기 전용 CLI 명령을 보여줍니다: agents --summary, path health, get PATH, agents --json. vhs docs/assets/demo.tape로 다시 생성할 수 있습니다.
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
