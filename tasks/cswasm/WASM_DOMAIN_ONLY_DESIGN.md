# Design: POC Parity + Domain-From-WASM Architecture (SekibanWasmRuntime)

作成日: 2026-02-12
対象リポジトリ: `J-Tech-Japan/SekibanWasmRuntime`
目的: `/Users/tomohisa/dev/GitHub/SekibanAsAService/src/poc` の構成・体験に寄せつつ、Sekiban と組み合わせた「WASM中心ドメイン」を実現する。

## 0. 問題提起（なぜ `SekibanWasm.Rust.Domain.csproj` があるのか）

現状の `src/internalUsages/rust/SekibanWasm.Rust.Domain` は Rust ではなく .NET プロジェクトで、
`ApiService/Web/Tests` が共有する **型・JSON context・DomainType 列挙** を置くために存在している。

つまり名前に `Rust` が付いているが、実態は「Rustサンプルの周辺を .NET 型で固めるための Domain ライブラリ」であり、
「DomainはWASMが唯一のソース」という目標と衝突する。

この設計書では、`.csproj Domain` をサンプルから排除し、Domain（Schema + 実行）はWASMから供給する方式へ移行する。

## 1. 要件（このタスクの定義）

### 1.1 MUST
- C# / Rust ともに同一の WasmServer（C#）に接続する構成を持つ
- ClientApi は言語ごとにネイティブ実装
  - C# ClientApi: ネイティブ C#（.NET）
  - Rust ClientApi: ネイティブ Rust
- **Sekiban の domain は WASM から行う**
  - Domain schema（command/event/query/projector/tag 等の型情報・名前解決）
  - Domain execution（最低限: projector/query。最終的には command も含める）
- `src/poc` と同様に Aspire AppHost で起動できる

### 1.2 SHOULD
- Web 管理 UI（Blazor）を用意し、ClientApi を切り替え可能にする（POC の web 体験）
- 言語横断の契約（schema）を固定し、生成コード/手書き実装のどちらでも運用できる

### 1.3 NON-GOALS（初期）
- ABI の互換性を崩す大規模刷新を一気にやらない（段階移行）
- 本番性能最適化

## 2. 目標アーキテクチャ（POC Parity）

POCの基本:
- AppHost: 依存(Postgres/Orleans等) + WasmServer + ClientApi(複数言語) + Web
- WasmServer: WASMモジュール実行と永続化（イベントコミット/問い合わせ）
- ClientApi: ドメイン操作の入口（HTTP API）。言語ごとに実装。

本リポジトリ版の到達点:

- WasmServer（C# / .NET）
  - Orleans Silo（または最小のホスト）
  - EventStore / ProjectionStateStore
  - WASM runtime (Wasmtime)
  - Domain manifest 解釈
  - （最終）WASM command execution

- ClientApi（C# / Rust）
  - WasmServer の HTTP API を叩く薄いアダプタ
  - 可能なら OpenAPI/JSON schema により DTO を生成
  - Domain型定義を内製しない（.NET Domain プロジェクトを置かない）

- Web（Blazor）
  - C# ClientApi / Rust ClientApi をサービスディスカバリで切替

## 3. 「DomainはWASM」の具体化

ここが現在の SekibanWasmRuntime 実装（projectionのみWASM）との根本差。

### 3.1 Domain Schema = WASM Manifest
WasmServer は WASM モジュールから manifest を取得し、以下を解決できる必要がある。

- Command types
- Event types
- Query types / ListQuery types
- Projector/Tag projector
- QueryType -> ProjectorName mapping
- Versioning (moduleVersion / domainVersion)

### 3.2 Domain Execution の段階

Phase A（最小）:
- Projector / Query は WASM
- Command は ClientApi がイベント生成（POCに近い）
  - ただし「DomainがWASM」要件とは厳密にはズレるため短期措置

Phase B（目標）:
- Command も WASM
- ClientApi は command JSON を WasmServer に渡すだけ
- WasmServer が command を WASM に渡し、イベントを生成し、commit する

この設計書は Phase B を最終目標としつつ、Phase A -> B の移行ルートを用意する。

