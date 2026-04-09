export class HttpError extends Error {
  constructor(status, message, body = null) {
    super(message);
    this.name = "HttpError";
    this.status = status;
    this.body = body;
  }
}

const projectorByTagGroup = {
  weather: "WeatherForecastProjector",
  Student: "StudentProjector",
  ClassRoom: "ClassRoomProjector",
  User: "UserDirectoryProjector",
  UserAccess: "UserAccessProjector",
  Room: "RoomProjector",
  RoomReservation: "RoomReservationsProjector",
  Reservation: "ReservationProjector",
  ApprovalRequest: "ApprovalRequestProjector",
};

function decodePayload(payloadBase64) {
  if (!payloadBase64) {
    return "{}";
  }

  return Buffer.from(payloadBase64, "base64").toString("utf8") || "{}";
}

function parseJsonOrDefault(text, fallback) {
  try {
    return JSON.parse(text);
  } catch {
    return fallback;
  }
}

export class SekibanRuntimeClient {
  constructor(baseUrl) {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
  }

  async #postJson(path, body) {
    const response = await fetch(`${this.baseUrl}${path}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body),
    });

    const text = await response.text();
    if (!response.ok) {
      const parsed = text ? parseJsonOrDefault(text, null) : null;
      const message =
        parsed?.error ??
        parsed?.message ??
        (text || `${response.status} ${response.statusText}`);
      throw new HttpError(response.status, message, parsed ?? text ?? null);
    }

    return text ? parseJsonOrDefault(text, null) : null;
  }

  async getTagState(tagGroup, tagContent, projectorName = projectorByTagGroup[tagGroup]) {
    if (!projectorName) {
      throw new Error(`Unknown projector for tag group '${tagGroup}'`);
    }

    const tagStateId = `${tagGroup}:${tagContent}:${projectorName}`;
    const result = await this.#postJson("/api/sekiban/serialized/tag-state", { tagStateId });
    const payloadJson = decodePayload(result?.payload);
    return {
      tag: `${tagGroup}:${tagContent}`,
      tagStateId,
      payloadJson,
      payload: parseJsonOrDefault(payloadJson, {}),
      version: result?.version ?? 0,
      lastSortableUniqueId: result?.lastSortedUniqueId ?? result?.lastSortableUniqueId ?? "",
    };
  }

  async executeQuery(queryType, queryParams, waitForSortableUniqueId = null) {
    const result = await this.#postJson("/api/sekiban/serialized/query", {
      queryType,
      queryParamsJson: JSON.stringify(queryParams ?? {}),
      waitForSortableUniqueId,
    });
    return parseJsonOrDefault(result?.resultJson ?? "null", null);
  }

  async executeListQuery(queryType, queryParams, waitForSortableUniqueId = null) {
    const result = await this.#postJson("/api/sekiban/serialized/list-query", {
      queryType,
      queryParamsJson: JSON.stringify(queryParams ?? {}),
      waitForSortableUniqueId,
    });
    return {
      items: parseJsonOrDefault(result?.itemsJson ?? "[]", []),
      totalCount: result?.totalCount ?? null,
      totalPages: result?.totalPages ?? null,
      currentPage: result?.currentPage ?? null,
      pageSize: result?.pageSize ?? null,
    };
  }

  async commitCommandOutput(output, loadedStates = []) {
    const stateMap = new Map();
    for (const state of loadedStates) {
      if (state?.tag) {
        stateMap.set(state.tag, state);
      }
    }

    const events = Array.isArray(output?.events) ? output.events : [];
    if (events.length === 0) {
      return {
        eventId: null,
        sortableUniqueId: null,
        writtenEvents: [],
        tagWriteResults: [],
        duration: null,
      };
    }

    const consistencyTags = Array.from(new Set(output?.consistencyTags ?? [])).map((tag) => ({
      tag,
      lastSortableUniqueId: stateMap.get(tag)?.lastSortableUniqueId ?? "",
    }));

    const result = await this.#postJson("/api/sekiban/serialized/commit", {
      eventCandidates: events.map((event) => ({
        payload: Buffer.from(event.payload ?? "", "utf8").toString("base64"),
        eventPayloadName: event.eventType,
        tags: output?.tags ?? [],
      })),
      consistencyTags,
    });

    const firstEvent = result?.writtenEvents?.[0] ?? null;
    return {
      eventId: firstEvent?.id ?? null,
      sortableUniqueId: firstEvent?.sortableUniqueIdValue ?? null,
      writtenEvents: result?.writtenEvents ?? [],
      tagWriteResults: result?.tagWriteResults ?? [],
      duration: result?.duration ?? null,
    };
  }
}
