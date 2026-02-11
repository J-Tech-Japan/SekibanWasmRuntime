# SekibanWasmRuntime 実装ガイド（この1ファイルだけで実装する版）

このガイドは、`SekibanWasmRuntime` リポジトリで **ゼロから実装して動作確認まで完了** するための実行手順です。  
`SekibanAsAService` の中身は参照しない前提で書いています。

対象:
- C# Native runtime
- WASM in-proc runtime (Wasmtime)
- WASM remote runtime (HTTP runner)
- Orleans 統合
- internalUsage サンプル（WasmOnly / Hybrid / Remote）
- test / CI / pack

---

## Why（なぜこれをやるか）

現状の課題は次の 3 点です。

1. Runtime 抽象はあるが、WASM 実行を実運用へ接続する実装・手順が分散して再現性が低い。  
2. Native 依存（C# 実装依存）を段階的に減らし、projector 単位で実行基盤を差し替えられる状態にしたい。  
3. リポジトリ外の参照や暗黙知がないと実装が進めにくく、引き継ぎコストが高い。  

このガイドの目的は、上記を解消し、**単一ドキュメントだけで実装・検証・PR作成まで完了できる状態**を作ることです。

---

## Goal（何を目標にするか）

この取り組みの目標は、以下の 4 つです。

1. **実装目標**: Sekiban の runtime interface に準拠した Native / WASM(in-proc) / WASM(remote) を提供する。  
2. **運用目標**: Orleans 環境で projector 単位に Native と WASM を混在運用できる。  
3. **検証目標**: internalUsage で WasmOnly / Hybrid / Remote の3形態を再現し、E2E テストで動作保証する。  
4. **配布目標**: `src/lib/*` を NuGet pack 可能にし、外部利用できる成果物へ落とし込む。  

---

## Success Criteria（成功条件）

次をすべて満たしたら成功とみなします。

1. **再現性**: 新規環境でこのガイド通りに実行し、ビルドからテストまで再現できる。  
2. **機能性**: WasmOnly / Hybrid / Remote の各サンプルで event -> query -> snapshot restore が通る。  
3. **同値性**: 同一イベント列で Native と WASM の query 結果が一致する。  
4. **品質**: CI が green で、主要エラーパス（runner down / projector未登録）をテストで検出できる。  
5. **配布可能性**: `dotnet pack` 成功し、NuGet 配布可能な `.nupkg` が生成される。  

判定に使う具体コマンドは本ドキュメントの「受け入れチェック」を基準とします。

---

## 0. まず決めること（固定）

### 0.1 ブランチ
- `feature/runtime-pr1-foundation`
- `feature/runtime-pr2-wasm-wasmtime`
- `feature/runtime-pr3-remote-runner`
- `feature/runtime-pr4-internalusage-test-ci`

### 0.2 必須ツール
- .NET SDK 9.0.x
- Rust stable + `wasm32-wasip1` target
- `wasm-tools`
- `jq`（テストスクリプトで使用）
- GitHub CLI（PR作成する場合）

### 0.3 ディレクトリ規約（固定）

```text
SekibanWasmRuntime/
  src/
    lib/
    internalUsage/
  build/scripts/
  .github/workflows/
```

---

## 1. スキャフォールド（そのまま実行）

> 以下は `SekibanWasmRuntime` リポジトリ直下で実行。

