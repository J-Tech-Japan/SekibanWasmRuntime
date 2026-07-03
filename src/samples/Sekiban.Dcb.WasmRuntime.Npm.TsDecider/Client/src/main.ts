// Typed TypeScript smoke client, mirroring the crates.io Rust decider sample's
// Client/src/main.rs (src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider):
// creates a forecast, updates its location, reads tag state, polls the
// in-memory list query, runs the count query, and prints JSON smoke evidence.
import { randomUUID } from "node:crypto";
import {
  SekibanRuntimeClient,
  newCommandOutput,
  tagString,
  AlreadyExistsError,
  NotFoundError,
  type Command,
  type CommandContext,
  type CommandOutput,
} from "@sekiban/ts";

const TAG_GROUP = "weather";
const PROJECTOR_TAG = "WeatherForecastProjector";
const EVENT_CREATED = "WeatherForecastCreated";
const EVENT_LOCATION_UPDATED = "WeatherForecastLocationUpdated";

interface WeatherForecastState {
  forecastId?: string;
  location?: string;
  temperatureC?: number;
  summary?: string;
  createdAt?: string;
}

interface WrittenEvent {
  id: string;
  sortableUniqueIdValue: string;
}

interface CommitResponse {
  writtenEvents: WrittenEvent[];
}

function parseState(stateJson: string): WeatherForecastState {
  try {
    return JSON.parse(stateJson) as WeatherForecastState;
  } catch {
    return {};
  }
}

function isEmptyState(state: WeatherForecastState): boolean {
  return !state.forecastId || state.forecastId.length === 0;
}

class CreateWeatherForecast implements Command {
  constructor(
    private readonly forecastId: string,
    private readonly location: string,
    private readonly temperatureC: number,
    private readonly summary: string,
  ) {}

  commandType(): string {
    return "CreateWeatherForecast";
  }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TAG_GROUP, this.forecastId);
    const resp = await ctx.getTagState(TAG_GROUP, this.forecastId);
    if (!isEmptyState(parseState(resp.stateJson))) {
      throw new AlreadyExistsError(`weather forecast ${this.forecastId}`);
    }
    return newCommandOutput(
      EVENT_CREATED,
      {
        forecastId: this.forecastId,
        location: this.location,
        temperatureC: this.temperatureC,
        summary: this.summary,
        createdAt: new Date().toISOString(),
      },
      [tag],
      [tag],
      { [tag]: resp.version },
    );
  }
}

class UpdateWeatherForecastLocation implements Command {
  constructor(
    private readonly forecastId: string,
    private readonly newLocation: string,
  ) {}

  commandType(): string {
    return "UpdateWeatherForecastLocation";
  }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TAG_GROUP, this.forecastId);
    const resp = await ctx.getTagState(TAG_GROUP, this.forecastId);
    if (isEmptyState(parseState(resp.stateJson))) {
      throw new NotFoundError(`weather forecast ${this.forecastId}`);
    }
    return newCommandOutput(
      EVENT_LOCATION_UPDATED,
      {
        forecastId: this.forecastId,
        newLocation: this.newLocation,
        updatedAt: new Date().toISOString(),
      },
      [tag],
      [tag],
      { [tag]: resp.version },
    );
  }
}

interface SmokeEvidence {
  forecastId: string;
  originalLocation: string;
  updatedLocation: string;
  sortableUniqueId: string | null;
  tagStateVersion: number;
  tagStateLocation: string;
  listQueryCount: number;
  countQueryCount: number;
  foundInListQuery: boolean;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main(): Promise<void> {
  const baseUrl = process.env.RUNTIME_URL ?? "http://localhost:8080";
  const originalLocation = process.env.SAMPLE_FORECAST_LOCATION ?? "Kyoto";
  const updatedLocation = process.env.SAMPLE_UPDATED_LOCATION ?? "Osaka";
  const forecastId = process.env.SAMPLE_FORECAST_ID ?? randomUUID();

  const client = new SekibanRuntimeClient(baseUrl, { [TAG_GROUP]: PROJECTOR_TAG });

  const created = (await client.finalizeCommand(
    new CreateWeatherForecast(forecastId, originalLocation, 24, "npm TypeScript sample"),
  )) as CommitResponse;
  const updated = (await client.finalizeCommand(
    new UpdateWeatherForecastLocation(forecastId, updatedLocation),
  )) as CommitResponse;

  const tagState = await client.getTagState(TAG_GROUP, forecastId);
  const state = parseState(tagState.stateJson);
  if (state.forecastId !== forecastId || state.location !== updatedLocation) {
    throw new Error(
      `tag-state mismatch: expected ${forecastId}/${updatedLocation}, got ${JSON.stringify(state)}`,
    );
  }

  const waitFor: string | null =
    updated.writtenEvents?.[0]?.sortableUniqueIdValue ??
    created.writtenEvents?.[0]?.sortableUniqueIdValue ??
    null;

  // @sekiban/ts's executeListQuery/executeQuery have no host-side
  // wait-for-sortable-id parameter (unlike the Go SDK's ExecuteListQuery),
  // so this sample polls client-side, mirroring the Rust smoke client.
  let listItems: WeatherForecastState[] = [];
  for (let i = 0; i < 30; i++) {
    const paramsJson = JSON.stringify({
      locationFilter: updatedLocation,
      waitForSortableUniqueId: waitFor ?? "",
    });
    const itemsJson = await client.executeListQuery("GetWeatherForecastListQuery", paramsJson);
    listItems = JSON.parse(itemsJson || "[]") as WeatherForecastState[];
    if (listItems.some((item) => item.forecastId === forecastId && item.location === updatedLocation)) {
      break;
    }
    await sleep(2000);
  }

  const countParamsJson = JSON.stringify({
    locationFilter: updatedLocation,
    waitForSortableUniqueId: waitFor ?? "",
  });
  const countJson = await client.executeQuery("GetWeatherForecastCountQuery", countParamsJson);
  const count = JSON.parse(countJson || "{}") as { count?: number };

  const foundInListQuery = listItems.some(
    (item) => item.forecastId === forecastId && item.location === updatedLocation,
  );
  if (!foundInListQuery) {
    throw new Error(`list-query did not return forecast ${forecastId}; count=${listItems.length}`);
  }

  const evidence: SmokeEvidence = {
    forecastId,
    originalLocation,
    updatedLocation,
    sortableUniqueId: waitFor,
    tagStateVersion: tagState.version,
    tagStateLocation: state.location ?? "",
    listQueryCount: listItems.length,
    countQueryCount: count.count ?? 0,
    foundInListQuery,
  };
  console.log(JSON.stringify(evidence, null, 2));
}

main().catch((err) => {
  console.error(err instanceof Error ? err.message : String(err));
  process.exitCode = 1;
});
