# Generic WasmServer Contract Design (C# + Rust ClientApi)

## 1. Purpose
`internalUsages` の WasmServer をドメイン非依存の汎用実行面に統一し、
ClientApi 側でドメイン API との変換を担当する。

最終トポロジ:
- C#: `[Web] -> [C# ClientApi] -> [WasmServer]`
- Rust: `[Web/UI] -> [Rust ClientApi] -> [WasmServer]`

## 2. Problem Statement (Current)
現状の WasmServer は以下が混在している。
1. 汎用 contract API (`/api/sekiban/serialized/*`, `/v1/instances/*`)
2. Weather 固定 API (`/api/weatherforecast*`)

この状態だと、WasmServer の責務が「実行基盤」ではなく「特定ドメイン API」まで拡張され、
C#/Rust ClientApi 分離の価値が下がる。

## 3. Target Architecture
### 3.1 WasmServer responsibilities (generic only)
WasmServer は以下だけを公開する。
1. 汎用イベント保存: `POST /api/sekiban/serialized/commit`
2. 汎用タグステート取得: `POST /api/sekiban/serialized/tag-state`
3. 汎用プロジェクション操作: `/v1/instances/*`
4. 汎用コマンド実行: `POST /api/sekiban/serialized/command/execute` (new)

### 3.2 ClientApi responsibilities (domain-facing)
ClientApi は以下を担当する。
1. ドメイン API 入力を受け取る (`/api/weatherforecast*` など)
2. 実行モード選択 (local / remote)
3. WasmServer 汎用 contract へ変換して転送
4. レスポンスをドメイン API 形式へ再整形

## 4. Required Generic Contract

## 4.1 Existing contracts (must keep)
- `POST /api/sekiban/serialized/tag-state`
- `POST /api/sekiban/serialized/commit`
- `/v1/instances/*`

## 4.2 New command execution contract (must add)
Endpoint:
- `POST /api/sekiban/serialized/command/execute`

Request JSON:
```json
{
  "commandName": "CreateWeatherForecast",
  "commandJson": "{\"location\":\"Tokyo\",\"temperatureC\":22}",
  "consistencyTags": [
    {
      "tag": "weatherforecast-aggregate:weatherforecast-001",
      "lastSortableUniqueId": "000000000000000001"
    }
  ],
  "options": {
    "dryRun": false,
    "waitForSortableUniqueId": null
  }
}
```

Response JSON:
```json
{
  "eventCandidates": [
    {
      "eventPayloadName": "WeatherForecastCreated",
      "payloadBase64": "eyJmb3JlY2FzdElkIjoiLi4uIn0=",
      "tags": ["weatherforecast", "weatherforecast-aggregate:weatherforecast-001"]
    }
  ],
  "consistencyTags": [
    {
      "tag": "weatherforecast-aggregate:weatherforecast-001",
      "lastSortableUniqueId": "000000000000000001"
    }
  ],
  "commandResultJson": "{\"forecastId\":\"weatherforecast-001\"}"
}
```

Notes:
- `payloadBase64` は UTF-8 JSON bytes を base64 化したもの。
- WasmServer の `execute` は commit しない。commit は `serialized/commit` に分離。
- これにより local/remote の I/O 契約を統一できる。

## 5. Runtime Behavior Rules
1. Local command:
- ClientApi が TagState を取得し、クライアント側で command 実行。
- 得た eventCandidates を `serialized/commit` に送る。

2. Remote command:
- ClientApi が `serialized/command/execute` を呼ぶ。
- 返却 eventCandidates を `serialized/commit` へ送る。

3. Query:
- ClientApi は `/v1/instances/*` を使い、query/list-query/snapshot を汎用実行。

4. TagState:
- ClientApi は `serialized/tag-state` を直接使用。

## 6. File-level Refactoring Plan

### 6.1 WasmServer (C# and Rust folders)
Remove:
- `CommandEndpoints.cs` の Weather 固定 endpoint。

Keep:
- `InstanceEndpoints.cs`
- `serialized/tag-state`, `serialized/commit`

Add:
- `SerializedCommandEndpoints.cs` (new generic endpoint)
- Generic DTO files for command execute request/response

### 6.2 C# ClientApi
Add:
- command execute adapter (local/remote switch)
- domain -> generic contract mapper

Move:
- existing `/api/weatherforecast*` routes entirely to ClientApi

### 6.3 Rust ClientApi
Add:
- same generic contract mapper as C# (JSON compatible)
- local/remote mode switch
- endpoint response shape parity with C# ClientApi

## 7. Compatibility and Migration
1. Step 1: add new generic endpoint without deleting existing weather routes.
2. Step 2: migrate C# ClientApi and Rust ClientApi to new endpoint.
3. Step 3: remove weather routes from WasmServer.

No direct Web -> WasmServer call is allowed after migration.

## 8. Acceptance Criteria
1. WasmServer has no weather/domain-specific route.
2. WasmServer exposes only generic APIs (serialized + instances + generic command execute).
3. C# and Rust ClientApi both can execute local and remote command paths.
4. C# and Rust remote path use same JSON contract.
5. E2E passes for create/update/delete/query/tag-state.

## 9. Risks
1. Contract drift between C# and Rust ClientApi.
- Mitigation: shared contract tests with golden JSON.

2. Payload encoding mismatch (raw JSON vs base64).
- Mitigation: strict tests for payload bytes.

3. Dual-write behavior during migration.
- Mitigation: explicit phase gate and temporary compatibility window.

## 10. Deliverables
1. This design document
2. `IMPLEMENTATION_TASKS.md` with concrete checklist
3. `ISSUE_BODY.md` for tracking in GitHub issue
