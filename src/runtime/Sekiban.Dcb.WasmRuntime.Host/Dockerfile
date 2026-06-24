FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
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
