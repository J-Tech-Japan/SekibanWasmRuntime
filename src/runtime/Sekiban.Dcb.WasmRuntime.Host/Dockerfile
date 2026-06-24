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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Link the published GHCR package back to this repository.
LABEL org.opencontainers.image.source="https://github.com/J-Tech-Japan/SekibanWasmRuntime"
LABEL org.opencontainers.image.description="Sekiban WASM Runtime Host — OSS local runtime container (not Sekiban Cloud)."
LABEL org.opencontainers.image.licenses="Elastic-2.0"

ENV ASPNETCORE_URLS=http://0.0.0.0:8080

RUN mkdir -p /app/config /app/modules

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Sekiban.Dcb.WasmRuntime.Host.dll"]
