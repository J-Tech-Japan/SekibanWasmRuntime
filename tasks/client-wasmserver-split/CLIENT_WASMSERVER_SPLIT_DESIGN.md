# Client API / WasmServer Split Design (C# + Rust)

## 1. Goal
SekibanWasmRuntime で、C# と Rust の両系統において、責務を以下のように明確分離する。

- Client API: command/query を受け、実行モデル(local/remote)を選択
- WasmServer (C# ApiService): Sekiban runtime + Orleans + EventStore + WASM 実行ホスト

到達したい最終トポロジ:
- C#系: `[C# Web] -> [C# Client API] -> [C# WasmServer]`
- Rust系: `[C# Web or Rust UI] -> [Rust Client API] -> [C# WasmServer]`

## 2. Background
現状は internalUsages 配下で「rust」という名前でも C# 実装が混在し、Client API と WasmServer の境界が曖昧。
この曖昧さが以下の問題を生む。

1. 実装責務とフォルダ名が一致しない
2. local/remote command の実行差分がコード上で追いにくい
3. C#・Rust 両方で同じ契約を使う保証が弱い

## 3. Scope
### In scope
1. internalUsages で Client API と WasmServer を明確に分離
2. C# Client API と Rust Client API の両方を成立
3. 両者が同一の serialized contract を使う
4. AppHost の配線を `web -> clientapi -> apiservice` に統一

### Out of scope
1. Sekiban public API の破壊的変更
2. Orleans Grain の新規設計変更
3. production hardening（auth/rate limit等）の全面導入

## 4. Architecture Rules
### Rule A: WasmServer は C# に固定
- WasmServer は `internalUsages/*/*.ApiService` で C# 実装
- Sekiban/Orleans/EventStore へのアクセス責務を集中

### Rule B: Client API は C# と Rust で別実装
- C#系は C# ネイティブ Client API
- Rust系は Rust ネイティブ Client API
- ただし wire contract は共通（JSON + serialized DTO）

### Rule C: Contract は serialized boundary を正とする
- `GetSerializableTagState`
- `CommitSerializableEvents`
- local/remote 切替時も contract は同一

## 5. Execution Model
### 5.1 Local command path
1. Client API が TagState を取得（WasmServer endpoint）
2. Client API 側で command 実行
3. serialized events を WasmServer に commit

### 5.2 Remote command path
1. Client API が command 実行要求を WasmServer に転送
2. WasmServer 側で WASM command 実行
3. WasmServer 側で commit

## 6. InternalUsages Target Layout
```text
src/internalUsages/
  cs/
    SekibanWasm.Cs.ApiService      # WasmServer
    SekibanWasm.Cs.ClientApi       # Client API (new or clarified)
    SekibanWasm.Cs.Web
    SekibanWasm.Cs.AppHost
  rust/
    SekibanWasm.Rust.ApiService    # WasmServer (C#)
    sekiban_wasm_rust_clientapi    # Rust Client API (new)
    SekibanWasm.Rust.Web
    SekibanWasm.Rust.AppHost
```

## 7. Implementation Phases
### Phase 1: Contract freeze
- Client API <-> WasmServer の endpoint/DTO を固定
- エラーコードと失敗時フォーマットを固定

### Phase 2: C# path separation
- C# Client API を WasmServer から責務分離
- Web から Client API 経由に切替

### Phase 3: Rust path separation
- Rust Client API 実装追加
- AppHost で `rust-clientapi` を独立 service として配線

### Phase 4: Local/Remote parity
- C#系/Rust系双方で local/remote を選択可能にする
- 同じ期待結果（イベント数、sortableUniqueId、TagState）を確認

### Phase 5: E2E + docs
- E2E を `web -> clientapi -> apiservice` 経路に統一
- 実行手順を internalUsages README に反映

## 8. Acceptance Criteria
1. C# 系と Rust 系の両方で Client API が独立プロセスまたは独立責務として成立
2. WasmServer は command/query の入口責務を持たず、runtime 実行責務に集中
3. local/remote command の切替が双方で動く
4. 共通 serialized contract で C#/Rust 両方が疎通する
5. E2E で少なくとも create/update/delete/query を通過

## 9. Risks and Mitigation
1. 責務分離途中で endpoint が二重化する
- 対策: 旧 endpoint を deprecated と明示し、段階的に除去

2. C# と Rust で DTO の解釈差分が出る
- 対策: JSON schema 相当の契約テストを追加

3. AppHost 配線の循環依存
- 対策: service graph を固定化し、起動時検証テストを追加

## 10. Deliverables
1. この設計ファイル
2. 実装タスク Issue（チェックリスト付き）
3. 実装PR（C# path / Rust path / docs+tests を分割推奨）