## 4. WASM ABI（提案）

現状の C# WASM module / Rust WASM module は「projection向け exports」が中心。
Domain-from-WASM を成立させるため、最低限次を定義する。

### 4.1 Required exports (Phase A)
- `get_manifest_json() -> ptr,len`
  - manifest を JSON として返す
- `create_instance(projectorName) -> handle`
- `apply_event(handle, eventType, eventJson) -> state`
- `execute_query(handle, queryType, queryJson) -> resultJson`
- `execute_list_query(handle, queryType, queryJson) -> resultJson`
- `serialize_state(handle) -> bytes`
- `restore_state(bytes) -> handle`

### 4.2 Required exports (Phase B)
- `execute_command(commandType, commandJson, contextJson?) -> eventBatchJson`
  - 返却は event の配列（type + payload + metadata）
  - サーバ側はこの結果を commit

注意:
- ABI は JSON ベースで開始し、性能必要になれば MessagePack/FlatBuffers 等へ段階移行。

## 5. WasmServer API（HTTP）

POC を踏襲しつつ、Domain-from-WASM の経路を増やす。

### 5.1 Manifest
- `GET /api/manifest`
  - サーバの default module manifest を返す
  - ClientApi / Web が UI 構築や型解決に使える

### 5.2 Commands
Phase A:
- `POST /api/events` (既存の event commit)
- `POST /api/{customerId}/events`

Phase B:
- `POST /api/commands/{commandType}`
- `POST /api/{customerId}/commands/{commandType}`
  - body: JSON
  - WasmServer が `execute_command` -> commit -> result

### 5.3 Queries
- `GET /api/queries/{queryType}?queryParams=...`
- `GET /api/listqueries/{queryType}?queryParams=...`

## 6. 内部実装（Sekibanとの組み合わせ）

POC では `DcbDomainTypes` を WASM に委譲する factory がある。
SekibanWasmRuntime 側でも同等の責務を切り出す。

- `WasmDcbDomainTypesFactory`
  - manifest から `DcbDomainTypes`（または相当）を構築
- `IWasmProjectorHost`
  - module cache + manifest cache + instance lifecycle
- `IWasmEventStore` adapter
  - Sekiban DCB の EventStore に橋渡し

重要:
- internal usage のサンプルが `.csproj Domain` に依存すると、
  Domain-from-WASM ではなく「Domain-from-.NET」になってしまう。
  サンプルからは Domain 型を消し、WASM manifest / JSON に寄せる。

## 7. internalUsages の到達構造（提案）

`src/internalUsages/cs/`
- `SekibanWasm.Cs.AppHost`
- `SekibanWasm.WasmServer` (共通サーバ。cs/rust で共用しても良い)
- `SekibanWasm.Cs.ClientApi` (.NET)
- `SekibanWasm.Cs.Web` (Blazor)
- `SekibanWasm.Cs.WasmDomain` (WASM module project)

`src/internalUsages/rust/`
- `SekibanWasm.Rust.AppHost`
- `SekibanWasm.WasmServer` (共通)
- `rust/clientapi` (Rust binary)
- `SekibanWasm.Rust.Web` (Blazor)
- `rust/wasm-domain` (Rust WASM module)

削除候補:
- `SekibanWasm.*.Domain.csproj`（C# / Rust 両方）
  - これらは「WASMからDomain」成立後は不要。

## 8. 移行プラン（実務）

1. WasmServer を `src/internalUsages/shared/SekibanWasm.WasmServer` として新設（POC の WasmServer を移植）
2. 現行 ApiService は段階的に廃止し、WasmServer + ClientApi を標準にする
3. ClientApi を2本（C# / Rust）作る
4. Web は ClientApi を切替可能にする（service discovery）
5. 最後に Domain.csproj を削除（または `legacy/` に隔離）

## 9. 重要な論点（未決）

- Sekiban DCB の型システムが「.NET 型」を強く前提としている箇所をどこまで WASM 側へ押し出せるか
- `execute_command` ABI を追加した場合のセキュリティ/互換/テスト
- manifest のバージョニングとマルチテナント（customerId ごとの module key）

