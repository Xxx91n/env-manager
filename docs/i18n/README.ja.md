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

デモでは読み取り専用の CLI コマンドの動作を紹介しています: agents --summary、path health、get PATH、agents --json。`node scripts/demo/capture.mjs` で再生成できます。

*上記のデモ端末出力はシミュレーションデータを使用しています。*

### ギャラリー

| | | |
|:-:|:-:|:-:|
| ![変数ビュー](../../docs/assets/gallery/01-variables.png)<br>**変数** — 検索、スコープ、行ごとの操作 | ![System スコープ](../../docs/assets/gallery/02-variables-system.png)<br>**System スコープ** — 保護エントリはロック済み | ![PATH エディター](../../docs/assets/gallery/03-path-editor.png)<br>**PATH エディター** — 無効・重複エントリの検出 |
| ![プロファイル](../../docs/assets/gallery/04-profiles.png)<br>**プロファイル** — 適用 / 起動 / 継承 | ![履歴とインポート](../../docs/assets/gallery/05-history.png)<br>**履歴とインポート** — 元に戻せる変更 | ![保護](../../docs/assets/gallery/06-protection.png)<br>**保護** — カスタムロック |
| ![監査台帳](../../docs/assets/gallery/07-audit.png)<br>**監査台帳** — ハッシュチェーンで検証可能 | ![サービスマネージャー](../../docs/assets/gallery/08-service.png)<br>**サービスマネージャー** — シークレットのマウント | ![設定](../../docs/assets/gallery/09-settings-i18n.png)<br>**設定** — 10言語の i18n |
| ![編集ダイアログ](../../docs/assets/gallery/10-edit-dialog.png)<br>**編集ダイアログ** — 展開後の値のプレビュー | ![ライトテーマ](../../docs/assets/gallery/11-variables-light.png)<br>**ライトテーマ** 版 | ![CLI](../../docs/assets/gallery/12-cli-terminal.png)<br>**CLI** — JSON ファースト、エージェントネイティブ |


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

### 平文が存在しうる場所（シークレット境界表）

