# SekibanWasmRuntime 改善実装ガイド v4（PR #15 未達の最終修正版）

対象: `J-Tech-Japan/SekibanWasmRuntime`

## 1. 目的
PR #15 後に残った未達を完了する。

- macOS arm64 で `build-csharp-wasm.sh` が通らない
- `dotnet restore` で `NU1507` が出る

## 2. 成功条件
1. macOS arm64 で `./build/scripts/build-csharp-wasm.sh` 成功
2. Linux CI でも同じスクリプトが成功
3. `dotnet restore src/SekibanWasmRuntime.ci.slnx` で `NU1507` が 0 件
4. `build/test/pack` が完走

## 3. 修正対象ファイル
- `build/scripts/build-csharp-wasm.sh`
- `NuGet.config`
- `tasks/cswasm/README.md`

## 4. 実装手順

### Step 1: Docker SDK と global.json を整合させる
現状失敗原因は、Docker 内 SDK が preview で、`global.json` の `10.0.100` と不一致なこと。

`build/scripts/build-csharp-wasm.sh` の Docker 部分を次の方針に変更する:

- `mcr.microsoft.com/dotnet/sdk:10.0` を使う（preview ではなく安定タグ）
- 念のためコンテナ内で `dotnet --info` を出力
- もし `10.0.100` が入っていない場合に明示エラーで落とす

実装例（要点）:

```bash
DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"

docker run --rm -v "$ROOT":/work -w /work "$DOTNET_IMAGE" bash -c '
  set -euo pipefail
  dotnet --info
  dotnet --list-sdks | grep -q "^10.0.100" || {
    echo "ERROR: .NET SDK 10.0.100 not found in container" >&2
    exit 1
  }
  # wasi sdk install
  # dotnet publish ...
'
```

### Step 2: NU1507 を消すため source mapping を厳密化
`NuGet.config` で `nuget.org` に `*` を許可しているため、複数ソース状態が解消されない。

方針:

- `nuget.org` は通常パッケージのみ
- `dotnet10`/`dotnet-experimental` は ILCompiler 系だけ
- 必要なら CI/ローカルで同一 `NuGet.config` を明示指定して restore

実装例（考え方）:

- `nuget.org` の `*` は維持せず、最小限パターンを定義
- もしくは `dotnet` feed を WASM build 時だけ使うように分離

推奨実装（確実性優先）:

1. `NuGet.config` は `nuget.org` のみを残す
2. C# WASM build だけ別 `NuGet.wasm.config`（ILCompiler feed あり）を使う
3. `build-csharp-wasm.sh` の publish で `--configfile NuGet.wasm.config` を指定

これで通常 restore（CI solution）から `NU1507` を切り離せる。

### Step 3: README に運用ルールを追記
`tasks/cswasm/README.md` に次を追記:

- 通常 restore/build/test/pack は `NuGet.config`（nuget.org only）
- C# WASM publish は `NuGet.wasm.config` を使う理由
- Docker image 固定理由（SDK 再現性）

## 5. 検証コマンド

```bash
cd SekibanWasmRuntime

dotnet restore src/SekibanWasmRuntime.ci.slnx
dotnet build src/SekibanWasmRuntime.ci.slnx -c Release
./build/scripts/build-csharp-wasm.sh
./build/scripts/build-rust-wasm.sh
dotnet test src/SekibanWasmRuntime.ci.slnx -c Release --no-build
dotnet pack src/SekibanWasmRuntime.ci.slnx -c Release -o artifacts/nuget
```

追加確認:

```bash
ls -la src/internalUsage/modules/csharp-weather.wasm
ls -la src/internalUsage/modules/rust-weather.wasm
```

## 6. 完了チェック
- [ ] macOS arm64 で C# WASM build 成功
- [ ] Linux CI で C# WASM build 成功
- [ ] `NU1507` 0 件
- [ ] build/test/pack 完走
