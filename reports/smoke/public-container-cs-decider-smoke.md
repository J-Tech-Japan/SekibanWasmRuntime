# Public Container CS Decider Smoke (SWR-G036)

- Result: **PASS**
- Detail: Committed WeatherForecastCreated (tag=weather:sample-20260811021835-30090); read it back via tag-latest-sortable; saw it in GetWeatherForecastListQuery; and confirmed the WeatherForecast materialized view caught it up in DcbMaterializedViewPostgres (table=sekiban_mv_weatherforecast_v1_weather_forecast location=Kyoto) — all through ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3.
- Runtime image: `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`
- Runtime URL: `http://localhost:60469`
- Commit: `eb95d2f209d2bf6bd4894ffe8ce5ab678464a4dd`

## Last HTTP response body

```
{"writtenEvents":[{"payload":"eyJmb3JlY2FzdElkIjoic2FtcGxlLTIwMjYwODExMDIxODM1LTMwMDkwIiwibG9jYXRpb24iOiJLeW90byIsInRlbXBlcmF0dXJlQyI6MjQsInN1bW1hcnkiOiJTYW1wbGUiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTExVDA5OjE4OjM1WiJ9","sortableUniqueIdValue":"063922036715812611000355163469","id":"019ff01d-d924-7745-8a2d-75b1f3123d56","eventMetadata":{"causationId":"019ff01d-d924-7745-8a2d-75b1f3123d56","correlationId":"SerializedCommit","executedUser":"SerializedSekibanExecutor"},"tags":["weather:sample-20260811021835-30090"],"eventPayloadName":"WeatherForecastCreated"}],"tagWriteResults":[{"tag":"weather:sample-20260811021835-30090","version":1,"writtenAt":"2026-08-11T09:18:35.8441033+00:00"}],"duration":"00:00:00.0712427"}
```
