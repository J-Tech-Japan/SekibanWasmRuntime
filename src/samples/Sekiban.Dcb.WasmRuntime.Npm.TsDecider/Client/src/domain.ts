import { z } from "zod";
import {
  command,
  done,
  event,
  projector,
  read,
  readExists,
  reject,
  stateUnion,
  tagFamily,
} from "@sekiban/dcb-domain";

export const weather = tagFamily("weather");
export const weatherTag = (forecastId: string) => weather.of(forecastId);

const weatherStateSchema = z.object({
  forecastId: z.string().optional(),
  location: z.string().optional(),
  temperatureC: z.number().optional(),
  summary: z.string().optional(),
  createdAt: z.string().optional(),
});

export type WeatherState = z.infer<typeof weatherStateSchema>;

const weatherForecastCreated = event("WeatherForecastCreated", z.object({
  forecastId: z.string().min(1),
  location: z.string(),
  temperatureC: z.number(),
  summary: z.string(),
  createdAt: z.string(),
}), {
  tags: (payload) => [weather.of(payload.forecastId)],
});

const weatherForecastLocationUpdated = event("WeatherForecastLocationUpdated", z.object({
  forecastId: z.string().min(1),
  newLocation: z.string(),
  updatedAt: z.string(),
}), {
  tags: (payload) => [weather.of(payload.forecastId)],
});

const weatherStateUnion = stateUnion(weatherStateSchema, {
  initial: { forecastId: "", location: "", temperatureC: 0, summary: "", createdAt: "" },
});

export const weatherForecastProjector = projector({
  id: "WeatherForecastProjector",
  version: 1,
  tag: weather,
  state: weatherStateUnion,
  initialState: { forecastId: "", location: "", temperatureC: 0, summary: "", createdAt: "" },
  events: [weatherForecastCreated, weatherForecastLocationUpdated],
  handlers: {
    WeatherForecastCreated: (state) => state,
    WeatherForecastLocationUpdated: (state) => state,
  },
});

export function fixedNowIso(now: string | number | bigint): string {
  if (typeof now === "string") return now;
  return new Date(Number(now)).toISOString();
}

const createInput = z.object({
  forecastId: z.string().min(1),
  location: z.string(),
  temperatureC: z.number(),
  summary: z.string(),
});

const updateLocationInput = z.object({
  forecastId: z.string().min(1),
  newLocation: z.string(),
});

export const createWeatherForecastCommand = command({
  id: "CreateWeatherForecast",
  input: createInput,
  reads: (input) => readExists(weatherTag(input.forecastId)),
  handle: async (input, context) => {
    if (await context.exists(weatherTag(input.forecastId))) {
      return reject("conflict", `weather forecast ${input.forecastId}`);
    }
    context.append(weatherForecastCreated, weatherForecastCreated.make({
      forecastId: input.forecastId,
      location: input.location,
      temperatureC: input.temperatureC,
      summary: input.summary,
      createdAt: fixedNowIso(context.now()),
    }));
    return done();
  },
});

export const updateWeatherForecastLocationCommand = command({
  id: "UpdateWeatherForecastLocation",
  input: updateLocationInput,
  reads: (input) => read(weatherForecastProjector, weatherTag(input.forecastId)),
  handle: async (input, context) => {
    const state = await context.state(weatherForecastProjector, weatherTag(input.forecastId));
    if (!state.forecastId || state.forecastId.length === 0) {
      return reject("not-found", `weather forecast ${input.forecastId}`);
    }
    context.append(weatherForecastLocationUpdated, weatherForecastLocationUpdated.make({
      forecastId: input.forecastId,
      newLocation: input.newLocation,
      updatedAt: fixedNowIso(context.now()),
    }));
    return done();
  },
});

export const weatherEvents = {
  weatherForecastCreated,
  weatherForecastLocationUpdated,
};
