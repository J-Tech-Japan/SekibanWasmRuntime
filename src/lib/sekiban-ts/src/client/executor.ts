import {
  type TagStateResponse,
  type CommandContext,
  type Command,
  tagString,
} from "../domain/types.js";

export class SekibanRuntimeClient {
  private baseUrl: string;
  private tagProjectorMap: Record<string, string>;

  constructor(baseUrl: string, tagProjectorMap: Record<string, string>) {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
    this.tagProjectorMap = tagProjectorMap;
  }

  private async postJSON(path: string, body: unknown): Promise<any> {
    const resp = await fetch(`${this.baseUrl}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (!resp.ok) {
      const errText = await resp.text();
      throw new Error(`${path} failed: ${resp.status} ${resp.statusText} - ${errText}`);
    }
    return await resp.json();
  }

  async getTagState(tagGroup: string, id: string): Promise<TagStateResponse> {
    const projectorName = this.tagProjectorMap[tagGroup];
    if (!projectorName) {
      throw new Error(`no projector mapped for tag group: ${tagGroup}`);
    }
    const tagStateId = `${tagGroup}:${id}:${projectorName}`;

    const resp: any = await this.postJSON("/api/sekiban/serialized/tag-state", {
      tagStateId,
    });

    let stateJson = "{}";
    if (resp.payload && resp.payload !== "") {
      try {
        const decoded = Buffer.from(resp.payload, "base64").toString("utf-8");
        if (decoded.length > 0) stateJson = decoded;
      } catch {
        // ignore decode errors
      }
    }

    const lastSortable = resp.lastSortableUniqueId || resp.lastSortedUniqueId || null;

    return {
      stateJson,
      version: resp.version ?? 0,
      lastSortableUniqueId: lastSortable,
    };
  }

  async executeQuery(
    queryType: string,
    paramsJson: string,
    serviceId?: string | null,
  ): Promise<string> {
    const body: Record<string, any> = {
      queryType,
      queryParamsJson: paramsJson,
    };
    if (serviceId) body.serviceId = serviceId;

    const resp = await this.postJSON("/api/sekiban/serialized/query", body);
    return resp.resultJson ?? "null";
  }

  async executeListQuery(
    queryType: string,
    paramsJson: string,
    serviceId?: string | null,
  ): Promise<string> {
    const body: Record<string, any> = {
      queryType,
      queryParamsJson: paramsJson,
    };
    if (serviceId) body.serviceId = serviceId;

    const resp = await this.postJSON("/api/sekiban/serialized/list-query", body);
    return resp.itemsJson ?? "[]";
  }

  async finalizeCommand(cmd: Command): Promise<any> {
    const cache = new Map<string, TagStateResponse>();

    const ctx: CommandContext = {
      cache,
      getTagState: async (
        tagGroup: string,
        id: string,
      ): Promise<TagStateResponse> => {
        const key = tagString(tagGroup, id);
        const cached = cache.get(key);
        if (cached) {
          return cached;
        }
        const state = await this.getTagState(tagGroup, id);
        cache.set(key, state);
        return state;
      },
    };

    const output = await cmd.handle(ctx);

    // Build event candidates
    const eventCandidates = output.events.map((ev) => {
      const payloadJson = JSON.stringify(ev.payload);
      const payloadBase64 = Buffer.from(payloadJson).toString("base64");
      return {
        eventPayloadName: ev.eventType,
        payload: payloadBase64,
        tags: ev.tags,
      };
    });

    // Build consistency tags from cached state
    const seen = new Set<string>();
    const consistencyTags: { tag: string; lastSortableUniqueId: string }[] = [];
    for (const cTag of output.consistencyTags) {
      if (seen.has(cTag)) continue;
      seen.add(cTag);
      const cached = cache.get(cTag);
      const lastId = cached?.lastSortableUniqueId ?? "";
      consistencyTags.push({
        tag: cTag,
        lastSortableUniqueId: lastId,
      });
    }

    const commitBody = {
      eventCandidates,
      consistencyTags,
    };

    return await this.postJSON("/api/sekiban/serialized/commit", commitBody);
  }
}
