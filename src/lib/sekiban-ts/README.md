# @sekiban/ts

TypeScript host SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime).

`@sekiban/ts` is a thin client for the Sekiban DCB runtime host: it lets a
Node.js application define typed commands, read tag state, and execute
serialized queries against a running Sekiban WASM Runtime container
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`) over its HTTP contract.
Projection logic itself runs inside the runtime as a WASM module (for
AssemblyScript projectors, see
[`@sekiban/as-wasm`](https://www.npmjs.com/package/@sekiban/as-wasm)).

## Install

```bash
npm install @sekiban/ts
```

Requires Node.js 20 or later (uses the built-in `fetch`).

## Usage

```ts
import {
  SekibanRuntimeClient,
  newCommandOutput,
  isEmptyJSON,
  AlreadyExistsError,
  type Command,
  type CommandContext,
  type CommandOutput,
} from "@sekiban/ts";

// Map tag groups to the projector declared in the runtime manifest.
const client = new SekibanRuntimeClient("http://localhost:8080", {
  WeatherForecast: "WeatherForecastProjector",
});

// A command reads consistency state through the context, then emits events.
class CreateWeatherForecast implements Command {
  constructor(private forecastId: string, private location: string) {}

  commandType(): string {
    return "CreateWeatherForecast";
  }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const state = await ctx.getTagState("WeatherForecast", this.forecastId);
    if (!isEmptyJSON(state.stateJson)) {
      throw new AlreadyExistsError(`forecast ${this.forecastId} already exists`);
    }
    return newCommandOutput(
      "WeatherForecastCreated",
      { forecastId: this.forecastId, location: this.location },
      [`WeatherForecast:${this.forecastId}`],
      [`WeatherForecast:${this.forecastId}`],
      {},
    );
  }
}

// Commit events, read state, and query.
await client.finalizeCommand(new CreateWeatherForecast(id, "Kyoto"));
const state = await client.getTagState("WeatherForecast", id);
const items = await client.executeListQuery(
  "GetWeatherForecastListQuery",
  JSON.stringify({ pageSize: 10, pageNumber: 1 }),
);
```

## API surface

- `SekibanRuntimeClient` — `getTagState`, `executeQuery`, `executeListQuery`,
  and `finalizeCommand` against the serialized runtime endpoints.
- `Command`, `CommandContext`, `CommandOutput`, `EventOutput`,
  `TagStateResponse`, `CountResult` — the typed command/query contract.
- `newCommandOutput`, `tagString`, `isEmptyJSON` — helpers for writing commands.
- `AlreadyExistsError`, `NotFoundError`, `ValidationError` — canonical
  command validation errors.

## Compatibility

`@sekiban/ts` 0.1.x targets the Sekiban WASM Runtime host image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and speaks the
same serialized HTTP contract as the Rust `sekiban-executor` 0.1.0 crate.

## License

[Elastic License 2.0](./LICENSE)