```bash
mkdir -p src/lib src/internalUsage build/scripts .github/workflows

dotnet new sln -n SekibanWasmRuntime

# ライブラリ群
dotnet new classlib -n Sekiban.Dcb.Runtime.Abstractions -o src/lib/Sekiban.Dcb.Runtime.Abstractions
dotnet new classlib -n Sekiban.Dcb.Runtime.Native -o src/lib/Sekiban.Dcb.Runtime.Native
dotnet new classlib -n Sekiban.Dcb.Runtime.Wasm -o src/lib/Sekiban.Dcb.Runtime.Wasm
dotnet new classlib -n Sekiban.Dcb.Runtime.Wasm.Wasmtime -o src/lib/Sekiban.Dcb.Runtime.Wasm.Wasmtime
dotnet new classlib -n Sekiban.Dcb.Runtime.Wasm.Remote -o src/lib/Sekiban.Dcb.Runtime.Wasm.Remote
dotnet new classlib -n Sekiban.Dcb.Runtime.Orleans -o src/lib/Sekiban.Dcb.Runtime.Orleans

# テスト
dotnet new xunit -n Sekiban.Dcb.Runtime.Orleans.Tests -o src/lib/Sekiban.Dcb.Runtime.Orleans.Tests

# internalUsage
dotnet new webapi -n DcbRuntime.WasmOnly.ApiService -o src/internalUsage/DcbRuntime.WasmOnly.ApiService
dotnet new webapi -n DcbRuntime.WasmHybrid.ApiService -o src/internalUsage/DcbRuntime.WasmHybrid.ApiService
dotnet new webapi -n DcbRuntime.WasmRemote.ApiService -o src/internalUsage/DcbRuntime.WasmRemote.ApiService
dotnet new webapi -n DcbRuntime.WasmRunner -o src/internalUsage/DcbRuntime.WasmRunner
dotnet new classlib -n SampleDomain -o src/internalUsage/SampleDomain

# ソリューションへ追加
dotnet sln SekibanWasmRuntime.sln add \
  src/lib/Sekiban.Dcb.Runtime.Abstractions/Sekiban.Dcb.Runtime.Abstractions.csproj \
  src/lib/Sekiban.Dcb.Runtime.Native/Sekiban.Dcb.Runtime.Native.csproj \
  src/lib/Sekiban.Dcb.Runtime.Wasm/Sekiban.Dcb.Runtime.Wasm.csproj \
  src/lib/Sekiban.Dcb.Runtime.Wasm.Wasmtime/Sekiban.Dcb.Runtime.Wasm.Wasmtime.csproj \
  src/lib/Sekiban.Dcb.Runtime.Wasm.Remote/Sekiban.Dcb.Runtime.Wasm.Remote.csproj \
  src/lib/Sekiban.Dcb.Runtime.Orleans/Sekiban.Dcb.Runtime.Orleans.csproj \
  src/lib/Sekiban.Dcb.Runtime.Orleans.Tests/Sekiban.Dcb.Runtime.Orleans.Tests.csproj \
  src/internalUsage/DcbRuntime.WasmOnly.ApiService/DcbRuntime.WasmOnly.ApiService.csproj \
  src/internalUsage/DcbRuntime.WasmHybrid.ApiService/DcbRuntime.WasmHybrid.ApiService.csproj \
  src/internalUsage/DcbRuntime.WasmRemote.ApiService/DcbRuntime.WasmRemote.ApiService.csproj \
  src/internalUsage/DcbRuntime.WasmRunner/DcbRuntime.WasmRunner.csproj \
  src/internalUsage/SampleDomain/SampleDomain.csproj
```

---

## 2. 依存管理ファイル（そのまま作成）

### 2.1 `global.json`

```json
{
  "sdk": {
    "version": "9.0.100",
    "rollForward": "latestFeature"
  }
}
```

### 2.2 `Directory.Packages.props`

> バージョンは release 時点で調整。ここでは例。

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Sekiban.Dcb.Core" Version="0.0.0" />
    <PackageVersion Include="Sekiban.Dcb.Core.Model" Version="0.0.0" />
    <PackageVersion Include="Sekiban.Dcb.Orleans.Core" Version="0.0.0" />
    <PackageVersion Include="ResultBoxes" Version="4.0.0" />
    <PackageVersion Include="Wasmtime" Version="29.0.0" />
    <PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Orleans.TestingHost" Version="9.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

### 2.3 `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

## 3. プロジェクト参照（必須）

