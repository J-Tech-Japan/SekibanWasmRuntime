# SekibanWasmRuntime 改善実装ガイド v2（これだけ見れば進める版）

このドキュメントは、PR #8 実装レビュー（`reviews/03`）で見つかった未達点を解消し、
`SekibanWasmRuntime` を「ガイド通りに本当に再現可能な状態」に仕上げるための改善指示書です。

対象リポジトリ: `J-Tech-Japan/SekibanWasmRuntime`  
前提: `main` 最新を使う。  
このガイド以外を読まなくても実装完了できるように、手順・判定基準・PR/Issue運用まで含める。

---

## 1. Why（なぜこの改善が必要か）

現状（PR #8後）には次のギャップがある。

1. `build/scripts/build-csharp-wasm.sh` が失敗し、WASM成果物が生成できない。
2. CI で WASM build ステップを実行しておらず、上記失敗を自動検知できない。
3. `global.json` / `Directory.Packages.props` がなく、環境再現性が弱い。
4. `BuildServiceProvider()` 使用により ASP0000 警告が残る（DI安全性問題）。

この状態では「実装できた」ではなく「部分的に動く」に留まるため、
再現性と検証可能性を満たすために改善が必要。

---

## 2. Goal（目標）

この改善の目標は以下の 4 点。

1. C# WASM / Rust WASM のビルドをローカルで成功させる。
2. CI で WASM build 失敗を確実に落とせるようにする。
3. SDK/依存バージョンの再現性を固定する。
4. runtime 切替のDI構成を警告なく安全にする。

---

## 3. Success Criteria（成功条件）

次をすべて満たしたら完了。

1. `./build/scripts/build-csharp-wasm.sh` が成功し、`src/internalUsage/modules/csharp-weather.wasm` が生成される。
2. `./build/scripts/build-rust-wasm.sh` が成功し、`src/internalUsage/modules/rust-weather.wasm` が生成される。
3. CI が `build-csharp-wasm.sh` / `build-rust-wasm.sh` を実行する。
4. `dotnet build src/SekibanWasmRuntime.ci.slnx -c Release` で `ASP0000` 警告が消える。
5. `dotnet test src/SekibanWasmRuntime.ci.slnx -c Release` が green。
6. `dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget` が成功。

---

## 4. 作業スコープ（必須）

必ず修正するファイル:

- `src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj`
- `build/scripts/build-csharp-wasm.sh`
- `.github/workflows/ci.yml`
- `src/internalUsage/SekibanWasm.ApiService/Program.cs`
- `global.json`（新規）
- `Directory.Packages.props`（新規）
- `tasks/cswasm/README.md`（差分理由の明記）

必要に応じて修正:
- `Directory.Build.props`（SDK固定や共通設定を持たせる場合）

---

## 5. 実装手順（順番厳守）

### Step 1: 再現（先に失敗を確定）

```bash
cd SekibanWasmRuntime
dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
./build/scripts/build-csharp-wasm.sh
```

期待: ここでは現状失敗する（IL1034 系）。
この失敗ログを PR の検証欄に添付する。

### Step 2: C# WASM build を通す

修正対象: `src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj`

実施内容:
1. `publish` 時にエントリポイント必須にならない設定へ調整する。
2. `NativeLib` / `IlcExportUnmanagedEntrypoints` / `LinkerArg` の整合を取り、
   export 関数を残したまま `.wasm` 出力できることを確認する。
3. `PublishTrimmed` が原因で落ちる場合は、WASMライブラリ出力向けに無効化または適切な trim 設定へ変更する。

確認:

```bash
./build/scripts/build-csharp-wasm.sh
ls -la src/internalUsage/modules/csharp-weather.wasm
```

### Step 3: build script の堅牢化

修正対象: `build/scripts/build-csharp-wasm.sh`

実施内容:
1. 失敗時に「何を確認すべきか」を出力（例: csproj の publish 関連プロパティ）。
2. 出力 `.wasm` が複数ある場合の選択を明示（ファイル名固定推奨）。
3. 失敗時は必ず `exit 1`。

### Step 4: CI で WASM build を実行

修正対象: `.github/workflows/ci.yml`

`build` と `test` の間に以下を追加:

```yaml
      - name: Build C# WASM module
        run: ./build/scripts/build-csharp-wasm.sh

      - name: Build Rust WASM module
        run: ./build/scripts/build-rust-wasm.sh
```

