# Swift sample E2E smokes

Playwright scripts for the Sekiban Swift sample (`src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift`).
Run them after starting the AppHost via:

```bash
dotnet run --project src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift/SekibanDcbDecider.AppHost/SekibanDcbDecider.AppHost.csproj -c Release
```

Requires a one-time `npm install playwright` + `npx playwright install chromium`
(the scripts assume chromium is already provisioned).

| Script | Verifies |
|---|---|
| [`phase6-page-smoke.mjs`](phase6-page-smoke.mjs) | Every Blazor page (`http://127.0.0.1:6380/…`) and every Next.js page (`http://127.0.0.1:6381/…`) returns HTTP 200 with zero page-errors and zero non-HMR failed requests. |

Seed the MV registry with the helper script before running the smoke so the MV +
in-memory projection pages have data to render:

```bash
bash build/scripts/seed-swift-mv.sh
```