```bash
# Native/Wasm 共通
dotnet add src/lib/Sekiban.Dcb.Runtime.Native/Sekiban.Dcb.Runtime.Native.csproj reference src/lib/Sekiban.Dcb.Runtime.Abstractions/Sekiban.Dcb.Runtime.Abstractions.csproj
dotnet add src/lib/Sekiban.Dcb.Runtime.Wasm/Sekiban.Dcb.Runtime.Wasm.csproj reference src/lib/Sekiban.Dcb.Runtime.Abstractions/Sekiban.Dcb.Runtime.Abstractions.csproj

# Wasmtime/Remote
dotnet add src/lib/Sekiban.Dcb.Runtime.Wasm.Wasmtime/Sekiban.Dcb.Runtime.Wasm.Wasmtime.csproj reference src/lib/Sekiban.Dcb.Runtime.Wasm/Sekiban.Dcb.Runtime.Wasm.csproj
dotnet add src/lib/Sekiban.Dcb.Runtime.Wasm.Remote/Sekiban.Dcb.Runtime.Wasm.Remote.csproj reference src/lib/Sekiban.Dcb.Runtime.Wasm/Sekiban.Dcb.Runtime.Wasm.csproj

# Orleans
dotnet add src/lib/Sekiban.Dcb.Runtime.Orleans/Sekiban.Dcb.Runtime.Orleans.csproj reference \
  src/lib/Sekiban.Dcb.Runtime.Abstractions/Sekiban.Dcb.Runtime.Abstractions.csproj \
  src/lib/Sekiban.Dcb.Runtime.Native/Sekiban.Dcb.Runtime.Native.csproj \
  src/lib/Sekiban.Dcb.Runtime.Wasm/Sekiban.Dcb.Runtime.Wasm.csproj

# Tests
dotnet add src/lib/Sekiban.Dcb.Runtime.Orleans.Tests/Sekiban.Dcb.Runtime.Orleans.Tests.csproj reference \
  src/lib/Sekiban.Dcb.Runtime.Orleans/Sekiban.Dcb.Runtime.Orleans.csproj \
  src/internalUsage/SampleDomain/SampleDomain.csproj
```

---

## 4. 実装する型一覧（ファイル名固定）

以下を「そのままのファイル名」で作る。

### 4.1 `src/lib/Sekiban.Dcb.Runtime.Abstractions`
- `IProjectionRuntime.cs`
- `IProjectionState.cs`
- `IEventRuntime.cs`
- `ITagProjectionRuntime.cs`
- `ITagProjector.cs`
- `IProjectorRuntimeResolver.cs`

### 4.2 `src/lib/Sekiban.Dcb.Runtime.Native`
- `NativeProjectionRuntime.cs`
- `NativeProjectionState.cs`
- `NativeEventRuntime.cs`
- `NativeTagProjectionRuntime.cs`
- `NativeTagProjector.cs`
- `ProjectorRuntimeResolver.cs`
- `CompositeProjectionRuntime.cs`

### 4.3 `src/lib/Sekiban.Dcb.Runtime.Wasm`
- `WasmProjectionRuntime.cs`
- `WasmProjectionState.cs`
- `WasmStateSnapshot.cs`
- `WasmProjectorRegistry.cs`
- `WasmModuleRef.cs`
- `IPrimitiveProjectionHost.cs`
- `IPrimitiveProjectionInstance.cs`

### 4.4 `src/lib/Sekiban.Dcb.Runtime.Wasm.Wasmtime`
- `WasmtimeRuntime.cs`
- `WasmtimeHostOptions.cs`
- `WasmtimeModuleCache.cs`
- `WasmtimePrimitiveProjectionHost.cs`
- `WasmtimePrimitiveProjectionInstance.cs`
- `WasmtimeServiceCollectionExtensions.cs`

### 4.5 `src/lib/Sekiban.Dcb.Runtime.Wasm.Remote`
- `RemoteRunnerOptions.cs`
- `RemotePrimitiveProjectionHost.cs`
- `RemotePrimitiveProjectionInstance.cs`
- `CompositePrimitiveProjectionHost.cs`

---

## 5. 重要インターフェースの最小仕様

### 5.1 `IPrimitiveProjectionInstance`

