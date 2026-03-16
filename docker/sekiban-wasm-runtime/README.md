# Sekiban WASM Runtime Container

`docker/sekiban-wasm-runtime` is the smallest local runtime stack for Sekiban WASM:

- `runtime`: generic ASP.NET host for serialized Sekiban WASM APIs
- `postgres`: event store for the runtime
- `dbgate`: browser UI for inspecting PostgreSQL

## Quick Start

1. Put your Weather sample module at `docker/sekiban-wasm-runtime/modules/weather.wasm`.
2. Start the stack:

```bash
cd docker/sekiban-wasm-runtime
docker compose up --build
```

3. Open the runtime:

- API root: `http://localhost:3000/`
- Health: `http://localhost:3000/health`
- DBGate: `http://localhost:3001/`
- PostgreSQL: `localhost:5432`
- Serialized tag state: `POST http://localhost:3000/api/sekiban/serialized/tag-state`
- Serialized commit: `POST http://localhost:3000/api/sekiban/serialized/commit`
- Serialized query: `POST http://localhost:3000/api/sekiban/serialized/query`
- Serialized list query: `POST http://localhost:3000/api/sekiban/serialized/list-query`

## Manifest

`config/sekiban-manifest.json` is a runtime manifest for projector registration and query routing.

The committed manifest is ready for the Weather sample:

- `WeatherForecastProjector`
- `WeatherForecastMultiProjection`
- `GetWeatherForecastCountQuery`
- `GetWeatherForecastListQuery`
- `WeatherForecastCreated`
- `WeatherForecastLocationUpdated`
- `WeatherForecastDeleted`

If another project uses different projector or query names, edit `config/sekiban-manifest.json`.

## Notes

- The current runtime can infer a default Weather manifest when `SEKIBAN_MANIFEST_PATH` is missing, but a manifest is still the explicit and recommended way to describe projectors and query mappings.
- PostgreSQL is exposed on `localhost:5432` with `postgres/postgres` and database `sekiban`.
- DBGate is exposed on `localhost:3001` and is preconfigured with the `weather` PostgreSQL connection.
