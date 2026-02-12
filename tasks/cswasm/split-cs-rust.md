# internalUsage 分離タスク: C# WASM系 / Rust WASM系の独立実装

対象リポジトリ: `SekibanWasmRuntime`
作成日: 2026-02-12

## 1. 背景と目的

`src/internalUsage` は現在、検証用途の実装が混在しており、以下の観点で責務分離が不十分。

- Native 実装の実例は Sekiban 本体側に既に存在する
- C# WASM と Rust WASM の検証観点が異なるのに、構成上の境界が曖昧
- 「Wasm Server / Client API 分離 + Blazor Web 管理」のパターンを明示しづらい

本タスクでは `internalUsage` を用途別に再編し、以下を達成する。

- Native 実例は SekibanWasmRuntime 側から外す（Sekiban 側を参照）
- C# 実装と Rust 実装を完全に独立したサンプルとして運用する
- 両者とも Aspire 構成を前提に、Web 管理 UI は Blazor で統一する

参照実装:
`/Users/tomohisa/dev/GitHub/SekibanWasmRuntime/submodules/Sekiban/dcb/internalUsages/DcbOrleans.Web`

## 2. 対象スコープ

### 2.1 新ディレクトリ方針

現行 `src/internalUsage` 配下を以下へ段階的に移行する。

- `src/internalUsages/cs/`
  - Aspire + C# WASM
  - Wasm Server / Client API 分離
  - Web は Blazor でデータ管理