```csharp
public interface IPrimitiveProjectionInstance : IDisposable
{
    void ApplyEvent(string eventType, string eventPayloadJson, IReadOnlyList<string> tags, string? sortableUniqueId);
    string ExecuteQuery(string queryType, string queryParamsJson);
    string ExecuteListQuery(string queryType, string queryParamsJson);
    string SerializeState();
    void RestoreState(string stateJson);
}
```

### 5.2 `IPrimitiveProjectionHost`

```csharp
public interface IPrimitiveProjectionHost
{
    IPrimitiveProjectionInstance CreateInstance(string projectorName);
}
```

---

## 6. WASM ABI 契約（厳守）

WASM側 export 必須:
- `alloc(int len) -> int`
- `dealloc(int ptr, int len)` または `free`
- `create_instance(int ptr, int len) -> int`
- `apply_event(int instanceId, int eventTypePtr, int eventTypeLen, int payloadPtr, int payloadLen)`
- `execute_query(...) -> long`（上位32bit ptr / 下位32bit len）
- `execute_list_query(...) -> long`
- `serialize_state(int instanceId) -> long`
- `restore_state(int instanceId, int ptr, int len)`

任意:
- `apply_events_batch`（なければ host 側 fallback）

失敗時ルール:
- `create_instance` は失敗時 `-1`
- query は `{"error":"..."}` JSON 返却でも可

---

## 7. C# WASM モジュール（実装テンプレート）

`src/internalUsage/SampleDomain.WasmCSharp` を作る。

```bash
dotnet new classlib -n SampleDomain.WasmCSharp -o src/internalUsage/SampleDomain.WasmCSharp
dotnet sln SekibanWasmRuntime.sln add src/internalUsage/SampleDomain.WasmCSharp/SampleDomain.WasmCSharp.csproj
```

`SampleDomain.WasmCSharp.csproj` の要点:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
    <OutputType>Library</OutputType>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <LinkerArg Include="-Wl,--export,alloc" />
    <LinkerArg Include="-Wl,--export,dealloc" />
    <LinkerArg Include="-Wl,--export,create_instance" />
    <LinkerArg Include="-Wl,--export,apply_event" />
    <LinkerArg Include="-Wl,--export,execute_query" />
    <LinkerArg Include="-Wl,--export,execute_list_query" />
    <LinkerArg Include="-Wl,--export,serialize_state" />
    <LinkerArg Include="-Wl,--export,restore_state" />
  </ItemGroup>
