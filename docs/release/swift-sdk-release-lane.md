# Swift SDK Release Lane (SWR-G074)

The Swift SDK is a repository-root SwiftPM package. Its implementation remains
under `src/wasm-projectors/swift`, while the root `Package.swift` exposes the
same two products: `SekibanWasm` and `SekibanMv`.

SwiftPM resolves Git dependencies from the repository root, so consumers use
`https://github.com/J-Tech-Japan/SekibanWasmRuntime` directly. The Swift SDK
shares this repository's bare `v*` release version line with the NuGet packages
and runtime image; SDK-to-runtime compatibility is therefore established by a
single release version rather than a separate lane to maintain.

## Package Shape

| Surface | Name |
| --- | --- |
| Package name | `sekiban-swift` |
| Repository | `github.com/J-Tech-Japan/SekibanWasmRuntime` |
| Products | `SekibanWasm`, `SekibanMv` |
| Sources | `src/wasm-projectors/swift/Sources` |
| Tests | `src/wasm-projectors/swift/Tests` |

Consumers retain the existing product names:

```swift
.package(name: "sekiban-swift", url: "https://github.com/J-Tech-Japan/SekibanWasmRuntime", exact: "1.0.0-preview.4")
```

## Version and verification boundary

Tags `v1.0.0-preview.1` through `v1.0.0-preview.3` predate the root manifest
and are not SwiftPM-resolvable. The first Swift-consumable version is
`v1.0.0-preview.4`.

Before that tag exists, verify consumers with the local-package mode, which
stages the current repository tree as a temporary local Git repository tagged
`v1.0.0-preview.4` and redirects resolution without modifying the committed
manifest:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/smoke.sh --local-package
```

After the operator cuts `v1.0.0-preview.4`, rerun the same sample without the
flag so it resolves the remote exact-version dependency. Recording that result
is an explicit post-tag follow-up; this change does not create a tag, Release,
or published package.

## Validation

```bash
swift build --package-path .
swift test --package-path .
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/verify-no-local-sekiban-paths.sh
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/linux-build-check.sh
```

The Linux check mounts the repository root in a Swift container and runs the
same package build and bounded test flow. The public consumer sample continues
to exercise command execution, tag-state readback, in-memory projection query,
and materialized-view catch-up against the public runtime image.
