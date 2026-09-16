// Typed TypeScript smoke client using published @sekiban/dcb-* packages.
import { randomUUID } from "node:crypto";
import { createHttpTransport, createSekibanExecutor } from "@sekiban/dcb-client";
import {
  createWeatherForecastCommand,
  updateWeatherForecastLocationCommand,
  weatherForecastProjector,
  weatherTag,
  type WeatherState,
} from "./domain.js";
import { executeOrThrow, HttpCommandError } from "./executorAdapter.js";

interface WrittenEvent {
  sortableUniqueIdValue?: string;
}

interface CommitResponse {
  writtenEvents?: WrittenEvent[];
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

  const executor = createSekibanExecutor(createHttpTransport({ baseUrl }));

  const created = (await executeOrThrow(executor, createWeatherForecastCommand, {
    forecastId,
    location: originalLocation,
    temperatureC: 24,
    summary: "npm TypeScript sample",
  })).response as CommitResponse;

  const updated = (await executeOrThrow(executor, updateWeatherForecastLocationCommand, {
    forecastId,
    newLocation: updatedLocation,
  })).response as CommitResponse;

  const snapshot = await executor.readState(weatherForecastProjector, weatherTag(forecastId));
  const state = snapshot.state as WeatherState;
  if (state.forecastId !== forecastId || state.location !== updatedLocation) {
    throw new Error(
      `tag-state mismatch: expected ${forecastId}/${updatedLocation}, got ${JSON.stringify(state)}`,
    );
  }

  const waitFor: string | null =
    updated.writtenEvents?.[0]?.sortableUniqueIdValue ??
    created.writtenEvents?.[0]?.sortableUniqueIdValue ??
    null;

  let listItems: WeatherState[] = [];
  for (let i = 0; i < 30; i++) {
    const listResult = await executor.listQuery({
      queryType: "GetWeatherForecastListQuery",
      queryParamsJson: JSON.stringify({
        locationFilter: updatedLocation,
        waitForSortableUniqueId: waitFor ?? "",
      }),
      waitForSortableUniqueId: waitFor ?? undefined,
    });
    listItems = JSON.parse(listResult.itemsJson || "[]") as WeatherState[];
    if (listItems.some((item) => item.forecastId === forecastId && item.location === updatedLocation)) {
      break;
    }
    await sleep(2000);
  }

  const countResult = await executor.query({
    queryType: "GetWeatherForecastCountQuery",
    queryParamsJson: JSON.stringify({
      locationFilter: updatedLocation,
      waitForSortableUniqueId: waitFor ?? "",
    }),
    waitForSortableUniqueId: waitFor ?? undefined,
  });
  const count = JSON.parse(countResult.resultJson || "{}") as { count?: number };

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
    tagStateVersion: snapshot.exists ? 1 : 0,
    tagStateLocation: state.location ?? "",
    listQueryCount: listItems.length,
    countQueryCount: count.count ?? 0,
    foundInListQuery,
  };
  console.log(JSON.stringify(evidence, null, 2));
}

main().catch((err) => {
  if (err instanceof HttpCommandError) {
    console.error(err.message);
  } else {
    console.error(err instanceof Error ? err.message : String(err));
  }
  process.exitCode = 1;
});