</Project>
```

`WasmExports.cs` の実装方針:
- `Dictionary<int, WeatherState>` を static 管理
- `[UnmanagedCallersOnly(EntryPoint = "...")]` を各関数へ付与
- 文字列は UTF-8 で marshal
- `SerializeState` は `JsonSerializer.Serialize(state)`

ビルド:

```bash
dotnet publish src/internalUsage/SampleDomain.WasmCSharp/SampleDomain.WasmCSharp.csproj -c Release -o ./artifacts/csharp-wasm
cp ./artifacts/csharp-wasm/*.wasm ./src/internalUsage/modules/csharp-weather.wasm
```

---

## 8. Rust WASM モジュール（実装テンプレート）

```bash
cargo new --lib src/internalUsage/sample_domain_wasm_rust
rustup target add wasm32-wasip1
```

`Cargo.toml`:

```toml
[lib]
crate-type = ["cdylib"]

[dependencies]
serde = { version = "1", features = ["derive"] }
serde_json = "1"
once_cell = "1"
```

`build/scripts/build-rust-wasm.sh` 例:

```bash
#!/usr/bin/env bash
set -euo pipefail
cargo build --manifest-path src/internalUsage/sample_domain_wasm_rust/Cargo.toml --target wasm32-wasip1 --release
cp target/wasm32-wasip1/release/sample_domain_wasm_rust.wasm src/internalUsage/modules/rust-weather.wasm
```

---

## 9. Runner HTTP API 契約（JSONまで固定）

### 9.1 `POST /v1/instances`

Request:

```json
{
  "projectorName": "WeatherForecastMultiProjection"
}
```

Response 200:

```json
{
  "instanceId": "a95ef9f8-0ed6-4488-b8e9-8a4a9d7c2d9f"
}
```

### 9.2 `POST /v1/instances/{id}/events`

```json
{
  "events": [
    {
      "eventType": "WeatherForecastCreated",
      "payloadJson": "{\"forecastId\":\"f1\",\"location\":\"Tokyo\"}",
      "tags": ["weather:f1"],
      "sortableUniqueId": "20260211..."
    }
  ]
}
```

### 9.3 `POST /v1/instances/{id}/query`

```json
{
  "queryType": "GetWeatherForecastQuery",
  "queryParamsJson": "{\"forecastId\":\"f1\"}"
}
```

Response:

```json
{
  "resultJson": "{\"forecastId\":\"f1\",\"location\":\"Tokyo\"}"
}
```

### 9.4 Snapshot
- `GET /v1/instances/{id}/snapshot` -> `{"stateJson":"..."}`
- `PUT /v1/instances/{id}/snapshot` body `{"stateJson":"..."}`

---

## 10. internalUsage 各 `Program.cs` の登録例

### 10.1 WasmOnly

```csharp
builder.Services.AddSingleton(domainTypes);
builder.Services.AddWasmtimeProjectionHost(opt =>
{
    opt.ProjectorModulePaths["WeatherForecastMultiProjection"] = "src/internalUsage/modules/csharp-weather.wasm";
});
builder.Services.AddSingleton<WasmProjectorRegistry>(sp =>
{
    var reg = new WasmProjectorRegistry();
    reg.Register(new WasmModuleRef("WeatherForecastMultiProjection", "src/internalUsage/modules/csharp-weather.wasm", "v1", "c-abi-v1"));
    reg.MapQueryToProjector("SampleDomain.Queries.GetWeatherForecastQuery", "WeatherForecastMultiProjection");
    return reg;
});
builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
builder.Services.AddSingleton<IEventRuntime, NativeEventRuntime>();
```

### 10.2 Hybrid

```csharp
builder.Services.AddSingleton<NativeProjectionRuntime>();
builder.Services.AddSingleton<WasmProjectionRuntime>();

builder.Services.AddSingleton<IProjectorRuntimeResolver>(sp =>
    new ProjectorRuntimeResolver(
        defaultRuntime: sp.GetRequiredService<NativeProjectionRuntime>(),
        runtimeMap: new Dictionary<string, IProjectionRuntime>
        {
            ["WeatherForecastMultiProjection"] = sp.GetRequiredService<WasmProjectionRuntime>()
        }));

builder.Services.AddSingleton<IProjectionRuntime, CompositeProjectionRuntime>();
```

### 10.3 Remote

```csharp
builder.Services.Configure<RemoteRunnerOptions>(builder.Configuration.GetSection("RemoteRunner"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IPrimitiveProjectionHost, RemotePrimitiveProjectionHost>();
builder.Services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
```

`appsettings.json`:

```json
{
  "RemoteRunner": {
    "BaseUrl": "http://localhost:5107"
  }
}
```

---

## 11. テスト実装（ファイル名固定）

`src/lib/Sekiban.Dcb.Runtime.Orleans.Tests` に以下作成:

- `WasmProjectionRuntimeTests.cs`
- `HybridRuntimeRoutingTests.cs`
- `RemoteRuntimeErrorTests.cs`
- `SnapshotRoundTripTests.cs`

### 11.1 `WasmProjectionRuntimeTests` の最小ケース
- in-proc host を使って event 1件適用
- query 結果 JSON を deserialize して assert

### 11.2 `HybridRuntimeRoutingTests`
- `ProjectorRuntimeResolver.Resolve("WeatherForecastMultiProjection")` が wasm runtime を返す
- 未登録 projector は native runtime を返す

### 11.3 `RemoteRuntimeErrorTests`
- 無効 URL runner を指定
- `ApplyEvent` または `ExecuteQueryAsync` が error result を返す

### 11.4 `SnapshotRoundTripTests`
- state serialize
- 新規 instance へ restore
- 同じ query 結果

---

## 12. スクリプト（そのまま作成）

### 12.1 `build/scripts/build-csharp-wasm.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
dotnet publish src/internalUsage/SampleDomain.WasmCSharp/SampleDomain.WasmCSharp.csproj -c Release -o artifacts/csharp-wasm
mkdir -p src/internalUsage/modules
cp artifacts/csharp-wasm/*.wasm src/internalUsage/modules/csharp-weather.wasm
echo "built: src/internalUsage/modules/csharp-weather.wasm"
```

### 12.2 `build/scripts/build-rust-wasm.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
cargo build --manifest-path src/internalUsage/sample_domain_wasm_rust/Cargo.toml --target wasm32-wasip1 --release
mkdir -p src/internalUsage/modules
cp target/wasm32-wasip1/release/sample_domain_wasm_rust.wasm src/internalUsage/modules/rust-weather.wasm
echo "built: src/internalUsage/modules/rust-weather.wasm"
```

### 12.3 `build/scripts/run-e2e.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh

dotnet test SekibanWasmRuntime.sln -c Release
```

実行権限:

```bash
chmod +x build/scripts/*.sh
```

---

## 13. CI（最小 YAML）

`.github/workflows/ci.yml`:

```yaml
name: ci
on:
  pull_request:
  push:
    branches: [main]

jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - uses: dtolnay/rust-toolchain@stable
      - run: rustup target add wasm32-wasip1
      - run: cargo install wasm-tools
      - run: dotnet restore SekibanWasmRuntime.sln
      - run: dotnet build SekibanWasmRuntime.sln -c Release --no-restore
      - run: ./build/scripts/build-csharp-wasm.sh
      - run: ./build/scripts/build-rust-wasm.sh
      - run: dotnet test SekibanWasmRuntime.sln -c Release --no-build
```

---

## 14. パッケージ化

各 `src/lib/*/*.csproj` に以下を入れる:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  <PackageId>Sekiban.Dcb.Runtime.Wasm</PackageId>
  <Description>Sekiban DCB WASM runtime</Description>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

pack:

```bash
dotnet pack SekibanWasmRuntime.sln -c Release -o artifacts/nuget
```

---

## 15. 受け入れチェック（コピペ）

```bash
dotnet restore SekibanWasmRuntime.sln
dotnet build SekibanWasmRuntime.sln -c Release
./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh
dotnet test SekibanWasmRuntime.sln -c Release
dotnet pack SekibanWasmRuntime.sln -c Release -o artifacts/nuget
```

期待:
- すべて exit code 0
- `src/internalUsage/modules/csharp-weather.wasm` が存在
- `src/internalUsage/modules/rust-weather.wasm` が存在
- `artifacts/nuget/*.nupkg` が生成

---

## 16. トラブルシュート

### 16.1 `WASM module does not export create_instance`
- C# 側: `UnmanagedCallersOnly(EntryPoint = "create_instance")` を確認
- LinkerArg export を確認

### 16.2 `Projector not found`
- `WasmProjectorRegistry.Register` と `MapQueryToProjector` を確認

### 16.3 `Query type not found`
- Query の `FullName` と map key を一致させる

### 16.4 runner timeout
- `RemoteRunnerOptions.BaseUrl` と runner 起動ポート確認
- `HttpClient.Timeout` を一時的に増やす

### 16.5 snapshot restore 後に結果不一致
- `WasmStateSnapshot` へ `SafeVersion/UnsafeVersion/LastSortableUniqueId/LastEventId` を保持しているか確認

---

## 17. PR 作成テンプレート

```text
## Summary
- implement <phase>
- add <projects/files>

## Compatibility
- requires Sekiban packages >= <version>

## Validation
- dotnet build ...
- dotnet test ...
- wasm module build scripts ...

## Scope
- [x] Native
- [x] Wasm in-proc
- [ ] Wasm remote
```

---

## 18. 最終条件

このガイド通りに実装して次を満たせば完了:

1. Hybrid サンプルで Native projector と WASM projector が同時に動く
2. 同じイベント列で Native と WASM のクエリ結果が一致する
3. snapshot 復元後も同じ結果
4. CI green
5. NuGet pack 成功

以上。
