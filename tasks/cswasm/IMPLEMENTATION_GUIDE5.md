# SekibanWasmRuntime 安定ベースライン確認ガイド v5（split-cs-rust 着手前の最終確認版）

対象: `J-Tech-Japan/SekibanWasmRuntime`

## 1. 目的

`tasks/cswasm/split-cs-rust.md` の着手前に、ビルドパイプライン全体が安定して再現可能であることを確認・文書化する。

GUIDE1-4 で技術的な修正はすべて完了済み。本ガイドは「修正の実装」ではなく「安定状態の確認と運用ルールの統合リファレンス」である。

## 2. Success Criteria

以下をすべて満たしたら完了。Issue #22 の Tasks と 1:1 で対応する。

| # | 基準 | Issue #22 タスク |
|---|------|-----------------|
| 1 | `build-csharp-wasm.sh` が macOS arm64（Docker モード）と Linux CI（native モード）で成功し、`csharp-weather.wasm` が生成される | Docker SDK と global.json の整合 |
| 2 | `dotnet restore src/SekibanWasmRuntime.ci.slnx` で NU1507 が 0 件 | NuGet config の分離 |
| 3 | `dotnet build src/SekibanWasmRuntime.ci.slnx -c Release` がエラーなしで成功 | NU1507 除去の副次確認 |
| 4 | `dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build` が green | ビルドパイプライン全体の検証 |
| 5 | `dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget` が成功 | ビルドパイプライン全体の検証 |
| 6 | `build-rust-wasm.sh` が成功し、`rust-weather.wasm` が生成される | E2E 検証 |
| 7 | `tasks/cswasm/README.md` に最終運用ルール（GUIDE5 セクション）が記載されている | README 更新 |

## 3. ベースライン状態（GUIDE1-4 で確立済み）

### 3.1 Docker SDK 整合（GUIDE4 で修正）

- `build-csharp-wasm.sh` は `mcr.microsoft.com/dotnet/sdk:10.0`（GA タグ）を使用
- コンテナ起動時に `dotnet --list-sdks` で `10.0.1xx` 系の存在を検証
- `global.json` の `rollForward: "latestFeature"` により `10.0.1xx` feature band 内の最新 SDK が選択される
- macOS arm64 では `--platform linux/amd64` で Docker 経由ビルド、Linux CI では native ビルド

### 3.2 NuGet config 分離（GUIDE4 で修正）

- `NuGet.config`: `nuget.org` のみ。`packageSourceMapping` なし（単一ソースのため NU1507 発生条件を排除）
- `NuGet.wasm.config`: `nuget.org` + `dotnet10` + `dotnet-experimental`（ILCompiler feed 付き、`packageSourceMapping` あり）
- 通常の `dotnet restore/build/test/pack` は `NuGet.config` を使用
- `build-csharp-wasm.sh` のみ `--configfile NuGet.wasm.config` で WASM 用設定を使用

### 3.3 NU1507 除去（GUIDE4 で修正）

- `NuGet.config` が単一ソースのため NU1507 発生条件を完全に排除
- `SekibanWasm.Wasm`（ILCompiler 依存）は `ci.slnx` から除外済み
- `ci.slnx` は通常の restore/build/test/pack 用、`slnx` は開発用（WASM プロジェクト含む）

### 3.4 ILCompiler パッケージ条件（GUIDE3 で修正）

- `runtime.linux-x64.microsoft.dotnet.ilcompiler.llvm`: 無条件で参照（CI および Docker 内ビルドの基本パス）
- `runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm`: `EnableMacIlCompilerRuntime=true` でのみ有効（opt-in）
- macOS arm64 の ILCompiler ランタイムは不安定なため、Docker 経由の Linux ビルドに統一

## 4. ファイルインベントリ

ビルドシステムを構成する各ファイルの役割一覧。

### 4.1 設定ファイル

| ファイル | 役割 |
|---------|------|
| `global.json` | SDK バージョン固定（`10.0.100` + `rollForward: latestFeature`） |
| `Directory.Packages.props` | Central Package Management（全パッケージのバージョン一元管理） |
| `NuGet.config` | 通常 restore 用（`nuget.org` のみ） |
| `NuGet.wasm.config` | WASM publish 用（ILCompiler feed 付き） |

