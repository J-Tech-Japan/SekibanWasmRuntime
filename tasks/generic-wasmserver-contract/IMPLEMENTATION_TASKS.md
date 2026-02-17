# Implementation Tasks: Generic WasmServer + ClientApi Split

## 0. Preflight
- [ ] `main` 最新を取り込み
- [ ] `submodules/Sekiban` を `origin/main` に同期
- [ ] `dotnet --info` / `cargo --version` を記録

## 1. Contract Freeze
- [ ] `serialized/command/execute` の request/response DTO を追加
- [ ] JSON field 名を固定 (`camelCase`)
- [ ] `payloadBase64` を仕様化
- [ ] エラー形式を固定: `{ "error": "..." }`

Deliverables:
- [ ] DTO files in `src/lib/...`
- [ ] contract markdown snippet update

## 2. WasmServer Genericization
- [ ] C# WasmServer: Weather 固定 route を削除
- [ ] Rust WasmServer: Weather 固定 route を削除
- [ ] C#/Rust WasmServer に `SerializedCommandEndpoints` 追加
- [ ] `Program.cs` から generic endpoint mapping のみ呼ぶ

Deliverables:
- [ ] no `/api/weatherforecast*` in WasmServer code

## 3. C# ClientApi Migration
- [ ] `/api/weatherforecast*` を C# ClientApi に集約
- [ ] local mode: tag-state fetch -> local execute -> commit
- [ ] remote mode: command execute -> commit
- [ ] query/list-query/tag-state は generic endpoint を使う

Deliverables:
- [ ] ClientApi unit tests (mode switch + mapping)

## 4. Rust ClientApi Migration
- [ ] Rust ClientApi に C# と同じ mode switch を実装
- [ ] remote mode で `serialized/command/execute` + `serialized/commit`
- [ ] query/list-query/tag-state を generic endpoint に統一
- [ ] C# と完全に同じ JSON shape を保証

Deliverables:
- [ ] Rust integration tests (happy path + validation failure)

## 5. AppHost/Docs
- [ ] AppHost wiring を `web -> clientapi -> wasmserver` に固定
- [ ] README から WasmServer 直結説明を除去
- [ ] 実行手順を C#/Rust で明示

## 6. Tests (Required)
- [ ] Contract test: `serialized/command/execute` request/response schema
- [ ] Contract test: `payloadBase64` decode/encode parity
- [ ] Integration: create/update/delete/query/tag-state (C# path)
- [ ] Integration: create/update/delete/query/tag-state (Rust path)
- [ ] Regression: stale sortable id / duplicate consistency tag failures

## 7. Completion Gate
- [ ] WasmServer に domain route が 0 件
- [ ] ClientApi からのみ domain API を公開
- [ ] CI green
- [ ] docs only で再現可能

## 8. Suggested PR split
1. PR-A: contract + generic endpoint
2. PR-B: C# ClientApi migration
3. PR-C: Rust ClientApi migration
4. PR-D: cleanup + docs + tests
