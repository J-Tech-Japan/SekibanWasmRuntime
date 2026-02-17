## Background
`internalUsages` で Client API と WasmServer の責務が混在しているため、C# と Rust の両系統で責務分離を行う。

Source of truth:
- `tasks/client-wasmserver-split/CLIENT_WASMSERVER_SPLIT_DESIGN.md`

## Goal
- C#系: `[C# Web] -> [C# Client API] -> [C# WasmServer]`
- Rust系: `[Web/UI] -> [Rust Client API] -> [C# WasmServer]`

両系統で local/remote command を同じ serialized contract で実行可能にする。

## Scope
### In scope
- Client API と WasmServer の責務分離
- C# Client API / Rust Client API の実装
- 共通 serialized contract の適用
- AppHost 配線更新
- E2E + docs 更新

### Out of scope
- Sekiban public API の破壊的変更
- Orleans grain 設計の大幅変更

## Tasks
### Phase 1: Contract freeze
- [ ] Client API <-> WasmServer endpoint/DTO を固定
- [ ] エラーコード/失敗レスポンス形式を固定

### Phase 2: C# split
- [ ] C# Client API を WasmServer から責務分離
- [ ] C# Web の呼び先を Client API 経由に統一

### Phase 3: Rust split
- [ ] Rust Client API プロジェクトを追加
- [ ] AppHost に `rust-clientapi` service を追加
- [ ] Rust path を `clientapi -> apiservice` に統一

### Phase 4: local/remote parity
- [ ] C# path local/remote 両方を確認
- [ ] Rust path local/remote 両方を確認
- [ ] 同一 contract で疎通できることを確認

### Phase 5: tests/docs
- [ ] E2E: create/update/delete/query
- [ ] 実行手順を internalUsages README に追記
- [ ] 既知制約/未実装範囲を明記

## Acceptance Criteria
- [ ] C# と Rust の両方で Client API と WasmServer が分離されている
- [ ] local/remote command 切替が双方で動作
- [ ] serialized contract が共通利用される
- [ ] E2E が通る
