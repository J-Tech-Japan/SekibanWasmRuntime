import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createHttpTransport, createSekibanExecutor } from "@sekiban/dcb-client";
import {
  createWeatherForecastCommand,
  updateWeatherForecastLocationCommand,
  weatherForecastProjector,
  weatherTag,
} from "./domain.js";

function encoded(value: unknown): string {
  return Buffer.from(JSON.stringify(value)).toString("base64");
}

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

describe("createHttpTransport V1 wire", () => {
  it("emits version:1 commit envelopes with base64 payloads and consistencyTags", async () => {
    const calls: Array<{ path: string; body: unknown }> = [];
    let committed = false;
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      const body = request.method === "POST" ? await request.clone().json() : undefined;
      calls.push({ path, body });
      if (path.endsWith("/tag-latest-sortable")) {
        return response({ exists: false, lastSortableUniqueId: "" });
      }
      if (path.endsWith("/tag-state")) {
        const tagStateId = String((body as { tagStateId?: string })?.tagStateId ?? "");
        const [, content] = tagStateId.split(":");
        const payload = committed && content === "forecast-1"
          ? { forecastId: "forecast-1", location: "Kyoto" }
          : { status: "empty" };
        return response({
          payload: encoded(payload),
          version: committed ? 1 : 0,
          lastSortedUniqueId: committed ? "suid-1" : "",
          tagGroup: "weather",
          tagContent: content ?? "forecast-1",
          tagProjector: "WeatherForecastProjector",
        });
      }
      if (path.endsWith("/commit")) {
        committed = true;
        return response({ writtenEvents: [{ sortableUniqueIdValue: "suid-committed" }] });
      }
      return response({ code: "not_found" }, 404);
    };

    const executor = createSekibanExecutor(createHttpTransport({
      baseUrl: "https://runtime.test",
      fetch: fetcher,
    }));

    const createResult = await executor.execute(createWeatherForecastCommand, {
      forecastId: "forecast-1",
      location: "Kyoto",
      temperatureC: 24,
      summary: "test",
    });
    assert.equal(createResult.kind, "committed");

    const createCommit = calls.find((call) => call.path.endsWith("/commit"))?.body as Record<string, unknown>;
    assert.ok(createCommit);
    assert.equal(createCommit.version, 1);
    assert.ok(Array.isArray(createCommit.eventCandidates));
    assert.ok(Array.isArray(createCommit.consistencyTags));
    assert.equal((createCommit.consistencyTags as Array<{ tag: string; lastSortableUniqueId: string }>)[0]?.lastSortableUniqueId, "");
    const payload = (createCommit.eventCandidates as Array<{ payload: string }>)[0]?.payload;
    assert.match(payload ?? "", /^[A-Za-z0-9+/]+=*$/);
    assert.doesNotMatch(JSON.stringify(createCommit), /"candidates"/);
    assert.doesNotMatch(JSON.stringify(createCommit), /"consistency"/);

    const updateResult = await executor.execute(updateWeatherForecastLocationCommand, {
      forecastId: "forecast-1",
      newLocation: "Osaka",
    });
    assert.equal(updateResult.kind, "committed");
    const updateCommit = calls.filter((call) => call.path.endsWith("/commit")).at(-1)?.body as {
      consistencyTags: Array<{ tag: string; lastSortableUniqueId: string }>;
    };
    assert.deepEqual(updateCommit.consistencyTags, [
      { tag: "weather:forecast-1", lastSortableUniqueId: "suid-1" },
    ]);
  });

  it("fails closed on missing lastSortedUniqueId without committing", async () => {
    let commitCalls = 0;
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      if (path.endsWith("/tag-latest-sortable")) {
        return response({ exists: true, lastSortableUniqueId: "suid-head" });
      }
      if (path.endsWith("/tag-state")) {
        return response({
          payload: encoded({ forecastId: "forecast-1", location: "Kyoto" }),
          version: 1,
          tagGroup: "weather",
          tagContent: "forecast-1",
          tagProjector: "WeatherForecastProjector",
        });
      }
      if (path.endsWith("/commit")) {
        commitCalls += 1;
        return response({});
      }
      return response({ code: "not_found" }, 404);
    };

    const executor = createSekibanExecutor(createHttpTransport({
      baseUrl: "https://runtime.test",
      fetch: fetcher,
    }));
    const result = await executor.execute(updateWeatherForecastLocationCommand, {
      forecastId: "forecast-1",
      newLocation: "Osaka",
    });
    assert.equal(result.kind, "invalid");
    assert.equal(result.code, "invalid_read_snapshot");
    assert.equal(commitCalls, 0);
  });

  it("fails closed on misspelled lastSortableUniqueId without committing", async () => {
    let commitCalls = 0;
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      if (path.endsWith("/tag-latest-sortable")) {
        return response({ exists: true, lastSortableUniqueId: "suid-head" });
      }
      if (path.endsWith("/tag-state")) {
        return response({
          payload: encoded({ forecastId: "forecast-1", location: "Kyoto" }),
          version: 1,
          lastSortableUniqueId: "suid-head",
          tagGroup: "weather",
          tagContent: "forecast-1",
          tagProjector: "WeatherForecastProjector",
        });
      }
      if (path.endsWith("/commit")) {
        commitCalls += 1;
        return response({});
      }
      return response({ code: "not_found" }, 404);
    };

    const executor = createSekibanExecutor(createHttpTransport({
      baseUrl: "https://runtime.test",
      fetch: fetcher,
    }));
    const result = await executor.execute(updateWeatherForecastLocationCommand, {
      forecastId: "forecast-1",
      newLocation: "Osaka",
    });
    assert.equal(result.kind, "invalid");
    assert.equal(result.code, "invalid_read_snapshot");
    assert.equal(commitCalls, 0);
  });

  it("maps explicit non-existence to assert-empty consistency entry", async () => {
    const calls: Array<{ path: string; body: unknown }> = [];
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      const body = request.method === "POST" ? await request.clone().json() : undefined;
      calls.push({ path, body });
      if (path.endsWith("/tag-latest-sortable")) {
        return response({ exists: false, lastSortableUniqueId: "" });
      }
      if (path.endsWith("/tag-state")) {
        return response({
          payload: encoded({ status: "empty" }),
          version: 0,
          lastSortedUniqueId: "",
          tagGroup: "weather",
          tagContent: "forecast-2",
          tagProjector: "WeatherForecastProjector",
        });
      }
      if (path.endsWith("/commit")) {
        return response({ writtenEvents: [] });
      }
      return response({ code: "not_found" }, 404);
    };

    const executor = createSekibanExecutor(createHttpTransport({
      baseUrl: "https://runtime.test",
      fetch: fetcher,
    }));
    const result = await executor.execute(createWeatherForecastCommand, {
      forecastId: "forecast-2",
      location: "Tokyo",
      temperatureC: 20,
      summary: "empty-head",
    });
    assert.equal(result.kind, "committed");
    const commit = calls.find((call) => call.path.endsWith("/commit"))?.body as {
      consistencyTags: Array<{ lastSortableUniqueId: string }>;
    };
    assert.equal(commit.consistencyTags[0]?.lastSortableUniqueId, "");
  });
});
