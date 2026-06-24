FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# wasmtime-dotnet downloads its native runtime as a .tar.xz and extracts it with
# `xz` during the build (see external/wasmtime-dotnet/src/Wasmtime.csproj). The
# .NET SDK image does not ship xz, so without it the extraction silently produces
# nothing and the build fails with MSB3030 (libwasmtime.so not found).
RUN apt-get update \
    && apt-get install -y --no-install-recommends xz-utils \
    && rm -rf /var/lib/apt/lists/*

COPY . .

# Build wasmtime-dotnet on its own first so its native library is downloaded and
# extracted into obj/. Its DownloadWasmtime target runs
# BeforeTargets="DispatchToInnerBuilds", which only fires for the multi-targeting
# top-level build. `dotnet publish` of the host builds Wasmtime.csproj as a
# transitive ProjectReference for a single TFM, skips DispatchToInnerBuilds, and
# would otherwise fail with MSB3030 because libwasmtime.so was never downloaded.
RUN dotnet build external/wasmtime-dotnet/src/Wasmtime.csproj -c Release

RUN dotnet publish src/runtime/Sekiban.Dcb.WasmRuntime.Host/Sekiban.Dcb.WasmRuntime.Host.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Fail closed if the Wasmtime native asset for THIS build platform is missing or
# is built for the wrong architecture. wasmtime-dotnet's CopyWasmtimeLibrary
# target copies only the build host's libwasmtime.so to the output root (its Copy
# condition keys off the build OSArchitecture), so this assertion runs per Buildx
# platform leg and proves the amd64 and arm64 images each carry their OWN native
# library instead of silently shipping one arch. The ELF e_machine check
# (0x3e = x86-64, 0xb7 = AArch64) guards against an arch mismatch slipping in.
# TARGETARCH is populated by BuildKit for `--platform` builds; fall back to the
# Debian arch for a plain single-platform build.
ARG TARGETARCH
RUN set -eux; \
    arch="${TARGETARCH:-$(dpkg --print-architecture)}"; \
    case "$arch" in \
      amd64) want_machine=3e00 ;; \
      arm64) want_machine=b700 ;; \
      *) echo "unsupported target arch: $arch" >&2; exit 1 ;; \
    esac; \
    lib="/app/publish/libwasmtime.so"; \
    if [ ! -f "$lib" ]; then \
      echo "MISSING Wasmtime native asset for $arch at $lib" >&2; \
      ls -la /app/publish | head -50; \
      exit 1; \
    fi; \
    machine="$(od -An -tx1 -j18 -N2 "$lib" | tr -d ' ')"; \
    if [ "$machine" != "$want_machine" ]; then \
      echo "Wasmtime native asset arch mismatch for $arch: e_machine=$machine want=$want_machine" >&2; \
      exit 1; \
    fi; \
    echo "OK: Wasmtime native asset present and matches $arch (e_machine=$machine) -> $lib"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is used by the container/compose readiness healthcheck against /ready.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Link the published GHCR package back to this repository.
LABEL org.opencontainers.image.source="https://github.com/J-Tech-Japan/SekibanWasmRuntime"
LABEL org.opencontainers.image.description="Sekiban WASM Runtime Host — OSS local runtime container (not Sekiban Cloud)."
LABEL org.opencontainers.image.licenses="Elastic-2.0"

ENV ASPNETCORE_URLS=http://0.0.0.0:8080

RUN mkdir -p /app/config /app/modules

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Sekiban.Dcb.WasmRuntime.Host.dll"]
