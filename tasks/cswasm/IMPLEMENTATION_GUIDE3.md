# SekibanWasmRuntime 改善実装ガイド v3（PR #12 後の未達を完了させる版）

このドキュメントは、`IMPLEMENTATION_GUIDE2.md` 実行後に残った未達（特に C# WASM build の再現性）を解消し、
`SekibanWasmRuntime` を「ローカル/CIで同じ結果が得られる状態」に到達させるための最終実装指示です。

対象リポジトリ: `J-Tech-Japan/SekibanWasmRuntime`
前提: `main` 最新を使用する。

---

## 1. Why（なぜ必要か）

PR #12 により GUIDE2 の多くは達成されたが、次が残っている。

1. macOS arm64 で `build-csharp-wasm.sh` が `NU1101 (runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm)` で失敗する。
2. Central Package Management + 複数 feed で `NU1507` が出続け、依存解決の再現性が弱い。
3. `pack` 時に品質警告（`NU5104`, readme 未同梱）が残る。

この状態は「一部環境でしか完走できない」ため、
ガイドとしては未完了。GUIDE3 で最終到達条件を定義して閉じる。

---

## 2. Goal（目標）

1. C# WASM build を **macOS arm64 / Linux CI の両方で成功**させる。
2. `restore/build/test/pack` の警告を整理し、CIの品質ゲートを明確化する。
3. 「これだけ見れば実装できる」状態として、運用手順を固定する。

---

## 3. Success Criteria（成功条件）

以下をすべて満たしたら完了。

1. `./build/scripts/build-csharp-wasm.sh` が macOS arm64 で成功し、`src/internalUsage/modules/csharp-weather.wasm` を更新する。
2. `./build/scripts/build-csharp-wasm.sh` が Linux CI で成功する（既存CIで確認可能）。
3. `dotnet restore src/SekibanWasmRuntime.ci.slnx` で `NU1507` が出ない。
4. `dotnet build src/SekibanWasmRuntime.ci.slnx -c Release` がエラーなしで成功。
5. `dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build` が green。
6. `dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget` が成功し、
   最低限 `Sekiban.Dcb.WasmRuntime*.nupkg` と `SekibanWasm.Domain*.nupkg` が生成される。
7. `tasks/cswasm/README.md` に GUIDE3 の差分方針（なぜその方式か）を追記。

---

## 4. 作業スコープ（必須）

必ず修正するファイル:

- `build/scripts/build-csharp-wasm.sh`
- `src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj`
- `NuGet.config`
- `tasks/cswasm/README.md`

必要に応じて修正:

- `.github/workflows/ci.yml`
- `Directory.Packages.props`
- `Directory.Build.props`

---

## 5. 実装方針（重要）

### 方針A: C# WASM build は「Linux toolchain」を基準に固定する

`runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm` が不安定なため、
ローカル macOS でも Linux コンテナ経由で publish して成果物のみ取り出す方式に統一する。

- 利点:
  - CI と同一条件で再現できる
  - OS依存パッケージ不足に引きずられない
- 欠点:
  - Docker 必須

GUIDE3 では再現性優先のため、この方式を採用する。

### 方針B: package source mapping を導入して NU1507 を解消する

`NuGet.config` に `<packageSourceMapping>` を追加し、
`dotnet10` / `dotnet-experimental` を必要パッケージだけに限定する。

---

## 6. 具体的コーディング手順

### Step 1: `build-csharp-wasm.sh` を Linux固定ビルド対応にする

修正対象: `build/scripts/build-csharp-wasm.sh`

実装要件:

1. `uname` 判定で Linux 以外の場合は Docker 経由で build。
2. Docker イメージは `mcr.microsoft.com/dotnet/sdk:10.0-preview`（または CI と同じSDK）を使用。
3. コンテナ内で `dotnet publish ... -r wasi-wasm` を実行し、`artifacts/csharp-wasm` に出力。
4. 既存同様に `csharp-weather.wasm` へコピーし、失敗時は `exit 1`。

実装イメージ（要約）:

```bash
if [[ "$(uname -s)" != "Linux" ]]; then
  docker run --rm \
    -v "$ROOT":/work \
    -w /work \
    mcr.microsoft.com/dotnet/sdk:10.0-preview \
    bash -lc "dotnet publish $WASM_PROJ -c Release -r wasi-wasm -o $PUBLISH_DIR"
else
  dotnet publish "$WASM_PROJ" -c Release -r wasi-wasm -o "$PUBLISH_DIR"
fi
```

