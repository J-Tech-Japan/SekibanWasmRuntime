# Wasmtime Preview Package Inspection

Inspection date: 2026-06-16

Package: `Sekiban.Dcb.WasmRuntime.Wasmtime`

Version: `1.0.0-preview.1`

## Purpose

Record the package inspection evidence for the initial Wasmtime preview package
before any NuGet publish workflow exists. The Wasmtime package remains in the
public preview matrix, but it must not be represented as stable because its
native/runtime dependency shape still needs platform-specific release
validation.

## Commands

```bash
dotnet pack src/lib/Sekiban.Dcb.WasmRuntime.Wasmtime/Sekiban.Dcb.WasmRuntime.Wasmtime.csproj -c Release -o /tmp/sekiban-wasm-pack-119 --nologo
unzip -l /tmp/sekiban-wasm-pack-119/Sekiban.Dcb.WasmRuntime.Wasmtime.1.0.0-preview.1.nupkg
unzip -p /tmp/sekiban-wasm-pack-119/Sekiban.Dcb.WasmRuntime.Wasmtime.1.0.0-preview.1.nupkg Sekiban.Dcb.WasmRuntime.Wasmtime.nuspec
```

## Observed Package Contents

The macOS package candidate contained these relevant entries:

```text
lib/net10.0/Sekiban.Dcb.WasmRuntime.Wasmtime.dll
content/libwasmtime.dylib
contentFiles/any/net10.0/libwasmtime.dylib
README.md
LICENSE
Sekiban.Dcb.WasmRuntime.Wasmtime.nuspec
```

The package size was dominated by the native Wasmtime library. In this macOS
inspection, `libwasmtime.dylib` appeared twice: once under `content/` and once
under `contentFiles/any/net10.0/`.

## Observed Nuspec Metadata

The generated nuspec included:

- package id `Sekiban.Dcb.WasmRuntime.Wasmtime`
- version `1.0.0-preview.1`
- license file `LICENSE`
- readme `README.md`
- repository URL `https://github.com/J-Tech-Japan/SekibanWasmRuntime`
- tags including `wasmtime`, `host`, and `preview`
- dependency `Sekiban.Dcb.WasmRuntime` version `1.0.0-preview.1`
- dependency `Wasmtime` version `35.0.0-dev` with `Compile`, `Build`, and
  `Analyzers` excluded
- content file entry `any/net10.0/libwasmtime.dylib`

## Caveat

This inspection was produced on macOS, so it confirms only the macOS native
asset shape. Linux and Windows release candidates must be packed and inspected
on their respective release build environments before publish so the expected
native asset is present (`libwasmtime.so` on Linux, `wasmtime.dll` on Windows).

The `Wasmtime 35.0.0-dev` dependency and native content layout are preview
release caveats. They should remain visible in package-facing documentation
until the Wasmtime host policy is finalized.