### 4.2 ソリューションファイル

| ファイル | 用途 | WASM プロジェクト |
|---------|------|-----------------|
| `src/SekibanWasmRuntime.ci.slnx` | CI 用（restore/build/test/pack） | 除外 |
| `src/SekibanWasmRuntime.slnx` | 開発用（全プロジェクト） | 含む |

### 4.3 ビルドスクリプト

| ファイル | 役割 |
|---------|------|
| `build/scripts/build-csharp-wasm.sh` | C# WASM モジュールのビルド（Linux: native, macOS: Docker） |
| `build/scripts/build-rust-wasm.sh` | Rust WASM モジュールのビルド |
| `build/scripts/run-e2e.sh` | E2E 実行スクリプト |

### 4.4 検証スクリプト

| ファイル | 検証対象 |
|---------|---------|
| `build/scripts/tests/test-build-csharp-wasm.sh` | `build-csharp-wasm.sh` の構造（Docker 設定、SDK 検証、パス定義） |
| `build/scripts/tests/test-nuget-source-mapping.sh` | `NuGet.config` が `nuget.org` のみであること |
| `build/scripts/tests/test-nuget-wasm-config.sh` | `NuGet.wasm.config` が 3 feed + mapping を持つこと |
| `build/scripts/tests/test-csproj-ilcompiler.sh` | ILCompiler パッケージ条件（Linux 無条件、macOS opt-in） |

### 4.5 成果物

| ファイル | 生成元 |
|---------|--------|
| `src/internalUsage/modules/csharp-weather.wasm` | `build-csharp-wasm.sh` |
| `src/internalUsage/modules/rust-weather.wasm` | `build-rust-wasm.sh` |
| `artifacts/nuget/*.nupkg` | `dotnet pack` |

## 5. 検証コマンド

```bash
# 1) 検証スクリプト実行（環境非依存）
./build/scripts/tests/test-build-csharp-wasm.sh
./build/scripts/tests/test-nuget-source-mapping.sh
./build/scripts/tests/test-nuget-wasm-config.sh
./build/scripts/tests/test-csproj-ilcompiler.sh

# 2) restore/build（サブモジュール初期化済みの環境で実行）
dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release

# 3) WASM builds
./build/scripts/build-csharp-wasm.sh   # macOS: Docker 必須
./build/scripts/build-rust-wasm.sh     # wasm32-wasip1 target 必須

# 4) test/pack
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build
dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget

# 5) 成果物確認
ls -la src/internalUsage/modules/csharp-weather.wasm
ls -la src/internalUsage/modules/rust-weather.wasm
ls -la artifacts/nuget/*.nupkg
```

## 6. 前提条件

| 環境 | 条件 |
|------|------|
| macOS arm64 | Docker Desktop が必要（C# WASM ビルド用） |
| Linux CI | WASI SDK v29 がインストール済み（`ci.yml` で自動設定） |
| Rust | `wasm32-wasip1` ターゲットが追加済み |
| .NET SDK | `10.0.1xx` feature band（`global.json` により制御） |
| サブモジュール | `external/wasmtime-dotnet`、`submodules/Sekiban` が初期化済み |

## 7. 完了チェックリスト

- [ ] 検証スクリプト 4 本がすべて PASS
- [ ] macOS arm64 で `build-csharp-wasm.sh` 成功（Docker モード）
- [ ] Linux CI で `build-csharp-wasm.sh` 成功（native モード）
- [ ] `dotnet restore src/SekibanWasmRuntime.ci.slnx` で NU1507 が 0 件
- [ ] `dotnet build` / `dotnet test` / `dotnet pack` が成功
- [ ] `build-rust-wasm.sh` 成功
- [ ] `csharp-weather.wasm` と `rust-weather.wasm` が生成
- [ ] `tasks/cswasm/README.md` に GUIDE5 最終運用ルールが記載