必要なら Rust toolchain を追加:

```yaml
      - uses: dtolnay/rust-toolchain@stable
      - run: rustup target add wasm32-wasip1
```

### Step 5: `BuildServiceProvider()` の除去

修正対象: `src/internalUsage/SekibanWasm.ApiService/Program.cs`

現状課題:
- `builder.Services.BuildServiceProvider()` で runtime 解決しており `ASP0000`。

実施内容:
1. `BuildServiceProvider()` を使わずに runtime resolver を組み立てる。
2. `IProjectionRuntime` の最終登録を factory 経由にして、
   依存は `sp.GetRequiredService<...>()` で解決する。

許容パターン:
- `AddSingleton<IProjectorRuntimeResolver>(sp => ...)`
- `AddSingleton<IProjectionRuntime>(sp => new CompositeProjectionRuntime(...))`

確認:

```bash
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
```

期待: `ASP0000` が出ない。

### Step 6: 再現性ファイルの追加

#### 6.1 `global.json` 追加

リポジトリ直下に追加例:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

#### 6.2 `Directory.Packages.props` 追加

リポジトリ直下に追加し、主要 package のバージョンを中央管理。

注意:
- 以前の `0.0.0` は使わない。
- 実際に restore/build が通る具体バージョンを入れる。

### Step 7: ガイドとの差分を明記

修正対象: `tasks/cswasm/README.md`

追記すべき内容:
- なぜ `DcbRuntime.WasmOnly.ApiService` 等の別プロジェクトではなく、
  `SekibanWasm.ApiService` の runtime 切替方式を採用したか。
- ガイド原案との差分と、その運用上の理由。

---

## 6. 検証コマンド（この順で実行）

```bash
cd SekibanWasmRuntime

# 1) 基本
dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release

# 2) WASM build
./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh

# 3) テスト
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build

# 4) pack
dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget

# 5) 生成物確認
ls -la src/internalUsage/modules
ls -la artifacts/nuget
```

---

## 7. 完了チェックリスト

- [ ] C# WASM build が成功
- [ ] Rust WASM build が成功
- [ ] CI が2つのWASM build scriptを実行
- [ ] `ASP0000` 警告を除去
- [ ] `global.json` を追加
- [ ] `Directory.Packages.props` を追加
- [ ] 差分理由を `tasks/cswasm/README.md` に記載
- [ ] restore/build/test/pack がすべて成功

---

## 8. PR 作成テンプレート

タイトル例:
- `fix: make cs wasm build reproducible and CI-enforced`

本文テンプレート:

```md
## Summary
- fix C# WASM publish failure (IL1034 path)
- run wasm build scripts in CI
- remove BuildServiceProvider() usage causing ASP0000
- add global.json and Directory.Packages.props for reproducibility
- document guide-vs-implementation differences

## Validation
- dotnet restore src/SekibanWasmRuntime.ci.slnx
- dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
- ./build/scripts/build-csharp-wasm.sh
- ./build/scripts/build-rust-wasm.sh
- dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build
- dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget

## Result
- csharp-weather.wasm generated: yes/no
- rust-weather.wasm generated: yes/no
- ASP0000 warning gone: yes/no
```

---

## 9. Issue 作成テンプレート

タイトル:
- `Follow-up: close remaining gaps after PR #8 against IMPLEMENTATION_GUIDE`

本文:

```md
## Background
PR #8 implemented most of guide v1, but review found reproducibility and build-validation gaps.

## Tasks
- [ ] Fix C# WASM publish failure in `SekibanWasm.Wasm.csproj`
- [ ] Make `build-csharp-wasm.sh` robust and diagnostic
- [ ] Execute wasm build scripts in CI
- [ ] Remove `BuildServiceProvider()` from ApiService runtime wiring
- [ ] Add `global.json`
- [ ] Add `Directory.Packages.props` with real versions
- [ ] Document divergence from guide in `tasks/cswasm/README.md`

## Done Criteria
- [ ] restore/build/test/pack green
- [ ] csharp-weather.wasm and rust-weather.wasm generated
- [ ] CI includes wasm build checks
```

---

## 10. 最終判定ルール

以下のどれか1つでも満たせない場合は「未完了」。

1. C# WASM build script が失敗する
2. CI が wasm build scripts を実行しない
3. `ASP0000` が残る
4. pack が失敗する

すべて満たした場合のみ「完了」。