- `src/internalUsages/rust/`
  - Aspire + Rust WASM
  - Wasm Server(C#) / Client API(Rust) 分離
  - Web は Blazor でデータ管理

注記:
- 以降は `internalUsages`（複数形）を正とする
- 必要に応じて互換期間として `src/internalUsage` からの案内 README を残し、最終的に撤去する

### 2.2 非スコープ

- Sekiban 本体側の Native 実装の改変
- 本番運用向けの性能最適化
- 既存 WASM ABI の大幅刷新（互換破壊を伴う変更）

## 3. 成功条件（Done Criteria）

1. `src/internalUsages/cs` と `src/internalUsages/rust` が存在し、役割が README で明確化されている
2. Native サンプルが SekibanWasmRuntime 側の実行対象から除外されている（参照先を明記）
3. CS 系サンプルが `dotnet run`（または Aspire 起動）で正常起動し、Blazor UI から基本データ操作が可能
4. Rust 系サンプルが同様に起動し、Wasm Server(C#)/Client API(Rust) 分離が確認できる
5. `DcbOrleans.Web` を参照した構成差分（共通点/相違点）が文書化されている
6. CI もしくはローカル再現手順で、両サンプルの最小 smoke test が通る

## 4. 技術仕様

### 4.1 共通アーキテクチャ

- オーケストレーション: .NET Aspire AppHost
- 管理 UI: Blazor Web
- API 分離:
  - Server: WASM 実行責務（インスタンス生成・イベント適用・クエリ実行）
  - Client API: Web/UI からの呼び出し境界
- 設定:
  - AppHost で依存サービス、接続情報、モジュールパスを管理
  - `appsettings.*` + 環境変数で切替可能にする

### 4.2 CS 系 (`src/internalUsages/cs`)

- WASM モジュール: C# 由来
- 期待構成（例）:
  - `SekibanWasm.Cs.AppHost`
  - `SekibanWasm.Cs.WasmServer`
  - `SekibanWasm.Cs.ClientApi`
  - `SekibanWasm.Cs.Web`（Blazor）
  - `SekibanWasm.Cs.Wasm`（C# wasm build）
- 実装方針:
  - Server/Client/Web の責務をプロジェクトで分割
  - 既存 `src/internalUsage` の C# WASM 資産を移植・再編

### 4.3 Rust 系 (`src/internalUsages/rust`)

- WASM モジュール: Rust 由来
- Wasm Server: C# 実装
- Client API: Rust 実装
- 期待構成（例）:
  - `SekibanWasm.Rust.AppHost`
  - `SekibanWasm.Rust.WasmServer`（C#）
  - `SekibanWasm.Rust.ClientApi`（Rust）
  - `SekibanWasm.Rust.Web`（Blazor）
  - `src/wasm-projectors/rust` の成果物連携
- 実装方針:
  - ABI 契約を固定し、言語境界は API 契約とシリアライズで吸収
  - Web 側は CS 系と同等 UX を維持し、差分検証を容易にする

### 4.4 Native 取り扱い

- SekibanWasmRuntime 側の `internalUsages` には Native 実例を置かない
- 参照先として Sekiban 側ドキュメント/実装パスを README に記載する

## 5. 実施ステップ（提案）

### Phase 0: 設計整理

- 現在 `src/internalUsage` にあるプロジェクトの役割を棚卸し
- `DcbOrleans.Web` から流用する設計パターンを抽出
- 命名規則・フォルダ規約を確定

### Phase 1: ディレクトリ分離

- `src/internalUsages/cs` と `src/internalUsages/rust` を作成
- 既存プロジェクトを段階移設（必要に応じて first move -> rename）
- ソリューション参照・プロジェクト参照を更新

### Phase 2: CS 系完成

- Aspire AppHost 構成を確立
- C# WASM ビルド出力を WasmServer で読み込み
- Client API / Web(Blazor) 接続を確認
- 最小 E2E（create/query/update 相当）を整備

### Phase 3: Rust 系完成

- Rust WASM ビルド出力と C# WasmServer 連携
- Rust Client API を経由する経路を確立
- Web(Blazor) からのデータ管理操作を確認
- 最小 E2E を整備

### Phase 4: Native 撤去と文書化

- Native 実例を internalUsages から除外
- 参照先（Sekiban 側）を README に記載
- 移行手順・トラブルシュートを追記

## 6. 検証手順（初版）

前提:
- .NET SDK（repo 指定版）
- Rust toolchain
- wasm build に必要な workload / target が導入済み

検証例:

```bash
# restore/build
 dotnet restore src/SekibanWasmRuntime.slnx
 dotnet build src/SekibanWasmRuntime.slnx -c Release

# cs sample
 dotnet run --project src/internalUsages/cs/SekibanWasm.Cs.AppHost

# rust sample（必要なら先に wasm build）
 dotnet run --project src/internalUsages/rust/SekibanWasm.Rust.AppHost
```

期待結果:
- CS 系/Rust 系ともに起動失敗しない
- Blazor UI からデータ作成・参照・更新・削除の最小操作が成功
- エラーログに ABI 不整合やモジュールロード失敗が出ない

## 7. リスクと対策

- ディレクトリ移動で参照切れが多発
  - 対策: Phase ごとに build を通し、移動を小さく刻む
- Rust Client API と C# Server の契約ズレ
  - 対策: 契約 DTO/JSON schema を固定し、契約テストを先行
- 既存 internalUsage の暗黙依存
  - 対策: 旧構成に移行 README を置いて段階撤去

## 8. 生成する成果物

- `src/internalUsages/cs/*` 一式
- `src/internalUsages/rust/*` 一式
- 移行 README（旧 -> 新）
- 起動/検証スクリプト（必要に応じて）
- CI 向け smoke test 定義

## 9. Issue 分解案（このタスクから起票する子 Issue）

1. `internalUsage` 棚卸しと `internalUsages` 命名/構成確定
2. CS 系 Aspire + Wasm Server/Client API/Web の分離実装
3. Rust 系 Aspire + Wasm Server(C#)/Client API(Rust)/Web の分離実装
4. Native 実例の除外と Sekiban 側参照導線の追加
5. CS/Rust 両系の smoke test と CI 連携
6. ドキュメント整備（構成図・起動手順・差分説明）

## 10. 受け入れ時チェックリスト

- [ ] `src/internalUsages/cs` が自己完結して起動可能
- [ ] `src/internalUsages/rust` が自己完結して起動可能
- [ ] Native 実例が runtime 側 internalUsages から外れている
- [ ] `DcbOrleans.Web` 参照点が文書化されている
- [ ] 最低 1 本ずつの起動確認ログまたは test 結果が残っている
- [ ] README から初見で実行手順が追える
