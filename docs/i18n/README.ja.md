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

モダンで軽量な Windows 環境変数マネージャー — CLI と GUI のデュアルモード。Microsoft PowerToys に着想を得つつ、スタンドアロンでエージェントフレンドリーな設計です。

**「あらゆる環境にシームレスに適応します。」**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](../../LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

<!-- README-I18N:START -->
**言語:** [English](../../README.md) · **日本語** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [العربية](README.ar.md)
<!-- README-I18N:END -->

</div>


---


## Demos

<p align="center">
  <img src="../../docs/assets/demo.gif" alt="Env Manager CLI demo" width="100%">
</p>

デモでは読み取り専用の CLI コマンドの動作を紹介しています: agents --summary、path health、get PATH、agents --json。vhs docs/assets/demo.tape で再生成できます。
## Features
- エージェントネイティブな CLI — ファーストクラスの機械契約を備えた 18 以上のコマンド: env-manager-cli agents --json は構造化されたコマンド仕様を出力し、すべての機能はバイナリに同梱されるエージェント向けマニュアル (AGENTS.cli.md) に記載されています。
- プロファイルと設定 — グローバルプロファイルはレジストリに適用されます。Launch プロファイルは単一プロセスに隔離された env ブロックを注入します (レジストリには一切触れず、WM_SETTINGCHANGE もブロードキャストしません)。継承、競合プレビュー、安全な逆順ロールバックに対応しています。
- 8 つのシークレットプロバイダー、平文ゼロ — DPAPI、Credential Manager、SecretStore、HashiCorp Vault、SOPS、Azure Key Vault、1Password、AWS Secrets Manager。平文がディスクやログに残ることはありません。
- デフォルトで保護 — システム変数と PATH エントリは削除・名前変更できません。すべての書き込みは 3 層の直列化コントラクト (mutex + write lock + 検証後スワップ) に従います。
- PATH ヘルスチェック — 重複や無効なエントリを検出し、--fix / --dry-run に対応しています。
- 監査台帳 — 追記のみ、SHA256 ハッシュチェーンによる履歴。ロールバックと災害復旧エクスポートに対応。
- CLI + GUI デュアルモード — スクリプト/CI 用の C# CLI、対話型編集用のネイティブ Tauri 2 + Svelte GUI。どちらも同じレジストリコントラクトを経由します。10 言語の i18n 対応。
## For AI Agents
Env Manager は人間だけでなく LLM エージェントによる運用も想定して設計されています:
- AGENTS.md — リポジトリレベルのエージェント向け手順書 (アーキテクチャ、ハードバウンダリ、テスト方針)。
- AGENTS.cli.md — CLI バイナリに同梱され、あらゆるエージェントが実行時にコントラクトを発見できます。
- 機能単位のエージェントサーフェス — secret-providers.json のオプトイン方式の agentCapabilities ホワイトリストにより、デプロイメントはエージェントからの並行 set/delete 呼び出しを拒否できます。
## Security
> 現在のビルドはコード署名されていません。初回起動時に Windows SmartScreen が「認識されていないアプリ」の警告を表示する場合があります — [詳細情報] をクリックしてから [実行] を選択してください。無料のオープンソース向けコード署名を SignPath Foundation 経由で申請済みです。承認され次第、すべてのリリース成果物 (MSI + EXE) が署名されます。
保護された変数と PATH エントリは削除前に無効化され、復元時にはレジストリの値の型を厳密に検証します。シークレット値はプロバイダー固有の仕組みで暗号化されます — 平文がディスクやログに残ることはありません。名前付きパイプ IPC はアンチスクワッティングフラグと入力検証 (引数最大 64、32767 文字上限、null バイト拒否) を採用しています。
## Install
### MSI インストーラー
GitHub Releases から MSI をダウンロードして実行してください。スタートメニューのショートカットが自動的に作成されます。x64、x86、ARM64 に対応。
### ポータブル版
GitHub Releases からポータブル ZIP をダウンロードしてください。解凍して env-manager.exe を直接実行します。インストールは不要です。
### CLI のみ
ヘッドレスまたはスクリプト用途向けに CLI 専用 ZIP をダウンロードしてください: env-manager-cli.exe と .dll ファイル。GUI なし、WebView2 依存なし。
### 前提条件
> ポータブル版と CLI 専用ビルドはフレームワーク依存です。対象マシンに .NET 10 Desktop Runtime が必要です。MSI インストーラーはインストール時に .NET 10 をチェックし、自動的にプロンプトを表示します。
> WebView2 Runtime (GUI 用) は Windows 11 にプレインストールされており、Windows 10 21H2+ では Microsoft から入手できます。
オプションの外部シークレットプロバイダーツール (SOPS、1Password CLI、Vault CLI、AWS CLI、Azure CLI、PowerShell 7) については、Secret Providers Guide を参照してください。
### winget
> winget での配布は計画中ですが、まだ利用できません。更新情報は GitHub Issues で追跡してください。
### ソースからビルド
.NET 10 SDK、Node.js 20+、MSVC ターゲットの Rust stable が必要です。
## Usage
### CLI
完全なコマンドリファレンスは docs/cli-commands.md を参照してください。
### GUI
env-manager.exe を実行してください。GUI は検索・スコープフィルタリング・インライン編集に対応したリアルタイム変数リスト、ドラッグ & ドロップで並べ替え可能な PATH エディター、プロファイル管理、シークレットプロバイダー選択、サービスコントロールパネル、監査履歴、10 言語の i18n を提供します。
## Architecture
- CLI: C# .NET 10 の単一ファイル実行可能ファイル — 調整レイヤー兼レジストリゲートウェイ。
- Service: 名前付きパイプ IPC 経由でシークレットマウントのライフサイクルを管理するスタンドアロンの Rust バイナリ。
- GUI: 同じ IPC コントラクトを使用する Tauri 2 + Svelte 4 フロントエンド。
## Secret Providers
アクティベーション前チェックを備えた 8 つのプロバイダーバックエンド — 失敗はプロファイルエディター内にインラインの琥珀色バナーとして直接表示されます。
プロバイダーごとの前提条件、初期セットアップ、アクティベーションエラーの修正手順については docs/secret-providers-guide.md を参照してください。
## Service Mode
env-manager-service.exe は、名前付きパイプ IPC 経由でシークレットマウントのライフサイクルを管理するスタンドアロンの Rust バイナリです:
- RuntimeMode: Service (SCM 管理、マシン起動時)、Background (ユーザー起動)、Cli (ワンショットゲートウェイ)
- リコンサイルループ: 300 秒間隔の定期的なフルスキャン、項目ごとの冪等ハンドラー、30 秒の初回ティック遅延
- 証明書ブートストラップ: Vault AppRole と Azure SP の証明書ベース認証により、長期間有効なトークンを排除
- 監査台帳: 追記のみのハッシュチェーン方式 audit-ledger.jsonl、100MB ローテーションと改ざん検出
- IPC: アンチスクワッティングパイプフラグ、65536 バイトのリクエスト上限、改行区切り JSON プロトコル
- ウォッチドッグ: 2 層のリカバリー — SCM 自動再起動 (Service モード) + GUI の 30 秒ピングウォッチドッグ (Background モード)
## Documentation
## Maintainers
## Contributing
Issue と PR を歓迎します。アーキテクチャの境界とテスト方針については、まず AGENTS.md をお読みください。
## License
Apache-2.0 (c) 2026 Env Manager Contributors.