補足:

- Docker未導入時は明確なエラーメッセージを出す。
- 実行ログの先頭に `host OS`, `build mode (native|docker)` を出す。

### Step 2: `SekibanWasm.Wasm.csproj` の OS依存パッケージ条件を整理

修正対象: `src/internalUsage/SekibanWasm.Wasm/SekibanWasm.Wasm.csproj`

実装要件:

1. Linux基準方式に合わせ、ILCompiler runtime package は Linux を主とする。
2. macOS 固有 package 参照は削除、または opt-in 条件に変更する。
3. `NativeLib`, `IlcExportUnmanagedEntrypoints`, `LinkerArg` は維持。

推奨:

- macOS package 参照をデフォルトで無効化し、必要時だけ `EnableMacIlCompilerRuntime=true` で有効化。

### Step 3: `NuGet.config` に source mapping を導入

修正対象: `NuGet.config`

実装要件:

1. `nuget.org` をデフォルトソースにする。
2. `Microsoft.DotNet.ILCompiler.*` / `runtime.*.microsoft.dotnet.ilcompiler.llvm` のみ `dotnet10` と `dotnet-experimental` を許可。
3. それ以外は `nuget.org` へマップ。

期待効果:

- `NU1507` の解消
- 依存取得先の説明可能性向上

### Step 4: README に GUIDE3 方針を追記

修正対象: `tasks/cswasm/README.md`

必須記載:

1. なぜ Linux固定（Docker fallback）にしたか
2. なぜ source mapping を入れたか
3. ローカル実行時の前提（Docker 必須）

### Step 5: （任意）CI の追加検証

修正対象: `.github/workflows/ci.yml`

必要なら以下を追加:

- C# WASM 生成後に `test -f src/internalUsage/modules/csharp-weather.wasm`
- 生成物サイズが 0 でないことの確認

---

## 7. 検証コマンド（この順で実行）

```bash
cd SekibanWasmRuntime

# 1) restore/build
 dotnet restore src/SekibanWasmRuntime.ci.slnx
 dotnet build src/SekibanWasmRuntime.ci.slnx -c Release

# 2) wasm builds
 ./build/scripts/build-csharp-wasm.sh
 ./build/scripts/build-rust-wasm.sh

# 3) tests
 dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build

# 4) pack
 dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget

# 5) artifacts
 ls -la src/internalUsage/modules
 ls -la artifacts/nuget
```

完了判定で必ず確認:

- `src/internalUsage/modules/csharp-weather.wasm`
- `src/internalUsage/modules/rust-weather.wasm`
- `artifacts/nuget/Sekiban.Dcb.WasmRuntime*.nupkg`

---

## 8. 完了チェックリスト

- [ ] macOS arm64 で `build-csharp-wasm.sh` 成功
- [ ] Linux CI で `build-csharp-wasm.sh` 成功
- [ ] `NU1507` 解消
- [ ] restore/build/test/pack 完走
- [ ] README に GUIDE3 方針追記
- [ ] 生成物確認（csharp/rust wasm + nupkg）

---

## 9. PR テンプレート

タイトル例:

- `fix: stabilize csharp wasm build across macOS and CI (guide3)`

本文テンプレート:

```md
## Summary
- switch C# WASM build to Linux-based reproducible path (Docker fallback on non-Linux)
- harden ILCompiler package conditions for WASM project
- add NuGet package source mapping to remove NU1507 and improve determinism
- document GUIDE3 operational policy in tasks/cswasm/README.md

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
- NU1507 resolved: yes/no
```

---

## 10. Issue テンプレート

タイトル:

- `Guide3 follow-up: complete reproducible C# WASM build on macOS and CI`

本文テンプレート:

```md
## Background
PR #12 achieved most GUIDE2 items, but C# WASM build is still not reproducible on macOS arm64.

## Goal
Close remaining gaps defined in IMPLEMENTATION_GUIDE3.md.

## Tasks
- [ ] make build-csharp-wasm.sh Linux-toolchain reproducible (Docker fallback on non-Linux)
- [ ] refine SekibanWasm.Wasm.csproj ILCompiler package conditions
- [ ] add NuGet packageSourceMapping and remove NU1507
- [ ] document GUIDE3 policy in tasks/cswasm/README.md
- [ ] validate restore/build/wasm/test/pack end-to-end

## Done Criteria
All Success Criteria in tasks/cswasm/IMPLEMENTATION_GUIDE3.md are satisfied.
```