| 出力先（sink） | 平文が残るか | 存続期間 | クリーンアップ | 根拠 |
|---|---|---|---|---|
| `profiles.json` / `secretMount.json` | secret 系コマンドでは残らない — エンベロープ `{provider, version, createdAt, ciphertext, targetName}` または `mount:<id>` 参照のみ。汎用の `add-var`/`edit-var` はマーカーを迂回する（[平文のライフサイクル](../../docs/architecture.md#v070-dpapi-secrets-runtime)を参照） | シークレットが削除されるまで | `profile remove-secret` がエンベロープとプロバイダー側の状態を削除 | ハード境界。`plaintext-never-in-envelope` 契約アサーション |
| dpapi-current-user | 残らない — DPAPI `CryptProtectData`、CurrentUser スコープ | エンベロープ内 | エンベロープとともに | ハード境界（v0.7.0） |
| credential-manager | 残らない — CredMan の blob 自体が永続化前に DPAPI で暗号化される | プロバイダーの `Delete` まで | remove-secret 時に `Delete` | [プロバイダーガイド](../../docs/secret-providers-guide.md) |
| powershell-secretmanagement | SecretStore の保管庫ファイルは暗号化されたまま — ただし呼び出しごとに平文が `pwsh -EncodedCommand` 引数（base64 スクリプト）を経由する | pwsh プロセスの存続期間。保管庫エントリは削除まで | 引数はプロセスとともに消滅。保管庫エントリは remove-secret で削除 | ハード境界（v0.7.4） |
| vault-kv2 · azure-keyvault · aws-secretsmanager · 1password | ローカルに平文なし — プロファイルはパス/シークレット ID の参照のみを保持し、シークレットはリモートのバックエンドに留まる | バックエンド側の運用ポリシー | プロバイダーの `Delete`。`secret-provider rotate` で再暗号化 | [プロバイダーガイド](../../docs/secret-providers-guide.md) |
| sops | エンベロープは暗号文のみ — **加えて**暗号化/復号中は呼び出しごとのディレクトリに平文の `secret.json` / `secret.dec.json` が置かれる：`%TEMP%\env-manager-sops-*`、またはローテーションが委任された場合はサービス所有のルート内のディレクトリ | 呼び出しごとに数秒 | そのディレクトリを `Directory.Delete`。サービスから委任された場合、CLI 子プロセスが終了した後にサービスがルート全体を削除する — CLI を終了させた停止の場合も含む — 削除は回数制限付きで再試行し、それでも失敗したら error レベルでログに記録する（黙って残さない）。手動実行時の削除はベストエフォートで、クラッシュで取り残されうる。ディレクトリは作成時から所有者のみ（継承オフ。現在の ID、SYSTEM、Administrators）なので、残留物を読めるのはこれらの ID だけ — ただしその DACL の設定に失敗した場合は、本来継承するはずだった ACL（`%TEMP%` のもの、または委任時はサービスの scratch ルートのもの）を保持する | ハード境界（v0.7.11）。`SopsProvider`、`SecretTempDirectory`、`service/src/sops_scratch.rs` |
| 起動元プロセスのメモリ（`env-manager-cli`） | 残る — 復号されたバイトが起動元プロセスに一時的に存在する | ミリ秒単位。注入/reveal の完了まで | `SecretString` が Dispose 時に `char[]` をゼロ化 — **ベストエフォート**：GC のコピーや string の中間値はそれより長く残りうる | `SecretString` のゼロ化。[canary ゼロリーク網](../../docs/architecture.md#canary-zero-leak-assertion-net-and-goldensnapshot-layers) |
| 起動された子プロセスの環境ブロック | 残る — 注入された環境変数は子プロセス内で平文 | 子プロセスの存続期間 | 不可能 — OS の設計上、環境ブロックは同一ユーザーから読める | Launch プロファイルの設計 |
| `profile reveal-secret` の標準出力 | 残る — **設計どおり**。平文を出力する 2 つの経路の 1 つ（`profile reveal-secret`、`profile show --reveal`） | ターミナルのスクロールバックが消去されるまで | なし — ユーザーが明示的に実行 | ハード境界。canary 網はこれらの sink を意図的にスキャンしない |
| サバイバルキットの一時ファイル（`%TEMP%\env-manager-kit-<guid>\` 配下の `envmanager-kit-*.json`、`mount-survival-kit.tmp.json`） | 残る — キット組み立て中に短時間の平文一時ファイル。CLI 側のキットファイルは `%TEMP%\env-manager-kit-<guid>\` 内に書かれ、このディレクトリは作成時から所有者のみ（継承オフ。現在の ID、SYSTEM、Administrators）なので、残留物を読めるのはこれらの ID だけ — ただしその DACL の設定に失敗した場合は、継承された `%TEMP%` の ACL を保持する。サービス側の `mount-survival-kit.tmp.json` は ledger ディレクトリに置かれ、`%TEMP%` 配下ではない | 数秒 | 暗号化後にベストエフォートで削除し、続いて格納していたディレクトリもベストエフォートで削除：ディレクトリ削除に失敗すると空のディレクトリが残り、ファイル削除も失敗していれば平文ファイルも残る — どちらもベストエフォートで、上の sops 行と同じ扱い | `AuditLedgerMigration.RunAuditExportSurvivalKit`、`SecretTempDirectory.Create`。`service/src/audit_ledger.rs` |
| `audit-ledger.jsonl` | 残らない — 変数名と `<redacted>`/`<encrypted>` マーカーのみ。CLI 側のエントリはさらに AES-256-GCM で保存時暗号化（DEK は DPAPI でラップ） | 追記専用、100MB でローテーション | ローテーション | ハード境界（v0.9.13）。`AuditCrypto` 静的暗号化 |
| `export-state` ファイル | 残らない — 二重構造：AES-256-GCM ペイロード + DPAPI でラップした DEK + HMAC-SHA256 | ユーザーがファイルを削除するまで | ユーザー管理 | `AuditCrypto`（v0.9.13） |
| `logs/env-manager.log` / stderr | 設計上は残らない — 例外テキストは先に 22 種のシークレットパターンで除去される。**ベストエフォート**の許可リスト：未知のシークレット形式は漏れうるが、512 文字の切り詰めで影響範囲を制限 | ログの保持期間 | — | `ScrubExceptionMessage` 許可リスト。`CanaryRedactionTests` + 7 sink の canary 網 |
| GUI（WebView2）/ Rust サービス | 残らない — GUI は `<encrypted>` プレースホルダーのみを表示し reveal 表示を持たない。サービスは値を永続化しない | — | — | アーキテクチャのシークレット境界 |
| Windows レジストリ | secret 系コマンドでは残らない — Launch プロファイルはレジストリに書き込めず apply も拒否する。汎用コマンドの名前変更の抜け穴は到達しうる（[平文のライフサイクル](../../docs/architecture.md#v070-dpapi-secrets-runtime)を参照） | — | — | ハード境界。`launch-env-injection.Tests.ps1` 不変条件 |

この表はユーザー向けの要約です。汎用コマンドの抜け穴を含む、サーフェスごとの正式な許可リストは `docs/architecture.md` の Plaintext lifecycle 節です。

メモリのゼロ化とログの除去はベストエフォートの衛生対策であり保証ではありません — Microsoft が `SecureString` について記している注意点（不変の string、GC による再配置、interop のコピー）と同じです。検証できる主張はテストで裏付けています：`tests/canary-redaction.Tests.ps1` は 7 つの出力 sink に仕込んだ canary をスキャンし、共有契約スイートは 8 つすべてのプロバイダーで `plaintext-never-in-envelope` を検証します。
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
