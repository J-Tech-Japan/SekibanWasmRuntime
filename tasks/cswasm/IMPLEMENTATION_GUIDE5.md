# SekibanWasmRuntime 改善実装ガイド v5（split 前に先に片付ける問題解決版）

対象: `J-Tech-Japan/SekibanWasmRuntime`
前提: `main` 最新

## 1. Why
`split-cs-rust.md` に進む前に、基盤の再現性問題を先に閉じる。

未達:
1. macOS arm64 で `build-csharp-wasm.sh` が Docker 内 SDK 不整合で失敗
2. `dotnet restore src/SekibanWasmRuntime.ci.slnx` で `NU1507` が継続

この2点を解消しないと、分離タスク実施中の失敗原因が構成変更由来か既存不具合由来か判別できない。

## 2. Goal
1. macOS arm64 / Linux CI で C# WASM build を同じ手順で成功させる
2. 通常 restore から `NU1507` を完全に除去する
3. split-cs-rust 着手前の安定ベースラインを確立する

## 3. Success Criteria
1. `./build/scripts/build-csharp-wasm.sh` が macOS arm64 で成功
2. CI の C# WASM step が green
3. `dotnet restore src/SekibanWasmRuntime.ci.slnx` で `NU1507` が 0 件
4. `dotnet build/test/pack` が連続実行で成功
5. `tasks/cswasm/README.md` に運用ルール（通常NuGetとWASM専用NuGet分離）を明記

## 4. 必須修正ファイル
- `build/scripts/build-csharp-wasm.sh`
- `NuGet.config`
- `NuGet.wasm.config`（新規）
- `tasks/cswasm/README.md`
- `.github/workflows/ci.yml`（必要に応じて、WASM publish時config指定）

## 5. 実装手順

### Step 1: Docker SDK の固定を `global.json` に合わせる

`build/scripts/build-csharp-wasm.sh` の Docker image を `10.0-preview` から stable に変更する。

- 変更前: `mcr.microsoft.com/dotnet/sdk:10.0-preview`
- 変更後: `mcr.microsoft.com/dotnet/sdk:10.0`

さらにコンテナ内で SDK 存在チェックを入れる:

```bash
dotnet --list-sdks | grep -q '^10.0.100' || {
  echo "[build-csharp-wasm] ERROR: .NET SDK 10.0.100 not found in container" >&2
  exit 1
}
```

### Step 2: NuGet 設定を「通常」と「WASM専用」に分離

#### 2.1 `NuGet.config`（通常用）
- `packageSources` は `nuget.org` のみ
- `dotnet10` / `dotnet-experimental` は入れない

#### 2.2 `NuGet.wasm.config`（新規）
- `nuget.org` + `dotnet10` + `dotnet-experimental` を定義
- `packageSourceMapping` で ILCompiler 系のみ dotnet feed を許可

例:

```xml
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="*" />
  </packageSource>
  <packageSource key="dotnet10">
    <package pattern="Microsoft.DotNet.ILCompiler.*" />
    <package pattern="runtime.*.microsoft.dotnet.ilcompiler.*" />
  </packageSource>
  <packageSource key="dotnet-experimental">
    <package pattern="Microsoft.DotNet.ILCompiler.*" />
    <package pattern="runtime.*.microsoft.dotnet.ilcompiler.*" />
  </packageSource>
</packageSourceMapping>
```

### Step 3: C# WASM publish だけ `NuGet.wasm.config` を使う

`build/scripts/build-csharp-wasm.sh` の publish コマンドに以下を追加:

```bash
--configfile "$ROOT/NuGet.wasm.config"
```

Docker 内 publish でも同様に `--configfile /work/NuGet.wasm.config` を指定する。

### Step 4: README に運用ルールを追記

`tasks/cswasm/README.md` に以下を明記:
- 通常の `restore/build/test/pack` は `NuGet.config`（nuget.org only）
- C# WASM publish は `NuGet.wasm.config` を使用
- 理由: `NU1507` 回避と ILCompiler feed の局所化

### Step 5: CI の確認

既存 CI の C# WASM build step が `build-csharp-wasm.sh` 経由で動くため、
スクリプト変更で自動的に新運用へ乗ることを確認。

## 6. 検証コマンド

```bash
cd SekibanWasmRuntime

# 1) 通常 restore/build（NU1507確認）
dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release

# 2) wasm
./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh

# 3) test/pack
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build
dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget

# 4) artifacts
ls -la src/internalUsage/modules/csharp-weather.wasm
ls -la src/internalUsage/modules/rust-weather.wasm
```

## 7. 完了チェック
- [ ] macOS arm64 で C# WASM build 成功
- [ ] CI C# WASM step 成功
- [ ] `NU1507` 0件
- [ ] build/test/pack 完走
- [ ] README に運用ルール追記

## 8. 実施後の次フェーズ
この GUIDE5 が完了したら `tasks/cswasm/split-cs-rust.md` に着手する。
